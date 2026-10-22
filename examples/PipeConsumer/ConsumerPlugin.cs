using System;
using BepInEx;
using FluidPipes.API;

namespace PipeConsumer
{
    /// <summary>Registers iron ingots for pipe transport and exposes bounded inventory interactions.</summary>
    [BepInPlugin("example.pipeconsumer", "Pipe Consumer Example", "1.0.0")]
    [BepInDependency(PipeApi.PluginGuid, "0.4.0")]
    public sealed class ConsumerPlugin : BaseUnityPlugin
    {
        private IDisposable registration;

        private void Awake()
        {
            registration = PipeApi.RegisterTransportItems(1101);
        }

        private void OnDestroy()
        {
            registration?.Dispose();
        }

        /// <summary>Moves a complete batch into a pipe, consuming the supplied counters only on success.</summary>
        internal bool TrySupply(
            PlanetFactory factory,
            int pipeEntityId,
            int itemId,
            int batchSize,
            ref int count,
            ref int points
        )
        {
            if (batchSize <= 0 || count < batchSize || points < 0 || points > (long)count * 10)
                return false;
            int transferredPoints = (int)((long)points * batchSize / count);
            if (!PipeApi.TryInsert(factory, pipeEntityId, itemId, batchSize, transferredPoints))
                return false;
            count -= batchSize;
            points -= transferredPoints;
            return true;
        }

        /// <summary>Extracts at most the caller's reserved free capacity.</summary>
        internal bool TryReceive(
            PlanetFactory factory,
            int pipeEntityId,
            int freeCapacity,
            out TransportAmount amount
        )
        {
            return PipeApi.TryTake(factory, pipeEntityId, freeCapacity, out amount);
        }
    }
}
