using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace FluidPipes.Integration
{
    internal static class BuildBarIntegration
    {
        internal const string PluginGuid = "Gnimaerd.DSP.plugin.BuildBarTool";

        internal static bool TryPlaceUpperTransportRow(ItemProto[] items)
        {
            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin))
            {
                return false;
            }
            Type pluginType = plugin.Instance.GetType();
            var upperRow = AccessTools.Field(pluginType, "protos")?.GetValue(null) as ItemProto[,];
            var reservations = AccessTools.Field(pluginType, "protoIds")?.GetValue(null) as int[,];
            Type apiType = pluginType.Assembly.GetType("BuildBarTool.BuildBarTool");
            var bind =
                apiType == null
                    ? null
                    : AccessTools.Method(
                        apiType,
                        "SetBuildBar",
                        new[] { typeof(int), typeof(int), typeof(int), typeof(bool) }
                    );
            var refresh = AccessTools.Method(pluginType, "LoadFromBuildIndex");
            int category = LDB.items.Select(2001).BuildIndex / 100;
            if (
                upperRow == null
                || reservations == null
                || bind == null
                || refresh == null
                || category <= 0
                || category >= upperRow.GetLength(0)
                || category >= reservations.GetLength(0)
            )
            {
                return false;
            }
            var ownItems = new HashSet<int>(items.Select(item => item.ID));
            var occupied = new HashSet<int>(
                LDB.items.dataArray.Where(item => !ownItems.Contains(item.ID))
                    .Select(item => item.BuildIndex)
            );
            int columns = Math.Min(
                10,
                Math.Min(upperRow.GetLength(1), reservations.GetLength(1)) - 1
            );
            int[] freeColumns = Enumerable
                .Range(1, columns)
                .Where(column =>
                    upperRow[category, column] == null
                    && reservations[category, column] == 0
                    && !occupied.Contains(category * 100 + 20 + column)
                )
                .Take(items.Length)
                .ToArray();
            if (freeColumns.Length != items.Length)
            {
                return false;
            }
            for (int itemIndex = 0; itemIndex < items.Length; itemIndex++)
            {
                if (
                    !(bool)
                        bind.Invoke(
                            null,
                            new object[]
                            {
                                category,
                                freeColumns[itemIndex],
                                items[itemIndex].ID,
                                true,
                            }
                        )
                )
                {
                    for (int placedIndex = 0; placedIndex < itemIndex; placedIndex++)
                    {
                        reservations[category, freeColumns[placedIndex]] = 0;
                    }
                    return false;
                }
            }
            for (int itemIndex = 0; itemIndex < items.Length; itemIndex++)
            {
                // BuildBarTool encodes upper-row columns as 21 through 30 in item prototypes.
                items[itemIndex].BuildIndex = category * 100 + 20 + freeColumns[itemIndex];
            }
            refresh.Invoke(null, null);
            return true;
        }
    }
}
