using System;
using System.Collections.Generic;
using UnityEngine;

namespace FluidPipes.API
{
    /// <summary>注册由物品承载的程序化连接，并公开其拓扑。</summary>
    /// <remarks>注册及注册阶段的查询使用 Unity 主线程。注册的定义在原型初始化后保持固定。实时工厂查询要求主线程或对相应工厂的独占访问。实体 ID 可在拆除后复用；调用方需在工厂与拓扑变化之间维护缓存生命周期。</remarks>
    public static class PipeTypes
    {
        /// <summary>在连接改动完成后的下一次主线程更新中触发，每批每个受影响实体一次。实体可能已被拆除；仍存在的相邻节点也会包含在内。</summary>
        public static event Action<PlanetFactory, int> ConnectionsChanged
        {
            add { PipeTypeNotifications.ConnectionsChanged += value; }
            remove { PipeTypeNotifications.ConnectionsChanged -= value; }
        }

        /// <summary>从已初始化的基础管道模板创建独立草稿。</summary>
        /// <remarks>模板在库的 LDBTool pre-add 回调中准备，禁用内置内容时同样如此。返回草稿的注册由调用方负责。</remarks>
        /// <param name="itemId">调用方的正数物品 ID。</param>
        /// <param name="nameKey">其名称的本地化键。</param>
        /// <param name="descriptionKey">其描述的本地化键。</param>
        /// <returns>供在 LDBTool 的 pre-add 阶段注册的草稿。</returns>
        /// <exception cref="ArgumentOutOfRangeException">物品 ID 为零或负数。</exception>
        /// <exception cref="ArgumentException">某个本地化键为空。</exception>
        /// <exception cref="InvalidOperationException">注册阶段、线程或基础模板不可用。</exception>
        public static ItemProto CreateItem(int itemId, string nameKey, string descriptionKey) =>
            PipeTypeRegistry.CreateItem(itemId, nameKey, descriptionKey);

        /// <summary>在原型注册期间指定由外部持有的物品图标。</summary>
        /// <remarks>精灵及其纹理在数据库与图标图集的整个生命周期内保持有效。IconPath 为空的配方继承其产出物品的图标。注册使用 Unity 主线程。</remarks>
        /// <param name="prototype">独立持有且具有正数物品 ID 的草稿。</param>
        /// <param name="icon">由可读、未压缩的 80×80 ARGB32 纹理支撑的完整 80×80 精灵。</param>
        /// <exception cref="ArgumentNullException">物品或精灵为 null，或精灵已被销毁。</exception>
        /// <exception cref="ArgumentOutOfRangeException">物品 ID 为零或负数。</exception>
        /// <exception cref="ArgumentException">精灵矩形或纹理与原生图标图集的要求不符。</exception>
        /// <exception cref="InvalidOperationException">注册已关闭或调用使用了其它线程。</exception>
        public static void SetItemIcon(ItemProto prototype, Sprite icon) =>
            PipeItemIcons.Set(prototype, icon);

        /// <summary>在原型数据库封闭之前于主线程注册类型。</summary>
        /// <param name="ownerGuid">拥有者 BepInEx 插件的 GUID。</param>
        /// <param name="definition">物品原型与不可变的连接设置。</param>
        /// <exception cref="ArgumentNullException">定义为 null。</exception>
        /// <exception cref="ArgumentException">拥有者 GUID 为空。</exception>
        /// <exception cref="InvalidOperationException">注册已关闭、使用了其它线程，或与已有的物品或类型键冲突。</exception>
        public static void Register(string ownerGuid, PipeTypeDefinition definition) =>
            PipeTypeRegistry.Register(ownerGuid, definition);

        /// <summary>标识已注册的程序化连接物品。</summary>
        /// <param name="itemId">游戏物品 ID。</param>
        /// <returns>已注册类型返回 true。</returns>
        public static bool IsPipeItem(int itemId) => PipeTypeRegistry.Get(itemId) != null;

        /// <summary>按物品 ID 查询已注册类型。</summary>
        /// <param name="itemId">游戏物品 ID。</param>
        /// <param name="type">独立的标识快照。</param>
        /// <returns>已注册类型返回 true。</returns>
        public static bool TryGet(int itemId, out PipeTypeInfo type)
        {
            var entry = PipeTypeRegistry.Get(itemId);
            type = entry == null ? default(PipeTypeInfo) : new PipeTypeInfo(entry);
            return entry != null;
        }

