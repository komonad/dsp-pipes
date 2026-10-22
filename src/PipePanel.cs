using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace FluidPipes
{
    internal static class PipePanel
    {
        private static GameObject panel;
        private static Font font;
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
        private static PlanetFactory factory;
        private static int entity;
        private static int page;
        private static bool valve;
        private static bool editing;
        private static float refresh;
        private static float emptyUntil;
        private static int[] peers = new int[0];
        private static readonly List<GameObject> peerButtons = new List<GameObject>();

        private sealed class NativeVisibility
        {
            public CanvasGroup Group;
            public float Alpha;
            public bool Interactable;
            public bool Raycasts;
        }

        private static readonly List<NativeVisibility> hiddenWindows = new List<NativeVisibility>();

        private static RectTransform Rect(
            GameObject gameObject2,
            Transform parent,
            float y,
            float height
        )
        {
            gameObject2.layer = parent.gameObject.layer;
            var rect = gameObject2.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = new Vector2(12, -y - height);
            rect.offsetMax = new Vector2(-12, -y);
            return rect;
        }

        private static Text Label(
            Transform parent,
            float y,
            float height,
            string value,
            int size = 16
        )
        {
            var gameObject = new GameObject("label", typeof(RectTransform), typeof(Text));
            Rect(gameObject, parent, y, height);
            var text2 = gameObject.GetComponent<Text>();
            text2.font = font;
            text2.fontSize = size;
            text2.color = new Color(0.85f, 0.92f, 0.95f);
            text2.text = value;
            text2.raycastTarget = false;
            text2.supportRichText = false;
            return text2;
        }

        private static Text Button(Transform parent, float y, string value, Action action)
        {
            var gameObject = new GameObject(
                "button",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button)
            );
            Rect(gameObject, parent, y, 30);
            var image = gameObject.GetComponent<Image>();
            image.color = new Color(0.14f, 0.28f, 0.34f, 1);
            var button = gameObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => action());
            var text = Label(gameObject.transform, 3, 24, value);
            text.alignment = TextAnchor.MiddleCenter;
            return text;
        }

        private static InputField Input(
            Transform parent,
            float y,
            string label,
            Action<string> change
        )
        {
            Label(parent, y, 24, label);
            var gameObject = new GameObject(
                "input",
                typeof(RectTransform),
                typeof(Image),
                typeof(InputField)
            );
            Rect(gameObject, parent, y + 25, 30);
            gameObject.GetComponent<Image>().color = new Color(0.06f, 0.12f, 0.16f, 1);
            var text = Label(gameObject.transform, 3, 24, "");
            text.raycastTarget = false;
            var input = gameObject.GetComponent<InputField>();
            input.textComponent = text;
            input.contentType = InputField.ContentType.DecimalNumber;
            input.onEndEdit.AddListener(value =>
            {
                if (!editing)
                {
                    change(value);
                }
            });
            return input;
        }

        private static void Create()
        {
            var root = UIRoot.instance;
            var native = root.uiGame.beltWindow.GetComponentInChildren<Text>(true);
            font = native?.font ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            panel = new GameObject("FluidPipes.Panel", typeof(RectTransform), typeof(Image));
            var rect = panel.GetComponent<RectTransform>();
            rect.SetParent(root.overlayCanvas.transform, false);
            panel.layer = root.overlayCanvas.gameObject.layer;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(20, -140);
            rect.sizeDelta = new Vector2(400, 610);
            panel.GetComponent<Image>().color = new Color(0.035f, 0.075f, 0.10f, 0.98f);
            title = Label(panel.transform, 12, 30, "流体管道", 20);
            inventory = Label(panel.transform, 48, 132, "");
            status = Label(panel.transform, 184, 48, "");
            var close = Button(
                panel.transform,
                12,
                "×",
                () =>
                {
                    GameMain.mainPlayer?.controller?.actionInspect?.InspectNothing();
                    Hide();
                }
            );
            var closeRect = (RectTransform)close.transform.parent;
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one;
            closeRect.sizeDelta = new Vector2(28, 28);
            closeRect.anchoredPosition = new Vector2(-12, -12);
            pipeControls = new GameObject("pipe-controls", typeof(RectTransform));
            Rect(pipeControls, panel.transform, 236, 350);
            Button(pipeControls.transform, 0, "放入手持液体", Deposit);
            Button(pipeControls.transform, 38, "取出一组到手上", Withdraw);
            emptyLabel = Button(pipeControls.transform, 76, "清空全部连通液体", Empty);
            connections = Label(pipeControls.transform, 118, 28, "");
            for (int index2 = 0; index2 < 4; index2++)
            {
                int index = index2;
                var label = Button(
                    pipeControls.transform,
                    150 + index2 * 36,
                    "",
                    () => Disconnect(index)
                );
                peerButtons.Add(label.transform.parent.gameObject);
            }
            Button(
                pipeControls.transform,
                300,
                "下一页连接",
                () =>
                {
                    page++;
                    UpdateContents();
                }
            );
            valveControls = new GameObject("valve-controls", typeof(RectTransform));
            Rect(valveControls, panel.transform, 236, 350);
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
            direction = Button(
                valveControls.transform,
                38,
                "",
                () =>
                {
                    var valveSettings2 = Settings();
                    valveSettings2.Direction = (ValveDirection)(
                        ((int)valveSettings2.Direction + 1) % 3
                    );
                    UpdateContents();
                }
            );
            level = Button(
                valveControls.transform,
                76,
                "",
                () =>
                {
                    var valveSettings3 = Settings();
                    valveSettings3.Level = (ValveLevel)(((int)valveSettings3.Level + 1) % 3);
                    UpdateContents();
                }
            );
            rate = Input(
                valveControls.transform,
                118,
                "最大流量（份/秒，0 为关闭）",
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
                valveControls.transform,
                184,
                "目标液位（0–100%）",
                text2 =>
                {
                    if (Number(text2, out double parsedValue2))
                    {
                        Settings().Target = Math.Max(0, Math.Min(1, parsedValue2 / 100));
                    }
                    UpdateContents();
                }
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
            )
            {
                selected = pipe;
            }

            if (game.splitterWindow.active)
            {
                int entityId2 = game.splitterWindow.splitterId;
                if (entityId2 > 0 && entityId2 < current.cargoTraffic.splitterPool.Length)
                {
                    int targetEntityId = current.cargoTraffic.splitterPool[entityId2].entityId;
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

            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
            if (entity != selected || factory != current || valve != isValve)
            {
                entity = selected;
                factory = current;
                valve = isValve;
                page = 0;
                emptyUntil = 0;
                refresh = 0;
            }
            pipeControls.SetActive(!valve);
            valveControls.SetActive(valve);
            if (Time.unscaledTime >= refresh)
            {
                UpdateContents();
                refresh = Time.unscaledTime + 0.2f;
            }
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
            if (panel != null)
            {
                panel.SetActive(false);
            }

            entity = 0;
            factory = null;
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

            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            if (valve)
            {
                title.text = "流体阀门";
                PipeValves.Ends(factory, entity, out int firstEntityId, out int secondEntityId);
                inventory.text =
                    "A 侧："
                    + (
                        firstEntityId == 0
                            ? "未连接"
                            : Describe(PipePools.View(graph, firstEntityId, true))
                    )
                    + "\n\nB 侧："
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
            title.text = "流体管道";
            var view = PipePools.View(graph, entity, true);
            inventory.text =
                "流体："
                + Fluid(view.Item)
                + "\n当前管段：约 "
                + (view.Fill * PipeGraph.SegmentCapacity).ToString("F2")
                + " / 100\n管道内总量："
                + view.PipeCount
                + " / "
                + view.PipeCapacity
                + "\n连通总量："
                + view.Count
                + " / "
                + view.Capacity
                + "  ("
                + (view.Fill * 100).ToString("F2")
                + "%)\n管段："
                + view.Nodes
                + "  外部容器："
                + view.Containers;
            status.text = view.Conflict
                ? "连接的容器存在液体冲突，已暂停均衡。"
                : "连通容器按容量共享液位。";
            emptyLabel.text =
                Time.unscaledTime < emptyUntil
                    ? "再次点击确认清空全部连通液体"
                    : "清空全部连通液体";
            peers = PipeTopology.Neighbors(factory, entity);
            int pages = Math.Max(1, (peers.Length + 3) / 4);
            page %= pages;
            connections.text = "相邻管道连接  " + (page + 1) + " / " + pages;
            for (int index2 = 0; index2 < peerButtons.Count; index2++)
            {
                int index = page * 4 + index2;
                peerButtons[index2].SetActive(index < peers.Length);
                if (index < peers.Length)
                {
                    peerButtons[index2].GetComponentInChildren<Text>().text =
                        "断开连接 " + (index + 1);
                }
            }
        }

        private static void Deposit()
        {
            var player = GameMain.mainPlayer;
            if (
                player == null
                || player.inhandItemCount <= 0
                || !TransportItemRegistry.IsAllowed(player.inhandItemId)
            )
            {
                return;
            }

            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            var view = PipePools.View(graph, entity, true);
            int count = (int)
                Math.Min(player.inhandItemCount, Math.Max(0, view.Capacity - view.Count));
            if (count <= 0)
            {
                return;
            }

            int proliferatorPoints = (int)(
                (long)player.inhandItemInc * count / player.inhandItemCount
            );
            if (PipePools.Insert(graph, entity, player.inhandItemId, count, proliferatorPoints))
            {
                player.SetHandItemInc_Unsafe(player.inhandItemInc - proliferatorPoints);
                player.SetHandItemCount_Unsafe(player.inhandItemCount - count);
                if (player.inhandItemCount == 0)
                {
                    player.SetHandItemId_Unsafe(0);
                }
            }
            UpdateContents();
        }

        private static void Withdraw()
        {
            var player = GameMain.mainPlayer;
            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            var inventoryView = PipePools.View(graph, entity, true);
            if (
                player == null
                || inventoryView.Count == 0
                || player.inhandItemCount > 0 && player.inhandItemId != inventoryView.Item
            )
            {
                return;
            }

            int room = LDB.items.Select(inventoryView.Item).StackSize - player.inhandItemCount;
            if (room <= 0)
            {
                return;
            }

            var amount = PipePools.Take(graph, entity, room);
            if (amount.Count > 0)
            {
                player.SetHandItemId_Unsafe(amount.Item);
                player.SetHandItemCount_Unsafe(player.inhandItemCount + (int)amount.Count);
                player.SetHandItemInc_Unsafe(player.inhandItemInc + (int)amount.ProliferatorPoints);
            }
            UpdateContents();
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

        private static void Disconnect(int offset)
        {
            int index = page * 4 + offset;
            if (index < peers.Length)
            {
                PipeConnections.Disconnect(factory, entity, peers[index]);
            }
            UpdateContents();
        }

        public static void DrawLabels()
        {
            if (panel == null || !panel.activeSelf || GameCamera.main == null)
            {
                return;
            }

            if (valve)
            {
                var entity2 = factory.entityPool[entity];
                var ports = LDB.items.Select(entity2.protoId).prefabDesc.portPoses;
                for (int index = 0; index < Math.Min(2, ports.Length); index++)
                {
                    var point =
                        entity2.pos
                        + entity2.rot * ports[index].position
                        + entity2.pos.normalized * 0.6f;
                    var screen = GameCamera.main.WorldToScreenPoint(point);
                    if (screen.z > 0)
                    {
                        GUI.Box(
                            new Rect(screen.x - 14, Screen.height - screen.y - 14, 28, 28),
                            index == 0 ? "A" : "B"
                        );
                    }
                }
                return;
            }
            for (int index2 = 0; index2 < peers.Length; index2++)
            {
                int peer = peers[index2];
                if (!PipeRuntime.IsPipeObject(factory, peer))
                {
                    continue;
                }

                var pos = peer > 0 ? factory.entityPool[peer].pos : factory.prebuildPool[-peer].pos;
                var screen = GameCamera.main.WorldToScreenPoint(
                    pos + pos.normalized * (PipeMesh.Radius + 0.06f)
                );
                if (screen.z > 0)
                {
                    GUI.Box(
                        new Rect(screen.x - 14, Screen.height - screen.y - 14, 28, 28),
                        (index2 + 1).ToString()
                    );
                }
            }
        }

        public static void Dispose()
        {
            Hide();
            if (panel != null)
            {
                UnityEngine.Object.Destroy(panel);
            }
            panel = null;
            entity = 0;
            peerButtons.Clear();
        }
    }
}
