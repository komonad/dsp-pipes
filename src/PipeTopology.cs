using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FluidPipes
{
    internal static class PipeTopology
    {
        private static readonly Dictionary<int, Dictionary<int, HashSet<int>>> planets =
            new Dictionary<int, Dictionary<int, HashSet<int>>>();

        private static Dictionary<int, HashSet<int>> Planet(PlanetFactory factory)
        {
            int planet = factory.planetId;
            if (!planets.TryGetValue(planet, out var nodes))
            {
                planets.Add(planet, nodes = new Dictionary<int, HashSet<int>>());
            }

            return nodes;
        }

        public static void Clear()
        {
            planets.Clear();
        }

        public static bool Contains(PlanetFactory factory, int entityId) =>
            Planet(factory).ContainsKey(entityId);

        public static void Register(PlanetFactory factory, int entityId)
        {
            var nodes = Planet(factory);
            if (!nodes.ContainsKey(entityId))
            {
                nodes.Add(entityId, new HashSet<int>());
            }
        }

        public static int[] Neighbors(PlanetFactory factory, int entityId) =>
            Planet(factory).TryGetValue(entityId, out var links) ? links.ToArray() : new int[0];

        public static bool Adjacent(PlanetFactory factory, int a, int b) =>
            Planet(factory).TryGetValue(a, out var links) && links.Contains(b);

        public static bool Compatible(PlanetFactory factory, int a, int b)
        {
            int fluid = 0;
            var nodes = Planet(factory);
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(a);
            queue.Enqueue(b);
            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            while (queue.Count > 0)
            {
                int entityId = queue.Dequeue();
                if (!seen.Add(entityId))
                {
                    continue;
                }

                if (entityId > 0)
                {
                    int item = PipePools.Query(graph, entityId).Item;
                    if (item != 0)
                    {
                        if (fluid != 0 && fluid != item)
                        {
                            return false;
                        }
                        fluid = item;
                    }
                }
                else if (nodes.TryGetValue(entityId, out var peers))
                {
                    foreach (int peer in peers)
                    {
                        queue.Enqueue(peer);
                    }
                }
            }
            return true;
        }

        public static void Connect(PlanetFactory factory, int firstEntityId, int secondEntityId)
        {
            Register(factory, firstEntityId);
            Register(factory, secondEntityId);
            var nodes = Planet(factory);
            nodes[firstEntityId].Add(secondEntityId);
            nodes[secondEntityId].Add(firstEntityId);
        }

        public static void Disconnect(PlanetFactory factory, int firstEntityId, int secondEntityId)
        {
            var nodes = Planet(factory);
            if (nodes.TryGetValue(firstEntityId, out var x))
            {
                x.Remove(secondEntityId);
            }

            if (nodes.TryGetValue(secondEntityId, out var y))
            {
                y.Remove(firstEntityId);
            }
        }

        public static int[] Remove(PlanetFactory factory, int entityId)
        {
            var nodes = Planet(factory);
            var peers = Neighbors(factory, entityId);
            foreach (int other in peers)
            {
                if (nodes.TryGetValue(other, out var links))
                {
                    links.Remove(entityId);
                }
            }

            nodes.Remove(entityId);
            return peers;
        }

        public static void Move(PlanetFactory factory, int oldId, int newId)
        {
            if (oldId == newId)
            {
                return;
            }

            var nodes = Planet(factory);
            if (!nodes.TryGetValue(oldId, out var links))
            {
                return;
            }

            Register(factory, newId);
            foreach (int other in links.ToArray())
            {
                if (nodes.TryGetValue(other, out var peers))
                {
                    peers.Remove(oldId);
                    peers.Add(newId);
                }
                nodes[newId].Add(other);
            }
            nodes.Remove(oldId);
        }

        public static void Export(BinaryWriter writer)
        {
            writer.Write(planets.Count);
            foreach (var planet in planets.OrderBy(x => x.Key))
            {
                writer.Write(planet.Key);
                writer.Write(planet.Value.Count);
                foreach (var node in planet.Value.OrderBy(x => x.Key))
                {
                    writer.Write(node.Key);
                    var edges = node.Value.Where(x => x > node.Key).OrderBy(x => x).ToArray();
                    writer.Write(edges.Length);
                    foreach (int other in edges)
                    {
                        writer.Write(other);
                    }
                }
            }
        }

        public static void Import(BinaryReader reader)
        {
            var loaded = new Dictionary<int, Dictionary<int, HashSet<int>>>();
            int count = reader.ReadInt32();
            if (count < 0 || count > 100000)
            {
                throw new InvalidDataException("Invalid pipe topology planet count.");
            }

            for (int index = 0; index < count; index++)
            {
                int planet = reader.ReadInt32();
                int size = reader.ReadInt32();
                if (planet <= 0 || loaded.ContainsKey(planet) || size < 0 || size > 2000000)
                {
                    throw new InvalidDataException("Invalid pipe topology size.");
                }

                var nodes = new Dictionary<int, HashSet<int>>();
                var edges = new List<Tuple<int, int>>();
                for (int otherIndex = 0; otherIndex < size; otherIndex++)
                {
                    int entityId = reader.ReadInt32();
                    int degree = reader.ReadInt32();
                    if (
                        entityId == 0
                        || nodes.ContainsKey(entityId)
                        || degree < 0
                        || degree >= size
                    )
                    {
                        throw new InvalidDataException("Invalid pipe topology node.");
                    }

                    nodes.Add(entityId, new HashSet<int>());
                    for (int innerIndex = 0; innerIndex < degree; innerIndex++)
                    {
                        edges.Add(Tuple.Create(entityId, reader.ReadInt32()));
                    }
                }
                foreach (var edge in edges)
                {
                    if (
                        edge.Item2 <= edge.Item1
                        || !nodes.ContainsKey(edge.Item2)
                        || !nodes[edge.Item1].Add(edge.Item2)
                    )
                    {
                        throw new InvalidDataException("Invalid pipe topology edge.");
                    }
                    else
                    {
                        nodes[edge.Item2].Add(edge.Item1);
                    }
                }

                loaded.Add(planet, nodes);
            }
            planets.Clear();
            foreach (var entry in loaded)
            {
                planets.Add(entry.Key, entry.Value);
            }
        }
    }
}
