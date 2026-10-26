using System;
using System.Collections.Generic;
using System.Linq;
using FluidPipes;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;

public sealed partial class RuntimeChecks
{
    private void PipeJointOwnership()
    {
        for (int scenario = 0; scenario < 4; scenario++)
        {
            var factory = Fixture(4, 981013 + scenario, false);
            factory.entityPool[1].pos = new Vector3(-1, 200, 0);
            factory.entityPool[2].pos = new Vector3(1, 200, 0);
            factory.entityPool[3].pos =
                scenario == 1 ? new Vector3(-3, 200, 0) : new Vector3(-1, 200, 2);
            factory.entityPool[4].pos = new Vector3(1, 200, -2);
            int wideEntityId = scenario >= 2 ? scenario - 1 : 0;
            if (wideEntityId > 0)
            {
                SetTypeCheckPrototype(factory, wideEntityId, "shared-variant");
            }
            factory.WriteObjectConn(1, 0, true, 2, 1);
            factory.WriteObjectConn(3, 0, true, 1, 1);
            factory.WriteObjectConn(2, 0, true, 4, 1);
            int ownerEntityId =
                wideEntityId > 0 ? wideEntityId
                : scenario == 1 ? 2
                : 1;
            for (int entityId = 1; entityId <= 2; entityId++)
            {
                var shape = AccessTools
                    .Method(typeof(PipeVisuals), "Shape")
                    .Invoke(
                        null,
                        new object[] { factory, entityId, factory.entityPool[entityId].pos }
                    );
                var ends = JointShapeField<List<Vector3>>(shape, "Ends");
                var collars = JointShapeField<List<bool>>(shape, "Collars");
                var axes = JointShapeField<List<Vector3>>(shape, "EndAxes");
                var profile =
                    entityId == wideEntityId
                        ? new PipeGeometryProfile(0.46f, 0.6f)
                        : PipeGeometryProfile.Default;
                int sharedEndIndex = Enumerable
                    .Range(0, ends.Count)
                    .OrderBy(index => Mathf.Abs(ends[index].z))
                    .First();
                bool ownsCollar = entityId == ownerEntityId;
                Check(
                    collars[sharedEndIndex] == ownsCollar,
                    "junction "
                        + scenario
                        + " assigns its shared fitting to exactly the intended endpoint "
                        + entityId
                );
                var actual = JointShapeMesh(shape, profile);
                Mesh withoutSharedCollar = null;
                try
                {
                    collars[sharedEndIndex] = false;
                    withoutSharedCollar = JointShapeMesh(shape, profile);
                    int fittingTriangleDifference =
                        actual.GetTriangles(1).Length - withoutSharedCollar.GetTriangles(1).Length;
                    if (wideEntityId == 0)
                    {
                        Check(
                            ownsCollar
                                ? fittingTriangleDifference > 0
                                : fittingTriangleDifference == 0,
                            "same-radius adjacent geometry contributes one shared metal collar for endpoint "
                                + entityId
                        );
                    }
                    else
                    {
                        Check(
                            fittingTriangleDifference == 0,
                            "radius-transition geometry contains its reducer and smooth peer without an overlapping collar"
                        );
                        CheckJointMouth(
                            actual,
                            ends[sharedEndIndex],
                            axes[sharedEndIndex],
                            entityId == wideEntityId
                        );
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(actual);
                    if (withoutSharedCollar != null)
                    {
                        UnityEngine.Object.DestroyImmediate(withoutSharedCollar);
                    }
                }
            }
        }
    }

    private static T JointShapeField<T>(object shape, string name)
    {
        return (T)AccessTools.Field(shape.GetType(), name).GetValue(shape);
    }

    private static Mesh JointShapeMesh(object shape, PipeGeometryProfile profile)
    {
        var builder = new PipeMesh(profile.Radius, geometry: profile);
        AccessTools.Method(shape.GetType(), "AddTo").Invoke(shape, new object[] { builder, false });
        return builder.Build("type-joint-fitting-check");
    }

    private void CheckJointMouth(
        Mesh mesh,
        Vector3 endpoint,
        Vector3 outwardAxis,
        bool wideEndpoint
    )
    {
        outwardAxis.Normalize();
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        var mouthRadii = mesh.GetTriangles(1)
            .Distinct()
            .Where(index =>
                Mathf.Abs(Vector3.Dot(vertices[index] - endpoint, outwardAxis)) < 0.0001f
                && Vector3.Dot(normals[index], outwardAxis) > 0.999f
            )
            .Select(index =>
                Vector3.ProjectOnPlane(vertices[index] - endpoint, outwardAxis).magnitude
            )
            .ToArray();
        Check(
            wideEndpoint
                ? mouthRadii.Length > 0
                    && mouthRadii.Min() > 0.32f * 0.95f
                    && mouthRadii.Min() < 0.32f
                    && mouthRadii.Max() > 0.46f
                : mouthRadii.Length == 0,
            wideEndpoint
                ? "the wider bend has a closed annular mouth sized for the narrower connected pipe"
                : "the narrower bend reaches the shared plane without a second annular fitting"
        );
    }
}
