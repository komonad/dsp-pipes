using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace FluidPipes
{
    internal struct FluidAmount
    {
        public int Item;
        public long Count;
        public long ProliferatorPoints;

        public FluidAmount(int item, long count, long proliferatorPoints)
        {
            Item = item;
            Count = count;
            ProliferatorPoints = proliferatorPoints;
        }
    }

    internal struct PipeView
    {
        public int Item;
        public int Nodes;
        public int Network;
        public long Count;
        public long ProliferatorPoints;
        public long Capacity;
    }

    internal sealed class PipeGraph
    {
        public const int SegmentCapacity = 10;

        // 图格式 1 包含按每节点 100 容量写入的存档。
        private const int HistoricalSegmentCapacity = 100;

        private sealed class Network
        {
            public readonly object Sync = new object();
            public readonly HashSet<int> Members = new HashSet<int>();
            public int Item;
            public int Anchor;
            public long Count;
            public long ProliferatorPoints;
        }

        private sealed class Node
        {
            public Network Net;
            public readonly HashSet<int> Links = new HashSet<int>();
        }

        private readonly Dictionary<int, Node> nodes = new Dictionary<int, Node>();
        private readonly HashSet<Network> networks = new HashSet<Network>();
        private readonly ReaderWriterLockSlim topology = new ReaderWriterLockSlim();
        private int networkSerial;
        public long TopologyVisits { get; private set; }
        public long TopologyVersion { get; private set; }
        public int NetworkCount
        {
            get
            {
                topology.EnterReadLock();
                try
                {
                    return networks.Count;
                }
                finally
                {
                    topology.ExitReadLock();
                }
            }
        }

        public void Add(int nodeId)
        {
            topology.EnterWriteLock();
            try
            {
                if (nodeId <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(nodeId));
                }

                if (nodes.ContainsKey(nodeId))
                {
                    return;
                }

                var network = new Network { Anchor = ++networkSerial };
                network.Members.Add(nodeId);
                TopologyVersion++;
                networks.Add(network);
                nodes.Add(nodeId, new Node { Net = network });
            }
            finally
            {
                topology.ExitWriteLock();
            }
        }

        public bool CanConnect(int firstNodeId, int secondNodeId)
        {
            topology.EnterWriteLock();
            try
            {
                return !nodes.TryGetValue(firstNodeId, out var firstNode)
                    || !nodes.TryGetValue(secondNodeId, out var secondNode)
                    || Compatible(firstNode.Net, secondNode.Net);
            }
            finally
            {
                topology.ExitWriteLock();
            }
        }

        private static bool Compatible(Network firstNetwork, Network secondNetwork) =>
            firstNetwork == secondNetwork
            || firstNetwork.Count == 0
            || secondNetwork.Count == 0
            || firstNetwork.Item == secondNetwork.Item;

        public bool Connect(int firstNodeId, int secondNodeId)
        {
            topology.EnterWriteLock();
            try
            {
                if (
                    firstNodeId == secondNodeId
                    || !nodes.TryGetValue(firstNodeId, out var firstNode)
                    || !nodes.TryGetValue(secondNodeId, out var secondNode)
                )
                {
                    return false;
                }

                if (!Compatible(firstNode.Net, secondNode.Net))
                {
                    return false;
                }

                if (firstNode.Links.Add(secondNodeId))
                {
                    TopologyVersion++;
                }

                secondNode.Links.Add(firstNodeId);
                var large = firstNode.Net;
                var small = secondNode.Net;
                if (large == small)
                {
                    return true;
                }

                if (large.Members.Count < small.Members.Count)
                {
                    var tmp = large;
                    large = small;
                    small = tmp;
                }
                foreach (int member in small.Members)
                {
                    nodes[member].Net = large;
                    large.Members.Add(member);
                    TopologyVisits++;
                }
                if (large.Count == 0)
                {
                    large.Item = small.Item;
                }

                large.Anchor = Math.Min(large.Anchor, small.Anchor);
                large.Count += small.Count;
                large.ProliferatorPoints += small.ProliferatorPoints;
                networks.Remove(small);
                return true;
            }
            finally
            {
                topology.ExitWriteLock();
            }
        }

        public void Disconnect(int firstNodeId, int secondNodeId)
        {
            topology.EnterWriteLock();
            try
            {
                if (
                    !nodes.TryGetValue(firstNodeId, out var firstNode)
                    || !firstNode.Links.Remove(secondNodeId)
                )
                {
                    return;
                }

                TopologyVersion++;
                nodes[secondNodeId].Links.Remove(firstNodeId);
                if (firstNode.Links.Count == 0 || nodes[secondNodeId].Links.Count == 0)
                {
                    int single =
                        firstNode.Links.Count == 0 && nodes[secondNodeId].Links.Count == 0
                            ? Math.Min(firstNodeId, secondNodeId)
                        : firstNode.Links.Count == 0 ? firstNodeId
                        : secondNodeId;
                    Detach(single);
                }
                else
                {
                    Repartition(firstNode.Net);
                }
            }
            finally
            {
                topology.ExitWriteLock();
            }
        }

        public FluidAmount Remove(int nodeId)
        {
            topology.EnterWriteLock();
            try
            {
                if (!nodes.TryGetValue(nodeId, out var node))
                {
                    return default(FluidAmount);
                }

                TopologyVersion++;
                var old = node.Net;
                bool leaf = node.Links.Count <= 1;
                long share = old.Count / old.Members.Count;
                long proliferatorPoints = Portion(old.ProliferatorPoints, share, old.Count);
                var refund = new FluidAmount(old.Item, share, proliferatorPoints);
                old.Count -= share;
                old.ProliferatorPoints -= proliferatorPoints;
                foreach (int link in node.Links)
                {
                    nodes[link].Links.Remove(nodeId);
                }

                old.Members.Remove(nodeId);
                nodes.Remove(nodeId);
                if (!leaf)
                {
                    Repartition(old);
                }
                else if (old.Count == 0)
                {
                    old.Item = 0;
                }

                if (old.Members.Count == 0)
                {
                    networks.Remove(old);
                }

                return refund;
            }
            finally
            {
                topology.ExitWriteLock();
            }
        }

        private void Detach(int nodeId)
        {
            var node = nodes[nodeId];
            var old = node.Net;
            long count = old.Count / old.Members.Count;
            long proliferatorPoints = Portion(old.ProliferatorPoints, count, old.Count);
            var part = new Network
            {
                Anchor = ++networkSerial,
                Item = count == 0 ? 0 : old.Item,
                Count = count,
                ProliferatorPoints = proliferatorPoints,
            };
            part.Members.Add(nodeId);
            networks.Add(part);
            old.Members.Remove(nodeId);
            old.Count -= count;
            old.ProliferatorPoints -= proliferatorPoints;
            if (old.Count == 0)
            {
                old.Item = 0;
            }

            node.Net = part;
        }

        // 整数余量保留在后续分量中；总数量与增产点守恒。
        private void Repartition(Network old)
        {
            networks.Remove(old);
            var unseen = new HashSet<int>(old.Members);
            var components = new List<Network>();
            var queue = new Queue<int>();
            foreach (int seed in old.Members.OrderBy(x => x))
            {
                if (!unseen.Remove(seed))
                {
                    continue;
                }

                var part = new Network { Item = old.Item, Anchor = ++networkSerial };
                queue.Enqueue(seed);
                while (queue.Count != 0)
                {
                    int nodeId = queue.Dequeue();
                    part.Members.Add(nodeId);
                    TopologyVisits++;
                    foreach (int link in nodes[nodeId].Links)
                    {
                        if (unseen.Remove(link))
                        {
                            queue.Enqueue(link);
                        }
                    }
                }
                components.Add(part);
            }
            long count = old.Count;
            long proliferatorPoints = old.ProliferatorPoints;
            int members = old.Members.Count;
            foreach (var part in components)
            {
                networks.Add(part);
                part.Count = Portion(count, part.Members.Count, members);
                part.ProliferatorPoints = Portion(proliferatorPoints, part.Count, count);
                count -= part.Count;
                proliferatorPoints -= part.ProliferatorPoints;
                members -= part.Members.Count;
                if (part.Count == 0)
                {
                    part.Item = 0;
                }

                foreach (int nodeId2 in part.Members)
                {
                    nodes[nodeId2].Net = part;
                }
            }
        }

        private static long Portion(long total, long take, long count)
        {
            if (count == 0 || take == 0)
            {
                return 0;
            }

            return total / count * take + total % count * take / count;
        }

        public bool Insert(int nodeId, int item, int count, int proliferatorPoints)
        {
            if (
                item <= 0
                || count <= 0
                || proliferatorPoints < 0
                || proliferatorPoints > (long)count * 10
            )
            {
                return false;
            }

            topology.EnterReadLock();
            try
            {
                if (!nodes.TryGetValue(nodeId, out var node))
                {
                    return false;
                }

                var network = node.Net;
                lock (network.Sync)
                {
                    if (
                        network.Count != 0 && network.Item != item
                        || network.Count + count > (long)network.Members.Count * SegmentCapacity
                    )
                    {
                        return false;
                    }

                    network.Item = item;
                    network.Count += count;
                    network.ProliferatorPoints += proliferatorPoints;
                    return true;
                }
            }
            finally
            {
                topology.ExitReadLock();
            }
        }

        public FluidAmount Take(int nodeId, int max, int filter = 0, int[] needs = null)
        {
            if (max <= 0)
            {
                return default(FluidAmount);
            }

            topology.EnterReadLock();
            try
            {
                if (!nodes.TryGetValue(nodeId, out var node))
                {
                    return default(FluidAmount);
                }

                var network = node.Net;
                lock (network.Sync)
                {
                    if (
                        network.Count == 0
                        || filter != 0 && filter != network.Item
                        || needs != null && Array.IndexOf(needs, network.Item) < 0
                    )
                    {
                        return default(FluidAmount);
                    }

                    long count = Math.Min(max, network.Count);

                    long proliferatorPoints = Portion(
                        network.ProliferatorPoints,
                        count,
                        network.Count
                    );
                    var result = new FluidAmount(network.Item, count, proliferatorPoints);
                    network.Count -= count;
                    network.ProliferatorPoints -= proliferatorPoints;
                    if (network.Count == 0)
                    {
                        network.Item = 0;
                    }

                    return result;
                }
            }
            finally
            {
                topology.ExitReadLock();
            }
        }

        public PipeView View(int nodeId)
        {
            topology.EnterReadLock();
            try
            {
                if (!nodes.TryGetValue(nodeId, out var node))
                {
                    return default(PipeView);
                }

                var network = node.Net;
                lock (network.Sync)
                {
                    return new PipeView
                    {
                        Item = network.Item,
                        Network = network.Anchor,
                        Count = network.Count,
                        ProliferatorPoints = network.ProliferatorPoints,
                        Nodes = network.Members.Count,
                        Capacity = (long)network.Members.Count * SegmentCapacity,
                    };
                }
            }
            finally
            {
                topology.ExitReadLock();
            }
        }

        public int[] Links(int nodeId)
        {
            topology.EnterReadLock();
            try
            {
                return nodes.TryGetValue(nodeId, out var node) ? node.Links.ToArray() : new int[0];
            }
            finally
            {
                topology.ExitReadLock();
            }
        }

        public int[] Members(int nodeId)
        {
            topology.EnterReadLock();
            try
            {
                return nodes.TryGetValue(nodeId, out var node)
                    ? node.Net.Members.ToArray()
                    : new int[0];
            }
            finally
            {
                topology.ExitReadLock();
            }
        }

        public int CopyLinks(int nodeId, List<int> destination) =>
            CopyTopology(nodeId, destination, false);

        public int CopyMembers(int nodeId, List<int> destination) =>
            CopyTopology(nodeId, destination, true);

        private int CopyTopology(int nodeId, List<int> destination, bool component)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            destination.Clear();
            topology.EnterReadLock();
            try
            {
                if (nodes.TryGetValue(nodeId, out var node))
                {
                    HashSet<int> source = component ? node.Net.Members : node.Links;
                    foreach (int entityId in source)
                    {
                        destination.Add(entityId);
                    }
                }
                return destination.Count;
            }
            finally
            {
                topology.ExitReadLock();
            }
        }

        public int[] Representatives()
        {
            topology.EnterReadLock();
            try
            {
                return networks.Select(network => network.Members.First()).ToArray();
            }
            finally
            {
                topology.ExitReadLock();
            }
        }

        public void SetInventory(int nodeId, int item, long count, long proliferatorPoints)
        {
            topology.EnterReadLock();
            try
            {
                if (!nodes.TryGetValue(nodeId, out var node))
                {
                    return;
                }

                var network = node.Net;
                lock (network.Sync)
                {
                    // 导入的超容量库存可以保留或减少，新增库存受标称容量限制。
                    if (
                        count < 0
                        || count
                            > Math.Max((long)network.Members.Count * SegmentCapacity, network.Count)
                        || proliferatorPoints < 0
                        || proliferatorPoints > count * 10
                        || count > 0 && item <= 0
                    )
                    {
                        throw new ArgumentOutOfRangeException(nameof(count));
                    }

                    network.Item = count == 0 ? 0 : item;
                    network.Count = count;
                    network.ProliferatorPoints = proliferatorPoints;
                }
            }
            finally
            {
                topology.ExitReadLock();
            }
        }

        public void Export(BinaryWriter writer)
        {
            topology.EnterWriteLock();
            try
            {
                writer.Write(1);
                writer.Write(nodes.Count);
                foreach (var entry in nodes.OrderBy(x => x.Key))
                {
                    writer.Write(entry.Key);
                    var edges = entry
                        .Value.Links.Where(x => x > entry.Key)
                        .OrderBy(x => x)
                        .ToArray();
                    writer.Write(edges.Length);
                    foreach (int link in edges)
                    {
                        writer.Write(link);
                    }
                }
                var nets = nodes
                    .Values.Select(firstNode => firstNode.Net)
                    .Distinct()
                    .OrderBy(firstNetwork => firstNetwork.Members.Min())
                    .ToArray();
                writer.Write(nets.Length);
                foreach (var network in nets)
                {
                    writer.Write(network.Members.Min());
                    writer.Write(network.Item);
                    writer.Write(network.Count);
                    writer.Write(network.ProliferatorPoints);
                }
            }
            finally
            {
                topology.ExitWriteLock();
            }
        }

        public static PipeGraph Import(BinaryReader reader)
        {
            if (reader.ReadInt32() != 1)
            {
                throw new InvalidDataException("Unsupported pipe graph version.");
            }

            int size = reader.ReadInt32();
            if (size < 0 || size > 2000000)
            {
                throw new InvalidDataException("Invalid pipe graph size.");
            }

            var graph = new PipeGraph();
            var edges = new List<Tuple<int, int>>();
            for (int index = 0; index < size; index++)
            {
                int nodeId = reader.ReadInt32();
                if (nodeId <= 0 || graph.nodes.ContainsKey(nodeId))
                {
                    throw new InvalidDataException("Invalid pipe node.");
                }

                graph.Add(nodeId);
                int count = reader.ReadInt32();
                if (count < 0 || count >= size)
                {
                    throw new InvalidDataException("Invalid pipe links.");
                }

                for (int otherIndex = 0; otherIndex < count; otherIndex++)
                {
                    edges.Add(Tuple.Create(nodeId, reader.ReadInt32()));
                }
            }
            foreach (var edge in edges)
            {
                if (edge.Item2 <= edge.Item1 || !graph.Connect(edge.Item1, edge.Item2))
                {
                    throw new InvalidDataException("Invalid pipe edge.");
                }
            }

            int networks = reader.ReadInt32();
            var visited = new HashSet<Network>();
            if (networks < 0 || networks > size)
            {
                throw new InvalidDataException("Invalid network count.");
            }

            for (int index2 = 0; index2 < networks; index2++)
            {
                int nodeId2 = reader.ReadInt32();
                int item = reader.ReadInt32();
                long count = reader.ReadInt64();
                long proliferatorPoints = reader.ReadInt64();
                if (
                    !graph.nodes.TryGetValue(nodeId2, out var node)
                    || !visited.Add(node.Net)
                    || count < 0
                    || count
                        > (long)node.Net.Members.Count
                            * Math.Max(SegmentCapacity, HistoricalSegmentCapacity)
                    || proliferatorPoints < 0
                    || proliferatorPoints > count * 10
                    || (count == 0 ? item != 0 || proliferatorPoints != 0 : item <= 0)
                )
                {
                    throw new InvalidDataException("Invalid fluid inventory.");
                }

                node.Net.Item = item;
                node.Net.Count = count;
                node.Net.ProliferatorPoints = proliferatorPoints;
            }
            if (
                visited.Count
                != graph.nodes.Values.Select(firstNode => firstNode.Net).Distinct().Count()
            )
            {
                throw new InvalidDataException("Missing pipe inventory.");
            }

            return graph;
        }
    }
}
