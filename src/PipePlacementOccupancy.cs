using System;
using HarmonyLib;
using UnityEngine;

namespace FluidPipes
{
    [HarmonyPatch]
    internal static class PipePlacementOccupancy
    {
        [ThreadStatic]
        private static int[] nearby;

        public static bool Validate(BuildTool_Path tool)
        {
            if (!PipeTypeRegistry.UsesPipeConstruction(tool.handItem?.ID))
            {
                return true;
            }

            if (nearby == null)
            {
                nearby = new int[64];
            }

            bool valid = true;
            for (int index = 0; index < tool.buildPreviews.Count; index++)
            {
                var preview = tool.buildPreviews[index];
                int count = tool.planet.physics.nearColliderLogic.GetBuildingsInAreaNonAlloc(
                    preview.lpos,
                    0.6f,
                    ref nearby,
                    false
                );
                for (int otherIndex = 0; otherIndex < count; otherIndex++)
                {
                    int entityId = nearby[otherIndex];
                    if (!PipeRuntime.IsPipeObject(tool.factory, entityId))
                    {
                        continue;
                    }

                    var pos =
                        entityId > 0
                            ? tool.factory.entityPool[entityId].pos
                            : tool.factory.prebuildPool[-entityId].pos;
                    if ((pos - preview.lpos).sqrMagnitude >= 0.0001f)
                    {
                        continue;
                    }

                    bool endpoint =
                        index == 0 && entityId == tool.startObjectId
                        || index == tool.buildPreviews.Count - 1 && entityId == tool.castObjectId;
                    if (endpoint)
                    {
                        preview.coverObjId = entityId;
                        preview.willRemoveCover = false;
                        continue;
                    }
                    preview.condition = EBuildCondition.Collide;
                    valid = false;
                    break;
                }
            }
            return valid;
        }

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(BuildTool_Path), nameof(BuildTool_Path.CheckBuildConditions))
        ]
        public static void Check(BuildTool_Path __instance)
        {
            Validate(__instance);
        }

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(BuildTool_Path), nameof(BuildTool_Path.CreatePrebuilds))
        ]
        public static bool Create(BuildTool_Path __instance) => Validate(__instance);
    }
}
