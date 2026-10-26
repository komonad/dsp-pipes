using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluidPipes;
using UnityEngine;

public sealed partial class RuntimeChecks
{
    private void InserterAppearanceGeometry()
    {
        for (int grade = 1; grade <= 4; grade++)
        {
            var original = LDB.items.Select(2010 + grade).prefabDesc.lodMaterials[0][0];
            var accent = FluidInserterVisuals.GradeMaterial(grade);
            Check(
                accent.color == original.GetColor("_Color") && accent.GetFloat("_Metallic") == 0,
                "inserter grade accent retains native color as dielectric paint " + grade
            );
            var arrow = FluidInserterVisuals.ArrowMaterial(grade);
            var originalArrow = LDB.items.Select(2010 + grade).prefabDesc.lodMaterials[0][1];
            Check(
                arrow.color == originalArrow.GetColor("_TintColor"),
                "direction marker uses native arrow tint " + grade
            );
        }
        var surfaces = FluidInserterVisuals.SurfaceMaterials;
        Check(
            surfaces.Length == FluidInserterMesh.MaterialCount
                && surfaces[4].GetFloat("_Metallic") == 0
                && surfaces[4].color.r > 0.7f,
            "inserter supports use a separate light dielectric casing material"
        );
        CheckArrowRendering();
        foreach (bool machine in new[] { true, false })
        {
            foreach (float elevation in new[] { 0f, 1.3333333f })
            {
                var source = new FluidInserterMesh.Socket
                {
                    Machine = machine,
                    Belt = !machine,
                    Anchor = Vector3.zero,
                    Center = new Vector3(0, machine ? 0.5f : 0.6f, machine ? 0.87f : 0.025f),
                    Normal = Vector3.forward,
                    Axis = Vector3.up,
                };
                source.Position = source.Center + Vector3.forward * 0.4f + Vector3.up * 0.2f;
                var destination = new FluidInserterMesh.Socket
                {
                    Position = new Vector3(0, source.Position.y + elevation, 4),
                };
                var mesh = FluidInserterMesh.Create(source, destination, Vector3.up);
                try
                {
                    var vertices = mesh.vertices;
                    var supportIndices = mesh.GetTriangles(4);
                    Check(
                        supportIndices.Length > 0,
                        "machine and belt endpoints have solid supports"
                    );
                    var components = MeshComponents(vertices, supportIndices);
                    float top =
                        Math.Max(source.Position.y, destination.Position.y)
                        + FluidInserterMesh.Clearance;
                    float bend = Math.Min(
                        0.16f,
                        (destination.Position.z - source.Position.z) * 0.24f
                    );
                    var seat = new Vector3(0, top, source.Position.z + bend);
                    var root = FluidInserterMesh.SupportRoot(source);
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        var foot = root + Vector3.right * (sign * 0.145f);
                        var shoulder = seat + Vector3.right * (sign * 0.145f);
                        Check(
                            components.Any(component =>
                                component.Any(index =>
                                    (vertices[index] - foot).sqrMagnitude < 0.004f
                                )
                                && component.Any(index =>
                                    (vertices[index] - shoulder).sqrMagnitude < 0.004f
                                )
                            ),
                            "support strut spans the native frame and upper tube collar"
                        );
                    }
                    Check(
                        mesh.bounds.max.y <= top + 0.161f,
                        "solid support remains within the compact second-tier clearance envelope"
                    );
                    Check(
                        components.Any(component =>
                            component.All(index => Math.Abs(vertices[index].z - seat.z) <= 0.061f)
                            && component.Any(index => vertices[index].x > seat.x + 0.155f)
                            && component.Any(index => vertices[index].x < seat.x - 0.155f)
                        ),
                        "support collar surrounds the upper tube at the strut endpoint"
                    );
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
        }
    }

    private void CheckArrowRendering()
    {
        var generated = FluidInserterMesh.Create(
            new FluidInserterMesh.Socket { Position = Vector3.zero },
            new FluidInserterMesh.Socket { Position = Vector3.forward * 3 },
            Vector3.up
        );
        int[] triangles = generated.GetTriangles(FluidInserterMesh.ArrowMaterialIndex);
        var arrowMesh = new Mesh { vertices = generated.vertices, normals = generated.normals };
        arrowMesh.SetTriangles(triangles, 0);
        arrowMesh.RecalculateBounds();
        var arrowObject = new GameObject("InserterArrowRenderCheck") { layer = 30 };
        arrowObject.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
        var renderer = arrowObject.AddComponent<MeshRenderer>();
        var cameraObject = new GameObject("InserterArrowCheckCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 0.32f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << 30;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 3;
        camera.transform.position = arrowMesh.bounds.center + Vector3.up;
        camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        var target = RenderTexture.GetTemporary(64, 64, 24);
        var pixels = new Texture2D(64, 64, TextureFormat.RGB24, false);
        var palette = new Texture2D(256, 128, TextureFormat.RGB24, false);
        var lighting = new MaterialPropertyBlock();
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            for (int grade = 1; grade <= 4; grade++)
            {
                renderer.sharedMaterial = FluidInserterVisuals.ArrowMaterial(grade);
                float unpoweredBrightness = 0;
                for (int powerState = 0; powerState <= 1; powerState++)
                {
                    lighting.SetColor("_Color", FluidInserterVisuals.ArrowColor(grade, powerState));
                    renderer.SetPropertyBlock(lighting);
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                    pixels.Apply();
                    Color[] rendered = pixels.GetPixels();
                    Color[] marker = rendered
                        .Where(color => color.maxColorComponent > 0.1f)
                        .ToArray();
                    float brightness =
                        marker.Length == 0 ? 0 : marker.Average(color => color.maxColorComponent);
                    Check(
                        marker.Length > 100 && brightness > 0.3f,
                        "grade "
                            + grade
                            + " arrow is visible with power="
                            + powerState
                            + " brightness="
                            + brightness.ToString("F3")
                    );
                    if (powerState == 0)
                    {
                        unpoweredBrightness = brightness;
                    }
                    else
                    {
                        Check(
                            brightness > unpoweredBrightness * 1.2f,
                            "grade " + grade + " arrow lights up with power"
                        );
                    }
                    palette.SetPixels((grade - 1) * 64, powerState * 64, 64, 64, rendered);
                }
            }
            palette.Apply();
            File.WriteAllBytes(
                Path.Combine(
                    Path.GetDirectoryName(Info.Location),
                    "pipe-inserter-arrow-palette.png"
                ),
                palette.EncodeToPNG()
            );
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(arrowObject);
            UnityEngine.Object.DestroyImmediate(generated);
            UnityEngine.Object.DestroyImmediate(arrowMesh);
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(palette);
        }
    }

    private static List<List<int>> MeshComponents(Vector3[] vertices, int[] triangles)
    {
        var adjacency = new Dictionary<int, HashSet<int>>();
        var coincident = new Dictionary<Vector3, int>();
        var representative = new int[vertices.Length];
        foreach (int index in triangles)
        {
            if (!coincident.TryGetValue(vertices[index], out int existing))
            {
                coincident.Add(vertices[index], existing = index);
            }
            representative[index] = existing;
            if (!adjacency.ContainsKey(existing))
            {
                adjacency.Add(existing, new HashSet<int>());
            }
        }
        for (int index = 0; index < triangles.Length; index += 3)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                int first = representative[triangles[index + corner]];
                int second = representative[triangles[index + (corner + 1) % 3]];
                adjacency[first].Add(second);
                adjacency[second].Add(first);
            }
        }
        var visited = new HashSet<int>();
        var result = new List<List<int>>();
        foreach (int vertex in adjacency.Keys)
        {
            if (!visited.Add(vertex))
            {
                continue;
            }
            var component = new List<int>();
            var pending = new Stack<int>();
            pending.Push(vertex);
            while (pending.Count > 0)
            {
                int current = pending.Pop();
                component.Add(current);
                foreach (int neighbor in adjacency[current])
                {
                    if (visited.Add(neighbor))
                    {
                        pending.Push(neighbor);
                    }
                }
            }
            result.Add(component);
        }
        return result;
    }
}
