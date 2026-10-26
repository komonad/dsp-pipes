using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;

namespace FluidPipes.Integration.Reservoirs
{
    internal static class OrbitalRingCompatibility
    {
        internal const string PluginGuid = "org.ProfessorCat305.OrbitalRing";
        private static int infiniteTankItemId;
        private static readonly ConditionalWeakTable<
            CargoPath,
            Dictionary<int, OutputCadence>
        > outputCadences = new ConditionalWeakTable<CargoPath, Dictionary<int, OutputCadence>>();

        private sealed class OutputCadence
        {
            public bool SkipNext;
        }

        internal static void Initialize(Harmony harmony, ManualLogSource logger)
        {
            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin))
            {
                return;
            }

            try
            {
                Assembly assembly = plugin.Instance.GetType().Assembly;
                Type prototypeIds = assembly.GetType("ProjectOrbitalRing.Utils.ProtoID", true);
                int itemId = (int)
                    AccessTools.Field(prototypeIds, "I无尽水罐").GetRawConstantValue();
                Type tankLogic = assembly.GetType(
                    "ProjectOrbitalRing.Patches.Logic.InfiniteTank",
                    true
                );
                MethodInfo tick = AccessTools.Method(tankLogic, "TankComponent_GameTick_PrePatch");
                harmony.Patch(
                    tick,
                    transpiler: new HarmonyMethod(
                        typeof(OrbitalRingCompatibility),
                        nameof(AdaptOutput)
                    )
                );
                infiniteTankItemId = itemId;
                logger.LogInfo(
                    "OrbitalRing infinite-tank pipe integration enabled (1800 items/min)."
                );
            }
            catch (Exception error)
            {
                logger.LogWarning("OrbitalRing infinite-tank integration unavailable: " + error);
            }
        }

        internal static bool UsesIndependentInventory(PlanetFactory factory, int entityId)
        {
            return infiniteTankItemId > 0
                && factory.entityPool[entityId].protoId == infiniteTankItemId;
        }

        private static IEnumerable<CodeInstruction> AdaptOutput(
            IEnumerable<CodeInstruction> instructions
        )
        {
            MethodInfo nativeOutput = AccessTools.Method(
                typeof(CargoTraffic),
                nameof(CargoTraffic.TryInsertItemAtHead),
                new[] { typeof(int), typeof(int), typeof(byte), typeof(byte) }
            );
            MethodInfo pipeOutput = AccessTools.Method(
                typeof(OrbitalRingCompatibility),
                nameof(InsertOutput)
            );
            int replacements = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(nativeOutput))
                {
                    var loadTank = new CodeInstruction(OpCodes.Ldarg_0);
                    loadTank.MoveLabelsFrom(instruction);
                    loadTank.MoveBlocksFrom(instruction);
                    yield return loadTank;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = pipeOutput;
                    replacements++;
                }
                yield return instruction;
            }
            if (replacements != 1)
            {
                throw new InvalidOperationException("Expected one infinite-tank output operation.");
            }
        }

        private static bool InsertOutput(
            CargoTraffic traffic,
            int beltId,
            int itemId,
            byte stack,
            byte inc,
            ref TankComponent tank
        )
        {
            if (PipeRuntime.IsPipe(traffic, beltId))
            {
                CargoPath path = traffic.pathPool[traffic.beltPool[beltId].segPathId];
                Dictionary<int, OutputCadence> sources = outputCadences.GetOrCreateValue(path);
                if (!sources.TryGetValue(tank.id, out var cadence))
                {
                    cadence = new OutputCadence();
                    sources.Add(tank.id, cadence);
                }
                bool skip = cadence.SkipNext;
                // 原生储液罐每 tick 尝试一次插入；蓝色传送带每秒运送 30 个物品。
                cadence.SkipNext = !skip;
                if (skip)
                {
                    return false;
                }
            }
            return traffic.TryInsertItemAtHead(beltId, itemId, stack, inc);
        }
    }
}
