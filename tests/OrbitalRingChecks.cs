using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("local.fluidpipes.orbitalring.checks", "Fluid Pipes OrbitalRing Checks", "1.0.0")]
[BepInDependency(PipeApi.PluginGuid)]
[BepInDependency(OrbitalRingGuid, BepInDependency.DependencyFlags.SoftDependency)]
public sealed partial class OrbitalRingChecks : BaseUnityPlugin
{
    private const string OrbitalRingGuid = "org.ProfessorCat305.OrbitalRing";
    private const int InfiniteTankItemId = 6223;
    private readonly List<string> results = new List<string>();
    private bool started;
    private bool tested;
    private bool finished;
    private string output;
    private float startupTime;
    private PlanetFactory factory;
    private Vector3 radial;
    private Vector3 east;
    private Vector3 north;

    private void Awake()
    {
        AudioListener.volume = 0;
        AudioListener.pause = true;
        output = Path.Combine(Path.GetDirectoryName(Info.Location), "orbitalring-results.txt");
        string documents = Path.Combine(Path.GetDirectoryName(output), "test-documents");
        Directory.CreateDirectory(documents);
        GameConfig.overrideDocumentFolder = documents.Replace('\\', '/') + "/";
        foreach (
            string fieldName in new[]
            {
                "gameDocument",
                "gameOption",
                "gameXMLOption",
                "gameSavePath",
                "achievementPath",
                "propertyPath",
                "gameScreenshot",
            }
        )
        {
            AccessTools.Field(typeof(GameConfig), fieldName)?.SetValue(null, null);
        }

        Application.runInBackground = true;
        startupTime = Time.realtimeSinceStartup;
        new Harmony("local.fluidpipes.orbitalring.checks").PatchAll(typeof(OrbitalRingChecks));
        Record(
            "LOADED OrbitalRing="
                + (
                    Chainloader.PluginInfos.TryGetValue(OrbitalRingGuid, out var orbitalRing)
                        ? orbitalRing.Metadata.Version.ToString()
                        : "absent"
                )
                + " FluidPipes="
                + Chainloader.PluginInfos[PipeApi.PluginGuid].Metadata.Version
        );
    }

