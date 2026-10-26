using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace FluidPipes.Integration.Belts
{
    // Harmony 按原生参数名绑定钩子参数。
    [HarmonyPatch]
    internal static class PathInsertPatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> Targets() =>
            typeof(CargoPath)
                .GetMethods()
                .Where(m =>
                    m.Name == "TryInsertItem"
                    || m.Name == "TryInsertItemAtHead"
                    || m.Name == "TryInsertItemAtHeadAndFillBlank"
                    || m.Name == "TryUpdateItemAtHeadAndFillBlank"
                    || m.Name == "InsertItemDirectWithFastCheck"
                );

        [HarmonyPrefix]
        public static bool Insert(
            CargoPath __instance,
            int itemId,
            byte stack,
            byte inc,
            ref bool __result
        )
        {
            if (!PipeRuntime.TryPath(__instance, out var graph, out int entity))
            {
                return true;
            }

            __result = PipeCargoAdapter.Insert(graph, entity, itemId, stack, inc);
            return false;
        }
    }

    [HarmonyPatch(typeof(CargoPath), nameof(CargoPath.TryInsertItemWithStackIncreasement))]
    internal static class IncrementPatch
    {
        [HarmonyPrefix]
        public static bool Insert(CargoPath __instance, int itemId, ref int count, ref int inc)
        {
            if (!PipeRuntime.TryPath(__instance, out var graph, out int entity))
            {
                return true;
            }

            PipeCargoAdapter.InsertAvailable(graph, entity, itemId, ref count, ref inc);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(CargoPath),
        nameof(CargoPath.TryPickItem),
        new[] { typeof(int), typeof(int), typeof(byte), typeof(byte) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Out }
    )]
    internal static class PickPatch
    {
        public static bool Pick(
            CargoPath path,
            int filter,
            int[] needs,
            ref byte stack,
            ref byte proliferatorPoints,
            ref int result
        )
        {
            if (!PipeRuntime.TryPath(path, out var graph, out int entity))
            {
                return true;
            }

            result = PipeCargoAdapter.Pick(
                graph,
                entity,
                filter,
                needs,
                out stack,
                out proliferatorPoints
            );
            return false;
        }

        [HarmonyPrefix]
        public static bool Prefix(
            CargoPath __instance,
            ref byte stack,
            ref byte inc,
            ref int __result
        ) => Pick(__instance, 0, null, ref stack, ref inc, ref __result);
    }

    [HarmonyPatch(
        typeof(CargoPath),
        nameof(CargoPath.TryPickItem),
        new[] { typeof(int), typeof(int), typeof(int), typeof(byte), typeof(byte) },
        new[]
        {
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Out,
            ArgumentType.Out,
        }
    )]
    internal static class PickFilteredPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            CargoPath __instance,
            int filter,
            ref byte stack,
            ref byte inc,
            ref int __result
        ) => PickPatch.Pick(__instance, filter, null, ref stack, ref inc, ref __result);
    }

    [HarmonyPatch(
        typeof(CargoPath),
        nameof(CargoPath.TryPickItem),
        new[] { typeof(int), typeof(int), typeof(int), typeof(int[]), typeof(byte), typeof(byte) },
        new[]
        {
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Normal,
            ArgumentType.Out,
            ArgumentType.Out,
        }
    )]
    internal static class PickNeedsPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            CargoPath __instance,
            int filter,
            int[] needs,
            ref byte stack,
            ref byte inc,
            ref int __result
        ) => PickPatch.Pick(__instance, filter, needs, ref stack, ref inc, ref __result);
    }

    [HarmonyPatch(typeof(CargoPath), nameof(CargoPath.TryPickFuel))]
    internal static class FuelPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            CargoPath __instance,
            int filter,
            int fuelMask,
            ref byte stack,
            ref byte inc,
            ref int __result
        ) =>
            PickPatch.Pick(
                __instance,
                filter,
                ItemProto.fuelNeeds[fuelMask],
                ref stack,
                ref inc,
                ref __result
            );
    }

    [HarmonyPatch(typeof(CargoPath), nameof(CargoPath.CanPickItemFromRear))]
    internal static class CanPickPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(CargoPath __instance, int[] needs, ref bool __result)
        {
            if (!PipeRuntime.TryPath(__instance, out var graph, out int entity))
            {
                return true;
            }

            var view = PipePools.Query(graph, entity);
            __result = view.Count > 0 && (needs == null || Array.IndexOf(needs, view.Item) >= 0);
            return false;
        }
    }

    [HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.TryPickItemAtRear))]
    internal static class RearPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            CargoTraffic __instance,
            int beltId,
            int filter,
            int[] needs,
            ref byte stack,
            ref byte inc,
            ref int __result
        )
        {
            if (!PipeRuntime.TryBelt(__instance, beltId, out var graph, out int entity))
            {
                return true;
            }

            __result = PipeCargoAdapter.Pick(graph, entity, filter, needs, out stack, out inc);
            return false;
        }
    }

    [HarmonyPatch(typeof(CargoPath), nameof(CargoPath.TryPickItemAtRear))]
    internal static class PathRearPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            CargoPath __instance,
            int[] needs,
            ref int needIdx,
            ref byte stack,
            ref byte inc,
            ref int __result
        )
        {
            if (!PipeRuntime.TryPath(__instance, out var graph, out int entity))
            {
                return true;
            }

            __result = PipeCargoAdapter.Pick(graph, entity, 0, needs, out stack, out inc);
            needIdx = __result == 0 || needs == null ? -1 : Array.IndexOf(needs, __result);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class PathQueryPatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> Targets() =>
            typeof(CargoPath)
                .GetMethods()
                .Where(m => m.Name == "HasCargoAtRear" || m.Name == "TestCargoAtRear");

        [HarmonyPrefix]
        public static bool Prefix(CargoPath __instance, ref bool __result)
        {
            if (!PipeRuntime.TryPath(__instance, out var graph, out int entity))
            {
                return true;
            }

            __result = PipePools.Query(graph, entity).Count > 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.GetItemIdAtRear))]
    internal static class RearQueryPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(CargoTraffic __instance, int beltId, ref int __result)
        {
            if (!PipeRuntime.TryBelt(__instance, beltId, out var graph, out int entity))
            {
                return true;
            }

            __result = PipePools.Query(graph, entity).Item;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class EmptyPathPatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> Targets() =>
            typeof(CargoPath)
                .GetMethods()
                .Where(m => m.Name == "Update" || m.Name == "PresentCargos");

        [HarmonyPrefix]
        public static bool Prefix(CargoPath __instance) =>
            !PipeRuntime.TryPath(__instance, out _, out _);
    }
}
