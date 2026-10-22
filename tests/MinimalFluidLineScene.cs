using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluidPipes;
using UnityEngine;

public sealed partial class SceneCapture
{
    private int lineSource;
    private int lineSink;
    private int lineFactory;
    private int lineWater;
    private int lineOil;
    private int lineAcid;
    private readonly List<int> lineSorters = new List<int>();
    private readonly List<int> lineSolidBelts = new List<int>();
    private readonly HashSet<int> usedSourcePorts = new HashSet<int>();
    private const int Water = 1000;
    private const int Stone = 1005;
    private const int Oil = 1114;
    private const int Acid = 1116;

    private StationComponent LineStation(int entity) =>
        factory.transport.stationPool[factory.entityPool[entity].stationId];

    private int[] LineRoute(int item, IList<Vector3> corners)
    {
        var points = new List<Vector3> { corners[0] };
        for (int corner = 1; corner < corners.Count; corner++)
        {
            var first = points[points.Count - 1];
            var last = corners[corner];
            int steps = Math.Max(1, Mathf.CeilToInt(Vector3.Distance(first, last)));
            for (int index = 1; index <= steps; index++)
                points.Add(Vector3.Lerp(first, last, (float)index / steps));
        }
        var ids = new int[points.Count];
        for (int index2 = 0; index2 < points.Count; index2++)
        {
            var pos = points[index2].normalized * (factory.planet.realRadius + 0.2f);
            var forward = Vector3
                .ProjectOnPlane(
                    points[Math.Min(index2 + 1, points.Count - 1)]
                        - points[Math.Max(0, index2 - 1)],
                    pos.normalized
                )
                .normalized;
            ids[index2] = PlaceFacility(
                item,
                pos,
                Quaternion.LookRotation(forward, pos.normalized)
            );
            if (index2 > 0)
                Connect(ids[index2 - 1], ids[index2], 1);
            if (item == 2001)
                lineSolidBelts.Add(ids[index2]);
        }
        return ids;
    }

    private Vector3 LinePortOutside(int entity, int slot)
    {
        var entity2 = factory.entityPool[entity];
        var pose = LDB.items.Select(entity2.protoId).prefabDesc.portPoses[slot];
        return Port(entity, slot) + entity2.rot * pose.rotation * Vector3.forward * 0.8f;
    }

    private int SourcePort(Vector3 toward)
    {
        var desc = LDB.items.Select(factory.entityPool[lineSource].protoId).prefabDesc;
        int slot = Enumerable
            .Range(0, desc.portPoses.Length)
            .Where(index => !usedSourcePorts.Contains(index))
            .OrderBy(index2 => (Port(lineSource, index2) - toward).sqrMagnitude)
            .First();
        usedSourcePorts.Add(slot);
        return slot;
    }

    private void LineAttach(int tower, int port, int segment, int storage, bool output)
    {
        Attach(tower, port, segment, output);
        LineStation(tower).slots[port].storageIdx = storage + 1;
        PipePools.Dirty(factory);
    }

    private int AddLineSorter(int source, int destination, int item)
    {
        var start = factory.entityPool[source].pos;
        var end = factory.entityPool[destination].pos;
        if (source == lineFactory)
            start = InserterSocket(source, end);
        if (destination == lineFactory)
            end = InserterSocket(destination, start);
        int entity = AddSceneSorter(source, destination, start, end);
        factory.factorySystem.inserterPool[factory.entityPool[entity].inserterId].filter = item;
        lineSorters.Add(entity);
        return entity;
    }

