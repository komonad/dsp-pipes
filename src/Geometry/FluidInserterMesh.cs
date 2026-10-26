using System;
using System.Collections.Generic;
using UnityEngine;

namespace FluidPipes
{
    internal static class FluidInserterMesh
    {
        public const float TubeRadius = 0.105f;
        public const float Clearance = 0.28f;
        internal const int MaterialCount = 6;
        internal const int ArrowMaterialIndex = 5;

        public struct Socket
        {
            public bool Clamp;
            public bool Machine;
            public bool Belt;
            public float PipeRadius;
            public Vector3 Position;
            public Vector3 Center;
            public Vector3 Axis;
            public Vector3 Normal;
            public Vector3 Anchor;

            public Socket Local(Vector3 origin)
            {
                var result = this;
                result.Position -= origin;
                result.Center -= origin;
                result.Anchor -= origin;
                return result;
            }
        }

        private sealed class Builder
        {
            private const int Sides = 20;
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<int>[] triangles =
            {
                new List<int>(),
                new List<int>(),
                new List<int>(),
                new List<int>(),
                new List<int>(),
                new List<int>(),
            };

            private void Triangle(int material, int a, int b, int c)
            {
                if (
                    Vector3.Dot(
                        Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]),
                        normals[a] + normals[b] + normals[c]
                    ) < 0
                )
                {
                    int swap = b;
                    b = c;
                    c = swap;
                }
                triangles[material].Add(a);
                triangles[material].Add(b);
                triangles[material].Add(c);
            }

            public void Tube(
                IList<Vector3> points,
                Vector3 up,
                float radius,
                int material,
                bool inside = false,
                Vector3? startDirection = null,
                Vector3? endDirection = null
            )
            {
                int first = vertices.Count;
                Vector3 side = Vector3.zero;
                for (int row = 0; row < points.Count; row++)
                {
                    var tangent = (
                        points[Math.Min(row + 1, points.Count - 1)] - points[Math.Max(0, row - 1)]
                    ).normalized;
                    if (row == 0 && startDirection.HasValue)
                    {
                        tangent = startDirection.Value.normalized;
                    }
                    else if (row == points.Count - 1 && endDirection.HasValue)
                    {
                        tangent = endDirection.Value.normalized;
                    }
                    if (tangent.sqrMagnitude < 0.001f)
                    {
                        tangent = Vector3.forward;
                    }

                    side =
                        side.sqrMagnitude > 0
                            ? Vector3.ProjectOnPlane(side, tangent)
                            : Vector3.Cross(tangent, up);
                    if (side.sqrMagnitude < 0.0001f)
                    {
                        side = Vector3.Cross(tangent, Vector3.right);
                    }

                    if (side.sqrMagnitude < 0.0001f)
                    {
                        side = Vector3.Cross(tangent, Vector3.forward);
                    }

                    side.Normalize();
                    var other = Vector3.Cross(tangent, side);
                    for (int col = 0; col < Sides; col++)
                    {
                        float angle = col * Mathf.PI * 2 / Sides;
                        var radial = side * Mathf.Cos(angle) + other * Mathf.Sin(angle);
                        vertices.Add(points[row] + radial * radius);
                        normals.Add(inside ? -radial : radial);
                        uv.Add(new Vector2((float)col / Sides, row));
                    }
                    if (row == 0)
                    {
                        continue;
                    }

                    for (int col = 0; col < Sides; col++)
                    {
                        int a = first + (row - 1) * Sides + col;
                        int b = first + (row - 1) * Sides + (col + 1) % Sides;
                        int c = first + row * Sides + col;
                        int d = first + row * Sides + (col + 1) % Sides;
                        Triangle(material, a, b, c);
                        Triangle(material, b, d, c);
                    }
                }
            }

            public void Ring(
                Vector3 center,
                Vector3 axis,
                Vector3 up,
                float length,
                float outer,
                float inner,
                int material
            )
            {
                axis.Normalize();
                var side = Vector3.Cross(axis, up);
                if (side.sqrMagnitude < 0.001f)
                {
                    side = Vector3.Cross(axis, Vector3.right);
                }

                if (side.sqrMagnitude < 0.001f)
                {
                    side = Vector3.Cross(axis, Vector3.forward);
                }

                side.Normalize();
                var other = Vector3.Cross(axis, side);
                Tube(
                    new[] { center - axis * length / 2, center + axis * length / 2 },
                    up,
                    outer,
                    material
                );
                if (inner > 0)
                {
                    Tube(
                        new[] { center - axis * length / 2, center + axis * length / 2 },
                        up,
                        inner,
                        material,
                        true
                    );
                }

                foreach (float sign in new[] { -1f, 1f })
                {
                    int start = vertices.Count;
                    var end = center + axis * (sign * length / 2);
                    for (int index = 0; index < Sides; index++)
                    {
                        float angle = index * Mathf.PI * 2 / Sides;
                        var radial = side * Mathf.Cos(angle) + other * Mathf.Sin(angle);
                        vertices.Add(end + radial * outer);
                        vertices.Add(end + radial * inner);
                        normals.Add(axis * sign);
                        normals.Add(axis * sign);
                        uv.Add(Vector2.zero);
                        uv.Add(Vector2.one);
                    }
                    for (int index2 = 0; index2 < Sides; index2++)
                    {
                        int a = start + index2 * 2;
                        int b = start + (index2 + 1) % Sides * 2;
                        Triangle(material, a, b, a + 1);
                        Triangle(material, b, b + 1, a + 1);
                    }
                }
            }

