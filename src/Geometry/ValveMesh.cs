using System;
using System.Collections.Generic;
using UnityEngine;

namespace FluidPipes
{
    internal sealed class ValveMesh
    {
        private const int Sides = 48;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> coordinates = new List<Vector2>();
        private readonly List<int>[] triangles =
        {
            new List<int>(),
            new List<int>(),
            new List<int>(),
            new List<int>(),
            new List<int>(),
        };
        private Vector3 origin;
        private Quaternion frame;

        internal static void Configure(PrefabDesc description)
        {
            Vector3 first = description.portPoses[0].position;
            Vector3 second = description.portPoses[1].position;
            Vector3 midpoint = (first + second) * 0.5f;
            Vector3 axis = (second - first).normalized;
            description.portPoses = (Pose[])description.portPoses.Clone();
            description.portPoses[0].position = midpoint - axis * 0.6f;
            description.portPoses[1].position = midpoint + axis * 0.6f;
            first = description.portPoses[0].position;
            second = description.portPoses[1].position;
            var builder = new ValveMesh
            {
                origin = (first + second) * 0.5f,
                frame = Quaternion.LookRotation(second - first, Vector3.up),
            };
            float halfLength = Vector3.Distance(first, second) * 0.5f;
            float bodyHalfLength = Math.Max(0.28f, halfLength - 0.27f);
            builder.Barrel(
                new Vector3(0, PipeMesh.CenterHeight, 0),
                Vector3.forward,
                bodyHalfLength * 2,
                0.37f,
                0.065f,
                0
            );
            builder.BeveledBox(
                new Vector3(0, 0.075f, 0),
                new Vector3(0.70f, 0.15f, bodyHalfLength * 1.7f),
                0.035f,
                2
            );
            foreach (float side in new[] { -1f, 1f })
            {
                builder.Barrel(
                    new Vector3(0, PipeMesh.CenterHeight, side * (halfLength - 0.14f)),
                    Vector3.forward,
                    0.29f,
                    PipeMesh.Radius,
                    0.025f,
                    1
                );
                builder.Flange(
                    new Vector3(0, PipeMesh.CenterHeight, side * (bodyHalfLength + 0.035f)),
                    0.15f,
                    PipeMesh.Radius + 0.075f,
                    PipeMesh.Radius * 0.94f
                );
                builder.Cylinder(
                    new Vector3(0, PipeMesh.CenterHeight, side * (bodyHalfLength - 0.02f)),
                    Vector3.forward,
                    0.035f,
                    PipeMesh.Radius + 0.078f,
                    side < 0 ? 3 : 4
                );
                for (int bolt = 0; bolt < 4; bolt++)
                {
                    float angle = (bolt + 0.5f) * Mathf.PI / 2;
                    builder.Barrel(
                        new Vector3(
                            Mathf.Cos(angle) * (PipeMesh.Radius + 0.028f),
                            PipeMesh.CenterHeight + Mathf.Sin(angle) * (PipeMesh.Radius + 0.028f),
                            side * (bodyHalfLength + 0.115f)
                        ),
                        Vector3.forward,
                        0.025f,
                        0.027f,
                        0.005f,
                        2
                    );
                }
            }
            builder.Barrel(new Vector3(0, 0.67f, 0), Vector3.up, 0.18f, 0.16f, 0.025f, 1);
            builder.BeveledBox(
                new Vector3(0, 0.765f, 0),
                new Vector3(0.48f, 0.25f, 0.48f),
                0.06f,
                0
            );
            builder.BeveledBox(
                new Vector3(0, 0.878f, 0),
                new Vector3(0.36f, 0.035f, 0.36f),
                0.015f,
                2
            );
            builder.Barrel(new Vector3(0, 0.91f, 0), Vector3.up, 0.04f, 0.10f, 0.012f, 1);
            builder.Box(new Vector3(0, 0.936f, 0), new Vector3(0.025f, 0.012f, 0.13f), 3);
            builder.Box(new Vector3(0, 0.78f, -0.242f), new Vector3(0.21f, 0.045f, 0.012f), 2);
            builder.Box(new Vector3(0, 0.78f, -0.25f), new Vector3(0.135f, 0.02f, 0.008f), 3);
            Mesh mesh = builder.Build();
            Material native = LDB.items.Select(2101).prefabDesc.lodMaterials[0][0];
            Material[] materials =
            {
                Surface(native, "Shell", new Color(0.83f, 0.86f, 0.88f), 0.02f, 0.48f),
                Surface(native, "Steel", new Color(0.80f, 0.83f, 0.86f), 0.95f, 0.78f),
                Surface(native, "Panel", new Color(0.12f, 0.17f, 0.21f), 0.12f, 0.4f),
                Surface(native, "PortA", new Color(0.16f, 0.72f, 0.9f), 0, 0.42f),
                Surface(native, "PortB", new Color(0.96f, 0.62f, 0.19f), 0, 0.42f),
            };
            description.mesh = mesh;
            description.meshes = new[] { mesh };
            description.materials = materials;
            description.lodCount = 1;
            description.lodMeshes = new[] { mesh };
            description.lodMaterials = new[] { materials };
            // Native culling uploads all four distance uniforms for every LOD count.
            description.lodDistances = new[] { 1000f, 1000f, 1000f, 1000f };
            description.lodBlueprintMaterials = new[] { new Material[materials.Length] };
            description.lodSubmeshIgnores = new[] { new bool[materials.Length] };
            description.lodVertas = new VertaBuffer[1];
            description.cullingHeight = 1;
            description.roughHeight = 1;
            description.roughWidth = 0.85f;
            description.colliders = (ColliderData[])description.colliders.Clone();
            for (int index = 0; index < description.colliders.Length; index++)
            {
                if (description.colliders[index].shape == EColliderShape.Box)
                {
                    var collider = description.colliders[index];
                    collider.pos = builder.origin + Vector3.up * 0.48f;
                    collider.q = builder.frame;
                    collider.ext = new Vector3(0.4f, 0.48f, halfLength);
                    description.colliders[index] = collider;
                }
            }
            description.buildColliders = Array.FindAll(
                description.colliders,
                collider => collider.usage == EColliderUsage.Build
            );
            if (description.buildColliders.Length > 0)
            {
                description.buildCollider = description.buildColliders[0];
            }
        }

