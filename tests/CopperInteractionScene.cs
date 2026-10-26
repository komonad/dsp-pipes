using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FluidPipes;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;

public sealed partial class SceneCapture
{
    private sealed class InteractionPath
    {
        public int[] Entities;
        public Vector3[] Positions;
        public Quaternion[] Rotations;
    }

    private int copperInteractionItem;

    private void PrepareCopperInteractions()
    {
        RequireCopperInteraction(
            PipeTypes.TryGet("example.copperpipes:copper", out var copper),
            "independent CopperPipes consumer is available to the native interaction scene"
        );
        copperInteractionItem = copper.ItemId;
        if (builtInContentDisabled)
        {
            CheckDisabledContentInitialization();
        }
        factory.planet.factoryModel.disableEntitySigns = true;
        RequireCopperInteraction(
            Mathf.Abs(Vector3.Dot(up, Vector3.up)) < 0.95f,
            "interaction fixtures use a nonpolar surface frame"
        );
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(0, 1, 15, 16, 8);
    }

    private IEnumerator CaptureCopperInteractions()
    {
        Player player = GameMain.mainPlayer;
        Vector3 savedPosition = player.position;
        float savedReach = player.mecha.buildArea;
        int savedStage = player.controller.cmd.stage;
        var model = player.controller.actionBuild.model;
        bool wasOpen = model.connRenderer != null;
        model.Open();
        try
        {
            player.position = center;
            player.mecha.buildArea = 1000;
            int[] pipeItems = builtInContentDisabled
                ? new[] { copperInteractionItem }
                : new[] { PipeApi.PipeItemId, copperInteractionItem };
            if (builtInContentDisabled)
            {
                CheckDisabledContentCrafting();
                InteractionPath inventoryPath = BuildInteractionPath(
                    copperInteractionItem,
                    Point(-4, 0),
                    Point(4, 0),
                    model
                );
                try
                {
                    CheckDisabledContentInventory(inventoryPath);
                }
                finally
                {
                    DismantleInteractionEntities(inventoryPath.Entities);
                }
            }
            foreach (float heading in new[] { 0f, 45f, 90f })
            {
                foreach (float rise in new[] { 0f, 2.6666667f })
                {
                    foreach (bool reverse in new[] { false, true })
                    {
                        InteractionPath baseline = null;
                        foreach (int itemId in pipeItems)
                        {
                            Vector3 direction =
                                east * Mathf.Cos(heading * Mathf.Deg2Rad)
                                + north * Mathf.Sin(heading * Mathf.Deg2Rad);
                            Vector3 first = (center - direction * 4).normalized * center.magnitude;
                            Vector3 last =
                                (center + direction * 4).normalized * (center.magnitude + rise);
                            InteractionPath path = BuildInteractionPath(
                                itemId,
                                reverse ? last : first,
                                reverse ? first : last,
                                model
                            );
                            try
                            {
                                CheckInteractionDismantle(path.Entities, model);
                                if (baseline == null)
                                {
                                    baseline = path;
                                }
                                else
                                {
                                    RequireCopperInteraction(
                                        path.Positions.Length == baseline.Positions.Length
                                            && path.Positions.Zip(
                                                    baseline.Positions,
                                                    Vector3.Distance
                                                )
                                                .All(distance => distance < 0.001f)
                                            && path.Rotations.Zip(
                                                    baseline.Rotations,
                                                    Quaternion.Angle
                                                )
                                                .All(angle => angle < 0.1f),
                                        "copper and glass native routes share preview positions and poses at heading "
                                            + heading
                                            + ", rise "
                                            + rise
                                            + ", reverse "
                                            + reverse
                                    );
                                }
                            }
                            finally
                            {
                                DismantleInteractionEntities(path.Entities);
                            }
                        }
                    }
                }
            }

            foreach (int itemId in pipeItems)
            {
                CheckInteractionElevationBaseline(itemId, model);
                foreach (float height in new[] { 0f, 1.3333333f })
                {
                    foreach (bool reverse in new[] { false, true })
                    {
                        Vector3 first = Point(-4, 0, height);
                        Vector3 last = Point(4, 0, height);
                        InteractionPath path = BuildInteractionPath(
                            itemId,
                            reverse ? last : first,
                            reverse ? first : last,
                            model
                        );
                        int machine = PlaceFacility(
                            2309,
                            Point(0, 4.5f, height),
                            Quaternion.LookRotation(north, up)
                        );
                        try
                        {
                            foreach (bool machineFirst in new[] { false, true })
                            {
                                int sorter = BuildInteractionInserter(
                                    path.Entities[path.Entities.Length / 2],
                                    machine,
                                    machineFirst,
                                    model
                                );
                                try
                                {
                                    CheckInteractionInserterSocket(sorter);
                                    if (height == 0 && !reverse)
                                    {
                                        model.Close();
                                        player.position = Point(-14, -10);
                                        SetView(0, 1.5f, 9, 11, 6);
                                        for (int frame = 0; frame < 8; frame++)
                                        {
                                            yield return null;
                                        }
                                        SaveImage(
                                            "ingame-copper-interactions-"
                                                + itemId
                                                + "-"
                                                + (machineFirst ? "input" : "output")
                                                + ".png"
                                        );
                                        model.Open();
                                        player.position = center;
                                    }
                                }
                                finally
                                {
                                    DismantleInteractionEntities(new[] { sorter });
                                }
                            }
                        }
                        finally
                        {
                            DismantleInteractionEntities(path.Entities.Concat(new[] { machine }));
                        }
                    }
                }
            }
            report.Add("PASS Native interaction matrix completed.");
        }
        finally
        {
            player.position = savedPosition;
            player.mecha.buildArea = savedReach;
            player.controller.cmd.stage = savedStage;
            if (!wasOpen)
            {
                model.Close();
            }
        }
    }

