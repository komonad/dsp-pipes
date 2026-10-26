namespace FluidPipes.API
{
    /// <summary>The surface receiving a procedural rendering effect.</summary>
    public enum PipeSurfaceRole
    {
        /// <summary>The outer wall.</summary>
        Wall,

        /// <summary>The transparent interior wall.</summary>
        InnerWall,

        /// <summary>Opaque joints, retaining rings and internal metal members.</summary>
        Fittings,

        /// <summary>The decorative internal conductor or beam.</summary>
        Core,

        /// <summary>The dark machine socket panel.</summary>
        MachinePanel,

        /// <summary>The machine socket casing.</summary>
        MachineShell,
    }
}
