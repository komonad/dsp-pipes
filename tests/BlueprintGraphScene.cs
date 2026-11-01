using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluidPipes;
using FluidPipes.Integration.Belts;
using UnityEngine;

public sealed partial class SceneCapture
{
    private void CheckBlueprintGraphFixtures()
    {
        var lattice = BuildCubicLattice(-55, -60);
        ExportWonderBlueprint(
            "fluid-lattice",
            "Fluid Cubic Lattice",
            new List<SolidGalleryPiece> { lattice }
        );
        var selected = new HashSet<int>(
            lattice.Nodes.Where(node =>
            {
                var local = InstrumentLocal(lattice, node);
                return local.x < 0.01f && local.y < 6.01f && local.z < 0.01f;
            })
        );
        var section = NewSolidPiece("lattice-section", -55, -60, 1000);
        section.Nodes.AddRange(lattice.Nodes.Where(selected.Contains));
        foreach (int node in section.Nodes)
            section.Degrees.Add(
                node,
                PipeTopology.Neighbors(factory, node).Count(selected.Contains)
            );
        section.Stock = section.Nodes.Count * 8;
        ExportWonderBlueprint(
            "graph-lattice-section",
            "Cubic Lattice Section",
            new List<SolidGalleryPiece> { section }
        );
        var dense = BuildWonderCurve(
            "dense-ring",
            -55,
            -30,
            1000,
            360,
            true,
            t => new Vector3(3 * Mathf.Cos(t * 2 * Mathf.PI), 3, 3 * Mathf.Sin(t * 2 * Mathf.PI))
        );
        ExportWonderBlueprint(
            "graph-dense-ring",
            "Dense Pipe Ring",
            new List<SolidGalleryPiece> { dense }
        );
        report.Add(
            "PASS graph blueprints preserve six-way junctions, cycles, clipped selections"
                + " and 360 distinct vertices spaced below the native belt merge threshold."
        );
    }

    private static int[][] BlueprintAdjacency(BlueprintData blueprint)
    {
        var peers = blueprint.buildings.Select(building => new HashSet<int>()).ToArray();
        foreach (var building in blueprint.buildings)
        {
            var graph = PipeBlueprints.Neighbors(building, peers.Length, out _);
            if (graph != null)
                foreach (int peer in graph)
                {
                    peers[building.index].Add(peer);
                    peers[peer].Add(building.index);
                }
            foreach (var peer in new[] { building.inputObj, building.outputObj })
                if (peer != null)
                {
                    peers[building.index].Add(peer.index);
                    peers[peer.index].Add(building.index);
                }
        }
        return peers.Select(set => set.OrderBy(index => index).ToArray()).ToArray();
    }

    private void AssertBlueprintGraph(BlueprintData blueprint, int[] objects) =>
        AssertBlueprintGraph(BlueprintAdjacency(blueprint), objects);

    private void AssertBlueprintGraph(int[][] expected, int[] objects)
    {
        for (int index = 0; index < objects.Length; index++)
        {
            var peers = new HashSet<int>(expected[index].Select(peer => objects[peer]));
            if (!peers.SetEquals(PipeTopology.Neighbors(factory, objects[index])))
                throw new InvalidOperationException(
                    "Blueprint graph changes an edge during pending or entity migration."
                );
        }
    }

    private Vector3 BlueprintObjectPosition(int id) =>
        id > 0 ? factory.entityPool[id].pos : factory.prebuildPool[-id].pos;

