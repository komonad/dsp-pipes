using System.IO;
using System.Linq;
using FluidPipes.API;

namespace FluidPipes
{
    internal static partial class PipeMachinePorts
    {
        public static void Export(BinaryWriter writer)
        {
            var activeStates = states.OrderBy(entry => entry.Key).ToArray();
            writer.Write(activeStates.Length);
            foreach (var entry in activeStates)
            {
                writer.Write(entry.Key);
                lock (entry.Value.Sync)
                {
                    writer.Write(entry.Value.Machines.Count);
                    foreach (
                        var connection in entry.Value.Machines.Values.OrderBy(connection =>
                            Key(connection.MachineEntityId, connection.SlotIndex)
                        )
                    )
                    {
                        writer.Write(connection.MachineEntityId);
                        writer.Write(connection.SlotIndex);
                        writer.Write(connection.PipeEntityId);
                        writer.Write(connection.PipeSlotIndex);
                        writer.Write(connection.Options.ItemId);
                        writer.Write((int)connection.Options.Direction);
                        writer.Write(connection.Options.Enabled);
                        writer.Write(connection.Pending.Item);
                        writer.Write((int)connection.Pending.Count);
                        writer.Write((int)connection.Pending.ProliferatorPoints);
                    }
                }
            }
        }

        public static void Import(BinaryReader reader)
        {
            Clear();
            int planetCount = reader.ReadInt32();
            if (planetCount < 0 || planetCount > 100000)
            {
                throw new InvalidDataException("Invalid machine-port planet count.");
            }
            for (int planetIndex = 0; planetIndex < planetCount; planetIndex++)
            {
                int planetId = reader.ReadInt32();
                int count = reader.ReadInt32();
                var state = new FactoryState();
                if (
                    planetId <= 0
                    || count < 0
                    || count > 1000000
                    || !states.TryAdd(planetId, state)
                )
                {
                    throw new InvalidDataException("Invalid machine-port factory record.");
                }
                for (int index = 0; index < count; index++)
                {
                    var connection = new Connection
                    {
                        MachineEntityId = reader.ReadInt32(),
                        SlotIndex = reader.ReadInt32(),
                        PipeEntityId = reader.ReadInt32(),
                        PipeSlotIndex = reader.ReadInt32(),
                        Options = new MachinePortOptions(
                            reader.ReadInt32(),
                            (MachinePortDirection)reader.ReadInt32(),
                            reader.ReadBoolean()
                        ),
                        Pending = new FluidAmount(
                            reader.ReadInt32(),
                            reader.ReadInt32(),
                            reader.ReadInt32()
                        ),
                    };
                    long machineKey = Key(connection.MachineEntityId, connection.SlotIndex);
                    long pipeKey = Key(connection.PipeEntityId, connection.PipeSlotIndex);
                    if (
                        connection.MachineEntityId == 0
                        || connection.MachineEntityId == int.MinValue
                        || connection.PipeEntityId == 0
                        || connection.PipeEntityId == int.MinValue
                        || connection.SlotIndex < 0
                        || connection.SlotIndex >= 16
                        || connection.PipeSlotIndex < 0
                        || connection.PipeSlotIndex >= 4
                        || state.Machines.ContainsKey(machineKey)
                        || state.Pipes.ContainsKey(pipeKey)
                        || connection.Pending.Count < 0
                        || connection.Pending.Count > 255
                        || connection.Pending.ProliferatorPoints < 0
                        || connection.Pending.ProliferatorPoints > connection.Pending.Count * 10
                        || connection.Pending.Count > 0
                            && connection.Pending.Item != connection.Options.ItemId
                    )
                    {
                        throw new InvalidDataException("Invalid machine-port connection record.");
                    }
                    state.Machines[machineKey] = connection;
                    state.Pipes[pipeKey] = connection;
                }
            }
        }
    }
}
