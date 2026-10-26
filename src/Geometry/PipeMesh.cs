using System;
using System.Collections.Generic;
using FluidPipes.API;
using UnityEngine;
using UnityEngine.Rendering;

namespace FluidPipes
{
    internal sealed partial class PipeMesh
    {
        public const float Radius = 0.32f;

        // 默认运输中心线相对原生实体锚点的高度偏移。
        internal const float CenterHeight = 0.34f;
        internal const int MachinePanelMaterial = 2;
        internal const int MachineShellMaterial = 3;
        public static bool MetalFittings = true;
        private const int Sides = 32;
        private const int TubeRingSize = Sides + 1;
        private readonly float radius;
        private readonly bool trim;
        private readonly PipeGeometryProfile geometry;
        private readonly Func<Vector3, float> textureCoordinate;
        private readonly float textureCoordinatePeriod;
        private readonly Vector3 textureOrigin;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> glassInterior = new List<int>();
        private readonly HashSet<Vector3> rings = new HashSet<Vector3>();
        private List<Vector3> polygon = new List<Vector3>(8);
        private List<Vector3> clipped = new List<Vector3>(8);
        private readonly List<int>[] indices =
        {
            new List<int>(),
            new List<int>(),
            new List<int>(),
            new List<int>(),
        };
        public int VertexCount => vertices.Count;

        public void Append(PipeMesh source, Vector3 offset, bool reverse = false)
        {
            int start = vertices.Count;
            foreach (var point in source.vertices)
            {
                vertices.Add(point + offset);
            }

            if (reverse)
            {
                foreach (var normal in source.normals)
                {
                    normals.Add(-normal);
                }
            }
            else
            {
                normals.AddRange(source.normals);
            }

            uvs.AddRange(source.uvs);
            for (int material = 0; material < indices.Length; material++)
            {
                for (int index = 0; index < source.indices[material].Count; index += 3)
                {
                    var triangles = source.indices[material];
                    var target = reverse && material == 0 ? glassInterior : indices[material];
                    target.Add(start + triangles[index]);
                    target.Add(start + triangles[index + (reverse ? 2 : 1)]);
                    target.Add(start + triangles[index + (reverse ? 1 : 2)]);
                }
            }

            for (int index = 0; index < source.glassInterior.Count; index += 3)
            {
                glassInterior.Add(start + source.glassInterior[index]);
                glassInterior.Add(start + source.glassInterior[index + (reverse ? 2 : 1)]);
                glassInterior.Add(start + source.glassInterior[index + (reverse ? 1 : 2)]);
            }
        }

        public PipeMesh(
            float radius = Radius,
            bool trim = true,
            Func<Vector3, float> textureCoordinate = null,
            Vector3 textureOrigin = default(Vector3),
            PipeGeometryProfile geometry = null,
            float textureCoordinatePeriod = 0
        )
        {
            this.radius = radius;
            this.trim = trim;
            this.geometry = geometry ?? PipeGeometryProfile.Default;
            this.textureCoordinate = textureCoordinate;
            this.textureCoordinatePeriod = textureCoordinatePeriod;
            this.textureOrigin = textureOrigin;
        }

