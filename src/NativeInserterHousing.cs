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
            // Native vertex colors encode endpoint, moving parts and facility-only casing.
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
            // Static source-endpoint tags select the closed shutter at its authored transform.
            for (int index3 = 0; index3 < selected.Count; index3++)
            {
                int n = selected[index3];
                var p = vertices[n];
                var normal = normals[n];
                var tangent = tangents[n];
                // The native destination endpoint faces into the facility; expose a canonical outward frame.
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
            var material = new Material(desc.lodMaterials[0][0])
            {
                name = "FluidPipes.NativeInserterHousing" + grade,
            };
            material.DisableKeyword("_ENABLE_VFINST");
            material.SetFloat("_ToggleVerta", 0);
            return cache[key] = new NativeInserterHousing { Mesh = mesh, Material = material };
        }

        public static void Dispose()
        {
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
