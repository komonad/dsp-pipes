using System;

namespace FluidPipes
{
    internal enum InfiniteLevelMode
    {
        AtLeast,
        AtMost,
        Exactly,
    }

    internal static class SandboxValve
    {
        internal static bool Available => GameMain.data?.gameDesc?.isSandboxMode == true;

        internal static long Step(PlanetFactory factory, int entityId, ValveSettings settings)
        {
            if (!Available || !settings.Enabled || !ItemProto.isFluid(settings.InfiniteItemId))
            {
                return 0;
            }
            PipeValves.Ends(factory, entityId, out int firstPipe, out int secondPipe);
            int pipe = settings.InfiniteSide == 0 ? firstPipe : secondPipe;
            if (pipe == 0)
            {
                return 0;
            }
            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            var inventory = PipePools.View(graph, pipe, true);
            if (
                inventory.Conflict
                || inventory.Item != 0 && inventory.Item != settings.InfiniteItemId
            )
            {
                return 0;
            }
            long target = (long)Math.Floor(inventory.Capacity * settings.InfiniteTarget);
            long difference = target - inventory.Count;
            if (difference > 0 && settings.InfiniteMode != InfiniteLevelMode.AtMost)
            {
                int count = (int)Math.Min(int.MaxValue, difference);
                return PipePools.Insert(graph, pipe, settings.InfiniteItemId, count, 0) ? count : 0;
            }
            if (difference < 0 && settings.InfiniteMode != InfiniteLevelMode.AtLeast)
            {
                var removed = PipePools.Take(
                    graph,
                    pipe,
                    (int)Math.Min(int.MaxValue, -difference),
                    settings.InfiniteItemId
                );
                return -removed.Count;
            }
            return 0;
        }
    }
}
