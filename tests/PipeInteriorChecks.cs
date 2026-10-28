using System;
using System.Collections.Generic;
using System.Linq;
using FluidPipes;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;

public sealed partial class RuntimeChecks
{
    private static PipeInteriorProfile TypeCheckInterior()
    {
        return new PipeInteriorProfile(
            new[]
            {
                new PipeInteriorMember(new Vector2(-0.42f, 0), 0.1f, PipeSurfaceRole.Fittings),
                new PipeInteriorMember(new Vector2(0.42f, 0), 0.1f, PipeSurfaceRole.Fittings),
                new PipeInteriorMember(Vector2.zero, 0.15f),
                new PipeInteriorMember(new Vector2(0, -0.36f), 0.08f),
                new PipeInteriorMember(new Vector2(0, 0.36f), 0.08f),
            }
        );
    }

    private void PipeInteriorChecks(Camera camera)
    {
        PipeInteriorArguments();
        PipeInteriorDefaultCore();
        PipeInteriorCurvedNetwork(camera);
        PipeInteriorDirectedEnds();
    }

    private static PipeInteriorProfile TypeCheckAsymmetricInterior()
    {
        return new PipeInteriorProfile(
            new[]
            {
                new PipeInteriorMember(new Vector2(0.26f, 0.18f), 0.12f),
                new PipeInteriorMember(new Vector2(-0.3f, -0.1f), 0.1f, PipeSurfaceRole.Fittings),
            }
        );
    }