    private InteractionPath BuildInteractionPath(
        int itemId,
        Vector3 start,
        Vector3 end,
        BuildModel model
    )
    {
        var tool = new BuildTool_Path();
        tool._Init(GameMain.data);
        tool.factory = factory;
        tool.planet = factory.planet;
        tool.handItem = LDB.items.Select(itemId);
        tool.handPrefabDesc = tool.handItem.prefabDesc;
        try
        {
            PrepareInteractionItems(tool, itemId);
            GameMain.mainPlayer.controller.cmd.stage = 0;
            GameMain.mainPlayer.controller.cmd.target = start;
            tool.cursorTarget = tool.castGroundPosSnapped = start;
            tool.cursorValid = true;
            tool.DeterminePreviews();
            model.EarlyGameTick();
            tool.UpdatePreviewModels(model);
            tool.UpdatePreviewModelConditions(model);
            model.connRenderer.Update();
            CheckInteractionGridSigns(model, new[] { start }, "single-node cursor");
            GameMain.mainPlayer.controller.cmd.stage = 1;
            tool.startObjectId = tool.castObjectId = 0;
            tool.startTarget = start;
            tool.cursorTarget = tool.castGroundPosSnapped = end;
            tool.cursorValid = true;
            tool.maxSlope = 90;
            tool.DeterminePreviews();
            RequireCopperInteraction(
                tool.buildPreviews.Count > 3,
                "native path planning produces a complete route for item " + itemId
            );
            var path = new InteractionPath
            {
                Positions = tool.buildPreviews.Select(preview => preview.lpos).ToArray(),
                Rotations = tool.buildPreviews.Select(preview => preview.lrot).ToArray(),
            };
            for (int index = 0; index < path.Positions.Length; index++)
            {
                Vector3 tangent =
                    path.Positions[Math.Min(index + 1, path.Positions.Length - 1)]
                    - path.Positions[Math.Max(index - 1, 0)];
                Vector3 radial = path.Positions[index].normalized;
                RequireCopperInteraction(
                    Mathf.Abs(
                        Vector3.Dot(path.Rotations[index] * Vector3.forward, tangent.normalized)
                    ) > 0.8f
                        && Vector3.Dot(path.Rotations[index] * Vector3.up, radial) > 0.8f,
                    "native planned rotation follows its path and radial surface for item "
                        + itemId
                        + " node "
                        + index
                );
            }
            model.EarlyGameTick();
            tool.UpdateCollidersForCursor();
            tool.UpdatePreviewModels(model);
            RequireCopperInteraction(
                tool.CheckBuildConditions(),
                "native path build conditions accept item "
                    + itemId
                    + ": "
                    + string.Join(",", tool.buildPreviews.Select(preview => preview.condition))
            );
            tool.UpdatePreviewModelConditions(model);
            tool.UpdateGizmos(model);
            model.connRenderer.Update();
            CheckInteractionGridSigns(model, path.Positions, "cursor");
            tool.CreatePrebuilds();
            int[] pending = tool.buildPreviews.Select(preview => preview.objId).ToArray();
            RequireCopperInteraction(
                pending.All(objectId => objectId < 0),
                "native path creation produces prebuilds for item " + itemId
            );
            model.EarlyGameTick();
            model.connRenderer.Update();
            CheckInteractionGridSigns(model, path.Positions, "pending");
            path.Entities = new int[pending.Length];
            for (int index = 0; index < pending.Length; index++)
            {
                RequireCopperInteraction(
                    Quaternion.Angle(
                        factory.prebuildPool[-pending[index]].rot,
                        path.Rotations[index]
                    ) < 0.1f,
                    "prebuild retains its planned tangent frame for item "
                        + itemId
                        + " node "
                        + index
                );
                factory.BuildFinally(GameMain.mainPlayer, -pending[index], false, false);
                path.Entities[index] = Enumerable
                    .Range(1, factory.entityCursor - 1)
                    .Single(entityId =>
                        factory.entityPool[entityId].id == entityId
                        && factory.entityPool[entityId].protoId == itemId
                        && Vector3.Distance(factory.entityPool[entityId].pos, path.Positions[index])
                            < 0.001f
                    );
                RequireCopperInteraction(
                    Quaternion.Angle(
                        factory.entityPool[path.Entities[index]].rot,
                        path.Rotations[index]
                    ) < 0.1f,
                    "completed pipe retains its native preview orientation for item "
                        + itemId
                        + " node "
                        + index
                );
            }
            RequireCopperInteraction(
                PipeTypes.GetConnectedNodes(factory, path.Entities[0]).Length
                    == path.Entities.Length,
                "native completed route remains connected for item " + itemId
            );
            return path;
        }
        finally
        {
            tool._Free();
        }
    }

