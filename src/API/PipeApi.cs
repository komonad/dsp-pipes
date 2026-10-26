using System;

namespace FluidPipes.API
{
    /// <summary>管道库存与物品准入的受支持集成接口。</summary>
    /// <remarks>
    /// 工厂操作需要 Unity 主线程或独占的工厂模拟上下文。
    /// 原生实体 ID 可被复用；存档或实体变化后需刷新工厂/实体绑定。
    /// 返回值是独立快照；原生储液罐的变化在模拟 tick 中协调。
    /// </remarks>
    public static class PipeApi
    {
        /// <summary>BepInEx 插件依赖 GUID。</summary>
        public const string PluginGuid = "com.komonad.dsp.fluidpipes";

        /// <summary>用于最低版本依赖声明的 mod 发行版本。</summary>
        public const string ModVersion = "0.8.0";

        /// <summary>受支持的 API 契约代数。</summary>
        public const int ApiVersion = 1;

        /// <summary>原型与初始物品策略是否已初始化。</summary>
        public static bool IsReady => Prototypes.Ready;

        /// <summary>为当前进程禁用内置管道、阀门、配方、科技解锁与建造栏条目。</summary>
        /// <remarks>在依赖插件的 Awake 中、Unity 主线程上、LDBTool 原型注册开始之前调用。该阶段内的重复调用是幂等的。外部类型注册、物品模板与运输 API 仍然可用。每个存档应保持所选内容集合稳定。</remarks>
        /// <exception cref="InvalidOperationException">在 Unity 主线程之外调用，或在原型注册开始之后调用。</exception>
        public static void DisableBuiltInItems() => Prototypes.DisableBuiltInItems();

        /// <summary>已注册的内置管道物品 ID；未初始化或禁用内置内容时为零。</summary>
        public static int PipeItemId => IsReady ? Prototypes.ItemId : 0;

        /// <summary>已注册的内置阀门物品 ID；未初始化或禁用内置内容时为零。</summary>
        public static int ValveItemId => IsReady ? Prototypes.ValveId : 0;

        /// <summary>内置管道配方 ID；未初始化或禁用内置内容时为零。</summary>
        public static int PipeRecipeId => IsReady ? Prototypes.RecipeId : 0;

        /// <summary>内置阀门配方 ID；未初始化或禁用内置内容时为零。</summary>
        public static int ValveRecipeId => IsReady ? Prototypes.ValveRecipeId : 0;

        /// <summary>测试生效的物品准入策略。物品原型初始化之前返回 false。</summary>
        /// <param name="itemId">已注册的物品原型 ID。</param>
        /// <returns>物品获准时为 true；该结果先于容器特定限制。</returns>
        public static bool IsItemAllowed(int itemId) =>
            IsReady && TransportItemRegistry.IsAllowed(itemId);

        /// <summary>授予物品 ID，直到返回的注册被释放。显式配置排除优先。</summary>
        /// <remarks>在 Unity 主线程注册与释放。注册可以先于原型初始化。</remarks>
        /// <param name="itemIds">针对已初始化原型数据库解析的正数物品 ID；数组会被复制。</param>
        /// <returns>独立持有的注册。释放它不会影响其它注册。</returns>
        /// <exception cref="ArgumentNullException">数组为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">某个 ID 为零或负数。</exception>
        /// <exception cref="InvalidOperationException">在 Unity 主线程之外调用。</exception>
        public static IDisposable RegisterTransportItems(params int[] itemIds) =>
            TransportItemRegistry.Register(itemIds);

        /// <summary>通过原生实体与传送带组件识别已建成的管道实体。</summary>
        /// <param name="factory">拥有该实体的工厂。</param>
        /// <param name="entityId">通过工厂实体池解析的正数原生 EntityData.id。</param>
        /// <returns>有效的已建成管道返回 true；null、过期或不支持的目标返回 false。</returns>
        public static bool IsPipe(PlanetFactory factory, int entityId) =>
            IsReady
            && factory?.cargoTraffic != null
            && entityId > 0
            && factory.entityPool != null
            && entityId < factory.entityPool.Length
            && factory.entityPool[entityId].id == entityId
            && PipeRuntime.IsPipe(factory.cargoTraffic, factory.entityPool[entityId].beltId);

        /// <summary>通过缓存的连通分量索引读取共享库存总量。</summary>
        /// <param name="factory">拥有该管道的工厂。</param>
        /// <param name="entityId">已建成的管道实体 ID。</param>
        /// <param name="inventory">独立的库存快照；失败时为默认值。</param>
        /// <returns>有效管道返回 true，包括空库存或存在冲突的共享空间。</returns>
        public static bool TryGetInventory(
            PlanetFactory factory,
            int entityId,
            out NetworkInventory inventory
        )
        {
            inventory = default(NetworkInventory);
            if (!Resolve(factory, entityId, out var graph))
            {
                return false;
            }

            inventory = new NetworkInventory(PipePools.Query(graph, entityId));
            return true;
        }

