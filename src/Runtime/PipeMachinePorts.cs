using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FluidPipes.API;
using UnityEngine;

namespace FluidPipes
{
    internal static partial class PipeMachinePorts
    {
        internal sealed class Connection
        {
            public int PipeEntityId;
            public int PipeSlotIndex;
            public int MachineEntityId;
            public int SlotIndex;
            public MachinePortOptions Options = new MachinePortOptions();
            public FluidAmount Pending;
            public int SelectionRecipeRevision = -1;
            public int SelectionPolicyVersion = -1;
            public int SelectionItemId = -1;
            public int SelectionConnectionRevision = -1;
            public long SelectionStorageRevision = -1;
        }

        private sealed class FactoryState
        {
            public PlanetFactory Factory;
            public PipeGraph Graph;
            public readonly object Sync = new object();
            public readonly Dictionary<long, Connection> Machines =
                new Dictionary<long, Connection>();
            public readonly Dictionary<long, Connection> Pipes = new Dictionary<long, Connection>();
            public readonly Dictionary<int, int> Filters = new Dictionary<int, int>();
            public int Revision;
            public int FilterRevision = -1;
            public int FilterRecipeRevision = -1;
            public int FilterPolicyVersion = -1;
            public long FilterStorageRevision = -1;
        }

        private static ConcurrentDictionary<int, FactoryState> states =
            new ConcurrentDictionary<int, FactoryState>();
        private static ConditionalWeakTable<PipeGraph, FactoryState> graphStates =
            new ConditionalWeakTable<PipeGraph, FactoryState>();
        private static int recipeRevision;

        [ThreadStatic]
        private static int objectMoveDepth;

        public static bool Enabled => PipeConfiguration.EnableMachinePorts;

        private static long Key(int entityId, int slotIndex) =>
            ((long)entityId << 4) | (uint)slotIndex;

        private static FactoryState State(PlanetFactory factory)
        {
            var state = states.GetOrAdd(factory.planetId, _ => new FactoryState());
            if (state.Factory == null)
            {
                state.Factory = factory;
                state.Graph = PipeRuntime.Bind(factory.cargoTraffic);
                graphStates.GetValue(state.Graph, _ => state);
            }
            return state;
        }

        public static PrefabDesc MachineDescription(PlanetFactory factory, int objectId)
        {
            if (objectId == 0)
            {
                return null;
            }

            int modelIndex;
            if (objectId > 0)
            {
                if (
                    objectId >= factory.entityPool.Length
                    || factory.entityPool[objectId].id != objectId
                )
                {
                    return null;
                }
                modelIndex = factory.entityPool[objectId].modelIndex;
            }
            else
            {
                int prebuildId = -objectId;
                if (
                    prebuildId >= factory.prebuildPool.Length
                    || factory.prebuildPool[prebuildId].id != prebuildId
                )
                {
                    return null;
                }
                modelIndex = factory.prebuildPool[prebuildId].modelIndex;
            }

            var description = LDB.models.Select(modelIndex)?.prefabDesc;
            return
                description != null
                && description.isAssembler
                && (description.portPoses == null || description.portPoses.Length == 0)
                && description.slotPoses != null
                && description.slotPoses.Length > 0
                ? description
                : null;
        }

        private static bool Exists(PlanetFactory factory, Connection connection)
        {
            var description = MachineDescription(factory, connection.MachineEntityId);
            if (
                description == null
                || connection.SlotIndex < 0
                || connection.SlotIndex >= Math.Min(16, description.slotPoses.Length)
                || !PipeRuntime.IsPipeObject(factory, connection.PipeEntityId)
            )
            {
                return false;
            }

            factory.ReadObjectConn(
                connection.MachineEntityId,
                connection.SlotIndex,
                out _,
                out int pipe,
                out int pipeSlot
            );
            factory.ReadObjectConn(
                connection.PipeEntityId,
                connection.PipeSlotIndex,
                out _,
                out int machine,
                out int machineSlot
            );
            return pipe == connection.PipeEntityId
                && pipeSlot == connection.PipeSlotIndex
                && machine == connection.MachineEntityId
                && machineSlot == connection.SlotIndex;
        }