        private static Material Surface(
            Material native,
            string name,
            Color color,
            float metallic,
            float smoothness
        )
        {
            var material = new Material(native) { name = "FluidPipes.Valve." + name };
            material.SetColor("_Color", color);
            material.SetTexture("_MainTex", Texture2D.whiteTexture);
            material.SetTexture("_NormalTex", null);
            material.SetTexture("_EmissionTex", null);
            var finish = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            finish.SetPixel(0, 0, new Color(metallic, 1, 0, smoothness));
            finish.Apply(false, true);
            material.SetTexture("_MS_Tex", finish);
            material.SetFloat("_AlbedoMultiplier", 1);
            material.SetFloat("_MetallicMultiplier", 1);
            material.SetFloat("_SmoothMultiplier", 1);
            material.SetFloat("_EmissionMultiplier", 0);
            return material;
        }

        private void Quad(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth,
            Vector3 normal,
            int material
        )
        {
            Quad(first, second, third, fourth, normal, normal, normal, normal, material);
        }

        private void Quad(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth,
            Vector3 firstNormal,
            Vector3 secondNormal,
            Vector3 thirdNormal,
            Vector3 fourthNormal,
            int material
        )
        {
            int start = vertices.Count;
            foreach (var point in new[] { first, second, third, fourth })
            {
                vertices.Add(origin + frame * point);
                coordinates.Add(Vector2.zero);
            }
            foreach (var normal in new[] { firstNormal, secondNormal, thirdNormal, fourthNormal })
            {
                normals.Add(frame * normal);
            }
            bool reverse =
                Vector3.Dot(Vector3.Cross(second - first, third - first), firstNormal) < 0;
            triangles[material]
                .AddRange(
                    reverse
                        ? new[] { start, start + 2, start + 1, start, start + 3, start + 2 }
                        : new[] { start, start + 1, start + 2, start, start + 2, start + 3 }
                );
        }

