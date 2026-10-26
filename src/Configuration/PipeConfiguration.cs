using System;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace FluidPipes
{
    internal static class PipeConfiguration
    {
        internal static bool EnableDebugHotkey { get; private set; }

        internal static bool EnableMachinePorts { get; private set; }

        internal static void Load(ConfigFile config, ManualLogSource log)
        {
            EnableMachinePorts = config
                .Bind(
                    "Integration",
                    "EnableMachinePorts",
                    false,
                    "Connect pipes directly to production-machine inserter slots. Configure direction and item at each connection. Requires a game restart. Existing connections retain their settings while disabled."
                )
                .Value;
            EnableDebugHotkey = config
                .Bind(
                    "General",
                    "EnableDebugHotkey",
                    false,
                    "F8 grants pipes and valves for local testing. Requires a game restart."
                )
                .Value;
            PipeMesh.MetalFittings = config
                .Bind(
                    "Appearance",
                    "MetalFittings",
                    true,
                    "Render elbows and multi-branch fittings with opaque steel. Requires a game restart."
                )
                .Value;
            bool fluids = config
                .Bind(
                    "Transport",
                    "IncludeFluids",
                    true,
                    "Allow item prototypes marked IsFluid. Requires a game restart."
                )
                .Value;
            string include = config
                .Bind(
                    "Transport",
                    "AllowedItemIds",
                    "",
                    "Additional item IDs separated by commas, spaces or semicolons. Can include solid items. Requires a game restart."
                )
                .Value;
            string exclude = config
                .Bind(
                    "Transport",
                    "DeniedItemIds",
                    "",
                    "Item IDs to reject even when allowed by defaults or another mod. Requires a game restart. Existing inventory is preserved."
                )
                .Value;
            TransportItemRegistry.Configure(
                fluids,
                Parse(include, "AllowedItemIds", log),
                Parse(exclude, "DeniedItemIds", log)
            );
        }

        private static int[] Parse(string value, string key, ManualLogSource log)
        {
            try
            {
                return ItemAdmission.ParseIds(value);
            }
            catch (FormatException error)
            {
                log.LogWarning(
                    "Transport." + key + ": " + error.Message + ". This entry is ignored."
                );
                return new int[0];
            }
        }
    }
}