    private void CreateMinimalFluidLine()
    {
        GameMain.sandboxToolsEnabled = true;
        var rotation = Quaternion.LookRotation(north, up);
        lineSource = PlaceFacility(2103, Point(-12, 0), rotation);
        lineSink = PlaceFacility(2103, Point(12, 0), rotation);
        lineFactory = PlaceFacility(2309, Point(0, 0), rotation);
        var recipe = LDB.recipes.dataArray.Single(recipePrototype =>
            recipePrototype.Type == ERecipeType.Chemical
            && recipePrototype.Results.Length == 1
            && recipePrototype.Results[0] == Acid
        );
        GameMain.history.UnlockRecipe(recipe.ID);
        factory
            .factorySystem.assemblerPool[factory.entityPool[lineFactory].assemblerId]
            .SetRecipe(recipe.ID, factory.entitySignPool);
        var supply = LineStation(lineSource);
        var sink = LineStation(lineSink);
        for (int index = 0; index < 3; index++)
            supply.storage[index] = new StationStore
            {
                itemId = new[] { Water, Oil, Stone }[index],
                max = 5000,
                keepMode = 1,
            };
        sink.storage[0] = new StationStore
        {
            itemId = Acid,
            max = 5000,
            keepMode = 3,
        };
        supply.energy = supply.energyMax;
        sink.energy = sink.energyMax;
        supply.energyPerTick = 0;
        sink.energyPerTick = 0;
        supply.UpdateKeepMode();
        sink.UpdateKeepMode();
        int wp = SourcePort(Point(-7, -4));
        int op = SourcePort(Point(-7, 4));
        int sp = SourcePort(Point(-12, -7));
        var water = LineRoute(
            Prototypes.ItemId,
            new[] { LinePortOutside(lineSource, wp), Point(-7, -4), Point(-1, -4), Point(-1, -3) }
        );
        var oil = LineRoute(
            Prototypes.ItemId,
            new[] { LinePortOutside(lineSource, op), Point(-7, 4), Point(-1, 4), Point(-1, 3) }
        );
        var stone = LineRoute(
            2001,
            new[] { LinePortOutside(lineSource, sp), Point(-12, -7), Point(2, -7), Point(2, -3) }
        );
        LineAttach(lineSource, wp, water[0], 0, true);
        LineAttach(lineSource, op, oil[0], 1, true);
        LineAttach(lineSource, sp, stone[0], 2, true);
        lineWater = water[water.Length - 1];
        lineOil = oil[oil.Length - 1];
        AddLineSorter(lineWater, lineFactory, Water);
        AddLineSorter(lineOil, lineFactory, Oil);
        AddLineSorter(stone[stone.Length - 1], lineFactory, Stone);
        int ap = NearestPort(lineSink, Point(8, 5.5f));
        var acid = LineRoute(
            Prototypes.ItemId,
            new[] { Point(2, 3), Point(2, 5.5f), Point(8, 5.5f), LinePortOutside(lineSink, ap) }
        );
        lineAcid = acid[0];
        LineAttach(lineSink, ap, acid[acid.Length - 1], 0, false);
        AddLineSorter(lineFactory, lineAcid, Acid);
        foreach (var position in new[] { V(-9, -3), V(4, -4), V(9, 3), V(0, 7) })
            PlaceFacility(2201, Point(position.x, position.z), rotation);
        foreach (int x in new[] { -10, -6, -2, 2, 6, 10 })
            PlaceFacility(2203, Point(x, 10), rotation);
        PipePools.Dirty(factory);
        PipePools.Tick(factory);
        factory.transport.GameTick_UpdateNeeds();
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        if (
            PipePools.SameSpace(graph, lineWater, lineOil)
            || PipePools.SameSpace(graph, lineWater, lineAcid)
            || PipePools.SameSpace(graph, lineOil, lineAcid)
        )
            throw new InvalidOperationException("Production liquids share a pipe space.");
        foreach (int belt in lineSolidBelts)
            if (PipeRuntime.IsPipeObject(factory, belt))
                throw new InvalidOperationException("Stone route is a pipe.");
        foreach (var item in recipe.Items)
            if (ItemProto.isFluid(item) && item != Water && item != Oil)
                throw new InvalidOperationException("Unexpected liquid input.");
        report.Add(
            "Recipe "
                + recipe.ID
                + ": "
                + string.Join(
                    " + ",
                    recipe.Items.Select(
                        (item, index2) =>
                            recipe.ItemCounts[index2] + " x " + LDB.items.Select(item).name
                    )
                )
                + " -> "
                + recipe.ResultCounts[0]
                + " x "
                + LDB.items.Select(Acid).name
                + "; seconds="
                + (recipe.TimeSpend / 60.0)
        );
        report.Add(
            "Two native planetary logistics stations: water/oil/stone keepMode=1 sources, sulfuric acid keepMode=3 sink. No direct recipe buffer seeding."
        );
        report.Add(
            "Routes: water_nodes="
                + water.Length
                + " oil_nodes="
                + oil.Length
                + " acid_nodes="
                + acid.Length
                + " solid_belt_nodes="
                + stone.Length
                + " native_inserters="
                + lineSorters.Count
        );
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(0, 1, 32, 44, 12, 7);
    }