    [HarmonyPrefix, HarmonyPatch(typeof(VFPreload), "IsSplashSolid")]
    private static bool Splash(ref bool __result)
    {
        __result = true;
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VFPreload), "Update")]
    private static void Preload(VFPreload __instance)
    {
        __instance.time = __instance.splashTime + 1;
        __instance.musicReached = true;
        __instance.clicked = true;
    }

    private void Update()
    {
        AudioListener.volume = 0;
        AudioListener.pause = true;
        if (finished)
        {
            return;
        }

        try
        {
            if (Time.realtimeSinceStartup - startupTime > 180)
            {
                throw new TimeoutException("OrbitalRing game startup timed out.");
            }

            if (!started && VFPreload.done && PipeApi.IsReady && UIRoot.instance != null)
            {
                started = true;
                Record("PROTOTYPES pipe=" + PipeApi.PipeItemId + " valve=" + PipeApi.ValveItemId);
                var description = new GameDesc();
                description.SetForNewGame(UniverseGen.algoVersion, 20261022, 32, 1, 1);
                description.isSandboxMode = true;
                description.isPeaceMode = true;
                description.achievementEnable = false;
                DSPGame.StartGameSkipPrologue(description);
            }

            if (
                !tested
                && started
                && GameMain.isRunning
                && GameMain.localPlanet?.factoryLoaded == true
            )
            {
                tested = true;
                RunChecks();
                if (Environment.GetEnvironmentVariable("FluidPipesOrbitalComponents") == "1")
                {
                    RunOrbitalComponentChecks();
                    StartCoroutine(CaptureOrbitalComponents());
                }
                else
                {
                    Finish(null);
                }
            }
        }
        catch (Exception error)
        {
            Finish(error);
        }
    }

    private void RunChecks()
    {
        factory = GameMain.localPlanet.factory;
        radial = GameMain.mainPlayer.position.normalized;
        east = Vector3.Cross(radial, Vector3.up).normalized;
        north = Vector3.Cross(east, radial).normalized;
        Record("GAME planet=" + factory.planetId + " tick=" + GameMain.gameTick);
        CheckBuildBarPlacement();
        Record(
            "MEASUREMENT ticks_per_case=600 nominal_seconds=10 storage_step=FactoryStorage.GameTickTank"
        );
        int[] fluidItems = LDB
            .items.dataArray.Where(item => item.IsFluid)
            .Select(item => item.ID)
            .ToArray();
        Record(
            "FLUIDS total="
                + fluidItems.Length
                + " admitted="
                + fluidItems.Count(PipeApi.IsItemAllowed)
        );
        if (fluidItems.Any(itemId => !PipeApi.IsItemAllowed(itemId)))
        {
            throw new InvalidOperationException(
                "A registered fluid is absent from pipe admission."
            );
        }

        var resolveColor = AccessTools.Method(
            typeof(PipeApi).Assembly.GetType("FluidPipes.FluidAppearance", true),
            "LinearColor"
        );
        int fallbackColors = 0;
        foreach (int itemId in fluidItems)
        {
            int offset = checked((int)GameMain.iconSet.itemIconIndex[itemId] * 40 + 20);
            float[] descriptors = GameMain.iconSet.itemDescArr;
            var native = new Color(
                descriptors[offset],
                descriptors[offset + 1],
                descriptors[offset + 2],
                descriptors[offset + 3]
            );
            if (native == Color.clear)
            {
                offset -= 20;
                native = new Color(
                    descriptors[offset],
                    descriptors[offset + 1],
                    descriptors[offset + 2]
                );
                fallbackColors++;
            }
            Color expected = native.linear * 1.5f;
            Color actual = (Color)resolveColor.Invoke(null, new object[] { itemId });
            if (
                new Vector3(
                    expected.r - actual.r,
                    expected.g - actual.g,
                    expected.b - actual.b
                ).magnitude > 0.003f
            )
            {
                throw new InvalidOperationException(
                    "Fluid color differs from the native tank palette: " + itemId
                );
            }
        }
        Record(
            "FLUID_COLORS checked="
                + fluidItems.Length
                + " native_tank="
                + (fluidItems.Length - fallbackColors)
                + " face_fallback="
                + fallbackColors
        );

        CheckDraggedConstruction(false);
        CheckDraggedConstruction(true);
        if (!Chainloader.PluginInfos.ContainsKey(OrbitalRingGuid))
        {
            return;
        }
        MeasurePipeOutput("continuous_drain", 0, true);
        MeasurePipeOutput("buffered_output", 8, false);
        MeasurePipeOutput("prefilled_drain", 16, true, 5);
        MeasureUnrestrictedPortOutput();
        CheckSourceControls();
        CheckFiniteInventoryAndActivation();
        CheckMultipleSources();
    }

    private void CheckBuildBarPlacement()
    {
        int category = LDB.items.Select(2001).BuildIndex / 100;
        bool hasUpperRow = Chainloader.PluginInfos.TryGetValue(
            "Gnimaerd.DSP.plugin.BuildBarTool",
            out var buildBarPlugin
        );
        var upperRow = hasUpperRow
            ? (ItemProto[,])
                AccessTools.Field(buildBarPlugin.Instance.GetType(), "protos").GetValue(null)
            : null;
        var nativeRow = (ItemProto[,])
            AccessTools.Field(typeof(UIBuildMenu), "protos").GetValue(null);
        foreach (int itemId in new[] { PipeApi.PipeItemId, PipeApi.ValveItemId })
        {
            var item = LDB.items.Select(itemId);
            if (hasUpperRow)
            {
                int column = item.BuildIndex % 100 - 20;
                Require(
                    item.BuildIndex / 100 == category && column >= 1 && column <= 10,
                    "upper transport-row index"
                );
                Require(upperRow[category, column]?.ID == itemId, "upper transport-row binding");
            }
            else
            {
                Require(
                    nativeRow[item.BuildIndex / 100, item.BuildIndex % 100]?.ID == itemId,
                    "native construction-row binding"
                );
            }
            Record(
                "BUILD_BAR item=" + itemId + " index=" + item.BuildIndex + " upper=" + hasUpperRow
            );
        }
    }

    private void CheckDraggedConstruction(bool reverseCompletion)
    {
        var player = GameMain.mainPlayer;
        Vector3 oldPosition = player.position;
        int oldStage = player.controller.cmd.stage;
        float oldReach = player.mecha.buildArea;
        var tool = new BuildTool_Path();
        tool._Init(GameMain.data);
        tool.factory = factory;
        tool.planet = factory.planet;
        tool.handItem = LDB.items.Select(PipeApi.PipeItemId);
        tool.handPrefabDesc = tool.handItem.prefabDesc;
        try
        {
            float side = reverseCompletion ? -1 : 1;
            Vector3 start = Position(side * 12, -18);
            Vector3 end = Position(side * 22, -18, reverseCompletion ? 2 : 0);
            player.position = start;
            player.mecha.buildArea = 100;
            player.TryAddItemToPackage(PipeApi.PipeItemId, 100, 0, false, 0);
            player.controller.cmd.stage = 1;
            tool.startObjectId = tool.castObjectId = 0;
            tool.startTarget = start;
            tool.cursorTarget = tool.castGroundPosSnapped = end;
            tool.cursorValid = true;
            tool.DeterminePreviews();
            AccessTools
                .Field(typeof(BuildTool), "tmpPackage")
                .SetValue(tool, new StorageComponent(player.package.size));
            AccessTools.Field(typeof(BuildTool), "tmpInhandId").SetValue(tool, PipeApi.PipeItemId);
            AccessTools.Field(typeof(BuildTool), "tmpInhandCount").SetValue(tool, 100);
            Require(tool.buildPreviews.Count > 2, "dragged path previews");
            Require(
                tool.CheckBuildConditions(),
                "dragged path build conditions: "
                    + string.Join(",", tool.buildPreviews.Select(preview => preview.condition))
            );
            tool.CreatePrebuilds();
            int[] pending = tool.buildPreviews.Select(preview => preview.objId).ToArray();
            Vector3[] positions = tool.buildPreviews.Select(preview => preview.lpos).ToArray();
            Require(pending.All(objectId => objectId < 0), "dragged path prebuild creation");
            RequireAdjacentConnections(pending);
            int[] currentObjects = (int[])pending.Clone();
            foreach (int objectId in reverseCompletion ? pending.Reverse() : pending)
            {
                int index = Array.IndexOf(pending, objectId);
                factory.BuildFinally(player, -objectId, false, false);
                currentObjects[index] = Enumerable
                    .Range(1, factory.entityCursor - 1)
                    .Single(entityId =>
                        factory.entityPool[entityId].id == entityId
                        && factory.entityPool[entityId].protoId == PipeApi.PipeItemId
                        && Vector3.Distance(factory.entityPool[entityId].pos, positions[index])
                            < 0.001f
                    );
                RequireAdjacentConnections(currentObjects);
            }
            int[] completed = positions
                .Select(position =>
                    Enumerable
                        .Range(1, factory.entityCursor - 1)
                        .Single(entityId =>
                            factory.entityPool[entityId].id == entityId
                            && factory.entityPool[entityId].protoId == PipeApi.PipeItemId
                            && Vector3.Distance(factory.entityPool[entityId].pos, position) < 0.001f
                        )
                )
                .ToArray();
            foreach (int entityId in completed)
            {
                Require(
                    PipeApi.TryGetNetwork(factory, entityId, out var network),
                    "completed path inventory"
                );
                Record(
                    "DRAG_NODE reverse="
                        + reverseCompletion
                        + " entity="
                        + entityId
                        + " nodes="
                        + network.PipeNodeCount
                        + " expected="
                        + completed.Length
                );
                Require(
                    network.PipeNodeCount == completed.Length,
                    "dragged path remains connected after completion"
                );
            }
            Require(PipeApi.TryInsert(factory, completed[0], 1000, 10), "dragged path insertion");
            Require(
                PipeApi.TryTake(
                    factory,
                    completed[completed.Length - 1],
                    10,
                    out var extracted,
                    1000
                )
                    && extracted.Count == 10,
                "remote extraction across dragged path"
            );
            Record(
                "DRAG_CONSTRUCTION reverse="
                    + reverseCompletion
                    + " nodes="
                    + completed.Length
                    + " connected=pass remote_io=pass"
            );
        }
        finally
        {
            player.position = oldPosition;
            player.controller.cmd.stage = oldStage;
            player.mecha.buildArea = oldReach;
            tool._Free();
        }
    }

    private void RequireAdjacentConnections(int[] path)
    {
        for (int index = 1; index < path.Length; index++)
        {
            bool connected = false;
            for (int slot = 0; slot < 4; slot++)
            {
                factory.ReadObjectConn(
                    path[index - 1],
                    slot,
                    out _,
                    out int neighbor,
                    out int neighborSlot
                );
                if (neighbor != path[index])
                {
                    continue;
                }
                factory.ReadObjectConn(
                    neighbor,
                    neighborSlot,
                    out _,
                    out int back,
                    out int backSlot
                );
                connected = back == path[index - 1] && backSlot == slot;
            }
            Require(
                connected,
                "reciprocal dragged path connection " + path[index - 1] + " to " + path[index]
            );
        }
    }

    private Vector3 Position(float x, float z, float height = 0)
    {
        return (radial * factory.planet.realRadius + east * x + north * z).normalized
            * (factory.planet.realRadius + 0.2f + height);
    }

    private void MeasurePipeOutput(string name, float offset, bool drain, int prefill = 0)
    {
        int tankEntity = Place(InfiniteTankItemId, offset, 0);
        int pipeEntity = Place(PipeApi.PipeItemId, offset, 5);
        int tankId = factory.entityPool[tankEntity].tankId;
        int pipeBelt = factory.entityPool[pipeEntity].beltId;
        Activate(tankId);
        factory.WriteObjectConn(tankEntity, 0, true, pipeEntity, 1);
        factory.factoryStorage.SetTankBelt(tankId, pipeBelt, 0, true);
        if (prefill > 0 && !PipeApi.TryInsert(factory, pipeEntity, 1000, prefill))
        {
            throw new InvalidOperationException("Pipe prefill failed.");
        }

        RecordSnapshot(name + "_initial", tankId, pipeEntity, 0);
        long extracted = 0;
        for (int tick = 1; tick <= 600; tick++)
        {
            factory.factoryStorage.GameTickTank();
            if (drain && PipeApi.TryTake(factory, pipeEntity, 10000, out var amount, 1000))
            {
                extracted += amount.Count;
            }

            if (tick <= 3 || tick == 60 || tick == 600)
            {
                RecordSnapshot(name + "_tick" + tick, tankId, pipeEntity, extracted);
            }
        }

        long expectedExtracted = drain ? 300 + prefill : 0;
        Require(extracted == expectedExtracted, name + " extraction rate");
        Require(PipeApi.TryGetNetwork(factory, pipeEntity, out var inventory), name + " inventory");
        Require(inventory.Inventory.Count == (drain ? 0 : 10), name + " buffered inventory");
        Require(inventory.Inventory.Capacity == 10, name + " source capacity isolation");
        Require(
            factory.factoryStorage.tankPool[tankId].fluidCount == 1,
            name + " activation sentinel"
        );
        Require(factory.factoryStorage.tankPool[tankId].fluidId == 1000, name + " source item");

        factory.factoryStorage.DisconnectToTank(tankId, pipeBelt);
        factory.ClearObjectConn(tankEntity, 0);
    }

    private void MeasureUnrestrictedPortOutput()
    {
        int tankEntity = Place(InfiniteTankItemId, 24, 0);
        int beltEntity = Place(2003, 24, 5);
        int tankId = factory.entityPool[tankEntity].tankId;
        int beltId = factory.entityPool[beltEntity].beltId;
        Activate(tankId);
        factory.WriteObjectConn(tankEntity, 0, true, beltEntity, 1);
        factory.factoryStorage.SetTankBelt(tankId, beltId, 0, true);
        ref BeltComponent belt = ref factory.cargoTraffic.beltPool[beltId];
        CargoPath path = factory.cargoTraffic.pathPool[belt.segPathId];
        int bufferLength = (int)AccessTools.Field(typeof(CargoPath), "bufferLength").GetValue(path);
        long extracted = 0;
        for (int tick = 1; tick <= 600; tick++)
        {
            factory.factoryStorage.GameTickTank();
            int itemId = path.TryPickItem(0, bufferLength, out byte stack, out byte points);
            if (itemId != 0)
            {
                if (itemId != 1000)
                {
                    throw new InvalidOperationException("Unexpected belt output item.");
                }
                extracted += stack;
            }
        }

        Record(
            "PORT_PROBE ticks=600 extracted="
                + extracted
                + " tank_count="
                + factory.factoryStorage.tankPool[tankId].fluidCount
        );
        Require(extracted == 600, "unrestricted native belt output behavior");
    }

    private void CheckSourceControls()
    {
        int tankEntity = Place(InfiniteTankItemId, 32, 0);
        int pipeEntity = Place(PipeApi.PipeItemId, 32, 5);
        int tankId = factory.entityPool[tankEntity].tankId;
        int beltId = factory.entityPool[pipeEntity].beltId;
        Activate(tankId);
        factory.WriteObjectConn(tankEntity, 0, true, pipeEntity, 1);
        factory.factoryStorage.SetTankBelt(tankId, beltId, 0, true);
        ref TankComponent tank = ref factory.factoryStorage.tankPool[tankId];
        tank.outputSwitch = false;
        for (int tick = 0; tick < 60; tick++)
        {
            factory.factoryStorage.GameTickTank();
        }
        Require(
            PipeApi.TryGetNetwork(factory, pipeEntity, out var stopped),
            "stopped source inventory"
        );
        Require(stopped.Inventory.Count == 0, "source output switch");
        tank.outputSwitch = true;
        long resumedCount = DrainSource(pipeEntity, 60);
        Require(resumedCount == 30, "source restart rate");
        Require(PipeApi.TryInsert(factory, pipeEntity, 1007, 5), "different-fluid fixture");
        for (int tick = 0; tick < 60; tick++)
        {
            factory.factoryStorage.GameTickTank();
        }
        Require(
            PipeApi.TryTake(factory, pipeEntity, 100, out var oil, 1007),
            "different-fluid extraction"
        );
        Require(oil.Count == 5, "different-fluid inventory conservation");
        Require(tank.fluidCount == 1 && tank.fluidId == 1000, "source survives backpressure");
        long recoveredCount = DrainSource(pipeEntity, 60);
        Require(recoveredCount == 30, "recovered source rate");
        Record(
            "SOURCE_CONTROLS output_switch=pass restart=pass mixed_fluid=pass backpressure=pass"
        );
    }

    private static void Require(bool condition, string invariant)
    {
        if (!condition)
        {
            throw new InvalidOperationException("OrbitalRing check failed: " + invariant);
        }
    }

    private void CheckFiniteInventoryAndActivation()
    {
        int tankEntity = Place(InfiniteTankItemId, 40, 0);
        int pipeEntity = Place(PipeApi.PipeItemId, 40, 5);
        int tankId = factory.entityPool[tankEntity].tankId;
        int beltId = factory.entityPool[pipeEntity].beltId;
        ref TankComponent tank = ref factory.factoryStorage.tankPool[tankId];
        tank.fluidId = 1000;
        tank.fluidCount = 6;
        tank.fluidInc = 24;
        tank.inputSwitch = true;
        tank.outputSwitch = true;
        factory.WriteObjectConn(tankEntity, 0, true, pipeEntity, 1);
        factory.factoryStorage.SetTankBelt(tankId, beltId, 0, true);
        for (int tick = 0; tick < 60; tick++)
        {
            factory.factoryStorage.GameTickTank();
        }
        Require(
            PipeApi.TryTake(factory, pipeEntity, 100, out var finite, 1000),
            "finite source output"
        );
        Require(
            finite.Count == 6 && finite.ProliferatorPoints == 24,
            "finite inventory conservation"
        );
        Require(tank.fluidCount == 0, "finite source exhaustion");
        tank.fluidId = 1000;
        tank.fluidCount = 4999;
        tank.fluidInc = 0;
        factory.ClearObjectConn(tankEntity, 0);
        factory.WriteObjectConn(tankEntity, 0, false, pipeEntity, 0);
        factory.factoryStorage.SetTankBelt(tankId, beltId, 0, false);
        Require(PipeApi.TryInsert(factory, pipeEntity, 1000, 10), "activation supply fixture");
        factory.factoryStorage.GameTickTank();
        factory.factoryStorage.GameTickTank();
        Require(tank.fluidCount == 1, "pipe supply activates native infinite source");
        factory.ClearObjectConn(tankEntity, 0);
        factory.WriteObjectConn(tankEntity, 0, true, pipeEntity, 1);
        factory.factoryStorage.SetTankBelt(tankId, beltId, 0, true);
        PipeApi.TryTake(factory, pipeEntity, 100, out var remaining, 1000);
        for (int tick = 0; tick < 60; tick++)
        {
            factory.factoryStorage.GameTickTank();
        }
        Require(
            PipeApi.TryTake(factory, pipeEntity, 100, out var infinite, 1000),
            "activated pipe output"
        );
        Require(infinite.Count == 10 && tank.fluidCount == 1, "activated source persistence");
        Record(
            "SOURCE_ACTIVATION finite_count=6 finite_points=24 pipe_activation=pass sustained_output=pass"
        );
    }

    private void CheckMultipleSources()
    {
        int firstEntity = Place(InfiniteTankItemId, 48, 0);
        int secondEntity = Place(InfiniteTankItemId, 52, 0);
        int pipeEntity = Place(PipeApi.PipeItemId, 50, 5);
        int firstTank = factory.entityPool[firstEntity].tankId;
        int secondTank = factory.entityPool[secondEntity].tankId;
        int beltId = factory.entityPool[pipeEntity].beltId;
        Activate(firstTank);
        Activate(secondTank);
        factory.WriteObjectConn(firstEntity, 0, true, pipeEntity, 1);
        factory.WriteObjectConn(secondEntity, 0, true, pipeEntity, 2);
        factory.factoryStorage.SetTankBelt(firstTank, beltId, 0, true);
        factory.factoryStorage.SetTankBelt(secondTank, beltId, 0, true);
        long combinedCount = DrainSource(pipeEntity, 60);
        Require(combinedCount == 60, "independent rates for sources sharing one pipe node");
        Record("MULTIPLE_SOURCES ticks=60 extracted=" + combinedCount);
    }

    private long DrainSource(int pipeEntity, int ticks)
    {
        long extracted = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            factory.factoryStorage.GameTickTank();
            if (PipeApi.TryTake(factory, pipeEntity, 100, out var amount, 1000))
            {
                extracted += amount.Count;
            }
        }
        return extracted;
    }

    private void Activate(int tankId)
    {
        ref TankComponent tank = ref factory.factoryStorage.tankPool[tankId];
        tank.fluidId = 1000;
        tank.fluidCount = 5001;
        tank.inputSwitch = true;
        tank.outputSwitch = true;
        UITankWindow window = UIRoot.instance.uiGame.tankWindow;
        window.factory = factory;
        window.storage = factory.factoryStorage;
        window.player = GameMain.mainPlayer;
        window.tankId = tankId;
        window._Open();
        AccessTools.Method(typeof(UITankWindow), "_OnUpdate").Invoke(window, null);
        Record(
            "ACTIVATED tank="
                + tankId
                + " count="
                + tank.fluidCount
                + " ui="
                + window.countText.text
                + " capacity="
                + tank.fluidCapacity
        );
        if (tank.fluidCount != 1)
        {
            throw new InvalidOperationException("Infinite tank activation failed.");
        }
        window._Close();
    }

    private int Place(int itemId, float x, float z)
    {
        Vector3 position =
            (radial * factory.planet.realRadius + east * x + north * z).normalized
            * (factory.planet.realRadius + 0.2f);
        return factory.AddEntityDataWithComponents(
            new EntityData
            {
                protoId = (short)itemId,
                modelIndex = (short)LDB.items.Select(itemId).ModelIndex,
                pos = position,
                rot = Quaternion.LookRotation(north, position.normalized),
                alt = position.magnitude,
                localized = true,
            },
            0
        );
    }

    private void RecordSnapshot(string name, int tankId, int pipeEntity, long extracted)
    {
        if (!PipeApi.TryGetNetwork(factory, pipeEntity, out var snapshot))
        {
            throw new InvalidOperationException("Pipe inventory snapshot is unavailable.");
        }
        TankComponent tank = factory.factoryStorage.tankPool[tankId];
        Record(
            "PIPE "
                + name
                + " extracted="
                + extracted
                + " pipe_count="
                + snapshot.PipeItemCount
                + " shared_count="
                + snapshot.Inventory.Count
                + " containers="
                + snapshot.ContainerCount
                + " tank_count="
                + tank.fluidCount
                + " tank_fluid="
                + tank.fluidId
        );
    }

    private void Record(string message)
    {
        results.Add(message);
        Logger.LogInfo(message);
        File.WriteAllLines(output, results);
    }

    private void Finish(Exception error)
    {
        finished = true;
        Record(error == null ? "Completed=1" : "ERROR " + error);
        Application.Quit(error == null ? 0 : 1);
    }
}
