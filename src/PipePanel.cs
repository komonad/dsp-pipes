using System;
using System.Collections.Generic;
using System.Globalization;
using CommonAPI.Systems;
using FluidPipes.Integration.Reservoirs;
using UnityEngine;
using UnityEngine.UI;

namespace FluidPipes
{
    internal static class PipePanel
    {
        private static GameObject panel;
        private static UITankWindow inventoryWindow;
        private static Text title;
        private static Text inventory;
        private static Text status;
        private static Text direction;
        private static Text level;
        private static Text enabledLabel;
        private static Text emptyLabel;
        private static Text connections;
        private static InputField rate;
        private static InputField target;
        private static GameObject pipeControls;
        private static GameObject valveControls;
        private static GameObject machinePortButton;
        private static PlanetFactory factory;
        private static int entity;
        private static bool valve;
        private static bool editing;
        private static float refresh;
        private static float emptyUntil;
        private static int[] peers = new int[0];
        private static readonly List<GameObject> peerButtons = new List<GameObject>();
        private static readonly Text[] pipeValues = new Text[4];
        private static GameObject summary;
        private static GameObject regularValveControls;
        private static GameObject infiniteValveControls;
        private static Text valveModeLabel;
        private static Text infiniteSideLabel;
        private static Text infiniteItemLabel;
        private static Text infiniteModeLabel;
        private static InputField infiniteTarget;

        private sealed class NativeVisibility
        {
            public CanvasGroup Group;
            public float Alpha;
            public bool Interactable;
            public bool Raycasts;
        }

        private static readonly List<NativeVisibility> hiddenWindows = new List<NativeVisibility>();

        internal static UITankWindow InventoryWindow => inventoryWindow;

        internal static bool CanInspectInventory(PlanetFactory owner, int entityId) =>
            API.PipeApi.IsPipe(owner, entityId) && PipeRuntime.HasInventory(owner, entityId);

        private static RectTransform Rect(
            GameObject control,
            Transform parent,
            float y,
            float height
        )
        {
            control.layer = parent.gameObject.layer;
            var rect = control.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = new Vector2(32, -y - height);
            rect.offsetMax = new Vector2(-32, -y);
            return rect;
        }

        internal static Text Label(
            Transform parent,
            float y,
            float height,
            string value,
            int size = 14
        )
        {
            return NativePipeUi.Label(parent, y, height, value, size);
        }

        internal static Text Button(Transform parent, float y, string value, Action action)
        {
            bool nested =
                parent == pipeControls?.transform
                || parent == valveControls?.transform
                || parent == regularValveControls?.transform
                || parent == infiniteValveControls?.transform;
            return NativePipeUi.Button(parent, y, value, action, nested ? 0 : 32);
        }

        private static InputField Input(
            Transform parent,
            float y,
            string label,
            Action<string> change
        )
        {
            return NativePipeUi.Input(
                parent,
                y,
                label,
                value =>
                {
                    if (!editing)
                    {
                        change(value);
                    }
                }
            );
        }

