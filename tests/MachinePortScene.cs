using System;
using System.Collections;
using System.IO;
using System.Linq;
using FluidPipes;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class SceneCapture
{
    private int directMachine;
    private int directWaterPipe;
    private int directOilPipe;
    private int directProductPipe;
    private int directWaterSlot;
    private int directOilSlot;
    private int directProductSlot;

    private void RequirePort(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
        report.Add("PASS " + description);
    }

    private void CreateMachinePortScene()
    {
        ReportNativePortGeometry();
        RequirePort(
            PipeApi.MachinePortsEnabled,
            "Machine-port configuration enables the optional integration."
        );
        directMachine = PlaceFacility(2309, Point(0, 0), Quaternion.LookRotation(north, up));
        var recipe = LDB.recipes.dataArray.Single(candidate =>
            candidate.Results.Length == 1 && candidate.Results[0] == Acid
        );
        GameMain.history.UnlockRecipe(recipe.ID);
        factory
            .factorySystem.assemblerPool[factory.entityPool[directMachine].assemblerId]
            .SetRecipe(recipe.ID, factory.entitySignPool);

        var choices = PipeApi.GetMachinePortChoices(factory, directMachine);
        RequirePort(
            choices.Length == 3
                && choices.Count(choice => choice.Direction == MachinePortDirection.IntoMachine)
                    == 2
                && choices
                    .Single(choice => choice.Direction == MachinePortDirection.OutOfMachine)
                    .ItemId == Acid,
            "A sulfuric-acid machine exposes two liquid inputs and one liquid product as distinct choices."
        );

        int slots = LDB.items.Select(2309).prefabDesc.slotPoses.Length;
        directWaterSlot = 0;
        directOilSlot = slots / 3;
        directProductSlot = slots * 2 / 3;
        directWaterPipe = DirectPipe(directMachine, directWaterSlot, new MachinePortOptions(Water));
        directOilPipe = DirectPipe(directMachine, directOilSlot, new MachinePortOptions(Oil));
        directProductPipe = DirectPipe(
            directMachine,
            directProductSlot,
            new MachinePortOptions(Acid, MachinePortDirection.OutOfMachine)
        );
        CheckMachinePortTransfers();
        CheckMachinePortLifecycle();
        CheckMachinePortPlacement();
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(0, 0, 17, 22, 10, 1);
    }

    private void ReportNativePortGeometry()
    {
        var description = LDB.items.Select(2011).prefabDesc;
        var source = description.lodMeshes[0];
        var tags = source.colors32;
        var points = source.vertices;
        var lines = new System.Collections.Generic.List<string>();
        foreach (var group in Enumerable.Range(0, points.Length).GroupBy(index => tags[index]))
        {
            var bounds = new Bounds(points[group.First()], Vector3.zero);
            foreach (int index in group)
            {
                bounds.Encapsulate(points[index]);
            }
            lines.Add(
                "TAG "
                    + group.Key
                    + " vertices="
                    + group.Count()
                    + " min="
                    + bounds.min.ToString("F4")
                    + " max="
                    + bounds.max.ToString("F4")
            );
        }
        var material = description.lodMaterials[0][0];
        lines.Add(
            "MATERIAL color="
                + material.GetColor("_Color")
                + " texture="
                + material.mainTexture.name
                + " size="
                + material.mainTexture.width
                + "x"
                + material.mainTexture.height
        );
        File.WriteAllLines(System.IO.Path.Combine(output, "native-port-geometry.txt"), lines);
    }

    private int DirectPipe(int machineEntityId, int slotIndex, MachinePortOptions options)
    {
        var machine = factory.entityPool[machineEntityId];
        var slot = LDB.items.Select(machine.protoId).prefabDesc.slotPoses[slotIndex];
        Vector3 socket = machine.pos + machine.rot * slot.position;
        Vector3 outward = machine.rot * slot.forward;
        int previous = 0;
        int first = 0;
        for (int index = 0; index < 4; index++)
        {
            Vector3 position = socket + outward * (1.1f + index * 1.2f);
            position = position.normalized * (factory.planet.realRadius + 0.2f);
            int pipe = PlaceFacility(
                Prototypes.ItemId,
                position,
                Quaternion.LookRotation(outward, position.normalized)
            );
            if (previous != 0)
            {
                Connect(previous, pipe, 1);
            }
            else
            {
                first = pipe;
            }
            previous = pipe;
        }
        RequirePort(
            PipeApi.TryConnectMachinePort(factory, first, machineEntityId, slotIndex, options),
            "A pipe connects to native machine slot "
                + slotIndex
                + " with filter "
                + options.ItemId
                + "."
        );
        return first;
    }

    private void CheckMachinePortTransfers()
    {
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        ref var assembler = ref factory.factorySystem.assemblerPool[
            factory.entityPool[directMachine].assemblerId
        ];
        RequirePort(
            PipeApi.TryInsert(factory, directWaterPipe, Water, 40, 160)
                && PipeApi.TryInsert(factory, directOilPipe, Oil, 40, 160),
            "Independent liquid networks accept their selected recipe inputs."
        );
        RequirePort(
            !PipeApi.TryInsert(factory, directProductPipe, Water, 1)
                && !PipePools.CanConnect(graph, directWaterPipe, directOilPipe)
                && !PipeApi.TryConnectMachinePort(
                    factory,
                    directWaterPipe,
                    directMachine,
                    directProductSlot,
                    new MachinePortOptions(Water)
                ),
            "Empty output networks reserve their item, conflicting networks reject merging, and occupied machine slots retain their owner."
        );

        PipeMachinePorts.Tick(factory);
        int waterIndex = Array.IndexOf(assembler.recipeExecuteData.requires, Water);
        int oilIndex = Array.IndexOf(assembler.recipeExecuteData.requires, Oil);
        RequirePort(
            assembler.served[waterIndex] > 0
                && assembler.served[oilIndex] > 0
                && PipePools.View(graph, directWaterPipe).Count + assembler.served[waterIndex] == 40
                && PipePools.View(graph, directOilPipe).Count + assembler.served[oilIndex] == 40
                && PipePools.View(graph, directWaterPipe).ProliferatorPoints
                    + assembler.incServed[waterIndex]
                    == 160,
            "Native machine input buffers receive liquids and conserve item counts and proliferator points."
        );
        int waterBefore = assembler.served[waterIndex];
        PipeMachinePorts.Tick(factory);
        RequirePort(
            assembler.served[waterIndex] == waterBefore,
            "Input transfer stops at the native demand threshold."
        );

        int productIndex = Array.IndexOf(assembler.recipeExecuteData.products, Acid);
        assembler.produced[productIndex] = 20;
        PipeMachinePorts.Tick(factory);
        RequirePort(
            assembler.produced[productIndex] == 0
                && PipePools.View(graph, directProductPipe).Count == 20,
            "Product-only extraction transfers native output into its selected network."
        );
        RequirePort(
            PipeApi.TryInsert(factory, directProductPipe, Acid, 20),
            "The product network fills to capacity."
        );
        assembler.produced[productIndex] = 3;
        PipeMachinePorts.Tick(factory);
        RequirePort(
            assembler.produced[productIndex] == 3,
            "A full product network applies backpressure without consuming machine products."
        );
        PipeApi.TryTake(factory, directProductPipe, 5, out _, Acid);
        PipeMachinePorts.Tick(factory);
        RequirePort(
            assembler.produced[productIndex] == 0
                && PipePools.View(graph, directProductPipe).Count == 38,
            "Product transfer resumes after network capacity becomes available."
        );

        RequirePort(
            PipeApi.TrySetMachinePortOptions(
                factory,
                directMachine,
                directWaterSlot,
                new MachinePortOptions(Water, enabled: false)
            ),
            "An individual machine connection can pause."
        );
        assembler.served[waterIndex] = 0;
        assembler.incServed[waterIndex] = 0;
        PipeMachinePorts.Tick(factory);
        RequirePort(
            assembler.served[waterIndex] == 0,
            "A paused input preserves machine and pipe ownership."
        );
        PipeApi.TrySetMachinePortOptions(
            factory,
            directMachine,
            directWaterSlot,
            new MachinePortOptions(Water)
        );

        int previousRecipe = assembler.recipeId;
        var plasticRecipe = LDB.recipes.dataArray.Single(recipe =>
            recipe.Results.Length == 1 && recipe.Results[0] == Plastic
        );
        assembler.SetRecipe(plasticRecipe.ID, factory.entitySignPool);
        long retainedWater = PipePools.View(graph, directWaterPipe).Count;
        PipeMachinePorts.Tick(factory);
        RequirePort(
            PipePools.View(graph, directWaterPipe).Count == retainedWater
                && PipeMachinePorts
                    .Status(factory, PipeApi.GetMachinePorts(factory, directWaterPipe)[0])
                    .Contains("配方"),
            "A recipe change suspends incompatible ports while retaining pipe inventory and filter settings."
        );
        assembler.SetRecipe(previousRecipe, factory.entitySignPool);

        int stoneIndex = Array.IndexOf(assembler.recipeExecuteData.requires, Stone);
        assembler.served[stoneIndex] = 100;
        var statistics = GameMain.statistics.production.factoryStatPool[factory.index];
        PipeApi.TryTake(factory, directProductPipe, 398, out _, Acid);
        for (int tick = 0; tick < 1200; tick++)
        {
            PipeMachinePorts.Tick(factory);
            assembler.InternalUpdate(1, statistics.productRegister, statistics.consumeRegister);
        }
        PipeMachinePorts.Tick(factory);
        RequirePort(
            PipePools.View(graph, directProductPipe).Count > 0,
            "A native chemical recipe produces sulfuric acid through direct liquid ports."
        );
    }

    private void CheckMachinePortLifecycle()
    {
        var enabledProperty = AccessTools.Property(typeof(PipeConfiguration), "EnableMachinePorts");
        enabledProperty.SetValue(null, false, null);
        try
        {
            RequirePort(
                !PipeApi.MachinePortsEnabled
                    && !PipeApi.TryConnectMachinePort(
                        factory,
                        directWaterPipe,
                        directMachine,
                        1,
                        new MachinePortOptions(Water)
                    )
                    && PipeApi.GetMachinePorts(factory, directWaterPipe).Single().Options.ItemId
                        == Water,
                "Disabling machine ports stops new connections and retains existing settings."
            );
        }
        finally
        {
            enabledProperty.SetValue(null, true, null);
        }

        byte[] saved;
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                PipeRuntime.Export(writer);
            }
            saved = stream.ToArray();
        }
        PipeApi.TrySetMachinePortOptions(
            factory,
            directMachine,
            directWaterSlot,
            new MachinePortOptions(Water, enabled: false)
        );
        using (var stream = new MemoryStream(saved))
        using (var reader = new BinaryReader(stream))
        {
            PipeRuntime.Import(reader);
        }
        RequirePort(
            PipeApi.GetMachinePorts(factory, directWaterPipe).Single().Options.Enabled
                && PipeApi.GetMachinePorts(factory, directOilPipe).Single().Options.ItemId == Oil
                && PipeApi.GetMachinePorts(factory, directProductPipe).Single().Options.Direction
                    == MachinePortDirection.OutOfMachine,
            "Current mod data restores direct-port filters, direction and enable state."
        );

        long retained = PipePools
            .View(PipeRuntime.Bind(factory.cargoTraffic), directWaterPipe)
            .Count;
        RequirePort(
            PipeApi.TryDisconnectMachinePort(factory, directMachine, directWaterSlot)
                && PipeApi.GetMachinePorts(factory, directWaterPipe).Length == 0
                && PipePools.View(PipeRuntime.Bind(factory.cargoTraffic), directWaterPipe).Count
                    == retained,
            "Disconnecting a direct port retains the pipe inventory and releases the native slot."
        );
        RequirePort(
            PipeApi.TryConnectMachinePort(
                factory,
                directWaterPipe,
                directMachine,
                directWaterSlot,
                new MachinePortOptions(Water)
            ),
            "A released native slot can reconnect."
        );
    }

    private void CheckMachinePortPlacement()
    {
        var player = GameMain.mainPlayer;
        var model = player.controller.actionBuild.model;
        model.Open();
        var tool = new BuildTool_Path();
        tool._Init(GameMain.data);
        tool.factory = factory;
        tool.planet = factory.planet;
        tool.handItem = LDB.items.Select(Prototypes.ItemId);
        tool.handPrefabDesc = tool.handItem.prefabDesc;
        var native = LDB.items.Select(2309).prefabDesc;
        RequirePort(
            tool.GetLocalPorts(directMachine) != native.slotPoses
                && tool.GetPrefabDesc(directMachine) != native
                && tool.GetPrefabDesc(directMachine).portPoses == tool.GetLocalPorts(directMachine)
                && tool.GetLocalPorts(directMachine).Length == native.slotPoses.Length
                && (native.portPoses == null || native.portPoses.Length == 0),
            "The pipe construction tool exposes external terminals with original machine slot identities."
        );
        tool.handItem = LDB.items.Select(2001);
        RequirePort(
            tool.GetPrefabDesc(directMachine) == native
                && tool.GetLocalPorts(directMachine) == native.portPoses,
            "Ordinary conveyor construction retains its original machine connection rules."
        );
        tool.handItem = LDB.items.Select(Prototypes.ItemId);
        CheckPlacedMachinePort(tool, true);
        CheckPlacedMachinePort(tool, false);
        player.controller.cmd.SetNoneCommand();
        model.Close();
        player.position = Point(0, -7);
    }

    private void CheckPlacedMachinePort(BuildTool_Path tool, bool startAtMachine)
    {
        var player = GameMain.mainPlayer;
        int side = startAtMachine ? -1 : 1;
        int newMachine = PlaceFacility(
            2309,
            Point(side * 13.17f, 0.31f),
            Quaternion.AngleAxis(side * 17, up) * Quaternion.LookRotation(north, up)
        );
        factory
            .factorySystem.assemblerPool[factory.entityPool[newMachine].assemblerId]
            .SetRecipe(
                factory
                    .factorySystem
                    .assemblerPool[factory.entityPool[directMachine].assemblerId]
                    .recipeId,
                factory.entitySignPool
            );
        player.position = Point(side * 11, -5);
        player.TryAddItemToPackage(Prototypes.ItemId, 100, 0, false, 0);
        player.controller.cmd.stage = 1;
        tool.startObjectId = startAtMachine ? newMachine : 0;
        tool.castObjectId = startAtMachine ? 0 : newMachine;
        tool.startTarget = startAtMachine
            ? factory.entityPool[newMachine].pos
            : Point(side * 13, -11);
        tool.cursorTarget = startAtMachine
            ? Point(side * 13, -11)
            : factory.entityPool[newMachine].pos;
        tool.castGroundPosSnapped = tool.cursorTarget;
        tool.cursorValid = true;
        tool.DeterminePreviews();
        AccessTools
            .Field(typeof(BuildTool), "tmpPackage")
            .SetValue(tool, new StorageComponent(player.package.size));
        AccessTools.Field(typeof(BuildTool), "tmpInhandId").SetValue(tool, Prototypes.ItemId);
        AccessTools.Field(typeof(BuildTool), "tmpInhandCount").SetValue(tool, 100);
        RequirePort(
            tool.buildPreviews.Count > 1,
            "Native construction produces a machine route with machine-start="
                + startAtMachine
                + "."
        );
        var endpoint = startAtMachine ? tool.buildPreviews.First() : tool.buildPreviews.Last();
        int connectedMachine = startAtMachine ? endpoint.inputObjId : endpoint.outputObjId;
        int slotIndex = startAtMachine ? endpoint.inputFromSlot : endpoint.outputToSlot;
        RequirePort(
            connectedMachine == newMachine,
            "Native pipe previews attach to a production-machine slot with machine-start="
                + startAtMachine
                + "."
        );
        var machine = factory.entityPool[newMachine];
        var slot = LDB.items.Select(machine.protoId).prefabDesc.slotPoses[slotIndex];
        var localTerminal = slot.position + slot.forward * 1.65f;
        localTerminal.y = 0;
        Vector3 expectedPosition = machine.pos + machine.rot * localTerminal;
        RequirePort(
            Vector3.Distance(endpoint.lpos, expectedPosition) < 0.001f
                && Vector3.Dot(
                    endpoint.lpos - (machine.pos + machine.rot * slot.position),
                    machine.rot * slot.forward
                ) > 1.6f,
            "Fractional machine sockets place their terminal outside the housing on the socket axis."
        );
        factory.planet.physics.nearColliderLogic.ActiveEntityBuildCollidersInArea(
            factory.entityPool[newMachine].pos,
            15
        );
        Physics.SyncTransforms();
        RequirePort(
            tool.CheckBuildConditions(),
            "Native collision and placement checks accept the selected machine socket: "
                + string.Join(
                    ",",
                    tool.buildPreviews.Select(preview => preview.condition.ToString())
                )
        );
        AccessTools.Field(typeof(BuildTool), "tmpInhandId").SetValue(tool, Prototypes.ItemId);
        AccessTools.Field(typeof(BuildTool), "tmpInhandCount").SetValue(tool, 100);
        tool.CreatePrebuilds();
        int[] pendingPipes = tool
            .buildPreviews.Where(preview => preview.objId < 0)
            .Select(preview => preview.objId)
            .ToArray();
        RequirePort(
            pendingPipes.Length > 0,
            "Native construction creates connected pipe prebuilds."
        );
        int itemId = startAtMachine ? Water : Oil;
        if (!startAtMachine)
        {
            RequirePort(
                PipeApi.TrySetMachinePortOptions(
                    factory,
                    newMachine,
                    slotIndex,
                    new MachinePortOptions(itemId)
                ),
                "An explicitly selected prebuild endpoint accepts its recipe input."
            );
        }
        foreach (int pending in pendingPipes.Reverse())
        {
            factory.BuildFinally(player, -pending, false, false);
        }
        factory.ReadObjectConn(newMachine, slotIndex, out _, out int builtPipe, out int pipeSlot);
        RequirePort(
            builtPipe > 0 && PipeApi.GetMachinePorts(factory, builtPipe).Length == 1,
            "Reverse-order native completion migrates direct-port prebuild identities to completed pipes."
        );
        RequirePort(
            PipeApi.GetMachinePorts(factory, builtPipe)[0].Options.ItemId
                == (startAtMachine ? 0 : itemId),
            "Native completion preserves explicit item settings and pending automatic selection."
        );
        RequirePort(
            PipeApi.TryInsert(factory, builtPipe, itemId, 80, 320),
            "A completed native route accepts its machine input liquid."
        );
        factory.factoryStorage.GameTickTank();
        ref var assembler = ref factory.factorySystem.assemblerPool[
            factory.entityPool[newMachine].assemblerId
        ];
        int inputIndex = Array.IndexOf(assembler.recipeExecuteData.requires, itemId);
        var inventory = PipePools.Query(PipeRuntime.Bind(factory.cargoTraffic), builtPipe);
        RequirePort(
            PipeApi.GetMachinePorts(factory, builtPipe)[0].Options.ItemId == itemId
                && assembler.served[inputIndex] > 0
                && assembler.served[inputIndex] + inventory.Count == 80
                && assembler.incServed[inputIndex] + inventory.ProliferatorPoints == 320,
            "The normal factory tick feeds a natively constructed machine route with conserved stock and points; automatic-selection="
                + startAtMachine
                + "."
        );
        int pipeItem = 0;
        factory.DismantleFinally(player, builtPipe, ref pipeItem);
        factory.ReadObjectConn(newMachine, slotIndex, out _, out int occupant, out _);
        RequirePort(
            occupant == 0 && PipeApi.GetMachinePorts(factory, builtPipe).Length == 0,
            "Native pipe dismantling removes its machine endpoint and frees the slot."
        );
    }

    private IEnumerator CaptureMachinePorts()
    {
        PipeVisuals.Draw(factory, GameCamera.main);
        SaveImage("ingame-machine-ports-overview.png");
        SetView(0, 0, 4, 7, 5, 0.7f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-machine-ports-socket.png");
        GameMain.mainPlayer.controller.actionInspect.SetInspectee(
            EObjectType.None,
            directWaterPipe
        );
        PipePanel.Update();
        PipeMachinePortPanel.Open(factory, directWaterPipe);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        RequirePort(
            GameObject.Find("FluidPipes.MachinePorts") != null
                && GameObject
                    .Find("FluidPipes.MachinePorts")
                    .GetComponentsInChildren<Text>()
                    .Any(text => text.text.Contains("供给机器")),
            "The in-game connection panel displays direction and recipe item choices."
        );
        SaveImage("ingame-machine-ports-settings.png", true);
        PipeMachinePortPanel.Hide();
        if (string.IsNullOrEmpty(inspectSave))
        {
            var inventoryChecks = CheckNativeInventoryUi();
            while (inventoryChecks.MoveNext())
            {
                yield return inventoryChecks.Current;
            }
            var appearanceChecks = CaptureFluidAppearance();
            while (appearanceChecks.MoveNext())
            {
                yield return appearanceChecks.Current;
            }
            fixtureSave = true;
            try
            {
                RequirePort(
                    GameSave.SaveCurrentGame("machine-ports"),
                    "A native sandbox save includes direct machine-port data."
                );
            }
            finally
            {
                fixtureSave = false;
            }
            foreach (string extension in new[] { ".dsv", ".moddsv" })
            {
                File.Copy(
                    System.IO.Path.Combine(GameConfig.gameSaveFolder, "machine-ports" + extension),
                    System.IO.Path.Combine(output, "machine-ports" + extension),
                    true
                );
            }
        }
        RequirePort(
            !GameMain.errored,
            "Direct-port construction, transfers and inspection complete without a native runtime error."
        );
    }

    private void RestoreMachinePortScene()
    {
        factory = GameMain.localPlanet.factory;
        var ports = factory
            .entityPool.Where(entity => entity.id > 0 && entity.protoId == Prototypes.ItemId)
            .SelectMany(entity => PipeApi.GetMachinePorts(factory, entity.id))
            .ToArray();
        RequirePort(
            ports.Length == 3,
            "A native save reload restores exactly three direct machine connections."
        );
        var water = ports.Single(port => port.Options.ItemId == Water);
        var oil = ports.Single(port => port.Options.ItemId == Oil);
        var acid = ports.Single(port => port.Options.ItemId == Acid);
        directMachine = water.MachineEntityId;
        directWaterPipe = water.PipeEntityId;
        directOilPipe = oil.PipeEntityId;
        directProductPipe = acid.PipeEntityId;
        directWaterSlot = water.SlotIndex;
        directOilSlot = oil.SlotIndex;
        directProductSlot = acid.SlotIndex;
        RequirePort(
            water.Options.Enabled
                && oil.Options.Enabled
                && acid.Options.Enabled
                && water.Options.Direction == MachinePortDirection.IntoMachine
                && oil.Options.Direction == MachinePortDirection.IntoMachine
                && acid.Options.Direction == MachinePortDirection.OutOfMachine,
            "Reloaded direct ports preserve their native slot, item filter, direction and enable state."
        );
        var machine = factory.entityPool[directMachine];
        center = machine.pos;
        up = center.normalized;
        east = machine.rot * Vector3.right;
        north = machine.rot * Vector3.forward;
        var infiniteValves = factory
            .entityPool.Where(entity =>
                entity.id > 0
                && entity.protoId == Prototypes.ValveId
                && PipeValves.Get(factory, entity.id).InfiniteEnabled
            )
            .ToArray();
        RequirePort(
            infiniteValves.Length == 1
                && PipeValves.Get(factory, infiniteValves[0].id).InfiniteItemId == Water
                && PipeValves.Get(factory, infiniteValves[0].id).InfiniteTarget == 0.75
                && PipeValves.Get(factory, infiniteValves[0].id).InfiniteMode
                    == InfiniteLevelMode.Exactly,
            "Native save reload preserves the active sandbox valve fluid, target and regulation mode."
        );
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        long previousStock = PipePools.View(graph, directWaterPipe).Count;
        PipeMachinePorts.Tick(factory);
        RequirePort(
            PipePools.View(graph, directWaterPipe).Count <= previousStock
                && !PipeApi.TryInsert(factory, directProductPipe, Water, 1),
            "Restored direct ports resume native transfer and retain empty-network item reservations."
        );
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(0, 0, 17, 22, 10, 1);
    }
}
