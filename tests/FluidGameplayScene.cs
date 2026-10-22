using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FluidPipes;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class SceneCapture
{
    private int valveEntity;
    private int tankA;
    private int tankB;
    private int towerEntity;
    private int sharedPipe;

    private int PlaceFacility(int proto, Vector3 pos, Quaternion rotation) =>
        factory.AddEntityDataWithComponents(
            new EntityData
            {
                protoId = (short)proto,
                modelIndex = (short)LDB.items.Select(proto).ModelIndex,
                pos = pos,
                rot = rotation,
                alt = pos.magnitude,
                localized = true,
            },
            0
        );

    private Vector3 Port(int entity, int slot)
    {
        var entity2 = factory.entityPool[entity];
        return entity2.pos
            + entity2.rot * LDB.items.Select(entity2.protoId).prefabDesc.portPoses[slot].position;
    }

    private int NearestPort(int entity, Vector3 toward)
    {
        var entity2 = factory.entityPool[entity];
        var ports = LDB.items.Select(entity2.protoId).prefabDesc.portPoses;
        int best = 0;
        float distance = float.MaxValue;
        for (int index = 0; index < ports.Length; index++)
        {
            float d = (Port(entity, index) - toward).sqrMagnitude;
            if (d < distance)
            {
                distance = d;
                best = index;
            }
        }
        return best;
    }

    private int[] FluidLine(Vector3 start, Vector3 end)
    {
        var delta = (end - start).normalized;
        start += delta * 0.65f;
        end -= delta * 0.65f;
        int count = Math.Max(2, Mathf.CeilToInt(Vector3.Distance(start, end) / 1.2f) + 1);
        var ids = new int[count];
        for (int index = 0; index < count; index++)
        {
            var pos = Vector3.Lerp(start, end, (float)index / (count - 1));
            pos = pos.normalized * (factory.planet.realRadius + 0.2f);
            ids[index] = PlaceFacility(
                Prototypes.ItemId,
                pos,
                Quaternion.LookRotation(delta, pos.normalized)
            );
            if (index > 0)
                Connect(ids[index - 1], ids[index], 1);
        }
        return ids;
    }

    private void Attach(int building, int slot, int pipe, bool output)
    {
        factory.WriteObjectConn(building, slot, output, pipe, output ? 1 : 0);
        var entity = factory.entityPool[building];
        int belt = factory.entityPool[pipe].beltId;
        if (entity.tankId > 0)
            factory.factoryStorage.SetTankBelt(entity.tankId, belt, slot, output);
        else if (entity.splitterId > 0)
            factory.cargoTraffic.ConnectToSplitter(entity.splitterId, belt, slot, !output);
        else if (entity.stationId > 0)
            factory.transport.stationPool[entity.stationId].slots[slot] = new SlotData
            {
                beltId = belt,
                dir = output ? IODir.Output : IODir.Input,
                storageIdx = 1,
            };
        PipePools.Dirty(factory);
        PipeVisuals.Dirty(factory, pipe);
    }

    private void CreateGameplay()
    {
        valveEntity = PlaceFacility(
            Prototypes.ValveId,
            Point(0, 0),
            Quaternion.LookRotation(east, up)
        );
        if (
            factory.entityPool[valveEntity].splitterId == 0
            || factory.entityPool[valveEntity].pilerId != 0
            || factory.entityPool[valveEntity].powerConId == 0
        )
            throw new InvalidOperationException("Valve has incorrect native components.");
        var aDirection = (Port(valveEntity, 0) - factory.entityPool[valveEntity].pos).normalized;
        tankA = PlaceFacility(
            2106,
            (center + aDirection * 9).normalized * (factory.planet.realRadius + 0.2f),
            Quaternion.LookRotation(north, up)
        );
        tankB = PlaceFacility(
            2106,
            (center - aDirection * 9).normalized * (factory.planet.realRadius + 0.2f),
            Quaternion.LookRotation(north, up)
        );
        int firstPrototypeId = NearestPort(tankA, Port(valveEntity, 0));
        int secondPrototypeId = NearestPort(tankB, Port(valveEntity, 1));
        var a = FluidLine(Port(tankA, firstPrototypeId), Port(valveEntity, 0));
        var b = FluidLine(Port(valveEntity, 1), Port(tankB, secondPrototypeId));
        Attach(tankA, firstPrototypeId, a[0], true);
        Attach(valveEntity, 0, a[a.Length - 1], false);
        Attach(valveEntity, 1, b[0], true);
        Attach(tankB, secondPrototypeId, b[b.Length - 1], false);
        sharedPipe = a[0];
        towerEntity = PlaceFacility(2103, Point(-9, 10), Quaternion.LookRotation(north, up));
        var tower = factory.transport.stationPool[factory.entityPool[towerEntity].stationId];
        tower.storage[0] = new StationStore { itemId = 1000, max = 5000 };
        tower.storage[1] = new StationStore
        {
            itemId = 1101,
            max = 5000,
            count = 123,
            inc = 492,
        };
        int pt = NearestPort(towerEntity, factory.entityPool[a[0]].pos);
        var line = FluidLine(Port(towerEntity, pt), factory.entityPool[a[0]].pos);
        Attach(towerEntity, pt, line[0], true);
        factory.WriteObjectConn(line[line.Length - 1], 0, true, a[0], 2);
        ref var tank = ref factory.factoryStorage.tankPool[factory.entityPool[tankA].tankId];
        tank.fluidId = 1000;
        tank.fluidCount = 4000;
        tank.fluidInc = 16000;
        ref var tank2 = ref factory.factoryStorage.tankPool[factory.entityPool[tankB].tankId];
        tank2.fluidId = 1000;
        tank2.fluidCount = 1000;
        tank2.fluidInc = 4000;
        PipeValves.Get(factory, valveEntity).Enabled = false;
        PipePools.Dirty(factory);
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        PipeValves.Ends(factory, valveEntity, out int endA, out int endB);
        PipePools.Empty(graph, endA);
        PipePools.Empty(graph, endB);
        if (
            !PipePools.Insert(graph, endA, 1000, 5500, 22000)
            || !PipePools.Insert(graph, endB, 1000, 1000, 4000)
        )
            throw new InvalidOperationException(
                "Native fluid fixture could not fill its isolated spaces."
            );
        var sideAInventory = PipePools.View(graph, endA, true);
        var sideBInventory = PipePools.View(graph, endB, true);
        if (
            PipePools.SameSpace(graph, endA, endB)
            || sideAInventory.Count != 5500
            || sideBInventory.Count != 1000
            || sideAInventory.Containers != 2
            || sideBInventory.Containers != 1
        )
            throw new InvalidOperationException(
                "Native valve or container connections do not form two fluid spaces: "
                    + sideAInventory.Count
                    + "/"
                    + sideBInventory.Count
                    + " containers="
                    + sideAInventory.Containers
                    + "/"
                    + sideBInventory.Containers
            );
        report.Add(
            "Native factory valve: exactly two pipe ports, separate A/B fluid spaces, actual tank and tower storage participation, unrelated solid slot retained."
        );
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(0, 2, 25, 22);
    }

    private void PanelButton(string label)
    {
        var panel = GameObject.Find("FluidPipes.Panel");
        if (panel == null)
            throw new InvalidOperationException("Pipe panel is absent.");
        var button = panel
            .GetComponentsInChildren<Button>(false)
            .FirstOrDefault(button2 => button2.GetComponentInChildren<Text>()?.text == label);
        if (button == null)
            throw new InvalidOperationException("Pipe panel button is absent: " + label);
        button.onClick.Invoke();
    }

    private IEnumerator CaptureGameplay()
    {
        var player = GameMain.mainPlayer;
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        player.position = factory.entityPool[sharedPipe].pos;
        player.controller.actionInspect.SetInspectee(EObjectType.None, sharedPipe);
        for (int index = 0; index < 12; index++)
            yield return null;
        PipePanel.Update();
        if (!UIRoot.instance.uiGame.beltWindow.active)
            throw new InvalidOperationException(
                "Inspecting a pipe does not open its native window."
            );
        long before = PipePools.View(graph, sharedPipe, true).Count;
        player.SetHandItemId_Unsafe(1000);
        player.SetHandItemCount_Unsafe(100);
        player.SetHandItemInc_Unsafe(400);
        PanelButton("放入手持液体");
        if (
            player.inhandItemCount != 0
            || PipePools.View(graph, sharedPipe, true).Count != before + 100
        )
            throw new InvalidOperationException("Pipe deposit interaction failed.");
        PanelButton("取出一组到手上");
        if (
            player.inhandItemCount <= 0
            || PipePools.View(graph, sharedPipe, true).Count + player.inhandItemCount
                != before + 100
        )
            throw new InvalidOperationException("Pipe withdrawal interaction failed.");
        for (int index2 = 0; index2 < 6; index2++)
            yield return null;
        SaveImage("ingame-gameplay-shared-pool.png", true);
        PanelButton("清空全部连通液体");
        PanelButton("再次点击确认清空全部连通液体");
        PipeValves.Ends(factory, valveEntity, out int a, out int b);
        if (
            PipePools.View(graph, a, true).Count != 0
            || PipePools.View(graph, b, true).Count != 1000
            || factory
                .transport
                .stationPool[factory.entityPool[towerEntity].stationId]
                .storage[1]
                .count != 123
        )
            throw new InvalidOperationException(
                "Pool clear crosses valve isolation or unrelated tower storage."
            );
        PipePools.Insert(graph, a, 1000, 5500, 22000);
        PipePools.View(graph, a, true);
        report.Add(
            "Inspecting a native pipe opens the uGUI panel; actual button callbacks deposit and withdraw hand-held water, and clear only the selected connected fluid space."
        );
        player.position = factory.entityPool[valveEntity].pos;
        player.controller.actionInspect.SetInspectee(EObjectType.None, valveEntity);
        for (int index3 = 0; index3 < 12; index3++)
            yield return null;
        PipePanel.Update();
        if (!UIRoot.instance.uiGame.splitterWindow.active)
            throw new InvalidOperationException(
                "Inspecting a native valve does not open its control panel."
            );
        CheckNativeValvePower();
        PipePanel.Update();
        for (int index4 = 0; index4 < 6; index4++)
            yield return null;
        SaveImage("ingame-gameplay-valve.png", true);
        player.controller.actionInspect.InspectNothing();
        PipePanel.Update();
        var tool = new BuildTool_Path();
        tool._Init(GameMain.data);
        tool.factory = factory;
        tool.planet = factory.planet;
        tool.handItem = LDB.items.Select(Prototypes.ItemId);
        tool.handPrefabDesc = tool.handItem.prefabDesc;
        var model = player.controller.actionBuild.model;
        model.Open();
        model.EarlyGameTick();
        CheckHeightPlanning(tool, model);
        CheckOccupiedPlanning(tool, model);
        tool.buildPreviews.Clear();
        model.EarlyGameTick();
        for (int index5 = 0; index5 < 10; index5++)
            tool.buildPreviews.Add(
                new BuildPreview
                {
                    item = tool.handItem,
                    desc = tool.handPrefabDesc,
                    lpos = Point(index5 - 4, -6),
                    lrot = Quaternion.identity,
                    isConnNode = true,
                    needModel = true,
                }
            );
        UndirectedConnections.Previews(tool);
        tool.UpdatePreviewModels(model);
        tool.UpdatePreviewModelConditions(model);
        if (model.previewModelCursor != 0 || model.connGraph.pointCount != 10)
            throw new InvalidOperationException(
                "Pipe construction does not use native point previews."
            );
        model.connRenderer.Update();
        CheckPlanningLines(model, 9);
        model.connRenderer.Draw();
        SetView(0, -4, 15, 13);
        SaveImage("ingame-gameplay-grid-preview.png");
        model.Close();
        tool._Free();
        report.Add(
            "Ten pipe previews render through the native connection-point renderer with zero model previews and no procedural preview mesh generation."
        );
        player.package.SetSize(600);
        long stock = WorldWater();
        long bag = player.package.GetItemCount(1000);
        int proto = 0;
        bool instant = GameMain.preferences.instantDismantle;
        try
        {
            GameMain.preferences.instantDismantle = false;
            factory.DismantleFinally(player, tankA, ref proto);
            PipePools.Tick(factory);
        }
        finally
        {
            GameMain.preferences.instantDismantle = instant;
        }
        if (WorldWater() + player.package.GetItemCount(1000) != stock + bag)
            throw new InvalidOperationException(
                "Native tank dismantling does not conserve shared water and refunds: world="
                    + stock
                    + "->"
                    + WorldWater()
                    + ", bag="
                    + bag
                    + "->"
                    + player.package.GetItemCount(1000)
            );
        report.Add(
            "Native tank dismantling conserves water across the remaining shared containers and the player's inventory."
        );
    }

    private void CheckPlanningLines(BuildModel model, int expected)
    {
        var native =
            (List<ConnGizmoObj>)
                AccessTools.Field(typeof(ConnGizmoRenderer), "objs_2").GetValue(model.connRenderer);
        if (native.Count != 0)
            throw new InvalidOperationException(
                "Pipe cursor emits directional native lines: " + native.Count
            );
        var mesh = PipePlanningGizmos.LineMesh;
        var vertices = mesh.vertices;
        if (
            mesh.triangles.Length != 18
            || mesh.triangles.Any(index => Mathf.Abs(vertices[index].x) > 0.061f)
        )
            throw new InvalidOperationException("Pipe line retains arrow triangles.");
        var states = AccessTools.Field(typeof(PipePlanningGizmos), "states").GetValue(null);
        var arguments = new object[] { model.connRenderer, null };
        if (!(bool)states.GetType().GetMethod("TryGetValue").Invoke(states, arguments))
            throw new InvalidOperationException("Pipe line cache is absent.");
        var items =
            (List<ConnGizmoObj>)arguments[1].GetType().GetField("Items").GetValue(arguments[1]);
        if (items.Count != expected)
            throw new InvalidOperationException(
                "Pipe preview line count=" + items.Count + ", expected=" + expected
            );
        report.Add(
            "Pipe preview uses arrow-free rectangular line geometry; native directional cursor lines="
                + native.Count
                + ", planned edges="
                + expected
        );
    }

    private void CheckHeightPlanning(BuildTool_Path tool, BuildModel model)
    {
        var player = GameMain.mainPlayer;
        int oldStage = player.controller.cmd.stage;
        float reach = player.mecha.buildArea;
        var position = player.position;
        var start = Point(-4, -15, 2.6666667f);
        var end = Point(4, -15, 9.3333333f);
        var top = Point(-4, -15, 10.6666667f);
        int a = PlaceFacility(
            Prototypes.ItemId,
            start,
            Quaternion.LookRotation(north, start.normalized)
        );
        int b = PlaceFacility(
            Prototypes.ItemId,
            end,
            Quaternion.LookRotation(north, end.normalized)
        );
        int c = PlaceFacility(
            Prototypes.ItemId,
            top,
            Quaternion.LookRotation(north, top.normalized)
        );
        int peer = PlaceFacility(
            Prototypes.ItemId,
            Point(-4, -14, 2.6666667f),
            Quaternion.LookRotation(north, start.normalized)
        );
        Connect(a, peer, 1);
        try
        {
            player.controller.cmd.stage = 1;
            player.mecha.buildArea = 10000;
            player.position = start;
            AccessTools.Field(typeof(BuildTool), "tmpInhandId").SetValue(tool, Prototypes.ItemId);
            foreach (bool vertical in new[] { false, true })
            foreach (bool reverse in new[] { false, true })
            foreach (bool attached in new[] { false, true })
            {
                int first = reverse ? (vertical ? c : b) : a;
                int last = reverse ? a : (vertical ? c : b);
                tool.startTarget = factory.entityPool[first].pos;
                tool.cursorTarget = factory.entityPool[last].pos;
                tool.startObjectId = attached ? first : 0;
                tool.castObjectId = attached ? last : 0;
                tool.cursorValid = true;
                tool.geodesic = true;
                tool.forceVertical = false;
                tool.DeterminePreviews();
                var previews = tool.buildPreviews;
                if (
                    previews.Count < 3
                    || Vector3.Distance(previews[0].lpos, tool.startTarget) > 0.001f
                    || Vector3.Distance(previews[previews.Count - 1].lpos, tool.cursorTarget)
                        > 0.001f
                )
                    throw new InvalidOperationException("Height planning loses exact endpoints.");
                float sign = Mathf.Sign(tool.cursorTarget.magnitude - tool.startTarget.magnitude);
                for (int index = 1; index < previews.Count; index++)
                    if (
                        (previews[index].lpos.magnitude - previews[index - 1].lpos.magnitude) * sign
                            < 0.01f
                        || Vector3.Distance(previews[index].lpos, previews[index - 1].lpos) > 1.334f
                    )
                        throw new InvalidOperationException(
                            "Height planning retains a horizontal landing or exceeds node spacing."
                        );
                if (attached)
                {
                    AccessTools.Field(typeof(BuildTool), "tmpInhandCount").SetValue(tool, 200);
                    factory.planet.physics.nearColliderLogic.ActiveEntityBuildCollidersInArea(
                        (start + top + end) / 3,
                        20
                    );
                    Physics.SyncTransforms();
                    if (!tool.CheckBuildConditions())
                        throw new InvalidOperationException(
                            "Sloped/vertical connected pipe rejected: "
                                + string.Join(
                                    ",",
                                    previews.Select(preview2 => preview2.condition.ToString())
                                )
                        );
                }
                model.EarlyGameTick();
                tool.UpdatePreviewModels(model);
                tool.UpdatePreviewModelConditions(model);
                model.connRenderer.Update();
                CheckPlanningLines(model, previews.Count - 1);
                report.Add(
                    "Native pipe planning vertical="
                        + vertical
                        + " reverse="
                        + reverse
                        + " attached="
                        + attached
                        + " nodes="
                        + previews.Count
                        + " preserves both endpoints and continuous height changes."
                );
                if (!reverse && attached)
                {
                    model.connRenderer.Draw();
                    SetView(0, -14, 18, 16, 0, 4);
                    SaveImage(
                        vertical
                            ? "ingame-gameplay-vertical-preview.png"
                            : "ingame-gameplay-sloped-preview.png"
                    );
                }
            }
            var aux = player.controller.actionBuild.planetAux;
            var original = new Vector3[256];
            var wrapped = new Vector3[256];
            float s1 = 0;
            float s2 = 0;
            tool.handItem = LDB.items.Select(2001);
            int n1 = aux.SnapLineNonAlloc(
                start,
                end,
                1,
                true,
                true,
                original,
                false,
                ref s1,
                false
            );
            int n2 = PipePlanning.Snap(
                aux,
                start,
                end,
                1,
                true,
                true,
                wrapped,
                false,
                ref s2,
                false,
                tool
            );
            if (
                n1 != n2
                || original.Where((position2, index2) => position2 != wrapped[index2]).Any()
            )
                throw new InvalidOperationException("Belt height planning changed.");
            tool.buildPreviews.Clear();
            for (int index3 = 0; index3 < n1; index3++)
                tool.buildPreviews.Add(
                    new BuildPreview { lpos = original[index3], isConnNode = true }
                );
            model.EarlyGameTick();
            tool.UpdatePreviewModelConditions(model);
            model.connRenderer.Update();
            var lines =
                (List<ConnGizmoObj>)
                    AccessTools
                        .Field(typeof(ConnGizmoRenderer), "objs_2")
                        .GetValue(model.connRenderer);
            if (lines.Count != n1 - 1)
                throw new InvalidOperationException("Belt preview arrows are missing.");
            report.Add(
                "Ordinary belt height planning and directional preview lines retain their native behavior."
            );
            tool.handItem = LDB.items.Select(Prototypes.ItemId);
            tool.handPrefabDesc = tool.handItem.prefabDesc;
            tool.startObjectId = a;
            tool.castObjectId = b;
            tool.startTarget = start;
            tool.cursorTarget = end;
            tool.DeterminePreviews();
            player.TryAddItemToPackage(Prototypes.ItemId, 200, 0, false, 0);
            tool.CreatePrebuilds();
            foreach (var preview in tool.buildPreviews)
                if (preview.objId < 0)
                    factory.BuildFinally(player, -preview.objId, false, false);
            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            if (
                graph.View(a).Nodes != tool.buildPreviews.Count + 1
                || graph.View(a).Nodes != graph.View(b).Nodes
            )
                throw new InvalidOperationException(
                    "Native sloped pipe completion loses its endpoints."
                );
            tool.DeterminePreviews();
            int entityCount = factory.entityCount;
            int prebuildCount = factory.prebuildCount;
            if (PipePlacementOccupancy.Validate(tool))
                throw new InvalidOperationException("Overlaid completed pipe path is accepted.");
            tool.CreatePrebuilds();
            if (factory.entityCount != entityCount || factory.prebuildCount != prebuildCount)
                throw new InvalidOperationException("Overlaid path creates duplicate nodes.");
            report.Add(
                "A native sloped plan completes into one connected pipe network with reused endpoints; overlaying the completed route creates no additional nodes."
            );
        }
        finally
        {
            tool.handItem = LDB.items.Select(Prototypes.ItemId);
            tool.handPrefabDesc = tool.handItem.prefabDesc;
            tool.startObjectId = tool.castObjectId = 0;
            player.controller.cmd.stage = oldStage;
            player.mecha.buildArea = reach;
            player.position = position;
        }
    }

    private void CheckOccupiedPlanning(BuildTool_Path tool, BuildModel model)
    {
        var player = GameMain.mainPlayer;
        int stage = player.controller.cmd.stage;
        try
        {
            player.controller.cmd.stage = 1;
            var position2 = Point(13, -16, 2.6666667f);
            int built = PlaceFacility(
                Prototypes.ItemId,
                position2,
                Quaternion.LookRotation(north, position2.normalized)
            );
            var q = Point(16, -16, 2.6666667f);
            int pending = factory.AddPrebuildDataWithComponents(
                new PrebuildData
                {
                    protoId = (short)Prototypes.ItemId,
                    modelIndex = (short)tool.handItem.ModelIndex,
                    pos = q,
                    rot = Quaternion.LookRotation(north, q.normalized),
                    itemRequired = 1,
                }
            );
            foreach (var position in new[] { position2, q })
            {
                tool.startObjectId = tool.castObjectId = 0;
                tool.buildPreviews.Clear();
                tool.buildPreviews.Add(
                    new BuildPreview
                    {
                        item = tool.handItem,
                        desc = tool.handPrefabDesc,
                        lpos = position,
                    }
                );
                int entities = factory.entityCount;
                int prebuilds = factory.prebuildCount;
                if (
                    PipePlacementOccupancy.Validate(tool)
                    || tool.buildPreviews[0].condition != EBuildCondition.Collide
                )
                    throw new InvalidOperationException("Duplicate pipe center is accepted.");
                tool.CreatePrebuilds();
                if (factory.entityCount != entities || factory.prebuildCount != prebuilds)
                    throw new InvalidOperationException(
                        "Duplicate pipe placement creates another entity."
                    );
            }
            tool.startObjectId = built;
            tool.buildPreviews.Clear();
            tool.buildPreviews.Add(
                new BuildPreview
                {
                    item = tool.handItem,
                    desc = tool.handPrefabDesc,
                    lpos = position2,
                }
            );
            if (
                !PipePlacementOccupancy.Validate(tool)
                || tool.buildPreviews[0].coverObjId != built
                || tool.buildPreviews[0].willRemoveCover
            )
                throw new InvalidOperationException("Existing pipe endpoint is not reused.");
            tool.startObjectId = tool.castObjectId = 0;
            tool.buildPreviews.Clear();
            model.EarlyGameTick();
            int pendingB = factory.AddPrebuildDataWithComponents(
                new PrebuildData
                {
                    protoId = (short)Prototypes.ItemId,
                    modelIndex = (short)tool.handItem.ModelIndex,
                    pos = q + east,
                    rot = Quaternion.LookRotation(north, q.normalized),
                    itemRequired = 1,
                }
            );
            factory.WriteObjectConn(-pending, 0, true, -pendingB, 1);
            model.connRenderer.Update();
            CheckPlanningLines(model, 2);
            model.connRenderer.Update();
            CheckPlanningLines(model, 2);
            factory.RemovePrebuildWithComponents(pending);
            factory.RemovePrebuildWithComponents(pendingB);
            report.Add(
                "Built and pending pipe centers reject duplicate placement before native creation; selected endpoints are reused and pending connections render without directional arrows across cached updates."
            );
        }
        finally
        {
            player.controller.cmd.stage = stage;
            tool.startObjectId = tool.castObjectId = 0;
            tool.buildPreviews.Clear();
            model.EarlyGameTick();
        }
    }

    private long WorldWater()
    {
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        long count = 0;
        foreach (int entity in graph.Representatives())
        {
            var inventoryView = graph.View(entity);
            if (inventoryView.Item == 1000)
                count += inventoryView.Count;
        }
        for (int index = 1; index < factory.factoryStorage.tankCursor; index++)
        {
            var tank = factory.factoryStorage.tankPool[index];
            if (tank.id == index && tank.fluidId == 1000)
                count += tank.fluidCount;
        }
        for (int index2 = 1; index2 < factory.transport.stationCursor; index2++)
        {
            var s = factory.transport.stationPool[index2];
            if (s != null && s.id == index2)
                foreach (var store in s.storage)
                    if (store.itemId == 1000)
                        count += store.count;
        }
        return count;
    }

    private void CheckNativeValvePower()
    {
        var power = factory.powerSystem;
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        var config = PipeValves.Get(factory, valveEntity);
        PipeValves.Ends(factory, valveEntity, out int a, out int b);
        int pc = factory.entityPool[valveEntity].powerConId;
        config.Enabled = true;
        config.Rate = 60;
        if (
            ValvePower.Supply(factory, valveEntity) != 0
            || PipeValves.Step(factory, valveEntity, 10) != 0
        )
            throw new InvalidOperationException(
                "Native unconnected valve pumps without electricity."
            );
        int pole = PlaceFacility(2201, Point(0, -4), Quaternion.LookRotation(north, up));
        int generator = PlaceFacility(2203, Point(5, -5), Quaternion.LookRotation(north, up));
        int gen = factory.entityPool[generator].powerGenId;
        int network = power.consumerPool[pc].networkId;
        if (network == 0 || power.genPool[gen].networkId != network)
            throw new InvalidOperationException(
                "Native electricity coverage does not connect the valve to its generator."
            );
        float wind = factory.planet.windStrength;
        long original = power.genPool[gen].genEnergyPerTick;
        try
        {
            factory.planet.windStrength = 1;
            foreach (long capacity in new[] { 0L, 834L, 3334L })
            {
                config.Enabled = false;
                PipeValves.Step(factory, valveEntity, 1.0 / 60);
                PipePools.Empty(graph, a);
                PipePools.Empty(graph, b);
                PipePools.Insert(graph, a, 1000, 5500, 22000);
                PipePools.Insert(graph, b, 1000, 1000, 4000);
                config.Enabled = true;
                power.genPool[gen].genEnergyPerTick = capacity;
                long moved = 0;
                for (int index = 0; index < 60; index++)
                {
                    ValvePower.Demand(factory, valveEntity, true);
                    power.GameTick(GameMain.gameTick + index, false, false, 0);
                    moved += PipeValves.Step(factory, valveEntity, 1.0 / 60);
                }
                long expected =
                    capacity == 0 ? 0
                    : capacity == 834 ? 30
                    : 60;
                if (
                    moved != expected
                    || power.netPool[network].energyRequired != 1667
                    || PipePools.View(graph, a, true).Count + PipePools.View(graph, b, true).Count
                        != 6500
                )
                    throw new InvalidOperationException(
                        "Native valve power mismatch capacity="
                            + capacity
                            + " moved="
                            + moved
                            + " expected="
                            + expected
                            + " required="
                            + power.netPool[network].energyRequired
                    );
                report.Add(
                    "Native valve power capacity_J_tick="
                        + capacity
                        + " demand_J_tick="
                        + power.netPool[network].energyRequired
                        + " supply="
                        + ValvePower.Supply(factory, valveEntity)
                        + " moved_per_second="
                        + moved
                );
            }
            config.Enabled = false;
            PipeValves.Step(factory, valveEntity, 1.0 / 60);
            power.GameTick(GameMain.gameTick, false, false, 0);
            if (power.consumerPool[pc].requiredEnergy != 0)
                throw new InvalidOperationException("Closed native valve consumes pump power.");
            int consumers = power.consumerCount;
            power.RemoveConsumerComponent(pc);
            factory.entityPool[valveEntity].powerConId = 0;
            ValvePower.Restore(factory);
            int restored = factory.entityPool[valveEntity].powerConId;
            ValvePower.Restore(factory);
            if (
                restored == 0
                || power.consumerCount != consumers
                || power.consumerPool[restored].networkId != network
            )
                throw new InvalidOperationException(
                    "Native legacy valve migration duplicates consumers or loses electrical coverage."
                );
            report.Add(
                "Native electricity coverage and power accounting provide 0/half/full valve speed, zero closed-valve demand, and idempotent legacy power-component restoration."
            );
        }
        finally
        {
            factory.planet.windStrength = wind;
            power.genPool[gen].genEnergyPerTick = original;
            config.Enabled = false;
            PipePools.Empty(graph, a);
            PipePools.Empty(graph, b);
            PipePools.Insert(graph, a, 1000, 5500, 22000);
            PipePools.Insert(graph, b, 1000, 1000, 4000);
        }
    }
}
