using System;

namespace FluidPipes.Integration.Belts
{
    internal static class PipeCargoAdapter
    {
        internal static bool Insert(
            PipeGraph graph,
            int pipeEntityId,
            int itemId,
            int count,
            int proliferatorPoints
        ) => PipePools.Insert(graph, pipeEntityId, itemId, count, proliferatorPoints);

        internal static void InsertAvailable(
            PipeGraph graph,
            int pipeEntityId,
            int itemId,
            ref int count,
            ref int proliferatorPoints
        )
        {
            if (
                count <= 0
                || proliferatorPoints < 0
                || proliferatorPoints > (long)count * 10
                || !TransportItemRegistry.IsAllowed(itemId)
            )
            {
                return;
            }

            var view = PipePools.Query(graph, pipeEntityId);
            if (view.Conflict || view.Item != 0 && view.Item != itemId)
            {
                return;
            }

            int acceptedCount = (int)Math.Min(count, Math.Max(0, view.Capacity - view.Count));
            int acceptedPoints = (int)((long)proliferatorPoints * acceptedCount / count);
            if (!PipePools.Insert(graph, pipeEntityId, itemId, acceptedCount, acceptedPoints))
            {
                return;
            }

            count -= acceptedCount;
            proliferatorPoints -= acceptedPoints;
        }

        internal static int Pick(
            PipeGraph graph,
            int pipeEntityId,
            int filterItemId,
            int[] neededItemIds,
            out byte stackCount,
            out byte proliferatorPoints
        )
        {
            var amount = PipePools.Take(graph, pipeEntityId, 1, filterItemId, neededItemIds);
            stackCount = (byte)amount.Count;
            proliferatorPoints = (byte)amount.ProliferatorPoints;
            return amount.Item;
        }
    }
}