        public static void Observe(PlanetFactory factory, int objectId, int slotIndex)
        {
            if (objectMoveDepth > 0 || slotIndex < 0 || slotIndex >= 16)
            {
                return;
            }
            factory.ReadObjectConn(objectId, slotIndex, out _, out int other, out int otherSlot);
            bool pipeFirst = PipeRuntime.IsPipeObject(factory, objectId);
            int machineId = pipeFirst ? other : objectId;
            int pipeId = pipeFirst ? objectId : other;
            int machineSlot = pipeFirst ? otherSlot : slotIndex;
            int pipeSlot = pipeFirst ? slotIndex : otherSlot;
            bool candidate =
                Enabled
                && pipeSlot >= 0
                && pipeSlot < 4
                && PipeRuntime.IsPipeObject(factory, pipeId)
                && MachineDescription(factory, machineId) != null;
            if (!candidate && !states.TryGetValue(factory.planetId, out _))
            {
                return;
            }

            var state = State(factory);
            lock (state.Sync)
            {
                if (
                    state.Machines.TryGetValue(Key(objectId, slotIndex), out var oldMachine)
                    && !Exists(factory, oldMachine)
                )
                {
                    Remove(state, oldMachine);
                }
                if (
                    state.Pipes.TryGetValue(Key(objectId, slotIndex), out var oldPipe)
                    && !Exists(factory, oldPipe)
                )
                {
                    Remove(state, oldPipe);
                }
                if (!candidate || machineSlot < 0 || machineSlot >= 16)
                {
                    return;
                }
                long key = Key(machineId, machineSlot);
                if (state.Machines.TryGetValue(key, out var current) && Exists(factory, current))
                {
                    return;
                }

                var connection = new Connection
                {
                    PipeEntityId = pipeId,
                    PipeSlotIndex = pipeSlot,
                    MachineEntityId = machineId,
                    SlotIndex = machineSlot,
                };
                if (Exists(factory, connection))
                {
                    state.Machines[key] = connection;
                    state.Pipes[Key(pipeId, pipeSlot)] = connection;
                    state.Revision++;
                    AutoConfigure(factory, connection);
                    state.Revision++;
                    PipeVisuals.Dirty(factory, pipeId);
                }
            }
        }

        private static bool AutoConfigure(PlanetFactory factory, Connection connection)
        {
            var state = State(factory);
            int itemId =
                connection.PipeEntityId > 0
                    ? PipePools.Query(state.Graph, connection.PipeEntityId).Item
                    : 0;
            long storageRevision = PipePools.StorageRevision(state.Graph);
            if (
                connection.SelectionRecipeRevision == recipeRevision
                && connection.SelectionPolicyVersion == TransportItemRegistry.Version
                && connection.SelectionItemId == itemId
                && connection.SelectionConnectionRevision == state.Revision
                && connection.SelectionStorageRevision == storageRevision
            )
            {
                return false;
            }
            connection.SelectionRecipeRevision = recipeRevision;
            connection.SelectionPolicyVersion = TransportItemRegistry.Version;
            connection.SelectionItemId = itemId;
            connection.SelectionConnectionRevision = state.Revision;
            connection.SelectionStorageRevision = storageRevision;

            var choices = Choices(factory, connection.MachineEntityId);
            if (itemId > 0)
            {
                choices = choices.Where(choice => choice.ItemId == itemId).ToArray();
            }
            if (choices.Length == 1 && CanConfigure(factory, connection, choices[0]))
            {
                connection.Options = choices[0];
                return true;
            }
            return false;
        }

