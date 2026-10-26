using System;
using System.Collections.Generic;
using UnityEngine;

namespace FluidPipes.API
{
    /// <summary>Registers item-backed procedural connections and exposes their topology.</summary>
    /// <remarks>Registration and registration-stage queries use the Unity main thread. Registered definitions remain fixed after prototype initialization. Live factory queries require the main thread or exclusive access to that factory. Entity IDs can be reused after removal; callers maintain cache lifetimes across factory and topology changes.</remarks>
    public static class PipeTypes
    {
        /// <summary>Raised on a subsequent main-thread update after completed connection mutations, once per affected entity per batch. The entity may have been removed; surviving neighbors are included.</summary>
        public static event Action<PlanetFactory, int> ConnectionsChanged
        {
            add { PipeTypeNotifications.ConnectionsChanged += value; }
            remove { PipeTypeNotifications.ConnectionsChanged -= value; }
        }

        /// <summary>Creates an independent draft from the initialized base pipe template.</summary>
        /// <remarks>The template is prepared during the library's LDBTool pre-add callback, including when built-in content is disabled. The caller owns registration of the returned draft.</remarks>
        /// <param name="itemId">The caller's positive item ID.</param>
        /// <param name="nameKey">The localization key for its name.</param>
        /// <param name="descriptionKey">The localization key for its description.</param>
        /// <returns>A draft to register with LDBTool during its pre-add phase.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The item ID is zero or negative.</exception>
        /// <exception cref="ArgumentException">A localization key is empty.</exception>
        /// <exception cref="InvalidOperationException">The registration phase, thread or base template is unavailable.</exception>
        public static ItemProto CreateItem(int itemId, string nameKey, string descriptionKey) =>
            PipeTypeRegistry.CreateItem(itemId, nameKey, descriptionKey);

        /// <summary>Assigns an externally owned item icon during prototype registration.</summary>
        /// <remarks>The sprite and its texture remain valid throughout the database and icon-atlas lifetime. Recipes with an empty IconPath inherit their result item's icon. Registration uses the Unity main thread.</remarks>
        /// <param name="prototype">An independently owned draft with a positive item ID.</param>
        /// <param name="icon">A full 80-by-80 sprite backed by a readable, unpacked 80-by-80 ARGB32 texture.</param>
        /// <exception cref="ArgumentNullException">The item or sprite is null or the sprite has been destroyed.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The item ID is zero or negative.</exception>
        /// <exception cref="ArgumentException">The sprite rectangle or texture differs from the native icon-atlas requirements.</exception>
        /// <exception cref="InvalidOperationException">Registration is closed or the call uses another thread.</exception>
        public static void SetItemIcon(ItemProto prototype, Sprite icon) =>
            PipeItemIcons.Set(prototype, icon);

        /// <summary>Registers a type on the main thread before the prototype database is sealed.</summary>
        /// <param name="ownerGuid">The owning BepInEx plugin GUID.</param>
        /// <param name="definition">An item prototype and immutable connection settings.</param>
        /// <exception cref="ArgumentNullException">The definition is null.</exception>
        /// <exception cref="ArgumentException">The owner GUID is empty.</exception>
        /// <exception cref="InvalidOperationException">Registration is closed, uses another thread, or conflicts with an existing item or type key.</exception>
        public static void Register(string ownerGuid, PipeTypeDefinition definition) =>
            PipeTypeRegistry.Register(ownerGuid, definition);

        /// <summary>Identifies a registered procedural connection item.</summary>
        /// <param name="itemId">A game item ID.</param>
        /// <returns>True for a registered type.</returns>
        public static bool IsPipeItem(int itemId) => PipeTypeRegistry.Get(itemId) != null;

        /// <summary>Queries a registered type by item ID.</summary>
        /// <param name="itemId">A game item ID.</param>
        /// <param name="type">A detached identity snapshot.</param>
        /// <returns>True for a registered type.</returns>
        public static bool TryGet(int itemId, out PipeTypeInfo type)
        {
            var entry = PipeTypeRegistry.Get(itemId);
            type = entry == null ? default(PipeTypeInfo) : new PipeTypeInfo(entry);
            return entry != null;
        }

