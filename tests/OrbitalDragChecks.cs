using System;
using System.Collections.Generic;
using System.Linq;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;

public sealed partial class OrbitalRingChecks
{
    private void CheckOrbitalDraggedConstruction()
    {
        int[] itemIds = { 6515, 6517, 6516 };
        for (int itemIndex = 0; itemIndex < itemIds.Length; itemIndex++)
        {
            CheckOrbitalDrag(itemIds[itemIndex], 126 + itemIndex * 18, false);
            CheckOrbitalDrag(itemIds[itemIndex], 132 + itemIndex * 18, true);
        }
    }

    private void CheckOrbitalDrag(int itemId, float longitude, bool reverse)
    {
        Player player = GameMain.mainPlayer;
        Vector3 originalPosition = player.position;
        int originalStage = player.controller.cmd.stage;
        float originalReach = player.mecha.buildArea;
        var originalEntities = new HashSet<int>(
            Enumerable
                .Range(1, factory.entityCursor - 1)
                .Where(entityId => factory.entityPool[entityId].id == entityId)
        );
        var originalPrebuilds = new HashSet<int>(
            Enumerable
                .Range(1, factory.prebuildCursor - 1)
                .Where(prebuildId => factory.prebuildPool[prebuildId].id == prebuildId)
        );
        var tool = new BuildTool_Path();
        tool._Init(GameMain.data);
        tool.factory = factory;
        tool.planet = factory.planet;
        tool.handItem = LDB.items.Select(itemId);
        tool.handPrefabDesc = tool.handItem.prefabDesc;
        tool.altitude = itemId == 6515 ? 48 : 43;
        try
        {
            double angleUnit = (double)
                AccessTools.Field(orbitalLayout.GetType(), "AngleUnit").GetValue(orbitalLayout);
            float firstLongitude = (float)(Math.Round(longitude / angleUnit) * angleUnit);
            float lastLongitude = firstLongitude + (float)angleUnit * 8;
            Vector3 first = OrbitalDragPosition(itemId, firstLongitude);
            Vector3 last = OrbitalDragPosition(itemId, lastLongitude);
            Vector3 start = reverse ? last : first;
            Vector3 end = reverse ? first : last;
            player.position = start;
            player.mecha.buildArea = 1000;
            player.TryAddItemToPackage(itemId, 200, 0, false, 0);
            player.controller.cmd.stage = 1;
            tool.startObjectId = tool.castObjectId = 0;
            tool.startTarget = start;
            tool.cursorTarget = tool.castGroundPosSnapped = end;
            tool.cursorValid = true;
            tool.DeterminePreviews();
            Require(tool.buildPreviews.Count > 2, "orbital drag preview count " + itemId);
            CheckOrbitalDragGeometry(itemId, tool, reverse);
            CheckOrbitalDragArrowPolicy(itemId, tool);

            AccessTools
                .Field(typeof(BuildTool), "tmpPackage")
                .SetValue(tool, new StorageComponent(player.package.size));
            AccessTools.Field(typeof(BuildTool), "tmpInhandId").SetValue(tool, itemId);
            AccessTools.Field(typeof(BuildTool), "tmpInhandCount").SetValue(tool, 200);
            Require(
                tool.CheckBuildConditions(),
                "orbital drag build conditions "
                    + itemId
                    + ": "
                    + string.Join(",", tool.buildPreviews.Select(preview => preview.condition))
            );

            Vector3[] positions = tool.buildPreviews.Select(preview => preview.lpos).ToArray();
            Quaternion[] rotations = tool.buildPreviews.Select(preview => preview.lrot).ToArray();
            tool.CreatePrebuilds();
            int[] pending = tool.buildPreviews.Select(preview => preview.objId).ToArray();
            Require(
                pending.All(objectId => objectId < 0),
                "orbital drag creates prebuilds " + itemId
            );
            RequireAdjacentConnections(pending);
            for (int index = 0; index < pending.Length; index++)
            {
                Quaternion pendingRotation = factory.prebuildPool[-pending[index]].rot;
                CheckOrbitalRotation(itemId, "prebuild", index, pendingRotation, rotations[index]);
            }
            bool[] directions = Enumerable
                .Range(1, pending.Length - 1)
                .Select(index => OrbitalEdgeOutput(pending[index - 1], pending[index]))
                .ToArray();
            if (itemId == 6516)
            {
                Require(
                    directions.All(output => output == directions[0]),
                    "particle prebuild direction continuity"
                );
            }

            int[] currentObjects = (int[])pending.Clone();
            IEnumerable<int> completionOrder = Enumerable.Range(0, pending.Length);
            if (reverse)
            {
                completionOrder = completionOrder.Reverse();
            }
            foreach (int index in completionOrder)
            {
                factory.BuildFinally(player, -pending[index], false, false);
                currentObjects[index] = Enumerable
                    .Range(1, factory.entityCursor - 1)
                    .Single(entityId =>
                        factory.entityPool[entityId].id == entityId
                        && factory.entityPool[entityId].protoId == itemId
                        && Vector3.Distance(factory.entityPool[entityId].pos, positions[index])
                            < 0.001f
                    );
                RequireAdjacentConnections(currentObjects);
                CheckOrbitalRotation(
                    itemId,
                    "completed",
                    index,
                    factory.entityPool[currentObjects[index]].rot,
                    rotations[index]
                );
            }

            CheckCompletedOrbitalGeometry(itemId, currentObjects);

            for (int index = 0; index < currentObjects.Length; index++)
            {
                int entityId = currentObjects[index];
                var expectedNeighbors = new List<int>(2);
                if (index > 0)
                {
                    expectedNeighbors.Add(currentObjects[index - 1]);
                }
                if (index + 1 < currentObjects.Length)
                {
                    expectedNeighbors.Add(currentObjects[index + 1]);
                }
                Require(
                    new HashSet<int>(PipeTypes.GetNeighbors(factory, entityId)).SetEquals(
                        expectedNeighbors
                    ),
                    "orbital drag adjacency matches preview sequence " + itemId
                );
                Require(
                    PipeTypes.GetConnectedNodes(factory, entityId).Length == currentObjects.Length,
                    "orbital drag completed component " + itemId
                );
                if (itemId != 6516)
                {
                    Require(
                        new HashSet<int>(
                            PipeTypes.GetOutgoingNeighbors(factory, entityId)
                        ).SetEquals(expectedNeighbors)
                            && new HashSet<int>(
                                PipeTypes.GetIncomingNeighbors(factory, entityId)
                            ).SetEquals(expectedNeighbors),
                        "orbital undirected adjacency is symmetric " + itemId
                    );
                }
            }
            if (itemId == 6516)
            {
                for (int index = 1; index < currentObjects.Length; index++)
                {
                    int source = directions[index - 1]
                        ? currentObjects[index - 1]
                        : currentObjects[index];
                    int target = directions[index - 1]
                        ? currentObjects[index]
                        : currentObjects[index - 1];
                    Require(
                        PipeTypes
                            .GetOutgoingNeighbors(factory, source)
                            .SequenceEqual(new[] { target })
                            && PipeTypes
                                .GetIncomingNeighbors(factory, target)
                                .SequenceEqual(new[] { source }),
                        "particle completed edge preserves native direction"
                    );
                    Require(
                        factory.cargoTraffic.beltPool[factory.entityPool[source].beltId].outputId
                            == factory.entityPool[target].beltId,
                        "particle native belt path matches completed edge"
                    );
                }
            }
            Record(
                "PASS ORBITAL_DRAG item="
                    + itemId
                    + " reverse="
                    + reverse
                    + " reverse_completion="
                    + reverse
                    + " nodes="
                    + currentObjects.Length
                    + " altitude="
                    + tool.altitude
                    + " native_planning=pass prebuild_links=pass completion_links=pass direction=pass"
            );
        }
        finally
        {
            foreach (int entityId in Enumerable.Range(1, factory.entityCursor - 1).ToArray())
            {
                if (
                    !originalEntities.Contains(entityId)
                    && factory.entityPool[entityId].id == entityId
                    && factory.entityPool[entityId].protoId == itemId
                )
                {
                    int returnedItem = 0;
                    factory.DismantleFinally(player, entityId, ref returnedItem);
                }
            }
            foreach (int prebuildId in Enumerable.Range(1, factory.prebuildCursor - 1).ToArray())
            {
                if (
                    !originalPrebuilds.Contains(prebuildId)
                    && factory.prebuildPool[prebuildId].id == prebuildId
                    && factory.prebuildPool[prebuildId].protoId == itemId
                )
                {
                    int returnedItem = 0;
                    factory.DismantleFinally(player, -prebuildId, ref returnedItem);
                }
            }
            player.position = originalPosition;
            player.controller.cmd.stage = originalStage;
            player.mecha.buildArea = originalReach;
            tool._Free();
        }
    }

