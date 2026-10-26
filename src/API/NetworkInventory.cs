namespace FluidPipes.API
{
    /// <summary>A detached view of the entire shared inventory, including attached native reservoirs.</summary>
    public readonly struct NetworkInventory
    {
        /// <summary>The stored or reserved item ID, including tower and enabled machine-port filters; zero denotes an untyped empty space.</summary>
        public int ItemId { get; }

        /// <summary>Total items in pipes and attached reservoirs.</summary>
        public long Count { get; }

        /// <summary>Total conserved proliferator points carried by the stored items.</summary>
        public long ProliferatorPoints { get; }

        /// <summary>Nominal item capacity of the shared space; each pipe node contributes 10.</summary>
        public long Capacity { get; }

        /// <summary>Whether incompatible container contents or filters prevent inventory operations.</summary>
        public bool HasConflict { get; }

        /// <summary>Whether an attached native tank restricts this space to native fluid items.</summary>
        public bool RequiresFluid { get; }

        /// <summary>Stored count divided by capacity; zero when capacity is zero.</summary>
        /// <remarks>Retained stock from higher-capacity saves can produce a value greater than one.</remarks>
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

    /// <summary>A detached inventory and capacity breakdown for one connected storage space.</summary>
    public readonly struct NetworkSnapshot
    {
        /// <summary>The inventory shared by all connected pipes and reservoirs.</summary>
        public NetworkInventory Inventory { get; }

        /// <summary>Number of completed pipe nodes contributing capacity.</summary>
        public int PipeNodeCount { get; }

        /// <summary>Number of attached tanks and selected tower storage slots, counted once each.</summary>
        public int ContainerCount { get; }

        /// <summary>Items currently assigned to pipe components, excluding native reservoirs.</summary>
        public long PipeItemCount { get; }

        /// <summary>Capacity contributed by pipes alone.</summary>
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

    /// <summary>An extracted quantity and its conserved proliferator points.</summary>
    public readonly struct TransportAmount
    {
        /// <summary>The extracted item ID; zero for an empty result.</summary>
        public int ItemId { get; }

        /// <summary>The extracted number of items.</summary>
        public int Count { get; }

        /// <summary>Total proliferator points carried by the extracted items.</summary>
        public long ProliferatorPoints { get; }

        internal TransportAmount(FluidAmount amount)
        {
            ItemId = amount.Item;
            Count = checked((int)amount.Count);
            ProliferatorPoints = amount.ProliferatorPoints;
        }
    }
}
