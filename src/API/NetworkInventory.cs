namespace FluidPipes.API
{
    /// <summary>整个共享库存的独立视图，包括挂接的原生储液罐。</summary>
    public readonly struct NetworkInventory
    {
        /// <summary>存储或预留的物品 ID，包含物流塔与已启用的机器接口过滤器；零表示未指定类型的空位。</summary>
        public int ItemId { get; }

        /// <summary>管道与挂接储液罐中的物品总数。</summary>
        public long Count { get; }

        /// <summary>已存储物品所携带的守恒增产点总数。</summary>
        public long ProliferatorPoints { get; }

        /// <summary>共享空间的标称物品容量；每个管道节点贡献 10。</summary>
        public long Capacity { get; }

        /// <summary>不兼容的容器内容或过滤器是否会阻止库存操作。</summary>
        public bool HasConflict { get; }

        /// <summary>挂接的原生储液罐是否将此空间限制为原生流体物品。</summary>
        public bool RequiresFluid { get; }

        /// <summary>存储数量除以容量；容量为零时为零。</summary>
        /// <remarks>来自更高容量存档的遗留库存可能产生大于 1 的值。</remarks>
        public double FillFraction => Capacity == 0 ? 0 : (double)Count / Capacity;

        internal NetworkInventory(FluidPoolView view)
        {
            ItemId = view.Item;
            Count = view.Count;
            ProliferatorPoints = view.ProliferatorPoints;
            Capacity = view.Capacity;
            HasConflict = view.Conflict;
            RequiresFluid = view.RequiresFluid;
        }
    }

    /// <summary>一个连通存储空间的独立库存与容量明细。</summary>
    public readonly struct NetworkSnapshot
    {
        /// <summary>所有连通的管道与储液罐共享的库存。</summary>
        public NetworkInventory Inventory { get; }

        /// <summary>贡献容量的已建成管道节点数量。</summary>
        public int PipeNodeCount { get; }

        /// <summary>挂接的储液罐与选定的物流塔存储插槽数量，各计一次。</summary>
        public int ContainerCount { get; }

        /// <summary>当前分配给管道组件的物品，不含原生储液罐。</summary>
        public long PipeItemCount { get; }

        /// <summary>仅由管道贡献的容量。</summary>
        public long PipeCapacity { get; }

        internal NetworkSnapshot(FluidPoolView view)
        {
            Inventory = new NetworkInventory(view);
            PipeNodeCount = view.Nodes;
            ContainerCount = view.Containers;
            PipeItemCount = view.PipeCount;
            PipeCapacity = view.PipeCapacity;
        }
    }

    /// <summary>一次提取的数量及其守恒增产点。</summary>
    public readonly struct TransportAmount
    {
        /// <summary>被提取的物品 ID；结果为空时为零。</summary>
        public int ItemId { get; }

        /// <summary>被提取的物品数量。</summary>
        public int Count { get; }

        /// <summary>被提取物品所携带的增产点总数。</summary>
        public long ProliferatorPoints { get; }

        internal TransportAmount(FluidAmount amount)
        {
            ItemId = amount.Item;
            Count = checked((int)amount.Count);
            ProliferatorPoints = amount.ProliferatorPoints;
        }
    }
}
