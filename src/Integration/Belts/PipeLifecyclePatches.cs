using System;
using System.Collections.Generic;
using HarmonyLib;

namespace FluidPipes.Integration.Belts
{
    [HarmonyPatch]
    internal static class PipeLifecyclePatches
    {
        internal sealed class RemovalState
        {
            internal int EntityId;
            internal int[] Neighbors;
        }

        [ThreadStatic]
        private static HashSet<Tuple<CargoTraffic, int>> removals;

        [HarmonyPostfix, HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.NewBeltComponent))]
        public static void NewBelt(CargoTraffic __instance, int entityId, int __result)
        {
            PipeRuntime.Bind(__instance);
            if (PipeTypeRegistry.IsItem(__instance.factory.entityPool[entityId].protoId))
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
                    int entityId = __instance.beltPool[index].entityId;
                    PipeRuntime.UseObjectLinks(__instance, entityId);
                    PipeRuntime.Sync(__instance, index);
                    API.PipeTypes.Changed(__instance.factory, entityId);
                }
            }
            PipeMachinePorts.Restore(__instance.factory);
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
            API.PipeTypes.Changed(__instance, entityId);
            foreach (int neighbor in PipeRuntime.Bind(__instance.cargoTraffic).Links(entityId))
            {
                API.PipeTypes.Changed(__instance, neighbor);
            }
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
            CanUpgrade(__instance.entityPool[entityId].protoId, newProto.ID);

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.UpgradeEntityWithComponents))
        ]
        public static void Upgraded(PlanetFactory __instance, int entityId, ItemProto newProto)
        {
            if (
                PipeRuntime.IsPipeObject(__instance, entityId)
                && __instance.entityPool[entityId].protoId == newProto.ID
            )
            {
                PipeVisuals.Dirty(__instance, entityId);
                // 中心高度变化可能改变相邻节点另一端的接头归属。
                foreach (int neighbor in API.PipeTypes.GetNeighbors(__instance, entityId))
                {
                    PipeVisuals.Dirty(__instance, neighbor);
                }
                API.PipeTypes.Changed(__instance, entityId);
            }
        }

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.UpgradePrebuildWithComponents))
        ]
        public static bool UpgradePrebuild(PlanetFactory __instance, int id, ItemProto newProto) =>
            CanUpgrade(__instance.prebuildPool[id].protoId, newProto.ID);

        private static bool CanUpgrade(int originalPrototypeId, int replacementPrototypeId)
        {
            bool originalIsPipe = PipeTypeRegistry.IsItem(originalPrototypeId);
            bool replacementIsPipe = PipeTypeRegistry.IsItem(replacementPrototypeId);
            return originalIsPipe && replacementIsPipe
                ? PipeTypeRegistry.Compatible(originalPrototypeId, replacementPrototypeId)
                : originalIsPipe == replacementIsPipe;
        }

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
            if (
                PipeRuntime.IsPipe(__instance, beltId)
                && PipeRuntime.HasInventory(__instance.factory, entity)
            )
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
            if (PipeRuntime.IsPipe(__instance, beltId))
            {
                int entityId = __instance.beltPool[beltId].entityId;
                PipeVisuals.Dirty(__instance.factory, entityId);
                API.PipeTypes.Changed(__instance.factory, entityId);
            }
        }

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.RemoveBeltComponent))
        ]
        public static void Remove(CargoTraffic __instance, int id, out RemovalState __state)
        {
            __state = null;
            if (!PipeRuntime.IsPipe(__instance, id))
            {
                return;
            }

            if (removals == null)
            {
                removals = new HashSet<Tuple<CargoTraffic, int>>();
            }

            int entityId = __instance.beltPool[id].entityId;
            var neighbors = new HashSet<int>(PipeRuntime.Bind(__instance).Links(entityId));
            foreach (int neighbor in PipeTopology.Neighbors(__instance.factory, entityId))
            {
                neighbors.Add(neighbor);
            }
            for (int slot = 0; slot < 4; slot++)
            {
                if (
                    PipeConnections.TryReadNativeLink(
                        __instance.factory,
                        entityId,
                        slot,
                        out _,
                        out int neighbor
                    )
                )
                {
                    neighbors.Add(neighbor);
                }
            }
            var detachedNeighbors = new int[neighbors.Count];
            neighbors.CopyTo(detachedNeighbors);
            __state = new RemovalState { EntityId = entityId, Neighbors = detachedNeighbors };
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
        public static void Removed(CargoTraffic __instance, RemovalState __state)
        {
            if (__state == null)
            {
                return;
            }

            int entityId = __state.EntityId;
            PipeTopology.Remove(__instance.factory, entityId);
            PipeRuntime.Return(
                GameMain.mainPlayer,
                entityId,
                PipeRuntime.Bind(__instance).Remove(entityId)
            );
            PipeVisuals.Dirty(__instance.factory, entityId);
            API.PipeTypes.Changed(__instance.factory, entityId);
            foreach (int neighbor in __state.Neighbors)
            {
                if (neighbor > 0 && PipeRuntime.IsPipeObject(__instance.factory, neighbor))
                {
                    PipeRuntime.Sync(__instance, __instance.factory.entityPool[neighbor].beltId);
                    PipeVisuals.Dirty(__instance.factory, neighbor);
                    API.PipeTypes.Changed(__instance.factory, neighbor);
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
                || PipeRuntime.CanLink(
                    __instance.factory,
                    __instance.startObjectId,
                    __instance.castObjectId
                )
                    && (
                        !PipeTypeRegistry.IsInventory(__instance.handItem?.ID)
                        || PipeTopology.Compatible(
                            __instance.factory,
                            __instance.startObjectId,
                            __instance.castObjectId
                        )
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
                    valid &= CanUpgrade(proto, preview.item.ID);
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
            return PipeRuntime.CanLinkPrototypes(
                preview.item.ID,
                PipeRuntime.ObjectPrototype(factory, other)
            );
        }
    }
}
