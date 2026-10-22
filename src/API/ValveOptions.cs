using System;

namespace FluidPipes.API
{
    /// <summary>Permitted transfer direction between the valve's native ports A and B.</summary>
    public enum FlowDirection
    {
        /// <summary>Transfers may occur in either direction.</summary>
        Both,

        /// <summary>Only transfers from A to B are permitted.</summary>
        AtoB,

        /// <summary>Only transfers from B to A are permitted.</summary>
        BtoA,
    }

    /// <summary>The fill-level objective used to choose transfer direction and quantity.</summary>
    public enum LevelControl
    {
        /// <summary>Reduces the fill-fraction difference between both sides.</summary>
        Equalize,

        /// <summary>Maintains side A at the target fraction.</summary>
        KeepA,

        /// <summary>Maintains side B at the target fraction.</summary>
        KeepB,
    }

    /// <summary>Immutable pump settings. Rate is items/second; target is a capacity fraction from zero to one.</summary>
    public sealed class ValveOptions
    {
        /// <summary>Whether the valve may request power and transfer items.</summary>
        public bool Enabled { get; }

        /// <summary>Maximum transfer rate before scaling by available electrical power.</summary>
        public double ItemsPerSecond { get; }

        /// <summary>Desired fill fraction of the selected side, from zero to one.</summary>
        public double TargetFraction { get; }

        /// <summary>The permitted transfer direction.</summary>
        public FlowDirection Direction { get; }

        /// <summary>The fill-level objective.</summary>
        public LevelControl Control { get; }

        /// <summary>Constructs an immutable, validated set of pump settings.</summary>
        /// <param name="enabled">Whether transfer is enabled.</param>
        /// <param name="itemsPerSecond">Finite maximum rate in the inclusive range 0–1,000,000.</param>
        /// <param name="targetFraction">Finite target fill fraction in the inclusive range 0–1.</param>
        /// <param name="direction">Permitted transfer direction.</param>
        /// <param name="control">Fill-level objective.</param>
        /// <exception cref="ArgumentOutOfRangeException">A rate, fraction or enum value is outside its range.</exception>
        public ValveOptions(
            bool enabled = true,
            double itemsPerSecond = 60,
            double targetFraction = 0.5,
            FlowDirection direction = FlowDirection.Both,
            LevelControl control = LevelControl.Equalize
        )
        {
            if (
                double.IsNaN(itemsPerSecond)
                || double.IsInfinity(itemsPerSecond)
                || itemsPerSecond < 0
                || itemsPerSecond > 1000000
            )
            {
                throw new ArgumentOutOfRangeException(nameof(itemsPerSecond));
            }

            if (
                double.IsNaN(targetFraction)
                || double.IsInfinity(targetFraction)
                || targetFraction < 0
                || targetFraction > 1
            )
            {
                throw new ArgumentOutOfRangeException(nameof(targetFraction));
            }

            if (direction < FlowDirection.Both || direction > FlowDirection.BtoA)
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }

            if (control < LevelControl.Equalize || control > LevelControl.KeepB)
            {
                throw new ArgumentOutOfRangeException(nameof(control));
            }

            Enabled = enabled;
            ItemsPerSecond = itemsPerSecond;
            TargetFraction = targetFraction;
            Direction = direction;
            Control = control;
        }
    }
}