        private static void Create()
        {
            inventoryWindow = NativePipeUi.Window(
                "FluidPipes.Panel",
                () =>
                {
                    GameMain.mainPlayer?.controller?.actionInspect?.InspectNothing();
                    Hide();
                }
            );
            NativeTankInventoryBridge.PipeWindow = inventoryWindow;
            inventoryWindow._Create();
            inventoryWindow._Init(GameMain.data);
            panel = inventoryWindow.gameObject;
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(20, -140);
            rect.sizeDelta = new Vector2(306, 580);
            title = inventoryWindow.titleText;
            inventory = Label(panel.transform, 58, 88, "");
            summary = new GameObject("pipe-summary", typeof(RectTransform));
            Rect(summary, panel.transform, 278, 88);
            string[] captions = { "当前管段", "管道存量", "连通容量", "管段 / 容器" };
            for (int row = 0; row < pipeValues.Length; row++)
            {
                pipeValues[row] = NativePipeUi.Property(summary.transform, row * 22, captions[row]);
            }
            status = Label(panel.transform, 372, 28, "", 12);
            pipeControls = new GameObject("pipe-controls", typeof(RectTransform));
            Rect(pipeControls, panel.transform, 414, 220);
            emptyLabel = Button(pipeControls.transform, 0, "清空整个液体网", Empty);
            connections = Label(pipeControls.transform, 40, 22, "");
            NativePipeUi.Layout((RectTransform)connections.transform, 40, 22, 0);
            machinePortButton = Button(
                pipeControls.transform,
                258,
                "机器直连接口设置",
                () => PipeMachinePortPanel.Open(factory, entity)
            ).transform.parent.gameObject;
            valveControls = new GameObject("valve-controls", typeof(RectTransform));
            Rect(valveControls, panel.transform, 198, 330);
            enabledLabel = Button(
                valveControls.transform,
                0,
                "",
                () =>
                {
                    var valveSettings = Settings();
                    valveSettings.Enabled = !valveSettings.Enabled;
                    UpdateContents();
                }
            );
            valveModeLabel = Button(
                valveControls.transform,
                36,
                "",
                () =>
                {
                    if (SandboxValve.Available)
                    {
                        Settings().InfiniteEnabled = !Settings().InfiniteEnabled;
                        UpdateContents();
                    }
                }
            );
            regularValveControls = new GameObject("pump-settings", typeof(RectTransform));
            regularValveControls.layer = valveControls.layer;
            regularValveControls.transform.SetParent(valveControls.transform, false);
            NativePipeUi.Layout((RectTransform)regularValveControls.transform, 78, 210, 0);
            direction = Button(
                regularValveControls.transform,
                0,
                "",
                () =>
                {
                    var valveSettings = Settings();
                    valveSettings.Direction = (ValveDirection)(
                        ((int)valveSettings.Direction + 1) % 3
                    );
                    UpdateContents();
                }
            );
            level = Button(
                regularValveControls.transform,
                36,
                "",
                () =>
                {
                    var valveSettings = Settings();
                    valveSettings.Level = (ValveLevel)(((int)valveSettings.Level + 1) % 3);
                    UpdateContents();
                }
            );
            rate = Input(
                regularValveControls.transform,
                76,
                "最大流量（份/秒）",
                text =>
                {
                    if (Number(text, out double parsedValue))
                    {
                        Settings().Rate = Math.Max(0, Math.Min(1000000, parsedValue));
                    }
                    UpdateContents();
                }
            );
            target = Input(
                regularValveControls.transform,
                144,
                "目标液位（0–100%）",
                text =>
                {
                    if (Number(text, out double parsedValue))
                    {
                        Settings().Target = Math.Max(0, Math.Min(1, parsedValue / 100));
                    }
                    UpdateContents();
                }
            );
            infiniteValveControls = new GameObject("infinite-settings", typeof(RectTransform));
            infiniteValveControls.layer = valveControls.layer;
            infiniteValveControls.transform.SetParent(valveControls.transform, false);
            NativePipeUi.Layout((RectTransform)infiniteValveControls.transform, 78, 180, 0);
            infiniteSideLabel = Button(
                infiniteValveControls.transform,
                0,
                "",
                () =>
                {
                    Settings().InfiniteSide = 1 - Settings().InfiniteSide;
                    UpdateContents();
                }
            );
            infiniteItemLabel = Button(infiniteValveControls.transform, 36, "", PickInfiniteFluid);
            infiniteModeLabel = Button(
                infiniteValveControls.transform,
                72,
                "",
                () =>
                {
                    Settings().InfiniteMode = (InfiniteLevelMode)(
                        ((int)Settings().InfiniteMode + 1) % 3
                    );
                    UpdateContents();
                }
            );
            infiniteTarget = Input(
                infiniteValveControls.transform,
                110,
                "目标液位（0–100%）",
                text =>
                {
                    if (Number(text, out double value))
                    {
                        Settings().InfiniteTarget = Math.Max(0, Math.Min(1, value / 100));
                    }
                    UpdateContents();
                }
            );
        }

