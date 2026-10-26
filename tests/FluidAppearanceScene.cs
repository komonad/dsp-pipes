using System;
using System.Collections;
using System.Linq;
using FluidPipes;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class SceneCapture
{
    private IEnumerator CaptureFluidAppearance()
    {
        int[] items = { 1000, 1007, 1114, 1116, 1120, 1121 };
        var captions = new Vector3[items.Length];
        for (int index = 0; index < items.Length; index++)
        {
            float horizontal = (index - 2.5f) * 10;
            Vector3 position = Point(horizontal, -28);
            int tank = PlaceFacility(
                2106,
                position,
                Quaternion.LookRotation(
                    Vector3.ProjectOnPlane(north, position.normalized),
                    position.normalized
                )
            );
            Vector3 pipeEnd = Point(horizontal, -36);
            int slot = NearestPort(tank, pipeEnd);
            int[] line = FluidLine(Port(tank, slot), pipeEnd);
            Attach(tank, slot, line[0], true);
            int tankId = factory.entityPool[tank].tankId;
            factory.factoryStorage.tankPool[tankId].fluidId = items[index];
            factory.factoryStorage.tankPool[tankId].fluidCount = 5000;
            var inventory = PipePools.View(PipeRuntime.Bind(factory.cargoTraffic), line[0], true);
            RequirePort(
                inventory.Item == items[index] && inventory.Count == 5000,
                "The color-comparison tank and pipe contain conserved fluid " + items[index] + "."
            );
            captions[index] = Point(horizontal, -38, 0.5f);
        }
        GameMain.mainPlayer.position = Point(0, -8);
        UIRoot.instance.uiGame.inventoryWindow._Close();
        SetView(0, -31, 32, 38, 0, 1);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        for (int index = 0; index < items.Length; index++)
        {
            NativePipeUi.WorldLabel(
                "FluidPipes.FluidComparison",
                index,
                captions[index],
                LDB.items.Select(items[index]).name
            );
        }
        SaveImage("ingame-machine-ports-fluid-colors.png", true);
        NativePipeUi.HideWorldLabels("FluidPipes.FluidComparison");
        SetView(15, -31, 14, 16, 3, 0.9f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        for (int index = 3; index < items.Length; index++)
        {
            NativePipeUi.WorldLabel(
                "FluidPipes.FluidComparison",
                index,
                captions[index],
                LDB.items.Select(items[index]).name
            );
        }
        SaveImage("ingame-machine-ports-fluid-gases.png", true);
        NativePipeUi.HideWorldLabels("FluidPipes.FluidComparison");
    }

    private IEnumerator CaptureJunctionOutline(int originalPipe)
    {
        int junction = PlaceFacility(
            Prototypes.ItemId,
            Point(-22, 14),
            Quaternion.LookRotation(north, up)
        );
        Vector3[] positions =
        {
            Point(-25, 14),
            Point(-19, 14),
            Point(-22, 17),
            Point(-22, 14, 1.3333333f),
            Point(-22, 11),
            Point(-22, 11, 1.3333333f),
        };
        foreach (var position in positions)
        {
            int peer = PlaceFacility(
                Prototypes.ItemId,
                position,
                Quaternion.LookRotation(north, position.normalized)
            );
            Connect(junction, peer, 1);
        }
        GameMain.mainPlayer.position = Point(-18, 20);
        GameMain.mainPlayer.controller.actionInspect.SetInspectee(EObjectType.None, junction);
        PipePanel.Update();
        RequirePort(
            PipeSelectionHighlights.Count == 7,
            "A junction outline includes its current node and six direct neighbors."
        );
        SetView(-22, 14, 7, 9, 4, 0.8f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-machine-ports-pipe-junction-outline.png", true);
        RequirePort(
            PipeSelectionHighlights.SelectedEntity == junction
                && PipeSelectionHighlights.SelectedDraws == 1
                && PipeSelectionHighlights.AdjacentDraws == 6,
            "Selected and adjacent junction nodes render through separate outline channels."
        );
        var window = PipePanel.InventoryWindow;
        CheckPipePanelLayout(window);
        var controls = window.transform.Find("pipe-controls");
        var disconnectButtons = controls
            .GetComponentsInChildren<Button>(false)
            .Where(button => button.name.StartsWith("disconnect-", StringComparison.Ordinal))
            .ToArray();
        RequirePort(
            disconnectButtons.Length == positions.Length
                && disconnectButtons.All(button =>
                    ((RectTransform)button.transform).rect.height == 26
                )
                && disconnectButtons
                    .Select(button => ((RectTransform)button.transform).anchorMin.x)
                    .Distinct()
                    .Count() == 2,
            "Six adjacent connections remain visible together in a compact two-column layout."
        );
        int[] adjacent = PipeTopology.Neighbors(factory, junction);
        ClickNativeUi(controls, "断开连接 6");
        RequirePort(
            PipeTopology.Neighbors(factory, junction).SequenceEqual(adjacent.Take(5))
                && controls
                    .GetComponentsInChildren<Button>(false)
                    .Count(button =>
                        button.name.StartsWith("disconnect-", StringComparison.Ordinal)
                    ) == 5,
            "A connection beyond the fourth button disconnects its own peer and updates the visible list."
        );
        GameMain.mainPlayer.position = Point(23, 20);
        GameMain.mainPlayer.controller.actionInspect.SetInspectee(EObjectType.None, originalPipe);
        PipePanel.Update();
    }
}
