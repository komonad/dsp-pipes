using System;
using UnityEngine;

namespace FluidPipes.API
{
    /// <summary>The perimeter shape of a longitudinal interior member.</summary>
    public enum PipeInteriorSectionKind
    {
        /// <summary>A circular section.</summary>
        Circle,

        /// <summary>An axis-aligned rectangle with rounded corners.</summary>
        RoundedRectangle,
    }

    /// <summary>Immutable member dimensions normalized by the outer pipe radius.</summary>
    /// <remarks>Surface U follows normalized distance around the perimeter, including rounded corner arcs. The seam duplicates the same position at U = 0 and U = 1. Longitudinal V follows the appearance's texture-coordinate mapping.</remarks>
    public sealed class PipeInteriorSection
    {
        /// <summary>The perimeter shape.</summary>
        public PipeInteriorSectionKind Kind { get; }

        /// <summary>The full lateral width divided by the outer pipe radius.</summary>
        public float WidthFraction { get; }

        /// <summary>The full radial height divided by the outer pipe radius.</summary>
        public float HeightFraction { get; }

        /// <summary>The corner-rounding radius divided by the outer pipe radius.</summary>
        public float BevelFraction { get; }

        /// <summary>A radius enclosing the section, divided by the outer pipe radius.</summary>
        public float RadiusFraction { get; }

        private PipeInteriorSection(
            PipeInteriorSectionKind kind,
            float width,
            float height,
            float bevel,
            float radius
        )
        {
            Kind = kind;
            WidthFraction = width;
            HeightFraction = height;
            BevelFraction = bevel;
            RadiusFraction = radius;
        }

        /// <summary>Defines a circular member section.</summary>
        /// <param name="radiusFraction">Radius divided by the outer pipe radius, from 0.01 through 0.85.</param>
        /// <exception cref="ArgumentOutOfRangeException">The radius falls outside the finite supported interval.</exception>
        public static PipeInteriorSection Circle(float radiusFraction)
        {
            RequireRange(radiusFraction, 0.01f, 0.85f, nameof(radiusFraction));
            return new PipeInteriorSection(
                PipeInteriorSectionKind.Circle,
                radiusFraction * 2,
                radiusFraction * 2,
                radiusFraction,
                radiusFraction
            );
        }

        /// <summary>Defines a rectangular member with circular corner rounding.</summary>
        /// <param name="widthFraction">Full lateral width divided by the outer pipe radius, from 0.02 through 1.7.</param>
        /// <param name="heightFraction">Full radial height divided by the outer pipe radius, from 0.02 through 1.7.</param>
        /// <param name="bevelFraction">Corner radius divided by the outer pipe radius, from zero through half the smaller side.</param>
        /// <exception cref="ArgumentOutOfRangeException">A dimension falls outside its finite supported interval.</exception>
        public static PipeInteriorSection RoundedRectangle(
            float widthFraction,
            float heightFraction,
            float bevelFraction
        )
        {
            RequireRange(widthFraction, 0.02f, 1.7f, nameof(widthFraction));
            RequireRange(heightFraction, 0.02f, 1.7f, nameof(heightFraction));
            RequireRange(
                bevelFraction,
                0,
                Mathf.Min(widthFraction, heightFraction) * 0.5f,
                nameof(bevelFraction)
            );
            float radius = new Vector2(widthFraction, heightFraction).magnitude * 0.5f;
            return new PipeInteriorSection(
                PipeInteriorSectionKind.RoundedRectangle,
                widthFraction,
                heightFraction,
                bevelFraction,
                radius
            );
        }

        internal bool Matches(PipeInteriorSection other) =>
            other != null
            && Kind == other.Kind
            && Mathf.Abs(WidthFraction - other.WidthFraction) <= 0.00001f
            && Mathf.Abs(HeightFraction - other.HeightFraction) <= 0.00001f
            && Mathf.Abs(BevelFraction - other.BevelFraction) <= 0.00001f;

        private static void RequireRange(
            float value,
            float minimum,
            float maximum,
            string parameter
        )
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(parameter);
            }
        }
    }
}
