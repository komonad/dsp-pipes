using System;
using FluidPipes.API;
using UnityEngine;
using UnityEngine.UI;

namespace FluidPipes
{
    internal static class PipeMachinePortPanel
    {
        private static GameObject panel;
        private static PlanetFactory factory;
        private static int pipeEntityId;
        private static int connectionIndex;
        private static int choicePage;
        private static Text connectionLabel;
        private static Text selectedLabel;
        private static Text statusLabel;
        private static Text enabledLabel;
        private static Text choicePageLabel;
        private static readonly Text[] choiceLabels = new Text[4];
        private static MachinePortSnapshot[] connections = new MachinePortSnapshot[0];
        private static MachinePortOptions[] choices = new MachinePortOptions[0];
        private static float nextRefresh;
        private static string feedback;

        private static void Create()
        {
            panel = NativePipeUi.SettingsWindow(
                "FluidPipes.MachinePorts",
                "机器直连接口",
                new Vector2(460, -140),
                new Vector2(420, 560),
                Hide
            );
            connectionLabel = PipePanel.Label(panel.transform, 48, 28, "");
            PipePanel.Button(
                panel.transform,
                82,
                "下一个接口",
                () =>
                {
                    connectionIndex++;
                    choicePage = 0;
                    feedback = null;
                    Refresh();
                }
            );
            selectedLabel = PipePanel.Label(panel.transform, 122, 32, "");
            statusLabel = PipePanel.Label(panel.transform, 160, 54, "", 14);
            for (int index = 0; index < choiceLabels.Length; index++)
            {
                int choiceIndex = index;
                choiceLabels[index] = PipePanel.Button(
                    panel.transform,
                    224 + index * 40,
                    "",
                    () => SelectChoice(choiceIndex)
                );
            }
            choicePageLabel = PipePanel.Button(
                panel.transform,
                390,
                "",
                () =>
                {
                    choicePage++;
                    Refresh();
                }
            );
            enabledLabel = PipePanel.Button(panel.transform, 430, "", ToggleEnabled);
            PipePanel.Button(panel.transform, 470, "断开当前机器接口", Disconnect);
            PipePanel.Button(panel.transform, 510, "关闭接口设置", Hide);
        }

        public static void Open(PlanetFactory targetFactory, int targetPipeEntityId)
        {
            if (panel == null)
            {
                Create();
            }
            factory = targetFactory;
            pipeEntityId = targetPipeEntityId;
            connectionIndex = 0;
            choicePage = 0;
            feedback = null;
            UIRoot.instance.uiGame.ShutPlayerInventory();
            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
            Refresh();
        }

        private static string Describe(MachinePortOptions options)
        {
            string item = LDB.items.Select(options.ItemId)?.name ?? "待选择";
            return (
                    options.Direction == MachinePortDirection.IntoMachine
                        ? "供给机器："
                        : "提取产物："
                ) + item;
        }

        private static void Refresh()
        {
            connections = PipeApi.GetMachinePorts(factory, pipeEntityId);
            if (connections.Length == 0)
            {
                Hide();
                return;
            }
            connectionIndex %= connections.Length;
            var connection = connections[connectionIndex];
            int prototypeId = factory.entityPool[connection.MachineEntityId].protoId;
            connectionLabel.text =
                LDB.items.Select(prototypeId).name
                + " · 插槽 "
                + (connection.SlotIndex + 1)
                + " · "
                + (connectionIndex + 1)
                + "/"
                + connections.Length;
            selectedLabel.text = "当前：" + Describe(connection.Options);
            statusLabel.text = feedback ?? PipeMachinePorts.Status(factory, connection);
            choices = PipeApi.GetMachinePortChoices(factory, connection.MachineEntityId);
            int pageCount = Math.Max(
                1,
                (choices.Length + choiceLabels.Length - 1) / choiceLabels.Length
            );
            choicePage %= pageCount;
            for (int index = 0; index < choiceLabels.Length; index++)
            {
                int candidateIndex = choicePage * choiceLabels.Length + index;
                var label = choiceLabels[index];
                label.transform.parent.gameObject.SetActive(candidateIndex < choices.Length);
                if (candidateIndex < choices.Length)
                {
                    var choice = choices[candidateIndex];
                    label.text = Describe(choice);
                    label.color =
                        choice.ItemId == connection.Options.ItemId
                        && choice.Direction == connection.Options.Direction
                            ? new Color(0.4f, 1, 0.8f)
                            : new Color(0.85f, 0.92f, 0.95f);
                }
            }
            choicePageLabel.text = "下一页配方物品  " + (choicePage + 1) + "/" + pageCount;
            enabledLabel.text = connection.Options.Enabled
                ? "接口：运行（点击暂停）"
                : "接口：暂停（点击启用）";
        }