    private void CheckInteractionGridSigns(BuildModel model, Vector3[] positions, string phase)
    {
        for (int frame = 0; frame < 12; frame++)
        {
            model.connRenderer.Update();
        }
        var signs = new List<ConnGizmoObj>();
        foreach (string field in new[] { "objs_0", "objs_1" })
        {
            signs.AddRange(
                (List<ConnGizmoObj>)
                    AccessTools.Field(typeof(ConnGizmoRenderer), field).GetValue(model.connRenderer)
            );
        }
        var routeSigns = signs
            .Where(sign => positions.Any(position => Vector3.Distance(position, sign.pos) < 0.7f))
            .ToArray();
        RequireCopperInteraction(
            routeSigns.Length > 0
                && routeSigns.All(sign =>
                    Vector3.Dot(sign.rot * Vector3.up, sign.pos.normalized) > 0.999f
                ),
            "native "
                + phase
                + " grid signs lie on the radial construction plane; matched="
                + routeSigns.Length
        );
    }

    private void CheckInteractionDismantle(int[] entities, BuildModel model)
    {
        var tool = new BuildTool_Dismantle();
        tool._Init(GameMain.data);
        tool.factory = factory;
        tool.planet = factory.planet;
        try
        {
            foreach (
                int entityId in new[]
                {
                    entities[0],
                    entities[entities.Length / 2],
                    entities[entities.Length - 1],
                }
            )
            {
                model.EarlyGameTick();
                tool.castObjectId = entityId;
                tool.castObjectPos = factory.entityPool[entityId].pos;
                tool.DeterminePreviews();
                tool.UpdateCollidersForCursor();
                tool.UpdatePreviewModels(model);
                tool.UpdateGizmos(model);
                var signs =
                    (List<ConnGizmoObj>)
                        AccessTools
                            .Field(typeof(ConnGizmoRenderer), "objs_3")
                            .GetValue(model.connRenderer);
                RequireCopperInteraction(
                    signs.Count == 1
                        && Vector3.Dot(signs[0].rot * Vector3.up, signs[0].pos.normalized) > 0.999f,
                    "native dismantle cursor stays tangent to the planet for entity " + entityId
                );
            }
        }
        finally
        {
            tool._Free();
        }
    }

