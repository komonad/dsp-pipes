using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;

namespace FluidPipes
{
    [HarmonyPatch(typeof(BuildTool_Path), nameof(BuildTool_Path.DeterminePreviews))]
    internal static class PipePlanning
    {
        private const int BuildingCollisionMask = 393216;
        private const float NativeBeltCollisionHeight = 0.22f;

        [ThreadStatic]
        private static Vector3[] route;

        [ThreadStatic]
        private static float[] distances;

        internal static bool TryCollisionGeometry(
            BuildTool_Path tool,
            int mask,
            out PipeGeometryProfile geometry
        )
        {
            geometry = PipeTypeRegistry.Geometry(tool.handItem?.ID);
            var defaults = PipeGeometryProfile.Default;
            return mask == BuildingCollisionMask
                && PipeTypeRegistry.UsesPipeConstruction(tool.handItem?.ID)
                && (
                    geometry.Radius != defaults.Radius
                    || geometry.CenterHeight != defaults.CenterHeight
                    || geometry.FittingScale != defaults.FittingScale
                    || geometry.SupportRingWidth != defaults.SupportRingWidth
                );
        }

        internal static Vector3 CollisionCenter(
            Vector3 nativeQueryPosition,
            PipeGeometryProfile geometry
        ) =>
            nativeQueryPosition
            + nativeQueryPosition.normalized * (geometry.CenterHeight - NativeBeltCollisionHeight);

        internal static float CollisionRadius(PipeGeometryProfile geometry) =>
            geometry.Radius * (1 + 0.1f * geometry.FittingScale);

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(
            IEnumerable<CodeInstruction> instructions
        )
        {
            var snap = AccessTools.Method(
                typeof(PlanetAuxData),
                nameof(PlanetAuxData.SnapLineNonAlloc)
            );
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(snap))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels);
                    load.blocks.AddRange(instruction.blocks);
                    yield return load;
                    yield return new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(typeof(PipePlanning), nameof(Snap))
                    );
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        public static int Snap(
            PlanetAuxData aux,
            Vector3 begin,
            Vector3 end,
            int path,
            bool geodesic,
            bool beginFlat,
            Vector3[] snaps,
            bool forceVertical,
            ref float maxSlope,
            bool useOldPath,
            BuildTool_Path tool
        )
        {
            if (
                !PipeTypeRegistry.IsUndirected(tool.handItem?.ID)
                || Mathf.Abs(begin.magnitude - end.magnitude) < 0.01f
                || forceVertical
            )
            {
                return aux.SnapLineNonAlloc(
                    begin,
                    end,
                    path,
                    geodesic,
                    beginFlat,
                    snaps,
                    forceVertical,
                    ref maxSlope,
                    useOldPath
                );
            }

            if (PipeRuntime.IsPipeObject(tool.factory, tool.startObjectId))
            {
                begin = Position(tool.factory, tool.startObjectId);
            }

            if (PipeRuntime.IsPipeObject(tool.factory, tool.castObjectId))
            {
                end = Position(tool.factory, tool.castObjectId);
            }

            if (route == null || route.Length < snaps.Length)
            {
                route = new Vector3[snaps.Length];
                distances = new float[snaps.Length];
            }
            float radius = begin.magnitude;
            int count = aux.SnapLineNonAlloc(
                begin,
                end.normalized * radius,
                path,
                geodesic,
                false,
                route,
                false,
                ref maxSlope,
                false
            );
            if (count == 0)
            {
                return 0;
            }

            route[0] = begin;
            route[count - 1] = end.normalized * radius;
            distances[0] = 0;
            for (int index = 1; index < count; index++)
            {
                distances[index] =
                    distances[index - 1] + Vector3.Distance(route[index - 1], route[index]);
            }

            float length = distances[count - 1];
            if (count < 2 || length < 0.001f)
            {
                count = 2;
                route[0] = begin;
                route[1] = end;
            }
            else
            {
                for (int index2 = 0; index2 < count; index2++)
                {
                    route[index2] =
                        route[index2].normalized
                        * Mathf.Lerp(radius, end.magnitude, distances[index2] / length);
                }

                route[0] = begin;
                route[count - 1] = end;
            }
            // 高度插值沿水平路线进行；细分受原生节点间距约束。
            int needed = 1;
            for (int index3 = 1; index3 < count; index3++)
            {
                needed += Mathf.Max(
                    1,
                    Mathf.CeilToInt(Vector3.Distance(route[index3 - 1], route[index3]) / 1.3333333f)
                );
            }

            if (needed > snaps.Length - 10)
            {
                return 0;
            }

            Array.Clear(snaps, 0, snaps.Length);
            int written = 0;
            snaps[written++] = begin;
            for (int index4 = 1; index4 < count; index4++)
            {
                int steps = Mathf.Max(
                    1,
                    Mathf.CeilToInt(Vector3.Distance(route[index4 - 1], route[index4]) / 1.3333333f)
                );
                for (int otherIndex = 1; otherIndex <= steps; otherIndex++)
                {
                    snaps[written++] = Vector3.Lerp(
                        route[index4 - 1],
                        route[index4],
                        (float)otherIndex / steps
                    );
                }
            }
            maxSlope =
                length > 0.001f ? Mathf.Abs(radius - end.magnitude) / length : float.MaxValue;
            return written;
        }

        public static Vector3 Position(PlanetFactory factory, int entityId) =>
            entityId > 0 ? factory.entityPool[entityId].pos : factory.prebuildPool[-entityId].pos;
    }
}
