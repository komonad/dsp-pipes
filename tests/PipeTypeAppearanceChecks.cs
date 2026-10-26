using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FluidPipes;
using FluidPipes.API;
using FluidPipes.Integration.Belts;
using HarmonyLib;
using UnityEngine;

public sealed partial class RuntimeChecks
{
    private const string EffectProbeProperty = "_FluidPipesCheckEffectProbe";
    private readonly Dictionary<string, int> appearanceConfigurations =
        new Dictionary<string, int>();
    private readonly Dictionary<string, int> appearanceEffectCalls = new Dictionary<string, int>();
    private readonly List<Material> ownedAppearanceMaterials = new List<Material>();
    private readonly List<PipeEffectContext> appearanceContexts = new List<PipeEffectContext>();
    private Material appearanceSourceMaterial;
    private Texture2D appearanceSourceTexture;
    private float appearanceAnimationPhase;
    private bool appearanceBlocksAreClean = true;
    private Material defaultAppearanceWall;
    private Material defaultAppearanceFittings;

    private PipeAppearance TypeCheckAppearance(
        string localName,
        bool hasCore = false,
        bool failConfiguration = false,
        bool failEffects = false,
        float coreRadiusFraction = 0.4f,
        float textureOffset = 0,
        PipeInteriorProfile interior = null,
        PipeWallMode wallMode = PipeWallMode.Glass
    )
    {
        return new PipeAppearance(
            wallMode: wallMode,
            configureMaterials: materials =>
            {
                appearanceConfigurations.TryGetValue(localName, out int configurations);
                appearanceConfigurations[localName] = configurations + 1;
                if (appearanceSourceMaterial == null)
                {
                    appearanceSourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    appearanceSourceTexture.SetPixels(
                        new[] { Color.white, Color.gray, Color.gray, Color.white }
                    );
                    appearanceSourceTexture.Apply();
                    appearanceSourceMaterial = new Material(Shader.Find("Standard"))
                    {
                        color = new Color(0.2f, 0.4f, 0.6f, 0.45f),
                        mainTexture = appearanceSourceTexture,
                    };
                }

                materials.SetMaterial(PipeSurfaceRole.Wall, appearanceSourceMaterial);
                materials.Fittings.color =
                    localName == "shared-variant"
                        ? new Color(0.7f, 0.4f, 0.2f)
                        : new Color(0.2f, 0.7f, 0.4f);
                materials.Core.color = new Color(0.6f, 0.2f, 0.9f);
                ownedAppearanceMaterials.AddRange(
                    new[]
                    {
                        materials.Wall,
                        materials.Fittings,
                        materials.Core,
                        materials.MachinePanel,
                        materials.MachineShell,
                    }
                );
                if (failConfiguration)
                {
                    throw new InvalidOperationException(
                        "Injected appearance configuration failure."
                    );
                }
            },
            hasCore: hasCore,
            coreRadiusFraction: coreRadiusFraction,
            applyEffects: (context, properties) =>
            {
                appearanceEffectCalls.TryGetValue(localName, out int calls);
                appearanceEffectCalls[localName] = calls + 1;
                appearanceBlocksAreClean &= properties.GetFloat(EffectProbeProperty) == 0;
                properties.SetFloat(EffectProbeProperty, appearanceAnimationPhase + 1);
                properties.SetColor(
                    "_Color",
                    new Color(appearanceAnimationPhase, context.GroupKey * 0.25f, 0.8f, 0.45f)
                );
                appearanceContexts.Add(context);
                if (failEffects)
                {
                    throw new InvalidOperationException("Injected appearance effect failure.");
                }
            },
            textureCoordinate: position => position.x * 0.25f + position.z * 0.5f + textureOffset,
            interior: interior
        );
    }

