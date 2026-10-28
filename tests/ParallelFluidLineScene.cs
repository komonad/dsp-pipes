using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluidPipes;
using UnityEngine;

public sealed partial class SceneCapture
{
    private const int Graphite = 1109;
    private const int Plastic = 1115;
    private readonly List<int> busFactories = new List<int>();
    private readonly List<int> busArms = new List<int>();
    private int busOil;
    private int busDemoPipe;
    private int busDemoBelt;
    private int busDemoArm;

    private int NearestSegment(IEnumerable<int> route, Vector3 point) =>
        route.OrderBy(entityId => (factory.entityPool[entityId].pos - point).sqrMagnitude).First();

    private void BusArm(int machine, int segment, int item, bool output)
    {
        var point = factory.entityPool[segment].pos;
        var socket = InserterSocket(machine, point);
        int entity = output
            ? AddSceneSorter(machine, segment, socket, point)
            : AddSceneSorter(segment, machine, point, socket);
        factory.factorySystem.inserterPool[factory.entityPool[entity].inserterId].filter = item;
        busArms.Add(entity);
    }

    private void CreateParallelFluidLine()
    {
        GameMain.sandboxToolsEnabled = true;
        var rotation = Quaternion.LookRotation(north, up);
        lineSource = PlaceFacility(2103, Point(-20, 0), rotation);
        lineSink = PlaceFacility(2103, Point(20, 0), rotation);
        var supply = LineStation(lineSource);
        var sink = LineStation(lineSink);
        supply.storage[0] = new StationStore
        {
            itemId = Oil,
            max = 5000,
            keepMode = 1,
        };
        supply.storage[1] = new StationStore
        {
            itemId = Graphite,
            max = 5000,
            keepMode = 1,
        };
        sink.storage[0] = new StationStore
        {
            itemId = Plastic,
            max = 5000,
            keepMode = 3,
        };
        supply.energy = supply.energyMax;
        sink.energy = sink.energyMax;
        supply.energyPerTick = sink.energyPerTick = 0;
        supply.UpdateKeepMode();
        sink.UpdateKeepMode();
        var oil = LineRoute(
            Prototypes.ItemId,
            new[] { Point(-14, -4), Point(14, -4), Point(14, 3), Point(-14, 3), Point(-14, -3) }
        );
        Connect(oil[oil.Length - 1], oil[0], 1);
        busOil = oil[0];
        int op = SourcePort(Point(-20, -7));
        int gp = SourcePort(Point(-15, -3));
        var feed = LineRoute(
            Prototypes.ItemId,
            new[]
            {
                LinePortOutside(lineSource, op),
                Point(-20, -7),
                Point(-14, -7),
                Point(-14, -5),
            }
        );
        Connect(feed[feed.Length - 1], busOil, 2);
        LineAttach(lineSource, op, feed[0], 0, true);
        var graphite = LineRoute(
            2001,
            new[] { LinePortOutside(lineSource, gp), Point(-15, -3), Point(-13, -3), Point(13, -3) }
        );
        LineAttach(lineSource, gp, graphite[0], 1, true);
        int pp = NearestPort(lineSink, Point(15, 4));
        var plastic = LineRoute(
            2001,
            new[] { Point(-13, 4), Point(15, 4), LinePortOutside(lineSink, pp) }
        );
        LineAttach(lineSink, pp, plastic[plastic.Length - 1], 0, false);
        var recipe = LDB.recipes.dataArray.Single(recipePrototype =>
            recipePrototype.Type == ERecipeType.Chemical
            && recipePrototype.Results.Length == 1
            && recipePrototype.Results[0] == Plastic
        );
        GameMain.history.UnlockRecipe(recipe.ID);
        foreach (int x in new[] { -7, 0, 7 })
        {
            int machine = PlaceFacility(2309, Point(x, 0), rotation);
            busFactories.Add(machine);
            factory
                .factorySystem.assemblerPool[factory.entityPool[machine].assemblerId]
                .SetRecipe(recipe.ID, factory.entitySignPool);
            BusArm(machine, NearestSegment(oil, Point(x - 1, -4)), Oil, false);
            BusArm(machine, NearestSegment(graphite, Point(x + 2, -3)), Graphite, false);
            BusArm(machine, NearestSegment(plastic, Point(x + 2, 4)), Plastic, true);
        }
        foreach (int x in new[] { -18, -10, -2, 6, 14, 20 })
            PlaceFacility(2201, Point(x, 6), rotation);
        foreach (int x in new[] { -10, -2, 6, 14 })
            PlaceFacility(2201, Point(x, -7), rotation);
        for (int x = -22; x <= 22; x += 4)
            PlaceFacility(2203, Point(x, 10), rotation);
        var empty = LineRoute(
            Prototypes.ItemId,
            new[] { Point(-5, -11), Point(-3, -11), Point(-1, -11) }
        );
        busDemoPipe = empty[2];
        var belt = LineRoute(2001, new[] { Point(-5, -14), Point(-3, -14), Point(-1, -14) });
        busDemoBelt = belt[2];
        busDemoArm = AddSceneSorter(
            busDemoPipe,
            busDemoBelt,
            factory.entityPool[busDemoPipe].pos,
            factory.entityPool[busDemoBelt].pos
        );
        factory.factorySystem.inserterPool[factory.entityPool[busDemoArm].inserterId].filter =
            Water;
        PipePools.Dirty(factory);
        PipePools.Tick(factory);
        factory.transport.GameTick_UpdateNeeds();
        var pool = PipePools.View(PipeRuntime.Bind(factory.cargoTraffic), busOil, true);
        if (pool.Item != Oil || pool.Nodes != oil.Length + feed.Length || pool.Containers != 1)
            throw new InvalidOperationException(
                "Parallel oil lanes do not share one tower-backed liquid space."
            );
        report.Add(
            "Parallel factory row: three chemical plants; south inner graphite belt at z=-3, outer oil pipe at z=-4; north inner oil pipe at z=3, outer plastic belt at z=4. Both oil lanes share one undirected loop."
        );
        report.Add(
            "Sandbox source: refined oil and energetic graphite; sandbox sink: plastic. Three pipe inserters cross the graphite belt; three native output inserters cross the oil pipe."
        );
        report.Add(
            "Recipe "
                + recipe.ID
                + ": "
                + string.Join(
                    " + ",
                    recipe.Items.Select(
                        (item, index) =>
                            recipe.ItemCounts[index] + " x " + LDB.items.Select(item).name
                    )
                )
                + " -> "
                + recipe.ResultCounts[0]
                + " x "
                + LDB.items.Select(Plastic).name
                + "; seconds="
                + recipe.TimeSpend / 60.0
        );
        report.Add(
            "Independent empty pipe-to-belt display has a native grade-colored belt pickup housing; production liquids remain entirely inside pipes."
        );
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(0, 0, 37, 52, 16, 6);
    }

