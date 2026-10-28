using System;
using System.Collections.Generic;
using System.Linq;
using FluidPipes;
using FluidPipes.API;
using UnityEngine;

public sealed partial class RuntimeChecks
{
    private static PipeInteriorProfile TypeCheckOpenInterior()
    {
        var rail = PipeInteriorSection.RoundedRectangle(0.18f, 0.45f, 0.035f);
        var lamp = PipeInteriorSection.RoundedRectangle(0.1f, 0.035f, 0.008f);
        return new PipeInteriorProfile(
            new[]
            {
                new PipeInteriorMember(new Vector2(-0.4f, 0), rail, PipeSurfaceRole.Fittings),
                new PipeInteriorMember(new Vector2(0.4f, 0), rail, PipeSurfaceRole.Fittings),
                new PipeInteriorMember(new Vector2(-0.4f, 0.29f), lamp),
                new PipeInteriorMember(new Vector2(0.4f, 0.29f), lamp),
                new PipeInteriorMember(
                    new Vector2(0, -0.25f),
                    PipeInteriorSection.RoundedRectangle(1, 0.14f, 0.025f),
                    PipeSurfaceRole.Fittings
                ),
            }
        );
    }

    private void PipeOpenBeamChecks(Camera camera)
    {
        PipeOpenBeamArguments();
        PipeRectangleGeometry();
        PipeOpenBeamNetwork(camera);
    }

