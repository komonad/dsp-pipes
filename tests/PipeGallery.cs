using System;
using System.Collections.Generic;
using System.IO;
using FluidPipes;
using UnityEngine;

public static class PipeGallery
{
    private sealed class Shape
    {
        public string Name;
        public string Title;
        public Vector3[] Points;
        public int[] Edges;
        public Mesh Mesh;
        public Mesh Liquid;
        public int Item;
    }

    public static void Create(Func<int, int, bool, PlanetFactory> fixture, string output)
    {
        var shapes = new[]
        {
            new Shape
            {
                Name = "01-straight",
                Title = "01  STRAIGHT CONNECTION",
                Points = new[]
                {
                    V(-2.4f, 0, 0),
                    V(-1.2f, 0, 0),
                    V(0, 0, 0),
                    V(1.2f, 0, 0),
                    V(2.4f, 0, 0),
                },
                Edges = new[] { 1, 2, 2, 3, 3, 4, 4, 5 },
            },
            new Shape
            {
                Name = "02-elbow",
                Title = "02  SMOOTH ELBOW",
                Points = new[]
                {
                    V(-2.4f, 0, 0),
                    V(-1.2f, 0, 0),
                    V(0, 0, 0),
                    V(0, 0, 1.2f),
                    V(0, 0, 2.4f),
                },
                Edges = new[] { 1, 2, 2, 3, 3, 4, 4, 5 },
            },
            new Shape
            {
                Name = "03-vertical-tee",
                Title = "03  VERTICAL INLET",
                Points = new[]
                {
                    V(0, 0, 0),
                    V(-1.2f, 0, 0),
                    V(-2.4f, 0, 0),
                    V(1.2f, 0, 0),
                    V(2.4f, 0, 0),
                    V(0, 1.2f, 0),
                    V(0, 2.4f, 0),
                },
                Edges = new[] { 1, 2, 2, 3, 1, 4, 4, 5, 1, 6, 6, 7 },
            },
            Multi(),
            new Shape
            {
                Name = "05-riser",
                Title = "05  VERTICAL RISER",
                Points = new[]
                {
                    V(-2.4f, 0, 0),
                    V(-1.2f, 0, 0),
                    V(0, 0, 0),
                    V(0, 1.2f, 0),
                    V(0, 2.4f, 0),
                    V(1.2f, 2.4f, 0),
                    V(2.4f, 2.4f, 0),
                },
                Edges = new[] { 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7 },
            },
            new Shape
            {
                Name = "06-network",
                Title = "06  COMBINED NETWORK",
                Points = new[]
                {
                    V(-5, 0, 0),
                    V(-3.8f, 0, 0),
                    V(-2.6f, 0, 0),
                    V(-1.4f, 0, 0),
                    V(-0.2f, 0, 0),
                    V(1, 0, 0),
                    V(1, 0, 1.2f),
                    V(1, 0, 2.4f),
                    V(2.2f, 0, 2.4f),
                    V(3.4f, 0.7f, 2.4f),
                    V(4.6f, 1.4f, 2.4f),
                    V(-1.4f, 0, -1.2f),
                    V(-1.4f, 0, -2.4f),
                },
                Edges = new[]
                {
                    1,
                    2,
                    2,
                    3,
                    3,
                    4,
                    4,
                    5,
                    5,
                    6,
                    6,
                    7,
                    7,
                    8,
                    8,
                    9,
                    9,
                    10,
                    10,
                    11,
                    4,
                    12,
                    12,
                    13,
                },
            },
        };
        var origin = new Vector3(0, 200, 0);
        var fluids = new[] { 0, 1000, 1000, 1116, 1114, 1120 };
        for (int index = 0; index < shapes.Length; index++)
        {
            var shape = shapes[index];
            var factory = fixture(shape.Points.Length, 960000 + index, false);
            shape.Item = fluids[index];
            for (int index2 = 0; index2 < shape.Points.Length; index2++)
                factory.entityPool[index2 + 1].pos = origin + shape.Points[index2];
            for (int index3 = 0; index3 < shape.Edges.Length; index3 += 2)
                PipeConnections.Connect(
                    factory,
                    shape.Edges[index3],
                    shape.Edges[index3 + 1],
                    0,
                    1
                );
            var builder = new PipeMesh();
            for (int index4 = 1; index4 <= shape.Points.Length; index4++)
                PipeVisuals.AddEntity(builder, factory, index4, origin);
            shape.Mesh = builder.Build("FluidPipes.Gallery." + shape.Name);
            var liquid = new PipeMesh(PipeMesh.Radius * 0.76f, false);
            for (int index5 = 1; index5 <= shape.Points.Length; index5++)
                PipeVisuals.AddEntity(liquid, factory, index5, origin);
            shape.Liquid = liquid.Build("FluidPipes.Gallery.Liquid." + shape.Name);
            shape.Title +=
                index == 0 ? " / EMPTY"
                : index == 3 ? " / ACID"
                : index == 4 ? " / REFINED OIL"
                : index == 5 ? " / HYDROGEN"
                : " / WATER";
        }
        try
        {
            foreach (var shape in shapes)
                Render(
                    new[] { shape },
                    1280,
                    900,
                    1,
                    Path.Combine(output, "pipe-" + shape.Name + ".png")
                );
            Render(shapes, 1800, 1200, 3, Path.Combine(output, "pipe-gallery.png"));
        }
        finally
        {
            foreach (var shape in shapes)
            {
                UnityEngine.Object.DestroyImmediate(shape.Mesh);
                UnityEngine.Object.DestroyImmediate(shape.Liquid);
            }
        }
    }