        /// <summary>按限定稳定键查询已注册类型。</summary>
        /// <param name="typeKey">插件 GUID、冒号与局部类型名。</param>
        /// <param name="type">独立的标识快照。</param>
        /// <returns>已注册类型返回 true。</returns>
        public static bool TryGet(string typeKey, out PipeTypeInfo type)
        {
            var entry = PipeTypeRegistry.Get(typeKey);
            return TryGet(entry?.Definition.Prototype.ID ?? 0, out type);
        }

        /// <summary>在主线程或工厂独占访问下读取已完成的直接相邻节点。</summary>
        /// <param name="factory">所属工厂。</param>
        /// <param name="entityId">已完成的程序化连接实体。</param>
        /// <returns>相邻实体 ID 的独立数组；目标无效时返回空数组。</returns>
        public static int[] GetNeighbors(PlanetFactory factory, int entityId)
        {
            if (!PipeApi.IsPipe(factory, entityId))
            {
                return Array.Empty<int>();
            }

            return PipeTypeRegistry.IsUndirected(factory.entityPool[entityId].protoId)
                ? PipeRuntime.Bind(factory.cargoTraffic).Links(entityId)
                : NativeNeighbors(factory, entityId, null);
        }

        /// <summary>读取原生输出连接；无向类型返回全部邻居。</summary>
        /// <param name="factory">所属工厂；使用主线程或工厂独占访问。</param>
        /// <param name="entityId">已完成的程序化连接实体。</param>
        /// <returns>可达直接目标的独立数组。</returns>
        public static int[] GetOutgoingNeighbors(PlanetFactory factory, int entityId) =>
            DirectedNeighbors(factory, entityId, true);

        /// <summary>读取原生输入连接；无向类型返回全部邻居。</summary>
        /// <param name="factory">所属工厂；使用主线程或工厂独占访问。</param>
        /// <param name="entityId">已完成的程序化连接实体。</param>
        /// <returns>直接来源实体的独立数组。</returns>
        public static int[] GetIncomingNeighbors(PlanetFactory factory, int entityId) =>
            DirectedNeighbors(factory, entityId, false);

        private static int[] DirectedNeighbors(PlanetFactory factory, int entityId, bool outgoing)
        {
            if (!PipeApi.IsPipe(factory, entityId))
            {
                return Array.Empty<int>();
            }

            return PipeTypeRegistry.IsUndirected(factory.entityPool[entityId].protoId)
                ? GetNeighbors(factory, entityId)
                : NativeNeighbors(factory, entityId, outgoing);
        }

        private static int[] NativeNeighbors(PlanetFactory factory, int entityId, bool? outgoing)
        {
            var neighbors = new List<int>(4);
            CopyNativeNeighbors(factory, entityId, outgoing, neighbors);
            return neighbors.ToArray();
        }

        private static int CopyNativeNeighbors(
            PlanetFactory factory,
            int entityId,
            bool? outgoing,
            List<int> neighbors
        )
        {
            for (int slot = 0; slot < 4; slot++)
            {
                if (
                    PipeConnections.TryReadNativeLink(
                        factory,
                        entityId,
                        slot,
                        out bool output,
                        out int other
                    )
                    && (!outgoing.HasValue || output == outgoing.Value)
                    && other > 0
                    && PipeApi.IsPipe(factory, other)
                    && !neighbors.Contains(other)
                )
                {
                    neighbors.Add(other);
                }
            }
            return neighbors.Count;
        }

        /// <summary>将直接邻居写入调用方可复用的缓冲区。</summary>
        /// <param name="factory">所属工厂；使用主线程或工厂独占访问。</param>
        /// <param name="entityId">已完成的程序化连接实体。</param>
        /// <param name="destination">先清空再填入结果；容量充足时沿用现有存储。</param>
        /// <returns>写入的节点数；无效目标清空缓冲区并返回零。</returns>
        /// <exception cref="ArgumentNullException">destination 为 null。</exception>
        public static int CopyNeighbors(
            PlanetFactory factory,
            int entityId,
            List<int> destination
        ) => CopyNeighbors(factory, entityId, destination, null);

