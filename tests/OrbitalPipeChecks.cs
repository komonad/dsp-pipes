using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;

public sealed partial class OrbitalRingChecks
{
    private Type orbitalIntegration;
    private Type orbitalPositions;
    private object orbitalLayout;
    private readonly Dictionary<int, List<int>> displayArcs = new Dictionary<int, List<int>>();
    private static bool NativeOrbitalBelts =>
        Environment.GetEnvironmentVariable("FluidPipesNativeOrbitalBelts") == "1";

    private void RunOrbitalComponentChecks()
    {
        InitializeOrbitalScene();
        Record("PASS orbital scene planet has full native foundation");
        CheckOrbitalDraggedConstruction();
        double angleUnit = (double)
            AccessTools.Field(orbitalLayout.GetType(), "AngleUnit").GetValue(orbitalLayout);
        int circleNodeCount = (int)Math.Round(360 / angleUnit);

        foreach (int itemId in new[] { 6515, 6517, 6516 })
        {
            Require(
                PipeTypes.TryGet(itemId, out var type),
                "orbital component registration " + itemId
            );
            Require(
                type.Behavior == PipeBehavior.Structure,
                "external orbital simulation " + itemId
            );
            Require(
                type.DirectionMode == PipeDirectionMode.Directed,
                "orbital direction policy " + itemId
            );
            var circle = new List<int>();
            for (int index = 0; index < circleNodeCount; index++)
            {
                circle.Add(PlaceOrbital(itemId, index * 360f / circleNodeCount));
                if (index > 0)
                {
                    ConnectOrbital(circle[index - 1], circle[index]);
                }
            }
            Require(!OrbitalComplete(circle[0]), "open orbital chain " + itemId);
            ConnectOrbital(circle[circle.Count - 1], circle[0]);
            Require(
                PipeTypes.GetConnectedNodes(factory, circle[0]).Length == circle.Count,
                "orbital weak component " + itemId
            );
            Require(OrbitalComplete(circle[0]), "full planetary ring " + itemId);
            Require(
                !PipeApi.TryInsert(factory, circle[0], 1000, 1),
                "orbital structure rejects fluid inventory " + itemId
            );
            if (itemId == 6516)
            {
                Require(
                    PipeTypes
                        .GetOutgoingNeighbors(factory, circle[0])
                        .SequenceEqual(new[] { circle[1] }),
                    "particle successor identity"
                );
                Require(
                    PipeTypes
                        .GetIncomingNeighbors(factory, circle[0])
                        .SequenceEqual(new[] { circle[circle.Count - 1] }),
                    "particle predecessor identity"
                );
            }
            AccessTools
                .Method(orbitalIntegration, "Schedule")
                .Invoke(null, new object[] { factory, circle[0] });
            AccessTools.Method(orbitalIntegration, "Update").Invoke(null, null);
            Require(OrbitalCompletionState(circle[0]), "native orbital completion flag " + itemId);
            Type connections = typeof(PipeApi).Assembly.GetType("FluidPipes.PipeConnections", true);
            bool disconnected = (bool)
                AccessTools
                    .Method(connections, "Disconnect")
                    .Invoke(null, new object[] { factory, circle[0], circle[1] });
            Require(
                disconnected && !OrbitalComplete(circle[0]),
                "cut invalidates planetary ring " + itemId
            );
            AccessTools
                .Method(orbitalIntegration, "Schedule")
                .Invoke(null, new object[] { factory, circle[0] });
            AccessTools.Method(orbitalIntegration, "Update").Invoke(null, null);
            Require(
                !OrbitalCompletionState(circle[0]),
                "cut clears native completion flag " + itemId
            );
            ConnectOrbital(circle[0], circle[1]);
            Require(OrbitalComplete(circle[0]), "reconnect restores planetary ring " + itemId);
            Record(
                "PASS orbital ring item="
                    + itemId
                    + " nodes="
                    + circle.Count
                    + " close/cut/reconnect/inventory-isolation"
            );
            AccessTools
                .Method(orbitalIntegration, "Schedule")
                .Invoke(null, new object[] { factory, circle[0] });
            AccessTools.Method(orbitalIntegration, "Update").Invoke(null, null);
            Require(
                OrbitalCompletionState(circle[0]),
                "reconnect restores native completion flag " + itemId
            );
            displayArcs.Add(itemId, circle);
        }
        int structure = displayArcs[6515][0];
        int power = displayArcs[6517][0];
        factory.WriteObjectConn(structure, 2, true, power, 2);
        Require(
            !PipeTypes.GetNeighbors(factory, structure).Contains(power),
            "orbital families remain separate"
        );
        Record("PASS orbital cross-family isolation");
        CheckOrbitalTextureCoordinates();
    }

