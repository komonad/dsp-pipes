using System;
using System.Collections.Generic;
using FluidPipes.API;
using UnityEngine;

namespace FluidPipes
{
    internal sealed partial class PipeMesh
    {
        internal void InteriorNode(
            Vector3 center,
            Vector3 up,
            Vector3 forward,
            IList<Vector3> ends,
            IList<Vector3> endAxes,
            IList<Vector3> endUps,
            PipeInteriorProfile profile,
            int outputEnd = -2
        )
        {
            if (ends.Count == 0)
            {
                float halfLength = geometry.IsolatedHalfLength;
                InteriorPath(
                    new[] { center - forward * halfLength, center + forward * halfLength },
                    up,
                    null,
                    null,
                    profile,
                    true,
                    true
                );
                return;
            }
            if (ends.Count == 2)
            {
                int first = outputEnd == 0 ? 1 : 0;
                int last = 1 - first;
                int samples = NeedsJointFittings(center, ends) ? 17 : 2;
                var points = new Vector3[samples];
                for (int sample = 0; sample < samples; sample++)
                {
                    float fraction = (float)sample / (samples - 1);
                    points[sample] =
                        (1 - fraction) * (1 - fraction) * ends[first]
                        + 2 * fraction * (1 - fraction) * center
                        + fraction * fraction * ends[last];
                }
                var axes = new Vector3[samples];
                axes[0] = endAxes == null ? center - ends[first] : -endAxes[first];
                axes[samples - 1] = endAxes == null ? ends[last] - center : endAxes[last];
                var frames = new Vector3[samples];
                frames[0] = endUps == null ? up : endUps[first];
                frames[samples - 1] = endUps == null ? up : endUps[last];
                InteriorPath(points, up, axes, frames, profile, false, false);
                return;
            }
            for (int endIndex = 0; endIndex < ends.Count; endIndex++)
            {
                Vector3 tangent = endAxes == null ? ends[endIndex] - center : endAxes[endIndex];
                Vector3 endUp = endUps == null ? up : endUps[endIndex];
                bool inward = outputEnd != -2 && outputEnd != endIndex;
                InteriorPath(
                    inward ? new[] { ends[endIndex], center } : new[] { center, ends[endIndex] },
                    up,
                    inward ? new[] { -tangent, -tangent } : new[] { tangent, tangent },
                    inward ? new[] { endUp, up } : new[] { up, endUp },
                    profile,
                    ends.Count == 1 && !inward,
                    ends.Count == 1 && inward
                );
            }
        }

        internal void InteriorPath(
            IList<Vector3> points,
            Vector3 up,
            IList<Vector3> axes,
            IList<Vector3> frameUps,
            PipeInteriorProfile profile,
            bool capStart,
            bool capEnd
        )
        {
            var sides = new Vector3[points.Count];
            var transverse = new Vector3[points.Count];
            var tangents = new Vector3[points.Count];
            var frames = new Vector3[points.Count];
            Vector3 previousSide = Vector3.zero;
            Vector3 previousTangent = Vector3.zero;
            for (int row = 0; row < points.Count; row++)
            {
                Vector3 tangent = PathTangent(points, axes, row, previousTangent);
                bool shared = frameUps != null && frameUps[row].sqrMagnitude > 0.0000001f;
                Vector3 side = FrameSide(
                    tangent,
                    shared ? frameUps[row] : up,
                    previousSide,
                    shared
                );
                sides[row] = side;
                transverse[row] = Vector3.Cross(tangent, side).normalized;
                tangents[row] = tangent;
                frames[row] = -transverse[row];
                previousSide = side;
                previousTangent = tangent;
            }
            var memberPoints = new Vector3[points.Count];
            foreach (var member in profile.Members)
            {
                if (member.Pattern != null)
                {
                    PatternedMember(points, tangents, frames, member, up, capStart, capEnd);
                    continue;
                }
                for (int row = 0; row < points.Count; row++)
                {
                    memberPoints[row] =
                        points[row]
                        + radius
                            * (sides[row] * member.Offset.x - transverse[row] * member.Offset.y);
                }
                Tube(
                    memberPoints,
                    up,
                    radius * member.RadiusFraction,
                    InteriorMaterial(member.Surface),
                    capStart,
                    capEnd,
                    null,
                    tangents,
                    frames,
                    false,
                    member.Section
                );
            }
        }