    private int BuildInteractionInserter(int pipe, int machine, bool machineFirst, BuildModel model)
    {
        var tool = PlanInteractionInserter(pipe, machine, machineFirst);
        try
        {
            RequireCopperInteraction(
                tool.buildPreviews.Count == 1 && tool.posePairs.Any(pair => pair.valid),
                "native inserter snapping finds a machine-pipe pose pair: pipe="
                    + pipe
                    + ", machine="
                    + machine
                    + ", machineFirst="
                    + machineFirst
            );
            BuildPreview preview = tool.buildPreviews[0];
            model.EarlyGameTick();
            tool.UpdateCollidersForCursor();
            tool.UpdatePreviewModels(model);
            RequireCopperInteraction(
                tool.CheckBuildConditions(),
                "native inserter conditions accept the snapped pair: " + preview.condition
            );
            tool.UpdatePreviewModelConditions(model);
            tool.UpdateGizmos(model);
            Vector3 start = preview.lpos;
            Vector3 end = preview.lpos2;
            Quaternion startRotation = preview.lrot;
            Quaternion endRotation = preview.lrot2;
            int[] existing = Enumerable
                .Range(1, factory.entityCursor - 1)
                .Where(entityId => factory.entityPool[entityId].inserterId > 0)
                .ToArray();
            tool.CreatePrebuilds();
            RequireCopperInteraction(
                preview.objId < 0,
                "native inserter creation produces the snapped prebuild"
            );
            factory.BuildFinally(GameMain.mainPlayer, -preview.objId, false, false);
            int entity = Enumerable
                .Range(1, factory.entityCursor - 1)
                .Single(entityId =>
                    factory.entityPool[entityId].inserterId > 0 && !existing.Contains(entityId)
                );
            var built = factory.entityPool[entity];
            var secondPose = factory.factorySystem.inserterPosePool[built.inserterId];
            RequireCopperInteraction(
                Vector3.Distance(built.pos, start) < 0.001f
                    && Vector3.Distance(secondPose.pos2, end) < 0.001f
                    && Quaternion.Angle(built.rot, startRotation) < 0.1f
                    && Quaternion.Angle(secondPose.rot2, endRotation) < 0.1f,
                "completed inserter retains both native snapped endpoint poses"
            );
            return entity;
        }
        finally
        {
            tool._Free();
        }
    }