    private void InitializeOrbitalScene()
    {
        Assembly orbitalAssembly = Chainloader
            .PluginInfos[OrbitalRingGuid]
            .Instance.GetType()
            .Assembly;
        orbitalIntegration = orbitalAssembly.GetType(
            "ProjectOrbitalRing.Compatibility.OrbitalPipeIntegration",
            !NativeOrbitalBelts
        );
        orbitalPositions = orbitalAssembly.GetType(
            "ProjectOrbitalRing.Patches.Logic.OrbitalRing.PosTool",
            true
        );
        orbitalLayout = AccessTools
            .Method(orbitalPositions, "GetLayout")
            .Invoke(null, new object[] { factory.planet });
        Type managerType = orbitalAssembly.GetType(
            "ProjectOrbitalRing.Patches.Logic.OrbitalRing.OrbitalStationManager",
            true
        );
        object manager = AccessTools.Property(managerType, "Instance").GetValue(null);
        AccessTools
            .Method(managerType, "AddPlanetId")
            .Invoke(manager, new[] { (object)factory.planetId, orbitalLayout });
        factory.PlanetReformAll(1, 0, true);
    }

    private void PrepareOrbitalAppearance()
    {
        factory = GameMain.localPlanet.factory;
        radial = GameMain.mainPlayer.position.normalized;
        east = Vector3.Cross(radial, Vector3.up).normalized;
        north = Vector3.Cross(east, radial).normalized;
        InitializeOrbitalScene();
        double angleUnit = (double)
            AccessTools.Field(orbitalLayout.GetType(), "AngleUnit").GetValue(orbitalLayout);
        int nodeCount = (int)Math.Round(360 / angleUnit);
        bool sevenRings = Environment.GetEnvironmentVariable("FluidPipesSevenOrbitalRings") == "1";
        if (sevenRings && Mathf.Abs(factory.planet.realRadius - 200) > 0.01f)
        {
            throw new InvalidOperationException(
                "The orbital render scene requires planet radius 200."
            );
        }
        int[][] specifications = sevenRings
            ? new[]
            {
                new[] { 6515, 1, 1 },
                new[] { 6515, 2, 1 },
                new[] { 6515, 1, -1 },
                new[] { 6515, 2, -1 },
                new[] { 6516, 1, 1 },
                new[] { 6516, 1, -1 },
                new[] { 6517, 2, 1 },
            }
            : new[] { new[] { 6515, 0, 1 }, new[] { 6517, 0, 1 }, new[] { 6516, 0, 1 } };
        foreach (int[] specification in specifications)
        {
            int itemId = specification[0];
            var circle = new List<int>();
            for (int index = 0; index < nodeCount; index++)
            {
                circle.Add(
                    PlaceOrbital(
                        itemId,
                        index * 360f / nodeCount,
                        specification[1],
                        specification[2]
                    )
                );
                if (index > 0)
                {
                    ConnectOrbital(circle[index - 1], circle[index]);
                }
            }
            ConnectOrbital(circle[circle.Count - 1], circle[0]);
            if (!displayArcs.ContainsKey(itemId))
            {
                displayArcs.Add(itemId, circle);
            }
            Record(
                "SCENE ring item="
                    + itemId
                    + " band="
                    + specification[1]
                    + " hemisphere="
                    + specification[2]
                    + " nodes="
                    + nodeCount
            );
            if (!NativeOrbitalBelts)
            {
                AccessTools
                    .Method(orbitalIntegration, "Schedule")
                    .Invoke(null, new object[] { factory, circle[0] });
            }
        }
        if (!NativeOrbitalBelts)
        {
            AccessTools.Method(orbitalIntegration, "Update").Invoke(null, null);
        }
        else
        {
            for (int entityId = 1; entityId < factory.entityCursor; entityId++)
            {
                int beltId = factory.entityPool[entityId].beltId;
                if (beltId > 0)
                {
                    factory.cargoTraffic.AlterBeltRenderer(
                        beltId,
                        factory.entityPool,
                        factory.planet.physics.colChunks
                    );
                }
            }
        }
        Record("RENDER_MODE " + (NativeOrbitalBelts ? "native-belts" : "pipe-models"));
        Record("SCENE complete orbital rings on full foundation; audio muted");
    }

