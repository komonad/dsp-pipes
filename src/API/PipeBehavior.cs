namespace FluidPipes.API
{
    /// <summary>The simulation provided by a procedural connection type.</summary>
    public enum PipeBehavior
    {
        /// <summary>Connected nodes participate in the shared item inventory.</summary>
        SharedInventory,

        /// <summary>Nodes provide construction and topology for an external simulation.</summary>
        Structure,
    }
}