        private void PatternedMember(
            IList<Vector3> points,
            IList<Vector3> tangents,
            IList<Vector3> frames,
            PipeInteriorMember member,
            Vector3 up,
            bool capStart,
            bool capEnd
        )
        {
            var pattern = member.Pattern;
            var memberPoints = new List<Vector3>();
            var memberTangents = new List<Vector3>();
            var memberFrames = new List<Vector3>();
            var radii = new List<float>();
            var fractions = new List<float>();
            bool hasVisibleGeometry = false;
            var textureCoordinates = pattern.TexturePhaseSpan > 0 ? new List<float>() : null;
            for (int segment = 1; segment < points.Count; segment++)
            {
                float firstPhase = pattern.PhaseAt(
                    patternFactory,
                    points[segment - 1] + textureOrigin
                );
                float delta =
                    pattern.PhaseAt(patternFactory, points[segment] + textureOrigin) - firstPhase;
                delta -= Mathf.Round(delta);
                fractions.Clear();
                if (segment == 1)
                {
                    fractions.Add(0);
                }
                fractions.Add(1);
                if (Mathf.Abs(delta) > 0.000001f)
                {
                    foreach (var keyframe in pattern.Keyframes)
                    {
                        if (keyframe.Phase == 1)
                        {
                            continue;
                        }
                        for (int cycle = -1; cycle <= 1; cycle++)
                        {
                            float fraction = (keyframe.Phase + cycle - firstPhase) / delta;
                            if (fraction > 0.00001f && fraction < 0.99999f)
                            {
                                fractions.Add(fraction);
                            }
                        }
                    }
                }
                fractions.Sort();
                foreach (float fraction in fractions)
                {
                    Vector3 center = Vector3.Lerp(points[segment - 1], points[segment], fraction);
                    Vector3 tangent = Vector3
                        .Lerp(tangents[segment - 1], tangents[segment], fraction)
                        .normalized;
                    Vector3 frameUp = Vector3
                        .Lerp(frames[segment - 1], frames[segment], fraction)
                        .normalized;
                    Vector3 side = Vector3.Cross(tangent, frameUp).normalized;
                    pattern.Sample(
                        firstPhase + delta * fraction,
                        out float radialOffset,
                        out float scale
                    );
                    if (scale < 0.00001f)
                    {
                        scale = 0;
                    }
                    memberPoints.Add(
                        center
                            + radius
                                * (
                                    side * member.Offset.x
                                    + frameUp * (member.Offset.y + radialOffset)
                                )
                    );
                    const float derivativeStep = 0.001f;
                    pattern.Sample(
                        pattern.PhaseAt(
                            patternFactory,
                            center + textureOrigin + tangent * derivativeStep
                        ),
                        out float nextOffset,
                        out _
                    );
                    pattern.Sample(
                        pattern.PhaseAt(
                            patternFactory,
                            center + textureOrigin - tangent * derivativeStep
                        ),
                        out float previousOffset,
                        out _
                    );
                    memberTangents.Add(
                        (
                            tangent
                            + frameUp
                                * (radius * (nextOffset - previousOffset) / (2 * derivativeStep))
                        ).normalized
                    );
                    memberFrames.Add(frameUp);
                    radii.Add(radius * member.RadiusFraction * scale);
                    hasVisibleGeometry |= scale > 0;
                    textureCoordinates?.Add(
                        Mathf.Repeat(firstPhase + delta * fraction - pattern.TextureStartPhase, 1)
                            / pattern.TexturePhaseSpan
                    );
                }
            }
            if (!hasVisibleGeometry)
            {
                return;
            }
            var visibleSegments = new bool[radii.Count - 1];
            for (int segment = 0; segment < visibleSegments.Length; segment++)
            {
                visibleSegments[segment] = radii[segment] > 0 || radii[segment + 1] > 0;
            }
            Tube(
                memberPoints,
                up,
                radius * member.RadiusFraction,
                InteriorMaterial(member.Surface),
                capStart,
                capEnd,
                radii,
                memberTangents,
                memberFrames,
                false,
                member.Section,
                visibleSegments,
                textureCoordinates: textureCoordinates
            );
        }

