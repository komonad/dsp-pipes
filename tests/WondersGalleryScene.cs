using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluidPipes;
using UnityEngine;

public sealed partial class SceneCapture
{
    private readonly List<SolidGalleryPiece> armillary = new List<SolidGalleryPiece>();
    private readonly List<SolidGalleryPiece> helixTower = new List<SolidGalleryPiece>();
    private SolidGalleryPiece armillaryFrame;
    private SolidGalleryPiece helixFrame;

    private void CreateWondersGallery()
    {
        armillaryFrame = NewSolidPiece("armillary", 0, 0, 0);
        armillaryFrame.Center = armillaryFrame.Origin + armillaryFrame.Up * 21.8f;
        armillaryFrame.Radius = 20;
        int[] fluids = { 1116, 1000, 1121, 1114, 1007 };
        var rotations = new[]
        {
            Quaternion.Euler(8, 0, 0),
            Quaternion.Euler(40, 60, 25),
            Quaternion.Euler(105, 120, 20),
            Quaternion.Euler(55, 30, 75),
            Quaternion.Euler(135, 40, 30),
        };
        for (int ring = 0; ring < rotations.Length; ring++)
        {
            float radius = 18 - ring * 1.8f;
            Quaternion rotation = rotations[ring];
            armillary.Add(
                BuildWonderCurve(
                    "armillary-ring-" + (ring + 1),
                    0,
                    0,
                    fluids[ring],
                    Mathf.CeilToInt(2 * Mathf.PI * radius / 0.85f),
                    true,
                    t =>
                        new Vector3(0, 21.8f, 0)
                        + rotation
                            * new Vector3(
                                radius * Mathf.Cos(t * 2 * Mathf.PI),
                                radius * Mathf.Sin(t * 2 * Mathf.PI),
                                0
                            )
                )
            );
        }
        for (int link = 0; link < 2; link++)
        {
            bool perpendicular = link == 1;
            armillary.Add(
                BuildWonderCurve(
                    "armillary-core-" + (link + 1),
                    0,
                    0,
                    perpendicular ? 1114 : 1120,
                    64,
                    true,
                    t =>
                    {
                        float a = t * 2 * Mathf.PI;
                        float x = (perpendicular ? 2.75f : -2.75f) + 5.5f * Mathf.Cos(a);
                        return new Vector3(
                            x,
                            21.8f + (perpendicular ? 0 : 5.5f * Mathf.Sin(a)),
                            perpendicular ? 5.5f * Mathf.Sin(a) : 0
                        );
                    }
                )
            );
        }
        armillary.Add(
            BuildWonderCurve(
                "armillary-plinth",
                0,
                0,
                1000,
                144,
                true,
                t => new Vector3(
                    19 * Mathf.Cos(t * 2 * Mathf.PI),
                    1,
                    19 * Mathf.Sin(t * 2 * Mathf.PI)
                )
            )
        );

        helixFrame = NewSolidPiece("double-helix", 65, 0, 0);
        helixFrame.Center = helixFrame.Origin + helixFrame.Up * 24;
        helixFrame.Radius = 24;
        for (int strand = 0; strand < 2; strand++)
        {
            float phase = strand * Mathf.PI;
            helixTower.Add(
                BuildWonderCurve(
                    "helix-strand-" + (strand + 1),
                    65,
                    0,
                    strand == 0 ? 1000 : 1121,
                    220,
                    false,
                    t =>
                    {
                        float a = t * 6 * Mathf.PI + phase;
                        return new Vector3(6.4f * Mathf.Cos(a), 3 + 42 * t, 6.4f * Mathf.Sin(a));
                    }
                )
            );
        }
        for (int ring = 0; ring < 3; ring++)
        {
            float height =
                ring == 0 ? 1
                : ring == 1 ? 24
                : 47;
            float radius = ring == 1 ? 8 : 9;
            helixTower.Add(
                BuildWonderCurve(
                    "helix-ring-" + (ring + 1),
                    65,
                    0,
                    1116,
                    96,
                    true,
                    t => new Vector3(
                        radius * Mathf.Cos(t * 2 * Mathf.PI),
                        height,
                        radius * Mathf.Sin(t * 2 * Mathf.PI)
                    )
                )
            );
        }
        foreach (var piece in armillary.Concat(helixTower))
            ValidateSolidInventory(piece);
        PrepareShowcase();
        SetSolidView(armillaryFrame, 20, 16, 72);
    }

    private SolidGalleryPiece BuildWonderCurve(
        string name,
        float x,
        float z,
        int item,
        int segments,
        bool closed,
        Func<float, Vector3> curve
    )
    {
        var piece = NewSolidPiece(name, x, z, item);
        int count = closed ? segments : segments + 1;
        for (int index = 0; index < count; index++)
        {
            int node = AddSolidNode(piece, curve((float)index / segments));
            if (index > 0)
                ConnectSolidNodes(piece, piece.Nodes[index - 1], node);
        }
        if (closed)
            ConnectSolidNodes(piece, piece.Nodes[count - 1], piece.Nodes[0]);
        FillSolid(piece);
        return piece;
    }

