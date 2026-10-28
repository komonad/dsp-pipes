using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace FluidPipes.Integration.Reservoirs
{
    [HarmonyPatch]
    internal static class NativeTankInventoryBridge
    {
        internal sealed class Projection
        {
            public FactoryStorage Storage;
            public FactoryStorage OriginalStorage;
            public FluidPoolView Inventory;
            public int PipeEntityId;
            public int TankId;
            public int ProjectedCount;
            public int HandItemId;
            public int HandCount;
            public int HandPoints;
            public bool Updating;
        }

        private static readonly Dictionary<UITankWindow, Projection> projections =
            new Dictionary<UITankWindow, Projection>();

        internal static UITankWindow PipeWindow { get; set; }
        internal static PlanetFactory PipeFactory { get; set; }
        internal static int PipeEntityId { get; set; }

        [HarmonyPrefix, HarmonyPatch(typeof(UITankWindow), "_OnOpen")]
        private static bool Open(UITankWindow __instance)
        {
            if (__instance != PipeWindow)
            {
                return true;
            }

            if (!PipePanel.CanInspectInventory(PipeFactory, PipeEntityId))
            {
                return false;
            }

            __instance.factory = PipeFactory;
            __instance.player = GameMain.mainPlayer;
            __instance.storage = ProjectionFor(__instance, PipeFactory, 1).Storage;
            __instance.tankId = 1;
            __instance.circle2RT.anchoredPosition = new Vector2(
                __instance.circle2RT.rect.width / 4,
                0
            );
            __instance.circle3RT.anchoredPosition = new Vector2(
                -__instance.circle3RT.rect.width / 8,
                0
            );
            __instance.OnTankIdChange();
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UITankWindow), nameof(UITankWindow.OnTankIdChange))]
        private static bool Selection(UITankWindow __instance)
        {
            if (__instance != PipeWindow)
            {
                return true;
            }

            if (__instance.active && PipePanel.CanInspectInventory(PipeFactory, PipeEntityId))
            {
                __instance.titleText.text = LDB
                    .items.Select(PipeFactory.entityPool[PipeEntityId].protoId)
                    .name;
                __instance.menuButton.SetInfo(
                    UIGenericMenuButton.EInfoType.Entity,
                    PipeFactory.index,
                    PipeEntityId,
                    0,
                    0
                );
            }
            return false;
        }

        private static Projection ProjectionFor(
            UITankWindow window,
            PlanetFactory factory,
            int tankId
        )
        {
            if (!projections.TryGetValue(window, out var projection))
            {
                projection = new Projection();
                projections.Add(window, projection);
            }
            if (projection.Storage == null || projection.Storage.factory != factory)
            {
                projection.Storage = new FactoryStorage(factory.planet, true)
                {
                    factory = factory,
                    tankPool = new TankComponent[Math.Max(2, tankId + 1)],
                };
            }
            else if (projection.Storage.tankPool.Length <= tankId)
            {
                Array.Resize(ref projection.Storage.tankPool, tankId + 1);
            }
            return projection;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UITankWindow), "_OnUpdate")]
        private static bool BeginProjection(UITankWindow __instance, out Projection __state)
        {
            __state = null;
            bool pipe = __instance == PipeWindow;
            if (pipe && !PipePanel.CanInspectInventory(PipeFactory, PipeEntityId))
            {
                return false;
            }
            if (__instance.factory == null || __instance.player == null)
            {
                return true;
            }

            int tankId = __instance.tankId;
            FluidPoolView inventory;
            if (pipe)
            {
                inventory = PipePools.View(
                    PipeRuntime.Bind(PipeFactory.cargoTraffic),
                    PipeEntityId,
                    true
                );
            }
            else if (!PipePools.TryTankInventory(__instance.factory, tankId, out inventory))
            {
                return true;
            }

            var projection = ProjectionFor(__instance, __instance.factory, tankId);
            // 原生手势修改的是私有投影；只有提交后的增量才改变库存。
            projection.OriginalStorage = __instance.storage;
            projection.Inventory = inventory;
            projection.PipeEntityId = pipe ? PipeEntityId : 0;
            projection.TankId = tankId;
            projection.ProjectedCount = (int)Math.Min(int.MaxValue - 255L, inventory.Count);
            projection.HandItemId = __instance.player.inhandItemId;
            projection.HandCount = __instance.player.inhandItemCount;
            projection.HandPoints = __instance.player.inhandItemInc;
            projection.Updating = true;

            var tank = pipe
                ? new TankComponent { id = tankId, entityId = PipeEntityId }
                : projection.OriginalStorage.tankPool[tankId];
            tank.fluidId = inventory.Count > 0 ? inventory.Item : 0;
            tank.fluidCount = projection.ProjectedCount;
            // 保持原生填充动画有界，同时让全部保留库存可被提取。
            tank.fluidCapacity = (int)
                Math.Min(int.MaxValue, Math.Max(inventory.Capacity, projection.ProjectedCount));
            tank.fluidInc = (int)
                Math.Min(
                    int.MaxValue,
                    inventory.Count == 0
                        ? 0
                        : inventory.ProliferatorPoints
                            * ((double)projection.ProjectedCount / inventory.Count)
                );
            projection.Storage.tankPool[tankId] = tank;
            __instance.storage = projection.Storage;
            __state = projection;
            return true;
        }

        [HarmonyFinalizer, HarmonyPatch(typeof(UITankWindow), "_OnUpdate")]
        private static Exception Commit(
            UITankWindow __instance,
            Projection __state,
            Exception __exception
        )
        {
            if (__state == null)
            {
                return __exception;
            }

            var projection = __state;
            var projectedTank = projection.Storage.tankPool[projection.TankId];
            __instance.storage = projection.OriginalStorage;
            projection.Updating = false;
            if (__exception != null)
            {
                RestoreHand(__instance.player, projection);
                return __exception;
            }
            if (
                projection.PipeEntityId > 0
                && (
                    __instance != PipeWindow
                    || __instance.factory != PipeFactory
                    || projection.PipeEntityId != PipeEntityId
                    || !PipePanel.CanInspectInventory(PipeFactory, PipeEntityId)
                )
            )
            {
                RestoreHand(__instance.player, projection);
                return __exception;
            }
            int change = projectedTank.fluidCount - projection.ProjectedCount;
            if (change != 0)
            {
                CommitGesture(__instance, projection, projectedTank, change);
                if (projection.PipeEntityId > 0)
                {
                    projection.Inventory = PipePools.View(
                        PipeRuntime.Bind(__instance.factory.cargoTraffic),
                        projection.PipeEntityId
                    );
                }
                else if (
                    PipePools.TryTankInventory(
                        __instance.factory,
                        projection.TankId,
                        out var inventory
                    )
                )
                {
                    projection.Inventory = inventory;
                }
            }

            __instance.countText.text = projection.Inventory.Count.ToString();
            __instance.circle1.fillAmount = (float)projection.Inventory.Fill;
            __instance.productIncArrow.incLevel =
                projection.Inventory.Count == 0
                    ? 0
                    : (int)(projection.Inventory.ProliferatorPoints / projection.Inventory.Count);
            if (
                projection.PipeEntityId > 0
                && projection.Inventory.Item > 0
                && !ItemProto.isFluid(projection.Inventory.Item)
            )
            {
                __instance.exchangeAndColoring(__instance.oriColor);
            }
            return __exception;
        }

        private static void RestoreHand(Player player, Projection projection)
        {
            player.SetHandItemId_Unsafe(projection.HandItemId);
            player.SetHandItemCount_Unsafe(projection.HandCount);
            player.SetHandItemInc_Unsafe(projection.HandPoints);
        }

        private static void CommitGesture(
            UITankWindow window,
            Projection projection,
            TankComponent projectedTank,
            int change
        )
        {
            var player = window.player;
            int remainingHandCount = player.inhandItemCount;
            int remainingHandPoints = player.inhandItemInc;
            int remainingHandItem = player.inhandItemId;
            // 工厂传输可能在手势与其共享库存提交之间改变可用量。
            RestoreHand(player, projection);

            var graph = PipeRuntime.Bind(window.factory.cargoTraffic);
            if (change > 0)
            {
                int points = projection.HandPoints - remainingHandPoints;
                bool accepted =
                    projection.PipeEntityId > 0
                        ? PipePools.Insert(
                            graph,
                            projection.PipeEntityId,
                            projectedTank.fluidId,
                            change,
                            points
                        )
                        : PipePools.TryTankInsert(
                            window.factory,
                            projection.TankId,
                            projectedTank.fluidId,
                            change,
                            points
                        );
                if (accepted)
                {
                    player.SetHandItemId_Unsafe(remainingHandItem);
                    player.SetHandItemCount_Unsafe(remainingHandCount);
                    player.SetHandItemInc_Unsafe(remainingHandPoints);
                }
                return;
            }

            var amount =
                projection.PipeEntityId > 0
                    ? PipePools.Take(
                        graph,
                        projection.PipeEntityId,
                        -change,
                        projection.Inventory.Item
                    )
                    : PipePools.TryTankTake(
                        window.factory,
                        projection.TankId,
                        -change,
                        projection.Inventory.Item
                    );
            if (amount.Count > 0)
            {
                player.SetHandItemId_Unsafe(amount.Item);
                player.SetHandItemCount_Unsafe(projection.HandCount + (int)amount.Count);
                player.SetHandItemInc_Unsafe(
                    projection.HandPoints + (int)amount.ProliferatorPoints
                );
            }
        }

        [HarmonyTranspiler, HarmonyPatch(typeof(UITankWindow), "_OnUpdate")]
        private static IEnumerable<CodeInstruction> ItemAdmission(
            IEnumerable<CodeInstruction> instructions
        )
        {
            MethodInfo nativeAdmission = AccessTools.Method(
                typeof(ItemProto),
                nameof(ItemProto.isFluid)
            );
            MethodInfo projectedAdmission = AccessTools.Method(
                typeof(NativeTankInventoryBridge),
                nameof(IsAllowed)
            );
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(nativeAdmission))
                {
                    var windowArgument = new CodeInstruction(OpCodes.Ldarg_0);
                    windowArgument.labels.AddRange(instruction.labels);
                    windowArgument.blocks.AddRange(instruction.blocks);
                    yield return windowArgument;
                    yield return new CodeInstruction(OpCodes.Call, projectedAdmission);
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        private static bool IsAllowed(int itemId, UITankWindow window)
        {
            if (window == PipeWindow && !PipePanel.CanInspectInventory(PipeFactory, PipeEntityId))
            {
                return false;
            }
            if (!projections.TryGetValue(window, out var projection) || !projection.Updating)
            {
                return ItemProto.isFluid(itemId);
            }

            var inventory = projection.Inventory;
            return !inventory.Conflict
                && TransportItemRegistry.IsAllowed(itemId)
                && (!inventory.RequiresFluid || ItemProto.isFluid(itemId))
                && (inventory.Item == 0 || inventory.Item == itemId);
        }

        internal static void Dispose()
        {
            TankNetworkControls.Dispose();
            projections.Clear();
            PipeWindow = null;
            PipeFactory = null;
            PipeEntityId = 0;
        }
    }
}
