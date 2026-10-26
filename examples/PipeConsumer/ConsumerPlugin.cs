using System;
using BepInEx;
using FluidPipes.API;

namespace PipeConsumer
{
    /// <summary>注册铁块以通过管道运输，并演示有界库存交互。</summary>
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

        /// <summary>将整批物品放入管道，仅在成功时扣减传入的计数器。</summary>
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

        /// <summary>最多提取调用方预留的空闲容量。</summary>
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