        public void Node(
            Vector3 center,
            Vector3 up,
            Vector3 forward,
            IList<Vector3> ends,
            IList<bool> collars,
            IList<Vector3> endAxes = null,
            IList<Vector3> endUps = null,
            IList<float> endPeerRadiusRatios = null
        )
        {
            if (trim && endPeerRadiusRatios != null)
            {
                for (int endIndex = 0; endIndex < ends.Count; endIndex++)
                {
                    float ratio = endPeerRadiusRatios[endIndex];
                    if (
                        float.IsNaN(ratio)
                        || float.IsInfinity(ratio)
                        || ratio <= 0
                        || ratio >= 0.9999f
                    )
                    {
                        continue;
                    }

                    Vector3 tangent = endAxes == null ? ends[endIndex] - center : endAxes[endIndex];
                    Vector3 frameUp = endUps == null ? up : endUps[endIndex];
                    float availableDepth = Vector3.Distance(ends[endIndex], center) * 0.45f;
                    Reducer(ends[endIndex], tangent, frameUp, ratio, availableDepth);
                }
            }

            if (ends.Count == 0)
            {
                if (forward.sqrMagnitude < 0.1f)
                {
                    forward = Vector3.Cross(up, Vector3.right).normalized;
                }

                float halfLength = 0.35f * geometry.Radius / Radius;
                Tube(
                    new[] { center - forward * halfLength, center + forward * halfLength },
                    up,
                    radius,
                    0,
                    true,
                    true
                );
                SupportRing(center, forward, up);
                return;
            }
            if (ends.Count == 2)
            {
                bool bend = NeedsJointFittings(center, ends);
                if (!bend)
                {
                    Tube(
                        new[] { ends[0], ends[1] },
                        up,
                        radius,
                        0,
                        false,
                        false,
                        null,
                        endAxes == null ? null : new[] { -endAxes[0], endAxes[1] },
                        endUps
                    );
                    SupportRing(center, ends[1] - ends[0], up);
                    return;
                }
                if (!trim && MetalFittings)
                {
                    return;
                }

                var points = new Vector3[17];
                for (int index = 0; index < points.Length; index++)
                {
                    float t = (float)index / (points.Length - 1);
                    points[index] =
                        (1 - t) * (1 - t) * ends[0] + 2 * t * (1 - t) * center + t * t * ends[1];
                }
                Vector3[] axes = null;
                Vector3[] frames = null;
                if (endAxes != null)
                {
                    axes = new Vector3[points.Length];
                    axes[0] = -endAxes[0];
                    axes[axes.Length - 1] = endAxes[1];
                }
                if (endUps != null)
                {
                    frames = new Vector3[points.Length];
                    frames[0] = endUps[0];
                    frames[frames.Length - 1] = endUps[1];
                }
                Tube(
                    points,
                    up,
                    radius,
                    trim && MetalFittings ? 1 : 0,
                    false,
                    false,
                    null,
                    axes,
                    frames
                );
                if (trim)
                {
                    for (int endIndex = 0; endIndex < ends.Count; endIndex++)
                    {
                        Vector3 end = ends[endIndex];
                        if (collars[endIndex] && rings.Add(end))
                        {
                            Collar(end, end - center, up);
                        }
                    }
                }
            }
            else
            {
                if (ends.Count > 2)
                {
                    if (!trim && MetalFittings)
                    {
                        return;
                    }

                    Junction(center, up, ends);
                    if (trim)
                    {
                        for (int endIndex = 0; endIndex < ends.Count; endIndex++)
                        {
                            Vector3 end = ends[endIndex];
                            if (collars[endIndex] && rings.Add(end))
                            {
                                Collar(end, end - center, up);
                            }
                        }
                    }
                }
                else
                {
                    for (int index2 = 0; index2 < ends.Count; index2++)
                    {
                        Tube(
                            new[] { center, ends[index2] },
                            up,
                            radius,
                            0,
                            true,
                            false,
                            null,
                            endAxes == null ? null : new[] { endAxes[index2], endAxes[index2] },
                            endUps == null ? null : new[] { up, endUps[index2] },
                            geometry.SupportRingWidth <= 0
                        );
                        SupportRing(
                            center,
                            endAxes == null ? ends[index2] - center : endAxes[index2],
                            up
                        );
                    }
                }
            }
        }

        internal static bool NeedsJointFittings(Vector3 center, IList<Vector3> ends) =>
            ends.Count > 2
            || ends.Count == 2
                && Vector3.Dot((ends[0] - center).normalized, (ends[1] - center).normalized)
                    > -0.995f;

        private void Collar(Vector3 center, Vector3 tangent, Vector3 up)
        {
            if (tangent.sqrMagnitude < 0.000001f)
            {
                return;
            }

            tangent.Normalize();
            float depthScale = radius / Radius * geometry.FittingScale;
            float edgeRadius = radius * (1 + 0.02f * geometry.FittingScale);
            float outerRadius = radius * (1 + 0.1f * geometry.FittingScale);
            int start = vertices.Count;
            Tube(
                new[]
                {
                    center - tangent * (0.055f * depthScale),
                    center - tangent * (0.035f * depthScale),
                    center + tangent * (0.035f * depthScale),
                    center + tangent * (0.055f * depthScale),
                },
                up,
                radius,
                1,
                false,
                false,
                new[] { edgeRadius, outerRadius, outerRadius, edgeRadius }
            );
            Annulus(center - tangent * (0.055f * depthScale), -tangent, start, radius * 0.98f);
            Annulus(
                center + tangent * (0.055f * depthScale),
                tangent,
                start + TubeRingSize * 3,
                radius * 0.98f
            );
        }

        private void SupportRing(Vector3 center, Vector3 tangent, Vector3 up)
        {
            if (!trim || geometry.SupportRingWidth <= 0 || tangent.sqrMagnitude < 0.000001f)
            {
                return;
            }

            tangent.Normalize();
            float halfWidth = geometry.SupportRingWidth * 0.5f;
            float flatHalfWidth = halfWidth * 0.6f;
            Vector3 firstEdge = center - tangent * halfWidth;
            Vector3 lastEdge = center + tangent * halfWidth;
            if ((lastEdge - firstEdge).sqrMagnitude < 0.0000000001f)
            {
                return;
            }

            float edgeRadius = radius * (1 + 0.025f * geometry.FittingScale);
            float outerRadius = radius * (1 + 0.08f * geometry.FittingScale);
            int start = vertices.Count;
            Tube(
                new[]
                {
                    firstEdge,
                    center - tangent * flatHalfWidth,
                    center + tangent * flatHalfWidth,
                    lastEdge,
                },
                up,
                radius,
                1,
                false,
                false,
                new[] { edgeRadius, outerRadius, outerRadius, edgeRadius }
            );
            Annulus(center - tangent * halfWidth, -tangent, start, radius * 0.98f);
            Annulus(
                center + tangent * halfWidth,
                tangent,
                start + TubeRingSize * 3,
                radius * 0.98f
            );
        }

