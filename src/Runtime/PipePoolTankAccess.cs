namespace FluidPipes
{
    internal static partial class PipePools
    {
        private static Reservoir FindTank(PlanetFactory factory, int tankId, out State state)
        {
            state = null;
            if (
                factory?.cargoTraffic == null
                || factory.factoryStorage?.tankPool == null
                || tankId <= 0
                || tankId >= factory.factoryStorage.tankPool.Length
                || factory.factoryStorage.tankPool[tankId].id != tankId
            )
            {
                return null;
            }
            state = Get(PipeRuntime.Bind(factory.cargoTraffic));
            if (state == null)
            {
                return null;
            }
            lock (state.Sync)
            {
                return state.Tanks.TryGetValue(tankId, out var store) ? store : null;
            }
        }

        public static bool TryTankInventory(
            PlanetFactory factory,
            int tankId,
            out FluidPoolView inventory
        )
        {
            inventory = default(FluidPoolView);
            var tank = FindTank(factory, tankId, out var state);
            if (tank?.Pool == null)
            {
                return false;
            }
            lock (state.Sync)
            {
                // 原生储液罐菜单与物流会在共享空间更新之间修改自身字段。
                tank.Pool.Measure();
                inventory = View(state.Graph, tank.Pool.Representative);
                return true;
            }
        }

        public static bool TryTankInsert(
            PlanetFactory factory,
            int tankId,
            int item,
            int count,
            int points
        )
        {
            var tank = FindTank(factory, tankId, out var state);
            if (tank?.Pool == null)
            {
                return false;
            }
            lock (state.Sync)
            {
                tank.Pool.Measure();
                return Insert(state.Graph, tank.Pool.Representative, item, count, points);
            }
        }

        public static FluidAmount TryTankTake(
            PlanetFactory factory,
            int tankId,
            int max,
            int filter
        )
        {
            var tank = FindTank(factory, tankId, out var state);
            if (tank?.Pool == null)
            {
                return default(FluidAmount);
            }
            lock (state.Sync)
            {
                tank.Pool.Measure();
                return Take(state.Graph, tank.Pool.Representative, max, filter);
            }
        }

        public static bool EmptyTank(PlanetFactory factory, int tankId)
        {
            var tank = FindTank(factory, tankId, out var state);
            if (tank == null)
            {
                return false;
            }
            lock (state.Sync)
            {
                tank.Write(0, 0, 0);
                tank.Pool.Measure();
                return true;
            }
        }

        public static bool EmptyTankNetwork(PlanetFactory factory, int tankId)
        {
            var tank = FindTank(factory, tankId, out var state);
            if (tank?.Pool == null)
            {
                return false;
            }
            lock (state.Sync)
            {
                Empty(state.Graph, tank.Pool.Representative);
                return true;
            }
        }
    }
}
