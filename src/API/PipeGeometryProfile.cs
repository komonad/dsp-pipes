using System;

namespace FluidPipes.API
{
    /// <summary>Immutable dimensions for a procedural pipe type, expressed in Unity world units.</summary>
    public sealed class PipeGeometryProfile
    {
        /// <summary>The standard pipe dimensions, shared by types that omit a geometry profile.</summary>
        public static PipeGeometryProfile Default { get; } = new PipeGeometryProfile();

        /// <summary>The outer wall radius in world units.</summary>
        public float Radius { get; }

        /// <summary>The radial centerline offset from the native entity position, in world units.</summary>
        public float CenterHeight { get; }

        /// <summary>The multiplier applied to retaining-ring thickness and axial depth; fitting bores follow the wall radius.</summary>
        public float FittingScale { get; }

        /// <summary>The axial width of a central support ring on straight segments and endpoints, in world units. Zero omits the ring.</summary>
        public float SupportRingWidth { get; }

        /// <summary>Defines pipe, fitting and optional support-ring dimensions.</summary>
        /// <param name="radius">Outer wall radius, from 0.08 through 1.2 world units.</param>
        /// <param name="centerHeight">Radial centerline offset from the native entity position, from zero through 2 world units.</param>
        /// <param name="fittingScale">Retaining-ring thickness and depth multiplier, from 0.75 through 1.5.</param>
        /// <param name="supportRingWidth">Central support-ring width, from zero through the outer wall radius. Zero omits the ring.</param>
        /// <exception cref="ArgumentOutOfRangeException">A dimension is nonfinite or outside its supported interval.</exception>
        public PipeGeometryProfile(
            float radius = 0.32f,
            float centerHeight = 0.34f,
            float fittingScale = 1f,
            float supportRingWidth = 0f
        )
        {
            RequireRange(radius, 0.08f, 1.2f, nameof(radius));
            RequireRange(centerHeight, 0, 2, nameof(centerHeight));
            RequireRange(fittingScale, 0.75f, 1.5f, nameof(fittingScale));
            RequireRange(supportRingWidth, 0, radius, nameof(supportRingWidth));
            Radius = radius;
            CenterHeight = centerHeight;
            FittingScale = fittingScale;
            SupportRingWidth = supportRingWidth;
        }

        private static void RequireRange(
            float value,
            float minimum,
            float maximum,
            string parameter
        )
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(
                    parameter,
                    value,
                    "A finite dimension within the supported interval is required."
                );
            }
        }
    }
}
