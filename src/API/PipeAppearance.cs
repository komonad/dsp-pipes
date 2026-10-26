using System;
using UnityEngine;

namespace FluidPipes.API
{
    /// <summary>Immutable wall, conductor and effect settings for a connection type.</summary>
    public sealed class PipeAppearance
    {
        /// <summary>The wall rendering category.</summary>
        public PipeWallMode WallMode { get; }

        /// <summary>Whether decorative internal geometry is drawn.</summary>
        public bool HasCore { get; }

        /// <summary>The default single-conductor radius divided by the wall radius.</summary>
        public float CoreRadiusFraction { get; }

        /// <summary>The period used to unwrap longitudinal texture coordinates across adjacent mesh sections. Zero keeps coordinates nonperiodic.</summary>
        public float TextureCoordinatePeriod { get; }

        /// <summary>The optional structural cross-section. A profile enables internal rendering and supplies the members drawn inside the wall.</summary>
        public PipeInteriorProfile Interior { get; }

        internal Action<PipeMaterialSet> ConfigureMaterials { get; }
        internal Action<PipeEffectContext, MaterialPropertyBlock> ApplyEffects { get; }
        internal Func<Vector3, float> TextureCoordinate { get; }

        /// <summary>Defines cached materials and single-conductor effects through the seven-parameter binary interface.</summary>
        /// <param name="wallMode">The wall rendering category.</param>
        /// <param name="configureMaterials">A main-thread callback that configures library-owned material copies once per resource generation.</param>
        /// <param name="hasCore">Whether a decorative conductor is rendered.</param>
        /// <param name="coreRadiusFraction">Conductor radius fraction, greater than zero and at most 0.85.</param>
        /// <param name="applyEffects">A main-thread draw callback writing batch uniforms into a borrowed property block; the block is valid during this callback.</param>
        /// <param name="textureCoordinate">A pure function mapping a world-space centerline position to a longitudinal texture coordinate; null uses local arc length.</param>
        /// <param name="textureCoordinatePeriod">A finite nonnegative period for the texture-coordinate callback. Positive values unwrap adjacent sections to the nearest period; zero preserves callback values.</param>
        /// <exception cref="ArgumentOutOfRangeException">A wall mode, core radius or coordinate period falls outside its supported range.</exception>
        public PipeAppearance(
            PipeWallMode wallMode,
            Action<PipeMaterialSet> configureMaterials,
            bool hasCore,
            float coreRadiusFraction,
            Action<PipeEffectContext, MaterialPropertyBlock> applyEffects,
            Func<Vector3, float> textureCoordinate,
            float textureCoordinatePeriod
        )
            : this(
                wallMode,
                configureMaterials,
                hasCore,
                coreRadiusFraction,
                applyEffects,
                textureCoordinate,
                textureCoordinatePeriod,
                null
            ) { }

        /// <summary>Defines cached materials and optional per-batch effects.</summary>
        /// <param name="wallMode">The wall rendering category.</param>
        /// <param name="configureMaterials">A main-thread callback that configures library-owned material copies once per resource generation.</param>
        /// <param name="hasCore">Whether a decorative conductor is rendered.</param>
        /// <param name="coreRadiusFraction">Conductor radius fraction, greater than zero and at most 0.85.</param>
        /// <param name="applyEffects">A main-thread draw callback writing batch uniforms into a borrowed property block; the block is valid during this callback.</param>
        /// <param name="textureCoordinate">A pure function mapping a world-space centerline position to a longitudinal texture coordinate; null uses local arc length.</param>
        /// <param name="textureCoordinatePeriod">A finite nonnegative period for the texture-coordinate callback. Positive values unwrap each adjacent section to the nearest period; zero preserves the callback values. Local arc-length coordinates retain their default mapping.</param>
        /// <param name="interior">A structural cross-section that enables internal rendering. Null uses the single conductor controlled by hasCore and coreRadiusFraction.</param>
        /// <exception cref="ArgumentOutOfRangeException">A wall mode, core radius or coordinate period falls outside its supported range.</exception>
        public PipeAppearance(
            PipeWallMode wallMode = PipeWallMode.Glass,
            Action<PipeMaterialSet> configureMaterials = null,
            bool hasCore = false,
            float coreRadiusFraction = 0.45f,
            Action<PipeEffectContext, MaterialPropertyBlock> applyEffects = null,
            Func<Vector3, float> textureCoordinate = null,
            float textureCoordinatePeriod = 0,
            PipeInteriorProfile interior = null
        )
        {
            if (!Enum.IsDefined(typeof(PipeWallMode), wallMode))
            {
                throw new ArgumentOutOfRangeException(nameof(wallMode));
            }

            if (
                float.IsNaN(coreRadiusFraction)
                || coreRadiusFraction <= 0
                || coreRadiusFraction > 0.85f
            )
            {
                throw new ArgumentOutOfRangeException(nameof(coreRadiusFraction));
            }

            if (
                float.IsNaN(textureCoordinatePeriod)
                || float.IsInfinity(textureCoordinatePeriod)
                || textureCoordinatePeriod < 0
            )
            {
                throw new ArgumentOutOfRangeException(nameof(textureCoordinatePeriod));
            }

            WallMode = wallMode;
            ConfigureMaterials = configureMaterials;
            HasCore = hasCore || interior != null;
            CoreRadiusFraction = coreRadiusFraction;
            ApplyEffects = applyEffects;
            TextureCoordinate = textureCoordinate;
            TextureCoordinatePeriod = textureCoordinatePeriod;
            Interior = interior;
        }
    }
}
