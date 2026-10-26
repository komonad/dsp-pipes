namespace FluidPipes.API
{
    /// <summary>程序化连接类型提供的模拟。</summary>
    public enum PipeBehavior
    {
        /// <summary>连通节点参与共享物品库存。</summary>
        SharedInventory,

        /// <summary>节点为外部模拟提供建造与拓扑。</summary>
        Structure,
    }
}