        /// <summary>将输入邻居写入可复用缓冲区；无向类型读取全部邻居。</summary>
        /// <param name="factory">所属工厂；使用主线程或工厂独占访问。</param>
        /// <param name="entityId">已完成的程序化连接实体。</param>
        /// <param name="destination">先清空再填入结果；容量充足时沿用现有存储。</param>
        /// <returns>写入的节点数；无效目标清空缓冲区并返回零。</returns>
        /// <exception cref="ArgumentNullException">destination 为 null。</exception>
        public static int CopyIncomingNeighbors(
            PlanetFactory factory,
            int entityId,
            List<int> destination
        ) => CopyNeighbors(factory, entityId, destination, false);

        /// <summary>将输出邻居写入可复用缓冲区；无向类型读取全部邻居。</summary>
        /// <param name="factory">所属工厂；使用主线程或工厂独占访问。</param>
        /// <param name="entityId">已完成的程序化连接实体。</param>
        /// <param name="destination">先清空再填入结果；容量充足时沿用现有存储。</param>
        /// <returns>写入的节点数；无效目标清空缓冲区并返回零。</returns>
        /// <exception cref="ArgumentNullException">destination 为 null。</exception>
        public static int CopyOutgoingNeighbors(
            PlanetFactory factory,
            int entityId,
            List<int> destination
        ) => CopyNeighbors(factory, entityId, destination, true);

        private static int CopyNeighbors(
            PlanetFactory factory,
            int entityId,
            List<int> destination,
            bool? outgoing
        )
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            destination.Clear();
            if (!PipeApi.IsPipe(factory, entityId))
            {
                return 0;
            }
            return PipeTypeRegistry.IsUndirected(factory.entityPool[entityId].protoId)
                ? PipeRuntime.Bind(factory.cargoTraffic).CopyLinks(entityId, destination)
                : CopyNativeNeighbors(factory, entityId, outgoing, destination);
        }

        /// <summary>将弱连通分量写入可复用缓冲区，开销与分量大小成正比。</summary>
        /// <param name="factory">所属工厂；使用主线程或工厂独占访问。</param>
        /// <param name="entityId">已完成的程序化连接实体。</param>
        /// <param name="destination">先清空再填入结果；容量充足时沿用现有存储。</param>
        /// <returns>写入的节点数；无效目标清空缓冲区并返回零。</returns>
        /// <exception cref="ArgumentNullException">destination 为 null。</exception>
        public static int CopyConnectedNodes(
            PlanetFactory factory,
            int entityId,
            List<int> destination
        )
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            destination.Clear();
            return PipeApi.IsPipe(factory, entityId)
                ? PipeRuntime.Bind(factory.cargoTraffic).CopyMembers(entityId, destination)
                : 0;
        }

        /// <summary>读取工厂当前的拓扑版本，供连接查询缓存使用。</summary>
        /// <param name="factory">所属工厂；使用主线程或工厂独占访问。</param>
        /// <param name="version">当前版本；无效目标返回零。</param>
        /// <returns>库已就绪且工厂可用时返回 true。</returns>
        /// <remarks>版本通过缓存读取，开销为 O(1)。版本在同一工厂的运行时图生命周期内有效；载入存档或更换工厂时重新建立缓存。</remarks>
        public static bool TryGetTopologyVersion(PlanetFactory factory, out long version)
        {
            version = 0;
            if (!PipeApi.IsReady || factory?.cargoTraffic?.container == null)
            {
                return false;
            }
            version = PipeRuntime.Bind(factory.cargoTraffic).TopologyVersion;
            return true;
        }

        /// <summary>在主线程或工厂独占访问下以 O(分量大小) 的开销读取连接分量。有向类型返回其弱连通分量。</summary>
        /// <param name="factory">所属工厂。</param>
        /// <param name="entityId">已完成的程序化连接实体。</param>
        /// <returns>分量实体 ID 的独立数组；目标无效时返回空数组。</returns>
        public static int[] GetConnectedNodes(PlanetFactory factory, int entityId) =>
            PipeApi.IsPipe(factory, entityId)
                ? PipeRuntime.Bind(factory.cargoTraffic).Members(entityId)
                : new int[0];

        internal static void Changed(PlanetFactory factory, int entityId) =>
            PipeTypeNotifications.Changed(factory, entityId);

        internal static void FlushChanges() => PipeTypeNotifications.FlushChanges();

        internal static void ClearChanges() => PipeTypeNotifications.ClearChanges();
    }
}