            public void Arrow(Vector3 center, Vector3 forward, Vector3 up)
            {
                var side = Vector3.Cross(up, forward).normalized;
                int first = vertices.Count;
                foreach (
                    var point in new[]
                    {
                        center + forward * 0.20f,
                        center - forward * 0.10f - side * 0.13f,
                        center - forward * 0.10f - side * 0.045f,
                        center - forward * 0.27f - side * 0.045f,
                        center - forward * 0.27f + side * 0.045f,
                        center - forward * 0.10f + side * 0.045f,
                        center - forward * 0.10f + side * 0.13f,
                    }
                )
                {
                    vertices.Add(point);
                    normals.Add(up);
                    uv.Add(Vector2.zero);
                }
                Triangle(ArrowMaterialIndex, first, first + 1, first + 2);
                Triangle(ArrowMaterialIndex, first, first + 2, first + 5);
                Triangle(ArrowMaterialIndex, first, first + 5, first + 6);
                Triangle(ArrowMaterialIndex, first + 2, first + 3, first + 4);
                Triangle(ArrowMaterialIndex, first + 2, first + 4, first + 5);
            }

            public void Saddle(Socket socket)
            {
                if (!socket.Clamp)
                {
                    return;
                }

                float pipeRadius = socket.PipeRadius > 0 ? socket.PipeRadius : PipeMesh.Radius;
                Ring(
                    socket.Center,
                    socket.Axis,
                    socket.Normal,
                    0.29f,
                    pipeRadius + 0.04f,
                    pipeRadius + 0.006f,
                    0
                );
                Ring(
                    socket.Center,
                    socket.Axis,
                    socket.Normal,
                    0.065f,
                    pipeRadius + 0.043f,
                    pipeRadius + 0.04f,
                    2
                );
                float length = Vector3.Distance(socket.Center, socket.Position) - pipeRadius;
                Ring(
                    socket.Center + socket.Normal * (pipeRadius + length / 2),
                    socket.Normal,
                    socket.Axis,
                    length,
                    0.165f,
                    TubeRadius * 0.87f,
                    0
                );
            }

            public void Machine(Socket socket, Vector3 up)
            {
                if (!socket.Machine && !socket.Belt)
                {
                    return;
                }

                var normal = socket.Normal;
                var center = socket.Center;
                Ring(center, normal, up, 0.08f, 0.16f, TubeRadius * 0.87f, 0);
                Tube(new[] { center - normal * 0.04f, center + normal * 0.10f }, up, TubeRadius, 0);
                Tube(
                    Bend(center + normal * 0.10f, center + normal * 0.40f, socket.Position),
                    up,
                    TubeRadius,
                    0,
                    startDirection: normal,
                    endDirection: up
                );
            }

            public void Support(Socket socket, Vector3 trunk, Vector3 forward, Vector3 up)
            {
                if (!socket.Machine && !socket.Belt)
                {
                    return;
                }

                var side = Vector3.Cross(forward, up).normalized;
                Vector3 root = SupportRoot(socket);
                // 两根支撑杆与原版框架及主干套环外侧重叠。
                foreach (float sign in new[] { -1f, 1f })
                {
                    Beam(
                        root + side * (sign * 0.145f),
                        trunk + side * (sign * 0.145f),
                        up,
                        0.075f,
                        0.075f
                    );
                }
                Ring(trunk, forward, up, 0.12f, 0.16f, TubeRadius + 0.005f, 4);
            }