        private void Reducer(
            Vector3 end,
            Vector3 tangent,
            Vector3 up,
            float peerRadiusRatio,
            float availableDepth
        )
        {
            float depth = Mathf.Min(radius * 0.45f * geometry.FittingScale, availableDepth);
            if (depth <= 0.00001f || tangent.sqrMagnitude < 0.000001f || !rings.Add(end))
            {
                return;
            }

            tangent.Normalize();
            Vector3 rear = end - tangent * depth;
            var points = new[]
            {
                rear,
                end - tangent * (depth * 0.78f),
                end - tangent * (depth * 0.22f),
                end,
            };
            float outerRadius = radius * (1 + 0.1f * geometry.FittingScale);
            float rearInnerRadius = radius * 0.98f;
            float mouthRadius = radius * peerRadiusRatio * 0.98f;
            int firstRing = vertices.Count;
            Tube(
                points,
                up,
                radius,
                1,
                false,
                false,
                new[]
                {
                    radius * (1 + 0.01f * geometry.FittingScale),
                    outerRadius,
                    outerRadius,
                    radius * (1 + 0.02f * geometry.FittingScale),
                }
            );
            Annulus(rear, -tangent, firstRing, rearInnerRadius);
            Annulus(end, tangent, firstRing + TubeRingSize * 3, mouthRadius);

            var liner = new PipeMesh(
                radius,
                false,
                textureCoordinate,
                textureOrigin,
                geometry,
                textureCoordinatePeriod
            );
            liner.Tube(
                points,
                up,
                radius,
                1,
                false,
                false,
                new[] { rearInnerRadius, rearInnerRadius, mouthRadius, mouthRadius }
            );
            Append(liner, Vector3.zero, true);
        }

        private void Annulus(Vector3 center, Vector3 normal, int ring, float innerRadius)
        {
            int start = vertices.Count;
            for (int index = 0; index <= Sides; index++)
            {
                var radial = vertices[ring + index % Sides] - center;
                vertices.Add(center + radial);
                vertices.Add(center + radial.normalized * innerRadius);
                normals.Add(normal);
                normals.Add(normal);
                uvs.Add(new Vector2((float)index / Sides, 1));
                uvs.Add(new Vector2((float)index / Sides, 0));
            }
            for (int index2 = 0; index2 < Sides; index2++)
            {
                int a = start + index2 * 2;
                int b = start + (index2 + 1) * 2;
                bool reverse =
                    Vector3.Dot(
                        Vector3.Cross(vertices[b] - vertices[a], vertices[a + 1] - vertices[a]),
                        normal
                    ) < 0;
                Triangle(1, a, reverse ? a + 1 : b, reverse ? b : a + 1);
                Triangle(1, b, reverse ? a + 1 : b + 1, reverse ? b + 1 : a + 1);
            }
        }