    private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    private static Shape Multi()
    {
        var points = new List<Vector3> { Vector3.zero };
        var edges = new List<int>();
        for (int index = 0; index < 7; index++)
        {
            float angle = index * Mathf.PI * 2 / 7;
            points.Add(V(Mathf.Cos(angle) * 2.5f, 0, Mathf.Sin(angle) * 2.5f));
            edges.Add(1);
            edges.Add(index + 2);
        }
        points.Add(V(0, 2.6f, 0));
        edges.Add(1);
        edges.Add(9);
        return new Shape
        {
            Name = "04-multi-inlet",
            Title = "04  MULTI-ANGLE JUNCTION",
            Points = points.ToArray(),
            Edges = edges.ToArray(),
        };
    }

    private static void Render(Shape[] shapes, int width, int height, int columns, string path)
    {
        int rows = (shapes.Length + columns - 1) / columns;
        var target = new RenderTexture(width, height, 24);
        var previous = RenderTexture.active;
        var objects = new List<GameObject>();
        var resources = new List<UnityEngine.Object>();
        var ambient = RenderSettings.ambientLight;
        try
        {
            RenderTexture.active = target;
            GL.Clear(true, true, new Color(0.035f, 0.05f, 0.065f));
            RenderSettings.ambientLight = new Color(0.65f, 0.65f, 0.65f);
            var font = Font.CreateDynamicFontFromOSFont("Arial", 48);
            resources.Add(font);
            for (int index = 0; index < shapes.Length; index++)
            {
                var shape = shapes[index];
                int layer = 20 + index;
                var root = new GameObject("pipe-gallery-model");
                objects.Add(root);
                root.layer = layer;
                root.AddComponent<MeshFilter>().sharedMesh = shape.Mesh;
                root.AddComponent<MeshRenderer>().sharedMaterials = new[]
                {
                    PipeVisuals.BodyMaterial,
                    PipeVisuals.JointMaterial,
                };
                if (shape.Item != 0)
                {
                    var fluid = new GameObject("pipe-gallery-fluid");
                    objects.Add(fluid);
                    fluid.layer = layer;
                    fluid.AddComponent<MeshFilter>().sharedMesh = shape.Liquid;
                    fluid.AddComponent<MeshRenderer>().sharedMaterials = new[]
                    {
                        PipeVisuals.LiquidMaterial(shape.Item),
                        PipeVisuals.LiquidMaterial(shape.Item),
                    };
                }
                var cameraObject = new GameObject("pipe-gallery-camera");
                objects.Add(cameraObject);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.rect = new Rect(
                    (float)(index % columns) / columns,
                    1f - (float)(index / columns + 1) / rows,
                    1f / columns,
                    1f / rows
                );
                var center = shape.Mesh.bounds.center;
                camera.transform.position = center + new Vector3(6, 7, -10);
                camera.transform.LookAt(center);
                camera.aspect = (float)width / columns / (height / rows);
                var bounds = shape.Mesh.bounds;
                float extentX = 0;
                float extentY = 0;
                for (int index2 = 0; index2 < 8; index2++)
                {
                    var corner =
                        bounds.center
                        + Vector3.Scale(
                            bounds.extents,
                            new Vector3(
                                (index2 & 1) == 0 ? -1 : 1,
                                (index2 & 2) == 0 ? -1 : 1,
                                (index2 & 4) == 0 ? -1 : 1
                            )
                        );
                    var projected = camera.transform.InverseTransformPoint(corner);
                    extentX = Math.Max(extentX, Mathf.Abs(projected.x));
                    extentY = Math.Max(extentY, Mathf.Abs(projected.y));
                }
                camera.orthographicSize = Math.Max(
                    1.6f,
                    Math.Max(extentY, extentX / camera.aspect) * 1.28f
                );
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.035f, 0.05f, 0.065f);
                camera.cullingMask = 1 << layer;
                camera.targetTexture = target;
                var lightObject = new GameObject("pipe-gallery-key");
                objects.Add(lightObject);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.5f;
                light.cullingMask = 1 << layer;
                light.transform.rotation = Quaternion.Euler(45, -25, 0);
                var title = new GameObject("pipe-gallery-caption");
                objects.Add(title);
                title.layer = layer;
                var text = title.AddComponent<TextMesh>();
                text.text = shape.Title;
                text.font = font;
                text.fontSize = 48;
                text.characterSize = camera.orthographicSize * 0.014f;
                text.anchor = TextAnchor.UpperLeft;
                text.color = new Color(0.8f, 0.88f, 0.9f);
                title.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                title.transform.SetPositionAndRotation(
                    camera.ViewportToWorldPoint(new Vector3(0.05f, 0.94f, 2)),
                    camera.transform.rotation
                );
                camera.Render();
            }
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            resources.Add(image);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderSettings.ambientLight = ambient;
            foreach (var item in objects)
                UnityEngine.Object.DestroyImmediate(item);
            foreach (var resource in resources)
                UnityEngine.Object.DestroyImmediate(resource);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
