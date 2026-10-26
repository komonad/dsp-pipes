using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

internal static class PipeIconRender
{
    private static Mesh Lathe(string name, Vector2[] profile)
    {
        const int sides = 64;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        for (int portIndex = 0; portIndex < profile.Length; portIndex++)
        {
            var a = profile[portIndex];
            var b = profile[(portIndex + 1) % profile.Length];
            var edge = b - a;
            for (int index = 0; index < sides; index++)
            {
                float t = index * 2 * Mathf.PI / sides;
                float u = (index + 1) * 2 * Mathf.PI / sides;
                var r0 = new Vector3(Mathf.Cos(t), Mathf.Sin(t), 0);
                var r1 = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0);
                int first = vertices.Count;
                vertices.Add(r0 * a.y + Vector3.forward * a.x);
                vertices.Add(r1 * a.y + Vector3.forward * a.x);
                vertices.Add(r1 * b.y + Vector3.forward * b.x);
                vertices.Add(r0 * b.y + Vector3.forward * b.x);
                var n0 = (r0 * edge.x - Vector3.forward * edge.y).normalized;
                var n1 = (r1 * edge.x - Vector3.forward * edge.y).normalized;
                normals.Add(n0);
                normals.Add(n1);
                normals.Add(n1);
                normals.Add(n0);
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
                triangles.Add(first);
                triangles.Add(first + 2);
                triangles.Add(first + 3);
            }
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    public static void Render(string directory, bool valve = false, bool copper = false)
    {
        const int size = 640;
        const int layer = 30;
        var objects = new List<UnityEngine.Object>();
        var previous = RenderTexture.active;
        var ambient = RenderSettings.ambientLight;
        var mode = RenderSettings.ambientMode;
        float intensity = RenderSettings.ambientIntensity;
        var target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 8,
        };
        objects.Add(target);
        try
        {
            var root = new GameObject("PipeIcon.Orthographic");
            objects.Add(root);
            Func<string, Material, Mesh, GameObject> part = (name, material, mesh) =>
            {
                objects.Add(mesh);
                var gameObject = new GameObject(name);
                gameObject.transform.SetParent(root.transform, false);
                gameObject.layer = layer;
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var lighting = (MaterialPropertyBlock)
                    AccessTools
                        .Method(typeof(FluidPipes.PipeVisuals), "SteelLighting")
                        .Invoke(null, new object[] { Vector3.up });
                renderer.SetPropertyBlock(lighting);
                if (material.name == "FluidPipes.IconGlass")
                {
                    var reflection = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(reflection);
                    reflection.SetVector("unity_SpecCube0_HDR", new Vector4(2.6f, 1, 0, 0));
                    reflection.SetVector("unity_SpecCube1_HDR", new Vector4(2.6f, 1, 0, 0));
                    renderer.SetPropertyBlock(reflection);
                }
                return gameObject;
            };
            var steel = new Material(Shader.Find("Standard"))
            {
                color = new Color(0.72f, 0.76f, 0.80f),
            };
            steel.SetFloat("_Metallic", 0.75f);
            steel.SetFloat("_Glossiness", 0.62f);
            if (copper)
            {
                steel.color = new Color(0.72f, 0.36f, 0.16f);
                steel.SetFloat("_Metallic", 0.95f);
                steel.SetFloat("_Glossiness", 0.75f);
            }
            objects.Add(steel);
            var dark = new Material(steel) { color = new Color(0.19f, 0.23f, 0.26f) };
            dark.SetFloat("_Glossiness", 0.45f);
            objects.Add(dark);
            var glass = new Material(Shader.Find("Standard"))
            {
                name = "FluidPipes.IconGlass",
                color = new Color(0.20f, 0.57f, 0.72f, 0.38f),
            };
            glass.SetFloat("_Metallic", 0.06f);
            glass.SetFloat("_Glossiness", 0.92f);
            glass.SetFloat("_Mode", 3);
            glass.SetOverrideTag("RenderType", "Transparent");
            glass.SetInt("_SrcBlend", (int)BlendMode.One);
            glass.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            glass.SetInt("_ZWrite", 0);
            glass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            glass.renderQueue = 3000;
            if (copper)
            {
                glass.name = "FluidPipes.IconCopper";
                glass.color = steel.color;
                glass.SetFloat("_Metallic", 0.95f);
                glass.SetFloat("_Glossiness", 0.75f);
                glass.SetFloat("_Mode", 0);
                glass.SetOverrideTag("RenderType", "Opaque");
                glass.SetInt("_DstBlend", (int)BlendMode.Zero);
                glass.SetInt("_ZWrite", 1);
                glass.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                glass.renderQueue = 2000;
            }
            objects.Add(glass);
            var accent = new Material(steel) { color = new Color(0.82f, 0.51f, 0.17f) };
            accent.SetFloat("_Metallic", 0.35f);
            objects.Add(accent);
            if (!valve)
                part(
                    "Glass",
                    glass,
                    Lathe(
                        "Icon.Glass",
                        new[]
                        {
                            new Vector2(-1.0f, 0.475f),
                            new Vector2(1.0f, 0.475f),
                            new Vector2(1.0f, 0.42f),
                            new Vector2(-1.0f, 0.42f),
                        }
                    )
                );
            else
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    float a = side * 0.40f;
                    float b = side * 1.0f;
                    if (a > b)
                    {
                        float swap = a;
                        a = b;
                        b = swap;
                    }
                    part(
                        "GlassPort",
                        glass,
                        Lathe(
                            "Icon.ValveGlass",
                            new[]
                            {
                                new Vector2(a, 0.475f),
                                new Vector2(b, 0.475f),
                                new Vector2(b, 0.42f),
                                new Vector2(a, 0.42f),
                            }
                        )
                    );
                }
                part(
                    "ValveBody",
                    steel,
                    Lathe(
                        "Icon.ValveBody",
                        new[]
                        {
                            new Vector2(-0.5f, 0.47f),
                            new Vector2(-0.44f, 0.58f),
                            new Vector2(-0.3f, 0.66f),
                            new Vector2(0.3f, 0.66f),
                            new Vector2(0.44f, 0.58f),
                            new Vector2(0.5f, 0.47f),
                            new Vector2(0.5f, 0.35f),
                            new Vector2(-0.5f, 0.35f),
                        }
                    )
                );
                var stem = part(
                    "Stem",
                    dark,
                    Lathe(
                        "Icon.Stem",
                        new[]
                        {
                            new Vector2(-0.25f, 0.12f),
                            new Vector2(0.25f, 0.12f),
                            new Vector2(0.25f, 0),
                            new Vector2(-0.25f, 0),
                        }
                    )
                );
                stem.transform.localRotation = Quaternion.FromToRotation(
                    Vector3.forward,
                    Vector3.up
                );
                stem.transform.localPosition = Vector3.up * 0.87f;
                var neck = part(
                    "Bonnet",
                    steel,
                    Lathe(
                        "Icon.Bonnet",
                        new[]
                        {
                            new Vector2(-0.10f, 0.23f),
                            new Vector2(0.10f, 0.23f),
                            new Vector2(0.14f, 0.16f),
                            new Vector2(-0.10f, 0.16f),
                        }
                    )
                );
                neck.transform.localRotation = Quaternion.FromToRotation(
                    Vector3.forward,
                    Vector3.up
                );
                neck.transform.localPosition = Vector3.up * 0.67f;
                var wheel = part(
                    "Handwheel",
                    accent,
                    Lathe(
                        "Icon.Handwheel",
                        new[]
                        {
                            new Vector2(-0.055f, 0.44f),
                            new Vector2(0.055f, 0.44f),
                            new Vector2(0.055f, 0.35f),
                            new Vector2(-0.055f, 0.35f),
                        }
                    )
                );
                wheel.transform.localRotation = Quaternion.FromToRotation(
                    Vector3.forward,
                    Vector3.up
                );
                wheel.transform.localPosition = Vector3.up * 1.15f;
                for (int index = 0; index < 3; index++)
                {
                    float angle = index * 120 * Mathf.Deg2Rad;
                    var spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    spoke.name = "Wheel.Spoke";
                    spoke.layer = layer;
                    spoke.transform.SetParent(root.transform, false);
                    spoke.transform.localPosition = new Vector3(
                        Mathf.Cos(angle) * 0.20f,
                        1.15f,
                        Mathf.Sin(angle) * 0.20f
                    );
                    spoke.transform.localScale = new Vector3(0.42f, 0.08f, 0.065f);
                    spoke.transform.localRotation = Quaternion.Euler(0, -index * 120, 0);
                    spoke.GetComponent<Renderer>().sharedMaterial = accent;
                }
            }
            var collar = new[]
            {
                new Vector2(-0.22f, 0.52f),
                new Vector2(-0.18f, 0.60f),
                new Vector2(0.12f, 0.60f),
                new Vector2(0.18f, 0.54f),
                new Vector2(0.22f, 0.54f),
                new Vector2(0.22f, 0.42f),
                new Vector2(-0.22f, 0.42f),
            };
            foreach (float z in new[] { -1.02f, 1.02f })
            {
                part("Collar", steel, Lathe("Icon.Collar", collar)).transform.localPosition =
                    Vector3.forward * z;
                part(
                    "Bore",
                    dark,
                    Lathe(
                        "Icon.Bore",
                        new[]
                        {
                            new Vector2(z - 0.22f, 0.419f),
                            new Vector2(z + 0.22f, 0.419f),
                            new Vector2(z + 0.22f, 0.415f),
                            new Vector2(z - 0.22f, 0.415f),
                        }
                    )
                );
                part(
                    "Groove",
                    dark,
                    Lathe(
                        "Icon.Groove",
                        new[]
                        {
                            new Vector2(z - 0.025f, 0.604f),
                            new Vector2(z + 0.025f, 0.604f),
                            new Vector2(z + 0.025f, 0.59f),
                            new Vector2(z - 0.025f, 0.59f),
                        }
                    )
                );
                var tab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tab.name = "Collar.Marker";
                tab.transform.SetParent(root.transform, false);
                tab.layer = layer;
                tab.transform.localPosition = new Vector3(0, 0.602f, z + 0.075f);
                tab.transform.localScale = new Vector3(0.17f, 0.018f, 0.09f);
                tab.GetComponent<Renderer>().sharedMaterial = accent;
            }
            var cameraObject = new GameObject("PipeIcon.Camera");
            objects.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.transform.position = new Vector3(6, 7, -5);
            camera.transform.LookAt(Vector3.zero);
            camera.orthographicSize = 1.85f;
            camera.aspect = 1;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << layer;
            camera.targetTexture = target;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            foreach (var vertex in filter.sharedMesh.vertices)
            {
                var position2 = camera.transform.InverseTransformPoint(
                    filter.transform.TransformPoint(vertex)
                );
                minX = Mathf.Min(minX, position2.x);
                maxX = Mathf.Max(maxX, position2.x);
                minY = Mathf.Min(minY, position2.y);
                maxY = Mathf.Max(maxY, position2.y);
            }
            camera.orthographicSize = Mathf.Max(maxX - minX, maxY - minY) / 1.8f;
            camera.transform.position +=
                camera.transform.right * ((minX + maxX) * 0.5f)
                + camera.transform.up * ((minY + maxY) * 0.5f);
            var lightObject = new GameObject("PipeIcon.Key");
            objects.Add(lightObject);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.cullingMask = 1 << layer;
            light.transform.rotation = Quaternion.Euler(35, -35, 0);
            var fillObject = new GameObject("PipeIcon.Fill");
            objects.Add(fillObject);
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.color = new Color(0.8f, 0.9f, 1);
            fill.cullingMask = 1 << layer;
            fill.transform.rotation = Quaternion.Euler(25, 140, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.48f, 0.52f, 0.58f);
            RenderSettings.ambientIntensity = 1;
            var axis = camera.transform.InverseTransformDirection(Vector3.forward);
            var minor = new Vector2(axis.x, axis.y).normalized;
            float xx = 0;
            float xy = 0;
            float yy = 0;
            for (int index2 = 0; index2 < 64; index2++)
            {
                float angle = index2 * 2 * Mathf.PI / 64;
                var position3 = camera.transform.InverseTransformDirection(
                    new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0)
                );
                xx += position3.x * position3.x;
                xy += position3.x * position3.y;
                yy += position3.y * position3.y;
            }
            var product = new Vector2(xx * minor.x + xy * minor.y, xy * minor.x + yy * minor.y);
            if (Mathf.Abs(minor.x * product.y - minor.y * product.x) > 0.001f)
                throw new InvalidOperationException(
                    "Projected cross section does not align with its cylinder axis."
                );
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false);
            objects.Add(image);
            var pixels = new Color[size * size];
            var backgroundPixels = new Color[size * size];
            camera.backgroundColor = Color.black;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            image.Apply();
            pixels = image.GetPixels();
            camera.backgroundColor = Color.white;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            image.Apply();
            backgroundPixels = image.GetPixels();
            // Black/white matting recovers straight-alpha coverage independently of shader alpha writes.
            for (int index3 = 0; index3 < pixels.Length; index3++)
            {
                var black = pixels[index3].linear;
                var white = backgroundPixels[index3].linear;
                float alpha = Mathf.Clamp01(
                    1 - (white.r - black.r + white.g - black.g + white.b - black.b) / 3
                );
                if (alpha < 0.004f)
                {
                    pixels[index3] = Color.clear;
                    continue;
                }
                var color = new Color(black.r / alpha, black.g / alpha, black.b / alpha, 1).gamma;
                color.a = alpha;
                pixels[index3] = color;
            }
            image.SetPixels(pixels);
            image.Apply();
            var middle = image.GetPixel(size / 2, size / 2);
            if (!valve && !copper && (middle.a < 0.25f || middle.a > 0.95f || middle.b < 0.25f))
                throw new InvalidOperationException(
                    "Icon glass body does not retain translucent blue coverage: " + middle
                );
            int minPixel = size;
            int maxPixel = 0;
            int minRow = size;
            int maxRow = 0;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                if (pixels[y * size + x].a > 0.1f)
                {
                    minPixel = Math.Min(minPixel, x);
                    maxPixel = Math.Max(maxPixel, x);
                    minRow = Math.Min(minRow, y);
                    maxRow = Math.Max(maxRow, y);
                }
            if (Math.Max(maxPixel - minPixel, maxRow - minRow) < size * 0.85f)
                throw new InvalidOperationException(
                    "Icon subject does not fill its inventory tile."
                );
            File.WriteAllBytes(
                Path.Combine(
                    directory,
                    copper ? "pipe-copper-icon-orthographic.png"
                        : valve ? "pipe-valve-icon-orthographic.png"
                        : "pipe-icon-orthographic.png"
                ),
                image.EncodeToPNG()
            );
        }
        finally
        {
            RenderTexture.active = previous;
            RenderSettings.ambientLight = ambient;
            RenderSettings.ambientMode = mode;
            RenderSettings.ambientIntensity = intensity;
            target.Release();
            for (int index4 = objects.Count - 1; index4 >= 0; index4--)
                UnityEngine.Object.DestroyImmediate(objects[index4]);
        }
    }
}