        private void Tube(
            IList<Vector3> points,
            Vector3 up,
            float radius,
            int material,
            bool capStart,
            bool capEnd,
            IList<float> radii = null,
            IList<Vector3> tangents = null,
            IList<Vector3> frameUps = null,
            bool capFittings = true,
            PipeInteriorSection section = null
        )
        {
            int start = vertices.Count;
            var previousSide = Vector3.zero;
            var previousTangent = Vector3.zero;
            var firstTangent = Vector3.zero;
            bool hasPreviousMappedCoordinate = false;
            float previousMappedCoordinate = 0;
            float previousLongitudinal = 0;
            float arcLength = 0;
            for (int row = 0; row < points.Count; row++)
            {
                if (row > 0)
                {
                    arcLength += Vector3.Distance(points[row - 1], points[row]);
                }
                float longitudinal =
                    textureCoordinate == null
                        ? arcLength * 2
                        : textureCoordinate(points[row] + textureOrigin);
                if (float.IsNaN(longitudinal) || float.IsInfinity(longitudinal))
                {
                    longitudinal = arcLength * 2;
                    hasPreviousMappedCoordinate = false;
                }
                else if (textureCoordinate != null && textureCoordinatePeriod > 0)
                {
                    float mappedCoordinate = longitudinal;
                    if (hasPreviousMappedCoordinate)
                    {
                        double difference = (double)mappedCoordinate - previousMappedCoordinate;
                        difference -=
                            Math.Round(difference / textureCoordinatePeriod)
                            * textureCoordinatePeriod;
                        longitudinal = (float)(previousLongitudinal + difference);
                    }
                    previousMappedCoordinate = mappedCoordinate;
                    previousLongitudinal = longitudinal;
                    hasPreviousMappedCoordinate = true;
                }
                Vector3 tangent = PathTangent(points, tangents, row, previousTangent);
                previousTangent = tangent;
                if (row == 0)
                {
                    firstTangent = tangent;
                }
                // 共享端面框架使相邻透明段位于同一环面与相位。
                bool shared = frameUps != null && frameUps[row].sqrMagnitude > 0.0000001f;
                Vector3 side = FrameSide(
                    tangent,
                    shared ? frameUps[row] : up,
                    previousSide,
                    shared
                );
                previousSide = side;
                var other = Vector3.Cross(tangent, side).normalized;
                for (int col = 0; col <= Sides; col++)
                {
                    float angle = (col % Sides) * Mathf.PI * 2 / Sides;
                    var normal = side * Mathf.Cos(angle) + other * Mathf.Sin(angle);
                    float rowRadius = radii == null ? radius : radii[row];
                    if (section?.Kind == PipeInteriorSectionKind.RoundedRectangle)
                    {
                        Vector2 sectionPoint = RectanglePoint(
                            section,
                            col,
                            out Vector2 sectionNormal
                        );
                        vertices.Add(
                            points[row]
                                + (side * sectionPoint.x + other * sectionPoint.y)
                                    * (rowRadius / section.RadiusFraction)
                        );
                        normal = side * sectionNormal.x + other * sectionNormal.y;
                    }
                    else
                    {
                        vertices.Add(points[row] + normal * rowRadius);
                    }
                    if (radii != null)
                    {
                        int before = Math.Max(0, row - 1);
                        int after = Math.Min(points.Count - 1, row + 1);
                        float sampleLength = (points[after] - points[before]).magnitude;
                        float slope =
                            sampleLength > 0 ? (radii[after] - radii[before]) / sampleLength : 0;
                        normal = (normal - tangent * slope).normalized;
                    }
                    normals.Add(normal);
                    float circumferential =
                        section?.Kind == PipeInteriorSectionKind.RoundedRectangle
                            ? RectanglePerimeterCoordinate(section, col)
                            : (float)col / Sides;
                    uvs.Add(new Vector2(circumferential, longitudinal));
                }
                if (row == 0)
                {
                    continue;
                }

                for (int col = 0; col < Sides; col++)
                {
                    int a = start + (row - 1) * TubeRingSize + col;
                    int b = a + 1;
                    int c = start + row * TubeRingSize + col;
                    int d = c + 1;
                    Triangle(material, a, b, c);
                    Triangle(material, b, d, c);
                }
            }
            if (capStart)
            {
                if (section?.Kind == PipeInteriorSectionKind.RoundedRectangle)
                {
                    SectionCap(points[0], -firstTangent, start, material, radius);
                }
                else
                {
                    Cap(points[0], -firstTangent, start, material, radius, capFittings);
                }
            }

            if (capEnd)
            {
                int lastRing = start + (points.Count - 1) * TubeRingSize;
                if (section?.Kind == PipeInteriorSectionKind.RoundedRectangle)
                {
                    SectionCap(
                        points[points.Count - 1],
                        previousTangent,
                        lastRing,
                        material,
                        radius
                    );
                }
                else
                {
                    Cap(
                        points[points.Count - 1],
                        previousTangent,
                        lastRing,
                        material,
                        radius,
                        capFittings
                    );
                }
            }

            if (trim && material == 0)
            {
                var interior = new PipeMesh(
                    radius * 0.92f,
                    false,
                    textureCoordinate,
                    textureOrigin,
                    geometry,
                    textureCoordinatePeriod
                );
                interior.Tube(
                    points,
                    up,
                    radius * 0.92f,
                    0,
                    capStart,
                    capEnd,
                    null,
                    tangents,
                    frameUps,
                    capFittings
                );
                Append(interior, Vector3.zero, true);
            }
        }

