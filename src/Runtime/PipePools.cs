using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace FluidPipes
{
    internal struct FluidPoolView
    {
        public int Item;
        public int Nodes;
        public int Containers;
        public long Count;
        public long ProliferatorPoints;
        public long Capacity;
        public long PipeCount;
        public long PipeCapacity;
        public bool Conflict;
        public bool RequiresFluid;
        public double Fill => Capacity == 0 ? 0 : (double)Count / Capacity;
    }

    internal static partial class PipePools
    {
        private sealed class Reservoir
        {
            public State State;
            public int Pipe;
            public int Tank;
            public int Slot;
            public StationComponent Station;
            public Pool Pool;
            public readonly HashSet<int> Networks = new HashSet<int>();

            public FluidAmount Read()
            {
                if (Pipe > 0)
                {
                    var pipeInventory = State.Graph.View(Pipe);
                    return new FluidAmount(
                        pipeInventory.Item,
                        pipeInventory.Count,
                        pipeInventory.ProliferatorPoints
                    );
                }
                if (Tank > 0)
                {
                    var tank = State.Factory.factoryStorage.tankPool[Tank];
                    return new FluidAmount(
                        tank.fluidCount == 0 ? 0 : tank.fluidId,
                        tank.fluidCount,
                        tank.fluidCount == 0 ? 0 : tank.fluidInc
                    );
                }
                lock (Station.storage)
                {
                    var storageSlot = Station.storage[Slot];
                    return new FluidAmount(
                        storageSlot.itemId,
                        storageSlot.count,
                        storageSlot.count == 0 ? 0 : storageSlot.inc
                    );
                }
            }

            public long Capacity =>
                Pipe > 0 ? State.Graph.View(Pipe).Capacity
                : Tank > 0 ? State.Factory.factoryStorage.tankPool[Tank].fluidCapacity
                : Station.storage[Slot].max;

            public void Write(int item, long count, long proliferatorPoints)
            {
                if (count == 0)
                {
                    proliferatorPoints = 0;
                }

                if (Pipe > 0)
                {
                    State.Graph.SetInventory(Pipe, item, count, proliferatorPoints);
                    return;
                }
                if (Tank > 0)
                {
                    ref var tank = ref State.Factory.factoryStorage.tankPool[Tank];
                    tank.fluidId = count == 0 ? 0 : item;
                    tank.fluidCount = checked((int)count);
                    tank.fluidInc = checked((int)proliferatorPoints);
                    return;
                }
                lock (Station.storage)
                {
                    Station.storage[Slot].count = checked((int)count);
                    Station.storage[Slot].inc = checked((int)proliferatorPoints);
                }
            }
        }

        private sealed class Pool
        {
            public readonly List<Reservoir> Stores = new List<Reservoir>();
            public int Item;
            public int ReadCursor;
            public int WriteCursor;
            public int Representative;
            public bool Conflict;
            public bool RequiresFluid;
            public long Count;
            public long ProliferatorPoints;
            public long Capacity;
            public FluidAmount[] Before;
            public int[] Written;

            public void Measure()
            {
                Item = 0;
                Count = ProliferatorPoints = Capacity = 0;
                Conflict = false;
                foreach (var store in Stores)
                {
                    var amount = store.Read();
                    Capacity += store.Capacity;
                    if (amount.Count > 0)
                    {
                        if (Item != 0 && Item != amount.Item)
                        {
                            Conflict = true;
                        }
                        Item = amount.Item;
                        Count += amount.Count;
                        ProliferatorPoints += amount.ProliferatorPoints;
                    }
                    if (store.Station != null && store.Station.storage[store.Slot].itemId > 0)
                    {
                        int filter = store.Station.storage[store.Slot].itemId;
                        if (Item != 0 && Item != filter)
                        {
                            Conflict = true;
                        }
                        Item = filter;
                    }
                }
                if (RequiresFluid && Item != 0 && !ItemProto.isFluid(Item))
                {
                    Conflict = true;
                }
            }

            public void Balance()
            {
                Measure();
                if (Conflict || Capacity == 0 || Count > Capacity)
                {
                    return;
                }

                long count = Count;

                long proliferatorPoints = ProliferatorPoints;

                long capacity = Capacity;
                foreach (var store in Stores)
                {
                    long cap = store.Capacity;
                    long itemCount = Portion(count, cap, capacity);
                    long proliferatorPoints2 = Portion(proliferatorPoints, itemCount, count);
                    store.Write(Item, itemCount, proliferatorPoints2);
                    count -= itemCount;
                    proliferatorPoints -= proliferatorPoints2;
                    capacity -= cap;
                }
            }
        }

        private sealed class State
        {
            public PlanetFactory Factory;
            public PipeGraph Graph;
            public readonly object Sync = new object();
            public long Version = -1;
            public int PolicyVersion = -1;
            public bool Dirty = true;
            public int Ticks;
            public long Revision;
            public bool HasReservoirs;
            public bool HasUnboundPorts;
            public readonly Dictionary<int, int> ItemHints = new Dictionary<int, int>();
            public readonly Dictionary<int, Pool> Networks = new Dictionary<int, Pool>();
            public readonly List<Pool> Pools = new List<Pool>();
            public readonly HashSet<int> AttachedTanks = new HashSet<int>();
            public readonly HashSet<long> AttachedSlots = new HashSet<long>();
            public readonly Dictionary<int, int> Removing = new Dictionary<int, int>();
            public readonly Dictionary<int, Reservoir> Tanks = new Dictionary<int, Reservoir>();
            public readonly Dictionary<long, Reservoir> Slots = new Dictionary<long, Reservoir>();

            public void Rebuild()
            {
                Networks.Clear();
                Pools.Clear();
                AttachedTanks.Clear();
                AttachedSlots.Clear();
                Tanks.Clear();
                Slots.Clear();
                HasUnboundPorts = false;
                var parents = new Dictionary<int, int>();
                var pipes = new Dictionary<int, Reservoir>();
                foreach (int pipeEntityId in Graph.Representatives())
                {
                    int network = Graph.View(pipeEntityId).Network;
                    parents[network] = network;
                    var store = new Reservoir { State = this, Pipe = pipeEntityId };
                    store.Networks.Add(network);
                    pipes.Add(network, store);
                }
                var external = new List<Reservoir>();
                var storage = Factory.factoryStorage;
                if (storage?.tankPool != null)
                {
                    var visited = new HashSet<int>();
                    for (int tankIndex = 1; tankIndex < storage.tankCursor; tankIndex++)
                    {
                        if (
                            storage.tankPool[tankIndex].id != tankIndex
                            || Integration.Reservoirs.OrbitalRingCompatibility.UsesIndependentInventory(
                                Factory,
                                storage.tankPool[tankIndex].entityId
                            )
                            || Removing.ContainsKey(storage.tankPool[tankIndex].entityId)
                            || !visited.Add(tankIndex)
                        )
                        {
                            continue;
                        }

                        var chain = new List<int>();
                        var queue = new Queue<int>();
                        var networks = new HashSet<int>();
                        queue.Enqueue(tankIndex);
                        while (queue.Count > 0)
                        {
                            int tank = queue.Dequeue();
                            chain.Add(tank);
                            var tankComponent = storage.tankPool[tank];
                            AddNetwork(tankComponent.belt0, networks);
                            AddNetwork(tankComponent.belt1, networks);
                            AddNetwork(tankComponent.belt2, networks);
                            AddNetwork(tankComponent.belt3, networks);
                            foreach (
                                int next in new[]
                                {
                                    tankComponent.lastTankId,
                                    tankComponent.nextTankId,
                                }
                            )
                            {
                                if (
                                    next > 0
                                    && next < storage.tankCursor
                                    && storage.tankPool[next].id == next
                                    && !Integration.Reservoirs.OrbitalRingCompatibility.UsesIndependentInventory(
                                        Factory,
                                        storage.tankPool[next].entityId
                                    )
                                    && !Removing.ContainsKey(storage.tankPool[next].entityId)
                                    && visited.Add(next)
                                )
                                {
                                    queue.Enqueue(next);
                                }
                            }
                        }
                        if (networks.Count == 0)
                        {
                            continue;
                        }

                        foreach (int tank in chain)
                        {
                            var store = new Reservoir { State = this, Tank = tank };
                            store.Networks.UnionWith(networks);
                            external.Add(store);
                            AttachedTanks.Add(tank);
                            Tanks[tank] = store;
                        }
                    }
                }
                var transport = Factory.transport;
                if (transport?.stationPool != null)
                {
                    for (
                        int stationIndex = 1;
                        stationIndex < transport.stationCursor;
                        stationIndex++
                    )
                    {
                        var station = transport.stationPool[stationIndex];
                        if (
                            station == null
                            || station.id != stationIndex
                            || station.storage == null
                            || station.slots == null
                            || Removing.ContainsKey(station.entityId)
                        )
                        {
                            continue;
                        }

                        var slots = new Dictionary<int, Reservoir>();
                        for (int portIndex = 0; portIndex < station.slots.Length; portIndex++)
                        {
                            var slot = station.slots[portIndex];
                            if (!PipeRuntime.IsPipe(Factory.cargoTraffic, slot.beltId))
                            {
                                continue;
                            }

                            int entity = Factory.cargoTraffic.beltPool[slot.beltId].entityId;

                            int network = Graph.View(entity).Network;
                            if (!parents.ContainsKey(network))
                            {
                                continue;
                            }

                            int storageSlotIndex = slot.storageIdx - 1;

                            int networkItemId = Graph.View(entity).Item;
                            if (networkItemId == 0)
                            {
                                ItemHints.TryGetValue(network, out networkItemId);
                            }

                            if (
                                storageSlotIndex < 0
                                || storageSlotIndex >= station.storage.Length
                                || !StationItemSupported(
                                    station.storage[storageSlotIndex].itemId,
                                    networkItemId
                                )
                            )
                            {
                                if (slot.dir != IODir.Input)
                                {
                                    continue;
                                }

                                var candidates = new List<int>();
                                for (
                                    int storageIndex = 0;
                                    storageIndex < station.storage.Length;
                                    storageIndex++
                                )
                                {
                                    if (
                                        StationItemSupported(
                                            station.storage[storageIndex].itemId,
                                            networkItemId
                                        )
                                        && (
                                            networkItemId == 0
                                            || station.storage[storageIndex].itemId == networkItemId
                                        )
                                    )
                                    {
                                        candidates.Add(storageIndex);
                                    }
                                }

                                if (candidates.Count != 1)
                                {
                                    HasUnboundPorts = true;
                                    continue;
                                }
                                storageSlotIndex = candidates[0];
                                station.slots[portIndex].storageIdx = storageSlotIndex + 1;
                            }
                            if (!slots.TryGetValue(storageSlotIndex, out var store))
                            {
                                store = new Reservoir
                                {
                                    State = this,
                                    Station = station,
                                    Slot = storageSlotIndex,
                                };
                                slots.Add(storageSlotIndex, store);
                                external.Add(store);
                                Slots[((long)station.id << 32) | (uint)storageSlotIndex] = store;
                            }
                            store.Networks.Add(network);
                            AttachedSlots.Add(((long)station.id << 32) | (uint)portIndex);
                        }
                    }
                }

                foreach (var store in external)
                {
                    int first = 0;
                    foreach (int network in store.Networks)
                    {
                        if (!parents.ContainsKey(network))
                        {
                            continue;
                        }

                        if (first == 0)
                        {
                            first = network;
                        }
                        else
                        {
                            parents[Root(parents, network)] = Root(parents, first);
                        }
                    }
                }
                var pools = new Dictionary<int, Pool>();
                foreach (var entry in pipes)
                {
                    int root = Root(parents, entry.Key);
                    if (!pools.TryGetValue(root, out var pool))
                    {
                        pool = new Pool();
                        pools.Add(root, pool);
                        Pools.Add(pool);
                    }
                    pool.Stores.Add(entry.Value);
                    if (pool.Representative == 0)
                    {
                        pool.Representative = entry.Value.Pipe;
                    }

                    Networks[entry.Key] = pool;
                }
                foreach (var store in external)
                {
                    int network = store.Networks.FirstOrDefault(itemCount =>
                        Networks.ContainsKey(itemCount)
                    );
                    if (network != 0)
                    {
                        Networks[network].Stores.Add(store);
                    }
                }
                HasReservoirs = external.Count > 0;
                foreach (var pool in Pools)
                {
                    foreach (var store in pool.Stores)
                    {
                        store.Pool = pool;
                    }
                    pool.RequiresFluid = pool.Stores.Any(store => store.Tank > 0);
                    pool.Before = new FluidAmount[pool.Stores.Count];
                    pool.Written = new int[pool.Stores.Count];
                    pool.Balance();
                }
                if (transport?.stationPool != null)
                {
                    foreach (var store in external)
                    {
                        if (
                            store.Station != null
                            && !stationOwners.TryGetValue(store.Station, out _)
                        )
                        {
                            stationOwners.Add(store.Station, this);
                        }
                    }
                }

                Version = Graph.TopologyVersion;
                Revision++;
                PolicyVersion = TransportItemRegistry.Version;
                Dirty = false;
            }

            private void AddNetwork(int belt, HashSet<int> result)
            {
                if (PipeRuntime.IsPipe(Factory.cargoTraffic, belt))
                {
                    int networkId = Graph
                        .View(Factory.cargoTraffic.beltPool[belt].entityId)
                        .Network;
                    if (networkId != 0)
                    {
                        result.Add(networkId);
                    }
                }
            }
        }

        private static ConditionalWeakTable<PlanetFactory, State> states =
            new ConditionalWeakTable<PlanetFactory, State>();
        private static ConditionalWeakTable<StationComponent, State> stationOwners =
            new ConditionalWeakTable<StationComponent, State>();
        private static readonly ConditionalWeakTable<PipeGraph, PlanetFactory> factories =
            new ConditionalWeakTable<PipeGraph, PlanetFactory>();

        public static void Register(PlanetFactory factory, PipeGraph graph)
        {
            if (!factories.TryGetValue(graph, out _))
            {
                lock (factories)
                {
                    if (!factories.TryGetValue(graph, out _))
                    {
                        factories.Add(graph, factory);
                    }
                }
            }
        }

        public static void Clear()
        {
            states = new ConditionalWeakTable<PlanetFactory, State>();
            stationOwners = new ConditionalWeakTable<StationComponent, State>();
        }

        public static void Dirty(PlanetFactory factory)
        {
            if (factory != null && states.TryGetValue(factory, out var state))
            {
                lock (state.Sync)
                {
                    state.Dirty = true;
                }
            }
        }

        public static int BeginRemoval(PlanetFactory factory, int entity)
        {
            if (
                entity <= 0
                || entity >= factory.entityPool.Length
                || factory.entityPool[entity].tankId == 0
                    && factory.entityPool[entity].stationId == 0
            )
            {
                return 0;
            }

            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            var state = StateFor(factory, graph);
            lock (state.Sync)
            {
                state.Removing.TryGetValue(entity, out int depth);
                state.Removing[entity] = depth + 1;
                state.Dirty = true;
            }
            return entity;
        }

        public static void EndRemoval(PlanetFactory factory, int entity)
        {
            if (entity == 0 || !states.TryGetValue(factory, out var state))
            {
                return;
            }

            lock (state.Sync)
            {
                if (state.Removing.TryGetValue(entity, out int depth))
                {
                    if (depth <= 1)
                    {
                        state.Removing.Remove(entity);
                    }
                    else
                    {
                        state.Removing[entity] = depth - 1;
                    }
                }
                state.Dirty = true;
            }
        }

        private static int Root(Dictionary<int, int> parents, int node)
        {
            int result = node;
            while (parents[result] != result)
            {
                result = parents[result];
            }
            while (parents[node] != node)
            {
                int next = parents[node];
                parents[node] = result;
                node = next;
            }
            return result;
        }

        private static long Portion(long total, long take, long capacity) =>
            capacity == 0 ? 0 : total / capacity * take + total % capacity * take / capacity;

        private static bool StationItemSupported(int item, int existingItem) =>
            item > 0 && (TransportItemRegistry.IsAllowed(item) || item == existingItem);

        private static State Get(PipeGraph graph)
        {
            if (!factories.TryGetValue(graph, out var factory))
            {
                return null;
            }

            var state = StateFor(factory, graph);
            if (
                !state.Dirty
                && state.Version == graph.TopologyVersion
                && state.PolicyVersion == TransportItemRegistry.Version
            )
            {
                return state;
            }

            lock (state.Sync)
            {
                if (
                    state.Dirty
                    || state.Version != graph.TopologyVersion
                    || state.PolicyVersion != TransportItemRegistry.Version
                )
                {
                    state.Rebuild();
                }
            }

            return state;
        }

        private static State StateFor(PlanetFactory factory, PipeGraph graph)
        {
            var cache = states;
            if (cache.TryGetValue(factory, out var state))
            {
                return state;
            }

            lock (cache)
            {
                if (!cache.TryGetValue(factory, out state))
                {
                    state = new State { Factory = factory, Graph = graph };
                    cache.Add(factory, state);
                }
                return state;
            }
        }

        private static Pool Find(State state, int entity)
        {
            state.Networks.TryGetValue(state.Graph.View(entity).Network, out var pool);
            return pool;
        }

        public static long StorageRevision(PipeGraph graph) => Get(graph)?.Revision ?? 0;

        public static int StorageGroup(PipeGraph graph, int entity)
        {
            var state = Get(graph);
            if (state == null)
            {
                return graph.View(entity).Network;
            }
            lock (state.Sync)
            {
                return Find(state, entity)?.Representative ?? 0;
            }
        }

        public static FluidPoolView Query(PipeGraph graph, int entity)
        {
            var inventory = QueryStorage(graph, entity);
            PipeMachinePorts.ApplyReservation(graph, entity, ref inventory);
            return inventory;
        }

        private static FluidPoolView QueryStorage(PipeGraph graph, int entity)
        {
            var state = Get(graph);
            if (state == null || !state.HasReservoirs)
            {
                var raw = graph.View(entity);
                return new FluidPoolView
                {
                    Item = raw.Item,
                    Count = raw.Count,
                    ProliferatorPoints = raw.ProliferatorPoints,
                    Capacity = raw.Capacity,
                };
            }
            lock (state.Sync)
            {
                var pool = Find(state, entity);
                if (pool == null)
                {
                    return default(FluidPoolView);
                }

                if (pool.Stores.Count == 1 && pool.Stores[0].Pipe > 0)
                {
                    var raw = graph.View(entity);
                    return new FluidPoolView
                    {
                        Item = raw.Item,
                        Count = raw.Count,
                        ProliferatorPoints = raw.ProliferatorPoints,
                        Capacity = raw.Capacity,
                    };
                }
                return new FluidPoolView
                {
                    Item = pool.Item,
                    Count = pool.Count,
                    ProliferatorPoints = pool.ProliferatorPoints,
                    Capacity = pool.Capacity,
                    Conflict = pool.Conflict,
                    RequiresFluid = pool.RequiresFluid,
                };
            }
        }

        public static bool CanConnect(PipeGraph graph, int firstEntityId, int secondEntityId)
        {
            var firstInventory = Query(graph, firstEntityId);
            var secondInventory = Query(graph, secondEntityId);
            return !firstInventory.Conflict
                && !secondInventory.Conflict
                && (
                    firstInventory.Item == 0
                    || secondInventory.Item == 0
                    || firstInventory.Item == secondInventory.Item
                )
                && (
                    !firstInventory.RequiresFluid
                    || secondInventory.Item == 0
                    || ItemProto.isFluid(secondInventory.Item)
                )
                && (
                    !secondInventory.RequiresFluid
                    || firstInventory.Item == 0
                    || ItemProto.isFluid(firstInventory.Item)
                );
        }

        public static bool CanAttach(PlanetFactory factory, int pipe, int container, int slot)
        {
            if (pipe <= 0 || container <= 0 || !PipeRuntime.IsPipeObject(factory, pipe))
            {
                return true;
            }

            var entity = factory.entityPool[container];
            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            var view = Query(graph, pipe);
            if (view.Conflict)
            {
                return false;
            }

            if (entity.tankId > 0 && factory.factoryStorage?.tankPool != null)
            {
                if (view.Item != 0 && !ItemProto.isFluid(view.Item))
                {
                    return false;
                }

                var tank = factory.factoryStorage.tankPool[entity.tankId];
                if (tank.fluidCount > 0 && view.Item != 0 && view.Item != tank.fluidId)
                {
                    return false;
                }

                foreach (int belt in new[] { tank.belt0, tank.belt1, tank.belt2, tank.belt3 })
                {
                    if (
                        PipeRuntime.IsPipe(factory.cargoTraffic, belt)
                        && !CanConnect(graph, pipe, factory.cargoTraffic.beltPool[belt].entityId)
                    )
                    {
                        return false;
                    }
                }
            }
            if (entity.stationId > 0 && factory.transport?.stationPool != null)
            {
                var station = factory.transport.stationPool[entity.stationId];
                if (station?.slots == null || slot < 0 || slot >= station.slots.Length)
                {
                    return false;
                }

                int index = station.slots[slot].storageIdx - 1;
                if (index >= 0 && index < station.storage.Length)
                {
                    int item = station.storage[index].itemId;
                    return TransportItemRegistry.IsAllowed(item)
                        && (view.Item == 0 || view.Item == item);
                }
                return station.slots[slot].dir != IODir.Output;
            }
            return true;
        }

        public static FluidPoolView View(PipeGraph graph, int entity, bool refresh = false)
        {
            var inventory = ViewStorage(graph, entity, refresh);
            PipeMachinePorts.ApplyReservation(graph, entity, ref inventory);
            return inventory;
        }

        private static FluidPoolView ViewStorage(PipeGraph graph, int entity, bool refresh)
        {
            var state = Get(graph);
            if (state == null)
            {
                var pipeInventory = graph.View(entity);
                return new FluidPoolView
                {
                    Item = pipeInventory.Item,
                    Nodes = pipeInventory.Nodes,
                    Count = pipeInventory.Count,
                    ProliferatorPoints = pipeInventory.ProliferatorPoints,
                    Capacity = pipeInventory.Capacity,
                    PipeCount = pipeInventory.Count,
                    PipeCapacity = pipeInventory.Capacity,
                };
            }
            lock (state.Sync)
            {
                var pool = Find(state, entity);
                if (pool == null)
                {
                    return default(FluidPoolView);
                }

                if (refresh)
                {
                    pool.Balance();
                }

                if (pool.Stores.Count == 1 && pool.Stores[0].Pipe > 0)
                {
                    var raw = graph.View(entity);
                    return new FluidPoolView
                    {
                        Item = raw.Item,
                        Nodes = raw.Nodes,
                        Count = raw.Count,
                        ProliferatorPoints = raw.ProliferatorPoints,
                        Capacity = raw.Capacity,
                        PipeCount = raw.Count,
                        PipeCapacity = raw.Capacity,
                    };
                }
                var sharedInventory = new FluidPoolView
                {
                    Item = pool.Item,
                    Count = pool.Count,
                    ProliferatorPoints = pool.ProliferatorPoints,
                    Capacity = pool.Capacity,
                    Conflict = pool.Conflict,
                    RequiresFluid = pool.RequiresFluid,
                };
                foreach (var store in pool.Stores)
                {
                    if (store.Pipe > 0)
                    {
                        var componentInventory = graph.View(store.Pipe);
                        sharedInventory.Nodes += componentInventory.Nodes;
                        sharedInventory.PipeCount += componentInventory.Count;
                        sharedInventory.PipeCapacity += componentInventory.Capacity;
                    }
                    else
                    {
                        sharedInventory.Containers++;
                    }
                }

                return sharedInventory;
            }
        }

        private static bool AcceptsInventory(PipeGraph graph, int entity)
        {
            return !factories.TryGetValue(graph, out var factory)
                || PipeRuntime.HasInventory(factory, entity);
        }

        public static bool Insert(
            PipeGraph graph,
            int entity,
            int item,
            int count,
            int proliferatorPoints
        )
        {
            if (
                !AcceptsInventory(graph, entity)
                || !TransportItemRegistry.IsAllowed(item)
                || !PipeMachinePorts.AllowsItem(graph, entity, item)
                || count <= 0
                || proliferatorPoints < 0
                || proliferatorPoints > (long)count * 10
            )
            {
                return false;
            }

            var state = Get(graph);
            if (state != null && state.HasUnboundPorts)
            {
                lock (state.Sync)
                {
                    state.ItemHints[graph.View(entity).Network] = item;
                    state.Rebuild();
                }
            }
            if (state == null || !state.HasReservoirs)
            {
                return graph.Insert(entity, item, count, proliferatorPoints);
            }

            lock (state.Sync)
            {
                var pool = Find(state, entity);
                if (pool == null)
                {
                    return false;
                }

                if (pool.Stores.Count == 1 && pool.Stores[0].Pipe > 0)
                {
                    return graph.Insert(entity, item, count, proliferatorPoints);
                }

                if (
                    pool.Conflict
                    || pool.Item != 0 && pool.Item != item
                    || pool.RequiresFluid && !ItemProto.isFluid(item)
                )
                {
                    return false;
                }
                // 容器字段仍是原生物流与存档序列化的权威来源。
                if (pool.Capacity - pool.Count < count)
                {
                    pool.Measure();
                }

                if (
                    pool.Conflict
                    || pool.Item != 0 && pool.Item != item
                    || pool.Capacity - pool.Count < count
                )
                {
                    return false;
                }

                long remainingCount = count;

                long remainingPoints = proliferatorPoints;
                int modifiedStoreCount = 0;
                for (
                    int visitedStoreCount = 0;
                    visitedStoreCount < pool.Stores.Count && remainingCount > 0;
                    visitedStoreCount++
                )
                {
                    int storeIndex = (pool.WriteCursor + visitedStoreCount) % pool.Stores.Count;
                    var store = pool.Stores[storeIndex];
                    var amount = store.Read();
                    if (amount.Count > 0 && amount.Item != item)
                    {
                        continue;
                    }

                    long acceptedCount = Math.Min(
                        remainingCount,
                        Math.Max(0, store.Capacity - amount.Count)
                    );

                    long acceptedPoints = Portion(remainingPoints, acceptedCount, remainingCount);
                    if (acceptedCount == 0)
                    {
                        continue;
                    }

                    pool.Before[modifiedStoreCount] = amount;
                    pool.Written[modifiedStoreCount++] = storeIndex;
                    store.Write(
                        item,
                        amount.Count + acceptedCount,
                        amount.ProliferatorPoints + acceptedPoints
                    );
                    remainingCount -= acceptedCount;
                    remainingPoints -= acceptedPoints;
                    if (remainingCount == 0)
                    {
                        pool.WriteCursor = storeIndex;
                        break;
                    }
                }
                if (remainingCount != 0)
                {
                    for (int rollbackIndex = 0; rollbackIndex < modifiedStoreCount; rollbackIndex++)
                    {
                        var storedAmount = pool.Before[rollbackIndex];
                        pool.Stores[pool.Written[rollbackIndex]]
                            .Write(
                                storedAmount.Item,
                                storedAmount.Count,
                                storedAmount.ProliferatorPoints
                            );
                    }
                    pool.Measure();
                    return false;
                }
                pool.Item = item;
                pool.Count += count;
                pool.ProliferatorPoints += proliferatorPoints;
                return true;
            }
        }

        public static FluidAmount Take(
            PipeGraph graph,
            int entity,
            int max,
            int filter = 0,
            int[] needs = null
        )
        {
            if (max <= 0 || !AcceptsInventory(graph, entity))
            {
                return default(FluidAmount);
            }

            var state = Get(graph);
            if (state == null || !state.HasReservoirs)
            {
                return graph.Take(entity, max, filter, needs);
            }

            lock (state.Sync)
            {
                var pool = Find(state, entity);
                if (pool == null || pool.Conflict)
                {
                    return default(FluidAmount);
                }

                if (pool.Stores.Count == 1 && pool.Stores[0].Pipe > 0)
                {
                    return graph.Take(entity, max, filter, needs);
                }

                if (pool.Count == 0)
                {
                    pool.Measure();
                }

                int item = pool.Item;
                if (
                    item == 0
                    || filter != 0 && filter != item
                    || needs != null && Array.IndexOf(needs, item) < 0
                )
                {
                    return default(FluidAmount);
                }

                long count = 0;

                long proliferatorPoints = 0;
                for (
                    int visitedStoreCount = 0;
                    visitedStoreCount < pool.Stores.Count && count < max;
                    visitedStoreCount++
                )
                {
                    int storeIndex = (pool.ReadCursor + visitedStoreCount) % pool.Stores.Count;
                    var store = pool.Stores[storeIndex];
                    var amount = store.Read();
                    if (amount.Count == 0 || amount.Item != item)
                    {
                        continue;
                    }

                    long takenCount = Math.Min(max - count, amount.Count);

                    long takenPoints = Portion(amount.ProliferatorPoints, takenCount, amount.Count);
                    store.Write(
                        item,
                        amount.Count - takenCount,
                        amount.ProliferatorPoints - takenPoints
                    );
                    count += takenCount;
                    proliferatorPoints += takenPoints;
                    if (count == max)
                    {
                        pool.ReadCursor = storeIndex;
                        break;
                    }
                }
                pool.Count -= count;
                pool.ProliferatorPoints -= proliferatorPoints;
                if (pool.Count <= 0)
                {
                    pool.Measure();
                }

                return count == 0
                    ? default(FluidAmount)
                    : new FluidAmount(item, count, proliferatorPoints);
            }
        }

        public static void Empty(PipeGraph graph, int entity)
        {
            var state = Get(graph);
            if (state == null)
            {
                graph.SetInventory(entity, 0, 0, 0);
                return;
            }
            lock (state.Sync)
            {
                var pool = Find(state, entity);
                if (pool == null)
                {
                    return;
                }
                foreach (var store in pool.Stores)
                {
                    store.Write(0, 0, 0);
                }
                pool.Measure();
            }
        }

        private static void Prime(Reservoir target, int budget)
        {
            var pool = target.Pool;
            if (pool == null || pool.Conflict)
            {
                return;
            }

            var current = target.Read();
            long needed = Math.Min(budget, target.Capacity) - current.Count;
            if (needed <= 0)
            {
                return;
            }

            if (pool.Count == 0)
            {
                pool.Measure();
            }

            int item = pool.Item;
            if (item == 0 || current.Count > 0 && current.Item != item)
            {
                return;
            }

            foreach (var source in pool.Stores)
            {
                if (source == target)
                {
                    continue;
                }

                var amount = source.Read();
                if (amount.Count == 0 || amount.Item != item)
                {
                    continue;
                }

                long itemCount = Math.Min(needed, amount.Count);

                long proliferatorPoints = Portion(
                    amount.ProliferatorPoints,
                    itemCount,
                    amount.Count
                );
                source.Write(
                    item,
                    amount.Count - itemCount,
                    amount.ProliferatorPoints - proliferatorPoints
                );
                current = new FluidAmount(
                    item,
                    current.Count + itemCount,
                    current.ProliferatorPoints + proliferatorPoints
                );
                target.Write(item, current.Count, current.ProliferatorPoints);
                needed -= itemCount;
                if (needed == 0)
                {
                    break;
                }
            }
        }

        public static void PrimeTank(PlanetFactory factory, int tank, int budget)
        {
            if (!states.TryGetValue(factory, out var state))
            {
                return;
            }

            state = Get(state.Graph);
            lock (state.Sync)
            {
                if (state.Tanks.TryGetValue(tank, out var store))
                {
                    Prime(store, budget);
                }
            }
        }

        public static void PrimeStation(
            StationComponent station,
            int budget,
            int item = 0,
            int[] needs = null
        )
        {
            if (!stationOwners.TryGetValue(station, out var state))
            {
                return;
            }

            state = Get(state.Graph);
            lock (state.Sync)
            {
                for (int slot = 0; slot < station.storage.Length; slot++)
                {
                    int type = station.storage[slot].itemId;
                    if (
                        item != 0 && item != type
                        || needs != null && Array.IndexOf(needs, type) < 0
                    )
                    {
                        continue;
                    }

                    if (
                        state.Slots.TryGetValue(
                            ((long)station.id << 32) | (uint)slot,
                            out var store
                        )
                    )
                    {
                        Prime(store, budget);
                    }
                }
            }
        }

        public static bool TryStationTake(
            StationComponent station,
            int max,
            int filter,
            int[] needs,
            out FluidAmount amount
        )
        {
            amount = default(FluidAmount);
            if (max <= 0 || !stationOwners.TryGetValue(station, out var state))
            {
                return false;
            }

            state = Get(state.Graph);
            lock (state.Sync)
            {
                bool matched = false;
                for (int slot = 0; slot < station.storage.Length; slot++)
                {
                    int item = station.storage[slot].itemId;
                    if (
                        item <= 0
                        || filter != 0 && item != filter
                        || needs != null && Array.IndexOf(needs, item) < 0
                    )
                    {
                        continue;
                    }

                    if (
                        !state.Slots.TryGetValue(
                            ((long)station.id << 32) | (uint)slot,
                            out var store
                        )
                    )
                    {
                        if (station.storage[slot].count > 0)
                        {
                            return false;
                        }
                        continue;
                    }
                    matched = true;
                    amount = Take(state.Graph, store.Pool.Representative, max, item, needs);
                    if (amount.Count > 0)
                    {
                        return true;
                    }
                }
                return matched;
            }
        }

        public static bool SameSpace(PipeGraph graph, int firstEntityId, int secondEntityId)
        {
            var state = Get(graph);
            if (state == null)
            {
                return graph.View(firstEntityId).Network == graph.View(secondEntityId).Network;
            }

            lock (state.Sync)
            {
                var firstPool = Find(state, firstEntityId);
                return firstPool != null && ReferenceEquals(firstPool, Find(state, secondEntityId));
            }
        }

        public static void Tick(PlanetFactory factory)
        {
            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            if (graph.NetworkCount == 0)
            {
                return;
            }

            var state = Get(graph);
            if (state == null)
            {
                return;
            }

            lock (state.Sync)
            {
                if (++state.Ticks % 60 == 0)
                {
                    state.Dirty = true;
                    state.Rebuild();
                }
                foreach (var pool in state.Pools)
                {
                    if (pool.Stores.Count > 1)
                    {
                        pool.Balance();
                    }
                }
            }
        }
    }
}
