using System;
using System.Collections.Generic;
using HarmonyLib;

namespace FluidPipes.Integration.Belts
{
    [HarmonyPatch]
    internal static class PipeLifecyclePatches
    {
        [ThreadStatic]
        private static HashSet<Tuple<CargoTraffic, int>> removals;

        [HarmonyPostfix, HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.NewBeltComponent))]
        public static void NewBelt(CargoTraffic __instance, int entityId, int __result)
        {
            PipeRuntime.Bind(__instance);
            if (__instance.factory.entityPool[entityId].protoId == Prototypes.ItemId)
            {
                PipeRuntime.Bind(__instance).Add(entityId);
                for (int slot = 0; slot < 4; slot++)
                {
                    __instance.factory.ReadObjectConn(entityId, slot, out _, out int other, out _);
                    if (PipeRuntime.IsPipeObject(__instance.factory, other))
                    {
                        PipeRuntime.UseObjectLinks(__instance, entityId);
                    }
                }
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.Import))]
        public static void Loaded(CargoTraffic __instance)
        {
            PipeRuntime.Bind(__instance);
            for (int index = 1; index < __instance.beltCursor; index++)
            {
                if (PipeRuntime.IsPipe(__instance, index))
                {
                    PipeRuntime.UseObjectLinks(__instance, __instance.beltPool[index].entityId);
                }
            }
        }

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.CreateEntityLogicComponents))
        ]
        public static void Created(PlanetFactory __instance, int entityId)
        {
            if (!PipeRuntime.IsPipeObject(__instance, entityId))
            {
                return;
            }

            PipeRuntime.UseObjectLinks(__instance.cargoTraffic, entityId);
            PipeRuntime.Sync(__instance.cargoTraffic, __instance.entityPool[entityId].beltId);
            PipeVisuals.Dirty(__instance, entityId);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.ConnectToSplitter))]
        public static bool Splitter(CargoTraffic __instance, int splitterId, int targetBeltId) =>
            PipeValves.IsValve(__instance.factory, __instance.splitterPool[splitterId].entityId)
            || !PipeRuntime.IsPipe(__instance, targetBeltId);

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.ConnectToSpraycoater))
        ]
        public static bool Coater(CargoTraffic __instance, int cargoBeltId, int incBeltId) =>
            !PipeRuntime.IsPipe(__instance, cargoBeltId)
            && !PipeRuntime.IsPipe(__instance, incBeltId);

        [HarmonyPrefix, HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.ConnectToMonitor))]
        public static bool Monitor(CargoTraffic __instance, int targetBeltId) =>
            !PipeRuntime.IsPipe(__instance, targetBeltId);

        [HarmonyPrefix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.WriteObjectConn))]
        [HarmonyPatch(typeof(PlanetFactory), "WriteObjectConnDirect")]
        public static bool Write(PlanetFactory __instance, int objId, int otherObjId) =>
            PipeRuntime.CanLink(__instance, objId, otherObjId);

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.UpgradeEntityWithComponents))
        ]
        public static bool Upgrade(PlanetFactory __instance, int entityId, ItemProto newProto) =>
            (__instance.entityPool[entityId].protoId == Prototypes.ItemId)
            == (newProto.ID == Prototypes.ItemId);

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.UpgradePrebuildWithComponents))
        ]
        public static bool UpgradePrebuild(PlanetFactory __instance, int id, ItemProto newProto) =>
            (__instance.prebuildPool[id].protoId == Prototypes.ItemId)
            == (newProto.ID == Prototypes.ItemId);

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.AlterBeltConnections))
        ]
        public static void BeforeConnections(
            CargoTraffic __instance,
            int beltId,
            ref int outputId,
            ref int input0Id,
            ref int input1Id,
            ref int input2Id
        )
        {
            int entity = __instance.beltPool[beltId].entityId;
            bool removing =
                removals != null
                && removals.Count > 0
                && removals.Contains(Tuple.Create(__instance, beltId));
            if (
                !removing
                && PipeRuntime.IsPipe(__instance, beltId)
                && PipeRuntime.UsesObjectLinks(__instance, entity)
            )
            {
                PipeConnections.NativeLinks(
                    __instance,
                    entity,
                    out outputId,
                    out input0Id,
                    out input1Id,
                    out input2Id
                );
            }

            Filter(__instance, entity, ref outputId);
            Filter(__instance, entity, ref input0Id);
            Filter(__instance, entity, ref input1Id);
            Filter(__instance, entity, ref input2Id);
            if (PipeRuntime.IsPipe(__instance, beltId))
            {
                var graph = PipeRuntime.Bind(__instance);
                int fluid = graph.View(entity).Item;
                FilterFluid(__instance, graph, entity, 0, ref fluid, ref outputId);
                FilterFluid(__instance, graph, entity, 1, ref fluid, ref input0Id);
                FilterFluid(__instance, graph, entity, 2, ref fluid, ref input1Id);
                FilterFluid(__instance, graph, entity, 3, ref fluid, ref input2Id);
            }
        }

        private static void FilterFluid(
            CargoTraffic traffic,
            PipeGraph graph,
            int entity,
            int slot,
            ref int fluid,
            ref int belt
        )
        {
            if (!PipeRuntime.IsPipe(traffic, belt))
            {
                return;
            }

            int other = graph.View(traffic.beltPool[belt].entityId).Item;
            if (fluid != 0 && other != 0 && fluid != other)
            {
                traffic.factory.ClearObjectConn(entity, slot);
                belt = 0;
            }
            else if (other != 0)
            {
                fluid = other;
            }
        }

        private static void Filter(CargoTraffic traffic, int entity, ref int belt)
        {
            if (
                belt > 0
                && !PipeRuntime.CanLink(traffic.factory, entity, traffic.beltPool[belt].entityId)
            )
            {
                belt = 0;
            }
        }

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.AlterBeltConnections))
        ]
        public static void Connections(CargoTraffic __instance, int beltId)
        {
            PipeRuntime.Sync(__instance, beltId);
        }

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.RemoveBeltComponent))
        ]
        public static void Remove(CargoTraffic __instance, int id, out int __state)
        {
            __state = PipeRuntime.IsPipe(__instance, id) ? __instance.beltPool[id].entityId : 0;
            if (__state == 0)
            {
                return;
            }

            if (removals == null)
            {
                removals = new HashSet<Tuple<CargoTraffic, int>>();
            }

            removals.Add(Tuple.Create(__instance, id));
        }

        [
            HarmonyFinalizer,
            HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.RemoveBeltComponent))
        ]
        public static void RemoveFinished(CargoTraffic __instance, int id)
        {
            if (removals != null && removals.Count > 0)
            {
                removals.Remove(Tuple.Create(__instance, id));
            }
        }

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.RemoveBeltComponent))
        ]
        public static void Removed(CargoTraffic __instance, int __state)
        {
            if (__state > 0)
            {
                var peers = PipeTopology.Remove(__instance.factory, __state);
                PipeRuntime.Return(
                    GameMain.mainPlayer,
                    __state,
                    PipeRuntime.Bind(__instance).Remove(__state)
                );
                PipeVisuals.Dirty(__instance.factory, __state);
                foreach (int other in peers)
                {
                    PipeVisuals.Dirty(__instance.factory, other);
                }
            }
        }

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(BuildTool_Path), nameof(BuildTool_Path.CheckBuildConditions))
        ]
        public static void BuildCheck(BuildTool_Path __instance, ref bool __result)
        {
            bool compatible =
                !PipeRuntime.IsPipeObject(__instance.factory, __instance.startObjectId)
                || !PipeRuntime.IsPipeObject(__instance.factory, __instance.castObjectId)
                || PipeTopology.Compatible(
                    __instance.factory,
                    __instance.startObjectId,
                    __instance.castObjectId
                );
            foreach (var preview in __instance.buildPreviews)
            {
                bool valid =
                    compatible
                    && (
                        preview.inputObjId == 0
                        || PreviewLink(__instance.factory, preview, preview.inputObjId)
                    )
                    && (
                        preview.outputObjId == 0
                        || PreviewLink(__instance.factory, preview, preview.outputObjId)
                    );
                if (preview.coverObjId != 0)
                {
                    int entityId = preview.coverObjId;
                    int proto =
                        entityId > 0
                            ? __instance.factory.entityPool[entityId].protoId
                            : __instance.factory.prebuildPool[-entityId].protoId;
                    valid &= (proto == Prototypes.ItemId) == (preview.item.ID == Prototypes.ItemId);
                }
                if (!valid)
                {
                    preview.condition = EBuildCondition.Failure;
                    __result = false;
                }
            }
        }

        private static bool PreviewLink(PlanetFactory factory, BuildPreview preview, int other)
        {
            int proto =
                other > 0
                    ? factory.entityPool[other].protoId
                    : factory.prebuildPool[-other].protoId;
            if (proto == Prototypes.ValveId || preview.item.ID == Prototypes.ValveId)
            {
                return proto == Prototypes.ItemId || preview.item.ID == Prototypes.ItemId;
            }

            if (preview.item.ID != Prototypes.ItemId && proto != Prototypes.ItemId)
            {
                return true;
            }

            if (preview.item.ID == Prototypes.ItemId && proto == Prototypes.ItemId)
            {
                return true;
            }

            var desc = LDB
                .items.Select(preview.item.ID == Prototypes.ItemId ? proto : preview.item.ID)
                ?.prefabDesc;
            return desc != null
                && !desc.isBelt
                && !desc.isSplitter
                && !desc.isPiler
                && !desc.isSpraycoster
                && !desc.isMonitor;
        }
    }
}