    private long LineAcidStock()
    {
        long result = PipePools.View(PipeRuntime.Bind(factory.cargoTraffic), lineAcid, true).Count;
        var assembler = factory.factorySystem.assemblerPool[
            factory.entityPool[lineFactory].assemblerId
        ];
        result += assembler.produced[0];
        foreach (int entity in lineSorters)
        {
            var arm = factory.factorySystem.inserterPool[factory.entityPool[entity].inserterId];
            if (arm.itemId == Acid)
                result += arm.itemCount;
        }
        return result;
    }

    private int RunMinimalLine(int ticks)
    {
        var stat = GameMain.statistics.production.factoryStatPool[factory.index];
        int before = stat.productRegister[Acid];
        long initial = LineAcidStock();
        long removed = 0;
        long time = GameMain.gameTick;
        float minimum = 1;
        for (int index2 = 0; index2 < ticks; index2++)
        {
            removed += LineStation(lineSink).storage[0].count;
            factory.transport.GameTick_SandboxMode(-1);
            factory.transport.GameTick_UpdateNeeds();
            factory.powerSystem.GameTick(time + index2, false, false, 0);
            factory.factorySystem.GameTick(time + index2, true);
            factory.transport.GameTick_OutputToBelt(1, time + index2);
            factory.cargoTraffic.CargoPathsGameTickSync();
            factory.factorySystem.GameTickInserters(time + index2, true);
            factory.transport.GameTick_InputFromBelt(time + index2);
            PipePools.Tick(factory);
            int pc = factory.entityPool[lineFactory].powerConId;
            minimum = Mathf.Min(
                minimum,
                factory.powerSystem.networkServes[factory.powerSystem.consumerPool[pc].networkId]
            );
        }
        int produced = stat.productRegister[Acid] - before;
        long retained = LineAcidStock();
        var machine = factory.factorySystem.assemblerPool[
            factory.entityPool[lineFactory].assemblerId
        ];
        report.Add(
            "Production ticks="
                + ticks
                + " acid_produced="
                + produced
                + " tower_disposed="
                + removed
                + " acid_stock_delta="
                + (retained - initial)
                + " minimum_power="
                + minimum
                + " served="
                + string.Join(",", machine.served)
        );
        if (
            produced < 40
            || removed < 32
            || retained - initial + removed != produced
            || minimum < 0.99f
        )
            throw new InvalidOperationException(
                "Minimal line does not sustain powered, conserved production."
            );
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        foreach (
            var pair in new[]
            {
                new[] { lineWater, Water },
                new[] { lineOil, Oil },
                new[] { lineAcid, Acid },
            }
        )
        {
            var view = PipePools.View(graph, pair[0], true);
            if (view.Item != pair[1] || view.Conflict)
                throw new InvalidOperationException(
                    "Production pipe contains an incorrect liquid."
                );
        }
        foreach (int entity in lineSorters)
        {
            var arm = factory.factorySystem.inserterPool[factory.entityPool[entity].inserterId];
            if (arm.filter != Stone && !FluidInserterVisuals.UsesPipe(factory, entity))
                throw new InvalidOperationException("Liquid sorter has no pipe endpoint.");
            if (arm.filter == Stone && FluidInserterVisuals.UsesPipe(factory, entity))
                throw new InvalidOperationException("Solid sorter attaches to a pipe.");
        }
        return produced;
    }

