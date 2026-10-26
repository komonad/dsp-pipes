using System;
using System.Collections;
using System.Linq;
using FluidPipes;
using UnityEngine;

public sealed partial class SceneCapture
{
    private int nativeSorter;
    private int fluidSorter;
    private int sourcePipe;
    private int destinationPipe;
    private int comparisonBelt;
    private int comparisonFactory;
    private int clearancePipe;
    private int clearanceBelt;
    private int comparisonPipeSorter;

    private Vector3 InserterSocket(int entity, Vector3 toward)
    {
        var data = factory.entityPool[entity];
        var slots = LDB.items.Select(data.protoId).prefabDesc.slotPoses;
        if (slots == null || slots.Length == 0)
            throw new InvalidOperationException("Facility has no inserter sockets.");
        return slots
            .Select(p => data.pos + data.rot * p.position)
            .OrderBy(position2 => (position2 - toward).sqrMagnitude)
            .First();
    }

    private int AddSceneSorter(
        int source,
        int destination,
        Vector3 start,
        Vector3 end,
        bool build = true,
        int proto = 2013
    )
    {
        var item = LDB.items.Select(proto);
        var direction = Vector3.ProjectOnPlane(end - start, (start + end).normalized).normalized;
        if (direction.sqrMagnitude < 0.001f)
            direction = north;
        Func<int, Vector3, int> socket = (entity, point) =>
        {
            if (factory.entityPool[entity].beltId > 0)
                return -1;
            var entity2 = factory.entityPool[entity];
            var slots = LDB.items.Select(entity2.protoId).prefabDesc.slotPoses;
            return Enumerable
                .Range(0, slots.Length)
                .OrderBy(index =>
                    (entity2.pos + entity2.rot * slots[index].position - point).sqrMagnitude
                )
                .First();
        };
        int fromSlot = socket(source, start);
        int toSlot = socket(destination, end);
        var fromRot =
            fromSlot < 0
                ? Quaternion.LookRotation(direction, start.normalized)
                : factory.entityPool[source].rot
                    * LDB.items.Select(factory.entityPool[source].protoId)
                        .prefabDesc.slotPoses[fromSlot]
                        .rotation;
        var toRot =
            toSlot < 0
                ? Quaternion.LookRotation(direction, end.normalized)
                : factory.entityPool[destination].rot
                    * LDB.items.Select(factory.entityPool[destination].protoId)
                        .prefabDesc.slotPoses[toSlot]
                        .rotation
                    * Quaternion.Euler(0, 180, 0);
        var prebuild = new PrebuildData
        {
            protoId = (short)item.ID,
            modelIndex = (short)item.ModelIndex,
            pos = start,
            pos2 = end,
            rot = fromRot,
            rot2 = toRot,
            itemRequired = 1,
        };
        prebuild.InitParametersArray(1);
        prebuild.parameters[0] = 1;
        int pending = factory.AddPrebuildDataWithComponents(prebuild);
        factory.WriteObjectConn(-pending, 1, false, source, fromSlot);
        factory.WriteObjectConn(-pending, 0, true, destination, toSlot);
        factory.PostRefreshPrebuildDisplay(pending, true);
        if (!build)
            return -pending;
        factory.BuildFinally(GameMain.mainPlayer, pending, false, false);
        for (int entityId = 1; entityId < factory.entityCursor; entityId++)
            if (
                factory.entityPool[entityId].protoId == proto
                && (factory.entityPool[entityId].pos - start).sqrMagnitude < 0.0001f
            )
                return entityId;
        throw new InvalidOperationException(
            "Native inserter construction did not create its entity."
        );
    }