        private void Cap(
            Vector3 center,
            Vector3 normal,
            int ring,
            int material,
            float radius,
            bool capFittings
        )
        {
            float depth = radius * 0.6f;
            int previous = ring;
            for (int row = 1; row < 6; row++)
            {
                float angle = row * Mathf.PI / 12;
                float radialScale = Mathf.Cos(angle);
                float axial = depth * Mathf.Sin(angle);
                int start = vertices.Count;
                for (int index = 0; index <= Sides; index++)
                {
                    var radial = (vertices[ring + index] - center).normalized;
                    vertices.Add(center + radial * (radius * radialScale) + normal * axial);
                    normals.Add(
                        (
                            radial * (radialScale / radius) + normal * (Mathf.Sin(angle) / depth)
                        ).normalized
                    );
                    uvs.Add(new Vector2((float)index / Sides, angle));
                }
                for (int index2 = 0; index2 < Sides; index2++)
                {
                    int next = index2 + 1;
                    OrientedTriangle(material, previous + index2, previous + next, start + index2);
                    OrientedTriangle(material, previous + next, start + next, start + index2);
                }
                previous = start;
            }
            for (int index3 = 0; index3 < Sides; index3++)
            {
                int tip = vertices.Count;
                vertices.Add(center + normal * depth);
                normals.Add(normal);
                uvs.Add(new Vector2((index3 + 0.5f) / Sides, Mathf.PI * 0.5f));
                OrientedTriangle(material, previous + index3, previous + index3 + 1, tip);
            }

            if (trim && material == 0 && capFittings)
            {
                var side = (vertices[ring] - center).normalized;
                var up = Vector3.Cross(side, normal);
                float depthScale = radius / Radius * geometry.FittingScale;
                float outerRadius = radius * (1 + 0.06f * geometry.FittingScale);
                int start = vertices.Count;
                Tube(
                    new[]
                    {
                        center - normal * (0.035f * depthScale),
                        center - normal * (0.02f * depthScale),
                        center + normal * (0.01f * depthScale),
                        center + normal * (0.025f * depthScale),
                    },
                    up,
                    radius,
                    1,
                    false,
                    false,
                    new[]
                    {
                        radius * (1 + 0.01f * geometry.FittingScale),
                        outerRadius,
                        outerRadius,
                        radius * (1 + 0.015f * geometry.FittingScale),
                    }
                );
                Annulus(center - normal * (0.035f * depthScale), -normal, start, radius * 0.97f);
                Annulus(
                    center + normal * (0.025f * depthScale),
                    normal,
                    start + TubeRingSize * 3,
                    radius * 0.97f
                );
            }
        }

