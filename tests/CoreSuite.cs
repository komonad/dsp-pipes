using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FluidPipes.Tests
{
    public static class CoreSuite
    {
        private static void Check(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
        }

        private static PipeGraph Chain(int size)
        {
            var graph = new PipeGraph();
            for (int index = 1; index <= size; index++)
            {
                graph.Add(index);
                if (index > 1)
                    Check(graph.Connect(index - 1, index), "chain connection");
            }
            return graph;
        }

        public static string Run(bool million)
        {
            var report = new StringBuilder();
            AdmissionPolicy();
            report.AppendLine(
                "PASS item admission union, exclusion precedence, ID parsing and snapshot isolation"
            );
            var graph = Chain(3);
            Check(graph.Insert(1, 1000, 299, 1196), "fill");
            Check(
                !graph.Insert(3, 1001, 1, 0) && !graph.Insert(3, 1000, 2, 0),
                "type and capacity rejection"
            );
            Check(
                graph.Take(2, 4, 1001).Count == 0 && graph.Take(2, 4, 0, new[] { 1001 }).Count == 0,
                "filters"
            );
            var removed = graph.Remove(2);
            var left = graph.View(1);
            var right = graph.View(3);
            Check(
                left.Nodes == 1 && right.Nodes == 1 && left.Count <= 100 && right.Count <= 100,
                "split capacity"
            );
            Check(
                left.Count + right.Count + removed.Count == 299
                    && left.ProliferatorPoints
                        + right.ProliferatorPoints
                        + removed.ProliferatorPoints
                        == 1196,
                "remove conservation"
            );
            Check(
                graph.Take(1, 1000).Count == left.Count && graph.Insert(1, 1001, 10, 40),
                "empty network changes type"
            );
            Check(!graph.Connect(1, 3), "incompatible merge rejection");
            graph.Take(1, 1000);
            Check(graph.Connect(1, 3), "empty merge");
            RoundTrip(graph, new[] { 1, 3 });
            report.AppendLine(
                "PASS capacity, filters, fluid lock, empty reset, split/merge, proliferation, serialization"
            );
            Randomized();
            report.AppendLine(
                "PASS 20000 randomized operations with independent connectivity and inventory oracle"
            );
            Concurrent();
            report.AppendLine(
                "PASS 8-worker conservation, topology changes during I/O, competing first fluids"
            );
            var pruning = Chain(100000);
            pruning.Insert(1, 1000, 100000, 400000);
            long beforePruning = pruning.TopologyVisits;
            var pruningWatch = Stopwatch.StartNew();
            long refunded = 0;
            long refundPoints = 0;
            for (int entityId = 1; entityId <= 10000; entityId++)
            {
                var amount = pruning.Remove(entityId);
                refunded += amount.Count;
                refundPoints += amount.ProliferatorPoints;
            }
            pruningWatch.Stop();
            var remainder = pruning.View(100000);
            Check(
                pruning.TopologyVisits == beforePruning
                    && remainder.Nodes == 90000
                    && remainder.Count + refunded == 100000
                    && remainder.ProliferatorPoints + refundPoints == 400000,
                "leaf dismantling avoids component traversal and conserves inventory"
            );
            pruning.Add(1);
            Check(
                pruning.View(1).Network != remainder.Network,
                "recycled node has a distinct network identity"
            );
            beforePruning = pruning.TopologyVisits;
            pruning.Disconnect(99999, 100000);
            Check(
                pruning.TopologyVisits == beforePruning
                    && pruning.View(100000).Nodes == 1
                    && pruning.View(99999).Nodes == 89999,
                "leaf edge cut avoids component traversal"
            );
            report.AppendLine(
                "BENCH endpoint_removals=10000 initial_nodes=100000 ms="
                    + pruningWatch.Elapsed.TotalMilliseconds.ToString("F3")
                    + " topology_visits=0"
            );
            foreach (
                int size in million
                    ? new[] { 10, 1000, 100000, 1000000 }
                    : new[] { 10, 1000, 100000 }
            )
            {
                GC.Collect();
                var timer = Stopwatch.StartNew();
                var network = Chain(size);
                timer.Stop();
                double build = timer.Elapsed.TotalMilliseconds;
                var samples = new List<double>();
                const int iterations = 200000;
                for (int otherIndex = 0; otherIndex < 10000; otherIndex++)
                {
                    network.Insert(1, 1000, 1, 4);
                    network.Take(size, 1);
                }
                long visits = network.TopologyVisits;
                for (int sample = 0; sample < 5; sample++)
                {
                    timer.Restart();
                    for (int index = 0; index < iterations; index++)
                    {
                        int source = 1 + (int)((long)index * 104729 % size);
                        Check(network.Insert(source, 1000, 1, 4), "benchmark insert");
                        Check(
                            network.Take(size + 1 - source, 1).ProliferatorPoints == 4,
                            "benchmark take"
                        );
                    }
                    timer.Stop();
                    samples.Add(timer.Elapsed.TotalMilliseconds * 1000000 / (iterations * 2));
                }
                samples.Sort();
                Check(
                    network.TopologyVisits == visits && network.View(1).Count == 0,
                    "I/O must not traverse topology"
                );
                report.AppendLine(
                    "BENCH nodes="
                        + size
                        + " build_ms="
                        + build.ToString("F2")
                        + " median_ns_per_op="
                        + samples[2].ToString("F1")
                        + " min="
                        + samples[0].ToString("F1")
                        + " max="
                        + samples[4].ToString("F1")
                        + " topology_visits_during_io=0"
                );
                if (size == 100000)
                {
                    network.Insert(1, 1000, 100, 400);
                    timer.Restart();
                    network.Disconnect(size / 2, size / 2 + 1);
                    timer.Stop();
                    Check(
                        network.View(1).Nodes == size / 2
                            && network.View(size).Count + network.View(1).Count == 100,
                        "large split"
                    );
                    report.AppendLine(
                        "BENCH split_nodes=100000 ms="
                            + timer.Elapsed.TotalMilliseconds.ToString("F2")
                    );
                }
            }
            foreach (int workers in new[] { 1, 4, 8 })
            {
                var network2 = Chain(100000);
                var timer = Stopwatch.StartNew();
                const int loops = 50000;
                Parallel.For(
                    0,
                    workers,
                    worker =>
                    {
                        for (int index2 = 0; index2 < loops; index2++)
                        {
                            Check(network2.Insert(1 + worker, 1000, 1, 4), "threaded insert");
                            Check(network2.Take(100000 - worker, 1).Count == 1, "threaded take");
                        }
                    }
                );
                timer.Stop();
                Check(network2.View(1).Count == 0, "threaded benchmark balance");
                report.AppendLine(
                    "BENCH shared_network_workers="
                        + workers
                        + " aggregate_Mops_s="
                        + (workers * loops * 2 / timer.Elapsed.TotalSeconds / 1000000).ToString(
                            "F3"
                        )
                );
            }
            return report.ToString();
        }

        private static void RoundTrip(PipeGraph graph, int[] nodes)
        {
            using (var memory = new MemoryStream())
            {
                graph.Export(new BinaryWriter(memory));
                memory.Position = 0;
                var copy = PipeGraph.Import(new BinaryReader(memory));
                foreach (int node in nodes)
                {
                    var inventoryView = graph.View(node);
                    var inventoryView2 = copy.View(node);
                    Check(
                        inventoryView.Nodes == inventoryView2.Nodes
                            && inventoryView.Item == inventoryView2.Item
                            && inventoryView.Count == inventoryView2.Count
                            && inventoryView.ProliferatorPoints
                                == inventoryView2.ProliferatorPoints,
                        "save roundtrip"
                    );
                }
                byte[] data = memory.ToArray();
                data[0] = 99;
                try
                {
                    PipeGraph.Import(new BinaryReader(new MemoryStream(data)));
                    throw new Exception("invalid save accepted");
                }
                catch (InvalidDataException) { }
            }
        }

        private static void Randomized()
        {
            const int size = 200;
            var random = new Random(1431);
            var graph = new PipeGraph();
            var nodes = new Dictionary<int, HashSet<int>>();
            long expected = 0;
            long expectedInc = 0;
            for (int entityId = 1; entityId <= size; entityId++)
            {
                graph.Add(entityId);
                nodes[entityId] = new HashSet<int>();
            }
            for (int step = 0; step < 20000; step++)
            {
                int a = random.Next(1, size + 1);
                int b = random.Next(1, size + 1);
                switch (random.Next(5))
                {
                    case 0:
                        if (
                            a != b
                            && nodes[a].Count < 4
                            && nodes[b].Count < 4
                            && graph.Connect(a, b)
                        )
                        {
                            nodes[a].Add(b);
                            nodes[b].Add(a);
                        }
                        break;
                    case 1:
                        if (nodes[a].Count > 0)
                        {
                            b = nodes[a].First();
                            graph.Disconnect(a, b);
                            nodes[a].Remove(b);
                            nodes[b].Remove(a);
                        }
                        break;
                    case 2:
                        int count = random.Next(1, 10),
                            item = random.Next(1000, 1003),
                            proliferatorPoints = count * random.Next(5);
                        if (graph.Insert(a, item, count, proliferatorPoints))
                        {
                            expected += count;
                            expectedInc += proliferatorPoints;
                        }
                        break;
                    case 3:
                        var take = graph.Take(a, random.Next(1, 12));
                        expected -= take.Count;
                        expectedInc -= take.ProliferatorPoints;
                        break;
                    case 4:
                        var refund = graph.Remove(a);
                        expected -= refund.Count;
                        expectedInc -= refund.ProliferatorPoints;
                        foreach (int link in nodes[a])
                            nodes[link].Remove(a);
                        nodes[a].Clear();
                        graph.Add(a);
                        break;
                }
                if (step % 41 == 0)
                {
                    var unseen = new HashSet<int>(nodes.Keys);
                    long actual = 0;
                    long actualInc = 0;
                    while (unseen.Count > 0)
                    {
                        int seed = unseen.First();
                        var members = new List<int>();
                        var queue = new Queue<int>();
                        queue.Enqueue(seed);
                        unseen.Remove(seed);
                        while (queue.Count > 0)
                        {
                            int entityId2 = queue.Dequeue();
                            members.Add(entityId2);
                            foreach (int link in nodes[entityId2])
                                if (unseen.Remove(link))
                                    queue.Enqueue(link);
                        }
                        var view = graph.View(seed);
                        Check(
                            view.Nodes == members.Count
                                && view.Count <= view.Capacity
                                && view.ProliferatorPoints >= 0
                                && view.ProliferatorPoints <= view.Count * 4,
                            "oracle topology/capacity"
                        );
                        foreach (int member in members)
                        {
                            var inventoryView = graph.View(member);
                            Check(
                                inventoryView.Item == view.Item
                                    && inventoryView.Count == view.Count
                                    && inventoryView.ProliferatorPoints == view.ProliferatorPoints
                                    && inventoryView.Nodes == view.Nodes,
                                "oracle shared inventory"
                            );
                        }
                        actual += view.Count;
                        actualInc += view.ProliferatorPoints;
                    }
                    Check(actual == expected && actualInc == expectedInc, "oracle conservation");
                }
                if (step % 1000 == 0)
                    RoundTrip(graph, nodes.Keys.ToArray());
            }
        }

        private static void AdmissionPolicy()
        {
            var additions = new[] { 1101, 1000 };
            var admission = new ItemAdmission(
                new[] { 1000, 1007 },
                additions,
                new[] { 1007, 1101 }
            );
            additions[0] = 1102;
            Check(
                admission.Allows(1000)
                    && !admission.Allows(1007)
                    && !admission.Allows(1101)
                    && !admission.Allows(1102)
                    && !admission.Allows(0)
                    && !admission.Allows(-1),
                "admission precedence and immutable snapshot"
            );
            Check(
                ItemAdmission
                    .ParseIds("1000, 1101;1000\n1007")
                    .SequenceEqual(new[] { 1000, 1101, 1007 }),
                "item ID separators and deduplication"
            );
            Check(ItemAdmission.ParseIds("").Length == 0, "empty item ID list");
            foreach (string invalid in new[] { "water", "0", "-1", "2147483648", "1000,broken" })
            {
                bool rejected = false;
                try
                {
                    ItemAdmission.ParseIds(invalid);
                }
                catch (FormatException)
                {
                    rejected = true;
                }
                Check(rejected, "invalid item ID list rejected: " + invalid);
            }
        }

        private static void Concurrent()
        {
            var graph = Chain(1000);
            long inserted = 0;
            long taken = 0;
            long inInc = 0;
            long outInc = 0;
            Parallel.For(
                0,
                9,
                worker =>
                {
                    if (worker == 8)
                    {
                        for (int index = 0; index < 100; index++)
                        {
                            graph.Disconnect(500, 501);
                            graph.Connect(500, 501);
                        }
                        return;
                    }
                    for (int index2 = 0; index2 < 10000; index2++)
                    {
                        if (graph.Insert(1 + worker, 1000, 1, 4))
                        {
                            Interlocked.Increment(ref inserted);
                            Interlocked.Add(ref inInc, 4);
                        }
                        var amount = graph.Take(1000 - worker, 1);
                        Interlocked.Add(ref taken, amount.Count);
                        Interlocked.Add(ref outInc, amount.ProliferatorPoints);
                    }
                }
            );
            Check(
                graph.View(1).Count == inserted - taken
                    && graph.View(1).ProliferatorPoints == inInc - outInc,
                "concurrent conservation"
            );
            graph = Chain(1);
            var success = new int[2];
            Parallel.For(
                0,
                2,
                worker =>
                {
                    for (int index3 = 0; index3 < 100; index3++)
                        if (graph.Insert(1, 1000 + worker, 1, 0))
                            Interlocked.Increment(ref success[worker]);
                }
            );
            Check((success[0] == 0) != (success[1] == 0), "competing initial fluid atomicity");
        }
    }
}