    private void CreateInserterScene()
    {
        var rotation = Quaternion.LookRotation(north, up);
        comparisonFactory = PlaceFacility(2309, Point(-5, 1), rotation);
        var ordinary = Path(2001, -6, -2, new[] { V(-1, 0), V(0, 0), V(1, 0) }, 0);
        comparisonBelt = ordinary[1];
        nativeSorter = AddSceneSorter(
            comparisonBelt,
            comparisonFactory,
            factory.entityPool[comparisonBelt].pos,
            InserterSocket(comparisonFactory, factory.entityPool[comparisonBelt].pos)
        );
        var comparisonPipe = Path(
            Prototypes.ItemId,
            -3,
            -2,
            new[] { V(-1, 0), V(0, 0), V(1, 0) },
            1000
        );
        comparisonPipeSorter = AddSceneSorter(
            comparisonPipe[1],
            comparisonFactory,
            factory.entityPool[comparisonPipe[1]].pos,
            InserterSocket(comparisonFactory, factory.entityPool[comparisonPipe[1]].pos)
        );
        int facility = PlaceFacility(2309, Point(5, 1), rotation);
        var incoming = Path(Prototypes.ItemId, 4, -2, new[] { V(-1, 0), V(0, 0), V(1, 0) }, 1000);
        AddSceneSorter(
            incoming[1],
            facility,
            factory.entityPool[incoming[1]].pos,
            InserterSocket(facility, factory.entityPool[incoming[1]].pos),
            true,
            2011
        );
        var outgoing = Path(Prototypes.ItemId, 7, -2, new[] { V(-1, 0), V(0, 0), V(1, 0) }, 0);
        AddSceneSorter(
            facility,
            outgoing[1],
            InserterSocket(facility, factory.entityPool[outgoing[1]].pos),
            factory.entityPool[outgoing[1]].pos
        );
        var a = Path(Prototypes.ItemId, -3, -7, new[] { V(-1, 0), V(0, 0), V(1, 0) }, 1000);
        var b = Path(Prototypes.ItemId, -3, -10, new[] { V(-1, 0), V(0, 0), V(1, 0) }, 0);
        sourcePipe = a[1];
        destinationPipe = b[1];
        fluidSorter = AddSceneSorter(
            sourcePipe,
            destinationPipe,
            factory.entityPool[sourcePipe].pos,
            factory.entityPool[destinationPipe].pos,
            true,
            2012
        );
        Path(
            Prototypes.ItemId,
            -3,
            -8.5f,
            new[] { V(-2, 0), V(-1, 0), V(0, 0), V(1, 0), V(2, 0) },
            1000
        );
        var overhead = new[]
        {
            new Vector3(-2, 1.3333333f, 0),
            new Vector3(-1, 1.3333333f, 0),
            new Vector3(0, 1.3333333f, 0),
            new Vector3(1, 1.3333333f, 0),
            new Vector3(2, 1.3333333f, 0),
        };
        clearancePipe = Path(Prototypes.ItemId, -3, -8, overhead, 1000)[2];
        clearanceBelt = Path(2001, -3, -9, overhead, 0)[2];
        var elevated = Path(
            Prototypes.ItemId,
            4,
            -9,
            new[]
            {
                new Vector3(-1, 1.3333333f, 0),
                new Vector3(0, 1.3333333f, 0),
                new Vector3(1, 1.3333333f, 0),
            },
            0
        );
        var lower = Path(Prototypes.ItemId, 4, -6, new[] { V(-1, 0), V(0, 0), V(1, 0) }, 1000);
        AddSceneSorter(
            lower[1],
            elevated[1],
            factory.entityPool[lower[1]].pos,
            factory.entityPool[elevated[1]].pos,
            true,
            2014
        );
        var pending = AddSceneSorter(
            lower[0],
            elevated[0],
            factory.entityPool[lower[0]].pos,
            factory.entityPool[elevated[0]].pos,
            false
        );
        if (!FluidInserterVisuals.UsesPipe(factory, pending))
            throw new InvalidOperationException(
                "Pending inserter does not recognize its pipe targets."
            );
        factory.planet.factoryModel.disableEntitySigns = true;
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(0, -2, 20, 23, 6);
    }

