namespace FluidPipes.API
{
    /// <summary>The orientation policy used by construction and native connection slots.</summary>
    public enum PipeDirectionMode
    {
        /// <summary>Connections are symmetric and construction has no flow arrows.</summary>
        Undirected,

        /// <summary>Connections retain native input/output orientation and directed path closure.</summary>
        Directed,
    }
}
