using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace FluidPipes
{
    internal enum ValveDirection
    {
        Both,
        AtoB,
        BtoA,
    }

    internal enum ValveLevel
    {
        Equalize,
        KeepA,
        KeepB,
    }

    internal sealed class ValveSettings
    {
        public bool Enabled = true;
        public double Rate = 60;
        public double Target = 0.5;
        public ValveDirection Direction;
        public ValveLevel Level;
        public bool InfiniteEnabled;
        public int InfiniteSide;
        public int InfiniteItemId;
        public double InfiniteTarget = 1;
        public InfiniteLevelMode InfiniteMode;
        internal double Credit;
    }

    internal static class PipeValves
    {
        private static readonly ConcurrentDictionary<long, ValveSettings> settings =
            new ConcurrentDictionary<long, ValveSettings>();

        private static long Key(PlanetFactory factory, int entity) =>
            ((long)factory.planet.id << 32) | (uint)entity;

        public static bool IsValve(PlanetFactory factory, int entityId) =>
            Prototypes.ValveId > 0
            && factory != null
            && (
                entityId > 0
                    && factory.entityPool != null
                    && entityId < factory.entityPool.Length
                    && factory.entityPool[entityId].id == entityId
                    && factory.entityPool[entityId].protoId == Prototypes.ValveId
                || entityId < 0
                    && factory.prebuildPool != null
                    && -entityId > 0
                    && -entityId < factory.prebuildPool.Length
                    && factory.prebuildPool[-entityId].id == -entityId
                    && factory.prebuildPool[-entityId].protoId == Prototypes.ValveId
            );

        public static ValveSettings Get(PlanetFactory factory, int entity)
        {
            long key = Key(factory, entity);
            if (!settings.TryGetValue(key, out var value))
            {
                value = settings.GetOrAdd(key, new ValveSettings());
            }
            return value;
        }

        public static void Remove(PlanetFactory factory, int entity)
        {
            settings.TryRemove(Key(factory, entity), out _);
            ValvePower.Remove(factory, entity);
        }

        public static void Clear()
        {
            settings.Clear();
            ValvePower.Clear();
        }

        public static void Ends(
            PlanetFactory factory,
            int entity,
            out int sideAPipeEntityId,
            out int sideBPipeEntityId
        )
        {
            factory.ReadObjectConn(entity, 0, out _, out sideAPipeEntityId, out _);
            factory.ReadObjectConn(entity, 1, out _, out sideBPipeEntityId, out _);
            if (!PipeRuntime.IsPipeObject(factory, sideAPipeEntityId) || sideAPipeEntityId < 0)
            {
                sideAPipeEntityId = 0;
            }

            if (!PipeRuntime.IsPipeObject(factory, sideBPipeEntityId) || sideBPipeEntityId < 0)
            {
                sideBPipeEntityId = 0;
            }
        }

        public static void Tick(PlanetFactory factory)
        {
            foreach (int entity in ValvePower.Entities(factory))
            {
                if (IsValve(factory, entity))
                {
                    Step(factory, entity, 1.0 / 60);
                }
            }
        }

        public static long Step(PlanetFactory factory, int entity, double seconds)
        {
            var config = Get(factory, entity);
            ValvePower.Demand(factory, entity, false);
            if (config.InfiniteEnabled)
            {
                config.Credit = 0;
                return SandboxValve.Step(factory, entity, config);
            }
            Ends(factory, entity, out int sideAPipeEntityId, out int sideBPipeEntityId);
            if (
                !config.Enabled
                || config.Rate <= 0
                || sideAPipeEntityId == 0
                || sideBPipeEntityId == 0
            )
            {
                config.Credit = 0;
                return 0;
            }
            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            if (PipePools.SameSpace(graph, sideAPipeEntityId, sideBPipeEntityId))
            {
                config.Credit = 0;
                return 0;
            }
            var sideAInventory = PipePools.View(graph, sideAPipeEntityId);
            var sideBInventory = PipePools.View(graph, sideBPipeEntityId);
            if (
                sideAInventory.Conflict
                || sideBInventory.Conflict
                || sideAInventory.Capacity == 0
                || sideBInventory.Capacity == 0
                || sideAInventory.Item != 0
                    && sideBInventory.Item != 0
                    && sideAInventory.Item != sideBInventory.Item
            )
            {
                config.Credit = 0;
                return 0;
            }
            double desired =
                config.Level == ValveLevel.KeepA
                    ? sideAInventory.Count - sideAInventory.Capacity * config.Target
                : config.Level == ValveLevel.KeepB
                    ? sideBInventory.Capacity * config.Target - sideBInventory.Count
                : (
                    (double)sideAInventory.Count * sideBInventory.Capacity
                    - (double)sideBInventory.Count * sideAInventory.Capacity
                ) / (sideAInventory.Capacity + sideBInventory.Capacity);
            bool forward = desired > 0;
            if (
                Math.Abs(desired) < 1
                || forward && config.Direction == ValveDirection.BtoA
                || !forward && config.Direction == ValveDirection.AtoB
            )
            {
                config.Credit = 0;
                return 0;
            }
            var source = forward ? sideAInventory : sideBInventory;
            var destination = forward ? sideBInventory : sideAInventory;
            if (
                !TransportItemRegistry.IsAllowed(source.Item)
                || destination.RequiresFluid && !ItemProto.isFluid(source.Item)
            )
            {
                config.Credit = 0;
                return 0;
            }
            if (source.Count == 0 || destination.Count >= destination.Capacity)
            {
                config.Credit = 0;
                return 0;
            }
            double supply = ValvePower.Demand(factory, entity, true);
            if (supply <= 0)
            {
                config.Credit = 0;
                return 0;
            }
            double rate = config.Rate * supply;
            config.Credit = Math.Min(config.Credit + rate * seconds, Math.Max(1, rate * seconds));
            int count = (int)
                Math.Min(
                    int.MaxValue,
                    Math.Min(
                        Math.Floor(config.Credit),
                        Math.Min(
                            Math.Floor(Math.Abs(desired)),
                            Math.Min(source.Count, destination.Capacity - destination.Count)
                        )
                    )
                );
            if (count <= 0)
            {
                return 0;
            }

            var amount = PipePools.Take(
                graph,
                forward ? sideAPipeEntityId : sideBPipeEntityId,
                count
            );
            if (amount.Count == 0)
            {
                return 0;
            }

            if (
                !PipePools.Insert(
                    graph,
                    forward ? sideBPipeEntityId : sideAPipeEntityId,
                    amount.Item,
                    (int)amount.Count,
                    (int)amount.ProliferatorPoints
                )
            )
            {
                if (
                    !PipePools.Insert(
                        graph,
                        forward ? sideAPipeEntityId : sideBPipeEntityId,
                        amount.Item,
                        (int)amount.Count,
                        (int)amount.ProliferatorPoints
                    )
                )
                {
                    throw new InvalidOperationException("Valve rollback could not restore fluid.");
                }
                return 0;
            }
            config.Credit -= amount.Count;
            return amount.Count;
        }

        public static void Export(BinaryWriter writer)
        {
            var entries = settings.ToArray();
            writer.Write(entries.Length);
            foreach (var entry in entries)
            {
                var valveSettings = entry.Value;
                writer.Write(entry.Key);
                writer.Write(valveSettings.Enabled);
                writer.Write(valveSettings.Rate);
                writer.Write(valveSettings.Target);
                writer.Write((int)valveSettings.Direction);
                writer.Write((int)valveSettings.Level);
                writer.Write(valveSettings.InfiniteEnabled);
                writer.Write(valveSettings.InfiniteSide);
                writer.Write(valveSettings.InfiniteItemId);
                writer.Write(valveSettings.InfiniteTarget);
                writer.Write((int)valveSettings.InfiniteMode);
            }
        }

        public static void Import(BinaryReader reader, bool hasInfiniteSettings = true)
        {
            settings.Clear();
            int count = reader.ReadInt32();
            if (count < 0 || count > 2000000)
            {
                throw new InvalidDataException("Invalid valve count.");
            }

            for (int index = 0; index < count; index++)
            {
                long key = reader.ReadInt64();
                var valveSettings = new ValveSettings
                {
                    Enabled = reader.ReadBoolean(),
                    Rate = reader.ReadDouble(),
                    Target = reader.ReadDouble(),
                    Direction = (ValveDirection)reader.ReadInt32(),
                    Level = (ValveLevel)reader.ReadInt32(),
                };
                if (hasInfiniteSettings)
                {
                    valveSettings.InfiniteEnabled = reader.ReadBoolean();
                    valveSettings.InfiniteSide = reader.ReadInt32();
                    valveSettings.InfiniteItemId = reader.ReadInt32();
                    valveSettings.InfiniteTarget = reader.ReadDouble();
                    valveSettings.InfiniteMode = (InfiniteLevelMode)reader.ReadInt32();
                }
                if (
                    double.IsNaN(valveSettings.Rate)
                    || double.IsInfinity(valveSettings.Rate)
                    || valveSettings.Rate < 0
                    || valveSettings.Rate > 1000000
                    || double.IsNaN(valveSettings.Target)
                    || valveSettings.Target < 0
                    || valveSettings.Target > 1
                    || (int)valveSettings.Direction < 0
                    || (int)valveSettings.Direction > 2
                    || (int)valveSettings.Level < 0
                    || (int)valveSettings.Level > 2
                    || settings.ContainsKey(key)
                    || valveSettings.InfiniteSide < 0
                    || valveSettings.InfiniteSide > 1
                    || valveSettings.InfiniteItemId < 0
                    || double.IsNaN(valveSettings.InfiniteTarget)
                    || valveSettings.InfiniteTarget < 0
                    || valveSettings.InfiniteTarget > 1
                    || (int)valveSettings.InfiniteMode < 0
                    || (int)valveSettings.InfiniteMode > 2
                )
                {
                    throw new InvalidDataException("Invalid valve settings.");
                }

                settings.TryAdd(key, valveSettings);
            }
        }
    }
}