    private Vector3 OrbitalDragPosition(int itemId, float longitude)
    {
        float radians = longitude * Mathf.Deg2Rad;
        float radius = factory.planet.realRadius + 0.2f + (itemId == 6515 ? 48 : 43) * 1.3333333f;
        Vector3 candidate =
            new Vector3(Mathf.Cos(radians), 0.05f, Mathf.Sin(radians)).normalized * radius;
        return (Vector3)
            AccessTools
                .Method(orbitalPositions, "BeltShouldBeAdsorb")
                .Invoke(
                    null,
                    new object[] { candidate, Vector3.zero, OrbitalDragSide(itemId), orbitalLayout }
                );
    }

    private void CheckOrbitalRotation(
        int itemId,
        string phase,
        int index,
        Quaternion actual,
        Quaternion planned
    )
    {
        float difference = Quaternion.Angle(actual, planned);
        if (difference >= 0.1f)
        {
            Record(
                "ORBITAL_ROTATION item="
                    + itemId
                    + " phase="
                    + phase
                    + " index="
                    + index
                    + " difference="
                    + difference.ToString("F4")
                    + " planned_forward="
                    + (planned * Vector3.forward).ToString("F5")
                    + " actual_forward="
                    + (actual * Vector3.forward).ToString("F5")
                    + " planned_up="
                    + (planned * Vector3.up).ToString("F5")
                    + " actual_up="
                    + (actual * Vector3.up).ToString("F5")
            );
        }
        Require(difference < 0.1f, "orbital " + phase + " preserves planned rotation " + itemId);
    }