        private static void PickInfiniteFluid()
        {
            var owner = factory;
            int valveEntity = entity;
            UIItemPickerExtension.Popup(
                new Vector2(-220, 180),
                item =>
                {
                    if (
                        owner == factory
                        && valveEntity == entity
                        && item != null
                        && item.IsFluid
                        && TransportItemRegistry.IsAllowed(item.ID)
                    )
                    {
                        Settings().InfiniteItemId = item.ID;
                        UpdateContents();
                    }
                },
                true,
                item => item.IsFluid && TransportItemRegistry.IsAllowed(item.ID)
            );
        }

        private static bool Number(string text, out double number) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && !double.IsNaN(number)
            && !double.IsInfinity(number);

        private static ValveSettings Settings() => PipeValves.Get(factory, entity);

        public static void Update()
        {
            var game = UIRoot.instance?.uiGame;
            var current = GameMain.localPlanet?.factory;
            if (game == null || current == null)
            {
                Hide();
                return;
            }
            int selected = 0;
            bool isValve = false;
            if (
                game.beltWindow.active
                && PipeRuntime.TryBelt(
                    current.cargoTraffic,
                    game.beltWindow.beltId,
                    out _,
                    out int pipe
                )
                && CanInspectInventory(current, pipe)
            )
            {
                selected = pipe;
            }

            if (game.splitterWindow.active)
            {
                int splitterId = game.splitterWindow.splitterId;
                if (splitterId > 0 && splitterId < current.cargoTraffic.splitterPool.Length)
                {
                    int targetEntityId = current.cargoTraffic.splitterPool[splitterId].entityId;
                    if (PipeValves.IsValve(current, targetEntityId))
                    {
                        selected = targetEntityId;
                        isValve = true;
                    }
                }
            }
            if (selected == 0)
            {
                Hide();
                return;
            }
            MaskWindow(isValve ? game.splitterWindow.gameObject : game.beltWindow.gameObject);
            if (panel == null)
            {
                Create();
            }

            if (entity != selected || factory != current || valve != isValve)
            {
                inventoryWindow._Close();
                entity = selected;
                factory = current;
                valve = isValve;
                emptyUntil = 0;
                refresh = 0;
                ConfigureInventory();
            }
            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
            if (!valve && PipeRuntime.HasInventory(factory, entity))
            {
                inventoryWindow._Update();
            }
            pipeControls.SetActive(!valve);
            valveControls.SetActive(valve);
            if (Time.unscaledTime >= refresh)
            {
                UpdateContents();
                refresh = Time.unscaledTime + 0.2f;
            }
            if (!valve)
            {
                PipeSelectionHighlights.Show(factory, entity, peers);
            }
            else
            {
                PipeSelectionHighlights.Hide();
            }
        }

        private static void ConfigureInventory()
        {
            if (inventoryWindow.data != GameMain.data)
            {
                inventoryWindow._Free();
                inventoryWindow._Init(GameMain.data);
            }
            bool inventoryTarget = !valve && CanInspectInventory(factory, entity);
            NativeTankInventoryBridge.PipeFactory = inventoryTarget ? factory : null;
            NativeTankInventoryBridge.PipeEntityId = inventoryTarget ? entity : 0;
            if (valve || !PipeRuntime.HasInventory(factory, entity))
            {
                NativePipeUi.HideInventory(inventoryWindow);
                summary.SetActive(false);
                inventory.gameObject.SetActive(true);
                NativePipeUi.Layout((RectTransform)inventory.transform, 58, 84);
                NativePipeUi.Layout((RectTransform)status.transform, 144, 44);
                return;
            }
            inventory.gameObject.SetActive(false);
            summary.SetActive(true);

            foreach (
                var component in new Component[]
                {
                    inventoryWindow.countLabel,
                    inventoryWindow.countText,
                    inventoryWindow.circle0,
                    inventoryWindow.circle1,
                    inventoryWindow.circle2,
                    inventoryWindow.circle3,
                    inventoryWindow.productIcon,
                    inventoryWindow.productIncArrow,
                    inventoryWindow.productTipButton,
                    inventoryWindow.actionImage0,
                    inventoryWindow.actionText,
                    inventoryWindow.menuButton,
                }
            )
            {
                component.gameObject.SetActive(true);
            }
            inventoryWindow.transform.Find("produce").gameObject.SetActive(true);
            inventoryWindow.inputButton.gameObject.SetActive(false);
            inventoryWindow.outputButton.gameObject.SetActive(false);
            inventoryWindow._Open();
            UIRoot.instance.uiGame.OpenPlayerInventory();
            NativePipeUi.Layout((RectTransform)status.transform, 374, 28);
        }