    private IEnumerator CaptureOrbitalAppearance()
    {
        if (Environment.GetEnvironmentVariable("FluidPipesOrbitalRenderProfile") == "1")
        {
            yield return GuardProfile(ProfileOrbitalRendering());
            if (!finished)
            {
                Finish(null);
            }
            yield break;
        }
        for (int frame = 0; frame < 30; frame++)
        {
            yield return null;
        }
        foreach (int itemId in new[] { 6515, 6517, 6516 })
        {
            if (
                !TryCaptureOrbitalView(
                    (camera, target) => CaptureCompleteOrbitalSegment(camera, target, itemId)
                )
            )
            {
                yield break;
            }
            yield return null;
        }
        if (!TryCaptureOrbitalView(CaptureAcceleratorDetail))
        {
            yield break;
        }
        yield return null;
        if (!TryCaptureOrbitalView(CaptureOrbitalComparison))
        {
            yield break;
        }
        yield return null;
        if (!TryCaptureOrbitalView(CaptureOrbitalOverview))
        {
            yield break;
        }
        Finish(null);
    }

    private IEnumerator GuardProfile(IEnumerator profile)
    {
        while (true)
        {
            object next;
            try
            {
                if (!profile.MoveNext())
                {
                    yield break;
                }
                next = profile.Current;
            }
            catch (Exception error)
            {
                Finish(error);
                yield break;
            }
            yield return next;
        }
    }

    private void CaptureAcceleratorDetail(Camera camera, RenderTexture target)
    {
        Type layoutType = orbitalIntegration.Assembly.GetType(
            "ProjectOrbitalRing.Compatibility.OrbitalTextureLayout",
            true
        );
        object layout = AccessTools
            .Method(layoutType, "FromBand")
            .Invoke(
                null,
                new object[] { factory.planet, 6516, 1, OrbitalDisplayGeometry(6516).CenterHeight }
            );
        int repeats = (int)AccessTools.Field(layoutType, "RepeatCount").GetValue(layout);
        int facilities = Math.Max(1, Mathf.RoundToInt(repeats / 12f));
        List<int> circle = displayArcs[6516];
        float index = Mathf.Round(facilities * 0.25f) / facilities * circle.Count;
        int first = Mathf.FloorToInt(index);
        Vector3 focus = Vector3.Lerp(
            OrbitalDisplayCenter(circle[first]),
            OrbitalDisplayCenter(circle[(first + 1) % circle.Count]),
            index - first
        );
        Vector3 localUp = focus.normalized;
        Vector3 localEast = Vector3.Cross(Vector3.up, localUp).normalized;
        Vector3 localNorth = Vector3.Cross(localUp, localEast).normalized;
        Vector3 viewOffset = localUp * 5 - localNorth * 9 - localEast * 13;
        camera.orthographicSize = 1.85f;
        camera.transform.SetPositionAndRotation(
            focus + viewOffset,
            Quaternion.LookRotation(-viewOffset, localUp)
        );
        SaveOrbitalView(camera, target, "orbital-accelerator-detail", 6516);
    }

    private void CaptureCompleteOrbitalSegment(Camera camera, RenderTexture target, int itemId)
    {
        List<int> circle = displayArcs[itemId];
        Vector3 focus = OrbitalDisplayCenter(circle[circle.Count / 4]);
        Vector3 localUp = focus.normalized;
        Vector3 localEast = Vector3.Cross(Vector3.up, localUp).normalized;
        Vector3 localNorth = Vector3.Cross(localUp, localEast).normalized;
        Vector3 viewOffset =
            localUp * (itemId == 6517 ? 12 : 8)
            - localNorth * (itemId == 6517 ? 14 : 18)
            - localEast * 7;
        camera.orthographicSize = 3.2f;
        camera.transform.SetPositionAndRotation(
            focus + viewOffset,
            Quaternion.LookRotation(-viewOffset, localUp)
        );
        RecordOrbitalMaterials(itemId);
        Record("SCENE ring=" + itemId + " complete=" + OrbitalCompletionState(circle[0]));
        SaveOrbitalView(camera, target, "orbital-appearance-" + itemId, itemId);
    }

    private bool OrbitalComplete(int root)
    {
        return (bool)
            AccessTools
                .Method(orbitalIntegration, "IsComplete")
                .Invoke(null, new object[] { factory, root });
    }