    private void CheckCompletedOrbitalGeometry(int itemId, int[] entities)
    {
        Type visuals = typeof(PipeApi).Assembly.GetType("FluidPipes.PipeVisuals", true);
        var shapes = new Dictionary<int, object>();
        var meshes = new Dictionary<int, Mesh>();
        try
        {
            for (int index = 0; index < entities.Length; index++)
            {
                int entityId = entities[index];
                EntityData entity = factory.entityPool[entityId];
                Vector3 previous = factory.entityPool[entities[Math.Max(0, index - 1)]].pos;
                Vector3 next = factory
                    .entityPool[entities[Math.Min(entities.Length - 1, index + 1)]]
                    .pos;
                Vector3 tangent = Vector3
                    .ProjectOnPlane(next - previous, entity.pos.normalized)
                    .normalized;
                float forwardAlignment = Mathf.Abs(
                    Vector3.Dot(entity.rot * Vector3.forward, tangent)
                );
                float radialAlignment = Vector3.Dot(entity.rot * Vector3.up, entity.pos.normalized);
                Require(
                    forwardAlignment > 0.995f && radialAlignment > 0.9995f,
                    "completed orbital tangent and radial up "
                        + itemId
                        + " node="
                        + index
                        + " tangent="
                        + forwardAlignment
                        + " radial="
                        + radialAlignment
                );
                shapes.Add(
                    entityId,
                    AccessTools
                        .Method(visuals, "Shape")
                        .Invoke(null, new object[] { factory, entityId, Vector3.zero })
                );
                object geometry = AccessTools
                    .Method(visuals, "SelectionGeometry")
                    .Invoke(null, new object[] { factory, entityId });
                meshes.Add(
                    entityId,
                    (Mesh)
                        AccessTools
                            .Method(geometry.GetType(), "Build")
                            .Invoke(geometry, new object[] { "OrbitalDrag.Seam", true })
                );
            }
            for (int index = 1; index < entities.Length; index++)
            {
                int first = entities[index - 1];
                int second = entities[index];
                object firstShape = shapes[first];
                object secondShape = shapes[second];
                Vector3 firstCenter = (Vector3)
                    AccessTools.Field(firstShape.GetType(), "Center").GetValue(firstShape);
                Vector3 secondCenter = (Vector3)
                    AccessTools.Field(secondShape.GetType(), "Center").GetValue(secondShape);
                Vector3 seam = (firstCenter + secondCenter) * 0.5f;
                Vector3 axis = (secondCenter - firstCenter).normalized;
                Vector3[] firstBoundary = OrbitalBoundary(
                    meshes[first],
                    factory.entityPool[first].pos,
                    seam,
                    axis
                );
                Vector3[] secondBoundary = OrbitalBoundary(
                    meshes[second],
                    factory.entityPool[second].pos,
                    seam,
                    axis
                );
                Require(
                    firstBoundary.Length >= 8 && secondBoundary.Length >= 8,
                    "orbital mesh has shared end sections "
                        + itemId
                        + " edge="
                        + index
                        + " first="
                        + firstBoundary.Length
                        + " second="
                        + secondBoundary.Length
                );
                Require(
                    firstBoundary.All(vertex =>
                        secondBoundary.Any(peer => Vector3.Distance(vertex, peer) < 0.001f)
                    )
                        && secondBoundary.All(vertex =>
                            firstBoundary.Any(peer => Vector3.Distance(vertex, peer) < 0.001f)
                        ),
                    "orbital adjacent mesh end sections coincide " + itemId + " edge=" + index
                );
            }
        }
        finally
        {
            foreach (Mesh mesh in meshes.Values)
            {
                UnityEngine.Object.Destroy(mesh);
            }
        }
    }