        /// <summary>Queries a registered type by qualified stable key.</summary>
        /// <param name="typeKey">The plugin GUID, colon and local type name.</param>
        /// <param name="type">A detached identity snapshot.</param>
        /// <returns>True for a registered type.</returns>
        public static bool TryGet(string typeKey, out PipeTypeInfo type)
        {
            var entry = PipeTypeRegistry.Get(typeKey);
            return TryGet(entry?.Definition.Prototype.ID ?? 0, out type);
        }

        /// <summary>Reads completed direct neighbors on the main thread or under exclusive factory access.</summary>
        /// <param name="factory">The owning factory.</param>
        /// <param name="entityId">A completed procedural connection entity.</param>
        /// <returns>A detached array of neighboring entity IDs, or an empty array for an invalid target.</returns>
        public static int[] GetNeighbors(PlanetFactory factory, int entityId)
        {
            if (!PipeApi.IsPipe(factory, entityId))
            {
                return Array.Empty<int>();
            }

            return PipeTypeRegistry.IsUndirected(factory.entityPool[entityId].protoId)
                ? PipeRuntime.Bind(factory.cargoTraffic).Links(entityId)
                : NativeNeighbors(factory, entityId, null);
        }

        /// <summary>Reads outgoing native connections, or all neighbors for an undirected type.</summary>
        /// <param name="factory">The owning factory; use main-thread or exclusive factory access.</param>
        /// <param name="entityId">A completed procedural connection entity.</param>
        /// <returns>A detached array of reachable direct targets.</returns>
        public static int[] GetOutgoingNeighbors(PlanetFactory factory, int entityId) =>
            DirectedNeighbors(factory, entityId, true);

        /// <summary>Reads incoming native connections, or all neighbors for an undirected type.</summary>
        /// <param name="factory">The owning factory; use main-thread or exclusive factory access.</param>
        /// <param name="entityId">A completed procedural connection entity.</param>
        /// <returns>A detached array of direct source entities.</returns>
        public static int[] GetIncomingNeighbors(PlanetFactory factory, int entityId) =>
            DirectedNeighbors(factory, entityId, false);

        private static int[] DirectedNeighbors(PlanetFactory factory, int entityId, bool outgoing)
        {
            if (!PipeApi.IsPipe(factory, entityId))
            {
                return Array.Empty<int>();
            }

            return PipeTypeRegistry.IsUndirected(factory.entityPool[entityId].protoId)
                ? GetNeighbors(factory, entityId)
                : NativeNeighbors(factory, entityId, outgoing);
        }

        private static int[] NativeNeighbors(PlanetFactory factory, int entityId, bool? outgoing)
        {
            var neighbors = new List<int>(4);
            for (int slot = 0; slot < 4; slot++)
            {
                if (
                    PipeConnections.TryReadNativeLink(
                        factory,
                        entityId,
                        slot,
                        out bool output,
                        out int other
                    )
                    && (!outgoing.HasValue || output == outgoing.Value)
                    && other > 0
                    && PipeApi.IsPipe(factory, other)
                    && !neighbors.Contains(other)
                )
                {
                    neighbors.Add(other);
                }
            }
            return neighbors.ToArray();
        }

        /// <summary>Reads the connection component in O(component size) on the main thread or under exclusive factory access. Directed types return their weakly connected component.</summary>
        /// <param name="factory">The owning factory.</param>
        /// <param name="entityId">A completed procedural connection entity.</param>
        /// <returns>A detached array of component entity IDs, or an empty array for an invalid target.</returns>
        public static int[] GetConnectedNodes(PlanetFactory factory, int entityId) =>
            PipeApi.IsPipe(factory, entityId)
                ? PipeRuntime.Bind(factory.cargoTraffic).Members(entityId)
                : new int[0];

        internal static void Changed(PlanetFactory factory, int entityId) =>
            PipeTypeNotifications.Changed(factory, entityId);

        internal static void FlushChanges() => PipeTypeNotifications.FlushChanges();

        internal static void ClearChanges() => PipeTypeNotifications.ClearChanges();
    }
}