    private InserterRenderer SorterRenderer(int entity) =>
        (InserterRenderer)
            GameMain.gpuiManager.GetObjectRenderer(factory.entityPool[entity].modelIndex);

    private IEnumerator CaptureInserters()
    {
        FluidInserterVisuals.Draw(factory, GameCamera.main);
        int custom = FluidInserterVisuals.Count;
        if (custom != 5 || FluidInserterVisuals.UsesPipe(factory, nativeSorter))
            throw new InvalidOperationException(
                "Automatic inserter classification is incorrect: " + custom
            );
        var original = factory.entityPool[nativeSorter];
        var fluid = factory.entityPool[fluidSorter];
        var originalInstance = SorterRenderer(nativeSorter).instPool[original.modelId];
        var fluidInstance = SorterRenderer(fluidSorter).instPool[fluid.modelId];
        if (
            originalInstance.posx == 0 && originalInstance.posy == 0 && originalInstance.posz == 0
            || fluidInstance.objId == 0
            || fluidInstance.posx != 0
            || fluidInstance.posy != 0
            || fluidInstance.posz != 0
            || fluid.colliderId == 0
            || fluid.powerConId == 0
            || fluid.inserterId == 0
        )
            throw new InvalidOperationException(
                "Custom inserter display changes native ownership or components."
            );
        report.Add(
            "Automatic pipe-endpoint detection replaces five completed inserters. Pending and ordinary belt inserters use native displays. Native GPU ownership, power consumers and selection colliders remain present."
        );
        factory.ReadObjectConn(
            nativeSorter,
            0,
            out bool normalOutput,
            out int normalFactory,
            out int normalSlot
        );
        factory.ReadObjectConn(
            comparisonPipeSorter,
            0,
            out bool pipeOutput,
            out int pipeFactory,
            out int pipeSlot
        );
        if (
            !normalOutput
            || !pipeOutput
            || normalFactory != comparisonFactory
            || pipeFactory != comparisonFactory
            || normalSlot == pipeSlot
            || factory.entityPool[nativeSorter].protoId
                != factory.entityPool[comparisonPipeSorter].protoId
        )
            throw new InvalidOperationException(
                "Comparison inserters do not share one factory, native grade and flow direction."
            );
        report.Add(
            "Same-factory comparison: native Mk.III belt inserter and pipe inserter feed separate sockets "
                + normalSlot
                + "/"
                + pipeSlot
                + " of chemical plant entity "
                + comparisonFactory
                + "."
        );
        var grades = factory
            .factorySystem.inserterPool.Where(i =>
                i.id > 0 && FluidInserterVisuals.UsesPipe(factory, i.entityId)
            )
            .Select(i => i.grade)
            .Distinct()
            .OrderBy(g => g)
            .ToArray();
        if (grades.Length != 4)
            throw new InvalidOperationException("Inserter scene does not cover all native grades.");
        report.Add(
            "Pipe inserters retain native grades "
                + string.Join(",", grades)
                + " with independent appearance accents."
        );
        for (int entityId2 = 1; entityId2 < factory.factorySystem.inserterCursor; entityId2++)
        {
            var armData = factory.factorySystem.inserterPool[entityId2];
            if (
                armData.id != entityId2
                || !FluidInserterVisuals.UsesPipe(factory, armData.entityId)
            )
                continue;
            for (int port = 0; port < 2; port++)
            {
                factory.ReadObjectConn(
                    armData.entityId,
                    port,
                    out _,
                    out int machine,
                    out int slot
                );
                if (machine <= 0 || factory.entityPool[machine].beltId > 0)
                    continue;
                var data = factory.entityPool[machine];
                var native = LDB.items.Select(data.protoId).prefabDesc.slotPoses[slot];
                var nativePosition = data.pos + data.rot * native.position;
                var nativeNormal = data.rot * native.rotation * Vector3.forward;
                var resolved = FluidInserterVisuals.ResolveSocket(
                    factory,
                    machine,
                    slot,
                    nativePosition,
                    factory.entityPool[armData.entityId].pos
                );
                var expected = nativePosition + data.rot * native.rotation * Vector3.up * 0.5f;
                var displayPose = factory.factorySystem.inserterPosePool[entityId2];
                var endpointPosition =
                    port == 0 ? displayPose.pos2 : factory.entityPool[armData.entityId].pos;
                var endpointRotation =
                    port == 0 ? displayPose.rot2 : factory.entityPool[armData.entityId].rot;
                var casingRotation = Quaternion.LookRotation(
                    port == 0 ? -resolved.Normal : resolved.Normal,
                    resolved.Axis
                );
                if (
                    (resolved.Anchor - endpointPosition).sqrMagnitude > 1e-7f
                    || Quaternion.Angle(casingRotation, endpointRotation) > 0.05f
                )
                    throw new InvalidOperationException(
                        "Machine casing differs from the native inserter endpoint transform."
                    );
                float extension = Vector3.Dot(resolved.Center - expected, nativeNormal);
                if (
                    !resolved.Machine
                    || (resolved.Anchor - nativePosition).sqrMagnitude > 1e-8f
                    || Vector3.Cross(resolved.Center - expected, nativeNormal).sqrMagnitude > 1e-8f
                    || Math.Abs(extension - 0.87f) > 0.0001f
                    || Vector3.Dot(resolved.Normal, nativeNormal) < 0.9999f
                )
                    throw new InvalidOperationException(
                        "Machine coupling does not align with its native inserter socket."
                    );
                report.Add(
                    "Machine coupling matches native inserter slot "
                        + slot
                        + " axis; housing_extension="
                        + extension
                        + " port="
                        + native.position
                        + " normal="
                        + (native.rotation * Vector3.forward)
                );
            }
        }
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        var source = PipePools.View(graph, sourcePipe);
        var destination = PipePools.View(graph, destinationPipe);
        if (PipePools.SameSpace(graph, sourcePipe, destinationPipe))
            throw new InvalidOperationException("Inserter joins its two fluid spaces.");
        var system = factory.factorySystem;
        int armId = fluid.inserterId;
        long total = source.Count + destination.Count;
        long totalInc = source.ProliferatorPoints + destination.ProliferatorPoints;
        for (int tick = 0; tick < 600; tick++)
            system
                .inserterPool[armId]
                .InternalUpdate(factory, factory.entityNeeds, factory.entityAnimPool, 1);
        var arm = system.inserterPool[armId];
        source = PipePools.View(graph, sourcePipe);
        destination = PipePools.View(graph, destinationPipe);
        if (
            destination.Count <= 0
            || source.Count + destination.Count + arm.itemCount != total
            || source.ProliferatorPoints + destination.ProliferatorPoints + arm.itemInc != totalInc
        )
            throw new InvalidOperationException(
                "Reskinned native inserter loses fluid or proliferation points."
            );
        long count = destination.Count;
        int phase = arm.time;
        for (int tick = 0; tick < 120; tick++)
            system
                .inserterPool[armId]
                .InternalUpdate(factory, factory.entityNeeds, factory.entityAnimPool, 0);
        if (
            PipePools.View(graph, destinationPipe).Count != count
            || system.inserterPool[armId].time != phase
        )
            throw new InvalidOperationException("Native inserter moves without power.");
        report.Add(
            "Native powered inserter transfers between independent pipe spaces; stock and proliferation points are conserved. Zero supply stops transfer."
        );
        int before = FluidInserterVisuals.Rebuilds;
        for (int index = 0; index < 120; index++)
            FluidInserterVisuals.Draw(factory, GameCamera.main);
        if (before != FluidInserterVisuals.Rebuilds)
            throw new InvalidOperationException(
                "Static inserter geometry rebuilds during rendering."
            );
        report.Add("120 static draw passes rebuild zero inserter meshes.");
        CheckInserterClearance();
        for (int index2 = 0; index2 < 8; index2++)
            yield return null;
        SaveImage("ingame-inserters-overview.png");
        SetView(-4.5f, -0.4f, 4.8f, 8.5f, 2, 0.8f);
        for (int index3 = 0; index3 < 8; index3++)
            yield return null;
        SaveImage("ingame-inserters-same-factory.png");
        SetView(-4.5f, -0.6f, 1.9f, 5.6f, 0.5f, 0.65f);
        for (int index4 = 0; index4 < 8; index4++)
            yield return null;
        SaveImage("ingame-inserters-same-factory-closeup.png");
        SetView(5, -1, 5, 8, 3, 0.9f);
        for (int index5 = 0; index5 < 8; index5++)
            yield return null;
        SaveImage("ingame-inserters-factory.png");
        SetView(5, -0.8f, 1.9f, 4.6f, 1.8f, 0.65f);
        for (int index6 = 0; index6 < 8; index6++)
            yield return null;
        SaveImage("ingame-inserters-inlet.png");
        SetView(-3, -8.5f, 2.5f, 5.5f, 4, 0.7f);
        for (int index7 = 0; index7 < 8; index7++)
            yield return null;
        SaveImage("ingame-inserters-overpass.png");
        SetView(-3, -8.5f, 1.4f, 5, 4, 0.9f);
        for (int index8 = 0; index8 < 8; index8++)
            yield return null;
        SaveImage("ingame-inserters-tier-clearance.png");
        SetView(4, -7.5f, 4, 7, 5, 1.2f);
        for (int index9 = 0; index9 < 8; index9++)
            yield return null;
        SaveImage("ingame-inserters-height.png");
        SetView(-3, -1.2f, 3.3f, 3.4f, 1.4f, 0.9f);
        for (int frame = 0; frame < 8; frame++)
        {
            yield return null;
        }
        int consumerId = factory.entityPool[comparisonPipeSorter].powerConId;
        var powerSystem = factory.powerSystem;
        int oldNetwork = powerSystem.consumerPool[consumerId].networkId;
        int markerNetwork = powerSystem.NewNetwork();
        powerSystem.consumerPool[consumerId].networkId = markerNetwork;
        try
        {
            powerSystem.networkServes[markerNetwork] = 0;
            if (FluidInserterVisuals.PowerRatio(factory, comparisonPipeSorter) != 0)
            {
                throw new InvalidOperationException(
                    "Disconnected supply illuminates the inserter marker."
                );
            }
            SaveImage("ingame-inserters-unpowered.png");
            powerSystem.networkServes[markerNetwork] = 0.5f;
            if (FluidInserterVisuals.PowerRatio(factory, comparisonPipeSorter) != 0.5f)
            {
                throw new InvalidOperationException(
                    "Inserter marker ignores partial power supply."
                );
            }
            powerSystem.networkServes[markerNetwork] = 1;
            if (FluidInserterVisuals.PowerRatio(factory, comparisonPipeSorter) != 1)
            {
                throw new InvalidOperationException(
                    "Powered supply leaves the inserter marker dark."
                );
            }
            SaveImage("ingame-inserters-powered.png");
            report.Add(
                "Direction-marker intensity follows native consumer supply at 0%, 50% and 100% without geometry rebuilds."
            );
        }
        finally
        {
            powerSystem.consumerPool[consumerId].networkId = oldNetwork;
        }
        system.SetInserterPickTarget(armId, comparisonBelt, 0);
        system.SetInserterInsertTarget(armId, comparisonFactory, 0);
        var restored = SorterRenderer(fluidSorter).instPool[fluid.modelId];
        if (
            FluidInserterVisuals.UsesPipe(factory, fluidSorter)
            || FluidInserterVisuals.Count != custom - 1
            || (new Vector3(restored.posx, restored.posy, restored.posz) - fluid.pos).sqrMagnitude
                > 0.0001f
        )
            throw new InvalidOperationException(
                "Removing pipe targets does not restore the native inserter display."
            );
        system.SetInserterPickTarget(armId, sourcePipe, 0);
        system.SetInserterInsertTarget(armId, destinationPipe, 0);
        if (
            !FluidInserterVisuals.UsesPipe(factory, fluidSorter)
            || FluidInserterVisuals.Count != custom
        )
            throw new InvalidOperationException(
                "Reconnecting a pipe does not restore its custom display."
            );
        var previewChecks = CheckInserterPreview();
        while (previewChecks.MoveNext())
        {
            yield return previewChecks.Current;
        }
        int proto = 0;
        factory.DismantleFinally(GameMain.mainPlayer, fluidSorter, ref proto);
        if (FluidInserterVisuals.Count != custom - 1)
            throw new InvalidOperationException("Dismantling retains custom inserter geometry.");
        report.Add(
            "Changing both targets to ordinary facilities restores native geometry; reconnecting restores pipe geometry; dismantling removes the cached shape."
        );
        int pending = 0;
        for (int entityId3 = 1; entityId3 < factory.prebuildCursor; entityId3++)
            if (
                factory.prebuildPool[entityId3].id == entityId3
                && factory.prebuildPool[entityId3].protoId == 2013
            )
            {
                pending = entityId3;
                break;
            }
        factory.ReadObjectConn(-pending, 0, out _, out int targetB, out int slotB);
        factory.ReadObjectConn(-pending, 1, out _, out int targetA, out int slotA);
        factory.ClearObjectConn(-pending, 0);
        factory.ClearObjectConn(-pending, 1);
        if (
            FluidInserterVisuals.UsesPipe(factory, -pending)
            || FluidInserterVisuals.Count != custom - 1
        )
            throw new InvalidOperationException(
                "Pending connection changes alter the completed inserter display cache."
            );
        factory.WriteObjectConn(-pending, 0, true, targetB, slotB);
        factory.WriteObjectConn(-pending, 1, false, targetA, slotA);
        if (
            !FluidInserterVisuals.UsesPipe(factory, -pending)
            || FluidInserterVisuals.Count != custom - 1
        )
            throw new InvalidOperationException(
                "Pending reconnection alters the completed inserter display cache."
            );
        factory.RemovePrebuildWithComponents(pending);
        if (FluidInserterVisuals.Count != custom - 1)
            throw new InvalidOperationException("Removing a pending inserter retains its shape.");
        report.Add(
            "Pending connection changes and removal preserve the completed inserter display cache."
        );
    }

