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
            owners = new ConditionalWeakTable<CargoContainer, Owner>();
            PipeTopology.Clear();
            PipePools.Clear();
            PipeValves.Clear();
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
                && traffic.factory.entityPool[traffic.beltPool[belt].entityId].protoId
                    == Prototypes.ItemId;
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
            if (UsesObjectLinks(traffic, entity))
            {
                foreach (int other in PipeTopology.Neighbors(traffic.factory, entity))
                {
                    if (other > 0 && IsPipeObject(traffic.factory, other))
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
                    if (IsPipe(traffic, other))
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
            bool pipeA = firstPrototypeId == Prototypes.ItemId;
            bool pipeB = secondPrototypeId == Prototypes.ItemId;
            if (firstPrototypeId == Prototypes.ValveId || secondPrototypeId == Prototypes.ValveId)
            {
                return pipeA || pipeB;
            }

            if (!pipeA && !pipeB)
            {
                return true;
            }

            if (pipeA && pipeB)
            {
                return firstEntityId <= 0
                    || secondEntityId <= 0
                    || Bind(factory.cargoTraffic).CanConnect(firstEntityId, secondEntityId)
                        && PipePools.CanConnect(
                            Bind(factory.cargoTraffic),
                            firstEntityId,
                            secondEntityId
                        );
            }

            int proto = pipeA ? secondPrototypeId : firstPrototypeId;
            if (proto == Prototypes.ValveId)
            {
                return true;
            }

            var desc = LDB.items.Select(proto)?.prefabDesc;
            if (
                desc == null
                || desc.isBelt
                || desc.isSplitter
                || desc.isPiler
                || desc.isSpraycoster
                || desc.isMonitor
            )
            {
                return false;
            }

            if (desc.isInserter)
            {
                return true;
            }

            return desc.isTank
                || desc.isStation
                || desc.minerType != EMinerType.None
                || desc.isFractionator;
        }

        public static bool IsPipeObject(PlanetFactory factory, int entityId) =>
            Proto(factory, entityId) == Prototypes.ItemId;

        public static void UseObjectLinks(CargoTraffic traffic, int entity)
        {
            Bind(traffic);
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
                    if (!IsPipeObject(traffic.factory, other))
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
            PipeTopology.Contains(traffic.factory, entity);

        private static int Proto(PlanetFactory factory, int entityId)
        {
            if (
                entityId > 0
                && entityId < factory.entityPool.Length
                && factory.entityPool[entityId].id == entityId
            )
            {
                return factory.entityPool[entityId].protoId;
            }

            if (
                entityId < 0
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
            writer.Write(4);
            var entries = graphs.OrderBy(x => x.Key).ToArray();
            writer.Write(entries.Length);
            foreach (var entry in entries)
            {
                writer.Write(entry.Key);
                entry.Value.Export(writer);
            }
            PipeTopology.Export(writer);
            PipeValves.Export(writer);
        }

        public static void Import(BinaryReader reader)
        {
            int version = reader.ReadInt32();
            if (version < 1 || version > 4)
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
                PipeValves.Import(reader);
            }
            else
            {
                PipeValves.Clear();
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
                        for (int index2 = 1; index2 < traffic.beltCursor; index2++)
                        {
                            if (IsPipe(traffic, index2))
                            {
                                UseObjectLinks(traffic, traffic.beltPool[index2].entityId);
                            }
                        }
                    }
                }
            }
        }
    }
}