    private void RestoreMinimalFluidLine()
    {
        factory = GameMain.localPlanet.factory;
        GameMain.sandboxToolsEnabled = true;
        var stations = factory.transport.stationPool.Where(s => s != null && s.id > 0).ToArray();
        lineSource = stations.Single(s => s.storage[0].itemId == Water).entityId;
        lineSink = stations.Single(s => s.storage[0].itemId == Acid).entityId;
        lineFactory = factory
            .entityPool.Single(entity => entity.id > 0 && entity.protoId == 2309)
            .id;
        var machine = factory.entityPool[lineFactory];
        center = machine.pos;
        up = machine.rot * Vector3.up;
        east = machine.rot * Vector3.right;
        north = machine.rot * Vector3.forward;
        foreach (var arm in factory.factorySystem.inserterPool)
            if (arm.id > 0)
                lineSorters.Add(arm.entityId);
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        for (int entityId2 = 1; entityId2 < factory.entityCursor; entityId2++)
        {
            if (!PipeRuntime.IsPipeObject(factory, entityId2))
                continue;
            int item = PipePools.View(graph, entityId2, true).Item;
            if (item == Water)
                lineWater = entityId2;
            else if (item == Oil)
                lineOil = entityId2;
            else if (item == Acid)
                lineAcid = entityId2;
        }
        report.Add(
            "Reload: sorters="
                + lineSorters.Count
                + " water="
                + lineWater
                + " oil="
                + lineOil
                + " acid="
                + lineAcid
                + " source_modes="
                + string.Join(
                    ",",
                    LineStation(lineSource).storage.Select(storageSlot => storageSlot.keepMode)
                )
                + " sink_mode="
                + LineStation(lineSink).storage[0].keepMode
                + " sandbox="
                + GameMain.data.gameDesc.isSandboxMode
        );
        if (
            lineSorters.Count != 4
            || lineWater == 0
            || lineOil == 0
            || lineAcid == 0
            || LineStation(lineSource)
                .storage.Take(3)
                .Any(storageSlot2 => storageSlot2.keepMode != 1)
            || LineStation(lineSink).storage[0].keepMode != 3
            || !GameMain.data.gameDesc.isSandboxMode
        )
            throw new InvalidOperationException(
                "Saved production layout or sandbox station settings did not restore."
            );
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(0, 1, 32, 44, 12, 7);
        report.Add(
            "Native sandbox save and companion data reloaded: two logistics towers, one chemical plant, four inserters, three independent liquid spaces, persisted source/sink modes."
        );
    }

    private IEnumerator CaptureMinimalFluidLine()
    {
        RunMinimalLine(7200);
        if (RunMinimalLine(7200) != 80)
            throw new InvalidOperationException(
                "Steady sulfuric acid production differs from 40 per minute."
            );
        for (int index = 0; index < 12; index++)
            yield return null;
        SaveImage("ingame-minimal-line-overview.png");
        SetView(0, 0, 22, 8, 0, 0);
        for (int index2 = 0; index2 < 12; index2++)
            yield return null;
        SaveImage("ingame-minimal-line-layout.png");
        SetView(0, -1, 5.5f, 10, 5, 0.8f);
        for (int index3 = 0; index3 < 12; index3++)
            yield return null;
        SaveImage("ingame-minimal-line-inputs.png");
        SetView(0, 1.5f, 5, 9, -5, 0.8f);
        for (int index4 = 0; index4 < 12; index4++)
            yield return null;
        SaveImage("ingame-minimal-line-output.png");
        SetView(0, 1, 32, 44, 12, 7);
        for (int index5 = 0; index5 < 12; index5++)
            yield return null;
        var player = GameMain.mainPlayer;
        player.position = Point(0, -10);
        player.uPosition =
            factory.planet.uPosition
            + Maths.QRotateLF(factory.planet.runtimeRotation, player.position);
        fixtureSave = true;
        try
        {
            if (!GameSave.SaveCurrentGame("minimal-fluid-line"))
                throw new InvalidOperationException("Sandbox production save failed.");
        }
        finally
        {
            fixtureSave = false;
        }
        foreach (string extension in new[] { ".dsv", ".moddsv" })
        {
            string path = System.IO.Path.Combine(
                GameConfig.gameSaveFolder,
                "minimal-fluid-line" + extension
            );
            if (!File.Exists(path))
                throw new InvalidOperationException("Production save is incomplete: " + extension);
            File.Copy(path, System.IO.Path.Combine(output, "minimal-fluid-line" + extension), true);
        }
        report.Add(
            "Saved native sandbox game and FluidPipes companion data: minimal-fluid-line.dsv + minimal-fluid-line.moddsv."
        );
    }
}
