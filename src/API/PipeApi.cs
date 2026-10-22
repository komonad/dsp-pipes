using System;

namespace FluidPipes.API
{
    /// <summary>Supported integration surface for pipe inventories and item admission.</summary>
    /// <remarks>
    /// Factory operations require the Unity main thread or an exclusive factory simulation context.
    /// Native entity IDs can be reused; refresh factory/entity bindings after save or entity changes.
    /// Returned values are detached snapshots; native reservoir changes reconcile on simulation ticks.
    /// </remarks>
    public static class PipeApi
    {
        /// <summary>BepInEx plugin dependency GUID.</summary>
        public const string PluginGuid = "com.komonad.dsp.fluidpipes";

        /// <summary>The mod release version for minimum-version dependency declarations.</summary>
        public const string ModVersion = "0.4.0";

        /// <summary>The supported API contract generation.</summary>
        public const int ApiVersion = 1;

        /// <summary>Whether prototypes and the initial item policy are initialized.</summary>
        public static bool IsReady => Prototypes.Ready;

        /// <summary>The registered pipe item ID, or zero before initialization.</summary>
        public static int PipeItemId => IsReady ? Prototypes.ItemId : 0;

        /// <summary>The registered valve item ID, or zero before initialization.</summary>
        public static int ValveItemId => IsReady ? Prototypes.ValveId : 0;

        /// <summary>The pipe crafting recipe ID, or zero before initialization.</summary>
        public static int PipeRecipeId => IsReady ? Prototypes.RecipeId : 0;

        /// <summary>The valve crafting recipe ID, or zero before initialization.</summary>
        public static int ValveRecipeId => IsReady ? Prototypes.ValveRecipeId : 0;

        /// <summary>Tests the effective admission policy. False before item prototypes are initialized.</summary>
        /// <param name="itemId">A registered item prototype ID.</param>
        /// <returns>True when an item is admitted, before container-specific restrictions.</returns>
        public static bool IsItemAllowed(int itemId) =>
            IsReady && TransportItemRegistry.IsAllowed(itemId);

        /// <summary>Grants item IDs until the returned registration is disposed. Explicit configuration exclusions take precedence.</summary>
        /// <remarks>Register and dispose on the Unity main thread. Registration may precede prototype initialization.</remarks>
        /// <param name="itemIds">Positive item IDs resolved against the initialized prototype database; the array is copied.</param>
        /// <returns>An independently owned registration. Disposing it leaves other registrations intact.</returns>
        /// <exception cref="ArgumentNullException">The array is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An ID is zero or negative.</exception>
        /// <exception cref="InvalidOperationException">Called outside the Unity main thread.</exception>
        public static IDisposable RegisterTransportItems(params int[] itemIds) =>
            TransportItemRegistry.Register(itemIds);

        /// <summary>Identifies completed pipe entities through native entity and belt components.</summary>
        /// <param name="factory">The factory owning the entity.</param>
        /// <param name="entityId">A positive native EntityData.id resolved through the factory's entity pool.</param>
        /// <returns>True for a valid completed pipe; false for null, stale or unsupported targets.</returns>
        public static bool IsPipe(PlanetFactory factory, int entityId) =>
            IsReady
            && factory?.cargoTraffic != null
            && entityId > 0
            && factory.entityPool != null
            && entityId < factory.entityPool.Length
            && factory.entityPool[entityId].id == entityId
            && PipeRuntime.IsPipe(factory.cargoTraffic, factory.entityPool[entityId].beltId);

        /// <summary>Reads shared inventory totals through the cached component index.</summary>
        /// <param name="factory">The factory owning the pipe.</param>
        /// <param name="entityId">A completed pipe entity ID.</param>
        /// <param name="inventory">A detached inventory snapshot, or its default value on failure.</param>
        /// <returns>True for a valid pipe, including empty or conflicting storage spaces.</returns>
        public static bool TryGetInventory(
            PlanetFactory factory,
            int entityId,
            out NetworkInventory inventory
        )
        {
            inventory = default(NetworkInventory);
            if (!Resolve(factory, entityId, out var graph))
            {
                return false;
            }

            inventory = new NetworkInventory(PipePools.Query(graph, entityId));
            return true;
        }

        /// <summary>Reads inventory plus pipe/container capacity totals.</summary>
        /// <param name="factory">The factory owning the pipe.</param>
        /// <param name="entityId">A completed pipe entity ID.</param>
        /// <param name="snapshot">A detached breakdown, or its default value on failure.</param>
        /// <returns>True for a valid pipe. Aggregation visits connected reservoirs and pipe components.</returns>
        public static bool TryGetNetwork(
            PlanetFactory factory,
            int entityId,
            out NetworkSnapshot snapshot
        )
        {
            snapshot = default(NetworkSnapshot);
            if (!Resolve(factory, entityId, out var graph))
            {
                return false;
            }

            snapshot = new NetworkSnapshot(PipePools.View(graph, entityId));
            return true;
        }