            private void Beam(Vector3 start, Vector3 end, Vector3 up, float width, float height)
            {
                var axis = (end - start).normalized;
                var side = Vector3.Cross(axis, up).normalized;
                if (side.sqrMagnitude < 0.001f)
                {
                    side = Vector3.Cross(axis, Vector3.right).normalized;
                }
                var vertical = Vector3.Cross(side, axis).normalized;
                var corners = new Vector3[8];
                for (int endpoint = 0; endpoint < 2; endpoint++)
                {
                    Vector3 center = endpoint == 0 ? start : end;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        float horizontalSign = corner == 0 || corner == 3 ? -1 : 1;
                        float verticalSign = corner < 2 ? -1 : 1;
                        corners[endpoint * 4 + corner] =
                            center
                            + side * (horizontalSign * width / 2)
                            + vertical * (verticalSign * height / 2);
                    }
                }
                for (int face = 0; face < 4; face++)
                {
                    Quad(
                        corners[face],
                        corners[(face + 1) % 4],
                        corners[4 + (face + 1) % 4],
                        corners[4 + face]
                    );
                }
                Quad(corners[3], corners[2], corners[1], corners[0]);
                Quad(corners[4], corners[5], corners[6], corners[7]);
            }

            private void Quad(Vector3 first, Vector3 second, Vector3 third, Vector3 fourth)
            {
                int start = vertices.Count;
                var normal = Vector3.Cross(second - first, third - first).normalized;
                vertices.AddRange(new[] { first, second, third, fourth });
                for (int index = 0; index < 4; index++)
                {
                    normals.Add(normal);
                    uv.Add(Vector2.zero);
                }
                Triangle(4, start, start + 1, start + 2);
                Triangle(4, start, start + 2, start + 3);
            }

            public Mesh Build()
            {
                var mesh = new Mesh { name = "FluidPipes.FluidInserter" };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uv);
                mesh.subMeshCount = triangles.Length;
                for (int index = 0; index < triangles.Length; index++)
                {
                    mesh.SetTriangles(triangles[index], index);
                }

                mesh.RecalculateBounds();
                return mesh;
            }
        }

        internal static Vector3 SupportRoot(Socket socket) =>
            socket.Anchor + socket.Axis * 0.96f + socket.Normal * (socket.Machine ? 0.80f : 0.27f);

        private static Vector3[] Bend(Vector3 a, Vector3 control, Vector3 b)
        {
            var result = new Vector3[9];
            for (int index = 0; index < result.Length; index++)
            {
                float t = (float)index / (result.Length - 1);
                result[index] = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * control + t * t * b;
            }
            return result;
        }

        public static Mesh Create(Vector3 start, Vector3 end, Vector3 up) =>
            Create(new Socket { Position = start }, new Socket { Position = end }, up);

        public static Mesh Create(Socket source, Socket destination, Vector3 up)
        {
            var start = source.Position;
            var end = destination.Position;
            up.Normalize();
            var delta = Vector3.ProjectOnPlane(end - start, up);
            float distance = delta.magnitude;
            var forward =
                distance > 0.001f ? delta / distance : Vector3.Cross(up, Vector3.right).normalized;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            float height = Mathf.Max(Vector3.Dot(start, up), Vector3.Dot(end, up)) + Clearance;
            var a = start + up * (height - Vector3.Dot(start, up));
            var b = end + up * (height - Vector3.Dot(end, up));
            float bend = Mathf.Min(0.16f, distance * 0.24f);
            var builder = new Builder();
            builder.Saddle(source);
            builder.Saddle(destination);
            builder.Machine(source, up);
            builder.Machine(destination, up);
            builder.Tube(new[] { start, a - up * bend }, forward, TubeRadius, 0);
            builder.Tube(
                Bend(a - up * bend, a, a + forward * bend),
                up,
                TubeRadius,
                0,
                startDirection: up,
                endDirection: forward
            );
            builder.Tube(
                Bend(b - forward * bend, b, b - up * bend),
                up,
                TubeRadius,
                0,
                startDirection: forward,
                endDirection: -up
            );
            builder.Tube(new[] { b - up * bend, end }, forward, TubeRadius, 0);
            var left = a + forward * bend;
            var right = b - forward * bend;
            builder.Tube(new[] { left, right }, up, TubeRadius, 1);
            builder.Tube(new[] { left, right }, up, TubeRadius * 0.87f, 1, true);
            foreach (var point in new[] { left, right })
            {
                builder.Ring(point, forward, up, 0.09f, 0.138f, TubeRadius * 0.86f, 0);
            }
            builder.Support(source, left, forward, up);
            builder.Support(destination, right, forward, up);

            foreach (var point in new[] { start, end })
            {
                builder.Ring(point + up * 0.025f, up, forward, 0.08f, 0.17f, TubeRadius * 0.87f, 0);
                builder.Ring(point + up * 0.10f, up, forward, 0.025f, 0.126f, TubeRadius, 2);
            }
            var plate = Vector3.Lerp(left, right, 0.5f);
            float housing = Mathf.Min(0.50f, Mathf.Max(0.1f, distance - 2 * bend));
            builder.Ring(plate, forward, up, housing, 0.15f, TubeRadius, 0);
            builder.Ring(plate, forward, up, housing * 0.55f, 0.155f, 0.15f, 2);
            builder.Ring(plate - forward * (housing * 0.3f), forward, up, 0.025f, 0.16f, 0.15f, 3);
            builder.Arrow(plate + up * 0.157f, forward, up);
            return builder.Build();
        }
    }
}
