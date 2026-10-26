using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace FluidPipes.Integration.Machines
{
    [HarmonyPatch]
    internal static class MachinePortPatches
    {
        private static readonly MethodInfo CloneDescription = AccessTools.Method(
            typeof(object),
            "MemberwiseClone"
        );
        private static readonly ConditionalWeakTable<PrefabDesc, PrefabDesc> descriptions =
            new ConditionalWeakTable<PrefabDesc, PrefabDesc>();
        private static readonly ConditionalWeakTable<PrefabDesc, Pose[]> pipePorts =
            new ConditionalWeakTable<PrefabDesc, Pose[]>();

        internal const float PipeStandOff = 1.65f;

        private static Pose[] PipePorts(PrefabDesc description) =>
            pipePorts.GetValue(
                description,
                original =>
                {
                    var ports = new Pose[original.slotPoses.Length];
                    for (int slotIndex = 0; slotIndex < ports.Length; slotIndex++)
                    {
                        var slot = original.slotPoses[slotIndex];
                        Vector3 position = slot.position + slot.forward * PipeStandOff;
                        position.y = 0;
                        ports[slotIndex] = new Pose(position, slot.rotation);
                    }
                    return ports;
                }
            );

        private static bool PipeTool(BuildTool tool) =>
            PipeMachinePorts.Enabled
            && tool is BuildTool_Path path
            && PipeTypeRegistry.IsInventory(path.handItem?.ID);

        [HarmonyPostfix, HarmonyPatch(typeof(BuildTool), nameof(BuildTool.GetPrefabDesc))]
        public static void Description(BuildTool __instance, int objId, ref PrefabDesc __result)
        {
            if (
                !PipeTool(__instance)
                || PipeMachinePorts.MachineDescription(__instance.factory, objId) == null
            )
            {
                return;
            }
            __result = descriptions.GetValue(
                __result,
                original =>
                {
                    // The terminal stands outside the housing while retaining its native machine slot index.
                    var clone = (PrefabDesc)CloneDescription.Invoke(original, null);
                    clone.portPoses = PipePorts(original);
                    return clone;
                }
            );
        }

        [HarmonyPostfix, HarmonyPatch(typeof(BuildTool), nameof(BuildTool.GetLocalPorts))]
        public static void Ports(BuildTool __instance, int objId, ref Pose[] __result)
        {
            if (PipeTool(__instance))
            {
                var description = PipeMachinePorts.MachineDescription(__instance.factory, objId);
                if (description != null)
                {
                    __result =
                        __instance.GetLocalSlots(objId).Length > 0
                            ? PipePorts(description)
                            : BuildTool.emptyPoseArr;
                }
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.WriteObjectConn))]
        [HarmonyPatch(typeof(PlanetFactory), "WriteObjectConnDirect")]
        public static void Connected(PlanetFactory __instance, int objId, int slot)
        {
            PipeMachinePorts.Observe(__instance, objId, slot);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlanetFactory), "ClearObjectConnDirect")]
        public static void Disconnected(PlanetFactory __instance, int objId, int slot)
        {
            PipeMachinePorts.Observe(__instance, objId, slot);
        }

        [
            HarmonyPrefix,
            HarmonyPriority(Priority.First),
            HarmonyPatch(
                typeof(PlanetFactory),
                nameof(PlanetFactory.HandleObjectConnChangeWhenBuild)
            )
        ]
        public static void BeginBuild(out bool __state)
        {
            // Reciprocal slots are rewritten in separate calls during prebuild conversion.
            PipeMachinePorts.BeginObjectMove();
            __state = true;
        }

        [
            HarmonyPostfix,
            HarmonyPatch(
                typeof(PlanetFactory),
                nameof(PlanetFactory.HandleObjectConnChangeWhenBuild)
            )
        ]
        public static void Built(PlanetFactory __instance, int oldId, int newId)
        {
            PipeMachinePorts.MoveObject(__instance, oldId, newId);
        }

        [
            HarmonyFinalizer,
            HarmonyPatch(
                typeof(PlanetFactory),
                nameof(PlanetFactory.HandleObjectConnChangeWhenBuild)
            )
        ]
        public static void EndBuild(bool __state)
        {
            if (__state)
            {
                PipeMachinePorts.EndObjectMove();
            }
        }

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.RemoveEntityWithComponents))
        ]
        public static void Removed(PlanetFactory __instance, int id)
        {
            PipeMachinePorts.RemoveObject(__instance, id);
        }

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.RemovePrebuildWithComponents))
        ]
        public static void RemovedPrebuild(PlanetFactory __instance, int id)
        {
            PipeMachinePorts.RemoveObject(__instance, -id);
        }

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(AssemblerComponent), nameof(AssemblerComponent.SetRecipe))
        ]
        public static void RecipeChanged()
        {
            PipeMachinePorts.RecipeChanged();
        }
    }
}
