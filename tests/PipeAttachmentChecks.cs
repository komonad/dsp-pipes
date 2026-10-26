using System;
using System.Collections.Generic;
using FluidPipes;
using FluidPipes.API;
using UnityEngine;

public sealed partial class RuntimeChecks
{
    private void PipeAttachmentGeometry()
    {
        var profiles = new List<KeyValuePair<int, PipeGeometryProfile>>
        {
            new KeyValuePair<int, PipeGeometryProfile>(
                PipeApi.PipeItemId,
                PipeGeometryProfile.Default
            ),
            new KeyValuePair<int, PipeGeometryProfile>(
                typeCheckItems["shared-variant"].ID,
                new PipeGeometryProfile(0.46f, 0.6f)
            ),
        };
        if (PipeTypes.TryGet("example.copperpipes:copper", out var copper))
        {
            profiles.Add(
                new KeyValuePair<int, PipeGeometryProfile>(
                    copper.ItemId,
                    PipeGeometryProfile.Default
                )
            );
        }
        try
        {
            foreach (var profile in profiles)
            {
                var factory = AttachmentFixture(profile.Key, 3, 981040);
                factory.entityPool[2].pos += Vector3.right * 2;
                factory.entityPool[3].pos += Vector3.left * 2;
                factory.WriteObjectConn(1, 0, true, 2, 1);
                factory.WriteObjectConn(3, 0, true, 1, 1);
                foreach (float offset in new[] { -0.7f, 0.65f })
                {
                    Vector3 nativePosition = new Vector3(offset, 200.13f, 0.16f);
                    var socket = FluidInserterVisuals.ResolveSocket(
                        factory,
                        1,
                        -1,
                        nativePosition,
                        nativePosition + Vector3.forward * 3
                    );
                    Vector3 expectedCenter = new Vector3(
                        offset,
                        200 + profile.Value.CenterHeight,
                        0
                    );
                    Check(
                        socket.Clamp
                            && Vector3.Distance(socket.Center, expectedCenter) < 0.003f
                            && Mathf.Abs(Vector3.Dot(socket.Axis, Vector3.right)) > 0.999f
                            && Mathf.Abs(socket.PipeRadius - profile.Value.Radius) < 0.00001f,
                        "off-node inserter attachment follows the rendered centerline, height and radius for item "
                            + profile.Key
                            + " at "
                            + offset
                    );
                    Check(
                        Mathf.Abs(Vector3.Dot(socket.Axis, socket.Normal)) < 0.0001f
                            && Mathf.Abs(
                                Vector3.Distance(socket.Position, socket.Center)
                                    - profile.Value.Radius
                                    - 0.14f
                            ) < 0.0001f,
                        "off-node attachment keeps its pipe clamp normal and neck length"
                    );
                }
            }
            PipeCurvedAttachmentGeometry();
            PipeBranchedAttachmentGeometry();
            PipeIsolatedAttachmentGeometry();
        }
        finally
        {
            PipeVisuals.Clear();
            PipeRuntime.Clear();
        }
    }

    private static PlanetFactory AttachmentFixture(int itemId, int size, int planetId)
    {
        var factory = Fixture(size, planetId, false);
        for (int entityId = 1; entityId <= size; entityId++)
        {
            factory.entityPool[entityId].protoId = (short)itemId;
            factory.entityPool[entityId].modelIndex = (short)LDB.items.Select(itemId).ModelIndex;
            factory.entityPool[entityId].pos = new Vector3(0, 200, 0);
            factory.entityPool[entityId].rot = Quaternion.LookRotation(Vector3.right, Vector3.up);
        }
        return factory;
    }

    private void PipeCurvedAttachmentGeometry()
    {
        var factory = AttachmentFixture(PipeApi.PipeItemId, 3, 981041);
        factory.entityPool[2].pos += Vector3.right * 2;
        factory.entityPool[3].pos += Vector3.forward * 2;
        factory.WriteObjectConn(1, 0, true, 2, 1);
        factory.WriteObjectConn(3, 0, true, 1, 1);
        Mesh mesh = PipeVisuals.SelectionGeometry(factory, 1).Build("attachment-curve-check");
        try
        {
            var vertices = mesh.vertices;
            const int circumferentialSamples = 32;
            const int ringStride = circumferentialSamples + 1;
            foreach (int ring in new[] { 4, 8, 12 })
            {
                Vector3 center = Vector3.zero;
                for (int sample = 0; sample < circumferentialSamples; sample++)
                {
                    center += vertices[ring * ringStride + sample];
                }
                center /= circumferentialSamples;
                center += factory.entityPool[1].pos;
                int first = ring * ringStride;
                Vector3 tangent = Vector3
                    .Cross(
                        vertices[first + 8] - vertices[first],
                        vertices[first + 16] - vertices[first]
                    )
                    .normalized;
                Check(
                    PipeVisuals.TryGetAttachmentFrame(
                        factory,
                        1,
                        center,
                        out Vector3 attachedCenter,
                        out Vector3 attachedTangent
                    )
                        && Vector3.Distance(attachedCenter, center) < 0.001f
                        && Mathf.Abs(Vector3.Dot(attachedTangent, tangent)) > 0.999f,
                    "curved attachment matches the actual rendered cross-section center and tangent at ring "
                        + ring
                );
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private void PipeBranchedAttachmentGeometry()
    {
        var factory = AttachmentFixture(PipeApi.PipeItemId, 4, 981042);
        factory.entityPool[2].pos += Vector3.right * 2;
        factory.entityPool[3].pos += Vector3.left * 2;
        factory.entityPool[4].pos += Vector3.forward * 2;
        factory.WriteObjectConn(1, 0, true, 2, 1);
        factory.WriteObjectConn(3, 0, true, 1, 1);
        factory.WriteObjectConn(4, 0, true, 1, 2);
        Vector3 query = new Vector3(0.12f, 200.18f, 0.8f);
        var socket = FluidInserterVisuals.ResolveSocket(
            factory,
            1,
            -1,
            query,
            query + Vector3.left * 3
        );
        Check(
            Vector3.Distance(socket.Center, new Vector3(0, 200.34f, 0.8f)) < 0.003f
                && Mathf.Abs(Vector3.Dot(socket.Axis, Vector3.forward)) > 0.999f,
            "junction attachment chooses the nearby branch independently of native slot ordering"
        );
    }

    private void PipeIsolatedAttachmentGeometry()
    {
        var factory = AttachmentFixture(PipeApi.PipeItemId, 1, 981043);
        Check(
            PipeVisuals.TryGetAttachmentFrame(
                factory,
                1,
                new Vector3(5, 200.2f, 0),
                out Vector3 center,
                out Vector3 tangent
            )
                && Vector3.Distance(center, new Vector3(0.35f, 200.34f, 0)) < 0.0001f
                && Vector3.Dot(tangent, Vector3.right) > 0.999f,
            "isolated attachment clamps its projection to the visible short pipe segment"
        );
    }
}