    private void SaveBlueprintGraphStage(BlueprintData blueprint, int[] objects)
    {
        string name = "pipe-blueprint-pending";
        var player = GameMain.mainPlayer;
        player.uPosition =
            factory.planet.uPosition
            + Maths.QRotateLF(factory.planet.runtimeRotation, player.position);
        fixtureSave = true;
        try
        {
            if (!GameSave.SaveCurrentGame(name))
                throw new InvalidOperationException(
                    "Cannot save partially constructed blueprint graph."
                );
        }
        finally
        {
            fixtureSave = false;
        }
        foreach (string extension in new[] { ".dsv", ".moddsv" })
            File.Copy(
                System.IO.Path.Combine(GameConfig.gameSaveFolder, name + extension),
                System.IO.Path.Combine(output, name + extension),
                true
            );
        using (
            var writer = new BinaryWriter(
                File.Create(System.IO.Path.Combine(output, name + ".graph"))
            )
        )
        {
            writer.Write(factory.planetId);
            writer.Write(objects.Length);
            var adjacency = BlueprintAdjacency(blueprint);
            for (int index = 0; index < objects.Length; index++)
            {
                writer.Write(objects[index]);
                Vector3 position = BlueprintObjectPosition(objects[index]);
                writer.Write(position.x);
                writer.Write(position.y);
                writer.Write(position.z);
                writer.Write(adjacency[index].Length);
                foreach (int peer in adjacency[index])
                    writer.Write(peer);
            }
        }
        using (var memory = new MemoryStream())
        {
            using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true))
                PipeRuntime.Export(writer);
            memory.Position = 0;
            PipeRuntime.Clear();
            using (var reader = new BinaryReader(memory, System.Text.Encoding.UTF8, true))
                PipeRuntime.Import(reader);
            AssertBlueprintGraph(blueprint, objects);
        }
        report.Add(
            "PASS partially constructed graph survives mod-save serialization with "
                + objects.Count(id => id < 0)
                + " pending vertices; native save pair exported."
        );
    }

    private void CheckRestoredBlueprintGraph()
    {
        factory = GameMain.localPlanet.factory;
        int[] objects;
        int[][] adjacency;
        using (
            var reader = new BinaryReader(
                File.OpenRead(System.IO.Path.ChangeExtension(inspectSave, ".graph"))
            )
        )
        {
            if (reader.ReadInt32() != factory.planetId)
                throw new InvalidOperationException("Restored graph is on a different planet.");
            int count = reader.ReadInt32();
            objects = new int[count];
            adjacency = new int[count][];
            for (int index = 0; index < count; index++)
            {
                int id = reader.ReadInt32();
                var position = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle()
                );
                if (!PipeRuntime.IsPipeObject(factory, id))
                    id = FindBlueprintEntity(position);
                if (id == 0)
                    throw new InvalidOperationException("Reload loses a blueprint vertex.");
                objects[index] = id;
                adjacency[index] = new int[reader.ReadInt32()];
                for (int peer = 0; peer < adjacency[index].Length; peer++)
                    adjacency[index][peer] = reader.ReadInt32();
            }
        }
        AssertBlueprintGraph(adjacency, objects);
        int pending = objects.Count(id => id < 0);
        foreach (int index in Enumerable.Range(0, objects.Length).Reverse())
        {
            int id = objects[index];
            if (id > 0)
                continue;
            Vector3 position = BlueprintObjectPosition(id);
            factory.BuildFinally(GameMain.mainPlayer, -id, false, false);
            objects[index] = FindBlueprintEntity(position);
            if (objects[index] == 0)
                throw new InvalidOperationException("Reloaded prebuild cannot complete.");
        }
        AssertBlueprintGraph(adjacency, objects);
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        foreach (int id in objects)
            if (graph.View(id).Nodes != objects.Length || graph.View(id).Count != 0)
                throw new InvalidOperationException(
                    "Reloaded blueprint graph has invalid inventory membership."
                );
        int stock = objects.Length * 8;
        if (
            !graph.Insert(objects[0], 1000, stock, 0)
            || graph.View(objects[objects.Length - 1]).Count != stock
        )
            throw new InvalidOperationException("Reloaded blueprint graph cannot share its fluid.");
        var taken = graph.Take(objects[objects.Length - 1], 7, 1000);
        if (
            taken.Count != 7
            || !graph.Insert(
                objects[0],
                taken.Item,
                (int)taken.Count,
                (int)taken.ProliferatorPoints
            )
            || graph.View(objects[0]).Count != stock
        )
            throw new InvalidOperationException(
                "Reloaded blueprint graph does not conserve fluid."
            );
        report.Add(
            "PASS cold native save reload nodes="
                + objects.Length
                + " edges="
                + adjacency.Sum(peers => peers.Length) / 2
                + " pending_completed="
                + pending
                + " maximum_degree="
                + adjacency.Max(peers => peers.Length)
                + " every_edge=true shared_inventory=true remote_io_conservation=true"
        );
    }

    private void CheckBlueprintOverlay(BlueprintData blueprint, int[] objects, Vector3 destination)
    {
        var player = GameMain.mainPlayer;
        var tool = new BuildTool_BlueprintPaste();
        tool._Init(GameMain.data);
        tool.factory = factory;
        tool.planet = factory.planet;
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        var inventory = graph.View(objects[0]);
        PipeVisuals.FlushColliderChanges();
        Physics.SyncTransforms();
        var adjacency = BlueprintAdjacency(blueprint);
        int peer = adjacency[0][0];
        if (!PipeConnections.Disconnect(factory, objects[0], objects[peer]))
            throw new InvalidOperationException("Cannot cut an overlay test edge.");
        try
        {
            tool.blueprint = blueprint;
            tool.dotsSnapped = new[] { factory.planet.aux.Snap(destination, true) };
            tool.dotsCursor = 1;
            tool.cursorValid = true;
            tool.useReforms = false;
            tool.GenerateBlueprintGratBoxes();
            tool.DeterminePreviewsPrestage(true);
            tool.ActiveColliders(player.controller.actionBuild.model);
            bool valid;
            try
            {
                valid = tool.CheckBuildConditions();
            }
            finally
            {
                tool.DeactiveColliders(player.controller.actionBuild.model);
            }
            report.Add(
                "OVERLAY valid="
                    + valid
                    + " covers="
                    + tool.bpPool.Take(tool.bpCursor).Count(p => p.coverObjId > 0)
                    + " conditions="
                    + string.Join(
                        ",",
                        tool.bpPool.Take(tool.bpCursor)
                            .GroupBy(p => p.condition)
                            .Select(g => g.Key + "=" + g.Count())
                    )
            );
            if (!valid || tool.bpPool.Take(tool.bpCursor).Any(preview => preview.coverObjId <= 0))
                throw new InvalidOperationException(
                    "Existing pipe graph cannot receive its blueprint overlay."
                );
            tool.CreatePrebuilds();
            if (
                !tool
                    .bpPool.Take(tool.bpCursor)
                    .Select(preview => preview.objId)
                    .SequenceEqual(objects)
            )
                throw new InvalidOperationException(
                    "Blueprint overlay replaces existing pipe identities."
                );
            AssertBlueprintGraph(adjacency, objects);
            var after = graph.View(objects[0]);
            if (
                after.Count != inventory.Count
                || after.Item != inventory.Item
                || after.ProliferatorPoints != inventory.ProliferatorPoints
            )
                throw new InvalidOperationException(
                    "Blueprint overlay changes existing inventory."
                );
            report.Add(
                "PASS blueprint overlay restores a missing graph edge while retaining every entity and inventory."
            );

            var corrupt = BlueprintData.CreateNew(blueprint.ToBase64String());
            corrupt.buildings[0].parameters[corrupt.buildings[0].parameters.Length - 2] = -1;
            tool.blueprint = corrupt;
            tool.DeterminePreviewsPrestage(true);
            if (tool.CheckBuildConditions())
                throw new InvalidOperationException(
                    "Corrupt graph metadata passes blueprint validation."
                );
            tool.CreatePrebuilds();
            AssertBlueprintGraph(adjacency, objects);
            if (graph.View(objects[0]).Count != inventory.Count)
                throw new InvalidOperationException("Rejected blueprint changes inventory.");
            report.Add("PASS corrupt graph metadata is rejected before blueprint placement.");
        }
        finally
        {
            tool._Free();
        }
    }

    private int FindBlueprintEntity(Vector3 position)
    {
        for (int id = 1; id < factory.entityCursor; id++)
            if (
                PipeRuntime.IsPipeObject(factory, id)
                && (factory.entityPool[id].pos - position).sqrMagnitude < 0.00001f
            )
                return id;
        return 0;
    }
}