    private void CheckInserterClearance()
    {
        var entity = factory.entityPool[fluidSorter];
        var pose = factory.factorySystem.inserterPosePool[entity.inserterId];
        factory.ReadObjectConn(fluidSorter, 1, out _, out int a, out int slotA);
        factory.ReadObjectConn(fluidSorter, 0, out _, out int b, out int slotB);
        var first = FluidInserterVisuals.ResolveSocket(factory, a, slotA, entity.pos, pose.pos2);
        var last = FluidInserterVisuals.ResolveSocket(factory, b, slotB, pose.pos2, entity.pos);
        var origin = Point(-3, -8.5f);
        var mesh = FluidInserterMesh.Create(
            first.Local(origin),
            last.Local(origin),
            (first.Position + last.Position).normalized
        );
        var pipe = new PipeMesh();
        PipeVisuals.AddEntity(pipe, factory, clearancePipe, origin);
        var pipeMesh = pipe.Build("FluidPipes.ClearanceReference");
        try
        {
            float top = mesh.vertices.Max(position => Vector3.Dot(position, up));
            float pipeBottom = pipeMesh.vertices.Min(position2 => Vector3.Dot(position2, up));
            int encoded = factory.entityPool[clearanceBelt].colliderId;
            if (encoded <= 0)
                throw new InvalidOperationException("Second-tier belt has no collision volume.");
            var collider = factory.planet.physics.colChunks[encoded >> 20].colliderPool[
                encoded & 0xFFFFF
            ];
            float extent =
                Mathf.Abs(Vector3.Dot(collider.q * Vector3.right, up)) * collider.ext.x
                + Mathf.Abs(Vector3.Dot(collider.q * Vector3.up, up)) * collider.ext.y
                + Mathf.Abs(Vector3.Dot(collider.q * Vector3.forward, up)) * collider.ext.z;
            float beltBottom = Vector3.Dot(collider.pos - origin, up) - extent;
            if (pipeBottom - top < 0.08f || beltBottom - top < 0.08f)
                throw new InvalidOperationException(
                    "Inserter intersects second-tier transport: top="
                        + top
                        + " pipe_bottom="
                        + pipeBottom
                        + " belt_bottom="
                        + beltBottom
                );
            report.Add(
                "Second-tier clearance: inserter_top="
                    + top.ToString("F4")
                    + " pipe_gap="
                    + (pipeBottom - top).ToString("F4")
                    + " native_belt_collider_gap="
                    + (beltBottom - top).ToString("F4")
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(pipeMesh);
        }
    }

    private IEnumerator CheckInserterPreview()
    {
        var model = GameMain.mainPlayer.controller.actionBuild.model;
        model.Open();
        model.EarlyGameTick();
        var tool = new BuildTool_Inserter();
        tool._Init(GameMain.data);
        tool.factory = factory;
        tool.planet = factory.planet;
        var item = LDB.items.Select(2013);
        try
        {
            var ghost = new BuildPreview
            {
                item = item,
                desc = item.prefabDesc,
                inputObjId = sourcePipe,
                outputObjId = destinationPipe,
                lpos = factory.entityPool[sourcePipe].pos,
                lpos2 = factory.entityPool[destinationPipe].pos,
                lrot = Quaternion.LookRotation(north, up),
                lrot2 = Quaternion.LookRotation(north, up),
                needModel = true,
                condition = EBuildCondition.Ok,
            };
            tool.buildPreviews.Add(ghost);
            tool.UpdatePreviewModels(model);
            tool.UpdatePreviewModelConditions(model);
            if (model.previewModelCursor != 1 || ghost.previewIndex < 0 || !ghost.needModel)
                throw new InvalidOperationException(
                    "Pipe inserter preview has no native arm model."
                );
            FluidInserterVisuals.Draw(factory, GameCamera.main);
            int before = FluidInserterVisuals.Rebuilds;
            for (int index = 0; index < 60; index++)
            {
                yield return null;
                model.EarlyGameTick();
                tool.UpdatePreviewModels(model);
                tool.UpdatePreviewModelConditions(model);
                FluidInserterVisuals.Draw(factory, GameCamera.main);
                if (model.previewModelCursor != 1 || ghost.previewIndex < 0 || !ghost.needModel)
                {
                    throw new InvalidOperationException(
                        "Native pipe inserter preview disappears at frame " + index
                    );
                }
            }
            if (before != FluidInserterVisuals.Rebuilds)
                throw new InvalidOperationException(
                    "A stationary pipe inserter preview regenerates its mesh."
                );
            ghost.inputObjId = comparisonBelt;
            ghost.outputObjId = comparisonFactory;
            tool.UpdatePreviewModels(model);
            tool.UpdatePreviewModelConditions(model);
            if (model.previewModelCursor != 1 || ghost.previewIndex < 0)
                throw new InvalidOperationException(
                    "Ordinary inserter previews lose their native model."
                );
            report.Add(
                "Pipe inserter construction retains one native preview across 60 rendered frames and rebuilds zero procedural meshes. Ordinary inserters use the same native preview path."
            );
        }
        finally
        {
            model.Close();
            tool._Free();
        }
    }
}