        public static MachinePortOptions[] Choices(PlanetFactory factory, int machineEntityId)
        {
            if (machineEntityId <= 0 || MachineDescription(factory, machineEntityId) == null)
            {
                return new MachinePortOptions[0];
            }
            int assemblerId = factory.entityPool[machineEntityId].assemblerId;
            if (assemblerId <= 0 || factory.factorySystem == null)
            {
                return new MachinePortOptions[0];
            }
            var recipe = factory.factorySystem.assemblerPool[assemblerId].recipeExecuteData;
            if (recipe == null)
            {
                return new MachinePortOptions[0];
            }
            return recipe
                .requires.Where(TransportItemRegistry.IsAllowed)
                .Distinct()
                .Select(item => new MachinePortOptions(item, MachinePortDirection.IntoMachine))
                .Concat(
                    recipe
                        .products.Where(TransportItemRegistry.IsAllowed)
                        .Distinct()
                        .Select(item => new MachinePortOptions(
                            item,
                            MachinePortDirection.OutOfMachine
                        ))
                )
                .ToArray();
        }

        private static bool RecipeMatches(
            PlanetFactory factory,
            Connection connection,
            MachinePortOptions options
        )
        {
            if (
                options.ItemId <= 0
                || connection.MachineEntityId <= 0
                || !TransportItemRegistry.IsAllowed(options.ItemId)
                || MachineDescription(factory, connection.MachineEntityId) == null
            )
            {
                return false;
            }
            int assemblerId = factory.entityPool[connection.MachineEntityId].assemblerId;
            if (assemblerId <= 0)
            {
                return false;
            }
            var recipe = factory.factorySystem.assemblerPool[assemblerId].recipeExecuteData;
            int[] items =
                options.Direction == MachinePortDirection.IntoMachine
                    ? recipe?.requires
                    : recipe?.products;
            return items != null && Array.IndexOf(items, options.ItemId) >= 0;
        }

        private static bool CanConfigure(
            PlanetFactory factory,
            Connection connection,
            MachinePortOptions options
        )
        {
            if (
                connection.Pending.Count > 0
                && (
                    connection.Options.ItemId != options.ItemId
                    || connection.Options.Direction != options.Direction
                )
            )
            {
                return false;
            }
            if (!options.Enabled || options.ItemId == 0)
            {
                return true;
            }
            if (!RecipeMatches(factory, connection, options))
            {
                return false;
            }
            if (connection.PipeEntityId <= 0)
            {
                return true;
            }

            var state = State(factory);
            var inventory = PipePools.View(state.Graph, connection.PipeEntityId);
            if (inventory.Count > 0 && inventory.Item != options.ItemId)
            {
                return false;
            }
            foreach (var other in state.Machines.Values)
            {
                if (
                    !ReferenceEquals(other, connection)
                    && other.PipeEntityId > 0
                    && other.Options.Enabled
                    && RecipeMatches(factory, other, other.Options)
                    && other.Options.ItemId != options.ItemId
                    && PipePools.SameSpace(state.Graph, connection.PipeEntityId, other.PipeEntityId)
                )
                {
                    return false;
                }
            }
            return true;
        }

        public static bool Configure(
            PlanetFactory factory,
            int machineEntityId,
            int slotIndex,
            MachinePortOptions options
        )
        {
            if (
                slotIndex < 0
                || slotIndex >= 16
                || !Enabled
                || factory == null
                || !states.TryGetValue(factory.planetId, out var state)
            )
            {
                return false;
            }
            lock (state.Sync)
            {
                if (
                    !state.Machines.TryGetValue(Key(machineEntityId, slotIndex), out var connection)
                    || !Exists(factory, connection)
                    || !CanConfigure(factory, connection, options)
                )
                {
                    return false;
                }
                connection.Options = options;
                state.Revision++;
                return true;
            }
        }

