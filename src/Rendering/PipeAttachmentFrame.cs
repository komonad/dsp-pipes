using UnityEngine;

namespace FluidPipes
{
    internal static partial class PipeVisuals
    {
        internal static bool TryGetAttachmentFrame(
            PlanetFactory factory,
            int entityId,
            Vector3 nearPosition,
            out Vector3 center,
            out Vector3 tangent
        )
        {
            center = Vector3.zero;
            tangent = Vector3.zero;
            if (entityId <= 0 || !PipeRuntime.IsPipeObject(factory, entityId))
            {
                return false;
            }

            Initialize(factory);
            var node = GetNodeGeometry(factory, entityId);
            var shape = node.Shape;
            // Native inserter anchors follow cargo-path samples; the clamp follows rendered geometry.
            var localQuery = nearPosition - shape.Position;
            var dimensions = PipeTypeRegistry.Geometry(node.ItemId);
            float closestDistance = float.MaxValue;
            float closestAlignment = -1;
            Vector3 localCenter = shape.Center;
            Vector3 localTangent = shape.Forward;

            if (shape.Ends.Count == 0)
            {
                float halfLength = 0.35f * dimensions.Radius / PipeMesh.Radius;
                ConsiderAttachmentSegment(
                    localQuery,
                    shape.Center - shape.Forward * halfLength,
                    shape.Center + shape.Forward * halfLength,
                    shape.Forward,
                    shape.Forward,
                    shape.Forward,
                    ref closestDistance,
                    ref closestAlignment,
                    ref localCenter,
                    ref localTangent
                );
            }
            else if (shape.Ends.Count == 2)
            {
                bool bend = PipeMesh.NeedsJointFittings(shape.Center, shape.Ends);
                int samples = bend ? 17 : 2;
                Vector3 previous = shape.Ends[0];
                Vector3 previousTangent = -shape.EndAxes[0];
                for (int sample = 1; sample < samples; sample++)
                {
                    float fraction = (float)sample / (samples - 1);
                    float remaining = 1 - fraction;
                    Vector3 point = bend
                        ? remaining * remaining * shape.Ends[0]
                            + 2 * fraction * remaining * shape.Center
                            + fraction * fraction * shape.Ends[1]
                        : shape.Ends[1];
                    Vector3 nextTangent;
                    if (sample == samples - 1)
                    {
                        nextTangent = shape.EndAxes[1];
                    }
                    else
                    {
                        nextTangent = (
                            remaining * (shape.Center - shape.Ends[0])
                            + fraction * (shape.Ends[1] - shape.Center)
                        ).normalized;
                    }
                    ConsiderAttachmentSegment(
                        localQuery,
                        previous,
                        point,
                        previousTangent,
                        nextTangent,
                        shape.Forward,
                        ref closestDistance,
                        ref closestAlignment,
                        ref localCenter,
                        ref localTangent
                    );
                    previous = point;
                    previousTangent = nextTangent;
                }
            }
            else
            {
                for (int branch = 0; branch < shape.Ends.Count; branch++)
                {
                    Vector3 branchAxis = shape.EndAxes[branch];
                    ConsiderAttachmentSegment(
                        localQuery,
                        shape.Center,
                        shape.Ends[branch],
                        branchAxis,
                        branchAxis,
                        shape.Forward,
                        ref closestDistance,
                        ref closestAlignment,
                        ref localCenter,
                        ref localTangent
                    );
                }
            }

            center = localCenter + shape.Position;
            tangent = localTangent.normalized;
            return tangent.sqrMagnitude > 0.5f;
        }

        private static void ConsiderAttachmentSegment(
            Vector3 query,
            Vector3 begin,
            Vector3 end,
            Vector3 beginTangent,
            Vector3 endTangent,
            Vector3 preferredTangent,
            ref float closestDistance,
            ref float closestAlignment,
            ref Vector3 closestPoint,
            ref Vector3 tangent
        )
        {
            Vector3 segment = end - begin;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 0.0000001f)
            {
                return;
            }
            float fraction = Mathf.Clamp01(Vector3.Dot(query - begin, segment) / lengthSquared);
            Vector3 point = begin + segment * fraction;
            float distance = (query - point).sqrMagnitude;
            Vector3 direction = Vector3.Lerp(beginTangent, endTangent, fraction).normalized;
            float alignment = Mathf.Abs(Vector3.Dot(direction, preferredTangent));
            // Equal-distance branches use the saved path tangent to keep the attachment stable.
            if (
                distance < closestDistance - 0.0000001f
                || Mathf.Abs(distance - closestDistance) <= 0.0000001f
                    && alignment > closestAlignment
            )
            {
                closestDistance = distance;
                closestAlignment = alignment;
                closestPoint = point;
                tangent = direction;
            }
        }
    }
}