        /// <summary>读取库存以及管道/容器容量明细。</summary>
        /// <param name="factory">拥有该管道的工厂。</param>
        /// <param name="entityId">已建成的管道实体 ID。</param>
        /// <param name="snapshot">独立的明细快照；失败时为默认值。</param>
        /// <returns>有效管道返回 true。汇总会遍历相连的储液罐与管道连通分量。</returns>
        public static bool TryGetNetwork(
            PlanetFactory factory,
            int entityId,
            out NetworkSnapshot snapshot
        )
        {
            snapshot = default(NetworkSnapshot);
            if (!Resolve(factory, entityId, out var graph))
            {
                return false;
            }

            snapshot = new NetworkSnapshot(PipePools.View(graph, entityId));
            return true;
        }

        /// <summary>在准入、物品标识与容量校验通过后提交完整数量。</summary>
        /// <param name="factory">拥有该管道的工厂。</param>
        /// <param name="entityId">已建成的管道实体 ID。</param>
        /// <param name="itemId">获准插入的物品。</param>
        /// <param name="count">正数物品数量。</param>
        /// <param name="proliferatorPoints">增产点总数，取值为零到数量的十倍之间。</param>
        /// <returns>参数无效、容量不足、物品冲突或容器限制时返回 false。</returns>
        /// <remarks>调用方在成功后从来源扣减；被拒绝的批次仍留在调用方。</remarks>
        public static bool TryInsert(
            PlanetFactory factory,
            int entityId,
            int itemId,
            int count,
            int proliferatorPoints = 0
        ) =>
            Resolve(factory, entityId, out var graph)
            && PipePools.Insert(graph, entityId, itemId, count, proliferatorPoints);

        /// <summary>从共享空间提取至多 maxCount 个物品。筛选值为零时接受当前物品。</summary>
        /// <param name="factory">拥有该管道的工厂。</param>
        /// <param name="entityId">已建成的管道实体 ID。</param>
        /// <param name="maxCount">要移除数量的正数上限。</param>
        /// <param name="amount">被移除的物品与增产点；失败时为空结果。</param>
        /// <param name="filterItemId">零表示任意物品，正数表示要求的物品 ID。</param>
        /// <returns>至少移除一个物品时返回 true。此前获准的物品同样允许提取。</returns>
        /// <remarks>调用方拥有提取出的物品，需预留目标容量并持有剩余数量。</remarks>
        public static bool TryTake(
            PlanetFactory factory,
            int entityId,
            int maxCount,
            out TransportAmount amount,
            int filterItemId = 0
        )
        {
            amount = default(TransportAmount);
            if (maxCount <= 0 || filterItemId < 0 || !Resolve(factory, entityId, out var graph))
            {
                return false;
            }

            var taken = PipePools.Take(graph, entityId, maxCount, filterItemId);
            amount = new TransportAmount(taken);
            return taken.Count > 0;
        }

        /// <summary>检查共享存储标识，包括由原生容器桥接的连接。</summary>
        /// <param name="factory">同时拥有两条管道的工厂。</param>
        /// <param name="firstEntityId">第一条已建成管道的实体 ID。</param>
        /// <param name="secondEntityId">第二条已建成管道的实体 ID。</param>
        /// <returns>两条有效管道访问同一库存时返回 true。</returns>
        public static bool ShareInventory(
            PlanetFactory factory,
            int firstEntityId,
            int secondEntityId
        ) =>
            Resolve(factory, firstEntityId, out var graph)
            && IsPipe(factory, secondEntityId)
            && PipePools.SameSpace(graph, firstEntityId, secondEntityId);

        /// <summary>读取阀门设置的独立副本。</summary>
        /// <param name="factory">拥有该阀门的工厂。</param>
        /// <param name="entityId">已建成的阀门实体 ID。</param>
        /// <param name="options">当前设置；目标无效时为 null。</param>
        /// <returns>有效阀门返回 true。</returns>
        public static bool TryGetValveOptions(
            PlanetFactory factory,
            int entityId,
            out ValveOptions options
        )
        {
            options = null;
            if (!ValidValve(factory, entityId))
            {
                return false;
            }

            var current = PipeValves.Get(factory, entityId);
            options = new ValveOptions(
                current.Enabled,
                current.Rate,
                current.Target,
                (FlowDirection)current.Direction,
                (LevelControl)current.Level
            );
            return true;
        }

        /// <summary>应用经过校验的阀门设置，并重置小数传输额度。</summary>
        /// <param name="factory">拥有该阀门的工厂。</param>
        /// <param name="entityId">已建成的阀门实体 ID。</param>
        /// <param name="options">要应用的不可变设置。</param>
        /// <returns>有效阀门返回 true；返回 false 时设置保持不变。</returns>
        /// <exception cref="ArgumentNullException">options 为 null。</exception>
        public static bool TrySetValveOptions(
            PlanetFactory factory,
            int entityId,
            ValveOptions options
        )
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (!ValidValve(factory, entityId))
            {
                return false;
            }