        public static bool Connect(
            PlanetFactory factory,
            int pipeEntityId,
            int machineEntityId,
            int slotIndex,
            MachinePortOptions options
        )
        {
            if (
                !Enabled
                || !PipeApi.IsPipe(factory, pipeEntityId)
                || !PipeRuntime.HasInventory(factory, pipeEntityId)
            )
            {
                return false;
            }
            var description = MachineDescription(factory, machineEntityId);
            if (
                description == null
                || slotIndex < 0
                || slotIndex >= Math.Min(16, description.slotPoses.Length)
            )
            {
                return false;
            }
            factory.ReadObjectConn(machineEntityId, slotIndex, out _, out int occupant, out _);
            if (occupant != 0)
            {
                return occupant == pipeEntityId
                    && Configure(factory, machineEntityId, slotIndex, options);
            }
            int pipeSlot = PipeConnections.FreeSlot(factory, pipeEntityId, 1);
            var connection = new Connection
            {
                PipeEntityId = pipeEntityId,
                PipeSlotIndex = pipeSlot,
                MachineEntityId = machineEntityId,
                SlotIndex = slotIndex,
            };
            if (pipeSlot < 0 || !CanConfigure(factory, connection, options))
            {
                return false;
            }
            factory.WriteObjectConn(
                machineEntityId,
                slotIndex,
                options.Direction == MachinePortDirection.OutOfMachine,
                pipeEntityId,
                pipeSlot
            );
            if (Configure(factory, machineEntityId, slotIndex, options))
            {
                return true;
            }
            factory.ReadObjectConn(machineEntityId, slotIndex, out _, out int connectedPipe, out _);
            if (connectedPipe == pipeEntityId)
            {
                factory.ClearObjectConn(machineEntityId, slotIndex);
            }
            return false;
        }

        public static MachinePortSnapshot[] Connections(PlanetFactory factory, int pipeEntityId)
        {
            if (factory == null || !states.TryGetValue(factory.planetId, out var state))
            {
                return new MachinePortSnapshot[0];
            }
            lock (state.Sync)
            {
                var connections = new List<MachinePortSnapshot>(4);
                for (int slotIndex = 0; slotIndex < 4; slotIndex++)
                {
                    if (
                        state.Pipes.TryGetValue(Key(pipeEntityId, slotIndex), out var connection)
                        && Exists(factory, connection)
                    )
                    {
                        connections.Add(new MachinePortSnapshot(connection));
                    }
                }
                return connections.ToArray();
            }
        }

        public static bool Disconnect(PlanetFactory factory, int machineEntityId, int slotIndex)
        {
            if (
                slotIndex < 0
                || slotIndex >= 16
                || factory == null
                || !states.TryGetValue(factory.planetId, out var state)
            )
            {
                return false;
            }
            lock (state.Sync)
            {
                if (
                    !state.Machines.TryGetValue(Key(machineEntityId, slotIndex), out var connection)
                )
                {
                    return false;
                }
                factory.ClearObjectConn(machineEntityId, slotIndex);
                Remove(state, connection);
                PipeVisuals.Dirty(factory, connection.PipeEntityId);
                return true;
            }
        }

        private static int ReservedItem(PipeGraph graph, int pipeEntityId)
        {
            if (!Enabled || !graphStates.TryGetValue(graph, out var state))
            {
                return 0;
            }
            lock (state.Sync)
            {
                long storageRevision = PipePools.StorageRevision(graph);
                if (
                    state.FilterRevision != state.Revision
                    || state.FilterRecipeRevision != recipeRevision
                    || state.FilterPolicyVersion != TransportItemRegistry.Version
                    || state.FilterStorageRevision != storageRevision
                )
                {
                    state.Filters.Clear();
                    foreach (var connection in state.Machines.Values)
                    {
                        if (
                            connection.PipeEntityId <= 0
                            || !connection.Options.Enabled
                            || !Exists(state.Factory, connection)
                            || !RecipeMatches(state.Factory, connection, connection.Options)
                        )
                        {
                            continue;
                        }
                        int group = PipePools.StorageGroup(graph, connection.PipeEntityId);
                        int itemId = connection.Options.ItemId;
                        if (
                            state.Filters.TryGetValue(group, out int existing)
                            && existing != itemId
                        )
                        {
                            itemId = -1;
                        }
                        state.Filters[group] = itemId;
                    }
                    state.FilterRevision = state.Revision;
                    state.FilterRecipeRevision = recipeRevision;
                    state.FilterPolicyVersion = TransportItemRegistry.Version;
                    state.FilterStorageRevision = storageRevision;
                }
                return state.Filters.TryGetValue(
                    PipePools.StorageGroup(graph, pipeEntityId),
                    out int filter
                )
                    ? filter
                    : 0;
            }
        }

        public static bool AllowsItem(PipeGraph graph, int pipeEntityId, int itemId)
        {
            int filter = ReservedItem(graph, pipeEntityId);
            return filter == 0 || filter == itemId;
        }

