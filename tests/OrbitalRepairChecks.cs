using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

public sealed partial class OrbitalRingChecks
{
    private void CheckOrbitalRepairs()
    {
        foreach (int itemId in new[] { 6515, 6517, 6516 })
        {
            var ring = displayArcs[itemId];
            int index = ring.Count / 3;
            int removed = ring[index];
            EntityData saved = factory.entityPool[removed];
            factory.RemoveEntityWithComponents(removed, false);
            Type visuals = typeof(FluidPipes.API.PipeApi).Assembly.GetType(
                "FluidPipes.PipeVisuals",
                true
            );
            AccessTools.Method(visuals, "FlushColliderChanges").Invoke(null, null);
            factory.planet.physics.RefreshColliders();
            Physics.SyncTransforms();
            var player = GameMain.mainPlayer;
            Vector3 originalPosition = player.position;
            int originalStage = player.controller.cmd.stage;
            float originalReach = player.mecha.buildArea;
            var tool = new BuildTool_Path();
            tool._Init(GameMain.data);
            try
            {
                player.position = saved.pos;
                player.mecha.buildArea = 1000;
                player.TryAddItemToPackage(itemId, 200, 0, false, 0);
                tool.factory = factory;
                tool.planet = factory.planet;
                tool.handItem = LDB.items.Select(itemId);
                tool.handPrefabDesc = tool.handItem.prefabDesc;
                tool.altitude = itemId == 6515 ? 48 : 43;
                player.controller.cmd.stage = 0;
                tool.startObjectId = tool.castObjectId = 0;
                tool.startTarget = tool.cursorTarget = tool.castGroundPosSnapped = saved.pos;
                tool.cursorValid = true;
                tool.DeterminePreviews();
                factory.planet.physics.nearColliderLogic.ActiveEntityBuildCollidersInArea(
                    saved.pos,
                    16
                );
                factory.planet.physics.RefreshColliders();
                Physics.SyncTransforms();
                AccessTools
                    .Field(typeof(BuildTool), "tmpPackage")
                    .SetValue(tool, new StorageComponent(player.package.size));
                AccessTools.Field(typeof(BuildTool), "tmpInhandId").SetValue(tool, itemId);
                AccessTools.Field(typeof(BuildTool), "tmpInhandCount").SetValue(tool, 200);
                bool valid = tool.CheckBuildConditions();
                Record(
                    "REPAIR item="
                        + itemId
                        + " valid="
                        + valid
                        + " previews="
                        + tool.buildPreviews.Count
                        + " conditions="
                        + string.Join(",", tool.buildPreviews.Select(preview => preview.condition))
                        + " endpoints="
                        + tool.startObjectId
                        + ","
                        + tool.castObjectId
                );
                foreach (var preview in tool.buildPreviews)
                {
                    Record(
                        "REPAIR_PREVIEW item="
                            + itemId
                            + " pos_error="
                            + Vector3.Distance(preview.lpos, saved.pos)
                            + " input="
                            + preview.inputObjId
                            + " output="
                            + preview.outputObjId
                            + " tangent="
                            + Mathf.Abs(
                                Vector3.Dot(
                                    preview.lrot * Vector3.forward,
                                    Vector3.Cross(Vector3.up, preview.lpos).normalized
                                )
                            )
                    );
                }
                Require(
                    valid,
                    "removed orbital component accepts single-node reconstruction " + itemId
                );
                player.controller.cmd.stage = 1;
                tool.startObjectId = ring[index - 1];
                tool.castObjectId = ring[index + 1];
                tool.startTarget = factory.entityPool[tool.startObjectId].pos;
                tool.cursorTarget = tool.castGroundPosSnapped = factory
                    .entityPool[tool.castObjectId]
                    .pos;
                tool.DeterminePreviews();
                Require(
                    tool.CheckBuildConditions(),
                    "native dragged repair conditions "
                        + itemId
                        + ": "
                        + string.Join(",", tool.buildPreviews.Select(preview => preview.condition))
                );
                tool.CreatePrebuilds();
                int pending = -tool
                    .buildPreviews.Single(preview =>
                        preview.objId < 0 && Vector3.Distance(preview.lpos, saved.pos) < 0.01f
                    )
                    .objId;
                Require(pending > 0, "orbital repair prebuild exists " + itemId);
                factory.BuildFinally(player, pending, false, false);
                int restored = Enumerable
                    .Range(1, factory.entityCursor - 1)
                    .First(entityId =>
                        factory.entityPool[entityId].id == entityId
                        && factory.entityPool[entityId].protoId == itemId
                        && Vector3.Distance(factory.entityPool[entityId].pos, saved.pos) < 0.001f
                    );
                ring[index] = restored;
                Require(
                    new System.Collections.Generic.HashSet<int>(
                        FluidPipes.API.PipeTypes.GetNeighbors(factory, restored)
                    ).SetEquals(new[] { ring[index - 1], ring[index + 1] }),
                    "native repair reconnects both neighbors " + itemId
                );
                AccessTools
                    .Method(orbitalIntegration, "Schedule")
                    .Invoke(null, new object[] { factory, restored });
                AccessTools.Method(orbitalIntegration, "Update").Invoke(null, null);
                Require(OrbitalComplete(restored), "repaired orbital ring is complete " + itemId);
                Record("PASS native dismantle/rebuild item=" + itemId);
            }
            finally
            {
                player.position = originalPosition;
                player.controller.cmd.stage = originalStage;
                player.mecha.buildArea = originalReach;
                tool._Free();
            }
        }
    }
}
