using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace FluidPipes.Integration.Reservoirs
{
    [HarmonyPatch]
    internal static class ReservoirRemovalPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.DismantleFinally))]
        public static void Dismantle(PlanetFactory __instance, int objId, out int __state)
        {
            __state = PipePools.BeginRemoval(__instance, objId);
        }

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.RemoveEntityWithComponents))
        ]
        public static void Remove(PlanetFactory __instance, int id, out int __state)
        {
            __state = PipePools.BeginRemoval(__instance, id);
        }

        [
            HarmonyFinalizer,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.DismantleFinally))
        ]
        [HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.RemoveEntityWithComponents))]
        public static void End(PlanetFactory __instance, int __state)
        {
            PipePools.EndRemoval(__instance, __state);
        }
    }

    [HarmonyPatch]
    internal static class PoolPatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(FactoryStorage), nameof(FactoryStorage.GameTickTank))]
        public static void Tick(FactoryStorage __instance)
        {
            PipePools.Tick(__instance.factory);
            PipeValves.Tick(__instance.factory);
            PipeMachinePorts.Tick(__instance.factory);
        }

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.CreateEntityLogicComponents))
        ]
        [HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.RemoveEntityWithComponents))]
        public static void Changed(PlanetFactory __instance)
        {
            PipePools.Dirty(__instance);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.WriteObjectConn))]
        [HarmonyPatch(typeof(PlanetFactory), "WriteObjectConnDirect")]
        [HarmonyPatch(
            typeof(PlanetFactory),
            nameof(PlanetFactory.ClearObjectConn),
            new[] { typeof(int), typeof(int) }
        )]
        public static void Connections(PlanetFactory __instance)
        {
            PipePools.Dirty(__instance);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.UpdateSplitter))]
        [HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.UpdateSplitterAsync))]
        public static bool Splitter(CargoTraffic __instance, ref SplitterComponent sp) =>
            !PipeValves.IsValve(__instance.factory, sp.entityId);

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.RemoveEntityWithComponents))
        ]
        public static void Removed(PlanetFactory __instance, int id)
        {
            if (PipeValves.IsValve(__instance, id))
            {
                PipeValves.Remove(__instance, id);
            }
        }

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(PlanetTransport), nameof(PlanetTransport.SetStationStorage))
        ]
        public static void StationChanged(PlanetTransport __instance)
        {
            PipePools.Dirty(__instance.factory);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(FactoryStorage), nameof(FactoryStorage.SetTankBelt))]
        [HarmonyPatch(typeof(FactoryStorage), nameof(FactoryStorage.DisconnectToTank))]
        [HarmonyPatch(typeof(FactoryStorage), nameof(FactoryStorage.SetTankNextTarget))]
        public static void TankChanged(FactoryStorage __instance)
        {
            PipePools.Dirty(__instance.factory);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.WriteObjectConn))]
        [HarmonyPatch(typeof(PlanetFactory), "WriteObjectConnDirect")]
        public static bool ValvePorts(
            PlanetFactory __instance,
            int objId,
            int slot,
            int otherObjId,
            int otherSlot
        ) =>
            (
                !PipeValves.IsValve(__instance, objId)
                || slot >= 0
                    && slot < 2
                    && (otherObjId == 0 || PipeRuntime.IsPipeObject(__instance, otherObjId))
            )
            && (
                !PipeValves.IsValve(__instance, otherObjId)
                || otherSlot >= 0 && otherSlot < 2 && PipeRuntime.IsPipeObject(__instance, objId)
            )
            && PipePools.CanAttach(__instance, objId, otherObjId, otherSlot)
            && PipePools.CanAttach(__instance, otherObjId, objId, slot);
    }

    [HarmonyPatch]
    internal static class ValveWindowPatches
    {
        private static bool IsValve(UISplitterWindow window)
        {
            var factory = GameMain.localPlanet?.factory;
            int entityId2 = window.splitterId;
            return factory != null
                && entityId2 > 0
                && entityId2 < factory.cargoTraffic.splitterPool.Length
                && PipeValves.IsValve(
                    factory,
                    factory.cargoTraffic.splitterPool[entityId2].entityId
                );
        }

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(UISplitterWindow), nameof(UISplitterWindow.OnSplitterIdChange))
        ]
        public static bool Open(UISplitterWindow __instance)
        {
            bool valve = IsValve(__instance);
            __instance.splitterCircleTrans.gameObject.SetActive(!valve);
            var image =
                AccessTools
                    .Field(typeof(UISplitterWindow), "splitterImageTrans")
                    .GetValue(__instance) as UnityEngine.RectTransform;
            if (image != null)
            {
                image.gameObject.SetActive(!valve);
            }

            var legends =
                AccessTools.Field(typeof(UISplitterWindow), "legendsTrans").GetValue(__instance)
                as UnityEngine.RectTransform;
            if (legends != null)
            {
                legends.gameObject.SetActive(!valve);
            }

            if (!valve)
            {
                return true;
            }

            __instance.titleText.text = "流体阀门";
            AccessTools
                .Field(typeof(UISplitterWindow), "splitterGo")
                .SetValue(
                    __instance,
                    AccessTools
                        .Field(typeof(UISplitterWindow), "splitter_a_ui")
                        .GetValue(__instance)
                );
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UISplitterWindow), "_OnUpdate")]
        public static bool Update(UISplitterWindow __instance) => !IsValve(__instance);
    }

    [HarmonyPatch]
    internal static class ReservoirPortPatches
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> Targets()
        {
            yield return AccessTools.Method(typeof(TankComponent), nameof(TankComponent.GameTick));
            yield return AccessTools.Method(
                typeof(StationComponent),
                nameof(StationComponent.UpdateInputSlots)
            );
            yield return AccessTools.Method(
                typeof(StationComponent),
                nameof(StationComponent.UpdateOutputSlots)
            );
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original
        )
        {
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (
                    instruction.opcode != OpCodes.Ldfld
                    || !(instruction.operand is FieldInfo field)
                )
                {
                    continue;
                }

                bool tank =
                    field.DeclaringType == typeof(TankComponent)
                    && (
                        field.Name == "belt0"
                        || field.Name == "belt1"
                        || field.Name == "belt2"
                        || field.Name == "belt3"
                    );
                bool station = field.DeclaringType == typeof(SlotData) && field.Name == "beltId";
                if (!tank && !station)
                {
                    continue;
                }

                yield return new CodeInstruction(OpCodes.Ldarg_1);
                if (tank)
                {
                    yield return new CodeInstruction(
                        OpCodes.Ldfld,
                        AccessTools.Field(typeof(PlanetFactory), nameof(PlanetFactory.cargoTraffic))
                    );
                }

                yield return new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(ReservoirPortPatches), nameof(Belt))
                );
            }
        }

        public static int Belt(int belt, CargoTraffic traffic) =>
            PipeRuntime.IsPipe(traffic, belt) ? 0 : belt;
    }
}