        public static void ApplyReservation(
            PipeGraph graph,
            int pipeEntityId,
            ref FluidPoolView inventory
        )
        {
            int filter = ReservedItem(graph, pipeEntityId);
            if (filter < 0 || filter > 0 && inventory.Item != 0 && inventory.Item != filter)
            {
                inventory.Conflict = true;
            }
            if (filter > 0 && inventory.Item == 0)
            {
                inventory.Item = filter;
            }
        }

        public static void RecipeChanged()
        {
            System.Threading.Interlocked.Increment(ref recipeRevision);
        }

        public static void Tick(PlanetFactory factory)
        {
            if (!Enabled || !states.TryGetValue(factory.planetId, out var state))
            {
                return;
            }
            lock (state.Sync)
            {
                foreach (var connection in state.Machines.Values)
                {
                    if (
                        connection.Options.Enabled
                        && connection.Options.ItemId == 0
                        && Exists(factory, connection)
                        && AutoConfigure(factory, connection)
                    )
                    {
                        state.Revision++;
                    }
                    if (
                        connection.PipeEntityId <= 0
                        || !connection.Options.Enabled
                        || !Exists(factory, connection)
                        || !RecipeMatches(factory, connection, connection.Options)
                    )
                    {
                        continue;
                    }
                    Step(state, connection);
                }
            }
        }

        private static void Step(FactoryState state, Connection connection)
        {
            var factory = state.Factory;
            var options = connection.Options;
            int machineId = connection.MachineEntityId;
            int pipeId = connection.PipeEntityId;
            int assemblerId = factory.entityPool[machineId].assemblerId;
            uint machineTarget = (uint)EFactoryIOTargetType.Assembler | (uint)assemblerId;
            ref var assembler = ref factory.factorySystem.assemblerPool[assemblerId];
            // The native APIs transfer bytes; a bounded batch keeps each endpoint's tick cost finite.
            for (int transferred = 0; transferred < byte.MaxValue; transferred++)
            {
                var inventory = PipePools.Query(state.Graph, pipeId);
                if (inventory.Conflict || inventory.Item != 0 && inventory.Item != options.ItemId)
                {
                    break;
                }
                if (options.Direction == MachinePortDirection.IntoMachine)
                {
                    assembler.UpdateNeeds();
                    if (Array.IndexOf(assembler.needs, options.ItemId) < 0)
                    {
                        break;
                    }
                    if (connection.Pending.Count == 0)
                    {
                        connection.Pending = PipePools.Take(state.Graph, pipeId, 1, options.ItemId);
                        if (connection.Pending.Count == 0)
                        {
                            break;
                        }
                    }
                    int inserted = factory.InsertInto(
                        machineTarget,
                        0,
                        options.ItemId,
                        1,
                        (byte)connection.Pending.ProliferatorPoints,
                        out _
                    );
                    if (inserted == 1)
                    {
                        connection.Pending = default(FluidAmount);
                    }
                    else
                    {
                        if (
                            PipePools.Insert(
                                state.Graph,
                                pipeId,
                                connection.Pending.Item,
                                1,
                                (int)connection.Pending.ProliferatorPoints
                            )
                        )
                        {
                            connection.Pending = default(FluidAmount);
                        }
                        break;
                    }
                }
                else
                {
                    if (inventory.Count >= inventory.Capacity)
                    {
                        break;
                    }
                    if (connection.Pending.Count == 0)
                    {
                        int itemId = factory.PickFrom(
                            machineTarget,
                            0,
                            options.ItemId,
                            null,
                            out byte count,
                            out byte points
                        );
                        if (itemId == 0)
                        {
                            break;
                        }
                        connection.Pending = new FluidAmount(itemId, count, points);
                    }
                    if (
                        !PipePools.Insert(
                            state.Graph,
                            pipeId,
                            connection.Pending.Item,
                            (int)connection.Pending.Count,
                            (int)connection.Pending.ProliferatorPoints
                        )
                    )
                    {
                        break;
                    }
                    connection.Pending = default(FluidAmount);
                }
            }
        }