        private void Triangle(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 normal,
            int material
        )
        {
            int start = vertices.Count;
            foreach (var point in new[] { first, second, third })
            {
                vertices.Add(origin + frame * point);
                normals.Add(frame * normal);
                coordinates.Add(Vector2.zero);
            }
            bool reverse = Vector3.Dot(Vector3.Cross(second - first, third - first), normal) < 0;
            triangles[material]
                .AddRange(
                    reverse
                        ? new[] { start, start + 2, start + 1 }
                        : new[] { start, start + 1, start + 2 }
                );
        }

        private void BeveledBox(Vector3 center, Vector3 size, float bevel, int material)
        {
            Vector3 halfSize = size * 0.5f;
            float[] heights = { -halfSize.y, -halfSize.y + bevel, halfSize.y - bevel, halfSize.y };
            var sections = new Vector3[4, 8];
            for (int row = 0; row < 4; row++)
            {
                float inset = row == 0 || row == 3 ? bevel : 0;
                float halfWidth = halfSize.x - inset;
                float halfDepth = halfSize.z - inset;
                Vector2[] outline =
                {
                    new Vector2(-halfWidth + bevel, -halfDepth),
                    new Vector2(halfWidth - bevel, -halfDepth),
                    new Vector2(halfWidth, -halfDepth + bevel),
                    new Vector2(halfWidth, halfDepth - bevel),
                    new Vector2(halfWidth - bevel, halfDepth),
                    new Vector2(-halfWidth + bevel, halfDepth),
                    new Vector2(-halfWidth, halfDepth - bevel),
                    new Vector2(-halfWidth, -halfDepth + bevel),
                };
                for (int index = 0; index < 8; index++)
                {
                    sections[row, index] =
                        center + new Vector3(outline[index].x, heights[row], outline[index].y);
                }
            }
            for (int index = 0; index < 8; index++)
            {
                int next = (index + 1) % 8;
                for (int row = 0; row < 3; row++)
                {
                    Vector3 first = sections[row, index];
                    Vector3 second = sections[row, next];
                    Vector3 third = sections[row + 1, next];
                    Vector3 normal = Vector3.Cross(third - first, second - first).normalized;
                    Quad(first, second, third, sections[row + 1, index], normal, material);
                }
                Triangle(
                    center - Vector3.up * halfSize.y,
                    sections[0, index],
                    sections[0, next],
                    Vector3.down,
                    material
                );
                Triangle(
                    center + Vector3.up * halfSize.y,
                    sections[3, index],
                    sections[3, next],
                    Vector3.up,
                    material
                );
            }
        }

        private void Box(Vector3 center, Vector3 size, int material)
        {
            foreach (
                Vector3 normal in new[]
                {
                    Vector3.right,
                    Vector3.left,
                    Vector3.up,
                    Vector3.down,
                    Vector3.forward,
                    Vector3.back,
                }
            )
            {
                Vector3 side = Vector3.Cross(
                    normal,
                    Math.Abs(normal.y) > 0.5f ? Vector3.forward : Vector3.up
                );
                Vector3 up = Vector3.Cross(normal, side);
                Vector3 face = center + Vector3.Scale(normal, size) * 0.5f;
                side = Vector3.Scale(side, size) * 0.5f;
                up = Vector3.Scale(up, size) * 0.5f;
                Quad(
                    face - side - up,
                    face + side - up,
                    face + side + up,
                    face - side + up,
                    normal,
                    material
                );
            }
        }

