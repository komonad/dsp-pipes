using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using FluidPipes;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("local.fluidpipes.scene", "Fluid Pipe Scene Capture", "1.0.0")]
[BepInDependency(Plugin.Guid)]
public sealed partial class SceneCapture : BaseUnityPlugin
{
    private static SceneCapture instance;
    private readonly List<string> report = new List<string>();
    private bool started;
    private bool placed;
    private bool cameraLocked;
    private string output;
    private PlanetFactory factory;
    private Vector3 up;
    private Vector3 east;
    private Vector3 north;
    private Vector3 center;
    private Vector3 cameraPosition;
    private Vector3 cameraTarget;
    private Vector3? cameraUp;
    private bool orthographicCapture;
    private RenderTexture target;
    private int pipes;
    private int belts;
    private int materialEntity;
    private int cutA;
    private int cutB;
    private bool connectionsOnly;
    private bool cubeOnly;
    private bool structuresOnly;
    private bool gameplayOnly;
    private bool insertersOnly;
    private bool minimalLineOnly;
    private bool parallelLineOnly;
    private bool machinePortsOnly;
    private bool copperInteractionsOnly;
    private bool windowPipePreviewOnly;
    private bool powerBuildingReferenceOnly;
    private bool builtInContentDisabled;
    private bool fixtureSave;
    private string inspectSave;
    private readonly List<int> galleryNodes = new List<int>();