    private long BusStock()
    {
        long result = LineStation(lineSink).storage[0].count;
        foreach (int entity in busFactories)
            result += factory
                .factorySystem
                .assemblerPool[factory.entityPool[entity].assemblerId]
                .produced[0];
        foreach (int entity in busArms)
        {
            var arm = factory.factorySystem.inserterPool[factory.entityPool[entity].inserterId];
            if (arm.itemId == Plastic)
                result += arm.itemCount;
        }
        foreach (var cargo in factory.cargoTraffic.container.cargoPool)
            if (cargo.item == Plastic)
                result += cargo.stack;
        return result;
    }

    private int RunParallelFluidLine(int ticks)
    {
        var stats = GameMain.statistics.production.factoryStatPool[factory.index];
        int before = stats.productRegister[Plastic];
        long initial = BusStock();
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
            foreach (int entity in busFactories)
            {
                int pc = factory.entityPool[entity].powerConId;
                minimum = Mathf.Min(
                    minimum,
                    factory.powerSystem.networkServes[
                        factory.powerSystem.consumerPool[pc].networkId
                    ]
                );
            }
        }
        int produced = stats.productRegister[Plastic] - before;
        long retained = BusStock();
        report.Add(
            "Parallel production ticks="
                + ticks
                + " plastic_produced="
                + produced
                + " tower_disposed="
                + removed
                + " stock_delta="
                + (retained - initial)
                + " minimum_power="
                + minimum
        );
        if (
            produced < 60
            || removed < 50
            || retained - initial + removed != produced
            || minimum < 0.99f
        )
            throw new InvalidOperationException(
                "Parallel factory row fails sustained production or inventory conservation."
            );
        foreach (var cargo in factory.cargoTraffic.container.cargoPool)
            if (ItemProto.isFluid(cargo.item))
                throw new InvalidOperationException("Production liquid enters a belt.");
        if (!FluidInserterVisuals.UsesPipe(factory, busDemoArm))
            throw new InvalidOperationException(
                "Pipe-to-belt display retains a native moving arm."
            );
        var demo = FluidInserterVisuals.ResolveSocket(
            factory,
            busDemoBelt,
            -1,
            factory.entityPool[busDemoBelt].pos,
            factory.entityPool[busDemoPipe].pos
        );
        if (!demo.Belt || demo.Machine || demo.Clamp)
            throw new InvalidOperationException(
                "Belt endpoint does not use its dedicated native housing."
            );
        return produced;
    }

    private void RestoreParallelFluidLine()
    {
        factory = GameMain.localPlanet.factory;
        GameMain.sandboxToolsEnabled = true;
        var stations = factory.transport.stationPool.Where(s => s != null && s.id > 0).ToArray();
        lineSource = stations.Single(s => s.storage[0].itemId == Oil).entityId;
        lineSink = stations.Single(s => s.storage[0].itemId == Plastic).entityId;
        foreach (var entity in factory.entityPool)
            if (entity.id > 0 && entity.protoId == 2309)
                busFactories.Add(entity.id);
        var origin = factory.entityPool[
            busFactories.OrderBy(entityId2 => Math.Abs(factory.entityPool[entityId2].pos.x)).First()
        ];
        up = origin.rot * Vector3.up;
        east = origin.rot * Vector3.right;
        north = origin.rot * Vector3.forward;
        center =
            busFactories
                .Select(entityId3 => factory.entityPool[entityId3].pos)
                .Aggregate(
                    Vector3.zero,
                    (startPosition, endPosition) => startPosition + endPosition
                )
                .normalized * (factory.planet.realRadius + 0.2f);
        foreach (var arm in factory.factorySystem.inserterPool)
            if (arm.id > 0)
            {
                if (arm.filter == Water)
                {
                    busDemoArm = arm.entityId;
                    busDemoPipe = arm.pickTarget;
                    busDemoBelt = arm.insertTarget;
                }
                else
                    busArms.Add(arm.entityId);
            }
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        busOil = busArms
            .Select(entityId4 =>
                factory.factorySystem.inserterPool[factory.entityPool[entityId4].inserterId]
            )
            .First(a => a.filter == Oil)
            .pickTarget;
        var pool = PipePools.View(graph, busOil, true);
        if (
            busFactories.Count != 3
            || busArms.Count != 9
            || pool.Item != Oil
            || pool.Containers != 1
            || LineStation(lineSource).storage.Take(2).Any(storageSlot => storageSlot.keepMode != 1)
            || LineStation(lineSink).storage[0].keepMode != 3
        )
            throw new InvalidOperationException(
                "Parallel layout or sandbox modes fail to restore."
            );
        report.Add(
            "Parallel sandbox save reloaded: three chemical plants, nine production inserters, an oil pipe loop, two logistics towers and an independent empty pipe-to-belt display."
        );
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(0, 0, 37, 52, 16, 6);
    }

    private void CheckLiquidSeams()
    {
        int tested = 0;
        int stale = 0;
        var meshes = new Dictionary<int, Mesh>();
        try
        {
            foreach (var entity in factory.entityPool)
            {
                if (entity.id <= 0 || !PipeRuntime.IsPipeObject(factory, entity.id))
                    continue;
                var peers = PipeTopology.Neighbors(factory, entity.id);
                if (peers.Length != 2 || peers.Any(entityId => entityId <= 0))
                    continue;
                var startPosition = (factory.entityPool[peers[0]].pos - entity.pos).normalized;
                var endPosition = (factory.entityPool[peers[1]].pos - entity.pos).normalized;
                if (Vector3.Dot(startPosition, endPosition) > -0.995f)
                    continue;
                var builder = new PipeMesh(PipeMesh.Radius * 0.76f, false);
                PipeVisuals.AddEntity(builder, factory, entity.id, center);
                meshes.Add(entity.id, builder.Build("FluidPipes.LiquidSeamCheck"));
                var cached = (PipeMesh)
                    HarmonyLib
                        .AccessTools.Method(typeof(PipeVisuals), "NodeMesh")
                        .Invoke(null, new object[] { factory, entity.id, true, 0 });
                if (cached.VertexCount != builder.VertexCount)
                    stale++;
            }
            foreach (var entry in meshes)
            foreach (int other in PipeTopology.Neighbors(factory, entry.Key))
            {
                if (other <= entry.Key || !meshes.ContainsKey(other))
                    continue;
                var position = factory.entityPool[entry.Key].pos;
                var q = factory.entityPool[other].pos;
                var axis = (q - position).normalized;
                var seam =
                    (
                        position
                        + position.normalized * (PipeMesh.Radius + 0.06f)
                        + q
                        + q.normalized * (PipeMesh.Radius + 0.06f)
                    ) * 0.5f
                    - center;
                var first = entry.Value.vertices;
                var second = meshes[other].vertices;
                var left = first
                    .Where(position2 => Math.Abs(Vector3.Dot(position2 - seam, axis)) < 0.0001f)
                    .ToArray();
                var right = second
                    .Where(position3 => Math.Abs(Vector3.Dot(position3 - seam, axis)) < 0.0001f)
                    .ToArray();
                if (
                    left.Length != 32
                    || right.Length != 32
                    || left.Any(position4 => right.All(w => (position4 - w).sqrMagnitude > 1e-8f))
                )
                    throw new InvalidOperationException(
                        "Adjacent liquid rings do not share a boundary: "
                            + entry.Key
                            + "/"
                            + other
                            + " vertices="
                            + left.Length
                            + "/"
                            + right.Length
                    );
                tested++;
            }
            if (stale != 0)
                throw new InvalidOperationException(
                    "Cached liquid geometry differs from the current topology."
                );
            if (tested < 30)
                throw new InvalidOperationException(
                    "Liquid seam fixture contains too few straight boundaries."
                );
            report.Add(
                "Continuous liquid geometry: "
                    + tested
                    + " adjacent straight boundaries share all 32 ring vertices; no internal endcaps."
            );
        }
        finally
        {
            foreach (var mesh in meshes.Values)
                UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private void CheckPipeBeltTransfer()
    {
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        var system = factory.factorySystem;
        if (!PipePools.Insert(graph, busDemoPipe, Water, 8, 0))
            throw new InvalidOperationException("Isolated pipe/belt fixture cannot accept water.");
        int arm = factory.entityPool[busDemoArm].inserterId;
        for (int tick = 0; tick < 900; tick++)
        {
            system
                .inserterPool[arm]
                .InternalUpdate(factory, factory.entityNeeds, factory.entityAnimPool, 1);
            factory.cargoTraffic.CargoPathsGameTickSync();
        }
        long onBelt = factory
            .cargoTraffic.container.cargoPool.Where(c => c.item == Water)
            .Sum(c => (int)c.stack);
        long inPipe = PipePools.View(graph, busDemoPipe, true).Count;
        int held = system.inserterPool[arm].itemCount;
        report.Add("Pipe-to-belt fixture: pipe=" + inPipe + " belt=" + onBelt + " held=" + held);
        if (onBelt <= 0 || onBelt + inPipe + held != 8)
            throw new InvalidOperationException("Native pipe-to-belt inserter loses water.");
        int terminal = busDemoBelt;
        while (true)
        {
            factory.ReadObjectConn(terminal, 0, out _, out int next, out _);
            if (next <= 0 || factory.entityPool[next].beltId == 0)
                break;
            terminal = next;
        }
        int reverse = AddSceneSorter(
            terminal,
            busDemoPipe,
            factory.entityPool[terminal].pos,
            factory.entityPool[busDemoPipe].pos
        );
        int reverseArm = factory.entityPool[reverse].inserterId;
        system.inserterPool[reverseArm].filter = Water;
        for (int tick = 0; tick < 900; tick++)
        {
            if (system.inserterPool[arm].itemCount > 0)
                system
                    .inserterPool[arm]
                    .InternalUpdate(factory, factory.entityNeeds, factory.entityAnimPool, 1);
            system
                .inserterPool[reverseArm]
                .InternalUpdate(factory, factory.entityNeeds, factory.entityAnimPool, 1);
            factory.cargoTraffic.CargoPathsGameTickSync();
        }
        if (
            PipePools.View(graph, busDemoPipe, true).Count != 8
            || factory.cargoTraffic.container.cargoPool.Any(c => c.item == Water)
            || system.inserterPool[reverseArm].itemCount != 0
        )
            throw new InvalidOperationException("Native belt-to-pipe inserter loses water.");
        report.Add(
            "Isolated native pipe-to-belt and belt-to-pipe inserters conserve an eight-water inventory through blocked output and return transfer; saved production layout retains an empty independent display."
        );
    }

    private IEnumerator CaptureParallelFluidLine()
    {
        if (!GameMain.history.TechUnlocked(Prototypes.UnlockTechId))
            GameMain.history.UnlockTech(Prototypes.UnlockTechId);
        if (
            !GameMain.history.RecipeUnlocked(Prototypes.RecipeId)
            || !GameMain.history.RecipeUnlocked(Prototypes.ValveRecipeId)
        )
            throw new InvalidOperationException(
                "Tank technology does not unlock pipe and valve crafting."
            );
        CheckLiquidSeams();
        RunParallelFluidLine(7200);
        if (RunParallelFluidLine(7200) != 120)
            throw new InvalidOperationException(
                "Three chemical plants do not reach 60 plastic per minute."
            );
        for (int index2 = 0; index2 < 12; index2++)
            yield return null;
        SaveImage("ingame-parallel-line-overview.png");
        SetView(0, 0, 28, 5, 0, 0);
        for (int index3 = 0; index3 < 12; index3++)
            yield return null;
        SaveImage("ingame-parallel-line-layout.png");
        SetView(0, -2, 5, 10, 5, 0.6f);
        for (int index4 = 0; index4 < 12; index4++)
            yield return null;
        SaveImage("ingame-parallel-line-pipe-over-belt.png");
        SetView(0, 2, 4, -9, -5, 0.6f);
        for (int index5 = 0; index5 < 12; index5++)
            yield return null;
        SaveImage("ingame-parallel-line-belt-over-pipe.png");
        SetView(-3, -12.5f, 2.7f, 5.5f, 4, 0.5f);
        for (int index6 = 0; index6 < 12; index6++)
            yield return null;
        SaveImage("ingame-parallel-line-pipe-to-belt.png");
        SetView(0, -4, 1.1f, 3, 6, 0.34f);
        for (int index7 = 0; index7 < 12; index7++)
            yield return null;
        SaveImage("ingame-parallel-line-liquid-seams.png");
        var menu = UIRoot.instance.uiGame.buildMenu;
        var player = GameMain.mainPlayer;
        UIRoot.instance.uiGame.researchResultTip._Close();
        foreach (int entityId in new[] { Prototypes.ItemId, Prototypes.ValveId })
        {
            int available = player.package.GetItemCount(entityId);
            if (available < 20)
                player.TryAddItemToPackage(entityId, 20 - available, 0, false, 0);
            int index = LDB.items.Select(entityId).BuildIndex;
            menu.SetCurrentCategory(index / 100);
            menu.OnChildButtonClick(index % 100);
            for (int frame = 0; frame < 2; frame++)
                yield return null;
            if (player.inhandItemId != entityId || player.controller.cmd.type != ECommand.Build)
                throw new InvalidOperationException(
                    "Construction-bar button does not select its native building tool: " + entityId
                );
            player.SetHandItems(0, 0);
            player.controller.cmd.SetNoneCommand();
            player.controller.actionBuild.CloseWithoutResetAxes();
        }
        report.Add(
            "Native construction-bar buttons select pipe and valve construction tools after tank technology unlock."
        );
        menu.SetCurrentCategory(LDB.items.Select(Prototypes.ItemId).BuildIndex / 100);
        for (int index8 = 0; index8 < 24; index8++)
            yield return null;
        if (GameMain.errored)
            throw new InvalidOperationException(
                "Opening the construction bar raises a native runtime error."
            );
        SaveImage("ingame-parallel-line-buildbar.png", true);
        menu.SetCurrentCategory(0);
        SetView(0, 0, 37, 52, 16, 6);
        for (int index9 = 0; index9 < 12; index9++)
            yield return null;
        player.position = Point(0, -9);
        player.uPosition =
            factory.planet.uPosition
            + Maths.QRotateLF(factory.planet.runtimeRotation, player.position);
        fixtureSave = true;
        try
        {
            if (!GameSave.SaveCurrentGame("parallel-fluid-line"))
                throw new InvalidOperationException("Parallel sandbox save failed.");
        }
        finally
        {
            fixtureSave = false;
        }
        foreach (string extension in new[] { ".dsv", ".moddsv" })
            File.Copy(
                System.IO.Path.Combine(
                    GameConfig.gameSaveFolder,
                    "parallel-fluid-line" + extension
                ),
                System.IO.Path.Combine(output, "parallel-fluid-line" + extension),
                true
            );
        report.Add(
            "Saved native sandbox and companion data: parallel-fluid-line.dsv + parallel-fluid-line.moddsv."
        );
        CheckPipeBeltTransfer();
    }
}
