using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FluidPipes.Integration.Reservoirs
{
    [HarmonyPatch]
    internal static class TankNetworkControls
    {
        private static GameObject controls;
        private static Text localClearLabel;
        private static Text networkClearLabel;
        private static Text inventoryLabel;
        private static Vector2 originalSize;
        private static RectTransform inventoryRoot;
        private static Vector2 inventoryAnchorMin;
        private static Vector2 inventoryAnchorMax;
        private static Vector2 inventoryPivot;
        private static Vector2 inventoryPosition;
        private static Vector2 inventorySize;
        private static PlanetFactory selectedFactory;
        private static int selectedTank;
        private static float localClearUntil;
        private static float networkClearUntil;

        internal static Vector2 TemplateSize(UITankWindow window)
        {
            return controls == null ? ((RectTransform)window.transform).sizeDelta : originalSize;
        }

        private static void Create(UITankWindow window)
        {
            var transform = (RectTransform)window.transform;
            originalSize = transform.sizeDelta;
            inventoryRoot = (RectTransform)window.transform.Find("produce");
            inventoryAnchorMin = inventoryRoot.anchorMin;
            inventoryAnchorMax = inventoryRoot.anchorMax;
            inventoryPivot = inventoryRoot.pivot;
            inventoryPosition = inventoryRoot.anchoredPosition;
            inventorySize = inventoryRoot.sizeDelta;
            controls = new GameObject("FluidPipes.NetworkControls", typeof(RectTransform));
            controls.layer = window.gameObject.layer;
            var controlsTransform = (RectTransform)controls.transform;
            controlsTransform.SetParent(window.transform, false);
            // Root offsets include the native frame artwork's transparent outer gutter.
            NativePipeUi.Layout(controlsTransform, originalSize.y + 8, 112, 32);
            inventoryLabel = NativePipeUi.Label(controls.transform, 0, 42, "", 14);
            NativePipeUi.Layout((RectTransform)inventoryLabel.transform, 0, 42, 0);
            localClearLabel = NativePipeUi.Button(controls.transform, 50, "清空本罐", ClearLocal);
            networkClearLabel = NativePipeUi.Button(
                controls.transform,
                50,
                "清空整网",
                ClearNetwork
            );
            var localButtonRect = (RectTransform)localClearLabel.transform.parent;
            localButtonRect.anchorMax = new Vector2(0.5f, 1);
            localButtonRect.offsetMin = new Vector2(0, -80);
            localButtonRect.offsetMax = new Vector2(-4, -50);
            var networkButtonRect = (RectTransform)networkClearLabel.transform.parent;
            networkButtonRect.anchorMin = new Vector2(0.5f, 1);
            networkButtonRect.offsetMin = new Vector2(4, -80);
            networkButtonRect.offsetMax = new Vector2(0, -50);
            NativePipeUi.Layout((RectTransform)localClearLabel.transform, 0, 30, 10);
            NativePipeUi.Layout((RectTransform)networkClearLabel.transform, 0, 30, 10);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UITankWindow), "_OnUpdate")]
        private static void Refresh(UITankWindow __instance)
        {
            if (__instance != UIRoot.instance?.uiGame?.tankWindow)
            {
                return;
            }
            bool connected = PipePools.TryTankInventory(
                __instance.factory,
                __instance.tankId,
                out var inventory
            );
            if (controls == null && !connected)
            {
                return;
            }
            if (controls == null)
            {
                Create(__instance);
            }
            controls.SetActive(connected);
            var transform = (RectTransform)__instance.transform;
            transform.sizeDelta = connected
                ? new Vector2(originalSize.x, originalSize.y + 120)
                : originalSize;
            if (connected)
            {
                NativePipeUi.Layout(inventoryRoot, 40, originalSize.y - 40, 0);
            }
            else
            {
                RestoreInventoryLayout();
            }
            if (selectedFactory != __instance.factory || selectedTank != __instance.tankId)
            {
                selectedFactory = __instance.factory;
                selectedTank = __instance.tankId;
                localClearUntil = 0;
                networkClearUntil = 0;
            }
            if (connected)
            {
                var localTank = __instance.factory.factoryStorage.tankPool[__instance.tankId];
                inventoryLabel.text =
                    "当前储液罐："
                    + localTank.fluidCount
                    + " / "
                    + localTank.fluidCapacity
                    + "\n整个液体网："
                    + inventory.Count
                    + " / "
                    + inventory.Capacity;
            }
            localClearLabel.text =
                Time.unscaledTime < localClearUntil ? "确认清空本罐" : "清空本罐";
            networkClearLabel.text =
                Time.unscaledTime < networkClearUntil ? "确认清空整网" : "清空整网";
        }

        private static void ClearLocal()
        {
            var window = UIRoot.instance.uiGame.tankWindow;
            if (Time.unscaledTime < localClearUntil)
            {
                PipePools.EmptyTank(window.factory, window.tankId);
                localClearUntil = 0;
            }
            else
            {
                localClearUntil = Time.unscaledTime + 3;
                networkClearUntil = 0;
            }
            Refresh(window);
        }

        private static void ClearNetwork()
        {
            var window = UIRoot.instance.uiGame.tankWindow;
            if (Time.unscaledTime < networkClearUntil)
            {
                PipePools.EmptyTankNetwork(window.factory, window.tankId);
                networkClearUntil = 0;
            }
            else
            {
                networkClearUntil = Time.unscaledTime + 3;
                localClearUntil = 0;
            }
            Refresh(window);
        }

        internal static void Dispose()
        {
            if (controls != null)
            {
                var window = UIRoot.instance?.uiGame?.tankWindow;
                if (window != null)
                {
                    ((RectTransform)window.transform).sizeDelta = originalSize;
                    RestoreInventoryLayout();
                }
                Object.Destroy(controls);
            }
            controls = null;
            selectedFactory = null;
            selectedTank = 0;
            localClearUntil = 0;
            networkClearUntil = 0;
        }

        private static void RestoreInventoryLayout()
        {
            if (inventoryRoot == null)
            {
                return;
            }
            inventoryRoot.anchorMin = inventoryAnchorMin;
            inventoryRoot.anchorMax = inventoryAnchorMax;
            inventoryRoot.pivot = inventoryPivot;
            inventoryRoot.anchoredPosition = inventoryPosition;
            inventoryRoot.sizeDelta = inventorySize;
        }
    }
}
