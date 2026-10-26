using UnityEngine;

namespace FluidPipes.API
{
    /// <summary>A rendering callback context for one spatial and semantic batch.</summary>
    public readonly struct PipeEffectContext
    {
        /// <summary>The factory being rendered.</summary>
        public PlanetFactory Factory { get; }

        /// <summary>A representative completed entity in this batch.</summary>
        public int EntityId { get; }

        /// <summary>The stable semantic grouping key supplied by the type.</summary>
        public int GroupKey { get; }

        /// <summary>Unity animation time in seconds.</summary>
        public float TimeSeconds { get; }

        /// <summary>The surface being drawn.</summary>
        public PipeSurfaceRole Surface { get; }

        internal PipeEffectContext(
            PlanetFactory factory,
            int entityId,
            int groupKey,
            PipeSurfaceRole surface
        )
        {
            Factory = factory;
            EntityId = entityId;
            GroupKey = groupKey;
            TimeSeconds = Time.time;
            Surface = surface;
        }
    }
}