    private void PipeInteriorArguments()
    {
        foreach (
            float invalidValue in new[]
            {
                float.NaN,
                float.NegativeInfinity,
                float.PositiveInfinity,
            }
        )
        {
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeInteriorMember(new Vector2(invalidValue, 0), 0.1f),
                "interior member offsets require finite horizontal coordinates"
            );
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeInteriorMember(new Vector2(0, invalidValue), 0.1f),
                "interior member offsets require finite vertical coordinates"
            );
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeInteriorMember(Vector2.zero, invalidValue),
                "interior member radii require finite values"
            );
        }
        foreach (float invalidRadius in new[] { -0.1f, 0f, 0.009f, 0.851f })
        {
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeInteriorMember(Vector2.zero, invalidRadius),
                "interior radius fractions stay within their supported range"
            );
        }
        foreach (
            PipeSurfaceRole invalidSurface in new[]
            {
                PipeSurfaceRole.Wall,
                PipeSurfaceRole.InnerWall,
                PipeSurfaceRole.MachinePanel,
                (PipeSurfaceRole)int.MaxValue,
            }
        )
        {
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeInteriorMember(Vector2.zero, 0.1f, invalidSurface),
                "interior members use conductor or fitting material roles"
            );
        }
        ExpectTypeException<ArgumentOutOfRangeException>(
            () => new PipeInteriorMember(new Vector2(0.85f, 0), 0.1f),
            "interior members retain clearance from the surrounding wall"
        );
        Check(
            new PipeInteriorMember(Vector2.zero, 0.01f).RadiusFraction == 0.01f
                && new PipeInteriorMember(Vector2.zero, 0.85f).RadiusFraction == 0.85f,
            "interior radius limits are inclusive"
        );
        ExpectTypeException<ArgumentNullException>(
            () => new PipeInteriorProfile(null),
            "interior profiles require a member collection"
        );
        ExpectTypeException<ArgumentException>(
            () => new PipeInteriorProfile(Array.Empty<PipeInteriorMember>()),
            "interior profiles require at least one member"
        );
        ExpectTypeException<ArgumentException>(
            () => new PipeInteriorProfile(new PipeInteriorMember[] { null }),
            "interior profile collections contain defined members"
        );
        var centralMember = new PipeInteriorMember(Vector2.zero, 0.12f);
        ExpectTypeException<ArgumentException>(
            () => new PipeInteriorProfile(Enumerable.Repeat(centralMember, 17)),
            "interior profiles bound their per-node geometry complexity"
        );
        Check(
            new PipeInteriorProfile(Enumerable.Repeat(centralMember, 16)).Members.Count == 16,
            "interior profiles accept the maximum supported member count"
        );
        var source = new List<PipeInteriorMember> { centralMember };
        var snapshot = new PipeInteriorProfile(source);
        source[0] = new PipeInteriorMember(Vector2.zero, 0.3f);
        source.Clear();
        Check(
            snapshot.Members.Count == 1 && ReferenceEquals(snapshot.Members[0], centralMember),
            "interior profiles retain a detached snapshot of their input collection"
        );
        if (snapshot.Members is IList<PipeInteriorMember> mutableView)
        {
            ExpectTypeException<NotSupportedException>(
                () => mutableView[0] = new PipeInteriorMember(Vector2.zero, 0.2f),
                "interior member views preserve the captured profile"
            );
        }
        var appearance = new PipeAppearance(interior: snapshot);
        Check(
            appearance.HasCore
                && ReferenceEquals(appearance.Interior, snapshot)
                && new PipeAppearance().Interior == null,
            "an interior profile enables its decorative geometry while omitted profiles keep the default path"
        );
        var prototype = typeCheckItems["structural-interior"];
        ExpectTypeException<ArgumentException>(
            () =>
                PipeTypeDefinition.CreateSharedInventory(
                    localName: "inventory-interior",
                    prototype: prototype,
                    appearance: appearance
                ),
            "decorative interior profiles belong to structural connection types"
        );
        var asymmetric = new PipeAppearance(
            interior: new PipeInteriorProfile(
                new[] { new PipeInteriorMember(new Vector2(0.25f, 0.2f), 0.1f) }
            )
        );
        ExpectTypeException<ArgumentException>(
            () =>
                PipeTypeDefinition.CreateStructure(
                    localName: "asymmetric-undirected",
                    prototype: prototype,
                    appearance: asymmetric
                ),
            "undirected interior profiles retain their lateral mirror symmetry"
        );
        Check(
            PipeTypeDefinition
                .CreateStructure(
                    localName: "asymmetric-directed",
                    prototype: prototype,
                    appearance: asymmetric,
                    directionMode: PipeDirectionMode.Directed
                )
                .Appearance == asymmetric
                && PipeTypeDefinition
                    .CreateStructure(
                        localName: "symmetric-undirected",
                        prototype: prototype,
                        appearance: new PipeAppearance(interior: TypeCheckInterior())
                    )
                    .Appearance.Interior.Members.Count == 5,
            "directed types preserve asymmetric sections and symmetric sections support either orientation policy"
        );
    }

    private void PipeInteriorDefaultCore()
    {
        var factory = Fixture(1, 981020, false);
        SetTypeCheckPrototype(factory, 1, "structural-frame");
        var mesh = InteriorNodeMesh(factory, 1);
        try
        {
            Check(
                mesh.GetTriangles(0).Length > 0
                    && mesh.GetTriangles(1).Length == 0
                    && Mathf.Abs(mesh.bounds.extents.x - 0.32f * 0.45f) < 0.0001f
                    && Mathf.Abs(mesh.bounds.center.y - 0.34f) < 0.0001f,
                "a structural appearance without an interior profile retains one centered core at its configured radius"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private void PipeInteriorCurvedNetwork(Camera camera)
    {
        var factory = Fixture(5, 981021, false);
        for (int entityId = 1; entityId <= 5; entityId++)
        {
            float angle = (entityId - 1) * 0.15f;
            factory.entityPool[entityId].pos =
                new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0) * 200;
            SetTypeCheckPrototype(factory, entityId, "structural-interior");
        }
        for (int entityId = 1; entityId < 5; entityId++)
        {
            factory.WriteObjectConn(entityId, 0, true, entityId + 1, 1);
        }
        var firstMesh = InteriorNodeMesh(factory, 2);
        var secondMesh = InteriorNodeMesh(factory, 3);
        try
        {
            var firstShape = InteriorShape(factory, 2);
            var secondShape = InteriorShape(factory, 3);
            Vector3 firstOrigin = factory.entityPool[2].pos;
            Vector3 secondOrigin = factory.entityPool[3].pos;
            Vector3 firstCenter = firstOrigin + JointShapeField<Vector3>(firstShape, "Center");
            Vector3 secondCenter = secondOrigin + JointShapeField<Vector3>(secondShape, "Center");
            Vector3 endpoint = (firstCenter + secondCenter) * 0.5f;
            Vector3 tangent = (secondCenter - firstCenter).normalized;
            for (int material = 0; material <= 1; material++)
            {
                var firstIndices = InteriorEndpointIndices(
                    firstMesh,
                    firstOrigin,
                    endpoint,
                    tangent,
                    material
                );
                var secondIndices = InteriorEndpointIndices(
                    secondMesh,
                    secondOrigin,
                    endpoint,
                    tangent,
                    material
                );
                var firstVertices = firstMesh.vertices;
                var secondVertices = secondMesh.vertices;
                var firstNormals = firstMesh.normals;
                var secondNormals = secondMesh.normals;
                bool matching =
                    firstIndices.Length > 0
                    && secondIndices.Length > 0
                    && firstIndices.All(first =>
                        secondIndices.Any(second =>
                            Vector3.Distance(
                                firstVertices[first] + firstOrigin,
                                secondVertices[second] + secondOrigin
                            ) < 0.0005f
                            && Vector3.Dot(firstNormals[first], secondNormals[second]) > 0.995f
                        )
                    )
                    && secondIndices.All(second =>
                        firstIndices.Any(first =>
                            Vector3.Distance(
                                secondVertices[second] + secondOrigin,
                                firstVertices[first] + firstOrigin
                            ) < 0.0005f
                        )
                    );
                Check(
                    matching,
                    "adjacent curved interior sections share their endpoint vertices and normals for material "
                        + material
                );
                CheckInteriorSection(
                    firstMesh,
                    firstIndices,
                    firstOrigin,
                    endpoint,
                    tangent,
                    material,
                    TypeCheckInterior()
                );
            }
            CheckInteriorEnvelope(firstMesh, firstShape);
            CheckInteriorEnvelope(secondMesh, secondShape);
            PrepareStructureBatches(factory, camera);
            int previousContexts = appearanceContexts.Count;
            Check(
                PipeVisuals.Draw(factory, camera) > 0,
                "the renderer draws an enlarged curved connection with its internal members"
            );
            var contexts = appearanceContexts
                .Skip(previousContexts)
                .Where(context => context.Factory == factory)
                .ToArray();
            Check(
                contexts.Any(context => context.Surface == PipeSurfaceRole.Core)
                    && contexts.Count(context => context.Surface == PipeSurfaceRole.Fittings)
                        >= 2 * contexts.Count(context => context.Surface == PipeSurfaceRole.Core),
                "interior conductors and frame members reach their own core and fitting draw passes"
            );
            var materials = PipeMaterialLibrary.Get(typeCheckItems["structural-interior"].ID);
            ownedAppearanceMaterials.Add(materials.Get(PipeSurfaceRole.InnerWall));
            Check(
                materials.Core != materials.Fittings
                    && materials.Core.color != materials.Fittings.color,
                "interior conductor and metal frame surfaces retain independent configured materials"
            );
            int nodeBuilds = PipeVisuals.NodeRebuilds;
            int coreBuilds = PipeVisuals.LiquidRebuilds;
            appearanceAnimationPhase += 0.1f;
            PipeVisuals.Draw(factory, camera);
            Check(
                PipeVisuals.NodeRebuilds == nodeBuilds && PipeVisuals.LiquidRebuilds == coreBuilds,
                "interior effect updates preserve cached wall and member geometry"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(firstMesh);
            UnityEngine.Object.DestroyImmediate(secondMesh);
        }
    }

    private void PipeInteriorDirectedEnds()
    {
        var factory = Fixture(3, 981022, false);
        var meshes = new Mesh[3];
        for (int entityId = 1; entityId <= 3; entityId++)
        {
            float angle = (entityId - 1) * 0.15f;
            factory.entityPool[entityId].pos =
                new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0) * 200;
            SetTypeCheckPrototype(factory, entityId, "directed-interior");
        }
        factory.WriteObjectConn(1, 0, true, 2, 1);
        factory.WriteObjectConn(2, 0, true, 3, 1);
        try
        {
            for (int entityId = 1; entityId <= 3; entityId++)
            {
                meshes[entityId - 1] = InteriorNodeMesh(factory, entityId);
            }
            for (int entityId = 1; entityId <= 2; entityId++)
            {
                var firstMesh = meshes[entityId - 1];
                var secondMesh = meshes[entityId];
                Vector3 firstOrigin = factory.entityPool[entityId].pos;
                Vector3 secondOrigin = factory.entityPool[entityId + 1].pos;
                Vector3 firstCenter = firstOrigin + firstOrigin.normalized * 0.75f;
                Vector3 secondCenter = secondOrigin + secondOrigin.normalized * 0.75f;
                Vector3 endpoint = (firstCenter + secondCenter) * 0.5f;
                Vector3 tangent = (secondCenter - firstCenter).normalized;
                for (int material = 0; material <= 1; material++)
                {
                    var firstIndices = InteriorEndpointIndices(
                        firstMesh,
                        firstOrigin,
                        endpoint,
                        tangent,
                        material
                    );
                    var secondIndices = InteriorEndpointIndices(
                        secondMesh,
                        secondOrigin,
                        endpoint,
                        tangent,
                        material
                    );
                    var firstVertices = firstMesh.vertices;
                    var secondVertices = secondMesh.vertices;
                    Check(
                        firstIndices.Length >= 32
                            && secondIndices.Length >= 32
                            && firstIndices.All(first =>
                                secondIndices.Any(second =>
                                    Vector3.Distance(
                                        firstVertices[first] + firstOrigin,
                                        secondVertices[second] + secondOrigin
                                    ) < 0.0005f
                                )
                            )
                            && secondIndices.All(second =>
                                firstIndices.Any(first =>
                                    Vector3.Distance(
                                        secondVertices[second] + secondOrigin,
                                        firstVertices[first] + firstOrigin
                                    ) < 0.0005f
                                )
                            ),
                        "directed asymmetric interiors preserve their section from terminal to bend at connection "
                            + entityId
                    );
                    CheckInteriorSection(
                        firstMesh,
                        firstIndices,
                        firstOrigin,
                        endpoint,
                        tangent,
                        material,
                        TypeCheckAsymmetricInterior()
                    );
                    CheckInteriorSection(
                        secondMesh,
                        secondIndices,
                        secondOrigin,
                        endpoint,
                        tangent,
                        material,
                        TypeCheckAsymmetricInterior()
                    );
                }
            }
            var materials = PipeMaterialLibrary.Get(typeCheckItems["directed-interior"].ID);
            ownedAppearanceMaterials.Add(materials.Get(PipeSurfaceRole.InnerWall));
        }
        finally
        {
            foreach (var mesh in meshes)
            {
                if (mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
        }
    }

    private static Mesh InteriorNodeMesh(PlanetFactory factory, int entityId)
    {
        PipeVisuals.SelectionGeometry(factory, entityId);
        var builder = (PipeMesh)
            AccessTools
                .Method(typeof(PipeVisuals), "NodeMesh")
                .Invoke(null, new object[] { factory, entityId, true, 0 });
        return builder.Build("pipe-interior-check");
    }

    private static object InteriorShape(PlanetFactory factory, int entityId)
    {
        return AccessTools
            .Method(typeof(PipeVisuals), "Shape")
            .Invoke(null, new object[] { factory, entityId, factory.entityPool[entityId].pos });
    }

    private static int[] InteriorEndpointIndices(
        Mesh mesh,
        Vector3 origin,
        Vector3 endpoint,
        Vector3 tangent,
        int material
    )
    {
        var vertices = mesh.vertices;
        return mesh.GetTriangles(material)
            .Distinct()
            .Where(index =>
                Mathf.Abs(Vector3.Dot(vertices[index] + origin - endpoint, tangent)) < 0.0002f
            )
            .ToArray();
    }

    private void CheckInteriorSection(
        Mesh mesh,
        int[] indices,
        Vector3 origin,
        Vector3 endpoint,
        Vector3 tangent,
        int material,
        PipeInteriorProfile profile
    )
    {
        Vector3 sectionUp = Vector3.ProjectOnPlane(endpoint.normalized, tangent).normalized;
        Vector3 sectionSide = Vector3.Cross(tangent, sectionUp).normalized;
        var members = profile
            .Members.Where(member =>
                member.Surface == (material == 0 ? PipeSurfaceRole.Core : PipeSurfaceRole.Fittings)
            )
            .ToArray();
        var vertices = mesh.vertices;
        foreach (var member in members)
        {
            Vector3 memberCenter =
                endpoint + (sectionSide * member.Offset.x + sectionUp * member.Offset.y) * 0.8f;
            int matchingVertices = indices.Count(index =>
                Mathf.Abs(
                    Vector3.Distance(vertices[index] + origin, memberCenter)
                        - member.RadiusFraction * 0.8f
                ) < 0.0005f
            );
            Check(
                matchingVertices >= 32,
                "interior endpoint preserves each declared offset, radius and material role"
            );
        }
        Check(
            indices.All(index =>
                members.Any(member =>
                {
                    Vector3 memberCenter =
                        endpoint
                        + (sectionSide * member.Offset.x + sectionUp * member.Offset.y) * 0.8f;
                    return Mathf.Abs(
                            Vector3.Distance(vertices[index] + origin, memberCenter)
                                - member.RadiusFraction * 0.8f
                        ) < 0.0005f;
                })
            ),
            "interior endpoint surfaces match their declared section members"
        );
    }

    private void CheckInteriorEnvelope(Mesh mesh, object shape)
    {
        Vector3 center = JointShapeField<Vector3>(shape, "Center");
        var ends = JointShapeField<List<Vector3>>(shape, "Ends");
        var centerline = new Vector3[129];
        for (int sample = 0; sample < centerline.Length; sample++)
        {
            float position = (float)sample / (centerline.Length - 1);
            centerline[sample] =
                (1 - position) * (1 - position) * ends[0]
                + 2 * position * (1 - position) * center
                + position * position * ends[1];
        }
        float maximumDistance = 0;
        foreach (Vector3 vertex in mesh.vertices)
        {
            float nearestDistance = float.MaxValue;
            for (int sample = 0; sample + 1 < centerline.Length; sample++)
            {
                Vector3 segment = centerline[sample + 1] - centerline[sample];
                float amount = Mathf.Clamp01(
                    Vector3.Dot(vertex - centerline[sample], segment) / segment.sqrMagnitude
                );
                nearestDistance = Mathf.Min(
                    nearestDistance,
                    Vector3.Distance(vertex, centerline[sample] + segment * amount)
                );
            }
            maximumDistance = Mathf.Max(maximumDistance, nearestDistance);
        }
        Check(
            maximumDistance > 0.35f
                && maximumDistance < 0.8f * 0.9f
                && mesh.normals.All(normal => Mathf.Abs(normal.sqrMagnitude - 1) < 0.001f),
            "offset interior members follow the enlarged curve within the wall clearance and retain finite unit normals"
        );
    }
}