    private static Vector3[] OrbitalBoundary(Mesh mesh, Vector3 origin, Vector3 seam, Vector3 axis)
    {
        Vector3[] vertices = mesh.vertices;
        return mesh.GetTriangles(0)
            .Distinct()
            .Select(index => vertices[index] + origin)
            .Where(vertex => Mathf.Abs(Vector3.Dot(vertex - seam, axis)) < 0.0005f)
            .ToArray();
    }

    private static int OrbitalDragSide(int itemId)
    {
        return itemId == 6516 ? 1
            : itemId == 6517 ? 2
            : 0;
    }

    private void CheckOrbitalDragGeometry(int itemId, BuildTool_Path tool, bool reverse)
    {
        float expectedRadius = factory.planet.realRadius + 0.2f + tool.altitude * 1.3333333f;
        float maximumSpacing = 3.5f * Mathf.Max(1, factory.planet.realRadius / 200f);
        for (int index = 0; index < tool.buildPreviews.Count; index++)
        {
            BuildPreview preview = tool.buildPreviews[index];
            Require(
                Mathf.Abs(preview.lpos.magnitude - expectedRadius) < 0.03f,
                "orbital preview elevation " + itemId
            );
            int ringIndex = (int)
                AccessTools
                    .Method(orbitalPositions, "isBeltBuildingPosYCorrect")
                    .Invoke(
                        null,
                        new object[] { preview.lpos, OrbitalDragSide(itemId), orbitalLayout }
                    );
            Require(ringIndex >= 0, "orbital preview latitude band " + itemId);
            if (index == 0)
            {
                continue;
            }
            Vector3 previous = tool.buildPreviews[index - 1].lpos;
            Vector3 offset = preview.lpos - previous;
            Require(
                offset.magnitude > 0.05f && offset.magnitude < maximumSpacing,
                "orbital preview node spacing " + itemId
            );
            float longitudeDelta = Mathf.DeltaAngle(
                Mathf.Atan2(previous.z, previous.x) * Mathf.Rad2Deg,
                Mathf.Atan2(preview.lpos.z, preview.lpos.x) * Mathf.Rad2Deg
            );
            Require(
                reverse ? longitudeDelta < 0 : longitudeDelta > 0,
                "orbital preview follows drag orientation " + itemId
            );
            Vector3 forward = preview.lrot * Vector3.forward;
            Require(
                Mathf.Abs(Vector3.Dot(forward.normalized, offset.normalized)) > 0.8f,
                "orbital preview tangent alignment " + itemId
            );
        }
    }

    private void CheckOrbitalDragArrowPolicy(int itemId, BuildTool_Path tool)
    {
        var actionBuild = new PlayerAction_Build { factoryModel = factory.planet.factoryModel };
        var model = new BuildModel();
        model.Init(actionBuild);
        try
        {
            model.Open();
            model.EarlyGameTick();
            Require(
                model.connRenderer == factory.planet.factoryModel.connGizmoRenderer
                    && model.connGraph == model.connRenderer.cursorGizmoGraph,
                "orbital preview binds native cursor graph " + itemId
            );
            tool.UpdatePreviewModels(model);
            tool.UpdatePreviewModelConditions(model);
            Type gizmos = typeof(PipeApi).Assembly.GetType("FluidPipes.PipePlanningGizmos", true);
            object state = AccessTools
                .Method(gizmos, "State")
                .Invoke(null, new object[] { model.connRenderer });
            bool plainConnectionLines = (bool)
                AccessTools.Field(state.GetType(), "PipeCursor").GetValue(state);
            Require(
                plainConnectionLines == (itemId != 6516),
                "orbital preview arrow routing " + itemId
            );
        }
        finally
        {
            model.Close();
            model.Free();
            foreach (BuildPreview preview in tool.buildPreviews)
            {
                preview.previewIndex = -1;
            }
        }
    }

    private bool OrbitalEdgeOutput(int source, int target)
    {
        for (int slot = 0; slot < 4; slot++)
        {
            factory.ReadObjectConn(source, slot, out bool output, out int neighbor, out _);
            if (neighbor == target)
            {
                return output;
            }
        }
        throw new InvalidOperationException(
            "Missing orbital prebuild edge " + source + " to " + target
        );
    }
}
