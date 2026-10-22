using System;
using HarmonyLib;

namespace FluidPipes.Integration.Reservoirs
{
    [HarmonyPatch]
    internal static class SharedExtractionPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(TankComponent), nameof(TankComponent.GameTick))]
        public static void Tank(ref TankComponent __instance, PlanetFactory factory)
        {
            if (!__instance.outputSwitch || __instance.fluidCount >= 8)
            {
                return;
            }

            var traffic = factory.cargoTraffic;
            bool output =
                __instance.belt0 > 0
                    && __instance.isOutput0
                    && !PipeRuntime.IsPipe(traffic, __instance.belt0)
                || __instance.belt1 > 0
                    && __instance.isOutput1
                    && !PipeRuntime.IsPipe(traffic, __instance.belt1)
                || __instance.belt2 > 0
                    && __instance.isOutput2
                    && !PipeRuntime.IsPipe(traffic, __instance.belt2)
                || __instance.belt3 > 0
                    && __instance.isOutput3
                    && !PipeRuntime.IsPipe(traffic, __instance.belt3);
            if (output)
            {
                PipePools.PrimeTank(factory, __instance.id, 8);
            }
        }

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(StationComponent), nameof(StationComponent.UpdateOutputSlots))
        ]
        public static void Output(
            StationComponent __instance,
            CargoTraffic traffic,
            int maxPilerCount
        )
        {
            if (__instance.storage == null || __instance.slots == null)
            {
                return;
            }

            int count = 0;
            foreach (var slot in __instance.slots)
            {
                if (
                    slot.dir == IODir.Output
                    && slot.beltId > 0
                    && !PipeRuntime.IsPipe(traffic, slot.beltId)
                )
                {
                    count++;
                }
            }

            if (count > 0)
            {
                PipePools.PrimeStation(
                    __instance,
                    Math.Max(1, __instance.pilerCount == 0 ? maxPilerCount : __instance.pilerCount)
                        * count
                );
            }
        }

        [
            HarmonyPrefix,
            HarmonyPatch(
                typeof(StationComponent),
                nameof(StationComponent.TakeItem),
                new[] { typeof(int), typeof(int), typeof(int) },
                new[] { ArgumentType.Ref, ArgumentType.Ref, ArgumentType.Out }
            )
        ]
        public static bool Take(
            StationComponent __instance,
            ref int itemId,
            ref int count,
            out int inc
        )
        {
            inc = 0;
            if (
                itemId <= 0
                || !PipePools.TryStationTake(__instance, count, itemId, null, out var amount)
            )
            {
                return true;
            }

            itemId = amount.Item;
            count = (int)amount.Count;
            inc = (int)amount.ProliferatorPoints;
            return false;
        }

        [
            HarmonyPrefix,
            HarmonyPatch(
                typeof(StationComponent),
                nameof(StationComponent.TakeItem),
                new[] { typeof(int), typeof(int), typeof(int[]), typeof(int) },
                new[] { ArgumentType.Ref, ArgumentType.Ref, ArgumentType.Normal, ArgumentType.Out }
            )
        ]
        public static bool TakeNeeds(
            StationComponent __instance,
            ref int _itemId,
            ref int _count,
            int[] _needs,
            out int _inc
        )
        {
            _inc = 0;
            if (!PipePools.TryStationTake(__instance, _count, _itemId, _needs, out var amount))
            {
                return true;
            }

            _itemId = amount.Item;
            _count = (int)amount.Count;
            _inc = (int)amount.ProliferatorPoints;
            return false;
        }
    }
}
