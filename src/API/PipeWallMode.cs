namespace FluidPipes.API
{
    /// <summary>The rendering category of the pipe wall.</summary>
    public enum PipeWallMode
    {
        /// <summary>A transparent wall with separate interior and exterior passes.</summary>
        Glass,

        /// <summary>An opaque wall that writes depth.</summary>
        Opaque,

        /// <summary>An exposed structural interior using the configured pipe dimensions as its collision envelope.</summary>
        Open,
    }
}
