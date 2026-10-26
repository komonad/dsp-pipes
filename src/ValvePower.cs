using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace FluidPipes
{
    [HarmonyPatch]
    internal static class ValvePower
    {
        // 原生电力值以焦耳每模拟 tick 计，每秒 60 tick。
        public const long WorkEnergyPerTick = 1667;
        public const long IdleEnergyPerTick = 0;
        private static ConditionalWeakTable<PlanetFactory, List<int>> valves =
            new ConditionalWeakTable<PlanetFactory, List<int>>();

        public static void Clear()
        {
            valves = new ConditionalWeakTable<PlanetFactory, List<int>>();
        }

        private static List<int> Scan(PlanetFactory factory)
        {
            var result = new List<int>();
            var traffic = factory.cargoTraffic;
            if (traffic?.splitterPool != null)
            {
                for (int index = 1; index < traffic.splitterCursor; index++)
                {
                    int entity = traffic.splitterPool[index].entityId;
                    if (
                        traffic.splitterPool[index].id == index
                        && PipeValves.IsValve(factory, entity)
                    )
                    {
                        result.Add(entity);
                        Ensure(factory, entity);
                    }
                }
            }

            return result;
        }

        public static List<int> Entities(PlanetFactory factory) => valves.GetValue(factory, Scan);

        public static int Ensure(PlanetFactory factory, int entity)
        {
            var power = factory.powerSystem;
            if (power?.consumerPool == null || !PipeValves.IsValve(factory, entity) || entity < 0)
            {
                return 0;
            }

            int entityId2 = factory.entityPool[entity].powerConId;
            if (
                entityId2 <= 0
                || entityId2 >= power.consumerPool.Length
                || power.consumerPool[entityId2].id != entityId2
                || power.consumerPool[entityId2].entityId != entity
            )
            {
                entityId2 = power.NewConsumerComponent(
                    entity,
                    WorkEnergyPerTick,
                    IdleEnergyPerTick
                );
                if (factory.planet.factoryLoaded && factory.planet.factoryModel != null)
                {
                    factory.planet.factoryModel.RefreshPowerConsumers();
                }
            }
            power.consumerPool[entityId2].workEnergyPerTick = WorkEnergyPerTick;
            power.consumerPool[entityId2].idleEnergyPerTick = IdleEnergyPerTick;
            return entityId2;
        }

        public static float Supply(PlanetFactory factory, int entity)
        {
            var power = factory.powerSystem;
            if (power?.consumerPool == null || entity <= 0 || entity >= factory.entityPool.Length)
            {
                return 0;
            }

            int entityId2 = factory.entityPool[entity].powerConId;
            if (
                entityId2 <= 0
                || entityId2 >= power.consumerPool.Length
                || power.consumerPool[entityId2].id != entityId2
                || power.consumerPool[entityId2].entityId != entity
            )
            {
                return 0;
            }

            int network = power.consumerPool[entityId2].networkId;
            if (
                network <= 0
                || power.networkServes == null
                || network >= power.networkServes.Length
                || power.netPool == null
                || network >= power.netPool.Length
                || power.netPool[network]?.id != network
            )
            {
                return 0;
            }

            float ratio = power.networkServes[network];
            return float.IsNaN(ratio) ? 0 : Math.Max(0, Math.Min(1, ratio));
        }

        public static float Demand(PlanetFactory factory, int entity, bool working)
        {
            int entityId = Ensure(factory, entity);
            if (entityId == 0)
            {
                return 0;
            }

            factory.powerSystem.consumerPool[entityId].SetRequiredEnergy(working);
            return working ? Supply(factory, entity) : 0;
        }

        public static void Restore(PlanetFactory factory)
        {
            foreach (int entity in Entities(factory))
            {
                if (PipeValves.IsValve(factory, entity))
                {
                    Ensure(factory, entity);
                }
            }
        }

        public static void Remove(PlanetFactory factory, int entity)
        {
            if (valves.TryGetValue(factory, out var entities))
            {
                entities.Remove(entity);
            }
        }

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.CreateEntityLogicComponents))
        ]
        public static void Created(PlanetFactory __instance, int entityId)
        {
            if (PipeValves.IsValve(__instance, entityId))
            {
                var entities = Entities(__instance);
                if (!entities.Contains(entityId))
                {
                    entities.Add(entityId);
                }
                Ensure(__instance, entityId);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.Import))]
        public static void Imported(PlanetFactory __instance)
        {
            Restore(__instance);
        }
    }
}