        public static string Status(PlanetFactory factory, MachinePortSnapshot snapshot)
        {
            if (!Enabled)
            {
                return "直连接口功能已关闭，设置已保留";
            }
            if (!snapshot.Options.Enabled)
            {
                return "接口已暂停";
            }
            if (snapshot.Options.ItemId == 0)
            {
                return "请选择输送方向和物品";
            }
            if (
                !states.TryGetValue(factory.planetId, out var state)
                || !state.Machines.TryGetValue(
                    Key(snapshot.MachineEntityId, snapshot.SlotIndex),
                    out var connection
                )
            )
            {
                return "接口连接已变化";
            }
            if (!RecipeMatches(factory, connection, snapshot.Options))
            {
                return "配方或物品许可已变化，接口已暂停";
            }
            return PipePools.Query(state.Graph, snapshot.PipeEntityId).Conflict
                ? "管网筛选或库存冲突，接口已暂停"
                : "按机器需求与管网容量输送";
        }

        private static void Remove(FactoryState state, Connection connection)
        {
            state.Machines.Remove(Key(connection.MachineEntityId, connection.SlotIndex));
            state.Pipes.Remove(Key(connection.PipeEntityId, connection.PipeSlotIndex));
            state.Revision++;
            if (connection.Pending.Count > 0)
            {
                PipeRuntime.Return(
                    GameMain.mainPlayer,
                    connection.PipeEntityId,
                    connection.Pending
                );
                connection.Pending = default(FluidAmount);
            }
        }

        public static void RemoveObject(PlanetFactory factory, int objectId)
        {
            if (!states.TryGetValue(factory.planetId, out var state))
            {
                return;
            }
            lock (state.Sync)
            {
                for (int slotIndex = 0; slotIndex < 16; slotIndex++)
                {
                    long key = Key(objectId, slotIndex);
                    if (state.Machines.TryGetValue(key, out var machineConnection))
                    {
                        Remove(state, machineConnection);
                    }
                    if (state.Pipes.TryGetValue(key, out var pipeConnection))
                    {
                        Remove(state, pipeConnection);
                    }
                }
            }
        }

        public static void MoveObject(PlanetFactory factory, int oldId, int newId)
        {
            if (!states.TryGetValue(factory.planetId, out var state))
            {
                return;
            }
            lock (state.Sync)
            {
                for (int slotIndex = 0; slotIndex < 16; slotIndex++)
                {
                    long oldKey = Key(oldId, slotIndex);
                    if (
                        !state.Machines.TryGetValue(oldKey, out var connection)
                        && !state.Pipes.TryGetValue(oldKey, out connection)
                    )
                    {
                        continue;
                    }
                    state.Machines.Remove(Key(connection.MachineEntityId, connection.SlotIndex));
                    state.Pipes.Remove(Key(connection.PipeEntityId, connection.PipeSlotIndex));
                    if (connection.PipeEntityId == oldId)
                    {
                        connection.PipeEntityId = newId;
                    }
                    if (connection.MachineEntityId == oldId)
                    {
                        connection.MachineEntityId = newId;
                    }
                    state.Machines[Key(connection.MachineEntityId, connection.SlotIndex)] =
                        connection;
                    state.Pipes[Key(connection.PipeEntityId, connection.PipeSlotIndex)] =
                        connection;
                }
                state.Revision++;
            }
        }

        public static void BeginObjectMove()
        {
            objectMoveDepth++;
        }

        public static void EndObjectMove()
        {
            objectMoveDepth--;
        }

        public static void Restore(PlanetFactory factory)
        {
            if (!states.ContainsKey(factory.planetId))
            {
                return;
            }
            var state = State(factory);
            lock (state.Sync)
            {
                foreach (
                    var connection in state
                        .Machines.Values.Where(connection => !Exists(factory, connection))
                        .ToArray()
                )
                {
                    Remove(state, connection);
                }
                state.Revision++;
            }
        }

        public static void Clear()
        {
            states = new ConcurrentDictionary<int, FactoryState>();
            graphStates = new ConditionalWeakTable<PipeGraph, FactoryState>();
        }
    }
}