        private static int InteriorMaterial(PipeSurfaceRole surface) =>
            surface == PipeSurfaceRole.Detail ? 4
            : surface == PipeSurfaceRole.Fittings ? 1
            : 0;

        private void AnnularTube(
            IList<Vector3> points,
            Vector3 up,
            float outerRadius,
            int material,
            bool capStart,
            bool capEnd,
            IList<float> radii,
            IList<Vector3> tangents,
            IList<Vector3> frames,
            PipeInteriorSection section,
            IList<float> textureCoordinates
        )
        {
            int radialSegments =
                detailLevel > 0 ? (detailLevel == 1 ? 32 : 24)
                : outerRadius >= 0.5f ? 96
                : Sides;
            float ratio = section.InnerRadiusFraction / section.RadiusFraction;
            var outerSection = PipeInteriorSection.Circle(section.RadiusFraction);
            float[] outerRadii = null;
            bool[] visibleSegments = null;
            if (radii != null)
            {
                outerRadii = new float[radii.Count];
                visibleSegments = new bool[radii.Count - 1];
                for (int index = 0; index < radii.Count; index++)
                {
                    float scale = radii[index] / outerRadius;
                    outerRadii[index] = outerRadius * ratio + outerRadius * (1 - ratio) * scale;
                    if (index > 0)
                    {
                        visibleSegments[index - 1] =
                            radii[index - 1] > 0.000001f || radii[index] > 0.000001f;
                    }
                }
            }
            int outerStart = vertices.Count;
            Tube(
                points,
                up,
                outerRadius,
                material,
                false,
                false,
                outerRadii,
                tangents,
                frames,
                false,
                outerSection,
                visibleSegments,
                textureCoordinates,
                radialSegments
            );
            int innerStart = vertices.Count;
            int innerIndexStart = indices[material].Count;
            float[] innerRadii = null;
            if (radii != null)
            {
                innerRadii = new float[radii.Count];
                for (int index = 0; index < radii.Count; index++)
                {
                    innerRadii[index] = outerRadius * ratio;
                }
            }
            Tube(
                points,
                up,
                outerRadius * ratio,
                material,
                false,
                false,
                innerRadii,
                tangents,
                frames,
                false,
                outerSection,
                visibleSegments,
                textureCoordinates,
                radialSegments
            );
            for (int index = innerStart; index < normals.Count; index++)
            {
                normals[index] = -normals[index];
            }
            for (int index = innerIndexStart; index < indices[material].Count; index += 3)
            {
                int second = indices[material][index + 1];
                indices[material][index + 1] = indices[material][index + 2];
                indices[material][index + 2] = second;
            }
            if (capStart)
            {
                Vector3 tangent = PathTangent(points, tangents, 0, Vector3.zero);
                Annulus(
                    points[0],
                    -tangent,
                    outerStart,
                    outerRadius * ratio,
                    material,
                    radialSegments
                );
            }
            if (capEnd)
            {
                int last = points.Count - 1;
                Vector3 tangent = PathTangent(points, tangents, last, Vector3.zero);
                Annulus(
                    points[last],
                    tangent,
                    outerStart + last * (radialSegments + 1),
                    outerRadius * ratio,
                    material,
                    radialSegments
                );
            }
        }