        /// <summary>Commits a complete quantity after admission, item-identity and capacity validation.</summary>
        /// <param name="factory">The factory owning the pipe.</param>
        /// <param name="entityId">A completed pipe entity ID.</param>
        /// <param name="itemId">The admitted item to insert.</param>
        /// <param name="count">A positive item count.</param>
        /// <param name="proliferatorPoints">Total points, between zero and ten times count.</param>
        /// <returns>False for invalid arguments, insufficient capacity, item conflicts or container restrictions.</returns>
        /// <remarks>The caller debits its source after success; a rejected batch remains with the caller.</remarks>
        public static bool TryInsert(
            PlanetFactory factory,
            int entityId,
            int itemId,
            int count,
            int proliferatorPoints = 0
        ) =>
            Resolve(factory, entityId, out var graph)
            && PipePools.Insert(graph, entityId, itemId, count, proliferatorPoints);

        /// <summary>Extracts up to maxCount from the shared space. Filter zero accepts the current item.</summary>
        /// <param name="factory">The factory owning the pipe.</param>
        /// <param name="entityId">A completed pipe entity ID.</param>
        /// <param name="maxCount">A positive upper bound on the quantity to remove.</param>
        /// <param name="amount">The removed items and points, or an empty result on failure.</param>
        /// <param name="filterItemId">Zero for any item, or a positive required item ID.</param>
        /// <returns>True when at least one item was removed. Extraction also permits previously admitted items.</returns>
        /// <remarks>The caller owns extracted items, reserves destination capacity and holds any remaining quantity.</remarks>
        public static bool TryTake(
            PlanetFactory factory,
            int entityId,
            int maxCount,
            out TransportAmount amount,
            int filterItemId = 0
        )
        {
            amount = default(TransportAmount);
            if (maxCount <= 0 || filterItemId < 0 || !Resolve(factory, entityId, out var graph))
            {
                return false;
            }

            var taken = PipePools.Take(graph, entityId, maxCount, filterItemId);
            amount = new TransportAmount(taken);
            return taken.Count > 0;
        }

        /// <summary>Checks shared storage identity, including connections bridged by native containers.</summary>
        /// <param name="factory">The factory owning both pipes.</param>
        /// <param name="firstEntityId">The first completed pipe entity ID.</param>
        /// <param name="secondEntityId">The second completed pipe entity ID.</param>
        /// <returns>True when both valid pipes access the same inventory.</returns>
        public static bool ShareInventory(
            PlanetFactory factory,
            int firstEntityId,
            int secondEntityId
        ) =>
            Resolve(factory, firstEntityId, out var graph)
            && IsPipe(factory, secondEntityId)
            && PipePools.SameSpace(graph, firstEntityId, secondEntityId);

        /// <summary>Reads a detached copy of a valve's settings.</summary>
        /// <param name="factory">The factory owning the valve.</param>
        /// <param name="entityId">A completed valve entity ID.</param>
        /// <param name="options">The current settings, or null for an invalid target.</param>
        /// <returns>True for a valid valve.</returns>
        public static bool TryGetValveOptions(
            PlanetFactory factory,
            int entityId,
            out ValveOptions options
        )
        {
            options = null;
            if (!ValidValve(factory, entityId))
            {
                return false;
            }

            var current = PipeValves.Get(factory, entityId);
            options = new ValveOptions(
                current.Enabled,
                current.Rate,
                current.Target,
                (FlowDirection)current.Direction,
                (LevelControl)current.Level
            );
            return true;
        }

        /// <summary>Applies validated valve settings and resets fractional transfer credit.</summary>
        /// <param name="factory">The factory owning the valve.</param>
        /// <param name="entityId">A completed valve entity ID.</param>
        /// <param name="options">The immutable settings to apply.</param>
        /// <returns>True for a valid valve; false leaves its settings unchanged.</returns>
        /// <exception cref="ArgumentNullException">Options is null.</exception>
        public static bool TrySetValveOptions(
            PlanetFactory factory,
            int entityId,
            ValveOptions options
        )
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (!ValidValve(factory, entityId))
            {
                return false;
            }

            var current = PipeValves.Get(factory, entityId);
            current.Enabled = options.Enabled;
            current.Rate = options.ItemsPerSecond;
            current.Target = options.TargetFraction;
            current.Direction = (ValveDirection)options.Direction;
            current.Level = (ValveLevel)options.Control;
            current.Credit = 0;
            return true;
        }

        private static bool Resolve(PlanetFactory factory, int entityId, out PipeGraph graph)
        {
            graph = null;
            if (!IsPipe(factory, entityId))
            {
                return false;
            }

            graph = PipeRuntime.Bind(factory.cargoTraffic);
            return graph.View(entityId).Nodes > 0;
        }

        private static bool ValidValve(PlanetFactory factory, int entityId) =>
            IsReady
            && factory?.entityPool != null
            && entityId > 0
            && entityId < factory.entityPool.Length
            && PipeValves.IsValve(factory, entityId);
    }
}
