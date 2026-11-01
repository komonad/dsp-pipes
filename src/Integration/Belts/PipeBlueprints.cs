using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FluidPipes.Integration.Belts
{
    [HarmonyPatch]
    internal static class PipeBlueprints
    {
        // FPG1 trailers contain blueprint-local neighbor indices; native parameters precede them.
        private const int Marker = 0x46504731;

        [ThreadStatic]
        private static int[] nearby;

        private sealed class PreviewSelection
        {
            internal BuildPreview[] Pool;
            internal int Count;
        }

        internal static int[] Neighbors(
            BlueprintBuilding building,
            int buildingCount,
            out int nativeCount
        )
        {
            var data = building.parameters ?? new int[0];
            nativeCount = data.Length;
            if (
                !PipeTypeRegistry.IsUndirected(building.itemId)
                || data.Length == 0
                || data[data.Length - 1] != Marker
            )
                return null;
            if (data.Length < 4)
                throw new FormatException("Invalid pipe blueprint graph trailer.");
            if (building.index < 0 || building.index >= buildingCount)
                throw new FormatException("Invalid pipe blueprint vertex index.");
            nativeCount = data[data.Length - 2];
            if (
                nativeCount < 0
                || nativeCount > data.Length - 4
                || data[nativeCount] != Marker
                || data[nativeCount + 1] != data.Length - nativeCount - 4
            )
                throw new FormatException("Invalid pipe blueprint graph length.");
            int count = data[nativeCount + 1];
            var neighbors = new int[count];
            int previous = building.index;
            for (int index = 0; index < count; index++)
            {
                int peer = data[nativeCount + 2 + index];
                if (peer <= previous || peer >= buildingCount)
                    throw new FormatException("Invalid pipe blueprint neighbor index.");
                neighbors[index] = peer;
                previous = peer;
            }
            return neighbors;
        }

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(BlueprintUtils), nameof(BlueprintUtils.GenerateBlueprintData))
        ]
        private static void Copy(
            BlueprintData _blueprintData,
            PlanetData _planet,
            int[] _objIds,
            int _objCount
        )
        {
            var buildings = _blueprintData?.buildings;
            var factory = _planet?.factory;
            if (factory == null || buildings == null || buildings.Length != _objCount)
                return;
            var indices = new Dictionary<int, int>();
            for (int index = 0; index < _objCount; index++)
                indices[_objIds[index]] = index;
            for (int index = 0; index < buildings.Length; index++)
            {
                var building = buildings[index];
                if (!PipeTypeRegistry.IsUndirected(building.itemId))
                    continue;
                var peers = PipeTopology
                    .Neighbors(factory, _objIds[index])
                    .Where(indices.ContainsKey)
                    .Select(peer => indices[peer])
                    .Where(peer => peer > index)
                    .OrderBy(peer => peer)
                    .ToArray();
                var native = building.parameters ?? new int[0];
                if (native.Length + peers.Length + 4 > short.MaxValue)
                    throw new InvalidOperationException(
                        "Pipe blueprint vertex exceeds the native parameter limit."
                    );
                var data = new int[native.Length + peers.Length + 4];
                Array.Copy(native, data, native.Length);
                data[native.Length] = Marker;
                data[native.Length + 1] = peers.Length;
                Array.Copy(peers, 0, data, native.Length + 2, peers.Length);
                data[data.Length - 2] = native.Length;
                data[data.Length - 1] = Marker;
                building.parameters = data;
            }
        }

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(BlueprintUtils), nameof(BlueprintUtils.InitBuildPreviewByBPData))
        ]
        private static void Preview(
            BlueprintData _blueprintData,
            BuildPreview[] _bpArray,
            int __result
        )
        {
            for (int index = 0; index < __result; index++)
            {
                var building = _blueprintData.buildings[index % _blueprintData.buildings.Length];
                try
                {
                    if (
                        Neighbors(building, _blueprintData.buildings.Length, out int nativeCount)
                        != null
                    )
                    {
                        var preview = _bpArray[index];
                        preview.SetParameters(nativeCount, building.parameters);
                        // Graph edges are restored after placement without native belt replacement.
                        if (PipeTypeRegistry.IsUndirected(preview.output?.item?.ID))
                            preview.output = null;
                        if (PipeTypeRegistry.IsUndirected(preview.input?.item?.ID))
                            preview.input = null;
                    }
                }
                catch (FormatException)
                {
                    _bpArray[index].SetParameters(0, new int[0]);
                    _bpArray[index].condition = EBuildCondition.Failure;
                }
            }
        }

        [
            HarmonyPrefix,
            HarmonyPatch(
                typeof(BuildTool_BlueprintPaste),
                nameof(BuildTool_BlueprintPaste.ArrangeOverlapBP)
            )
        ]
        private static void Arrange(
            BuildTool_BlueprintPaste __instance,
            out PreviewSelection __state
        )
        {
            __state = null;
            var native = __instance
                .bpPool.Take(__instance.bpCursor)
                .Where(preview => !PipeTypeRegistry.IsUndirected(preview.item?.ID))
                .ToArray();
            if (native.Length == __instance.bpCursor)
                return;
            // Pipe graph vertices retain their identities even when their centerlines are close.
            __state = new PreviewSelection
            {
                Pool = __instance.bpPool,
                Count = __instance.bpCursor,
            };
            __instance.bpPool = native;
            __instance.bpCursor = native.Length;
        }

        [HarmonyPostfix, HarmonyPriority(Priority.First)]
        [HarmonyPatch(
            typeof(BuildTool_BlueprintPaste),
            nameof(BuildTool_BlueprintPaste.ArrangeOverlapBP)
        )]
        private static void Arranged(BuildTool_BlueprintPaste __instance, PreviewSelection __state)
        {
            if (__state == null)
                return;
            __instance.bpPool = __state.Pool;
            __instance.bpCursor = __state.Count;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(
            typeof(BuildTool_BlueprintPaste),
            nameof(BuildTool_BlueprintPaste.ArrangeOverlapBP)
        )]
        private static void ArrangeFinished(
            BuildTool_BlueprintPaste __instance,
            PreviewSelection __state
        ) => Arranged(__instance, __state);

        private static List<Tuple<BuildPreview, BuildPreview>> Edges(BuildTool_BlueprintPaste tool)
        {
            var result = new List<Tuple<BuildPreview, BuildPreview>>();
            var buildings = tool.blueprint?.buildings;
            if (buildings == null || buildings.Length == 0)
                return result;
            for (int index = 0; index < tool.bpCursor; index++)
            {
                int source = index % buildings.Length;
                var peers = Neighbors(buildings[source], buildings.Length, out _);
                if (peers == null)
                    continue;
                foreach (int peer in peers)
                {
                    int target = index - source + peer;
                    if (
                        target >= tool.bpCursor
                        || !PipeTypeRegistry.IsUndirected(buildings[peer].itemId)
                        || !PipeTypeRegistry.Compatible(
                            buildings[source].itemId,
                            buildings[peer].itemId
                        )
                    )
                        throw new FormatException("Incompatible pipe blueprint edge.");
                    result.Add(Tuple.Create(tool.bpPool[index], tool.bpPool[target]));
                }
            }
            return result;
        }

        private static bool Validate(
            BuildTool_BlueprintPaste tool,
            List<Tuple<BuildPreview, BuildPreview>> edges
        )
        {
            var adjacency = new Dictionary<BuildPreview, List<BuildPreview>>();
            var buildings = tool.blueprint?.buildings;
            if (buildings == null || buildings.Length == 0)
                return true;
            for (int index = 0; index < tool.bpCursor; index++)
                if (Neighbors(buildings[index % buildings.Length], buildings.Length, out _) != null)
                    adjacency.Add(tool.bpPool[index], new List<BuildPreview>());
            foreach (var edge in edges)
            {
                if (!adjacency.ContainsKey(edge.Item1))
                    adjacency.Add(edge.Item1, new List<BuildPreview>());
                if (!adjacency.ContainsKey(edge.Item2))
                    adjacency.Add(edge.Item2, new List<BuildPreview>());
                adjacency[edge.Item1].Add(edge.Item2);
                adjacency[edge.Item2].Add(edge.Item1);
            }
            if (adjacency.Count == 0)
                return true;
            var graph = PipeRuntime.Bind(tool.factory.cargoTraffic);
            var existing = new Dictionary<int, BuildPreview>();
            foreach (var preview in adjacency.Keys.ToArray())
            {
                if (preview.coverObjId <= 0)
                    continue;
                if (
                    !PipeRuntime.IsPipeObject(tool.factory, preview.coverObjId)
                    || !PipeTypeRegistry.Compatible(
                        preview.item.ID,
                        PipeRuntime.ObjectPrototype(tool.factory, preview.coverObjId)
                    )
                )
                    return false;
                int network = graph.View(preview.coverObjId).Network;
                if (existing.TryGetValue(network, out var other))
                {
                    adjacency[preview].Add(other);
                    adjacency[other].Add(preview);
                }
                else
                    existing.Add(network, preview);
            }
            var seen = new HashSet<BuildPreview>();
            foreach (var preview in adjacency.Keys)
            {
                int item = 0;
                var queue = new Queue<BuildPreview>();
                queue.Enqueue(preview);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (!seen.Add(current))
                        continue;
                    if (current.coverObjId > 0)
                    {
                        int stored = PipePools.Query(graph, current.coverObjId).Item;
                        if (item != 0 && stored != 0 && stored != item)
                            return false;
                        if (stored != 0)
                            item = stored;
                    }
                    foreach (var peer in adjacency[current])
                        queue.Enqueue(peer);
                }
            }
            return true;
        }

        private static bool TryEdges(
            BuildTool_BlueprintPaste tool,
            out List<Tuple<BuildPreview, BuildPreview>> edges
        )
        {
            try
            {
                edges = Edges(tool);
                if (Validate(tool, edges))
                    return true;
            }
            catch (FormatException)
            {
                edges = null;
            }
            edges = null;
            for (int index = 0; index < tool.bpCursor; index++)
                if (PipeTypeRegistry.IsUndirected(tool.bpPool[index].item?.ID))
                    tool.bpPool[index].condition = EBuildCondition.Failure;
            return false;
        }

        [
            HarmonyPrefix,
            HarmonyPatch(
                typeof(BuildTool_BlueprintPaste),
                nameof(BuildTool_BlueprintPaste.CheckBuildConditions)
            )
        ]
        private static void Cover(BuildTool_BlueprintPaste __instance)
        {
            var buildings = __instance.blueprint?.buildings;
            var logic = __instance.planet?.physics?.nearColliderLogic;
            if (buildings == null || buildings.Length == 0 || logic == null)
                return;
            if (nearby == null)
                nearby = new int[64];
            var used = new HashSet<int>();
            for (int index = 0; index < __instance.bpCursor; index++)
            {
                var preview = __instance.bpPool[index];
                try
                {
                    if (
                        Neighbors(buildings[index % buildings.Length], buildings.Length, out _)
                        == null
                    )
                        continue;
                }
                catch (FormatException)
                {
                    continue;
                }
                preview.coverObjId = 0;
                preview.willRemoveCover = false;
                int count = logic.GetBuildingsInAreaNonAlloc(preview.lpos, 0.6f, ref nearby, false);
                for (int other = 0; other < count; other++)
                {
                    int id = nearby[other];
                    if (
                        used.Contains(id)
                        || PipeRuntime.ObjectPrototype(__instance.factory, id) != preview.item.ID
                    )
                        continue;
                    Vector3 position =
                        id > 0
                            ? __instance.factory.entityPool[id].pos
                            : __instance.factory.prebuildPool[-id].pos;
                    if ((position - preview.lpos).sqrMagnitude >= 0.0001f)
                        continue;
                    preview.coverObjId = id;
                    preview.willRemoveCover = false;
                    used.Add(id);
                    break;
                }
            }
        }

        [
            HarmonyPostfix,
            HarmonyPatch(
                typeof(BuildTool_BlueprintPaste),
                nameof(BuildTool_BlueprintPaste.CheckBuildConditions)
            )
        ]
        private static void Check(BuildTool_BlueprintPaste __instance, ref bool __result)
        {
            if (!TryEdges(__instance, out _))
                __result = false;
        }

        [
            HarmonyPrefix,
            HarmonyPatch(
                typeof(BuildTool_BlueprintPaste),
                nameof(BuildTool_BlueprintPaste.CreatePrebuilds)
            )
        ]
        private static bool Paste(
            BuildTool_BlueprintPaste __instance,
            out List<Tuple<BuildPreview, BuildPreview>> __state
        ) => TryEdges(__instance, out __state);

        [
            HarmonyPostfix,
            HarmonyPatch(
                typeof(BuildTool_BlueprintPaste),
                nameof(BuildTool_BlueprintPaste.CreatePrebuilds)
            )
        ]
        private static void Pasted(
            BuildTool_BlueprintPaste __instance,
            List<Tuple<BuildPreview, BuildPreview>> __state
        )
        {
            if (__state == null)
                return;
            foreach (var edge in __state)
            {
                if (
                    edge.Item1.condition != EBuildCondition.Ok
                        && edge.Item1.condition != EBuildCondition.NotEnoughItem
                    || edge.Item2.condition != EBuildCondition.Ok
                        && edge.Item2.condition != EBuildCondition.NotEnoughItem
                )
                    continue;
                int first = edge.Item1.objId;
                int second = edge.Item2.objId;
                if (first == 0 || second == 0 || first == second)
                    continue;
                if (!PipeConnections.Connect(__instance.factory, first, second, 0, 1))
                    throw new InvalidOperationException(
                        "Cannot restore a pipe blueprint connection."
                    );
            }
        }
    }
}