        private static Vector2 RectanglePoint(
            PipeInteriorSection section,
            int column,
            int radialSegments,
            out Vector2 normal
        )
        {
            int cornerSamples = radialSegments / 4;
            int perimeterColumn = column % radialSegments;
            int corner = perimeterColumn / cornerSamples;
            float fraction = (float)(perimeterColumn % cornerSamples) / (cornerSamples - 1);
            float angle = (corner + fraction) * Mathf.PI * 0.5f;
            normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float horizontal = corner == 0 || corner == 3 ? 1 : -1;
            float vertical = corner < 2 ? 1 : -1;
            Vector2 point =
                new Vector2(
                    horizontal * (section.WidthFraction * 0.5f - section.BevelFraction),
                    vertical * (section.HeightFraction * 0.5f - section.BevelFraction)
                )
                + normal * section.BevelFraction;
            float rotation = section.RotationDegrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(rotation);
            float sine = Mathf.Sin(rotation);
            normal = new Vector2(
                normal.x * cosine - normal.y * sine,
                normal.x * sine + normal.y * cosine
            );
            return new Vector2(
                point.x * cosine - point.y * sine,
                point.x * sine + point.y * cosine
            );
        }

        private static float RectanglePerimeterCoordinate(
            PipeInteriorSection section,
            int column,
            int radialSegments
        )
        {
            if (column == radialSegments)
            {
                return 1;
            }

            int cornerSamples = radialSegments / 4;
            int corner = column / cornerSamples;
            float cornerFraction = (float)(column % cornerSamples) / (cornerSamples - 1);
            float cornerLength = section.BevelFraction * Mathf.PI * 0.5f;
            float horizontalLength = section.WidthFraction - 2 * section.BevelFraction;
            float verticalLength = section.HeightFraction - 2 * section.BevelFraction;
            float perimeter = 4 * cornerLength + 2 * (horizontalLength + verticalLength);
            float distance =
                (corner + cornerFraction) * cornerLength
                + ((corner + 1) / 2) * horizontalLength
                + (corner / 2) * verticalLength;
            return distance / perimeter;
        }

        private void SectionCap(
            Vector3 center,
            Vector3 normal,
            int ring,
            int material,
            float radius,
            int radialSegments
        )
        {
            int start = vertices.Count;
            Vector3 side = (vertices[ring] - center).normalized;
            Vector3 transverse = Vector3.Cross(normal, side).normalized;
            vertices.Add(center);
            normals.Add(normal);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int column = 0; column <= radialSegments; column++)
            {
                Vector3 radial = vertices[ring + column] - center;
                vertices.Add(center + radial);
                normals.Add(normal);
                uvs.Add(
                    new Vector2(
                        0.5f + Vector3.Dot(radial, side) / (radius * 2),
                        0.5f + Vector3.Dot(radial, transverse) / (radius * 2)
                    )
                );
            }
            for (int column = 0; column < radialSegments; column++)
            {
                OrientedTriangle(material, start, start + column + 1, start + column + 2);
            }
        }

        private static Vector3 PathTangent(
            IList<Vector3> points,
            IList<Vector3> tangents,
            int row,
            Vector3 previousTangent
        )
        {
            Vector3 tangent =
                tangents != null && tangents[row].sqrMagnitude > 0.0000001f
                    ? tangents[row]
                    : points[Math.Min(row + 1, points.Count - 1)] - points[Math.Max(0, row - 1)];
            if (tangent.sqrMagnitude == 0)
            {
                tangent = previousTangent.sqrMagnitude > 0 ? previousTangent : Vector3.forward;
            }
            return tangent / tangent.magnitude;
        }

        private static Vector3 FrameSide(
            Vector3 tangent,
            Vector3 up,
            Vector3 previousSide,
            bool shared
        )
        {
            Vector3 side =
                shared ? Vector3.Cross(tangent, up)
                : previousSide.sqrMagnitude > 0
                    ? previousSide - tangent * Vector3.Dot(previousSide, tangent)
                : Vector3.Cross(tangent, up);
            if (side.sqrMagnitude < 0.001f)
            {
                side = Vector3.Cross(tangent, Vector3.right);
            }
            if (side.sqrMagnitude < 0.001f)
            {
                side = Vector3.Cross(tangent, Vector3.forward);
            }
            return side.normalized;
        }
    }
}