    private BuildTool_Inserter PlanInteractionInserter(
        int transport,
        int machine,
        bool machineFirst
    )
    {
        var tool = new BuildTool_Inserter();
        tool._Init(GameMain.data);
        tool.factory = factory;
        tool.planet = factory.planet;
        tool.handItem = LDB.items.Select(2013);
        tool.handPrefabDesc = tool.handItem.prefabDesc;
        PrepareInteractionItems(tool, 2013);
        GameMain.mainPlayer.controller.cmd.stage = 1;
        tool.startObjectId = machineFirst ? machine : transport;
        tool.castObjectId = machineFirst ? transport : machine;
        tool.startObjectPos = factory.entityPool[tool.startObjectId].pos;
        tool.castObjectPos = factory.entityPool[tool.castObjectId].pos;
        tool.cursorTarget = tool.castObjectPos;
        tool.cursorValid = true;
        tool.DeterminePreviews();
        return tool;
    }

    private int CountInteractionInserterPairs(int transport, int machine, bool machineFirst)
    {
        var tool = PlanInteractionInserter(transport, machine, machineFirst);
        try
        {
            return tool.posePairs.Count(pair => pair.valid);
        }
        finally
        {
            tool._Free();
        }
    }

    private void CheckInteractionElevationBaseline(int itemId, BuildModel model)
    {
        InteractionPath path = BuildInteractionPath(
            itemId,
            Point(-4, 0, 1.3333333f),
            Point(4, 0, 1.3333333f),
            model
        );
        int machine = PlaceFacility(2309, Point(0, 4.5f), Quaternion.LookRotation(north, up));
        int[] currentEntities = path.Entities;
        try
        {
            int middle = path.Entities.Length / 2;
            int pipeOutputPairs = CountInteractionInserterPairs(
                path.Entities[middle],
                machine,
                false
            );
            int pipeInputPairs = CountInteractionInserterPairs(
                path.Entities[middle],
                machine,
                true
            );
            DismantleInteractionEntities(path.Entities);
            currentEntities = new int[path.Positions.Length];
            for (int index = 0; index < currentEntities.Length; index++)
            {
                currentEntities[index] = PlaceFacility(
                    2001,
                    path.Positions[index],
                    path.Rotations[index]
                );
                if (index > 0)
                {
                    factory.WriteObjectConn(
                        currentEntities[index - 1],
                        0,
                        true,
                        currentEntities[index],
                        1
                    );
                }
            }
            for (int index = 0; index < currentEntities.Length; index++)
            {
                factory.cargoTraffic.AlterBeltConnections(
                    factory.entityPool[currentEntities[index]].beltId,
                    index + 1 < currentEntities.Length
                        ? factory.entityPool[currentEntities[index + 1]].beltId
                        : 0,
                    index > 0 ? factory.entityPool[currentEntities[index - 1]].beltId : 0,
                    0,
                    0
                );
            }
            int beltOutputPairs = CountInteractionInserterPairs(
                currentEntities[middle],
                machine,
                false
            );
            int beltInputPairs = CountInteractionInserterPairs(
                currentEntities[middle],
                machine,
                true
            );
            RequireCopperInteraction(
                (pipeOutputPairs > 0) == (beltOutputPairs > 0)
                    && (pipeInputPairs > 0) == (beltInputPairs > 0),
                "one-level transport to ground-machine native snapping matches the same-position belt baseline for item "
                    + itemId
                    + ": pipeOutput="
                    + pipeOutputPairs
                    + ", beltOutput="
                    + beltOutputPairs
                    + ", pipeInput="
                    + pipeInputPairs
                    + ", beltInput="
                    + beltInputPairs
            );
        }
        finally
        {
            DismantleInteractionEntities(currentEntities.Concat(new[] { machine }));
        }
    }