    private void Awake()
    {
        AudioListener.volume = 0;
        AudioListener.pause = true;
        instance = this;
        output = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Info.Location),
            "scene-output"
        );
        connectionsOnly = Environment.GetEnvironmentVariable("FluidPipesCapture") == "connections";
        cubeOnly = Environment.GetEnvironmentVariable("FluidPipesCapture") == "cube";
        structuresOnly = Environment.GetEnvironmentVariable("FluidPipesCapture") == "structures";
        gameplayOnly = Environment.GetEnvironmentVariable("FluidPipesCapture") == "gameplay";
        insertersOnly = Environment.GetEnvironmentVariable("FluidPipesCapture") == "inserters";
        minimalLineOnly = Environment.GetEnvironmentVariable("FluidPipesCapture") == "minimal-line";
        parallelLineOnly =
            Environment.GetEnvironmentVariable("FluidPipesCapture") == "parallel-line";
        machinePortsOnly =
            Environment.GetEnvironmentVariable("FluidPipesCapture") == "machine-ports";
        copperInteractionsOnly =
            Environment.GetEnvironmentVariable("FluidPipesCapture") == "copper-interactions";
        windowPipePreviewOnly =
            Environment.GetEnvironmentVariable("FluidPipesCapture") == "window-pipes";
        powerBuildingReferenceOnly =
            Environment.GetEnvironmentVariable("FluidPipesCapture") == "power-reference";
        builtInContentDisabled =
            Environment.GetEnvironmentVariable("FluidPipesBuiltInContent") == "0";
        inspectSave = Environment.GetEnvironmentVariable("FluidPipesInspectSave");
        Directory.CreateDirectory(output);
        new Harmony("local.fluidpipes.scene").PatchAll(typeof(SceneCapture));
        var documents = System.IO.Path.Combine(output, "documents");
        Directory.CreateDirectory(documents);
        GameConfig.overrideDocumentFolder = documents.Replace('\\', '/') + "/";
        foreach (
            var field in new[]
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
            AccessTools.Field(typeof(GameConfig), field)?.SetValue(null, null);
        Application.runInBackground = true;
        if (!string.IsNullOrEmpty(inspectSave))
            foreach (string extension in new[] { ".dsv", ".moddsv" })
                File.Copy(
                    System.IO.Path.ChangeExtension(inspectSave, extension),
                    System.IO.Path.Combine(GameConfig.gameSaveFolder, "pipe-inspect" + extension),
                    true
                );
    }

    [HarmonyPrefix, HarmonyPatch(typeof(GameSave), nameof(GameSave.SaveCurrentGame))]
    private static bool Save(ref bool __result)
    {
        if (instance != null && instance.fixtureSave)
            return true;
        __result = false;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(VFPreload), "IsSplashSolid")]
    private static bool Splash(ref bool __result)
    {
        __result = true;
        return false;
    }

    [
        HarmonyPrefix,
        HarmonyPatch(typeof(PlayerAction_Inspect), nameof(PlayerAction_Inspect.GameTick))
    ]
    private static bool CaptureHover(PlayerAction_Inspect __instance)
    {
        if (instance == null || !(instance.parallelLineOnly || instance.machinePortsOnly))
            return true;
        __instance.hoveringEntityId = __instance.hoveringPrebuildId = 0;
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VFPreload), "Update")]
    private static void Preload(VFPreload __instance)
    {
        if (
            instance != null
            && (
                instance.machinePortsOnly
                || instance.copperInteractionsOnly
                || instance.windowPipePreviewOnly
                || instance.powerBuildingReferenceOnly
            )
            && __instance.languageSelectGroup.gameObject.activeSelf
        )
        {
            __instance.SelectLanguageZHCN();
            __instance.languageSelectGroup.gameObject.SetActive(false);
        }
        __instance.time = __instance.splashTime + 1;
        __instance.musicReached = true;
        __instance.clicked = true;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(GameCamera), nameof(GameCamera.FrameLogic))]
    private static void CameraPose()
    {
        if (instance == null || !instance.cameraLocked || GameCamera.main == null)
            return;
        var camera = GameCamera.main;
        camera.transform.SetPositionAndRotation(
            instance.cameraPosition,
            Quaternion.LookRotation(
                instance.cameraTarget - instance.cameraPosition,
                instance.cameraUp ?? instance.up
            )
        );
        camera.orthographic = instance.orthographicCapture;
        if (instance.orthographicCapture)
        {
            camera.orthographicSize = 1.5f;
        }
        camera.fieldOfView = 42;
        camera.aspect = 16f / 9;
        camera.targetTexture = instance.target;
    }

    private void Update()
    {
        AudioListener.volume = 0;
        AudioListener.pause = true;
        if (!started && VFPreload.done && Prototypes.Ready && UIRoot.instance != null)
        {
            started = true;
            if (!string.IsNullOrEmpty(inspectSave))
            {
                DSPGame.StartGame("pipe-inspect");
                return;
            }
            var desc = new GameDesc();
            desc.SetForNewGame(UniverseGen.algoVersion, 20261021, 32, 1, 1);
            desc.isSandboxMode = true;
            desc.isPeaceMode = true;
            desc.achievementEnable = false;
            DSPGame.StartGameSkipPrologue(desc);
            Logger.LogInfo("Scene planet loading");
        }
        if (
            !placed
            && started
            && GameMain.isRunning
            && GameMain.localPlanet?.factoryLoaded == true
            && GameCamera.main != null
        )
        {
            placed = true;
            if (!string.IsNullOrEmpty(inspectSave))
            {
                try
                {
                    if (machinePortsOnly)
                    {
                        RestoreMachinePortScene();
                        StartCoroutine(Guard(Capture()));
                    }
                    else if (parallelLineOnly)
                    {
                        RestoreParallelFluidLine();
                        StartCoroutine(Guard(Capture()));
                    }
                    else if (minimalLineOnly)
                    {
                        RestoreMinimalFluidLine();
                        StartCoroutine(Guard(Capture()));
                    }
                    else
                    {
                        InspectConnections();
                        Finish(null);
                    }
                }
                catch (Exception error)
                {
                    Finish(error);
                }
                return;
            }
            try
            {
                CreateScene();
                StartCoroutine(Guard(Capture()));
            }
            catch (Exception error)
            {
                Finish(error);
            }
        }
    }

    private Vector3 Point(float x, float z, float height = 0) =>
        (center + east * x + north * z).normalized * (factory.planet.realRadius + 0.2f + height);

    private void InspectConnections()
    {
        factory = GameMain.localPlanet.factory;
        for (int entityId = 1; entityId < factory.entityCursor; entityId++)
        {
            if (!PipeRuntime.IsPipeObject(factory, entityId))
                continue;
            var pos = factory.entityPool[entityId].pos;
            report.Add(
                "PIPE "
                    + entityId
                    + " pos="
                    + pos.ToString("F4")
                    + " altitude="
                    + pos.magnitude.ToString("F4")
            );
            foreach (int other in PipeTopology.Neighbors(factory, entityId))
                report.Add(
                    "EDGE "
                        + entityId
                        + " "
                        + other
                        + " distance="
                        + (
                            other > 0
                                ? (factory.entityPool[other].pos - pos).magnitude.ToString("F4")
                                : "pending"
                        )
                );
            for (int slot = 0; slot < 4; slot++)
            {
                factory.ReadObjectConn(entityId, slot, out _, out int other, out int peerSlot);
                if (other == 0)
                    continue;
                factory.ReadObjectConn(other, peerSlot, out _, out int back, out int backSlot);
                report.Add(
                    "SLOT "
                        + entityId
                        + ":"
                        + slot
                        + " -> "
                        + other
                        + ":"
                        + peerSlot
                        + " reciprocal="
                        + (back == entityId && backSlot == slot)
                );
            }
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        PipeVisuals.Draw(factory, GameCamera.main);
        watch.Stop();
        report.Add("BENCH first_draw_ms=" + watch.Elapsed.TotalMilliseconds.ToString("F3"));
        var samples = new List<double>();
        int selected = 0;
        for (int entityId2 = 1; entityId2 < factory.entityCursor; entityId2++)
            if (PipeRuntime.IsPipeObject(factory, entityId2))
            {
                selected = entityId2;
                break;
            }
        for (int sample = 0; sample < 8; sample++)
        {
            PipeVisuals.Dirty(factory, selected);
            watch.Restart();
            PipeVisuals.Draw(factory, GameCamera.main);
            watch.Stop();
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        report.Add(
            "BENCH local_edit_draw_ms median="
                + samples[4].ToString("F3")
                + " max="
                + samples[7].ToString("F3")
        );
        CheckHover();
        if (AccessTools.Method(typeof(PipeConnections), "RepairImported") != null)
        {
            CheckCopiedDismantle();
        }
    }

    private void CheckCopiedDismantle()
    {
        var ids = new List<int>();
        for (int entityId = 1; entityId < factory.entityCursor; entityId++)
            if (PipeRuntime.IsPipeObject(factory, entityId))
                ids.Add(entityId);
        var samples = new List<double>();
        for (int index = 0; index < ids.Count; index++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int proto = 0;
            factory.DismantleFinally(GameMain.mainPlayer, ids[index], ref proto);
            PipeVisuals.Draw(factory, GameCamera.main);
            watch.Stop();
            samples.Add(watch.Elapsed.TotalMilliseconds);
            for (int remaining = index + 1; remaining < ids.Count; remaining++)
            {
                int belt = factory.entityPool[ids[remaining]].beltId;
                if (
                    factory.cargoTraffic.GetCargoPath(factory.cargoTraffic.beltPool[belt].segPathId)
                    == null
                )
                    throw new InvalidOperationException(
                        "Dismantling removed a surviving pipe cargo path."
                    );
            }
        }
        foreach (int entityId2 in ids)
            if (
                PipeRuntime.Bind(factory.cargoTraffic).View(entityId2).Nodes != 0
                || PipeTopology.Neighbors(factory, entityId2).Length != 0
            )
                throw new InvalidOperationException("Dismantled pipe retains logical membership.");
        samples.Sort();
        report.Add(
            "BENCH copied_save_dismantle_with_draw nodes="
                + samples.Count
                + " median_ms="
                + samples[samples.Count / 2].ToString("F3")
                + " max_ms="
                + samples[samples.Count - 1].ToString("F3")
        );
        report.Add(
            "Every surviving pipe retains a native cargo path during dismantling; all removed entities leave both pipe graphs."
        );
    }

    private void CheckHover()
    {
        var player = GameMain.mainPlayer;
        var model = player.controller.actionBuild.model;
        bool modelWasOpen = model.connRenderer != null;
        model.Open();
        var dismantle = new BuildTool_Dismantle();
        dismantle._Init(GameMain.data);
        dismantle.factory = factory;
        dismantle.planet = factory.planet;
        var path = new BuildTool_Path();
        path._Init(GameMain.data);
        path.factory = factory;
        path.planet = factory.planet;
        path.handItem = LDB.items.Select(Prototypes.ItemId);
        path.handPrefabDesc = path.handItem.prefabDesc;
        var pipePreview = AccessTools.Method(typeof(PipeVisuals), "Preview");
        AccessTools
            .Field(typeof(BuildTool), "tmpPackage")
            .SetValue(path, new StorageComponent(player.package.size));
        AccessTools.Field(typeof(BuildTool), "tmpInhandId").SetValue(path, Prototypes.ItemId);
        AccessTools.Field(typeof(BuildTool), "tmpInhandCount").SetValue(path, 200);
        int oldStage = player.controller.cmd.stage;
        float oldReach = player.mecha.buildArea;
        var oldPosition = player.position;
        try
        {
            player.controller.cmd.stage = 0;
            player.mecha.buildArea = 10000;
            var ids = new List<int>();
            for (int entityId = 1; entityId < factory.entityCursor; entityId++)
                if (PipeRuntime.IsPipeObject(factory, entityId))
                    ids.Add(entityId);
            byte[] inventory;
            using (var memory = new MemoryStream())
            {
                PipeRuntime.Export(new BinaryWriter(memory));
                inventory = memory.ToArray();
            }
            var native = (int[])factory.entityConnPool.Clone();
            var pending = (int[])factory.prebuildConnPool.Clone();
            foreach (int mode in new[] { 0, 1, 2 })
            {
                Array.Copy(native, factory.entityConnPool, native.Length);
                Array.Copy(pending, factory.prebuildConnPool, pending.Length);
                using (var memory = new MemoryStream(inventory))
                    PipeRuntime.Import(new BinaryReader(memory));
                PipeVisuals.Clear();
                PipeVisuals.Draw(factory, GameCamera.main);
                bool build = mode != 0;
                player.controller.cmd.stage = mode == 2 ? 1 : 0;
                var samples = new List<double>();
                for (int sample = 0; sample < Math.Min(20, ids.Count); sample++)
                {
                    int entityId2 = ids[build ? ids.Count - 1 - sample : sample];
                    var pos = factory.entityPool[entityId2].pos;
                    player.position = pos;
                    model.EarlyGameTick();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    if (build)
                    {
                        path.castObjectId = entityId2;
                        path.castObjectPos = pos;
                        path.castObject = true;
                        path.castGround = true;
                        path.castGroundPos = path.castGroundPosSnapped = path.cursorTarget = pos;
                        path.startObjectId = mode == 2 ? entityId2 : 0;
                        path.startTarget = pos;
                        path.cursorValid = true;
                        path.maxSlope = 45;
                        if (mode == 2)
                        {
                            path.castObjectId = 0;
                            path.castObject = false;
                            path.cursorTarget =
                                (
                                    pos
                                    + Vector3.Cross(pos.normalized, Vector3.up).normalized
                                        * (3 + sample % 3)
                                ).normalized * pos.magnitude;
                            path.castGroundPos = path.castGroundPosSnapped = path.cursorTarget;
                        }
                        path.DeterminePreviews();
                        path.UpdateCollidersForCursor();
                        path.UpdatePreviewModels(model);
                        path.CheckBuildConditions();
                        path.UpdatePreviewModelConditions(model);
                        path.UpdateGizmos(model);
                        if (pipePreview != null)
                            pipePreview.Invoke(null, new object[] { path, GameCamera.main });
                    }
                    else
                    {
                        dismantle.castObjectId = entityId2;
                        dismantle.castObjectPos = pos;
                        dismantle.DeterminePreviews();
                        dismantle.UpdateCollidersForCursor();
                        dismantle.UpdatePreviewModels(model);
                        dismantle.UpdateGizmos(model);
                    }
                    PipeVisuals.Draw(factory, GameCamera.main);
                    watch.Stop();
                    samples.Add(watch.Elapsed.TotalMilliseconds);
                }
                samples.Sort();
                report.Add(
                    "BENCH hover_"
                        + (
                            mode == 2 ? "build_drag"
                            : build ? "build"
                            : "dismantle"
                        )
                        + " samples="
                        + samples.Count
                        + " median_cpu_ms="
                        + samples[samples.Count / 2].ToString("F3")
                        + " max_cpu_ms="
                        + samples[samples.Count - 1].ToString("F3")
                );
            }
        }
        finally
        {
            player.controller.cmd.stage = oldStage;
            player.mecha.buildArea = oldReach;
            player.position = oldPosition;
            dismantle._Free();
            path._Free();
            if (!modelWasOpen)
                model.Close();
        }
    }

    private void CreateScene()
    {
        factory = GameMain.localPlanet.factory;
        InspectTank();
        var planet = factory.planet;
        var sunlight = (
            Quaternion.Inverse(planet.runtimeRotation)
            * (Vector3)(planet.star.uPosition - planet.uPosition)
        ).normalized;
        float best = float.MaxValue;
        up = GameMain.mainPlayer.position.normalized;
        for (int index = 0; index < 1000; index++)
        {
            float y = 1 - 2f * (index + 0.5f) / 1000;
            if (copperInteractionsOnly && Mathf.Abs(y) > 0.8f)
            {
                continue;
            }
            float angle = index * 2.399963f;
            var normal2 = new Vector3(
                Mathf.Sqrt(1 - y * y) * Mathf.Cos(angle),
                y,
                Mathf.Sqrt(1 - y * y) * Mathf.Sin(angle)
            );
            if (Vector3.Dot(normal2, sunlight) < 0.45f)
                continue;
            var endPosition = Vector3.Cross(normal2, Vector3.up).normalized;
            if (endPosition.sqrMagnitude < 0.5f)
                continue;
            var f = Vector3.Cross(endPosition, normal2).normalized;
            float min = float.MaxValue;
            float max = 0;
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                float height = planet.data.QueryHeight(
                    normal2 * planet.realRadius + endPosition * x * 15 + f * z * 15
                );
                min = Mathf.Min(min, height);
                max = Mathf.Max(max, height);
            }
            if (min < planet.realRadius + 0.1f)
                continue;
            float score =
                max
                - min
                + Mathf.Abs(max - planet.realRadius - 0.2f) * 0.5f
                + (1 - Vector3.Dot(normal2, sunlight)) * 0.3f;
            if (score < best)
            {
                best = score;
                up = normal2;
            }
        }
        east = Vector3.Cross(up, Vector3.up).normalized;
        north = Vector3.Cross(east, up).normalized;
        center = up * (planet.realRadius + 0.2f);
        factory.PlanetReformAll(1, 0, true);
        report.Add("PASS The scene planet has native foundation across its surface.");
        factory.FlattenTerrain(
            center,
            Quaternion.LookRotation(north, up),
            new Bounds(
                Vector3.zero,
                new Vector3(parallelLineOnly ? 62 : 38, 1, parallelLineOnly ? 40 : 34)
            ),
            6,
            1,
            false,
            false,
            false,
            true
        );
        for (int entityId = 1; entityId < planet.data.vegeCursor; entityId++)
            if (
                planet.data.vegePool[entityId].id == entityId
                && (planet.data.vegePool[entityId].pos - center).sqrMagnitude < 900
            )
                factory.RemoveVegeWithComponents(entityId);
        for (int entityId2 = 1; entityId2 < planet.data.veinCursor; entityId2++)
            if (
                planet.data.veinPool[entityId2].id == entityId2
                && (planet.data.veinPool[entityId2].pos - center).sqrMagnitude < 900
            )
                factory.RemoveVeinWithComponents(entityId2);
        if (powerBuildingReferenceOnly)
        {
            CreatePowerBuildingReference();
            return;
        }
        if (windowPipePreviewOnly)
        {
            CreateWindowPipePreview();
            return;
        }
        if (connectionsOnly)
        {
            CreateConnections();
            return;
        }
        if (copperInteractionsOnly)
        {
            PrepareCopperInteractions();
            return;
        }
        if (machinePortsOnly)
        {
            DumpNativeUi();
            CreateMachinePortScene();
            return;
        }
        if (cubeOnly)
        {
            CreateCube();
            return;
        }
        if (structuresOnly)
        {
            CreateStructures();
            return;
        }
        if (gameplayOnly)
        {
            CreateGameplay();
            return;
        }
        if (insertersOnly)
        {
            CreateInserterScene();
            return;
        }
        if (minimalLineOnly)
        {
            CreateMinimalFluidLine();
            return;
        }
        if (parallelLineOnly)
        {
            CreateParallelFluidLine();
            return;
        }
        Path(
            2001,
            -10,
            -9,
            new[]
            {
                V(-5, 0),
                V(-4, 0),
                V(-3, 0),
                V(-2, 0),
                V(-1, 0),
                V(0, 0),
                V(1, 0),
                V(2, 0),
                V(3, 0),
                V(4, 0),
                V(5, 0),
            },
            0
        );
        Path(
            Prototypes.ItemId,
            8,
            -9,
            new[]
            {
                V(-5, 0),
                V(-4, 0),
                V(-3, 0),
                V(-2, 0),
                V(-1, 0),
                V(0, 0),
                V(1, 0),
                V(2, 0),
                V(3, 0),
                V(4, 0),
                V(5, 0),
            },
            1000
        );
        var elbow = new[]
        {
            V(-4, 0),
            V(-3, 0),
            V(-2, 0),
            V(-1, 0),
            V(0, 0),
            V(0, 1),
            V(0, 2),
            V(0, 3),
            V(0, 4),
        };
        Path(2001, -9, -3, elbow, 0);
        Path(Prototypes.ItemId, 9, -3, elbow, 1000);
        var junction = new[]
        {
            V(-4, 0),
            V(-3, 0),
            V(-2, 0),
            V(-1, 0),
            V(0, 0),
            V(1, 0),
            V(2, 0),
            V(3, 0),
            V(4, 0),
        };
        var normal = Path(2001, -9, 6, junction, 0);
        var pipe = Path(Prototypes.ItemId, 9, 6, junction, 1000);
        materialEntity = pipe[4];
        var normalBranch = Path(2001, -9, 2, new[] { V(0, 0), V(0, 1), V(0, 2), V(0, 3) }, 0);
        Connect(normalBranch[3], normal[4], 2);
        var pipeBranch = Path(
            Prototypes.ItemId,
            9,
            2,
            new[] { V(0, 0), V(0, 1), V(0, 2), V(0, 3) },
            0
        );
        Connect(pipeBranch[3], pipe[4], 2);
        var riser = new[]
        {
            new Vector3(-4, 0, 0),
            new Vector3(-3, 0, 0),
            new Vector3(-2, 0, 0),
            new Vector3(-1, 0, 0),
            new Vector3(0, 0, 0),
            new Vector3(0, 1.333f, 1),
            new Vector3(0, 2.666f, 2),
            new Vector3(1, 2.666f, 2),
            new Vector3(2, 2.666f, 2),
            new Vector3(3, 2.666f, 2),
        };
        Path(2001, -9, 11, riser, 0);
        Path(Prototypes.ItemId, 9, 11, riser, 1000);
        Tank(0, 0, 0);
        Tank(0, 10, 1000);
        Path(Prototypes.ItemId, -1.5f, -10, new[] { Vector3.zero }, 0);
        Path(Prototypes.ItemId, 1.5f, -10, new[] { Vector3.zero }, 1000);
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        report.Add("Scene: new sandbox galaxy seed=20261021, planet=" + planet.id);
        report.Add("Native entities: belts=" + belts + ", pipes=" + pipes);
        report.Add(
            "Left: Conveyor Belt Mk.I. Right: one Fluid Pipe prototype, filled with water. Rows: straight, elbow, junction, elevated."
        );
        report.Add("Center: native storage tanks, empty and water-filled.");
        report.Add(
            "Native factory display and production PipeVisuals renderer; game camera RenderTexture."
        );
        SetView(0, 2, 39, 28);
        Logger.LogInfo("Comparison scene placed");
    }

    private static Vector3 V(float x, float z) => new Vector3(x, 0, z);

    private void CreateStructures()
    {
        var loop = new List<Vector3>();
        for (int x = -3; x <= 3; x++)
            loop.Add(V(x, -2));
        for (int z = -1; z <= 2; z++)
            loop.Add(V(3, z));
        for (int x = 2; x >= -3; x--)
            loop.Add(V(x, 2));
        for (int z = 1; z >= -1; z--)
            loop.Add(V(-3, z));
        foreach (int item in new[] { 2001, Prototypes.ItemId })
        {
            float x = item == 2001 ? -9 : 9;
            var ring = Path(item, x, -10, loop.ToArray(), 0);
            Connect(ring[ring.Length - 1], ring[0], 1);
            int left = ring[loop.IndexOf(V(-3, 0))];
            int right = ring[loop.IndexOf(V(3, 0))];
            if (item == 2001)
            {
                var a = Path(item, x, -10, new[] { V(-1, 0), V(-2, 0) }, 0);
                Connect(a[1], left, 2);
                var b = Path(item, x, -10, new[] { V(1, 0), V(2, 0) }, 0);
                Connect(b[1], right, 2);
            }
            else
            {
                var bridge = Path(
                    item,
                    x,
                    -10,
                    new[] { V(-2, 0), V(-1, 0), V(0, 0), V(1, 0), V(2, 0) },
                    0
                );
                Connect(left, bridge[0], 2);
                Connect(bridge[4], right, 2);
                materialEntity = ring[0];
                var graph = PipeRuntime.Bind(factory.cargoTraffic);
                graph.Insert(ring[0], 1000, graph.View(ring[0]).Nodes * 4, 0);
                if (graph.View(ring[0]).Nodes != 25)
                    throw new InvalidOperationException("Theta network is disconnected.");
            }
            Path(
                item,
                x,
                0,
                new[]
                {
                    V(-4, 0),
                    V(-3, 0),
                    V(-2, 0),
                    V(-1, 0),
                    V(0, 0),
                    V(1, 0),
                    V(2, 0),
                    V(3, 0),
                    V(4, 0),
                },
                item == 2001 ? 0 : 1000
            );
            var raised = new List<Vector3>();
            for (int z = -4; z <= 4; z++)
                raised.Add(new Vector3(0, Math.Max(0, 3 - Math.Abs(z)), z));
            Path(item, x, 0, raised.ToArray(), item == 2001 ? 0 : 1000);
            var snake = new List<Vector3>();
            for (int col = -3; col <= 3; col++)
                snake.Add(V(col, -2));
            snake.Add(V(3, -1));
            snake.Add(V(3, 0));
            for (int col = 2; col >= -3; col--)
                snake.Add(V(col, 0));
            snake.Add(V(-3, 1));
            snake.Add(V(-3, 2));
            for (int col = -2; col <= 3; col++)
                snake.Add(V(col, 2));
            Path(item, x, 10, snake.ToArray(), item == 2001 ? 0 : 1000);
        }
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(0, 0, 38, 27);
        report.Add("Paired structures: left=native Mk.I belts, right=water-filled pipes.");
        report.Add(
            "Theta layout: belt ring with two incoming trees and a center gap; 24 nodes, 24 edges, cycle rank 1."
        );
        report.Add(
            "Pipe theta: complete shared bar; 25 nodes, 26 edges, cycle rank 2; one immediately shared inventory."
        );
        report.Add(
            "Other pairs: elevated crossing with disconnected levels, serpentine with two return bends."
        );
    }

    private void CreateCube()
    {
        var nodes = new Dictionary<Vector3Int, int>();
        for (int x = 0; x <= 3; x++)
        for (int y = 0; y <= 3; y++)
        for (int z = 0; z <= 3; z++)
        {
            int boundary =
                (x == 0 || x == 3 ? 1 : 0)
                + (y == 0 || y == 3 ? 1 : 0)
                + (z == 0 || z == 3 ? 1 : 0);
            if (boundary < 2)
                continue;
            int node = Path(
                Prototypes.ItemId,
                0,
                0,
                new[] { new Vector3(x - 1.5f, y, z - 1.5f) },
                0
            )[0];
            nodes.Add(new Vector3Int(x, y, z), node);
            galleryNodes.Add(node);
        }
        int edges = 0;
        foreach (var node in nodes)
        foreach (var step in new[] { Vector3Int.right, Vector3Int.up, new Vector3Int(0, 0, 1) })
            if (nodes.TryGetValue(node.Key + step, out int other))
            {
                Connect(node.Value, other, 1);
                edges++;
            }
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        int count = nodes.Count * 4;
        graph.Insert(galleryNodes[0], 1000, count, 0);
        foreach (int node in galleryNodes)
            if (graph.View(node).Nodes != 32 || graph.View(node).Count != count)
                throw new InvalidOperationException("Cube inventory is not shared immediately.");
        cutA = nodes[new Vector3Int(3, 3, 0)];
        cutB = nodes[new Vector3Int(2, 3, 0)];
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(0, 0, 7, 8, 6, 1.8f);
        report.Add("Cube frame: side=3 grid units, nodes=" + nodes.Count + ", unit edges=" + edges);
        report.Add(
            "All 32 nodes immediately expose one inventory of "
                + count
                + " water units, capacity="
                + graph.View(galleryNodes[0]).Capacity
        );
    }

    private void CreateConnections()
    {
        galleryNodes.AddRange(
            Path(
                Prototypes.ItemId,
                -12,
                7,
                new[] { V(-3, 0), V(-2, 0), V(-1, 0), V(0, 0), V(1, 0), V(2, 0), V(3, 0) },
                1000
            )
        );
        galleryNodes.AddRange(
            Path(
                Prototypes.ItemId,
                0,
                7,
                new[] { V(-3, 0), V(-2, 0), V(-1, 0), V(0, 0), V(0, 1), V(0, 2), V(0, 3) },
                1000
            )
        );
        Star(
            12,
            7,
            new[]
            {
                new Vector3(0, 0, -1),
                new Vector3(-0.7071f, 0, 0.7071f),
                new Vector3(0.7071f, 0, 0.7071f),
            }
        );
        Star(-12, -7, new[] { Vector3.left, Vector3.right, new Vector3(0, 0, -1) });
        Star(0, -7, new[] { Vector3.left, Vector3.right, Vector3.up });
        var spokes = new List<Vector3>();
        for (int index = 0; index < 7; index++)
        {
            float angle = index * Mathf.PI * 2 / 7;
            spokes.Add(new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)));
        }
        spokes.Add(Vector3.up);
        Star(12, -7, spokes.ToArray());
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(0, 0, 30, 20);
        report.Add("Scene: sandbox connection shapes; pipe nodes=" + pipes);
        report.Add(
            "Far row left to right: straight, elbow, oblique Y. Near row: T, vertical inlet, eight-interface junction."
        );
        report.Add(
            "One transparent pipe prototype; matched full-water and empty network captures."
        );
    }

    private void Star(float x, float z, Vector3[] directions)
    {
        int node = Path(Prototypes.ItemId, x, z, new[] { Vector3.zero }, 0)[0];
        galleryNodes.Add(node);
        foreach (var direction in directions)
        {
            var step = new Vector3(direction.x, direction.y * 1.333f, direction.z);
            var arm = Path(Prototypes.ItemId, x, z, new[] { step, step * 2, step * 3 }, 0);
            galleryNodes.AddRange(arm);
            Connect(node, arm[0], 1);
        }
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        graph.Insert(node, 1000, graph.View(node).Nodes * 4, 0);
    }

    private void Tank(float x, float z, int fluid)
    {
        var pos = Point(x, z);
        int entityId = factory.AddEntityDataWithComponents(
            new EntityData
            {
                protoId = 2106,
                modelIndex = (short)LDB.items.Select(2106).ModelIndex,
                pos = pos,
                rot = Quaternion.LookRotation(north, pos.normalized),
                alt = pos.magnitude,
                localized = true,
            },
            0
        );
        ref var tank = ref factory.factoryStorage.tankPool[factory.entityPool[entityId].tankId];
        tank.fluidId = fluid;
        tank.fluidCount = fluid == 0 ? 0 : tank.fluidCapacity * 3 / 4;
    }

    private void InspectTank()
    {
        var lines = new List<string>();
        var desc = LDB.items.Select(2106).prefabDesc;
        foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
            if (
                shader.name.ToLowerInvariant().Contains("glass")
                || shader.name.ToLowerInvariant().Contains("tank")
                || shader.name.ToLowerInvariant().Contains("liquid")
            )
                lines.Add("Shader " + shader.name);
        var materials = new HashSet<Material>();
        foreach (var renderer in desc.prefab.GetComponentsInChildren<Renderer>(true))
        foreach (var material in renderer.sharedMaterials)
            if (material != null)
                materials.Add(material);
        if (desc.materials != null)
            foreach (var material in desc.materials)
                if (material != null)
                    materials.Add(material);
        if (desc.lodMaterials != null)
            foreach (var lod in desc.lodMaterials)
                if (lod != null)
                    foreach (var material in lod)
                        if (material != null)
                            materials.Add(material);
        materials.Add(PipeVisuals.BodyMaterial);
        materials.Add(PipeVisuals.JointMaterial);
        materials.Add(PipeVisuals.LiquidMaterial(1000));
        foreach (var material in materials)
        {
            lines.Add(
                "Material "
                    + material.name
                    + " shader="
                    + material.shader.name
                    + " queue="
                    + material.renderQueue
            );
            for (int index = 0; index < material.shader.GetPropertyCount(); index++)
            {
                string property = material.shader.GetPropertyName(index);
                var type = material.shader.GetPropertyType(index);
                string value =
                    type == UnityEngine.Rendering.ShaderPropertyType.Color
                        ? material.GetColor(property).ToString()
                    : type == UnityEngine.Rendering.ShaderPropertyType.Vector
                        ? material.GetVector(property).ToString()
                    : type == UnityEngine.Rendering.ShaderPropertyType.Texture
                        ? material.GetTexture(property)?.name
                    : material.GetFloat(property).ToString();
                lines.Add(property + " " + type + " " + value);
            }
        }
        File.WriteAllLines(System.IO.Path.Combine(output, "tank-materials.txt"), lines);
    }

    private int[] Path(int item, float x, float z, Vector3[] points, int fluid)
    {
        var ids = new int[points.Length];
        for (int index = 0; index < points.Length; index++)
        {
            var position = points[index];
            var pos = Point(x + position.x, z + position.z, position.y);
            var direction =
                points[Math.Min(index + 1, points.Length - 1)] - points[Math.Max(0, index - 1)];
            var forward =
                direction.sqrMagnitude < 0.001f
                    ? north
                    : (east * direction.x + north * direction.z + up * direction.y).normalized;
            if (Mathf.Abs(Vector3.Dot(forward, pos.normalized)) > 0.99f)
                forward = north;
            ids[index] = factory.AddEntityDataWithComponents(
                new EntityData
                {
                    protoId = (short)item,
                    modelIndex = (short)LDB.items.Select(item).ModelIndex,
                    pos = pos,
                    rot = Quaternion.LookRotation(forward, pos.normalized),
                    alt = pos.magnitude,
                    localized = true,
                },
                0
            );
            if (item == 2001)
                belts++;
            else
                pipes++;
            if (index > 0)
                Connect(ids[index - 1], ids[index], 1);
        }
        if (fluid > 0)
            PipeRuntime.Bind(factory.cargoTraffic).Insert(ids[0], fluid, points.Length * 4, 0);
        return ids;
    }

    private void Connect(int firstEntityId, int secondEntityId, int slot)
    {
        factory.WriteObjectConn(firstEntityId, 0, true, secondEntityId, slot);
        if (PipeRuntime.IsPipeObject(factory, firstEntityId))
            return;
        foreach (int entityId in new[] { firstEntityId, secondEntityId })
        {
            factory.ReadObjectConn(entityId, 0, out _, out int o, out _);
            factory.ReadObjectConn(entityId, 1, out _, out int i0, out _);
            factory.ReadObjectConn(entityId, 2, out _, out int i1, out _);
            factory.ReadObjectConn(entityId, 3, out _, out int i2, out _);
            factory.cargoTraffic.AlterBeltConnections(
                factory.entityPool[entityId].beltId,
                Belt(o),
                Belt(i0),
                Belt(i1),
                Belt(i2)
            );
        }
    }

    private int Belt(int entityId) => entityId > 0 ? factory.entityPool[entityId].beltId : 0;

    private void SetView(
        float x,
        float z,
        float height,
        float back,
        float side = 0,
        float targetHeight = 0.5f
    )
    {
        orthographicCapture = false;
        cameraUp = null;
        cameraTarget = Point(x, z, targetHeight);
        cameraPosition = cameraTarget + up * height - north * back + east * side;
    }

    private void CheckPlacement()
    {
        var player = GameMain.mainPlayer;
        var tool = new BuildTool_Path();
        tool._Init(GameMain.data);
        tool.factory = factory;
        tool.planet = factory.planet;
        var oldPosition = player.position;
        int oldStage = player.controller.cmd.stage;
        float oldReach = player.mecha.buildArea;
        try
        {
            player.position = Point(10, 10);
            player.controller.cmd.stage = 0;
            player.mecha.buildArea = 100;
            AccessTools
                .Field(typeof(BuildTool), "tmpPackage")
                .SetValue(tool, new StorageComponent(player.package.size));
            var blocked = Point(12, 12);
            var box = LDB.items.Select(2101);
            factory.AddEntityDataWithComponents(
                new EntityData
                {
                    protoId = 2101,
                    modelIndex = (short)box.ModelIndex,
                    pos = blocked,
                    rot = Quaternion.LookRotation(north, blocked.normalized),
                    alt = blocked.magnitude,
                    localized = true,
                },
                0
            );
            foreach (int item in new[] { 2001, Prototypes.ItemId })
            {
                tool.handItem = LDB.items.Select(item);
                tool.handPrefabDesc = tool.handItem.prefabDesc;
                foreach (bool obstructed in new[] { false, true })
                {
                    AccessTools.Field(typeof(BuildTool), "tmpInhandId").SetValue(tool, item);
                    AccessTools.Field(typeof(BuildTool), "tmpInhandCount").SetValue(tool, 10);
                    tool.buildPreviews.Clear();
                    var pos = obstructed ? blocked + blocked.normalized : Point(12, -12);
                    var preview = new BuildPreview
                    {
                        item = tool.handItem,
                        desc = tool.handPrefabDesc,
                        lpos = pos,
                        lrot = Quaternion.LookRotation(north, pos.normalized),
                    };
                    tool.buildPreviews.Add(preview);
                    factory.planet.physics.nearColliderLogic.ActiveEntityBuildCollidersInArea(
                        pos,
                        4
                    );
                    Physics.SyncTransforms();
                    bool valid = tool.CheckBuildConditions();
                    if (
                        obstructed
                            ? (valid || preview.condition != EBuildCondition.Collide)
                            : (!valid || preview.condition != EBuildCondition.Ok)
                    )
                        throw new InvalidOperationException(
                            "Native placement mismatch item="
                                + item
                                + " blocked="
                                + obstructed
                                + " condition="
                                + preview.condition
                        );
                }
            }
            report.Add(
                "Original BuildTool_Path.CheckBuildConditions accepts clear terrain and rejects a solid storage-box collision for both belts and pipes."
            );
            CheckAcuteJunction(tool);
            tool.handItem = LDB.items.Select(Prototypes.ItemId);
            tool.handPrefabDesc = tool.handItem.prefabDesc;
            tool.buildPreviews.Clear();
            tool.startObjectId = galleryNodes[0];
            tool.castObjectId = galleryNodes[1];
            tool.buildPreviews.Add(
                new BuildPreview
                {
                    item = tool.handItem,
                    desc = tool.handPrefabDesc,
                    objId = tool.startObjectId,
                    coverObjId = tool.castObjectId,
                }
            );
            for (int tick = 0; tick < 10; tick++)
                tool.UpdateGizmos(player.controller.actionBuild.model);
            report.Add(
                "Original BuildTool_Path.UpdateGizmos selects built pipes, covered nodes and both drag endpoints for ten successive updates without a native belt render batch."
            );
            tool.buildPreviews.Clear();
            tool.startObjectId = tool.castObjectId = 0;
            player.TryAddItemToPackage(Prototypes.ItemId, 3, 0, false, 0);
            for (int node = 0; node < 3; node++)
            {
                var pos = Point(8 + node, -12);
                tool.buildPreviews.Add(
                    new BuildPreview
                    {
                        item = tool.handItem,
                        desc = tool.handPrefabDesc,
                        lpos = pos,
                        lrot = Quaternion.LookRotation(north, pos.normalized),
                        inputToSlot = 1,
                        outputToSlot = 1,
                    }
                );
            }
            for (int node = 0; node < 3; node++)
            {
                if (node > 0)
                    tool.buildPreviews[node].input = tool.buildPreviews[node - 1];
                if (node < 2)
                    tool.buildPreviews[node].output = tool.buildPreviews[node + 1];
            }
            tool.CreatePrebuilds();
            for (int node = 0; node < 3; node++)
            {
                var preview = tool.buildPreviews[node];
                int pending = preview.objId;
                if (pending >= 0)
                    throw new InvalidOperationException(
                        "Path placement did not create a pipe prebuild."
                    );
                factory.BuildFinally(player, -pending, false, false);
                int built = 0;
                for (int entityId = 1; entityId < factory.entityCursor; entityId++)
                    if (
                        factory.entityPool[entityId].id == entityId
                        && factory.entityPool[entityId].protoId == Prototypes.ItemId
                        && (factory.entityPool[entityId].pos - preview.lpos).sqrMagnitude < 0.0001f
                    )
                    {
                        built = entityId;
                        break;
                    }
                if (built == 0)
                    throw new InvalidOperationException("Pipe prebuild did not complete.");
                tool.NotifyBuilt(pending, built);
                tool.castObjectId = built;
                tool.UpdateGizmos(player.controller.actionBuild.model);
            }
            if (PipeRuntime.Bind(factory.cargoTraffic).View(tool.buildPreviews[0].objId).Nodes != 3)
                throw new InvalidOperationException("Completed pipe path is disconnected.");
            report.Add(
                "Original path placement creates a three-node pipe prebuild chain; BuildFinally completes each node and UpdateGizmos runs after every completion, preserving one connected network."
            );
            CheckElevatedRing(tool, player);
        }
        finally
        {
            player.position = oldPosition;
            player.controller.cmd.stage = oldStage;
            player.mecha.buildArea = oldReach;
            tool._Free();
        }
    }

    private void CheckAcuteJunction(BuildTool_Path tool)
    {
        var existing = Path(Prototypes.ItemId, -10, 10, new[] { V(0, 0), V(1.2f, 0) }, 0);
        tool.handItem = LDB.items.Select(Prototypes.ItemId);
        tool.handPrefabDesc = tool.handItem.prefabDesc;
        tool.startObjectId = existing[0];
        tool.castObjectId = 0;
        AccessTools.Field(typeof(BuildTool), "tmpInhandId").SetValue(tool, Prototypes.ItemId);
        AccessTools.Field(typeof(BuildTool), "tmpInhandCount").SetValue(tool, 10);
        tool.buildPreviews.Clear();
        for (int index = 0; index < 3; index++)
        {
            float distance = index * 1.2f;
            var pos = Point(
                -10 + distance * Mathf.Cos(8 * Mathf.Deg2Rad),
                10 + distance * Mathf.Sin(8 * Mathf.Deg2Rad)
            );
            tool.buildPreviews.Add(
                new BuildPreview
                {
                    item = tool.handItem,
                    desc = tool.handPrefabDesc,
                    lpos = pos,
                    lrot = Quaternion.LookRotation(east, pos.normalized),
                    coverObjId = index == 0 ? existing[0] : 0,
                }
            );
        }
        for (int index2 = 0; index2 < 3; index2++)
        {
            if (index2 > 0)
                tool.buildPreviews[index2].input = tool.buildPreviews[index2 - 1];
            if (index2 < 2)
                tool.buildPreviews[index2].output = tool.buildPreviews[index2 + 1];
        }
        UndirectedConnections.Previews(tool);
        factory.planet.physics.nearColliderLogic.ActiveEntityBuildCollidersInArea(Point(-9, 10), 5);
        Physics.SyncTransforms();
        if (!tool.CheckBuildConditions())
            throw new InvalidOperationException(
                "Eight-degree pipe junction is rejected: "
                    + string.Join(
                        ",",
                        tool.buildPreviews.ConvertAll(preview => preview.condition.ToString())
                    )
            );
        report.Add(
            "Original placement accepts an eight-degree branch beside the connected pipe's colliders; unrelated building collision checks remain active."
        );
    }

    private int[] BuildPipePath(
        BuildTool_Path tool,
        Player player,
        List<Vector3> positions,
        int start = 0,
        int end = 0
    )
    {
        tool.buildPreviews.Clear();
        tool.startObjectId = start;
        tool.castObjectId = end;
        player.TryAddItemToPackage(Prototypes.ItemId, positions.Count, 0, false, 0);
        foreach (var pos in positions)
            tool.buildPreviews.Add(
                new BuildPreview
                {
                    item = tool.handItem,
                    desc = tool.handPrefabDesc,
                    lpos = pos,
                    lrot = Quaternion.LookRotation(north, pos.normalized),
                    inputToSlot = 1,
                    outputToSlot = 1,
                }
            );
        if (start != 0)
            tool.buildPreviews[0].coverObjId = start;
        if (end != 0)
            tool.buildPreviews[positions.Count - 1].coverObjId = end;
        for (int node = 0; node < positions.Count; node++)
        {
            if (node > 0)
                tool.buildPreviews[node].input = tool.buildPreviews[node - 1];
            if (node + 1 < positions.Count)
                tool.buildPreviews[node].output = tool.buildPreviews[node + 1];
        }
        int collections = GC.CollectionCount(2);
        var placing = System.Diagnostics.Stopwatch.StartNew();
        tool.CreatePrebuilds();
        placing.Stop();
        report.Add(
            "BENCH native_pipe_prebuilds nodes="
                + positions.Count
                + " cpu_ms="
                + placing.Elapsed.TotalMilliseconds.ToString("F3")
                + " gen2_collections="
                + (GC.CollectionCount(2) - collections)
        );
        for (int node = positions.Count - 1; node >= 0; node--)
        {
            var preview = tool.buildPreviews[node];
            int pending = preview.objId;
            if (pending >= 0)
                continue;
            factory.BuildFinally(player, -pending, false, false);
            int built = 0;
            for (int entityId = 1; entityId < factory.entityCursor; entityId++)
                if (
                    factory.entityPool[entityId].id == entityId
                    && factory.entityPool[entityId].protoId == Prototypes.ItemId
                    && (factory.entityPool[entityId].pos - preview.lpos).sqrMagnitude < 0.0001f
                )
                {
                    built = entityId;
                    break;
                }
            if (built == 0)
                throw new InvalidOperationException("Path prebuild did not complete.");
            tool.NotifyBuilt(pending, built);
            tool.castObjectId = built;
            tool.UpdateGizmos(player.controller.actionBuild.model);
        }
        var result = new int[positions.Count];
        for (int index = 0; index < result.Length; index++)
            result[index] = tool.buildPreviews[index].objId;
        return result;
    }

    private void CheckElevatedRing(BuildTool_Path tool, Player player)
    {
        var first = new List<Vector3>();
        for (int x = 0; x <= 4; x++)
            first.Add(Point(8 + x, -8, 1.3333f));
        for (int z = 1; z <= 4; z++)
            first.Add(Point(12, -8 + z, 1.3333f));
        var a = BuildPipePath(tool, player, first);
        var second = new List<Vector3> { first[first.Count - 1] };
        for (int x = 3; x >= 0; x--)
            second.Add(Point(8 + x, -4, 1.3333f));
        for (int z = 3; z >= 0; z--)
            second.Add(Point(8, -8 + z, 1.3333f));
        var b = BuildPipePath(tool, player, second, a[a.Length - 1], a[0]);
        var ring = new List<int>(a);
        for (int index = 1; index + 1 < b.Length; index++)
            ring.Add(b[index]);
        for (int index2 = 0; index2 < ring.Count; index2++)
        {
            int entityId = ring[index2];
            var peers = PipeTopology.Neighbors(factory, entityId);
            if (
                peers.Length != 2
                || Array.IndexOf(peers, ring[(index2 + ring.Count - 1) % ring.Count]) < 0
                || Array.IndexOf(peers, ring[(index2 + 1) % ring.Count]) < 0
            )
                throw new InvalidOperationException(
                    "Elevated ring has a non-adjacent logical connection at " + entityId
                );
            for (int slot = 0; slot < 4; slot++)
            {
                factory.ReadObjectConn(entityId, slot, out _, out int peer, out int peerSlot);
                if (peer == 0)
                    continue;
                factory.ReadObjectConn(peer, peerSlot, out _, out int back, out int backSlot);
                if (peer < 0 || back != entityId || backSlot != slot)
                    throw new InvalidOperationException(
                        "Elevated ring retains a prebuild reference at " + entityId
                    );
            }
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        PipeVisuals.Draw(factory, GameCamera.main);
        watch.Stop();
        report.Add(
            "Elevated ring: two native placement batches, recycled pending IDs, reverse-order completion, sixteen nodes with exactly two adjacent neighbors and reciprocal native slots; draw_ms="
                + watch.Elapsed.TotalMilliseconds.ToString("F3")
        );
        var samples = new List<double>();
        foreach (int entityId2 in ring)
        {
            watch.Restart();
            int proto = 0;
            factory.DismantleFinally(player, entityId2, ref proto);
            PipeVisuals.Draw(factory, GameCamera.main);
            watch.Stop();
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        report.Add(
            "BENCH elevated_ring_dismantle_with_draw nodes=16 median_ms="
                + samples[8].ToString("F3")
                + " max_ms="
                + samples[15].ToString("F3")
        );
    }

    private IEnumerator Capture()
    {
        for (int frame = 0; frame < 150; frame++)
            yield return null;
        if (powerBuildingReferenceOnly)
        {
            var reference = CapturePowerBuildingReference();
            while (reference.MoveNext())
            {
                yield return reference.Current;
            }
            Finish(null);
            yield break;
        }
        if (windowPipePreviewOnly)
        {
            var preview = CaptureWindowPipePreview();
            while (preview.MoveNext())
            {
                yield return preview.Current;
            }
            Finish(null);
            yield break;
        }
        if (copperInteractionsOnly)
        {
            var capture = CaptureCopperInteractions();
            while (capture.MoveNext())
            {
                yield return capture.Current;
            }
            Finish(null);
            yield break;
        }
        if (machinePortsOnly)
        {
            var capture = CaptureMachinePorts();
            while (capture.MoveNext())
            {
                yield return capture.Current;
            }
            Finish(null);
            yield break;
        }
        if (parallelLineOnly)
        {
            var capture = CaptureParallelFluidLine();
            while (capture.MoveNext())
                yield return capture.Current;
            Finish(null);
            yield break;
        }
        if (minimalLineOnly)
        {
            var capture = CaptureMinimalFluidLine();
            while (capture.MoveNext())
                yield return capture.Current;
            Finish(null);
            yield break;
        }
        if (gameplayOnly)
        {
            var capture = CaptureGameplay();
            while (capture.MoveNext())
                yield return capture.Current;
            Finish(null);
            yield break;
        }
        if (insertersOnly)
        {
            var capture = CaptureInserters();
            while (capture.MoveNext())
                yield return capture.Current;
            Finish(null);
            yield break;
        }
        if (structuresOnly)
        {
            SaveImage("ingame-structures-overview.png");
            SetView(0, -10, 22, 15);
            for (int frame = 0; frame < 12; frame++)
                yield return null;
            SaveImage("ingame-structures-cycles.png");
            var structureGraph = PipeRuntime.Bind(factory.cargoTraffic);
            structureGraph.Take(materialEntity, int.MaxValue);
            for (int frame = 0; frame < 12; frame++)
                yield return null;
            SaveImage("ingame-structures-cycles-empty.png");
            SetView(0, 0, 20, 14);
            for (int frame = 0; frame < 12; frame++)
                yield return null;
            SaveImage("ingame-structures-overpass.png");
            SetView(0, 10, 22, 15);
            for (int frame = 0; frame < 12; frame++)
                yield return null;
            SaveImage("ingame-structures-serpentine.png");
            Finish(null);
            yield break;
        }
        if (cubeOnly)
        {
            CheckPlacement();
            SaveImage("ingame-cube-filled.png");
            SetView(1.5f, -1.5f, 1.8f, 2, 1.4f, 3.34f);
            for (int frame = 0; frame < 12; frame++)
                yield return null;
            SaveImage("ingame-cube-corner-filled.png");
            var cubeGraph = PipeRuntime.Bind(factory.cargoTraffic);
            cubeGraph.Take(galleryNodes[0], int.MaxValue);
            foreach (int node in galleryNodes)
                if (cubeGraph.View(node).Count != 0)
                    throw new InvalidOperationException("Cube extraction leaves remote stock.");
            SetView(0, 0, 7, 8, 6, 1.8f);
            for (int frame = 0; frame < 12; frame++)
                yield return null;
            SaveImage("ingame-cube-empty.png");
            SetView(1.5f, -1.5f, 1.8f, 2, 1.4f, 3.34f);
            for (int frame = 0; frame < 12; frame++)
                yield return null;
            SaveImage("ingame-cube-corner-empty.png");
            SetView(0, 0, 11, 2, 3, 1.5f);
            for (int frame = 0; frame < 12; frame++)
                yield return null;
            SaveImage("ingame-cube-empty-steep.png");
            SetView(0, 0, 2, 9, 6, 1.5f);
            for (int frame = 0; frame < 12; frame++)
                yield return null;
            SaveImage("ingame-cube-empty-low.png");
            if (
                !PipeConnections.Disconnect(factory, cutA, cutB)
                || cubeGraph.View(cutA).Nodes != 32
            )
                throw new InvalidOperationException("Cube edge cut loses alternate connectivity.");
            SetView(0, 0, 7, 8, 6, 1.8f);
            for (int frame = 0; frame < 12; frame++)
                yield return null;
            SaveImage("ingame-cube-cut.png");
            cubeGraph.Insert(cutA, 1000, 128, 0);
            var remaining = PipeTopology.Neighbors(factory, cutA);
            foreach (int peer in remaining)
                PipeConnections.Disconnect(factory, cutA, peer);
            if (
                cubeGraph.View(cutA).Nodes != 1
                || cubeGraph.View(cutA).Count != 4
                || cubeGraph.View(cutB).Nodes != 31
                || cubeGraph.View(cutB).Count != 124
            )
                throw new InvalidOperationException(
                    "Cube corner isolation does not conserve inventory."
                );
            PipeConnections.Connect(factory, cutA, cutB, 0, 1);
            foreach (int peer in remaining)
                PipeConnections.Connect(factory, cutA, peer, 0, 1);
            if (cubeGraph.View(cutA).Nodes != 32 || cubeGraph.View(cutA).Count != 128)
                throw new InvalidOperationException("Cube reconnection loses inventory.");
            report.Add(
                "Live edge cuts preserve alternate routes; corner isolation retains 32 entities and splits 128 water into 4+124, then reconnects without loss."
            );
            Finish(null);
            yield break;
        }
        if (connectionsOnly)
        {
            SaveImage("ingame-connections-filled.png");
            var galleryGraph = PipeRuntime.Bind(factory.cargoTraffic);
            foreach (int node in galleryNodes)
                galleryGraph.Take(node, int.MaxValue);
            for (int frame = 0; frame < 12; frame++)
                yield return null;
            SaveImage("ingame-connections-empty.png");
            Finish(null);
            yield break;
        }
        SaveImage("ingame-comparison-overview.png");
        SetView(0, -5, 25, 16);
        for (int frame = 0; frame < 12; frame++)
            yield return null;
        SaveImage("ingame-comparison-straight-elbow.png");
        SetView(0, 9, 26, 17);
        for (int frame = 0; frame < 12; frame++)
            yield return null;
        SaveImage("ingame-comparison-junction-riser.png");
        SetView(9, 4.4f, 9, 8);
        for (int frame = 0; frame < 12; frame++)
            yield return null;
        SaveImage("ingame-pipe-junction-closeup.png");
        SetView(-9, 4.4f, 9, 8);
        for (int frame = 0; frame < 12; frame++)
            yield return null;
        SaveImage("ingame-belt-junction-closeup.png");
        SetView(4, 6, 16, 13);
        for (int frame = 0; frame < 12; frame++)
            yield return null;
        SaveImage("ingame-tank-pipe-materials.png");
        SetView(9, 4.4f, 9, 8);
        for (int frame = 0; frame < 12; frame++)
            yield return null;
        SaveImage("ingame-filled-pipe.png");
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        var stock = graph.Take(materialEntity, int.MaxValue);
        for (int frame = 0; frame < 12; frame++)
            yield return null;
        SaveImage("ingame-empty-pipe.png");
        graph.Insert(
            materialEntity,
            stock.Item,
            checked((int)stock.Count),
            checked((int)stock.ProliferatorPoints)
        );
        SetView(0, -10, 3.2f, 2.8f);
        for (int frame = 0; frame < 12; frame++)
            yield return null;
        SaveImage("ingame-single-nodes.png");
        Finish(null);
    }

    private IEnumerator Guard(IEnumerator capture)
    {
        while (true)
        {
            bool more = false;
            object current = null;
            try
            {
                more = capture.MoveNext();
                if (more)
                    current = capture.Current;
            }
            catch (Exception error)
            {
                Finish(error);
            }
            if (!more)
                yield break;
            yield return current;
        }
    }

    private void SaveImage(string name, bool includeUI = false)
    {
        CameraPose();
        if (parallelLineOnly)
        {
            factory.planet.factoryModel.drawPowerArc = factory.planet.factoryModel.drawPowerConn =
                false;
            PowerSystemRenderer.powerGraphOn =
                PowerSystemRenderer.forceLinesOn =
                PowerSystemRenderer.forceDisksOn =
                PowerSystemRenderer.forceConsumersOn =
                    false;
        }
        factory.planet.factoryModel.DrawInstancedBatches(GameCamera.main, true);
        PipeVisuals.Draw(factory, GameCamera.main);
        FluidInserterVisuals.Draw(factory, GameCamera.main);
        GameCamera.main.Render();
        if (includeUI)
        {
            var canvas = UIRoot.instance.overlayCanvas;
            var camera = canvas.worldCamera;
            if (camera == null)
                throw new InvalidOperationException("UI canvas has no render camera.");
            var old = camera.targetTexture;
            var clear = camera.clearFlags;
            try
            {
                camera.targetTexture = target;
                camera.clearFlags = CameraClearFlags.Depth;
                camera.Render();
            }
            finally
            {
                camera.targetTexture = old;
                camera.clearFlags = clear;
            }
        }
        var previous = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            float min = 1;
            float max = 0;
            for (int y = 0; y < image.height; y += 64)
            for (int x = 0; x < image.width; x += 64)
            {
                float value = image.GetPixel(x, y).grayscale;
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }
            if (max - min < 0.05f)
                throw new InvalidOperationException(
                    "Scene capture has insufficient image variation: " + name
                );
            File.WriteAllBytes(System.IO.Path.Combine(output, name), image.EncodeToPNG());
            report.Add("Captured " + name);
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.Destroy(image);
        }
    }

    private void Finish(Exception error)
    {
        if (error != null)
        {
            report.Add(error.ToString());
            Logger.LogError(error);
        }
        File.WriteAllLines(
            System.IO.Path.Combine(
                output,
                !string.IsNullOrEmpty(inspectSave)
                    && !minimalLineOnly
                    && !parallelLineOnly
                    && !machinePortsOnly
                        ? "inspect-results.txt"
                    : machinePortsOnly ? "machine-ports-results.txt"
                    : copperInteractionsOnly ? "copper-interactions-results.txt"
                    : parallelLineOnly ? "parallel-line-results.txt"
                    : minimalLineOnly ? "minimal-line-results.txt"
                    : insertersOnly ? "inserters-results.txt"
                    : gameplayOnly ? "gameplay-results.txt"
                    : structuresOnly ? "structures-results.txt"
                    : cubeOnly ? "cube-results.txt"
                    : connectionsOnly ? "connections-results.txt"
                    : "scene-results.txt"
            ),
            report
        );
        Application.Quit(error == null ? 0 : 1);
    }
}
