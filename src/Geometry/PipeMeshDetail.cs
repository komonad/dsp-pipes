using System.Collections.Generic;
using UnityEngine;

namespace FluidPipes
{
    internal sealed partial class PipeMesh
    {
        private void SimplifyTubePath(
            ref IList<Vector3> points,
            ref IList<float> radii,
            ref IList<Vector3> tangents,
            ref IList<Vector3> frameUps,
            ref IList<float> textureCoordinates,
            ref IList<bool> visibleSegments
        )
        {
            if (points.Count < 3)
            {
                return;
            }

            float tolerance = detailLevel == 1 ? 0.025f : 0.09f;
            var rows = new List<int> { 0 };
            int first = 0;
            while (first < points.Count - 1)
            {
                int last = first + 1;
                for (int candidate = first + 2; candidate < points.Count; candidate++)
                {
                    Vector3 span = points[candidate] - points[first];
                    float lengthSquared = span.sqrMagnitude;
                    bool fits = lengthSquared > 0.0000001f;
                    for (int row = first + 1; fits && row < candidate; row++)
                    {
                        float fraction =
                            Vector3.Dot(points[row] - points[first], span) / lengthSquared;
                        fits =
                            fraction > 0
                            && fraction < 1
                            && Vector3.Distance(points[row], points[first] + span * fraction)
                                <= tolerance;
                        if (visibleSegments != null)
                        {
                            fits &=
                                visibleSegments[row - 1] == visibleSegments[first]
                                && visibleSegments[candidate - 1] == visibleSegments[first];
                        }
                        if (radii != null)
                        {
                            fits &=
                                Mathf.Abs(
                                    radii[row]
                                        - Mathf.Lerp(radii[first], radii[candidate], fraction)
                                )
                                <= tolerance * 0.02f;
                        }
                        if (frameUps != null)
                        {
                            Vector3 expected = Vector3
                                .Lerp(frameUps[first], frameUps[candidate], fraction)
                                .normalized;
                            fits &= Vector3.Dot(expected, frameUps[row].normalized) > 0.9998f;
                        }
                        if (textureCoordinates != null)
                        {
                            fits &=
                                Mathf.Abs(
                                    textureCoordinates[row]
                                        - Mathf.Lerp(
                                            textureCoordinates[first],
                                            textureCoordinates[candidate],
                                            fraction
                                        )
                                ) < 0.005f;
                        }
                    }
                    if (!fits)
                    {
                        break;
                    }
                    last = candidate;
                }
                rows.Add(last);
                first = last;
            }
            if (rows.Count == points.Count)
            {
                return;
            }
            points = SelectRows(points, rows);
            if (visibleSegments != null)
            {
                var simplifiedVisibility = new bool[rows.Count - 1];
                for (int index = 0; index < simplifiedVisibility.Length; index++)
                {
                    simplifiedVisibility[index] = visibleSegments[rows[index]];
                }
                visibleSegments = simplifiedVisibility;
            }
            radii = SelectRows(radii, rows);
            tangents = SelectRows(tangents, rows);
            frameUps = SelectRows(frameUps, rows);
            textureCoordinates = SelectRows(textureCoordinates, rows);
        }

        private static IList<T> SelectRows<T>(IList<T> source, List<int> rows)
        {
            if (source == null)
            {
                return null;
            }
            var result = new T[rows.Count];
            for (int index = 0; index < rows.Count; index++)
            {
                result[index] = source[rows[index]];
            }
            return result;
        }
    }
}