    private bool OrbitalCompletionState(int entityId)
    {
        Type managerType = orbitalIntegration.Assembly.GetType(
            "ProjectOrbitalRing.Patches.Logic.OrbitalRing.OrbitalStationManager",
            true
        );
        object manager = AccessTools.Property(managerType, "Instance").GetValue(null);
        object data = AccessTools
            .Method(managerType, "GetPlanetOrbitalRingData")
            .Invoke(manager, new object[] { factory.planetId });
        object mark = AccessTools
            .Method(orbitalPositions, "CalculateRingPosMark")
            .Invoke(null, new[] { (object)factory.entityPool[entityId].pos, orbitalLayout });
        int band = (int)AccessTools.Field(mark.GetType(), "Item1").GetValue(mark);
        int ringIndex = (int)AccessTools.Field(mark.GetType(), "Item2").GetValue(mark);
        var rings = (IList)AccessTools.Property(data.GetType(), "Rings").GetValue(data);
        object ring = rings[ringIndex];
        int itemId = factory.entityPool[entityId].protoId;
        string fieldName =
            itemId == 6516 ? "isLowInsideRingComplete"
            : itemId == 6517 ? "isLowOutsideRingComplete"
            : band == 1 ? "isInsideRingComplete"
            : "isOutsideRingComplete";
        return (bool)AccessTools.Field(ring.GetType(), fieldName).GetValue(ring);
    }

    private int PlaceOrbital(int itemId, float longitude, int band = 0, int hemisphere = 1)
    {
        float radians = longitude * Mathf.Deg2Rad;
        float radius = factory.planet.realRadius + 0.2f + (itemId == 6515 ? 48 : 43) * 1.3333333f;
        var candidate =
            new Vector3(Mathf.Cos(radians), hemisphere * 0.05f, Mathf.Sin(radians)).normalized
            * radius;
        int side =
            band != 0 ? band
            : itemId == 6516 ? 1
            : itemId == 6517 ? 2
            : 0;
        Vector3 position = (Vector3)
            AccessTools
                .Method(orbitalPositions, "BeltShouldBeAdsorb")
                .Invoke(null, new[] { (object)candidate, Vector3.zero, side, orbitalLayout });
        Vector3 forward = Vector3.Cross(Vector3.up, position).normalized;
        return factory.AddEntityDataWithComponents(
            new EntityData
            {
                protoId = (short)itemId,
                modelIndex = (short)LDB.items.Select(itemId).ModelIndex,
                pos = position,
                rot = Quaternion.LookRotation(-forward, position.normalized),
                alt = position.magnitude,
                localized = true,
            },
            0
        );
    }

    private void ConnectOrbital(int from, int to)
    {
        factory.WriteObjectConn(from, 0, true, to, 1);
        if (
            NativeOrbitalBelts
            || (
                PipeTypes.TryGet(factory.entityPool[from].protoId, out var type)
                && type.DirectionMode == PipeDirectionMode.Directed
            )
        )
        {
            CargoTraffic traffic = factory.cargoTraffic;
            int sourceBelt = factory.entityPool[from].beltId;
            BeltComponent source = traffic.beltPool[sourceBelt];
            traffic.AlterBeltConnections(
                sourceBelt,
                factory.entityPool[to].beltId,
                source.backInputId,
                source.leftInputId,
                source.rightInputId
            );
        }
    }

    private IEnumerator CaptureOrbitalComponents()
    {
        for (int frame = 0; frame < 20; frame++)
        {
            yield return null;
        }
        if (!TryCaptureOrbitalView(CaptureOrbitalComparison))
        {
            yield break;
        }
        yield return null;
        if (!TryCaptureOrbitalView(CaptureOrbitalOverview))
        {
            yield break;
        }
        yield return null;

        Exception sceneError = null;
        try
        {
            PrepareIsolatedOrbitalArcs();
        }
        catch (Exception error)
        {
            sceneError = error;
        }
        if (sceneError != null)
        {
            Finish(sceneError);
            yield break;
        }
        for (int frame = 0; frame < 20; frame++)
        {
            yield return null;
        }

        foreach (var arc in displayArcs)
        {
            int itemId = arc.Key;
            if (
                !TryCaptureOrbitalView(
                    (camera, target) => CaptureIsolatedOrbitalArc(camera, target, itemId, false)
                )
            )
            {
                yield break;
            }
            yield return null;
            if (
                !TryCaptureOrbitalView(
                    (camera, target) => CaptureIsolatedOrbitalArc(camera, target, itemId, true)
                )
            )
            {
                yield break;
            }
            yield return null;
        }
        Finish(null);
    }

