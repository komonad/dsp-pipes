namespace FluidPipes.API
{
    /// <summary>建造与原生连接槽使用的方向策略。</summary>
    public enum PipeDirectionMode
    {
        /// <summary>连接是对称的，建造时没有流向箭头。</summary>
        Undirected,

        /// <summary>连接保留原生输入/输出方向及有向路径闭环。</summary>
        Directed,
    }
}