        private static void MaskWindow(GameObject window)
        {
            var group = window.GetComponent<CanvasGroup>() ?? window.AddComponent<CanvasGroup>();
            foreach (var hidden in hiddenWindows)
            {
                if (hidden.Group == group)
                {
                    return;
                }
            }

            hiddenWindows.Add(
                new NativeVisibility
                {
                    Group = group,
                    Alpha = group.alpha,
                    Interactable = group.interactable,
                    Raycasts = group.blocksRaycasts,
                }
            );
            group.alpha = 0;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        private static void Hide()
        {
            PipeSelectionHighlights.Hide();
            NativePipeUi.HideWorldLabels("FluidPipes.Pipe");
            PipeMachinePortPanel.Hide();
            inventoryWindow?._Close();
            if (panel != null)
            {
                panel.SetActive(false);
            }

            entity = 0;
            factory = null;
            NativeTankInventoryBridge.PipeFactory = null;
            NativeTankInventoryBridge.PipeEntityId = 0;
            foreach (var hidden in hiddenWindows)
            {
                if (hidden.Group != null)
                {
                    hidden.Group.alpha = hidden.Alpha;
                    hidden.Group.interactable = hidden.Interactable;
                    hidden.Group.blocksRaycasts = hidden.Raycasts;
                }
            }

            hiddenWindows.Clear();
        }

        private static string Fluid(int item) =>
            item == 0 ? "空" : LDB.items.Select(item)?.name ?? item.ToString();

        private static string Describe(FluidPoolView inventoryView) =>
            Fluid(inventoryView.Item)
            + "  "
            + inventoryView.Count
            + " / "
            + inventoryView.Capacity
            + "  ("
            + (inventoryView.Fill * 100).ToString("F2")
            + "%)";

        private static void UpdateContents()
        {
            if (factory == null || entity == 0)
            {
                return;
            }
            machinePortButton.SetActive(
                !valve
                    && PipeRuntime.HasInventory(factory, entity)
                    && PipeMachinePorts.Connections(factory, entity).Length > 0
            );

            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            if (valve)
            {
                panel.GetComponent<RectTransform>().sizeDelta = new Vector2(306, 532);
                title.text = "流体阀门";
                PipeValves.Ends(factory, entity, out int firstEntityId, out int secondEntityId);
                inventory.text =
                    "A 侧："
                    + (
                        firstEntityId == 0
                            ? "未连接"
                            : Describe(PipePools.View(graph, firstEntityId, true))
                    )
                    + "\nB 侧："
                    + (
                        secondEntityId == 0
                            ? "未连接"
                            : Describe(PipePools.View(graph, secondEntityId, true))
                    );
                status.text =
                    firstEntityId != 0
                    && secondEntityId != 0
                    && PipePools.SameSpace(graph, firstEntityId, secondEntityId)
                        ? "两侧已通过其他路径连通，阀门不转移液体。"
                        : "两侧液体空间独立。";
                status.text =
                    "泵送功率：100 kW  供电："
                    + (ValvePower.Supply(factory, entity) * 100).ToString("F0")
                    + "%\n"
                    + status.text;
                var valveSettings = Settings();
                bool sandbox = SandboxValve.Available;
                valveModeLabel.transform.parent.gameObject.SetActive(sandbox);
                valveModeLabel.text = valveSettings.InfiniteEnabled
                    ? "沙盒模式：永续管"
                    : "工作模式：普通阀门";
                bool infinite = sandbox && valveSettings.InfiniteEnabled;
                regularValveControls.SetActive(!infinite);
                infiniteValveControls.SetActive(infinite);
                NativePipeUi.Layout(
                    (RectTransform)regularValveControls.transform,
                    sandbox ? 78 : 42,
                    210,
                    0
                );
                if (infinite)
                {
                    status.text = "沙盒永续管 · 直接调节所选空间液位";
                    infiniteSideLabel.text =
                        "控制接口：" + (valveSettings.InfiniteSide == 0 ? "A 侧" : "B 侧");
                    infiniteItemLabel.text = "液体：" + Fluid(valveSettings.InfiniteItemId);
                    infiniteModeLabel.text =
                        "液位模式："
                        + (
                            valveSettings.InfiniteMode == InfiniteLevelMode.AtLeast ? "至少 ≥"
                            : valveSettings.InfiniteMode == InfiniteLevelMode.AtMost ? "至多 ≤"
                            : "恰好 ="
                        );
                    if (!infiniteTarget.isFocused)
                    {
                        infiniteTarget.SetTextWithoutNotify(
                            (valveSettings.InfiniteTarget * 100).ToString(
                                "0.###",
                                CultureInfo.InvariantCulture
                            )
                        );
                    }
                }
                enabledLabel.text = valveSettings.Enabled ? "阀门：开启" : "阀门：关闭";
                direction.text =
                    "方向："
                    + (
                        valveSettings.Direction == ValveDirection.Both ? "双向"
                        : valveSettings.Direction == ValveDirection.AtoB ? "A → B"
                        : "B → A"
                    );
                level.text =
                    "控制："
                    + (
                        valveSettings.Level == ValveLevel.Equalize ? "按液位差均衡"
                        : valveSettings.Level == ValveLevel.KeepA ? "维持 A 侧液位"
                        : "维持 B 侧液位"
                    );
                editing = true;
                if (!rate.isFocused)
                {
                    rate.text = valveSettings.Rate.ToString("0.###", CultureInfo.InvariantCulture);
                }

                if (!target.isFocused)
                {
                    target.text = (valveSettings.Target * 100).ToString(
                        "0.###",
                        CultureInfo.InvariantCulture
                    );
                }

                editing = false;
                return;
            }
            title.text = LDB.items.Select(factory.entityPool[entity].protoId).name;
            var view = PipePools.View(graph, entity, true);
            bool storesItems = PipeRuntime.HasInventory(factory, entity);
            if (!storesItems)
            {
                inventory.text =
                    "连通节点：" + view.Nodes + "\n当前连接：" + graph.Links(entity).Length;
                status.text = PipeTypeRegistry.IsUndirected(factory.entityPool[entity].protoId)
                    ? "无向连接"
                    : "有向连接";
            }
            pipeValues[0].text =
                (view.Fill * PipeGraph.SegmentCapacity).ToString("0.##")
                + " / "
                + PipeGraph.SegmentCapacity;
            pipeValues[1].text = view.PipeCount + " / " + view.PipeCapacity;
            pipeValues[2].text = view.Capacity + "  (" + (view.Fill * 100).ToString("0.#") + "%)";
            pipeValues[3].text = view.Nodes + " / " + view.Containers;
            if (storesItems)
            {
                status.text =
                    view.Conflict ? "连接的容器存在液体冲突，已暂停均衡。"
                    : view.Count > view.Capacity ? "存量超出容量，提取后恢复存入。"
                    : "连通容器按容量共享液位。";
            }

            emptyLabel.transform.parent.gameObject.SetActive(storesItems);
            emptyLabel.text =
                Time.unscaledTime < emptyUntil ? "确认清空整个液体网" : "清空整个液体网";
            peers = PipeTypeRegistry.IsUndirected(factory.entityPool[entity].protoId)
                ? PipeTopology.Neighbors(factory, entity)
                : graph.Links(entity);
            connections.text = "相邻连接（" + peers.Length + "）";
            while (peerButtons.Count < peers.Length)
            {
                int connectionIndex = peerButtons.Count;
                var label = Button(
                    pipeControls.transform,
                    0,
                    "",
                    () => Disconnect(connectionIndex)
                );
                var button = label.transform.parent.gameObject;
                button.name = "disconnect-" + (connectionIndex + 1);
                peerButtons.Add(button);
            }
            const int columns = 2;
            const float buttonHeight = 26;
            const float rowSpacing = 32;
            const float columnGap = 8;
            for (int buttonIndex = 0; buttonIndex < peerButtons.Count; buttonIndex++)
            {
                var button = peerButtons[buttonIndex];
                bool visible = buttonIndex < peers.Length;
                button.SetActive(visible);
                if (!visible)
                {
                    continue;
                }
                int column = buttonIndex % columns;
                float top = 70 + buttonIndex / columns * rowSpacing;
                var bounds = (RectTransform)button.transform;
                bounds.anchorMin = new Vector2((float)column / columns, 1);
                bounds.anchorMax = new Vector2((float)(column + 1) / columns, 1);
                bounds.pivot = new Vector2(0.5f, 1);
                bounds.offsetMin = new Vector2(
                    column == 0 ? 0 : columnGap / 2,
                    -top - buttonHeight
                );
                bounds.offsetMax = new Vector2(column == columns - 1 ? 0 : -columnGap / 2, -top);
                var label = button.GetComponentInChildren<Text>();
                label.text = "断开连接 " + (buttonIndex + 1);
                NativePipeUi.Layout((RectTransform)label.transform, 0, buttonHeight, 10);
            }
            int rows = (peers.Length + columns - 1) / columns;
            float controlsBottom = 70 + rows * rowSpacing;
            if (machinePortButton.activeSelf)
            {
                NativePipeUi.Layout((RectTransform)machinePortButton.transform, controlsBottom, 30);
                controlsBottom += 38;
            }
            float controlsTop = storesItems ? 414 : 174;
            NativePipeUi.Layout(
                (RectTransform)pipeControls.transform,
                controlsTop,
                controlsBottom,
                32
            );
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(
                306,
                controlsTop + 32 + controlsBottom
            );
        }

        private static void Empty()
        {
            if (Time.unscaledTime < emptyUntil)
            {
                PipePools.Empty(PipeRuntime.Bind(factory.cargoTraffic), entity);
                emptyUntil = 0;
            }
            else
            {
                emptyUntil = Time.unscaledTime + 3;
            }
            UpdateContents();
        }

        private static void Disconnect(int index)
        {
            if (index < peers.Length)
            {
                PipeConnections.Disconnect(factory, entity, peers[index]);
            }
            UpdateContents();
        }

        public static void DrawLabels()
        {
            NativePipeUi.HideWorldLabels("FluidPipes.Pipe");
            if (panel == null || !panel.activeSelf || GameCamera.main == null)
            {
                return;
            }

            if (valve)
            {
                var valveEntity = factory.entityPool[entity];
                var ports = LDB.items.Select(valveEntity.protoId).prefabDesc.portPoses;
                for (int index = 0; index < Math.Min(2, ports.Length); index++)
                {
                    var point =
                        valveEntity.pos
                        + valveEntity.rot * ports[index].position
                        + valveEntity.pos.normalized * 0.6f;
                    NativePipeUi.WorldLabel(
                        "FluidPipes.Pipe",
                        index,
                        point,
                        index == 0 ? "A" : "B"
                    );
                }
                return;
            }
            for (int connectionIndex = 0; connectionIndex < peers.Length; connectionIndex++)
            {
                int peer = peers[connectionIndex];
                if (!PipeRuntime.IsPipeObject(factory, peer))
                {
                    continue;
                }

                var pos = peer > 0 ? factory.entityPool[peer].pos : factory.prebuildPool[-peer].pos;
                var dimensions = PipeTypeRegistry.Geometry(
                    PipeRuntime.ObjectPrototype(factory, peer)
                );
                NativePipeUi.WorldLabel(
                    "FluidPipes.Pipe",
                    connectionIndex,
                    pos + pos.normalized * (dimensions.CenterHeight + dimensions.Radius + 0.06f),
                    (connectionIndex + 1).ToString(),
                    (RectTransform)panel.transform
                );
            }
        }

        public static void Dispose()
        {
            Hide();
            PipeSelectionHighlights.Dispose();
            inventoryWindow?._Destroy();
            panel = null;
            inventoryWindow = null;
            NativeTankInventoryBridge.Dispose();
            NativePipeUi.DisposeWorldLabels();
            entity = 0;
            peerButtons.Clear();
        }
    }
}
