namespace FluidPipes.API
{
    /// <summary>管壁的渲染类别。</summary>
    public enum PipeWallMode
    {
        /// <summary>透明管壁，具有独立的内侧与外侧渲染通道。</summary>
        Glass,

        /// <summary>写入深度的不透明管壁。</summary>
        Opaque,

        /// <summary>外露的结构内芯，以配置的管道尺寸作为其碰撞包围体。</summary>
        Open,
    }
}