    private void PipeTypeAppearance()
    {
        PipeRuntime.Clear();
        defaultAppearanceWall = PipeVisuals.BodyMaterial;
        defaultAppearanceFittings = PipeVisuals.JointMaterial;
        Color defaultWallColor = defaultAppearanceWall.color;
        Color defaultFittingColor = defaultAppearanceFittings.color;
        int variantItemId = typeCheckItems["shared-variant"].ID;
        int trackItemId = typeCheckItems["directed-track"].ID;
        var variantMaterials = PipeMaterialLibrary.Get(variantItemId);
        var trackMaterials = PipeMaterialLibrary.Get(trackItemId);
        for (int lookup = 0; lookup < 20; lookup++)
        {
            Check(
                ReferenceEquals(PipeMaterialLibrary.Get(variantItemId), variantMaterials),
                "appearance cache retains one material set on lookup " + lookup
            );
        }
        Check(
            appearanceConfigurations["shared-variant"] == 1
                && appearanceConfigurations["directed-track"] == 1
                && variantMaterials.Wall != trackMaterials.Wall
                && variantMaterials.Fittings != trackMaterials.Fittings
                && variantMaterials.MachineShell != trackMaterials.MachineShell
                && variantMaterials.Wall != appearanceSourceMaterial
                && variantMaterials.Wall.mainTexture == appearanceSourceTexture,
            "types own independent material copies while retaining extension-owned texture references"
        );
        Check(
            variantMaterials.Fittings.color != trackMaterials.Fittings.color
                && defaultAppearanceWall.color == defaultWallColor
                && defaultAppearanceFittings.color == defaultFittingColor
                && variantMaterials.Wall.renderQueue == 3000
                && variantMaterials.Wall.GetInt("_ZWrite") == 0,
            "configuration preserves the default appearance and establishes transparent depth policy"
        );
        var interiorMaterial = variantMaterials.Get(PipeSurfaceRole.InnerWall);
        ownedAppearanceMaterials.Add(interiorMaterial);
        ownedAppearanceMaterials.Add(trackMaterials.Get(PipeSurfaceRole.InnerWall));
        Check(
            interiorMaterial != variantMaterials.Wall
                && interiorMaterial.renderQueue < variantMaterials.Wall.renderQueue
                && interiorMaterial.shader == variantMaterials.Wall.shader
                && interiorMaterial.mainTexture == variantMaterials.Wall.mainTexture
                && interiorMaterial.color == variantMaterials.Wall.color,
            "configured glass has matching independent interior and exterior surfaces"
        );
        var structuralMaterials = PipeMaterialLibrary.Get(typeCheckItems["structural-frame"].ID);
        ownedAppearanceMaterials.AddRange(
            new[]
            {
                structuralMaterials.Wall,
                structuralMaterials.Fittings,
                structuralMaterials.Core,
                structuralMaterials.MachinePanel,
                structuralMaterials.MachineShell,
                structuralMaterials.Get(PipeSurfaceRole.InnerWall),
            }
        );
        Check(
            structuralMaterials.Wall.renderQueue == 2000
                && structuralMaterials.Wall.GetInt("_ZWrite") == 1,
            "opaque structural walls retain depth-writing material state"
        );
        ExpectTypeException<InvalidOperationException>(
            () => variantMaterials.SetMaterial(PipeSurfaceRole.Wall, appearanceSourceMaterial),
            "material replacement is confined to the resource configuration stage"
        );
        Logger.LogInfo("Checking injected appearance initialization failure and fallback.");
        int failedItemId = typeCheckItems["other-structure"].ID;
        var fallback = PipeMaterialLibrary.Get(failedItemId);
        Check(
            fallback.Wall == defaultAppearanceWall
                && fallback.Fittings == defaultAppearanceFittings
                && ReferenceEquals(PipeMaterialLibrary.Get(failedItemId), fallback)
                && appearanceConfigurations["other-structure"] == 1,
            "failed configuration releases its copies and caches a usable default appearance"
        );

        var factory = Fixture(4, 981005, false);
        SetTypeCheckPrototype(factory, 1, "shared-variant");
        for (int entityId = 2; entityId <= 4; entityId++)
        {
            SetTypeCheckPrototype(factory, entityId, "directed-track");
        }
        factory.WriteObjectConn(2, 0, true, 3, 1);
        factory.WriteObjectConn(3, 0, true, 4, 1);
        var cameraObject = new GameObject("pipe-type-effects-camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = new Vector3(2, 205, -8);
        camera.transform.LookAt(new Vector3(2, 200, 0));
        try
        {
            appearanceContexts.Clear();
            appearanceAnimationPhase = 0.2f;
            Check(
                PipeVisuals.Draw(factory, camera) > 0,
                "the live renderer draws registered type batches and decorative conductors"
            );
            int meshBuilds = PipeVisuals.Rebuilds;
            int nodeBuilds = PipeVisuals.NodeRebuilds;
            int coreBuilds = PipeVisuals.LiquidRebuilds;
            Color savedWall = trackMaterials.Wall.color;
            Color savedCore = trackMaterials.Core.color;
            appearanceAnimationPhase = 0.8f;
            PipeVisuals.Draw(factory, camera);
            Check(
                PipeVisuals.Rebuilds == meshBuilds
                    && PipeVisuals.NodeRebuilds == nodeBuilds
                    && PipeVisuals.LiquidRebuilds == coreBuilds
                    && appearanceConfigurations["shared-variant"] == 1
                    && appearanceConfigurations["directed-track"] == 1
                    && trackMaterials.Wall.color == savedWall
                    && trackMaterials.Core.color == savedCore,
                "changing effect uniforms preserves meshes and cached material state"
            );
            var trackContexts = appearanceContexts
                .Where(context => factory.entityPool[context.EntityId].protoId == trackItemId)
                .ToArray();
            Check(
                appearanceBlocksAreClean
                    && trackContexts.All(context =>
                        context.Factory == factory && context.GroupKey == context.EntityId % 2
                    )
                    && trackContexts.Select(context => context.GroupKey).Distinct().Count() == 2
                    && trackContexts.Any(context => context.Surface == PipeSurfaceRole.Core)
                    && trackContexts.Any(context => context.Surface == PipeSurfaceRole.InnerWall)
                    && trackContexts.Any(context => context.Surface == PipeSurfaceRole.Fittings),
                "draw callbacks receive separate clean property blocks and correct semantic batch roles"
            );

            Logger.LogInfo("Checking injected draw effect failure and callback isolation.");
            var failedFactory = Fixture(1, 981006, false);
            SetTypeCheckPrototype(failedFactory, 1, "isolated-inventory");
            Check(
                PipeVisuals.Draw(failedFactory, camera) > 0
                    && PipeVisuals.Draw(failedFactory, camera) > 0
                    && appearanceEffectCalls["isolated-inventory"] == 1,
                "an effect exception disables that callback while the type continues rendering"
            );
            ownedAppearanceMaterials.Add(
                PipeMaterialLibrary
                    .Get(typeCheckItems["isolated-inventory"].ID)
                    .Get(PipeSurfaceRole.InnerWall)
            );
            Check(
                appearanceConfigurations["isolated-inventory"] == 1
                    && appearanceSourceMaterial.color == new Color(0.2f, 0.4f, 0.6f, 0.45f),
                "effect failure preserves configuration lifetime and the extension source material"
            );
            PipeTypeTextureCoordinates();
            PipePeriodicTextureCoordinates();
            PipeGeometryProfileMeshes();
            PipeGeometryReducers();
            PipeTypeProfileGeometry();
            PipeJointOwnership();
            PipeProfileCollisionQueries();
            PipeProfileColliderRefresh();
            PipeInteriorChecks(camera);
            PipeOpenBeamChecks(camera);
            PipeTypeUpgradeAppearance(camera);
            DirectedShapeAfterCut();
        }
        finally
        {
            PipeVisuals.Clear();
            PipeMaterialLibrary.Dispose();
            UnityEngine.Object.DestroyImmediate(cameraObject);
            PipeRuntime.Clear();
        }
    }

    private void PipeTypeUpgradeAppearance(Camera camera)
    {
        var factory = Fixture(1, 981007, false);
        SetTypeCheckPrototype(factory, 1, "structural-frame");
        PipeVisuals.Draw(factory, camera);
        int buildsBefore = PipeVisuals.NodeRebuilds;
        var coreBuilderMethod = AccessTools.Method(typeof(PipeVisuals), "NodeMesh");
        var wallBefore = PipeVisuals
            .SelectionGeometry(factory, 1)
            .Build("type-upgrade-wall-before");
        var coreBefore = (
            (PipeMesh)coreBuilderMethod.Invoke(null, new object[] { factory, 1, true })
        ).Build("type-upgrade-core-before");
        Mesh wallAfter = null;
        Mesh coreAfter = null;
        try
        {
            var replacement = typeCheckItems["structural-upgrade"];
            factory.entityPool[1].protoId = (short)replacement.ID;
            factory.entityPool[1].modelIndex = (short)replacement.ModelIndex;
            PipeLifecyclePatches.Upgraded(factory, 1, replacement);
            PipeVisuals.Draw(factory, camera);
            wallAfter = PipeVisuals.SelectionGeometry(factory, 1).Build("type-upgrade-wall-after");
            coreAfter = (
                (PipeMesh)coreBuilderMethod.Invoke(null, new object[] { factory, 1, true })
            ).Build("type-upgrade-core-after");
            Check(
                PipeVisuals.NodeRebuilds == buildsBefore + 1
                    && wallAfter.uv.Any(coordinate => coordinate.y > 29 && coordinate.y < 31)
                    && wallBefore.uv.All(coordinate => coordinate.y < 2)
                    && Mathf.Abs(coreAfter.bounds.extents.x - 0.4f * 0.2f) < 0.0001f
                    && Mathf.Abs(wallAfter.bounds.center.y - 0.8f) < 0.0001f
                    && Mathf.Abs(wallBefore.bounds.center.y - 0.34f) < 0.0001f,
                "a type upgrade at unchanged placement refreshes UVs, radius, center height and conductor geometry"
            );
            CheckProfileOutlineAndCollider(factory, wallAfter, 0.8f);
            var replacementMaterials = PipeMaterialLibrary.Get(replacement.ID);
            ownedAppearanceMaterials.Add(replacementMaterials.Get(PipeSurfaceRole.InnerWall));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(wallBefore);
            UnityEngine.Object.DestroyImmediate(coreBefore);
            if (wallAfter != null)
            {
                UnityEngine.Object.DestroyImmediate(wallAfter);
            }
            if (coreAfter != null)
            {
                UnityEngine.Object.DestroyImmediate(coreAfter);
            }
        }
    }

    private void PipeGeometryProfileArguments()
    {
        var defaults = PipeGeometryProfile.Default;
        Check(
            defaults.Radius == PipeMesh.Radius
                && defaults.CenterHeight == PipeMesh.CenterHeight
                && defaults.FittingScale == 1
                && defaults.SupportRingWidth == 0,
            "default geometry retains the fluid pipe dimensions and smooth straight walls"
        );
        foreach (
            float nonfinite in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity }
        )
        {
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeGeometryProfile(radius: nonfinite),
                "geometry rejects nonfinite radius " + nonfinite
            );
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeGeometryProfile(centerHeight: nonfinite),
                "geometry rejects nonfinite center height " + nonfinite
            );
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeGeometryProfile(fittingScale: nonfinite),
                "geometry rejects nonfinite fitting scale " + nonfinite
            );
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeGeometryProfile(supportRingWidth: nonfinite),
                "geometry rejects nonfinite support ring width " + nonfinite
            );
        }
        foreach (float invalid in new[] { 0.079f, 1.201f })
        {
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeGeometryProfile(radius: invalid),
                "geometry enforces its radius bounds " + invalid
            );
        }
        foreach (float invalid in new[] { -0.001f, 2.001f })
        {
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeGeometryProfile(centerHeight: invalid),
                "geometry enforces its center-height bounds " + invalid
            );
        }
        foreach (float invalid in new[] { 0.749f, 1.501f })
        {
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeGeometryProfile(fittingScale: invalid),
                "geometry enforces its fitting-scale bounds " + invalid
            );
        }
        foreach (float invalid in new[] { -0.001f, 0.321f })
        {
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeGeometryProfile(supportRingWidth: invalid),
                "geometry limits support ring width to the wall radius " + invalid
            );
        }
        var minimum = new PipeGeometryProfile(0.08f, 0, 0.75f, 0.08f);
        var maximum = new PipeGeometryProfile(1.2f, 2, 1.5f, 1.2f);
        Check(
            minimum.SupportRingWidth == minimum.Radius
                && maximum.SupportRingWidth == maximum.Radius
                && new PipeTypeDefinition(
                    "default-dimensions",
                    typeCheckItems["shared-variant"]
                ).Geometry == defaults,
            "geometry accepts inclusive bounds and omitted profiles retain the shared defaults"
        );
    }

    private void PipeGeometryProfileMeshes()
    {
        foreach (
            var profile in new[]
            {
                PipeGeometryProfile.Default,
                new PipeGeometryProfile(0.08f, 0, 0.75f, 0.02f),
                new PipeGeometryProfile(0.4f, 0.8f, 1.15f, 0.055f),
                new PipeGeometryProfile(0.4f, 0.8f, 1.15f, 0.0001f),
                new PipeGeometryProfile(1.2f, 2, 1.5f, 0.14f),
            }
        )
        {
            var builder = new PipeMesh(profile.Radius, geometry: profile);
            builder.Node(
                Vector3.zero,
                Vector3.up,
                Vector3.right,
                new[] { Vector3.left * 2, Vector3.right * 2 },
                new[] { true, true }
            );
            var mesh = builder.Build("geometry-profile-straight", true);
            try
            {
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                var wallIndices = mesh.GetTriangles(0).Distinct().ToArray();
                float wallRadius = wallIndices.Max(index =>
                    new Vector2(vertices[index].y, vertices[index].z).magnitude
                );
                Check(
                    Mathf.Abs(wallRadius - profile.Radius) < 0.0001f
                        && vertices.All(position =>
                            !float.IsNaN(position.sqrMagnitude)
                            && !float.IsInfinity(position.sqrMagnitude)
                        )
                        && normals.All(normal => Mathf.Abs(normal.sqrMagnitude - 1) < 0.001f),
                    "straight profile has the specified wall radius and finite unit normals "
                        + profile.Radius
                );
                var fittingIndices = mesh.GetTriangles(1).Distinct().ToArray();
                if (profile.SupportRingWidth == 0)
                {
                    Check(
                        fittingIndices.Length == 0,
                        "the default straight profile has no support-ring geometry"
                    );
                }
                else
                {
                    float fittingRadius = fittingIndices.Max(index =>
                        new Vector2(vertices[index].y, vertices[index].z).magnitude
                    );
                    Check(
                        fittingIndices.Length > 0
                            && fittingIndices.All(index =>
                                Mathf.Abs(vertices[index].x)
                                <= profile.SupportRingWidth * 0.5f + 0.0001f
                            )
                            && fittingRadius > profile.Radius
                            && fittingRadius < profile.Radius * 1.25f,
                        "a support ring surrounds the wall within its configured axial width "
                            + profile.Radius
                    );
                    var triangles = mesh.GetTriangles(1);
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
                            Vector3.Dot(face, normals[first] + normals[second] + normals[third])
                            < -0.000001f
                        )
                        {
                            throw new Exception("Support-ring winding disagrees with its normals.");
                        }
                    }
                    Check(
                        true,
                        "support-ring inward and outward faces have consistent winding "
                            + profile.Radius
                    );
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }

    private void PipeGeometryReducers()
    {
        foreach (float localRadius in new[] { 0.32f, 0.46f })
        {
            float peerRadius = localRadius == 0.32f ? 0.46f : 0.32f;
            var builder = new PipeMesh(localRadius, geometry: new PipeGeometryProfile(localRadius));
            builder.Node(
                Vector3.zero,
                Vector3.up,
                Vector3.right,
                new[] { Vector3.left, Vector3.right },
                new[] { false, false },
                endPeerRadiusRatios: new[] { 1f, peerRadius / localRadius }
            );
            var mesh = builder.Build("profile-radius-transition", true);
            try
            {
                var fittingIndices = mesh.GetTriangles(1).Distinct().ToArray();
                if (localRadius < peerRadius)
                {
                    Check(
                        fittingIndices.Length == 0,
                        "the narrower endpoint contributes its smooth wall to a radius transition"
                    );
                    continue;
                }
                var vertices = mesh.vertices;
                var mouth = fittingIndices
                    .Where(index => Mathf.Abs(vertices[index].x - 1) < 0.0001f)
                    .Select(index => new Vector2(vertices[index].y, vertices[index].z).magnitude)
                    .ToArray();
                Check(
                    fittingIndices.Length > 0
                        && mouth.Length > 0
                        && mouth.Min() < peerRadius
                        && mouth.Min() > peerRadius * 0.95f
                        && mouth.Max() > localRadius
                        && fittingIndices.All(index => vertices[index].x <= 1.0001f)
                        && mesh.normals.All(normal => Mathf.Abs(normal.sqrMagnitude - 1) < 0.001f),
                    "the wider endpoint seals the radius difference with an annular mouth and finite lining normals"
                );
                var triangles = mesh.GetTriangles(1);
                var normals = mesh.normals;
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
                        Vector3.Dot(face, normals[first] + normals[second] + normals[third])
                        < -0.000001f
                    )
                    {
                        throw new Exception(
                            "Radius-transition winding disagrees with its normals."
                        );
                    }
                }
                Check(
                    true,
                    "radius-transition mouth and lining retain consistent triangle winding"
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }

    private void PipeTypeProfileGeometry()
    {
        var factory = Fixture(1, 981009, false);
        SetTypeCheckPrototype(factory, 1, "shared-variant");
        var wall = PipeVisuals.SelectionGeometry(factory, 1).Build("profile-fluid-wall", true);
        var fluidBuilder = (PipeMesh)
            AccessTools
                .Method(typeof(PipeVisuals), "NodeMesh")
                .Invoke(null, new object[] { factory, 1, true });
        var liquid = fluidBuilder.Build("profile-fluid-interior");
        try
        {
            var vertices = wall.vertices;
            float wallRadius = wall.GetTriangles(0)
                .Distinct()
                .Max(index => Mathf.Abs(vertices[index].x));
            Check(
                Mathf.Abs(wallRadius - 0.46f) < 0.0001f
                    && Mathf.Abs(wall.bounds.center.y - 0.6f) < 0.0001f
                    && Mathf.Abs(liquid.bounds.extents.x - 0.46f * 0.76f) < 0.0001f
                    && Mathf.Abs(liquid.bounds.center.y - 0.6f) < 0.0001f,
                "typed wall and liquid geometry share the configured centerline and radius ratio"
            );
            CheckProfileOutlineAndCollider(factory, wall, 0.6f);
        }
        finally
        {
            PipeSelectionHighlights.Hide();
            UnityEngine.Object.DestroyImmediate(wall);
            UnityEngine.Object.DestroyImmediate(liquid);
        }

        var connected = Fixture(2, 981010, false);
        SetTypeCheckPrototype(connected, 1, "shared-variant");
        connected.WriteObjectConn(1, 0, true, 2, 1);
        var shapeMethod = AccessTools.Method(typeof(PipeVisuals), "Shape");
        var firstShape = shapeMethod.Invoke(
            null,
            new object[] { connected, 1, connected.entityPool[1].pos }
        );
        var secondShape = shapeMethod.Invoke(
            null,
            new object[] { connected, 2, connected.entityPool[2].pos }
        );
        var firstEnds =
            (List<Vector3>)AccessTools.Field(firstShape.GetType(), "Ends").GetValue(firstShape);
        var secondEnds =
            (List<Vector3>)AccessTools.Field(secondShape.GetType(), "Ends").GetValue(secondShape);
        Vector3 firstWorldEnd = firstEnds[0] + connected.entityPool[1].pos;
        Vector3 secondWorldEnd = secondEnds[0] + connected.entityPool[2].pos;
        Vector3 firstCenter =
            connected.entityPool[1].pos + connected.entityPool[1].pos.normalized * 0.6f;
        Vector3 secondCenter =
            connected.entityPool[2].pos + connected.entityPool[2].pos.normalized * 0.34f;
        Check(
            Vector3.Distance(firstWorldEnd, secondWorldEnd) < 0.0001f
                && Vector3.Distance(firstWorldEnd, (firstCenter + secondCenter) * 0.5f) < 0.0001f,
            "connected profiles of different center heights meet at one shared endpoint"
        );
        var wideMesh = PipeVisuals.SelectionGeometry(connected, 1).Build("connected-wide-profile");
        var narrowMesh = PipeVisuals
            .SelectionGeometry(connected, 2)
            .Build("connected-narrow-profile");
        try
        {
            Check(
                wideMesh.GetTriangles(1).Length > narrowMesh.GetTriangles(1).Length,
                "connected type profiles generate their radius transition on the wider endpoint"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(wideMesh);
            UnityEngine.Object.DestroyImmediate(narrowMesh);
        }
    }

    private void CheckProfileOutlineAndCollider(
        PlanetFactory factory,
        Mesh wall,
        float centerHeight
    )
    {
        PipeSelectionHighlights.Show(factory, 1, new int[0]);
        var outlines = (IDictionary)
            AccessTools.Field(typeof(PipeSelectionHighlights), "nodes").GetValue(null);
        var outline = outlines[1];
        var outlineMesh = (Mesh)AccessTools.Field(outline.GetType(), "Mesh").GetValue(outline);
        Check(
            outlineMesh.vertices.SequenceEqual(wall.vertices) && outlineMesh.bounds == wall.bounds,
            "selection outlines use the same profile geometry as the visible wall"
        );
        var colliderChunks = new[] { new ColliderContainer() };
        factory.entityPool[1].colliderId = 1;
        PipeVisuals.UpdateCollider(factory, 1, colliderChunks);
        var collider = colliderChunks[0].colliderPool[1];
        var inverseRotation = Quaternion.Inverse(collider.q);
        Check(
            Mathf.Abs(collider.pos.y - factory.entityPool[1].pos.y - centerHeight) < 0.0001f
                && wall.vertices.All(vertex =>
                {
                    Vector3 local =
                        inverseRotation * (vertex + factory.entityPool[1].pos - collider.pos);
                    return Mathf.Abs(local.x) <= collider.ext.x + 0.0001f
                        && Mathf.Abs(local.y) <= collider.ext.y + 0.0001f
                        && Mathf.Abs(local.z) <= collider.ext.z + 0.0001f;
                }),
            "selection colliders follow the profile centerline and contain the complete visible mesh"
        );
        PipeSelectionHighlights.Hide();
        colliderChunks[0].Free();
    }

    private void DirectedShapeAfterCut()
    {
        WithTypeNativePaths(() =>
        {
            var factory = Fixture(2, 981008, false);
            SetTypeCheckPrototype(factory, 1, "directed-track");
            SetTypeCheckPrototype(factory, 2, "directed-track");
            factory.WriteObjectConn(1, 0, true, 2, 1);
            RebuildTypeNativePaths(factory, 2);
            factory.ClearObjectConn(1, 0);
            factory.cargoTraffic.beltPool[1].outputId = factory.entityPool[2].beltId;
            var shape = AccessTools
                .Method(typeof(PipeVisuals), "Shape")
                .Invoke(null, new object[] { factory, 1, factory.entityPool[1].pos });
            var endpoints =
                (List<Vector3>)AccessTools.Field(shape.GetType(), "Ends").GetValue(shape);
            Check(
                PipeTypes.GetNeighbors(factory, 1).Length == 0 && endpoints.Count == 0,
                "directed geometry follows cleared reciprocal slots while native paths await rebuilding"
            );
        });
    }

    private void PipePeriodicTextureCoordinates()
    {
        foreach (float direction in new[] { -1f, 1f })
        {
            Func<Vector3, float> wrappedPhase = position =>
                (position.x < 0 ? 0.49f : -0.49f) * direction;
            var periodic = TextureCoordinateTestMesh(wrappedPhase, 1);
            var nonperiodic = TextureCoordinateTestMesh(wrappedPhase, 0);
            try
            {
                var periodicCoordinates = periodic
                    .GetTriangles(0)
                    .Distinct()
                    .Select(index => periodic.uv[index].y)
                    .ToArray();
                var innerCoordinates = periodic
                    .GetTriangles(2)
                    .Distinct()
                    .Select(index => periodic.uv[index].y)
                    .OrderBy(value => value)
                    .ToArray();
                var rawCoordinates = nonperiodic
                    .GetTriangles(0)
                    .Distinct()
                    .Select(index => nonperiodic.uv[index].y)
                    .ToArray();
                Check(
                    Mathf.Abs(periodicCoordinates.Max() - periodicCoordinates.Min() - 0.02f)
                        < 0.0001f
                        && periodicCoordinates
                            .OrderBy(value => value)
                            .SequenceEqual(innerCoordinates)
                        && Mathf.Abs(rawCoordinates.Max() - rawCoordinates.Min() - 0.98f) < 0.0001f,
                    "periodic UVs cross the phase seam continuously on both wall faces in direction "
                        + direction
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(periodic);
                UnityEngine.Object.DestroyImmediate(nonperiodic);
            }
        }

        var defaultMesh = TextureCoordinateTestMesh(null, 0);
        var defaultWithPeriod = TextureCoordinateTestMesh(null, 1);
        try
        {
            Check(
                defaultMesh.uv.SequenceEqual(defaultWithPeriod.uv)
                    && defaultMesh.uv.Max(coordinate => coordinate.y) == 4,
                "a coordinate period preserves the default arc-length UV mapping when the callback is absent"
            );
            var coordinates = defaultMesh.uv;
            foreach (int submesh in new[] { 0, 2 })
            {
                var triangles = defaultMesh.GetTriangles(submesh);
                for (int triangle = 0; triangle < triangles.Length; triangle += 3)
                {
                    float first = coordinates[triangles[triangle]].x;
                    float second = coordinates[triangles[triangle + 1]].x;
                    float third = coordinates[triangles[triangle + 2]].x;
                    if (
                        Mathf.Max(first, Mathf.Max(second, third))
                            - Mathf.Min(first, Mathf.Min(second, third))
                        > 1f / 32 + 0.0001f
                    )
                    {
                        throw new Exception(
                            "A wall triangle crosses the circumferential texture seam."
                        );
                    }
                }
            }
            var vertices = defaultMesh.vertices;
            var normals = defaultMesh.normals;
            var starts = Enumerable
                .Range(0, coordinates.Length)
                .Where(index => coordinates[index].x == 0)
                .ToArray();
            var ends = Enumerable
                .Range(0, coordinates.Length)
                .Where(index => coordinates[index].x == 1)
                .ToArray();
            Check(
                starts.Length > 0
                    && starts.Length == ends.Length
                    && starts.All(start =>
                        ends.Any(end =>
                            vertices[start] == vertices[end]
                            && normals[start] == normals[end]
                            && coordinates[start].y == coordinates[end].y
                        )
                    ),
                "both wall faces close their circumferential seam with coincident vertices at texture coordinates zero and one"
            );
            Check(
                true,
                "outer and inner wall triangles interpolate within one circumferential texture segment"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(defaultMesh);
            UnityEngine.Object.DestroyImmediate(defaultWithPeriod);
        }
    }

    private static Mesh TextureCoordinateTestMesh(Func<Vector3, float> coordinate, float period)
    {
        var builder = new PipeMesh(textureCoordinate: coordinate, textureCoordinatePeriod: period);
        builder.Node(
            Vector3.zero,
            Vector3.up,
            Vector3.right,
            new[] { Vector3.left, Vector3.right },
            new[] { false, false }
        );
        return builder.Build("periodic-texture-coordinate-check", true);
    }

    private void PipeTypeTextureCoordinates()
    {
        var textureOrigin = new Vector3(13, 0, 7);
        Func<Vector3, float> map = position => position.x * 0.25f + position.z * 0.5f;
        var builder = new PipeMesh(PipeMesh.Radius, true, map, textureOrigin);
        builder.Node(
            Vector3.zero,
            Vector3.up,
            Vector3.right,
            new[] { Vector3.left, Vector3.right },
            new[] { false, false }
        );
        var mesh = builder.Build("pipe-type-texture-coordinates", true);
        try
        {
            var coordinates = mesh.uv;
            var outerCoordinates = mesh.GetTriangles(0)
                .Distinct()
                .Select(vertex => coordinates[vertex].y)
                .OrderBy(value => value)
                .ToArray();
            var innerCoordinates = mesh.GetTriangles(2)
                .Distinct()
                .Select(vertex => coordinates[vertex].y)
                .OrderBy(value => value)
                .ToArray();
            Check(
                outerCoordinates.Length > 0
                    && outerCoordinates.SequenceEqual(innerCoordinates)
                    && Mathf.Abs(outerCoordinates.First() - map(textureOrigin + Vector3.left))
                        < 0.0001f
                    && Mathf.Abs(outerCoordinates.Last() - map(textureOrigin + Vector3.right))
                        < 0.0001f,
                "interior and exterior UVs use the same world-space longitudinal phase"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private void PipeTypeAppearanceRelease()
    {
        try
        {
            Check(
                ownedAppearanceMaterials.Count > 0
                    && ownedAppearanceMaterials.All(material => material == null)
                    && defaultAppearanceWall != null
                    && defaultAppearanceFittings != null
                    && appearanceSourceMaterial != null
                    && appearanceSourceTexture != null,
                "resource disposal destroys owned and failed material copies while retaining borrowed assets"
            );
        }
        finally
        {
            if (appearanceSourceMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(appearanceSourceMaterial);
            }
            if (appearanceSourceTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(appearanceSourceTexture);
            }
            ownedAppearanceMaterials.Clear();
        }
    }
}
