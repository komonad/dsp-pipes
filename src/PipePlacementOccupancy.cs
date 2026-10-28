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
            if (!PipeTypeRegistry.IsItem(tool.handItem?.ID))
            {
                return true;
            }

            if (nearby == null)
            {
                nearby = new int[64];
            }

            bool valid = true;
            bool nativeConstruction = PipeTypeRegistry.UsesNativeConstruction(tool.handItem?.ID);
            for (int index = 0; index < tool.buildPreviews.Count; index++)
            {
                var preview = tool.buildPreviews[index];
                int count = tool.planet.physics.nearColliderLogic.GetBuildingsInAreaNonAlloc(
                    preview.lpos,
                    nativeConstruction ? 2f : 0.6f,
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

                    Vector3 position = ObjectPosition(tool.factory, entityId);
                    bool samePosition = (position - preview.lpos).sqrMagnitude < 0.0001f;
                    if (
                        !samePosition
                        && (
                            !nativeConstruction
                            || !OverlapsNativeSpan(tool.factory, entityId, preview.lpos)
                        )
                    )
                    {
                        continue;
                    }

                    if (samePosition && nativeConstruction && preview.coverObjId == entityId)
                    {
                        continue;
                    }

                    bool endpoint =
                        samePosition
                        && (
                            index == 0 && entityId == tool.startObjectId
                            || index == tool.buildPreviews.Count - 1
                                && entityId == tool.castObjectId
                        );
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

        private static Vector3 ObjectPosition(PlanetFactory factory, int entityId) =>
            entityId > 0 ? factory.entityPool[entityId].pos : factory.prebuildPool[-entityId].pos;

        private static bool OverlapsNativeSpan(
            PlanetFactory factory,
            int entityId,
            Vector3 position
        )
        {
            int prototypeId = PipeRuntime.ObjectPrototype(factory, entityId);
            if (!PipeTypeRegistry.UsesNativeConstruction(prototypeId))
            {
                return false;
            }
            Vector3 first = ObjectPosition(factory, entityId);
            for (int slot = 0; slot < 4; slot++)
            {
                factory.ReadObjectConn(entityId, slot, out _, out int neighbor, out _);
                if (neighbor == 0 || PipeRuntime.ObjectPrototype(factory, neighbor) != prototypeId)
                {
                    continue;
                }
                Vector3 span = ObjectPosition(factory, neighbor) - first;
                float lengthSquared = span.sqrMagnitude;
                if (lengthSquared <= 0.0001f)
                {
                    continue;
                }
                float fraction = Vector3.Dot(position - first, span) / lengthSquared;
                // Connected native nodes own the centerline between their endpoints.
                if (
                    fraction > 0.001f
                    && fraction < 0.999f
                    && (position - first - span * fraction).sqrMagnitude < 0.0004f
                )
                {
                    return true;
                }
            }
            return false;
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
            HarmonyPostfix,
            HarmonyPriority(Priority.Last),
            HarmonyPatch(typeof(BuildTool_Path), nameof(BuildTool_Path.CheckBuildConditions))
        ]
        public static void Checked(BuildTool_Path __instance, ref bool __result)
        {
            if (!Validate(__instance))
            {
                __result = false;
            }
        }

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(BuildTool_Path), nameof(BuildTool_Path.CreatePrebuilds))
        ]
        public static bool Create(BuildTool_Path __instance) => Validate(__instance);
    }
}