    private void ExportWonderBlueprint(
        string fileName,
        string title,
        List<SolidGalleryPiece> pieces
    )
    {
        var nodes = pieces.SelectMany(piece => piece.Nodes).ToArray();
        var blueprint = BlueprintData.CreateNew();
        BlueprintUtils.GenerateBlueprintData(
            blueprint,
            factory.planet,
            factory.planet.aux,
            nodes,
            nodes.Length,
            BlueprintUtils.GetLongitudeRad(up) - Mathf.PI,
            null,
            false
        );
        blueprint.shortDesc = title;
        blueprint.desc =
            "FluidPipes glass pipe sculpture. Independent fluid circuits; inventory is not included.";
        blueprint.author = "FluidPipes";
        blueprint.icon0 = Prototypes.ItemId;
        string directory = System.IO.Path.Combine(output, "blueprints");
        Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, fileName + ".txt");
        if (!blueprint.SaveBlueprintData(path, out var error))
            throw new InvalidOperationException("Cannot save sculpture blueprint.", error);
        var loaded = BlueprintData.CreateFromFile(path);
        if (loaded == null || loaded.buildings.Length != nodes.Length)
            throw new InvalidOperationException("Sculpture blueprint cannot be read back.");
        var indexByNode = nodes
            .Select((node, index) => new { node, index })
            .ToDictionary(value => value.node, value => value.index);
        var expected = new HashSet<long>();
        foreach (int node in nodes)
        foreach (int peer in PipeTopology.Neighbors(factory, node))
            expected.Add(WonderEdge(indexByNode[node], indexByNode[peer]));
        var actual = new HashSet<long>();
        foreach (var building in loaded.buildings)
        {
            if (building.outputObj != null)
                actual.Add(WonderEdge(building.index, building.outputObj.index));
            if (building.inputObj != null)
                actual.Add(WonderEdge(building.index, building.inputObj.index));
        }
        if (!expected.SetEquals(actual))
            throw new InvalidOperationException("Sculpture blueprint loses pipe connections.");
        report.Add(
            "PASS BLUEPRINT "
                + fileName
                + " nodes="
                + nodes.Length
                + " edges="
                + actual.Count
                + " circuits="
                + pieces.Count
                + " native_file_roundtrip=true"
        );
        CheckWonderBlueprintPaste(loaded, nodes, pieces, fileName);
    }

    private static long WonderEdge(int first, int second) =>
        ((long)Math.Min(first, second) << 32) | (uint)Math.Max(first, second);

    private void CheckWonderBlueprintPaste(
        BlueprintData blueprint,
        int[] sourceNodes,
        List<SolidGalleryPiece> pieces,
        string name
    )
    {
        var player = GameMain.mainPlayer;
        var tool = new BuildTool_BlueprintPaste();
        tool._Init(GameMain.data);
        tool.factory = factory;
        tool.planet = factory.planet;
        var previousPosition = player.position;
        float previousReach = player.mecha.buildArea;
        int previousStage = player.controller.cmd.stage;
        int previousLabLevel = GameMain.history.labLevel;
        bool previousVertical = GameMain.history.beltVerticalConstruction;
        var created = new List<int>();
        try
        {
            player.position = Point(0, -90);
            player.mecha.buildArea = 200;
            GameMain.history.labLevel = 15;
            GameMain.history.beltVerticalConstruction = true;
            player.controller.cmd.stage = 1;
            tool.blueprint = blueprint;
            tool.dotsSnapped = new[] { factory.planet.aux.Snap(player.position, true) };
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
            var conditions = tool
                .bpPool.Take(tool.bpCursor)
                .GroupBy(preview => preview.condition)
                .Select(group => group.Key + "=" + group.Count());
            report.Add(
                "BLUEPRINT CONDITIONS "
                    + name
                    + " valid="
                    + valid
                    + " "
                    + string.Join(",", conditions)
            );
            if (!valid || tool.bpCursor != sourceNodes.Length)
                throw new InvalidOperationException("Sculpture blueprint placement is rejected.");
            tool.CreatePrebuilds();
            var pending = tool
                .bpPool.Take(tool.bpCursor)
                .Select(preview => preview.objId)
                .ToArray();
            if (pending.Any(id => id >= 0))
                throw new InvalidOperationException(
                    "Sculpture blueprint does not create every prebuild."
                );
            for (int index = 0; index < pending.Length; index++)
            {
                var position = factory.prebuildPool[-pending[index]].pos;
                factory.BuildFinally(player, -pending[index], false, false);
                int built = 0;
                for (int entity = 1; entity < factory.entityCursor; entity++)
                    if (
                        factory.entityPool[entity].id == entity
                        && factory.entityPool[entity].protoId == Prototypes.ItemId
                        && (factory.entityPool[entity].pos - position).sqrMagnitude < 0.00001f
                    )
                    {
                        built = entity;
                        break;
                    }
                if (built == 0)
                    throw new InvalidOperationException(
                        "Sculpture blueprint prebuild does not complete."
                    );
                created.Add(built);
                tool.NotifyBuilt(pending[index], built);
            }
            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            var builtBySource = sourceNodes
                .Select((source, index) => new { source, index })
                .ToDictionary(value => value.source, value => created[value.index]);
            foreach (int source in sourceNodes)
            {
                var expectedPeers = new HashSet<int>(
                    PipeTopology.Neighbors(factory, source).Select(peer => builtBySource[peer])
                );
                if (!expectedPeers.SetEquals(graph.Links(builtBySource[source])))
                    throw new InvalidOperationException(
                        "Pasted sculpture changes an edge endpoint."
                    );
            }
            int offset = 0;
            foreach (var piece in pieces)
            {
                for (int index = 0; index < piece.Nodes.Count; index++)
                {
                    int built = created[offset + index];
                    var view = graph.View(built);
                    if (
                        view.Nodes != piece.Nodes.Count
                        || view.Count != 0
                        || graph.Links(built).Length != piece.Degrees[piece.Nodes[index]]
                    )
                        throw new InvalidOperationException(
                            "Pasted sculpture has invalid topology or inventory."
                        );
                }
                if (
                    !graph.Insert(created[offset], piece.Item, piece.Stock, 0)
                    || graph.View(created[offset + piece.Nodes.Count - 1]).Count != piece.Stock
                )
                    throw new InvalidOperationException(
                        "Pasted sculpture circuit cannot hold its fluid."
                    );
                offset += piece.Nodes.Count;
            }
            report.Add(
                "PASS BLUEPRINT "
                    + name
                    + " native_preview_conditions=true create_prebuilds=true build_finally=true"
                    + " topology=true empty_inventory=true independent_refill=true"
            );
        }
        finally
        {
            foreach (int entity in created)
                factory.RemoveEntityWithComponents(entity, false);
            tool._Free();
            player.position = previousPosition;
            player.mecha.buildArea = previousReach;
            player.controller.cmd.stage = previousStage;
            GameMain.history.labLevel = previousLabLevel;
            GameMain.history.beltVerticalConstruction = previousVertical;
        }
    }

    private void SetWonderLight(SolidGalleryPiece frame, float elevation)
    {
        var planet = factory.planet;
        GameMain.mainPlayer.position = frame.Origin - frame.North * 30;
        GameMain.mainPlayer.position =
            GameMain.mainPlayer.position.normalized * (planet.realRadius + 0.2f);
        double start = GameMain.gameTick / 60.0;
        float phase = planet.rotationPhase;
        float bestPhase = phase;
        float best = float.MaxValue;
        for (int step = 0; step < 720; step++)
        {
            planet.rotationPhase = phase + step * 0.5f;
            GameMain.data.galaxy.UpdatePoses(start);
            Vector3 sun = (
                Quaternion.Inverse(planet.runtimeRotation)
                * (Vector3)(planet.star.uPosition - planet.uPosition)
            ).normalized;
            float angle = Mathf.Asin(Vector3.Dot(frame.Up, sun)) * Mathf.Rad2Deg;
            float score = Mathf.Abs(angle - elevation);
            if (score < best)
            {
                best = score;
                bestPhase = planet.rotationPhase;
            }
        }
        planet.rotationPhase = bestPhase;
        GameMain.data.galaxy.UpdatePoses(start);
        report.Add("LIGHT solar_elevation=" + elevation + " error_degrees=" + best.ToString("F2"));
    }

    private IEnumerator CaptureWondersGallery()
    {
        foreach (var frame in new[] { armillaryFrame, helixFrame })
        {
            bool tower = frame == helixFrame;
            string name = tower ? "helix" : "armillary";
            SetWonderLight(frame, 40);
            SetSolidView(frame, tower ? -35 : 20, tower ? -1 : 2, tower ? 87 : 79);
            for (int tick = 0; tick < 30; tick++)
                yield return null;
            SaveImage("ingame-wonders-" + name + "-day.png");
            SetSolidView(frame, tower ? -65 : 75, tower ? 5 : 8, tower ? 86 : 76);
            for (int tick = 0; tick < 20; tick++)
                yield return null;
            SaveImage("ingame-wonders-" + name + "-low.png");
            SetSolidView(frame, tower ? -35 : 30, tower ? 26 : 20, tower ? 39 : 35);
            for (int tick = 0; tick < 20; tick++)
                yield return null;
            SaveImage("ingame-wonders-" + name + "-detail.png");
            SetWonderLight(frame, 3);
            SetSolidView(frame, tower ? -35 : 20, tower ? -1 : 2, tower ? 87 : 79);
            for (int tick = 0; tick < 30; tick++)
                yield return null;
            SaveImage("ingame-wonders-" + name + "-dusk.png");
        }
        foreach (var piece in armillary.Concat(helixTower))
            ValidateSolidInventory(piece);
        ExportWonderBlueprint("fluid-armillary", "Fluid Armillary", armillary);
        ExportWonderBlueprint("fluid-double-helix", "Fluid Double Helix", helixTower);
    }
}
