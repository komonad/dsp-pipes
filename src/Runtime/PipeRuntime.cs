using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace FluidPipes
{
    internal static class PipeRuntime
    {
        private sealed class Owner
        {
            public CargoTraffic Traffic;
            public PipeGraph Graph;
        }

        private static ConcurrentDictionary<int, PipeGraph> graphs =
            new ConcurrentDictionary<int, PipeGraph>();
        private static ConditionalWeakTable<CargoContainer, Owner> owners =
            new ConditionalWeakTable<CargoContainer, Owner>();

        public static void Clear()
        {
            graphs.Clear();
            API.PipeTypes.ClearChanges();
            owners = new ConditionalWeakTable<CargoContainer, Owner>();
            PipeTopology.Clear();
            PipePools.Clear();
            PipeValves.Clear();
            PipeMachinePorts.Clear();
        }

        public static PipeGraph Bind(CargoTraffic traffic)
        {
            if (!owners.TryGetValue(traffic.container, out var owner))
            {
                lock (owners)
                {
                    if (!owners.TryGetValue(traffic.container, out owner))
                    {
                        owner = new Owner
                        {
                            Traffic = traffic,
                            Graph = graphs.GetOrAdd(traffic.planet.id, key => new PipeGraph()),
                        };
                        owners.Add(traffic.container, owner);
                    }
                }
            }

            var graph = owner.Graph;
            PipePools.Register(traffic.factory, graph);
            return graph;
        }

        public static bool IsPipe(CargoTraffic traffic, int belt)
        {
            return traffic?.factory?.entityPool != null
                && belt > 0
                && belt < traffic.beltPool.Length
                && traffic.beltPool[belt].id == belt
                && PipeTypeRegistry.IsItem(
                    traffic.factory.entityPool[traffic.beltPool[belt].entityId].protoId
                );
        }

        public static bool TryBelt(
            CargoTraffic traffic,
            int belt,
            out PipeGraph graph,
            out int entity
        )
        {
            graph = null;
            entity = 0;
            if (!IsPipe(traffic, belt))
            {
                return false;
            }

            entity = traffic.beltPool[belt].entityId;
            graph = Bind(traffic);
            return true;
        }

        public static bool TryPath(CargoPath path, out PipeGraph graph, out int entity)
        {
            graph = null;
            entity = 0;
            if (
                path?.cargoContainer == null
                || path.belts == null
                || path.belts.Count == 0
                || !owners.TryGetValue(path.cargoContainer, out var owner)
            )
            {
                return false;
            }

            int belt = path.belts[0];
            if (!IsPipe(owner.Traffic, belt))
            {
                return false;
            }

            graph = owner.Graph;
            entity = owner.Traffic.beltPool[belt].entityId;
            return true;
        }

        public static void Sync(CargoTraffic traffic, int belt)
        {
            if (!TryBelt(traffic, belt, out var graph, out int entity))
            {
                return;
            }

            graph.Add(entity);
            var segment = traffic.beltPool[belt];
            var desired = new HashSet<int>();
            if (!PipeTypeRegistry.IsUndirected(traffic.factory.entityPool[entity].protoId))
            {
                for (int slot = 0; slot < 4; slot++)
                {
                    if (
                        PipeConnections.TryReadNativeLink(
                            traffic.factory,
                            entity,
                            slot,
                            out _,
                            out int peer
                        )
                        && peer > 0
                        && IsPipe(traffic, traffic.factory.entityPool[peer].beltId)
                    )
                    {
                        desired.Add(peer);
                    }
                }
            }
            else if (UsesObjectLinks(traffic, entity))
            {
                foreach (int other in PipeTopology.Neighbors(traffic.factory, entity))
                {
                    if (
                        other > 0
                        && IsPipeObject(traffic.factory, other)
                        && IsPipe(traffic, traffic.factory.entityPool[other].beltId)
                        && PipeTypeRegistry.Compatible(
                            traffic.factory.entityPool[entity].protoId,
                            traffic.factory.entityPool[other].protoId
                        )
                    )
                    {
                        desired.Add(other);
                    }
                }
            }
            else
            {
                foreach (
                    int other in new[]
                    {
                        segment.outputId,
                        segment.backInputId,
                        segment.leftInputId,
                        segment.rightInputId,
                    }
                )
                {
                    if (
                        IsPipe(traffic, other)
                        && PipeTypeRegistry.Compatible(
                            traffic.factory.entityPool[entity].protoId,
                            traffic.factory.entityPool[traffic.beltPool[other].entityId].protoId
                        )
                    )
                    {
                        desired.Add(traffic.beltPool[other].entityId);
                    }
                }
            }

            foreach (int existing in graph.Links(entity))
            {
                if (!desired.Contains(existing))
                {
                    graph.Disconnect(entity, existing);
                }
            }

            foreach (int other in desired)
            {
                graph.Add(other);
                graph.Connect(entity, other);
            }
        }

        public static bool CanLink(PlanetFactory factory, int firstEntityId, int secondEntityId)
        {
            if (firstEntityId == 0 || secondEntityId == 0)
            {
                return true;
            }

            int firstPrototypeId = Proto(factory, firstEntityId);
            int secondPrototypeId = Proto(factory, secondEntityId);
            if (!CanLinkPrototypes(firstPrototypeId, secondPrototypeId))
            {
                return false;
            }

            if (
                firstEntityId > 0
                && secondEntityId > 0
                && PipeTypeRegistry.IsInventory(firstPrototypeId)
                && PipeTypeRegistry.IsInventory(secondPrototypeId)
            )
            {
                var graph = Bind(factory.cargoTraffic);
                return graph.CanConnect(firstEntityId, secondEntityId)
                    && PipePools.CanConnect(graph, firstEntityId, secondEntityId);
            }
            return true;
        }

        internal static bool CanLinkPrototypes(int firstPrototypeId, int secondPrototypeId)
        {
            bool firstIsPipe = PipeTypeRegistry.IsItem(firstPrototypeId);
            bool secondIsPipe = PipeTypeRegistry.IsItem(secondPrototypeId);
            if (
                Prototypes.ValveId > 0
                && (
                    firstPrototypeId == Prototypes.ValveId
                    || secondPrototypeId == Prototypes.ValveId
                )
            )
            {
                return firstIsPipe && PipeTypeRegistry.IsInventory(firstPrototypeId)
                    || secondIsPipe && PipeTypeRegistry.IsInventory(secondPrototypeId);
            }

            if (!firstIsPipe && !secondIsPipe)
            {
                return true;
            }

            if (firstIsPipe && secondIsPipe)
            {
                return PipeTypeRegistry.Compatible(firstPrototypeId, secondPrototypeId);
            }

            if (!PipeTypeRegistry.IsInventory(firstIsPipe ? firstPrototypeId : secondPrototypeId))
            {
                return false;
            }

            int machinePrototypeId = firstIsPipe ? secondPrototypeId : firstPrototypeId;
            var descriptor = LDB.items.Select(machinePrototypeId)?.prefabDesc;
            if (
                descriptor == null
                || descriptor.isBelt
                || descriptor.isSplitter
                || descriptor.isPiler
                || descriptor.isSpraycoster
                || descriptor.isMonitor
            )
            {
                return false;
            }

            return descriptor.isInserter
                || descriptor.isTank
                || PipeMachinePorts.Enabled
                    && descriptor.isAssembler
                    && descriptor.slotPoses?.Length > 0
                || descriptor.isStation
                || descriptor.minerType != EMinerType.None
                || descriptor.isFractionator;
        }

        internal static bool HasInventory(PlanetFactory factory, int entityId) =>
            PipeTypeRegistry.IsInventory(Proto(factory, entityId));

        public static bool IsPipeObject(PlanetFactory factory, int entityId) =>
            PipeTypeRegistry.IsItem(Proto(factory, entityId));

        public static void UseObjectLinks(CargoTraffic traffic, int entity)
        {
            Bind(traffic);
            if (!PipeTypeRegistry.IsUndirected(traffic.factory.entityPool[entity].protoId))
            {
                return;
            }
            if (!PipeTopology.Contains(traffic.factory, entity))
            {
                PipeTopology.Register(traffic.factory, entity);
                for (int slot = 0; slot < 4; slot++)
                {
                    traffic.factory.ReadObjectConn(
                        entity,
                        slot,
                        out _,
                        out int other,
                        out int otherSlot
                    );
                    if (
                        !IsPipeObject(traffic.factory, other)
                        || !PipeTypeRegistry.Compatible(
                            traffic.factory.entityPool[entity].protoId,
                            Proto(traffic.factory, other)
                        )
                    )
                    {
                        continue;
                    }

                    traffic.factory.ReadObjectConn(
                        other,
                        otherSlot,
                        out _,
                        out int back,
                        out int backSlot
                    );
                    if (back == entity && backSlot == slot)
                    {
                        PipeTopology.Connect(traffic.factory, entity, other);
                    }
                }
            }
        }

        public static bool UsesObjectLinks(CargoTraffic traffic, int entity) =>
            PipeTypeRegistry.IsUndirected(Proto(traffic.factory, entity))
            && PipeTopology.Contains(traffic.factory, entity);

        internal static int ObjectPrototype(PlanetFactory factory, int entityId) =>
            Proto(factory, entityId);

        private static int Proto(PlanetFactory factory, int entityId)
        {
            if (factory == null)
            {
                return 0;
            }

            if (
                entityId > 0
                && factory.entityPool != null
                && entityId < factory.entityPool.Length
                && factory.entityPool[entityId].id == entityId
            )
            {
                return factory.entityPool[entityId].protoId;
            }

            if (
                entityId < 0
                && factory.prebuildPool != null
                && -entityId < factory.prebuildPool.Length
                && factory.prebuildPool[-entityId].id == -entityId
            )
            {
                return factory.prebuildPool[-entityId].protoId;
            }

            return 0;
        }

        public static void Return(Player player, int entity, FluidAmount amount)
        {
            if (player == null || amount.Count == 0)
            {
                return;
            }

            player.TryAddItemToPackage(
                amount.Item,
                checked((int)amount.Count),
                checked((int)amount.ProliferatorPoints),
                true,
                entity
            );
        }

        public static void Export(BinaryWriter writer)
        {
            writer.Write(6);
            var entries = graphs.OrderBy(x => x.Key).ToArray();
            writer.Write(entries.Length);
            foreach (var entry in entries)
            {
                writer.Write(entry.Key);
                entry.Value.Export(writer);
            }
            PipeTopology.Export(writer);
            PipeValves.Export(writer);
            PipeMachinePorts.Export(writer);
        }

        public static void Import(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version < 1 || version > 6)
            {
                throw new InvalidDataException("Unsupported fluid pipe save.");
            }

            int size = reader.ReadInt32();
            if (size < 0 || size > 100000)
            {
                throw new InvalidDataException("Invalid planet count.");
            }

            var loaded = new ConcurrentDictionary<int, PipeGraph>();
            for (int index = 0; index < size; index++)
            {
                int planet = reader.ReadInt32();
                if (planet <= 0 || !loaded.TryAdd(planet, PipeGraph.Import(reader)))
                {
                    throw new InvalidDataException("Invalid pipe planet.");
                }
            }
            if (version >= 2)
            {
                PipeTopology.Import(reader);
            }
            else
            {
                PipeTopology.Clear();
            }

            if (version >= 4)
            {
                PipeValves.Import(reader, version >= 6);
            }
            else
            {
                PipeValves.Clear();
            }

            if (version >= 5)
            {
                PipeMachinePorts.Import(reader);
            }
            else
            {
                PipeMachinePorts.Clear();
            }

            PipePools.Clear();
            graphs = loaded;
            owners = new ConditionalWeakTable<CargoContainer, Owner>();
            if (GameMain.data?.factories != null)
            {
                foreach (var factory in GameMain.data.factories)
                {
                    if (factory?.cargoTraffic?.container != null)
                    {
                        var traffic = factory.cargoTraffic;
                        Bind(traffic);
                        PipeConnections.RepairImported(factory, version == 2);
                        ValvePower.Restore(factory);
                        PipeMachinePorts.Restore(factory);
                        for (int index2 = 1; index2 < traffic.beltCursor; index2++)
                        {
                            if (IsPipe(traffic, index2))
                            {
                                int entityId = traffic.beltPool[index2].entityId;
                                UseObjectLinks(traffic, entityId);
                                Sync(traffic, index2);
                                API.PipeTypes.Changed(factory, entityId);
                            }
                        }
                    }
                }
            }
        }
    }
}