    private void PipeOpenBeamArguments()
    {
        var circular = new PipeInteriorMember(Vector2.zero, 0.2f);
        Check(
            circular.Section.Kind == PipeInteriorSectionKind.Circle
                && circular.RadiusFraction == 0.2f,
            "circular members preserve their original section and radius"
        );
        foreach (
            float invalidDimension in new[]
            {
                float.NaN,
                float.NegativeInfinity,
                float.PositiveInfinity,
                0f,
                0.019f,
                1.701f,
            }
        )
        {
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => PipeInteriorSection.RoundedRectangle(invalidDimension, 0.2f, 0),
                "rectangular section widths stay within their finite supported range"
            );
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => PipeInteriorSection.RoundedRectangle(0.2f, invalidDimension, 0),
                "rectangular section heights stay within their finite supported range"
            );
        }
        foreach (
            float invalidBevel in new[]
            {
                float.NaN,
                float.NegativeInfinity,
                float.PositiveInfinity,
                -0.001f,
                0.101f,
            }
        )
        {
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => PipeInteriorSection.RoundedRectangle(0.4f, 0.2f, invalidBevel),
                "rectangular corner radii fit within both half-dimensions"
            );
        }
        var rectangle = PipeInteriorSection.RoundedRectangle(0.4f, 0.2f, 0.03f);
        Check(
            rectangle.Kind == PipeInteriorSectionKind.RoundedRectangle
                && rectangle.WidthFraction == 0.4f
                && rectangle.HeightFraction == 0.2f
                && rectangle.BevelFraction == 0.03f
                && PipeInteriorSection.RoundedRectangle(0.02f, 1.7f, 0.01f).WidthFraction == 0.02f
                && typeof(PipeInteriorSection)
                    .GetProperties()
                    .All(property => property.GetSetMethod(true) == null),
            "rectangular sections retain immutable dimensions and inclusive limits"
        );
        ExpectTypeException<ArgumentNullException>(
            () => new PipeInteriorMember(Vector2.zero, (PipeInteriorSection)null),
            "interior members require a section definition"
        );
        ExpectTypeException<ArgumentOutOfRangeException>(
            () => new PipeInteriorMember(new Vector2(0.75f, 0), rectangle),
            "off-axis rectangular members preserve the outer envelope clearance"
        );
        var prototype = typeCheckItems["open-beam"];
        var openAppearance = new PipeAppearance(
            PipeWallMode.Open,
            interior: TypeCheckOpenInterior()
        );
        Check(
            PipeTypeDefinition
                .CreateStructure(
                    localName: "open-structure",
                    prototype: prototype,
                    appearance: openAppearance
                )
                .Appearance == openAppearance,
            "open structural profiles allow a fixed radial floor and lateral rail symmetry"
        );
        ExpectTypeException<ArgumentException>(
            () =>
                PipeTypeDefinition.CreateSharedInventory(
                    localName: "open-inventory",
                    prototype: prototype,
                    appearance: openAppearance
                ),
            "open profiles retain structural behavior"
        );
        ExpectTypeException<ArgumentException>(
            () =>
                PipeTypeDefinition.CreateStructure(
                    localName: "open-without-members",
                    prototype: prototype,
                    appearance: new PipeAppearance(PipeWallMode.Open)
                ),
            "open profiles require visible interior members"
        );
        var mismatchedSections = new PipeAppearance(
            PipeWallMode.Open,
            interior: new PipeInteriorProfile(
                new[]
                {
                    new PipeInteriorMember(
                        new Vector2(-0.3f, 0),
                        PipeInteriorSection.RoundedRectangle(0.2f, 0.1f, 0.01f)
                    ),
                    new PipeInteriorMember(
                        new Vector2(0.3f, 0),
                        PipeInteriorSection.RoundedRectangle(0.1f, 0.2f, 0.01f)
                    ),
                }
            )
        );
        ExpectTypeException<ArgumentException>(
            () =>
                PipeTypeDefinition.CreateStructure(
                    localName: "mismatched-rectangles",
                    prototype: prototype,
                    appearance: mismatchedSections
                ),
            "undirected mirror partners match width and height even when their enclosing radii agree"
        );
        var mismatchedBevels = new PipeAppearance(
            PipeWallMode.Open,
            interior: new PipeInteriorProfile(
                new[]
                {
                    new PipeInteriorMember(
                        new Vector2(-0.3f, 0),
                        PipeInteriorSection.RoundedRectangle(0.2f, 0.1f, 0.01f)
                    ),
                    new PipeInteriorMember(
                        new Vector2(0.3f, 0),
                        PipeInteriorSection.RoundedRectangle(0.2f, 0.1f, 0.02f)
                    ),
                }
            )
        );
        ExpectTypeException<ArgumentException>(
            () =>
                PipeTypeDefinition.CreateStructure(
                    localName: "mismatched-bevels",
                    prototype: prototype,
                    appearance: mismatchedBevels
                ),
            "undirected mirror partners retain equal rounded corners"
        );
        var leftRail = new PipeInteriorMember(new Vector2(-0.3f, 0), rectangle);
        var rightRail = new PipeInteriorMember(new Vector2(0.3f, 0), rectangle);
        ExpectTypeException<ArgumentException>(
            () =>
                PipeTypeDefinition.CreateStructure(
                    localName: "unbalanced-mirror-members",
                    prototype: prototype,
                    appearance: new PipeAppearance(
                        PipeWallMode.Open,
                        interior: new PipeInteriorProfile(new[] { leftRail, leftRail, rightRail })
                    )
                ),
            "undirected mirror sections retain equal member multiplicities"
        );
        Check(
            PipeTypeDefinition
                .CreateStructure(
                    localName: "balanced-mirror-members",
                    prototype: prototype,
                    appearance: new PipeAppearance(
                        PipeWallMode.Open,
                        interior: new PipeInteriorProfile(
                            new[] { leftRail, leftRail, rightRail, rightRail }
                        )
                    )
                )
                .Appearance.Interior.Members.Count == 4,
            "undirected mirror sections accept balanced repeated members"
        );
    }

    private void PipeRectangleGeometry()
    {
        foreach (float bevel in new[] { 0f, 0.03f, 0.1f })
        {
            var profile = new PipeInteriorProfile(
                new[]
                {
                    new PipeInteriorMember(
                        Vector2.zero,
                        PipeInteriorSection.RoundedRectangle(0.4f, 0.2f, bevel),
                        PipeSurfaceRole.Fittings
                    ),
                }
            );
            var builder = new PipeMesh(1, false, geometry: new PipeGeometryProfile(1));
            builder.InteriorNode(
                Vector3.zero,
                Vector3.up,
                Vector3.forward,
                new[] { Vector3.back * 2, Vector3.forward * 2 },
                new[] { Vector3.back, Vector3.forward },
                new[] { Vector3.up, Vector3.up },
                profile
            );
            var mesh = builder.Build("rectangular-member-check");
            try
            {
                var vertices = mesh.vertices;
                var section = mesh.GetTriangles(1)
                    .Distinct()
                    .Select(index => vertices[index])
                    .Where(position => Mathf.Abs(position.z - 2) < 0.00001f)
                    .Distinct()
                    .ToArray();
                Check(
                    mesh.GetTriangles(0).Length == 0
                        && section.Length >= 4
                        && Mathf.Abs(section.Max(position => position.x) - 0.2f) < 0.00001f
                        && Mathf.Abs(section.Min(position => position.x) + 0.2f) < 0.00001f
                        && Mathf.Abs(section.Max(position => position.y) - 0.1f) < 0.00001f
                        && Mathf.Abs(section.Min(position => position.y) + 0.1f) < 0.00001f,
                    "rectangular beams preserve their full width and height at corner radius "
                        + bevel
                );
                if (bevel > 0)
                {
                    Check(
                        section.All(position =>
                            Mathf.Abs(position.x) < 0.19999f || Mathf.Abs(position.y) < 0.09999f
                        )
                            && section.Any(position =>
                                Mathf.Abs(Mathf.Abs(position.x) - 0.2f) < 0.00001f
                                && Mathf.Abs(Mathf.Abs(position.y) - (0.1f - bevel)) < 0.00001f
                            ),
                        "rounded rectangle corners meet their straight faces at the configured radius"
                    );
                }
                CheckBeamSurfaceGeometry(mesh);
                CheckRectangleTextureCoordinates(mesh, bevel);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }

    private void PipeOpenBeamNetwork(Camera camera)
    {
        var forward = OpenBeamFixture(981023, false);
        var reverse = OpenBeamFixture(981024, true);
        var meshes = new List<Mesh>();
        try
        {
            var first = InteriorNodeMesh(forward, 2);
            var second = InteriorNodeMesh(forward, 3);
            var terminal = InteriorNodeMesh(forward, 1);
            var reversed = InteriorNodeMesh(reverse, 2);
            meshes.AddRange(new[] { first, second, terminal, reversed });
            CheckOpenBeamTerminalCap(forward, terminal);
            Vector3 firstOrigin = forward.entityPool[2].pos;
            Vector3 secondOrigin = forward.entityPool[3].pos;
            Vector3 firstCenter = firstOrigin + firstOrigin.normalized * 0.75f;
            Vector3 secondCenter = secondOrigin + secondOrigin.normalized * 0.75f;
            Vector3 endpoint = (firstCenter + secondCenter) * 0.5f;
            Vector3 tangent = (secondCenter - firstCenter).normalized;
            for (int material = 0; material <= 1; material++)
            {
                var firstVertices = first.vertices;
                var secondVertices = second.vertices;
                var reverseVertices = reversed.vertices;
                var firstIndices = InteriorEndpointIndices(
                    first,
                    firstOrigin,
                    endpoint,
                    tangent,
                    material
                );
                var secondIndices = InteriorEndpointIndices(
                    second,
                    secondOrigin,
                    endpoint,
                    tangent,
                    material
                );
                var reverseIndices = InteriorEndpointIndices(
                    reversed,
                    firstOrigin,
                    endpoint,
                    tangent,
                    material
                );
                Check(
                    firstIndices.Length > 0
                        && secondIndices.Length > 0
                        && firstIndices.All(index =>
                            secondIndices.Any(other =>
                                Vector3.Distance(
                                    firstVertices[index] + firstOrigin,
                                    secondVertices[other] + secondOrigin
                                ) < 0.0005f
                            )
                        )
                        && secondIndices.All(index =>
                            firstIndices.Any(other =>
                                Vector3.Distance(
                                    secondVertices[index] + secondOrigin,
                                    firstVertices[other] + firstOrigin
                                ) < 0.0005f
                            )
                        ),
                    "rounded rectangular members join continuously across neighboring curved nodes for material "
                        + material
                );
                Check(
                    reverseIndices.Length > 0
                        && firstIndices.All(index =>
                            reverseIndices.Any(other =>
                                Vector3.Distance(firstVertices[index], reverseVertices[other])
                                < 0.0005f
                            )
                        )
                        && reverseIndices.All(index =>
                            firstIndices.Any(other =>
                                Vector3.Distance(reverseVertices[index], firstVertices[other])
                                < 0.0005f
                            )
                        ),
                    "reversing undirected construction preserves the rail and radial floor section for material "
                        + material
                );
                CheckOpenBeamSection(first, firstIndices, firstOrigin, endpoint, tangent, material);
            }
            CheckBeamSurfaceGeometry(first);
            CheckBeamSurfaceGeometry(second);
            var selection = PipeVisuals.SelectionGeometry(forward, 2).Build("open-beam-selection");
            meshes.Add(selection);
            Check(
                selection.vertices.SequenceEqual(first.vertices)
                    && selection.GetTriangles(0).SequenceEqual(first.GetTriangles(0))
                    && selection.GetTriangles(1).SequenceEqual(first.GetTriangles(1)),
                "open profile selection follows its visible rail and conductor geometry"
            );
            PrepareStructureBatches(forward, camera);
            int previousContexts = appearanceContexts.Count;
            Check(
                PipeVisuals.Draw(forward, camera) > 0,
                "open profiles form drawable structural batches"
            );
            var contexts = appearanceContexts
                .Skip(previousContexts)
                .Where(context => context.Factory == forward)
                .ToArray();
            Check(
                contexts.Any(context => context.Surface == PipeSurfaceRole.Core)
                    && contexts.Any(context => context.Surface == PipeSurfaceRole.Fittings)
                    && contexts.All(context =>
                        context.Surface == PipeSurfaceRole.Core
                        || context.Surface == PipeSurfaceRole.Fittings
                    ),
                "open rendering draws metal rails and emissive members without enclosing wall passes"
            );
            ownedAppearanceMaterials.Add(
                PipeMaterialLibrary
                    .Get(typeCheckItems["open-beam"].ID)
                    .Get(PipeSurfaceRole.InnerWall)
            );
        }
        finally
        {
            foreach (var mesh in meshes)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }

    private void CheckRectangleTextureCoordinates(Mesh mesh, float bevel)
    {
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        var coordinates = mesh.uv;
        var ring = Enumerable
            .Range(0, mesh.vertexCount)
            .Where(index =>
                Mathf.Abs(vertices[index].z - 2) < 0.00001f
                && Vector3.Dot(normals[index], new Vector3(vertices[index].x, vertices[index].y, 0))
                    > 0
            )
            .ToArray();
        int first = ring[0];
        int last = ring[ring.Length - 1];
        Check(
            Mathf.Abs(coordinates[first].x) < 0.00001f
                && Mathf.Abs(coordinates[last].x - 1) < 0.00001f
                && Vector3.Distance(vertices[first], vertices[last]) < 0.00001f
                && Vector3.Distance(normals[first], normals[last]) < 0.00001f,
            "rectangular texture seams duplicate their endpoint position and normal at zero and one"
        );
        float perimeter = 2 * (0.4f + 0.2f) + (2 * Mathf.PI - 8) * bevel;
        float longestFlatSpan = 0;
        for (int sample = 1; sample < ring.Length; sample++)
        {
            int previous = ring[sample - 1];
            int current = ring[sample];
            float span = coordinates[current].x - coordinates[previous].x;
            float normalAlignment = Mathf.Clamp(
                Vector3.Dot(normals[previous], normals[current]),
                -1,
                1
            );
            float expectedLength;
            if (normalAlignment > 0.99999f)
            {
                expectedLength = Vector3.Distance(vertices[previous], vertices[current]);
                longestFlatSpan = Mathf.Max(longestFlatSpan, span);
            }
            else
            {
                expectedLength = bevel * Mathf.Acos(normalAlignment);
            }
            if (
                float.IsNaN(span)
                || float.IsInfinity(span)
                || span < -0.00001f
                || Mathf.Abs(span - expectedLength / perimeter) > 0.0001f
            )
            {
                throw new InvalidOperationException(
                    "Rectangular texture coordinates differ from their section perimeter distances."
                );
            }
        }
        Check(
            Mathf.Abs(longestFlatSpan - (0.4f - 2 * bevel) / perimeter) < 0.0001f,
            "rectangular texture coordinates allocate straight and rounded surfaces their perimeter proportions"
        );
    }

    private PlanetFactory OpenBeamFixture(int planetId, bool reverse)
    {
        var factory = Fixture(5, planetId, false);
        for (int entityId = 1; entityId <= 5; entityId++)
        {
            float angle = (entityId - 1) * 0.15f;
            factory.entityPool[entityId].pos =
                new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0) * 200;
            SetTypeCheckPrototype(factory, entityId, "open-beam");
        }
        if (reverse)
        {
            for (int entityId = 5; entityId > 1; entityId--)
            {
                factory.WriteObjectConn(entityId, 0, true, entityId - 1, 1);
            }
        }
        else
        {
            for (int entityId = 1; entityId < 5; entityId++)
            {
                factory.WriteObjectConn(entityId, 0, true, entityId + 1, 1);
            }
        }
        return factory;
    }

    private void CheckOpenBeamSection(
        Mesh mesh,
        int[] indices,
        Vector3 origin,
        Vector3 endpoint,
        Vector3 tangent,
        int material
    )
    {
        Vector3 sectionUp = Vector3.ProjectOnPlane(endpoint.normalized, tangent).normalized;
        Vector3 sectionSide = Vector3.Cross(tangent, sectionUp).normalized;
        var vertices = mesh.vertices;
        var points = indices
            .Select(index => new Vector2(
                Vector3.Dot(vertices[index] + origin - endpoint, sectionSide) / 0.8f,
                Vector3.Dot(vertices[index] + origin - endpoint, sectionUp) / 0.8f
            ))
            .ToArray();
        foreach (
            var member in TypeCheckOpenInterior()
                .Members.Where(member =>
                    member.Surface
                    == (material == 0 ? PipeSurfaceRole.Core : PipeSurfaceRole.Fittings)
                )
        )
        {
            float halfWidth = member.Section.WidthFraction * 0.5f;
            float halfHeight = member.Section.HeightFraction * 0.5f;
            var section = points
                .Where(point =>
                    Mathf.Abs(point.x - member.Offset.x) <= halfWidth + 0.0005f
                    && Mathf.Abs(point.y - member.Offset.y) <= halfHeight + 0.0005f
                )
                .ToArray();
            Check(
                section.Length > 0
                    && Mathf.Abs(section.Min(point => point.x) - member.Offset.x + halfWidth)
                        < 0.0005f
                    && Mathf.Abs(section.Max(point => point.x) - member.Offset.x - halfWidth)
                        < 0.0005f
                    && Mathf.Abs(section.Min(point => point.y) - member.Offset.y + halfHeight)
                        < 0.0005f
                    && Mathf.Abs(section.Max(point => point.y) - member.Offset.y - halfHeight)
                        < 0.0005f,
                "open beam endpoint preserves each rectangular member's width, height and radial placement"
            );
        }
    }

    private void CheckBeamSurfaceGeometry(Mesh mesh)
    {
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        Check(
            vertices.All(vertex =>
                !float.IsNaN(vertex.x)
                && !float.IsNaN(vertex.y)
                && !float.IsNaN(vertex.z)
                && !float.IsInfinity(vertex.x)
                && !float.IsInfinity(vertex.y)
                && !float.IsInfinity(vertex.z)
            ) && normals.All(normal => Mathf.Abs(normal.sqrMagnitude - 1) < 0.001f),
            "rounded rectangular members have finite positions and unit normals"
        );
        for (int material = 0; material <= 1; material++)
        {
            var triangles = mesh.GetTriangles(material);
            for (int triangle = 0; triangle < triangles.Length; triangle += 3)
            {
                int first = triangles[triangle];
                int second = triangles[triangle + 1];
                int third = triangles[triangle + 2];
                var face = Vector3.Cross(
                    vertices[second] - vertices[first],
                    vertices[third] - vertices[first]
                );
                if (
                    Vector3.Dot(face, normals[first] + normals[second] + normals[third]) < -0.00001f
                )
                {
                    throw new InvalidOperationException(
                        "Rounded rectangular surface winding opposes its normals."
                    );
                }
            }
        }
        Check(true, "rounded rectangular surface winding matches the outward normals");
    }

    private void CheckOpenBeamTerminalCap(PlanetFactory factory, Mesh mesh)
    {
        var shape = InteriorShape(factory, 1);
        Vector3 center = JointShapeField<Vector3>(shape, "Center");
        Vector3 endpoint = JointShapeField<List<Vector3>>(shape, "Ends")[0];
        Vector3 normal = (center - endpoint).normalized;
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        float capArea = 0;
        for (int material = 0; material <= 1; material++)
        {
            var triangles = mesh.GetTriangles(material);
            for (int index = 0; index < triangles.Length; index += 3)
            {
                int first = triangles[index];
                int second = triangles[index + 1];
                int third = triangles[index + 2];
                if (
                    new[] { first, second, third }.All(vertex =>
                        Mathf.Abs(Vector3.Dot(vertices[vertex] - center, normal)) < 0.0001f
                        && Vector3.Dot(normals[vertex], normal) > 0.999f
                    )
                )
                {
                    capArea +=
                        Vector3
                            .Cross(
                                vertices[second] - vertices[first],
                                vertices[third] - vertices[first]
                            )
                            .magnitude * 0.5f;
                }
            }
        }
        float expectedArea =
            TypeCheckOpenInterior()
                .Members.Sum(member =>
                    member.Section.WidthFraction * member.Section.HeightFraction
                    - (4 - Mathf.PI) * member.Section.BevelFraction * member.Section.BevelFraction
                )
            * 0.8f
            * 0.8f;
        Check(
            Mathf.Abs(capArea - expectedArea) < 0.001f,
            "open rail terminals close each rounded rectangular member with its planar section area"
        );
        CheckBeamSurfaceGeometry(mesh);
    }
}