        private void OrientedTriangle(int material, int a, int b, int c)
        {
            bool reverse =
                Vector3.Dot(
                    Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]),
                    normals[a] + normals[b] + normals[c]
                ) < 0;
            Triangle(material, a, reverse ? c : b, reverse ? b : c);
        }

        private sealed class Field
        {
            public readonly Dictionary<Vector3Int, int> Vertices =
                new Dictionary<Vector3Int, int>();
            public Vector3 Center;
            public Vector3 Hub;
            public Vector3 Up;
            public Vector3[] Directions;
            public Vector3[] Ends;
            public Vector3[] SampleDirections;
            public Vector3[][] Curves;
            public float[] Lengths;
            public float Radius;

            public float Sample(Vector3 p)
            {
                var q = p - Hub;
                float result = q.magnitude - Radius;
                for (int index = 0; index < Directions.Length; index++)
                {
                    var direction = SampleDirections[index];
                    float t = Mathf.Clamp(
                        Vector3.Dot(q, direction),
                        0,
                        Lengths[index] + Radius * 2
                    );
                    float d;
                    if (Curves == null)
                    {
                        d = (q - direction * t).magnitude - Radius;
                    }
                    else
                    {
                        d = float.MaxValue;
                        var curve = Curves[index];
                        for (int otherIndex = 1; otherIndex < curve.Length; otherIndex++)
                        {
                            var edge = curve[otherIndex] - curve[otherIndex - 1];
                            float u = Mathf.Clamp01(
                                Vector3.Dot(p - curve[otherIndex - 1], edge) / edge.sqrMagnitude
                            );
                            d = Mathf.Min(
                                d,
                                (p - curve[otherIndex - 1] - edge * u).magnitude - Radius
                            );
                        }
                    }
                    // 在射线起点渐隐混合，使重合的圆形端面保持管道半径。
                    float blend = Radius * 0.5f * Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / Radius));
                    if (blend < 0.000001f)
                    {
                        result = Mathf.Min(result, d);
                        continue;
                    }
                    float h = Mathf.Max(blend - Mathf.Abs(result - d), 0) / blend;
                    result = Mathf.Min(result, d) - h * h * blend * 0.25f;
                }
                return result;
            }

            public Vector3 Normal(Vector3 p)
            {
                float e = Radius * 0.08f;
                var n = new Vector3(
                    Sample(p + Vector3.right * e) - Sample(p - Vector3.right * e),
                    Sample(p + Vector3.up * e) - Sample(p - Vector3.up * e),
                    Sample(p + Vector3.forward * e) - Sample(p - Vector3.forward * e)
                );
                for (int index = 0; index < Ends.Length; index++)
                {
                    if (Mathf.Abs(Vector3.Dot(p - Ends[index], Directions[index])) < 0.012f)
                    {
                        n -= Directions[index] * Vector3.Dot(n, Directions[index]);
                    }
                }

                return n.sqrMagnitude > 0.000000001f ? n.normalized : Vector3.up;
            }

            public Vector2 UV(Vector3 p, Vector3 up)
            {
                float distance = float.MaxValue;
                var result = Vector2.zero;
                for (int index = 0; index < Directions.Length; index++)
                {
                    float t = Mathf.Clamp(
                        Vector3.Dot(p - Center, Directions[index]),
                        0,
                        Lengths[index]
                    );
                    var q = p - Center - Directions[index] * t;
                    if (q.sqrMagnitude >= distance)
                    {
                        continue;
                    }

                    distance = q.sqrMagnitude;
                    result = new Vector2(Mathf.Atan2(q.z, q.x) / (Mathf.PI * 2), t * 2);
                }
                return result;
            }

            public Vector3 Snap(Vector3 p)
            {
                for (int index = 0; index < Ends.Length; index++)
                {
                    var q = p - Ends[index];
                    float axial = Vector3.Dot(q, Directions[index]);
                    if (Mathf.Abs(axial) > 0.0001f)
                    {
                        continue;
                    }

                    var radial = q - Directions[index] * axial;
                    if (radial.sqrMagnitude < Radius * Radius * 0.5f)
                    {
                        continue;
                    }

                    var side = Vector3.Cross(Directions[index], Up);
                    if (side.sqrMagnitude < 0.001f)
                    {
                        side = Vector3.Cross(Directions[index], Vector3.right);
                    }

                    if (side.sqrMagnitude < 0.001f)
                    {
                        side = Vector3.Cross(Directions[index], Vector3.forward);
                    }

                    side.Normalize();
                    var other = Vector3.Cross(Directions[index], side).normalized;
                    float angle = Mathf.Atan2(
                        Vector3.Dot(radial, other),
                        Vector3.Dot(radial, side)
                    );
                    float step = Mathf.PI * 2 / Sides;
                    float local = Mathf.Repeat(angle, step) - step * 0.5f;
                    float length = Radius * Mathf.Cos(step * 0.5f) / Mathf.Cos(local);
                    return Ends[index] + radial.normalized * length;
                }
                return p;
            }
        }

        private static readonly int[] Tetrahedra =
        {
            0,
            5,
            1,
            6,
            0,
            1,
            2,
            6,
            0,
            2,
            3,
            6,
            0,
            3,
            7,
            6,
            0,
            7,
            4,
            6,
            0,
            4,
            5,
            6,
        };

        private void Junction(Vector3 center, Vector3 up, IList<Vector3> ends)
        {
            var field = new Field
            {
                Center = center,
                Hub = center,
                Up = up,
                Radius = radius,
                Directions = new Vector3[ends.Count],
                Ends = new Vector3[ends.Count],
                Lengths = new float[ends.Count],
            };
            var bounds = new Bounds(center, Vector3.one * radius * 3);
            for (int index = 0; index < ends.Count; index++)
            {
                field.Ends[index] = ends[index];
                field.Directions[index] = (ends[index] - center).normalized;
                field.Lengths[index] = (ends[index] - center).magnitude;
                bounds.Encapsulate(new Bounds(ends[index], Vector3.one * radius * 2.5f));
            }
            field.SampleDirections = field.Directions;
            var inward = Vector3.zero;
            foreach (var direction in field.Directions)
            {
                inward += direction;
            }

            inward.Normalize();
            bool corner = inward.sqrMagnitude > 0.5f;
            float shortest = float.MaxValue;
            for (int index2 = 0; index2 < ends.Count; index2++)
            {
                if (Vector3.Dot(inward, field.Directions[index2]) < 0.2f)
                {
                    corner = false;
                }
                shortest = Mathf.Min(shortest, field.Lengths[index2]);
            }
            if (corner)
            {
                // 内缩凸形交汇处，并在相邻管段平面保持分支切线。
                field.Hub = center + inward * Mathf.Min(radius * 0.9f, shortest * 0.5f);
                field.Curves = new Vector3[ends.Count][];
                field.SampleDirections = new Vector3[ends.Count];
                for (int index3 = 0; index3 < ends.Count; index3++)
                {
                    field.SampleDirections[index3] = (ends[index3] - field.Hub).normalized;
                    var control = center + field.Directions[index3] * field.Lengths[index3] * 0.35f;
                    var curve = new Vector3[8];
                    for (int otherIndex = 0; otherIndex < 7; otherIndex++)
                    {
                        float t = otherIndex / 6f;
                        curve[otherIndex] =
                            (1 - t) * (1 - t) * field.Hub
                            + 2 * t * (1 - t) * control
                            + t * t * ends[index3];
                    }
                    curve[7] = ends[index3] + field.Directions[index3] * radius * 2;
                    field.Curves[index3] = curve;
                }
            }
            float step = Mathf.Max(
                radius * 0.25f,
                Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) / 64
            );
            int nx = Mathf.CeilToInt(bounds.size.x / step);
            int ny = Mathf.CeilToInt(bounds.size.y / step);
            int nz = Mathf.CeilToInt(bounds.size.z / step);
            var start = bounds.min;
            var size = new Vector3(bounds.size.x / nx, bounds.size.y / ny, bounds.size.z / nz);
            int sy = nx + 1;
            int sz = (ny + 1) * sy;
            var samples = new float[(nz + 1) * sz];
            for (int z = 0; z <= nz; z++)
            {
                for (int y = 0; y <= ny; y++)
                {
                    for (int x = 0; x <= nx; x++)
                    {
                        samples[z * sz + y * sy + x] = field.Sample(
                            start + Vector3.Scale(new Vector3(x, y, z), size)
                        );
                    }
                }
            }

            var p = new Vector3[8];
            var values = new float[8];
            var offsets = new[]
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(1, 1, 0),
                new Vector3Int(0, 1, 0),
                new Vector3Int(0, 0, 1),
                new Vector3Int(1, 0, 1),
                new Vector3Int(1, 1, 1),
                new Vector3Int(0, 1, 1),
            };
            for (int z = 0; z < nz; z++)
            {
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        for (int index4 = 0; index4 < 8; index4++)
                        {
                            var q = offsets[index4] + new Vector3Int(x, y, z);
                            p[index4] = start + Vector3.Scale((Vector3)q, size);
                            values[index4] = samples[q.z * sz + q.y * sy + q.x];
                        }
                        for (int t = 0; t < Tetrahedra.Length; t += 4)
                        {
                            Polygonise(
                                field,
                                up,
                                p,
                                values,
                                Tetrahedra[t],
                                Tetrahedra[t + 1],
                                Tetrahedra[t + 2],
                                Tetrahedra[t + 3]
                            );
                        }
                    }
                }
            }
        }

        private static Vector3 Intersection(Vector3 a, float da, Vector3 b, float db) =>
            Vector3.LerpUnclamped(a, b, da / (da - db));

        private void Polygonise(
            Field field,
            Vector3 up,
            Vector3[] p,
            float[] d,
            int a,
            int b,
            int c,
            int e
        )
        {
            int count =
                (d[a] < 0 ? 1 : 0) + (d[b] < 0 ? 1 : 0) + (d[c] < 0 ? 1 : 0) + (d[e] < 0 ? 1 : 0);
            if (count == 0 || count == 4)
            {
                return;
            }

            if (count == 1 || count == 3)
            {
                bool inside = count == 1;
                int pivot =
                    (d[a] < 0) == inside ? a
                    : (d[b] < 0) == inside ? b
                    : (d[c] < 0) == inside ? c
                    : e;
                var x = Vector3.zero;
                var y = Vector3.zero;
                var z = Vector3.zero;
                int found = 0;
                for (int index2 = 0; index2 < 4; index2++)
                {
                    int index =
                        index2 == 0 ? a
                        : index2 == 1 ? b
                        : index2 == 2 ? c
                        : e;
                    if (index == pivot)
                    {
                        continue;
                    }

                    var q = Intersection(p[pivot], d[pivot], p[index], d[index]);
                    if (found++ == 0)
                    {
                        x = q;
                    }
                    else if (found == 2)
                    {
                        y = q;
                    }
                    else
                    {
                        z = q;
                    }
                }
                Surface(field, up, x, y, z);
            }
            else
            {
                int i0 = -1;
                int i1 = -1;
                int o0 = -1;
                int o1 = -1;
                for (int index3 = 0; index3 < 4; index3++)
                {
                    int index =
                        index3 == 0 ? a
                        : index3 == 1 ? b
                        : index3 == 2 ? c
                        : e;
                    if (d[index] < 0)
                    {
                        if (i0 < 0)
                        {
                            i0 = index;
                        }
                        else
                        {
                            i1 = index;
                        }
                    }
                    else
                    {
                        if (o0 < 0)
                        {
                            o0 = index;
                        }
                        else
                        {
                            o1 = index;
                        }
                    }
                }
                var x = Intersection(p[i0], d[i0], p[o0], d[o0]);
                var y = Intersection(p[i0], d[i0], p[o1], d[o1]);
                var z = Intersection(p[i1], d[i1], p[o0], d[o0]);
                var w = Intersection(p[i1], d[i1], p[o1], d[o1]);
                Surface(field, up, x, y, z);
                Surface(field, up, y, w, z);
            }
        }

        private void Surface(Field field, Vector3 up, Vector3 a, Vector3 b, Vector3 c)
        {
            polygon.Clear();
            polygon.Add(a);
            polygon.Add(b);
            polygon.Add(c);
            for (int index = 0; index < field.Ends.Length && polygon.Count > 0; index++)
            {
                var direction = field.Directions[index];
                var end = field.Ends[index];
                var center = Vector3.zero;
                foreach (var p in polygon)
                {
                    center += p;
                }

                center /= polygon.Count;
                var delta = center - end;
                var radial = delta - direction * Vector3.Dot(delta, direction);
                if (radial.sqrMagnitude > field.Radius * field.Radius * 2.25f)
                {
                    continue;
                }

                clipped.Clear();
                var previous = polygon[polygon.Count - 1];
                float dp = Vector3.Dot(previous - end, direction);
                foreach (var p in polygon)
                {
                    float d = Vector3.Dot(p - end, direction);
                    if ((d <= 0) != (dp <= 0))
                    {
                        clipped.Add(Vector3.LerpUnclamped(previous, p, dp / (dp - d)));
                    }

                    if (d <= 0)
                    {
                        clipped.Add(p);
                    }

                    previous = p;
                    dp = d;
                }
                var swap = polygon;
                polygon = clipped;
                clipped = swap;
            }
            for (int index2 = 1; index2 + 1 < polygon.Count; index2++)
            {
                Face(field, up, polygon[0], polygon[index2], polygon[index2 + 1]);
            }
        }

        private void Face(Field field, Vector3 up, Vector3 a, Vector3 b, Vector3 c)
        {
            a = field.Snap(a);
            b = field.Snap(b);
            c = field.Snap(c);
            var face = Vector3.Cross(b - a, c - a);
            if (face.sqrMagnitude < 0.00000000001f)
            {
                return;
            }

            int ia = Vertex(field, up, a);

            int ib = Vertex(field, up, b);

            int ic = Vertex(field, up, c);
            bool reverse = Vector3.Dot(face, normals[ia] + normals[ib] + normals[ic]) < 0;
            Triangle(trim && MetalFittings ? 1 : 0, ia, reverse ? ic : ib, reverse ? ib : ic);
        }

        private int Vertex(Field field, Vector3 up, Vector3 point)
        {
            var key = new Vector3Int(
                Mathf.RoundToInt(point.x * 100000),
                Mathf.RoundToInt(point.y * 100000),
                Mathf.RoundToInt(point.z * 100000)
            );
            if (field.Vertices.TryGetValue(key, out int index))
            {
                return index;
            }

            index = vertices.Count;
            field.Vertices.Add(key, index);
            vertices.Add(point);
            normals.Add(field.Normal(point));
            uvs.Add(field.UV(point, up));
            return index;
        }

        private void Triangle(int material, int a, int b, int c)
        {
            indices[material].Add(a);
            indices[material].Add(b);
            indices[material].Add(c);
        }

        public Mesh Build(string name, bool separateGlass = false)
        {
            var positions = new List<Vector3>(vertices);
            var directions = new List<Vector3>(normals);
            var coordinates = new List<Vector2>(uvs);
            var opaque = new List<int>[indices.Length - 1];
            // 透过相邻玻璃管观察时，不透明接头需要朝向内侧的面。
            for (int material = 1; material < indices.Length; material++)
            {
                opaque[material - 1] = new List<int>(indices[material]);
                var inner = new Dictionary<int, int>();
                foreach (int vertex in indices[material])
                {
                    if (!inner.ContainsKey(vertex))
                    {
                        inner.Add(vertex, positions.Count);
                        positions.Add(vertices[vertex]);
                        directions.Add(-normals[vertex]);
                        coordinates.Add(uvs[vertex]);
                    }
                }
                for (int index = 0; index < indices[material].Count; index += 3)
                {
                    opaque[material - 1].Add(inner[indices[material][index]]);
                    opaque[material - 1].Add(inner[indices[material][index + 2]]);
                    opaque[material - 1].Add(inner[indices[material][index + 1]]);
                }
            }
            var mesh = new Mesh
            {
                name = name,
                indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(positions);
            mesh.SetNormals(directions);
            mesh.SetUVs(0, coordinates);
            mesh.subMeshCount = separateGlass ? 3 : 2;
            int machineSubMeshStart = mesh.subMeshCount;
            for (int surface = 1; surface < opaque.Length; surface++)
            {
                if (opaque[surface].Count == 0)
                {
                    continue;
                }
                int subMesh = machineSubMeshStart + surface - 1;
                mesh.subMeshCount = Math.Max(mesh.subMeshCount, subMesh + 1);
                mesh.SetTriangles(opaque[surface], subMesh);
            }
            if (separateGlass)
            {
                mesh.SetTriangles(indices[0], 0);
                mesh.SetTriangles(glassInterior, 2);
            }
            else
            {
                var glass = new List<int>(glassInterior);
                glass.AddRange(indices[0]);
                mesh.SetTriangles(glass, 0);
            }
            mesh.SetTriangles(opaque[0], 1);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