    private void CheckInteractionInserterSocket(int entityId)
    {
        var entity = factory.entityPool[entityId];
        var inserter = factory.factorySystem.inserterPool[entity.inserterId];
        var pose = factory.factorySystem.inserterPosePool[entity.inserterId];
        bool pipeFirst = PipeApi.IsPipe(factory, inserter.pickTarget);
        int pipe = pipeFirst ? inserter.pickTarget : inserter.insertTarget;
        Vector3 point = pipeFirst ? entity.pos : pose.pos2;
        Vector3 toward = pipeFirst ? pose.pos2 : entity.pos;
        factory.ReadObjectConn(entityId, pipeFirst ? 1 : 0, out _, out _, out int slot);
        FluidInserterMesh.Socket socket = FluidInserterVisuals.ResolveSocket(
            factory,
            pipe,
            slot,
            point,
            toward
        );
        Vector3 origin = factory.entityPool[pipe].pos;
        Vector3 pipeCenter = origin + origin.normalized * PipeGeometryProfile.Default.CenterHeight;
        float distanceToAxis = float.MaxValue;
        float bestAlignment = 0;
        foreach (int neighbor in PipeTypes.GetNeighbors(factory, pipe))
        {
            Vector3 neighborOrigin = factory.entityPool[neighbor].pos;
            Vector3 neighborCenter =
                neighborOrigin
                + neighborOrigin.normalized * PipeGeometryProfile.Default.CenterHeight;
            Vector3 direction = neighborCenter - pipeCenter;
            float fraction = Mathf.Clamp01(
                Vector3.Dot(socket.Center - pipeCenter, direction) / direction.sqrMagnitude
            );
            float distance = Vector3.Distance(socket.Center, pipeCenter + direction * fraction);
            if (distance < distanceToAxis)
            {
                distanceToAxis = distance;
                bestAlignment = Mathf.Abs(
                    Vector3.Dot(socket.Axis.normalized, direction.normalized)
                );
            }
        }
        RequireCopperInteraction(
            socket.Clamp && distanceToAxis < 0.03f && bestAlignment > 0.995f,
            "rendered inserter clamp follows the actual pipe centerline: distance="
                + distanceToAxis
                + ", alignment="
                + bestAlignment
        );
        RequireCopperInteraction(
            Mathf.Abs(Vector3.Dot(socket.Axis, socket.Normal)) < 0.001f
                && Vector3.Distance(
                    socket.Position,
                    socket.Center + socket.Normal * (socket.PipeRadius + 0.14f)
                ) < 0.001f,
            "rendered inserter neck joins its perpendicular pipe clamp"
        );
        FluidInserterVisuals.Draw(factory, GameCamera.main);
        var shapes = (IDictionary)
            AccessTools.Field(typeof(FluidInserterVisuals), "shapes").GetValue(null);
        RequireCopperInteraction(
            shapes.Contains(entityId),
            "completed native inserter enters the fluid presentation cache"
        );
        object shape = shapes[entityId];
        var drawnSocket = (FluidInserterMesh.Socket)
            AccessTools
                .Field(shape.GetType(), pipeFirst ? "Source" : "Destination")
                .GetValue(shape);
        RequireCopperInteraction(
            drawnSocket.Equals(socket)
                && AccessTools.Field(shape.GetType(), "Mesh").GetValue(shape) is Mesh,
            "live inserter geometry uses the verified native endpoint clamp pose"
        );
    }

    private static void PrepareInteractionItems(BuildTool tool, int itemId)
    {
        Player player = GameMain.mainPlayer;
        player.TryAddItemToPackage(itemId, 200, 0, false, 0);
        AccessTools
            .Field(typeof(BuildTool), "tmpPackage")
            .SetValue(tool, new StorageComponent(player.package.size));
        AccessTools.Field(typeof(BuildTool), "tmpInhandId").SetValue(tool, itemId);
        AccessTools.Field(typeof(BuildTool), "tmpInhandCount").SetValue(tool, 200);
    }

    private void DismantleInteractionEntities(IEnumerable<int> entities)
    {
        foreach (int entityId in entities)
        {
            if (factory.entityPool[entityId].id == entityId)
            {
                int returnedItem = 0;
                factory.DismantleFinally(GameMain.mainPlayer, entityId, ref returnedItem);
            }
        }
    }

    private void RequireCopperInteraction(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
        report.Add("PASS " + description);
    }
}