    private bool TryCaptureOrbitalView(Action<Camera, RenderTexture> capture)
    {
        Camera camera = GameCamera.main;
        Vector3 oldPosition = camera.transform.position;
        Quaternion oldRotation = camera.transform.rotation;
        RenderTexture previousTarget = camera.targetTexture;
        bool oldOrthographic = camera.orthographic;
        float oldSize = camera.orthographicSize;
        var renderTarget = new RenderTexture(1600, 900, 24);
        try
        {
            try
            {
                renderTarget.Create();
                camera.targetTexture = renderTarget;
                camera.orthographic = true;
                capture(camera, renderTarget);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.orthographic = oldOrthographic;
                camera.orthographicSize = oldSize;
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                renderTarget.Release();
                Destroy(renderTarget);
            }
            return true;
        }
        catch (Exception error)
        {
            Finish(error);
            return false;
        }
    }

    private void CaptureOrbitalComparison(Camera camera, RenderTexture renderTarget)
    {
        Vector3 focus = Vector3.zero;
        foreach (var arc in displayArcs)
        {
            focus += OrbitalDisplayCenter(arc.Value[arc.Value.Count / 4]);
        }
        focus /= displayArcs.Count;
        Vector3 localUp = focus.normalized;
        Vector3 localEast = Vector3.Cross(Vector3.up, localUp).normalized;
        Vector3 localNorth = Vector3.Cross(localUp, localEast).normalized;
        Vector3 viewOffset = localUp * 14 + localNorth * 20 - localEast * 3;
        camera.orthographicSize = 7.2f;
        camera.transform.SetPositionAndRotation(
            focus + viewOffset,
            Quaternion.LookRotation(-viewOffset, localUp)
        );
        SaveOrbitalView(camera, renderTarget, "orbital-types-comparison");
    }

    private void CaptureOrbitalOverview(Camera camera, RenderTexture renderTarget)
    {
        float planetRadius = factory.planet.realRadius;
        Vector3 overviewPosition = new Vector3(-0.3f, 0.55f, -1).normalized * planetRadius * 4;
        camera.orthographicSize = planetRadius * 1.6f;
        camera.transform.SetPositionAndRotation(
            overviewPosition,
            Quaternion.LookRotation(-overviewPosition, Vector3.up)
        );
        SaveOrbitalView(camera, renderTarget, "orbital-ring-overview");
    }

    private void PrepareIsolatedOrbitalArcs()
    {
        int typeIndex = 0;
        foreach (var entry in displayArcs.ToArray())
        {
            List<int> circle = entry.Value;
            float longitude = 70 + typeIndex * 20;
            int centerIndex = Mathf.RoundToInt(longitude / 360 * circle.Count);
            float nodeDistance = Vector3.Distance(
                OrbitalDisplayCenter(circle[centerIndex]),
                OrbitalDisplayCenter(circle[centerIndex + 1])
            );
            int halfSpan = Math.Max(3, Mathf.RoundToInt(7 / nodeDistance));
            var retained = circle.GetRange(centerIndex - halfSpan, halfSpan * 2 + 1);
            var retainedIds = new HashSet<int>(retained);
            for (int index = circle.Count - 1; index >= 0; index--)
            {
                int entityId = circle[index];
                if (retainedIds.Contains(entityId))
                {
                    continue;
                }
                int returnedItem = 0;
                factory.DismantleFinally(GameMain.mainPlayer, entityId, ref returnedItem);
            }
            displayArcs[entry.Key] = retained;
            AccessTools
                .Method(orbitalIntegration, "Schedule")
                .Invoke(null, new object[] { factory, retained[0] });
            Require(
                PipeTypes.GetConnectedNodes(factory, retained[0]).Length == retained.Count
                    && !OrbitalComplete(retained[0]),
                "isolated orbital display arc retains its native open-chain topology " + entry.Key
            );
            Record(
                "SCENE orbital display item="
                    + entry.Key
                    + " longitude="
                    + longitude
                    + " nodes="
                    + retained.Count
                    + " native_state=open"
            );
            typeIndex++;
        }
        AccessTools.Method(orbitalIntegration, "Update").Invoke(null, null);
    }

