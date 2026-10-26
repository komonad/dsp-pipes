using System;

namespace FluidPipes.API
{
    /// <summary>The transfer direction at a production-machine connection.</summary>
    public enum MachinePortDirection
    {
        /// <summary>Supply the machine's recipe input buffer from the pipe inventory.</summary>
        IntoMachine,

        /// <summary>Collect the machine's recipe products into the pipe inventory.</summary>
        OutOfMachine,
    }

    /// <summary>Immutable direction, item filter and enable state for one machine slot.</summary>
    public sealed class MachinePortOptions
    {
        /// <summary>The recipe item assigned to this connection; zero awaits item selection.</summary>
        public int ItemId { get; }

        /// <summary>The transfer direction relative to the machine.</summary>
        public MachinePortDirection Direction { get; }

        /// <summary>Whether the configured connection participates in transfer and item reservation.</summary>
        public bool Enabled { get; }

        /// <summary>Creates connection settings. Recipe and network compatibility are checked when applied.</summary>
        /// <param name="itemId">A positive item ID, or zero to await item selection.</param>
        /// <param name="direction">The transfer direction relative to the machine.</param>
        /// <param name="enabled">Whether the connection participates in transfer and item reservation.</param>
        /// <exception cref="ArgumentOutOfRangeException">The item ID or direction is invalid.</exception>
        public MachinePortOptions(
            int itemId = 0,
            MachinePortDirection direction = MachinePortDirection.IntoMachine,
            bool enabled = true
        )
        {
            if (itemId < 0 || !Enum.IsDefined(typeof(MachinePortDirection), direction))
            {
                throw new ArgumentOutOfRangeException(
                    itemId < 0 ? nameof(itemId) : nameof(direction)
                );
            }

            ItemId = itemId;
            Direction = direction;
            Enabled = enabled;
        }
    }

    /// <summary>A detached view of a native machine-slot connection.</summary>
    public readonly struct MachinePortSnapshot
    {
        /// <summary>The pipe entity or negative prebuild ID.</summary>
        public int PipeEntityId { get; }

        /// <summary>The machine entity or negative prebuild ID.</summary>
        public int MachineEntityId { get; }

        /// <summary>The zero-based native inserter slot on the machine.</summary>
        public int SlotIndex { get; }

        /// <summary>The immutable settings currently assigned to the connection.</summary>
        public MachinePortOptions Options { get; }

        internal MachinePortSnapshot(PipeMachinePorts.Connection connection)
        {
            PipeEntityId = connection.PipeEntityId;
            MachineEntityId = connection.MachineEntityId;
            SlotIndex = connection.SlotIndex;
            Options = connection.Options;
        }
    }
}