            var current = PipeValves.Get(factory, entityId);
            current.Enabled = options.Enabled;
            current.Rate = options.ItemsPerSecond;
            current.Target = options.TargetFraction;
            current.Direction = (ValveDirection)options.Direction;
            current.Level = (ValveLevel)options.Control;
            current.Credit = 0;
            return true;
        }

        /// <summary>配置是否启用与生产机器分拣器插槽的直接连接。</summary>
        public static bool MachinePortsEnabled => IsReady && PipeMachinePorts.Enabled;

        /// <summary>列出管道上独立的机器直连连接，包括已暂停的设置。</summary>
        /// <param name="factory">拥有该管道的工厂。</param>
        /// <param name="pipeEntityId">已建成的管道实体 ID。</param>
        /// <returns>新的连接数组；目标无效时为空数组。</returns>
        public static MachinePortSnapshot[] GetMachinePorts(
            PlanetFactory factory,
            int pipeEntityId
        ) =>
            IsPipe(factory, pipeEntityId)
                ? PipeMachinePorts.Connections(factory, pipeEntityId)
                : new MachinePortSnapshot[0];

        /// <summary>列出可用于机器连接的、已获准的配方输入与产物。</summary>
        /// <param name="factory">拥有该机器的工厂。</param>
        /// <param name="machineEntityId">已建成的生产机器实体 ID。</param>
        /// <returns>新的物品/方向选项数组；目标不受支持时为空数组。</returns>
        public static MachinePortOptions[] GetMachinePortChoices(
            PlanetFactory factory,
            int machineEntityId
        ) =>
            IsReady && factory != null
                ? PipeMachinePorts.Choices(factory, machineEntityId)
                : new MachinePortOptions[0];

        /// <summary>在配方、库存与筛选校验通过后，把管道连接到空闲的原生分拣器插槽。</summary>
        /// <param name="factory">同时拥有两个实体的工厂；请使用主线程或独占的模拟访问。</param>
        /// <param name="pipeEntityId">已建成的管道实体 ID。</param>
        /// <param name="machineEntityId">已建成的生产机器实体 ID。</param>
        /// <param name="slotIndex">从零开始的原生分拣器插槽。</param>
        /// <param name="options">不可变的方向、物品筛选与启停状态。</param>
        /// <returns>连接并配置成功时返回 true。集成被禁用、插槽被占用或存在冲突时返回 false。</returns>
        /// <exception cref="ArgumentNullException">options 为 null。</exception>
        public static bool TryConnectMachinePort(
            PlanetFactory factory,
            int pipeEntityId,
            int machineEntityId,
            int slotIndex,
            MachinePortOptions options
        )
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            return IsReady
                && PipeMachinePorts.Connect(
                    factory,
                    pipeEntityId,
                    machineEntityId,
                    slotIndex,
                    options
                );
        }

        /// <summary>在配方与共享空间筛选校验通过后，修改已有的机器直连连接。</summary>
        /// <param name="factory">拥有该连接的工厂。</param>
        /// <param name="machineEntityId">已连接的生产机器实体 ID。</param>
        /// <param name="slotIndex">从零开始的原生分拣器插槽。</param>
        /// <param name="options">不可变的方向、物品筛选与启停状态。</param>
        /// <returns>应用成功时返回 true。被拒绝的修改保留当前设置与库存。</returns>
        /// <exception cref="ArgumentNullException">options 为 null。</exception>
        public static bool TrySetMachinePortOptions(
            PlanetFactory factory,
            int machineEntityId,
            int slotIndex,
            MachinePortOptions options
        )
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            return IsReady
                && PipeMachinePorts.Configure(factory, machineEntityId, slotIndex, options);
        }

        /// <summary>移除机器直连连接，同时保留管道与机器的库存。</summary>
        /// <param name="factory">拥有该连接的工厂。</param>
        /// <param name="machineEntityId">已连接的生产机器实体 ID。</param>
        /// <param name="slotIndex">从零开始的原生分拣器插槽。</param>
        /// <returns>连接被移除时返回 true；集成被禁用时移除同样可用。</returns>
        public static bool TryDisconnectMachinePort(
            PlanetFactory factory,
            int machineEntityId,
            int slotIndex
        ) => IsReady && PipeMachinePorts.Disconnect(factory, machineEntityId, slotIndex);

        private static bool Resolve(PlanetFactory factory, int entityId, out PipeGraph graph)
        {
            graph = null;
            if (!IsPipe(factory, entityId) || !PipeRuntime.HasInventory(factory, entityId))
            {
                return false;
            }

            graph = PipeRuntime.Bind(factory.cargoTraffic);
            return graph.View(entityId).Nodes > 0;
        }

        private static bool ValidValve(PlanetFactory factory, int entityId) =>
            IsReady
            && factory?.entityPool != null
            && entityId > 0
            && entityId < factory.entityPool.Length
            && PipeValves.IsValve(factory, entityId);
    }
}
