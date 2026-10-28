using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FluidPipes.API;
using UnityEngine;

namespace FluidPipes
{
    internal static class PipeMaterialLibrary
    {
        private static readonly Dictionary<int, PipeMaterialSet> sets =
            new Dictionary<int, PipeMaterialSet>();
        private static readonly HashSet<int> failedEffects = new HashSet<int>();
        private static readonly HashSet<int> failedGroups = new HashSet<int>();
        private static ConditionalWeakTable<
            PlanetFactory,
            Dictionary<int, Func<Vector3, float>>
        > factoryCoordinates =
            new ConditionalWeakTable<PlanetFactory, Dictionary<int, Func<Vector3, float>>>();
        private static PipeMaterialSet defaults;

        internal static PipeMaterialSet Get(int itemId)
        {
            var appearance = PipeTypeRegistry.Get(itemId)?.Definition.Appearance;
            if (appearance == null)
            {
                return defaults ?? (defaults = new PipeMaterialSet(null));
            }

            if (!sets.TryGetValue(itemId, out var materials))
            {
                try
                {
                    materials = new PipeMaterialSet(appearance);
                }
                catch (Exception error)
                {
                    Debug.LogError(
                        "FluidPipes appearance initialization failed for " + itemId + ": " + error
                    );
                    materials = defaults ?? (defaults = new PipeMaterialSet(null));
                }
                sets.Add(itemId, materials);
            }
            return materials;
        }

        internal static int BatchGroup(PlanetFactory factory, int entityId)
        {
            int itemId = factory.entityPool[entityId].protoId;
            var callback = PipeTypeRegistry.Get(itemId)?.Definition.BatchGroup;
            if (callback == null || failedGroups.Contains(itemId))
            {
                return 0;
            }
            try
            {
                return callback(factory, entityId);
            }
            catch (Exception error)
            {
                failedGroups.Add(itemId);
                Debug.LogError("FluidPipes batch grouping failed for " + itemId + ": " + error);
                return 0;
            }
        }

        internal static Func<Vector3, float> TextureCoordinate(int itemId, PlanetFactory factory)
        {
            var callback = PipeTypeRegistry.Get(itemId)?.Definition.Appearance?.TextureCoordinate;
            if (callback == null || factory == null)
            {
                return null;
            }
            var cache = factoryCoordinates.GetValue(
                factory,
                _ => new Dictionary<int, Func<Vector3, float>>()
            );
            if (!cache.TryGetValue(itemId, out var coordinate))
            {
                coordinate = SafeCoordinate(itemId, position => callback(factory, position));
                cache.Add(itemId, coordinate);
            }
            return coordinate;
        }

        private static Func<Vector3, float> SafeCoordinate(
            int itemId,
            Func<Vector3, float> callback
        )
        {
            bool failed = false;
            return position =>
            {
                if (failed)
                {
                    return float.NaN;
                }
                try
                {
                    float value = callback(position);
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        throw new InvalidOperationException("Texture coordinates must be finite.");
                    }
                    return value;
                }
                catch (Exception error)
                {
                    failed = true;
                    Debug.LogError(
                        "FluidPipes texture coordinates failed for " + itemId + ": " + error
                    );
                    return float.NaN;
                }
            };
        }

        internal static bool Effects(
            int itemId,
            PipeEffectContext context,
            MaterialPropertyBlock properties
        )
        {
            var callback = PipeTypeRegistry.Get(itemId)?.Definition.Appearance?.ApplyEffects;
            if (callback == null || failedEffects.Contains(itemId))
            {
                return false;
            }

            try
            {
                callback(context, properties);
                return true;
            }
            catch (Exception error)
            {
                failedEffects.Add(itemId);
                Debug.LogError("FluidPipes effect callback failed for " + itemId + ": " + error);
                return false;
            }
        }

        internal static void Dispose()
        {
            foreach (var materials in new HashSet<PipeMaterialSet>(sets.Values))
            {
                if (materials != defaults)
                {
                    materials.Dispose();
                }
            }
            defaults?.Dispose();
            defaults = null;
            sets.Clear();
            failedEffects.Clear();
            failedGroups.Clear();
            factoryCoordinates =
                new ConditionalWeakTable<PlanetFactory, Dictionary<int, Func<Vector3, float>>>();
        }
    }
}
