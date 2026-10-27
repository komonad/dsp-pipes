namespace FluidPipes.API
{
    /// <summary>连接类型使用的建造、规划与碰撞规则。</summary>
    public enum PipeConstructionMode
    {
        /// <summary>共享库存管道的建造与接头接触规则。</summary>
        Pipe = 0,

        /// <summary>原生传送带的建造、端口连接与碰撞规则，适用于外部模拟的有向结构。</summary>
        NativeBelt = 1,
    }
}
