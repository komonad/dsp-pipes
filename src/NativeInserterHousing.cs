using System;
using System.Collections.Generic;
using UnityEngine;

namespace FluidPipes
{
    internal sealed class NativeInserterHousing
    {
        public const float PortDepth = 0.87f;
        public Mesh Mesh;
        public Material Material;
        private static readonly NativeInserterHousing[] cache = new NativeInserterHousing[10];
        private static readonly Texture2D[] paintedTextures = new Texture2D[5];

        private static Texture2D PaintTexture(int grade, Material native)
        {
            if (paintedTextures[grade] != null)
            {
                return paintedTextures[grade];
            }
            var source = native.GetTexture("_MainTex");
            var target = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            );
            var previous = RenderTexture.active;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                var pixels = texture.GetPixels();
                Color tint = native.GetColor("_Color").linear;
                float multiplier = native.GetFloat("_AlbedoMultiplier");
                for (int index = 0; index < pixels.Length; index++)
                {
                    // 原生分拣器反照率的 alpha 通道编码等级涂装遮罩。
                    float mask = Mathf.Clamp01((pixels[index].a - 0.1f) * 1.25f);
                    Color albedo =
                        pixels[index].linear * Color.Lerp(Color.white, tint, mask) * multiplier;
                    pixels[index] = albedo.gamma;
                    pixels[index].a = 1;
                }
                texture.SetPixels(pixels);
                texture.name = "FluidPipes.InserterHousingPaint" + grade;
                texture.Apply(true, true);
                paintedTextures[grade] = texture;
                return texture;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        public static NativeInserterHousing Get(int grade, bool machine = true)
        {
            grade = Math.Max(1, Math.Min(4, grade));
            int key = grade + (machine ? 0 : 5);
            if (cache[key] != null)
            {
                return cache[key];
            }

            var desc = LDB.items.Select(2010 + grade).prefabDesc;
            var source = desc.lodMeshes[0];
            var colors = source.colors32;
            var vertices = source.vertices;
            var normals = source.normals;
            var tangents = source.tangents;
            var uv = source.uv;
            var indices = new List<int>();
            var selected = new List<int>();
            var remap = new int[vertices.Length];
            for (int index = 0; index < remap.Length; index++)
            {
                remap[index] = -1;
            }

            var triangles = source.GetTriangles(0);
            // 原生顶点颜色编码端点、运动部件与仅设施外壳。
            Func<int, bool> fixedPart = i =>
                colors[i].g == 0
                && (
                    machine
                        ? (colors[i].b == 0 || colors[i].b == 3)
                            && (colors[i].r == 0 || colors[i].r == 2)
                        : colors[i].b == 0 && colors[i].r == 1
                );
            for (int index2 = 0; index2 < triangles.Length; index2 += 3)
            {
                if (
                    !fixedPart(triangles[index2])
                    || !fixedPart(triangles[index2 + 1])
                    || !fixedPart(triangles[index2 + 2])
                )
                {
                    continue;
                }

                for (int otherIndex = 0; otherIndex < 3; otherIndex++)
                {
                    int original = triangles[index2 + otherIndex];
                    if (remap[original] < 0)
                    {
                        remap[original] = selected.Count;
                        selected.Add(original);
                    }
                    indices.Add(remap[original]);
                }
            }
            if (selected.Count == 0)
            {
                throw new InvalidOperationException(
                    "Native inserter casing has no fixed triangles."
                );
            }

            var points = new Vector3[selected.Count];
            var directions = new Vector3[selected.Count];
            var frames = new Vector4[selected.Count];
            var texcoords = new Vector2[selected.Count];
            var tags = new Color32[selected.Count];
            // 静态源端点标记在既定变换位置选用闭合挡板。
            for (int index3 = 0; index3 < selected.Count; index3++)
            {
                int n = selected[index3];
                var p = vertices[n];
                var normal = normals[n];
                var tangent = tangents[n];
                // 原生目标端点朝向设施内部；对外暴露规范的外向坐标系。
                points[index3] = machine ? new Vector3(-p.x, p.y, -p.z) : p;
                directions[index3] = machine ? new Vector3(-normal.x, normal.y, -normal.z) : normal;
                frames[index3] = machine
                    ? new Vector4(-tangent.x, tangent.y, -tangent.z, tangent.w)
                    : tangent;
                texcoords[index3] = uv[n];
                tags[index3] = new Color32(1, 0, 0, 255);
            }
            var mesh = new Mesh
            {
                name = "FluidPipes.NativeInserterHousing" + grade,
                vertices = points,
                normals = directions,
                tangents = frames,
                uv = texcoords,
                colors32 = tags,
            };
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            var nativeMaterial = desc.lodMaterials[0][0];
            var material = new Material(Shader.Find("Standard"))
            {
                name = "FluidPipes.NativeInserterHousing" + grade,
                color = Color.white,
            };
            material.SetTexture("_MainTex", PaintTexture(grade, nativeMaterial));
            material.SetTexture("_MetallicGlossMap", nativeMaterial.GetTexture("_MS_Tex"));
            material.EnableKeyword("_METALLICGLOSSMAP");
            material.SetFloat("_Metallic", 1);
            material.SetFloat("_GlossMapScale", 0.5f);
            material.SetTexture("_EmissionMap", paintedTextures[grade]);
            material.SetColor("_EmissionColor", new Color(0.20f, 0.21f, 0.22f));
            material.EnableKeyword("_EMISSION");
            return cache[key] = new NativeInserterHousing { Mesh = mesh, Material = material };
        }

        public static void Dispose()
        {
            for (int grade = 1; grade < paintedTextures.Length; grade++)
            {
                if (paintedTextures[grade] != null)
                {
                    UnityEngine.Object.Destroy(paintedTextures[grade]);
                    paintedTextures[grade] = null;
                }
            }
            for (int index = 0; index < cache.Length; index++)
            {
                if (cache[index] != null)
                {
                    UnityEngine.Object.Destroy(cache[index].Mesh);
                    UnityEngine.Object.Destroy(cache[index].Material);
                    cache[index] = null;
                }
            }
        }
    }
}