        private static void SelectChoice(int index)
        {
            int choiceIndex = choicePage * choiceLabels.Length + index;
            if (connections.Length == 0 || choiceIndex >= choices.Length)
            {
                return;
            }
            var connection = connections[connectionIndex];
            bool accepted = PipeApi.TrySetMachinePortOptions(
                factory,
                connection.MachineEntityId,
                connection.SlotIndex,
                choices[choiceIndex]
            );
            feedback = accepted ? null : "物品与管网库存或其它接口筛选冲突，请检查连接与存量。";
            Refresh();
        }

        private static void ToggleEnabled()
        {
            if (connections.Length == 0)
            {
                return;
            }
            var connection = connections[connectionIndex];
            var options = connection.Options;
            bool accepted = PipeApi.TrySetMachinePortOptions(
                factory,
                connection.MachineEntityId,
                connection.SlotIndex,
                new MachinePortOptions(options.ItemId, options.Direction, !options.Enabled)
            );
            feedback = accepted ? null : "配方、物品许可或管网筛选存在冲突，请重新选择接口物品。";
            Refresh();
        }

        private static void Disconnect()
        {
            if (connections.Length > 0)
            {
                var connection = connections[connectionIndex];
                PipeApi.TryDisconnectMachinePort(
                    factory,
                    connection.MachineEntityId,
                    connection.SlotIndex
                );
                feedback = null;
                Refresh();
            }
        }

        public static void Update()
        {
            if (panel == null || !panel.activeSelf)
            {
                return;
            }
            if (factory != GameMain.localPlanet?.factory || !PipeApi.IsPipe(factory, pipeEntityId))
            {
                Hide();
                return;
            }
            if (Time.unscaledTime >= nextRefresh)
            {
                Refresh();
                nextRefresh = Time.unscaledTime + 0.2f;
            }
        }

        public static void Hide()
        {
            bool wasVisible = panel != null && panel.activeSelf;
            if (panel != null)
            {
                panel.SetActive(false);
            }
            factory = null;
            if (wasVisible && PipePanel.InventoryWindow?.active == true)
            {
                UIRoot.instance.uiGame.OpenPlayerInventory();
            }
        }

        public static void DrawLabels()
        {
            NativePipeUi.HideWorldLabels("FluidPipes.MachinePorts");
            if (panel == null || !panel.activeSelf || GameCamera.main == null)
            {
                return;
            }
            for (int connectionIndex = 0; connectionIndex < connections.Length; connectionIndex++)
            {
                var connection = connections[connectionIndex];
                var entity = factory.entityPool[connection.MachineEntityId];
                var slots = PipeMachinePorts
                    .MachineDescription(factory, connection.MachineEntityId)
                    ?.slotPoses;
                if (slots == null)
                {
                    continue;
                }
                Vector3 position = entity.pos + entity.rot * slots[connection.SlotIndex].position;
                NativePipeUi.WorldLabel(
                    "FluidPipes.MachinePorts",
                    connectionIndex,
                    position,
                    Describe(connection.Options)
                );
            }
        }

        public static void Dispose()
        {
            NativePipeUi.HideWorldLabels("FluidPipes.MachinePorts");
            if (panel != null)
            {
                UnityEngine.Object.Destroy(panel);
            }
            panel = null;
            factory = null;
        }
    }
}