        private void Cylinder(
            Vector3 center,
            Vector3 axis,
            float length,
            float radius,
            int material
        )
        {
            Barrel(center, axis, length, radius, 0, material);
        }

        private void Barrel(
            Vector3 center,
            Vector3 axis,
            float length,
            float radius,
            float bevel,
            int material
        )
        {
            Quaternion rotation = Quaternion.LookRotation(axis);
            Vector2[] profile =
                bevel > 0
                    ? new[]
                    {
                        new Vector2(-length / 2, radius - bevel),
                        new Vector2(-length / 2 + bevel, radius),
                        new Vector2(length / 2 - bevel, radius),
                        new Vector2(length / 2, radius - bevel),
                    }
                    : new[] { new Vector2(-length / 2, radius), new Vector2(length / 2, radius) };
            Revolve(center, axis, profile, material);
            for (int index = 0; index < Sides; index++)
            {
                float angle = index * Mathf.PI * 2 / Sides;
                float nextAngle = (index + 1) * Mathf.PI * 2 / Sides;
                Vector3 radial = rotation * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                Vector3 next =
                    rotation * new Vector3(Mathf.Cos(nextAngle), Mathf.Sin(nextAngle), 0);
                Vector3 back = center - axis * (length / 2);
                Vector3 front = center + axis * (length / 2);
                Triangle(
                    back,
                    back + next * profile[0].y,
                    back + radial * profile[0].y,
                    -axis,
                    material
                );
                int last = profile.Length - 1;
                Triangle(
                    front,
                    front + radial * profile[last].y,
                    front + next * profile[last].y,
                    axis,
                    material
                );
            }
        }

        private void Flange(Vector3 center, float length, float outerRadius, float innerRadius)
        {
            const float bevel = 0.025f;
            var profile = new[]
            {
                new Vector2(-length / 2, outerRadius - bevel),
                new Vector2(-length / 2 + bevel, outerRadius),
                new Vector2(length / 2 - bevel, outerRadius),
                new Vector2(length / 2, outerRadius - bevel),
                new Vector2(length / 2, innerRadius),
                new Vector2(-length / 2, innerRadius),
                new Vector2(-length / 2, outerRadius - bevel),
            };
            Revolve(center, Vector3.forward, profile, 1);
        }

        private void Revolve(Vector3 center, Vector3 axis, Vector2[] profile, int material)
        {
            Quaternion rotation = Quaternion.LookRotation(axis);
            for (int index = 0; index < Sides; index++)
            {
                float angle = index * Mathf.PI * 2 / Sides;
                float nextAngle = (index + 1) * Mathf.PI * 2 / Sides;
                Vector3 radial = rotation * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                Vector3 next =
                    rotation * new Vector3(Mathf.Cos(nextAngle), Mathf.Sin(nextAngle), 0);
                for (int section = 0; section + 1 < profile.Length; section++)
                {
                    Vector2 rear = profile[section];
                    Vector2 forward = profile[section + 1];
                    Vector2 slope = new Vector2(forward.x - rear.x, rear.y - forward.y).normalized;
                    Vector3 firstNormal = radial * slope.x + axis * slope.y;
                    Vector3 secondNormal = next * slope.x + axis * slope.y;
                    Quad(
                        center + axis * rear.x + radial * rear.y,
                        center + axis * rear.x + next * rear.y,
                        center + axis * forward.x + next * forward.y,
                        center + axis * forward.x + radial * forward.y,
                        firstNormal,
                        secondNormal,
                        secondNormal,
                        firstNormal,
                        material
                    );
                }
            }
        }

        private Mesh Build()
        {
            var mesh = new Mesh { name = "FluidPipes.Valve" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, coordinates);
            mesh.colors32 = new Color32[vertices.Count];
            mesh.subMeshCount = triangles.Length;
            for (int index = 0; index < triangles.Length; index++)
            {
                mesh.SetTriangles(triangles[index], index);
            }
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