    private void CaptureIsolatedOrbitalArc(
        Camera camera,
        RenderTexture renderTarget,
        int itemId,
        bool sectionView
    )
    {
        List<int> arc = displayArcs[itemId];
        Vector3 firstCenter = OrbitalDisplayCenter(arc[0]);
        Vector3 lastCenter = OrbitalDisplayCenter(arc[arc.Count - 1]);
        Vector3 focus = (firstCenter + lastCenter) * 0.5f;
        Vector3 axis = (lastCenter - firstCenter).normalized;
        Vector3 localUp = focus.normalized;
        Vector3 side = Vector3.Cross(localUp, axis).normalized;
        PipeGeometryProfile geometry = OrbitalDisplayGeometry(itemId);
        Vector3 viewOffset;
        if (sectionView)
        {
            focus = firstCenter + axis * 0.8f;
            viewOffset = localUp * 5 + side * 2.8f - axis * 10;
            camera.orthographicSize = Math.Max(2.2f, geometry.Radius * 1.9f);
        }
        else
        {
            viewOffset = localUp * 14 + side * 16 - axis * 9;
            camera.orthographicSize = Math.Max(3.7f, geometry.Radius * 3.1f);
        }
        camera.transform.SetPositionAndRotation(
            focus + viewOffset,
            Quaternion.LookRotation(-viewOffset, localUp)
        );
        RecordOrbitalMaterials(itemId);
        SaveOrbitalView(
            camera,
            renderTarget,
            (sectionView ? "orbital-section-" : "orbital-pipe-") + itemId
        );
    }

    private Vector3 OrbitalDisplayCenter(int entityId)
    {
        EntityData entity = factory.entityPool[entityId];
        return entity.pos
            + entity.pos.normalized * OrbitalDisplayGeometry(entity.protoId).CenterHeight;
    }

    private static PipeGeometryProfile OrbitalDisplayGeometry(int itemId)
    {
        Type registry = typeof(PipeApi).Assembly.GetType("FluidPipes.PipeTypeRegistry", true);
        object entry = AccessTools
            .Method(registry, "Get", new[] { typeof(int) })
            .Invoke(null, new object[] { itemId });
        var definition = (PipeTypeDefinition)
            AccessTools.Field(entry.GetType(), "Definition").GetValue(entry);
        return definition.Geometry;
    }

    private void SaveOrbitalView(
        Camera camera,
        RenderTexture renderTarget,
        string name,
        int itemId = 0
    )
    {
        factory.planet.factoryModel.DrawInstancedBatches(camera, true);
        AccessTools
            .Method(
                typeof(PipeApi).Assembly.GetType("FluidPipes.PipeVisuals", true),
                itemId == 0 ? "Draw" : "DrawType"
            )
            .Invoke(
                null,
                itemId == 0
                    ? new object[] { factory, camera }
                    : new object[] { factory, camera, itemId }
            );
        camera.Render();
        RenderTexture previousActive = RenderTexture.active;
        var pixels = new Texture2D(
            renderTarget.width,
            renderTarget.height,
            TextureFormat.RGB24,
            false
        );
        try
        {
            RenderTexture.active = renderTarget;
            pixels.ReadPixels(new Rect(0, 0, renderTarget.width, renderTarget.height), 0, 0);
            pixels.Apply();
            string path = Path.Combine(Path.GetDirectoryName(output), name + ".png");
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            Record("CAPTURE " + path);
        }
        finally
        {
            RenderTexture.active = previousActive;
            Destroy(pixels);
        }
    }

    private void RecordOrbitalMaterials(int itemId)
    {
        Type library = typeof(PipeApi).Assembly.GetType("FluidPipes.PipeMaterialLibrary", true);
        var surfaces = (PipeMaterialSet)
            AccessTools.Method(library, "Get").Invoke(null, new object[] { itemId });
        Material wall = surfaces.Wall;
        Record(
            "MATERIAL item="
                + itemId
                + " wall="
                + wall.shader.name
                + " color="
                + wall.color
                + " mode="
                + wall.GetFloat("_Mode")
                + " blend="
                + wall.GetInt("_SrcBlend")
                + "/"
                + wall.GetInt("_DstBlend")
                + " depth="
                + wall.GetInt("_ZWrite")
                + " keywords="
                + string.Join(",", wall.shaderKeywords)
                + " core="
                + surfaces.Core.shader.name
                + " emission="
                + surfaces.Core.GetColor("_EmissionColor")
        );
    }
}
