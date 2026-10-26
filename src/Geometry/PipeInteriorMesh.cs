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
                float halfLength = 0.35f * geometry.Radius / Radius;
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

        private void InteriorPath(
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
                    member.Surface == PipeSurfaceRole.Core ? 0 : 1,
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

        private static Vector2 RectanglePoint(
            PipeInteriorSection section,
            int column,
            out Vector2 normal
        )
        {
            int perimeterColumn = column % Sides;
            int corner = perimeterColumn / 8;
            float fraction = (perimeterColumn % 8) / 7f;
            float angle = (corner + fraction) * Mathf.PI * 0.5f;
            normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float horizontal = corner == 0 || corner == 3 ? 1 : -1;
            float vertical = corner < 2 ? 1 : -1;
            return new Vector2(
                    horizontal * (section.WidthFraction * 0.5f - section.BevelFraction),
                    vertical * (section.HeightFraction * 0.5f - section.BevelFraction)
                )
                + normal * section.BevelFraction;
        }

        private static float RectanglePerimeterCoordinate(PipeInteriorSection section, int column)
        {
            if (column == Sides)
            {
                return 1;
            }

            int corner = column / 8;
            float cornerFraction = (column % 8) / 7f;
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
            float radius
        )
        {
            int start = vertices.Count;
            Vector3 side = (vertices[ring] - center).normalized;
            Vector3 transverse = Vector3.Cross(normal, side).normalized;
            vertices.Add(center);
            normals.Add(normal);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int column = 0; column <= Sides; column++)
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
            for (int column = 0; column < Sides; column++)
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
