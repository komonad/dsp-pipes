namespace FluidPipes.API
{
    /// <summary>A detached identity snapshot of a registered type.</summary>
    public readonly struct PipeTypeInfo
    {
        /// <summary>The qualified owner and local type key.</summary>
        public string Key { get; }

        /// <summary>The registering plugin GUID.</summary>
        public string OwnerGuid { get; }

        /// <summary>The initialized game item ID.</summary>
        public int ItemId { get; }

        /// <summary>The simulation category.</summary>
        public PipeBehavior Behavior { get; }

        /// <summary>The construction and connection orientation policy.</summary>
        public PipeDirectionMode DirectionMode { get; }

        /// <summary>The connection family.</summary>
        public string ConnectionFamily { get; }

        internal PipeTypeInfo(PipeTypeRegistry.Entry entry)
        {
            Key = entry.Key;
            OwnerGuid = entry.OwnerGuid;
            ItemId = entry.Definition.Prototype.ID;
            Behavior = entry.Definition.Behavior;
            DirectionMode = entry.Definition.DirectionMode;
            ConnectionFamily = entry.Family;
        }
    }
}
