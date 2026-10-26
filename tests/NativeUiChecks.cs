using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluidPipes;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class SceneCapture
{
    private static void SetUiHand(int itemId, int count, int points)
    {
        var player = GameMain.mainPlayer;
        player.SetHandItemId_Unsafe(itemId);
        player.SetHandItemCount_Unsafe(count);
        player.SetHandItemInc_Unsafe(points);
    }

    private static void NativeTankGesture(UITankWindow window)
    {
        window.OnTakeBackPointerEnter();
        window.OnTakeBackPointerDown();
        window._Update();
        window.OnTakeBackPointerUp();
        window.OnTakeBackPointerExit();
    }

    private void ClickNativeUi(Transform root, string caption)
    {
        var button = root.GetComponentsInChildren<Button>(false)
            .SingleOrDefault(candidate =>
                candidate.GetComponentsInChildren<Text>(false).Any(label => label.text == caption)
            );
        RequirePort(button != null, "A native template button exposes caption: " + caption + ".");
        button.onClick.Invoke();
    }

    private IEnumerator CheckNativeInventoryUi()
    {
        int firstTank = PlaceFacility(2106, Point(18, 15), Quaternion.LookRotation(north, up));
        int secondTank = PlaceFacility(2106, Point(29, 15), Quaternion.LookRotation(north, up));
        int unrelatedTank = PlaceFacility(2106, Point(40, 15), Quaternion.LookRotation(north, up));
        int firstSlot = NearestPort(firstTank, factory.entityPool[secondTank].pos);
        int secondSlot = NearestPort(secondTank, factory.entityPool[firstTank].pos);
        int[] line = FluidLine(Port(firstTank, firstSlot), Port(secondTank, secondSlot));
        Attach(firstTank, firstSlot, line[0], true);
        Attach(secondTank, secondSlot, line[line.Length - 1], false);
        int firstTankId = factory.entityPool[firstTank].tankId;
        int secondTankId = factory.entityPool[secondTank].tankId;
        int unrelatedTankId = factory.entityPool[unrelatedTank].tankId;
        factory.factoryStorage.tankPool[unrelatedTankId].fluidId = Water;
        factory.factoryStorage.tankPool[unrelatedTankId].fluidCount = 777;
        factory.factoryStorage.tankPool[unrelatedTankId].fluidInc = 3108;
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        RequirePort(
            PipePools.Insert(graph, line[0], Water, 10000, 40000),
            "A native inventory fixture contains two connected tanks and an isolated tank."
        );
        PipePools.View(graph, line[0], true);

        var player = GameMain.mainPlayer;
        Vector3 originalPlayerPosition = player.position;
        int inspectedPipe = line[line.Length / 2];
        player.position = factory.entityPool[inspectedPipe].pos;
        player.controller.actionInspect.SetInspectee(EObjectType.None, inspectedPipe);
        PipePanel.Update();
        var pipeWindow = PipePanel.InventoryWindow;
        CheckPipePanelLayout(pipeWindow);
        RequirePort(
            PipeSelectionHighlights.Count == 3,
            "Inspecting a middle pipe outlines the current node and both directly adjacent nodes."
        );
        var connectionButton = pipeWindow
            .transform.Find("pipe-controls")
            .GetComponentsInChildren<UIButton>(false)
            .Single(button =>
                button.GetComponentsInChildren<Text>(false).Any(text => text.text == "断开连接 1")
            );
        connectionButton.OnPointerEnter(new PointerEventData(EventSystem.current));
        PipePanel.Update();
        SetView(23, 15, 11, 12, 3, 0.8f);
        player.position = Point(23, 20);
        for (int frame = 0; frame < 6; frame++)
        {
            yield return null;
        }
        int outlineRebuilds = PipeSelectionHighlights.MeshRebuilds;
        for (int frame = 0; frame < 12; frame++)
        {
            PipePanel.Update();
            yield return null;
        }
        SaveImage("ingame-machine-ports-pipe-ui.png", true);
        RequirePort(
            PipeSelectionHighlights.Count == 3
                && PipeSelectionHighlights.MeshRebuilds == outlineRebuilds
                && PipeSelectionHighlights.SelectedEntity == inspectedPipe
                && PipeSelectionHighlights.SelectedDraws == 1
                && PipeSelectionHighlights.AdjacentDraws == 2,
            "Stable inspection renders separate selected/adjacent contours while reusing cached geometry."
        );
        connectionButton.OnPointerExit(new PointerEventData(EventSystem.current));
        var junctionCapture = CaptureJunctionOutline(inspectedPipe);
        while (junctionCapture.MoveNext())
        {
            yield return junctionCapture.Current;
        }
        RequirePort(
            pipeWindow != null
                && pipeWindow.active
                && pipeWindow.productTipButton.gameObject.activeInHierarchy
                && pipeWindow.transform.Find("panel-bg/border") != null
                && pipeWindow.transform.Find("produce/circle-back/col") != null
                && !pipeWindow
                    .GetComponentsInChildren<Text>(false)
                    .Any(label => label.text.Contains("清空当前")),
            "Pipe inspection reuses the native tank window frame and circular pointer control."
        );
        var circleEvents = pipeWindow
            .transform.Find("produce/circle-back/col")
            .GetComponent<EventTrigger>();
        RequirePort(
            circleEvents.triggers.Count >= 4
                && circleEvents.triggers.All(entry =>
                    entry.callback.GetPersistentEventCount() > 0
                    && Enumerable
                        .Range(0, entry.callback.GetPersistentEventCount())
                        .All(index => entry.callback.GetPersistentTarget(index) == pipeWindow)
                ),
            "The cloned circular control routes its native pointer events to the pipe window."
        );

        SetUiHand(Water, 10, 40);
        long before = PipePools.View(graph, line[0]).Count;
        NativeTankGesture(pipeWindow);
        RequirePort(
            player.inhandItemCount == 8
                && player.inhandItemInc == 32
                && PipePools.View(graph, line[0]).Count == before + 2
                && PipePools.View(graph, line[0]).ProliferatorPoints == (before + 2) * 4,
            "The stock hold-to-inject gesture commits water and proliferator points to the shared pipe inventory."
        );
        SetUiHand(0, 0, 0);
        NativeTankGesture(pipeWindow);
        RequirePort(
            player.inhandItemId == Water
                && player.inhandItemCount == 2
                && player.inhandItemInc == 8
                && PipePools.View(graph, line[0]).Count == before,
            "The stock extraction gesture withdraws conserved water from shared storage."
        );
        SetUiHand(Oil, 10, 40);
        NativeTankGesture(pipeWindow);
        RequirePort(
            player.inhandItemCount == 10
                && player.inhandItemInc == 40
                && PipePools.View(graph, line[0]).Count == before,
            "The native circular interaction preserves the held stack when the network contains a different liquid."
        );
        var closeButton = pipeWindow
            .transform.Find("panel-bg/btn-box/close-wnd-btn")
            .GetComponent<UIButton>();
        closeButton.OnPointerClick(
            new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }
        );
        PipePanel.Update();
        RequirePort(
            !pipeWindow.active
                && player.controller.actionInspect.inspectId == 0
                && UIRoot.instance.uiGame.beltWindow.GetComponent<CanvasGroup>().blocksRaycasts,
            "The stock close button closes pipe inspection and restores the underlying native inspector."
        );
        RequirePort(
            PipeSelectionHighlights.Count == 0,
            "Closing pipe inspection releases adjacent-pipe highlights."
        );

        SetUiHand(0, 0, 0);
        player.position = factory.entityPool[firstTank].pos;
        player.controller.actionInspect.SetInspectee(EObjectType.None, firstTank);
        var tankWindow = UIRoot.instance.uiGame.tankWindow;
        tankWindow._Update();
        RequirePort(
            tankWindow.active
                && tankWindow.storage == factory.factoryStorage
                && tankWindow.countText.text == before.ToString(),
            "Connected native tanks display shared inventory while their world storage remains assigned to the real factory."
        );
        SetUiHand(Water, 10, 40);
        NativeTankGesture(tankWindow);
        RequirePort(
            player.inhandItemCount == 8
                && player.inhandItemInc == 32
                && PipePools.View(graph, line[0]).Count == before + 2,
            "Native tank injection enters the same shared inventory as pipe injection."
        );
        long localCount = factory.factoryStorage.tankPool[firstTankId].fluidCount;
        long localPoints = factory.factoryStorage.tankPool[firstTankId].fluidInc;
        var inventoryBeforeClear = PipePools.View(graph, line[0]);
        ClickNativeUi(tankWindow.transform, "清空本罐");
        ClickNativeUi(tankWindow.transform, "确认清空本罐");
        RequirePort(
            factory.factoryStorage.tankPool[firstTankId].fluidCount == 0
                && PipePools.View(graph, line[0]).Count == inventoryBeforeClear.Count - localCount
                && PipePools.View(graph, line[0]).ProliferatorPoints
                    == inventoryBeforeClear.ProliferatorPoints - localPoints
                && factory.factoryStorage.tankPool[unrelatedTankId].fluidCount == 777,
            "Clearing the current tank removes exactly its local stock and points while preserving connected and isolated inventories."
        );
        SetUiHand(0, 0, 0);
        long remaining = PipePools.View(graph, line[0]).Count;
        NativeTankGesture(tankWindow);
        RequirePort(
            player.inhandItemCount == 2
                && player.inhandItemInc == 8
                && PipePools.View(graph, line[0]).Count == remaining - 2,
            "An empty local tank can extract from the remaining connected inventory through the native gesture."
        );
        ClickNativeUi(tankWindow.transform, "清空整网");
        ClickNativeUi(tankWindow.transform, "确认清空整网");
        RequirePort(
            PipePools.View(graph, line[0]).Count == 0
                && factory.factoryStorage.tankPool[firstTankId].fluidCount == 0
                && factory.factoryStorage.tankPool[secondTankId].fluidCount == 0
                && factory.factoryStorage.tankPool[unrelatedTankId].fluidCount == 777,
            "Clearing the entire network empties both connected tanks and pipes while preserving the isolated tank."
        );
        RequirePort(
            PipePools.Insert(graph, line[0], Water, 10000, 40000),
            "The cleared shared inventory accepts a new water supply."
        );
        SetUiHand(0, 0, 0);
        SetView(23, 15, 24, 25, 10, 1);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-machine-ports-native-tank.png", true);
        player.controller.actionInspect.InspectNothing();
        PipePanel.Update();
        Vector3 valvePosition = Point(18, 25);
        Quaternion valveRotation = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(east, valvePosition.normalized),
            valvePosition.normalized
        );
        int valve = PlaceFacility(Prototypes.ValveId, valvePosition, valveRotation);
        player.position = factory.entityPool[valve].pos;
        player.controller.actionInspect.SetInspectee(EObjectType.None, valve);
        PipePanel.Update();
        var valvePanel = GameObject.Find("FluidPipes.Panel");
        RequirePort(
            valvePanel != null
                && valvePanel.transform.Find("panel-bg/border") != null
                && !valvePanel.transform.Find("produce").gameObject.activeSelf,
            "Valve settings reuse the native window frame with their own control content."
        );
        SetView(18, 25, 6, 8, 3, 0.8f);
        player.position = Point(14, 29);
        for (int frame = 0; frame < 6; frame++)
        {
            yield return null;
        }
        CheckNativeButtonTypography(valvePanel.transform.Find("valve-controls"));
        SaveImage("ingame-machine-ports-valve-ui.png", true);
        ClickNativeUi(valvePanel.transform, "阀门：开启");
        var valveInputs = valvePanel
            .transform.Find("valve-controls")
            .GetComponentsInChildren<InputField>(false)
            .OrderByDescending(input => ((RectTransform)input.transform).anchoredPosition.y)
            .ToArray();
        RequirePort(valveInputs.Length == 2, "Valve settings expose two native input fields.");
        valveInputs[0].onEndEdit.Invoke("123.5");
        valveInputs[1].onEndEdit.Invoke("90");
        var valveOptions = PipeValves.Get(factory, valve);
        RequirePort(
            !valveOptions.Enabled
                && Math.Abs(valveOptions.Rate - 123.5) < 0.001
                && Math.Abs(valveOptions.Target - 0.9) < 0.001,
            "Native button and input-field callbacks preserve valve enable, rate and target controls."
        );
        CheckSandboxValve(valve);
        valveOptions = PipeValves.Get(factory, valve);
        ClickNativeUi(valvePanel.transform, "工作模式：普通阀门");
        ClickNativeUi(valvePanel.transform, "阀门：关闭");
        var infiniteInput = valvePanel
            .transform.Find("valve-controls/infinite-settings")
            .GetComponentInChildren<InputField>();
        infiniteInput.onEndEdit.Invoke("75");
        PipeValves.Step(factory, valve, 1.0 / 60);
        PipePanel.Update();
        RequirePort(
            valveOptions.InfiniteEnabled && valveOptions.InfiniteTarget == 0.75,
            "Sandbox controls select infinite mode and a native target-level input."
        );
        for (int frame = 0; frame < 6; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-machine-ports-infinite-ui.png", true);
        player.controller.actionInspect.InspectNothing();
        PipePanel.Update();
        SetView(18, 25, 2.8f, 3.2f, 2.2f, 0.45f);
        player.position = Point(8, 28);
        for (int frame = 0; frame < 6; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-machine-ports-valve-model.png");
        var valveEntity = factory.entityPool[valve];
        var valveDescription = LDB.items.Select(Prototypes.ValveId).prefabDesc;
        Vector3 axis = (
            valveDescription.portPoses[1].position - valveDescription.portPoses[0].position
        ).normalized;
        Vector3 worldAxis = valveEntity.rot * axis;
        Vector3 localUp = valveEntity.pos.normalized;
        Vector3 across = Vector3.Cross(worldAxis, localUp).normalized;
        cameraTarget = valveEntity.pos + localUp * 0.47f;
        cameraPosition = cameraTarget + across * 5;
        orthographicCapture = true;
        cameraUp = localUp;
        for (int frame = 0; frame < 3; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-machine-ports-valve-side.png");
        cameraPosition = cameraTarget + localUp * 5 - across * 0.001f;
        cameraUp = worldAxis;
        for (int frame = 0; frame < 3; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-machine-ports-valve-top.png");
        player.position = originalPlayerPosition;
        SetView(0, 0, 4, 7, 5, 0.7f);
    }

    private void CheckSandboxValve(int valve)
    {
        var valveEntity = factory.entityPool[valve];
        var ports = LDB.items.Select(Prototypes.ValveId).prefabDesc.portPoses;
        int first = PlaceValvePipe(valveEntity, ports[0]);
        int second = PlaceValvePipe(valveEntity, ports[1]);
        report.Add(
            "Valve local ports: "
                + ports[0].position.ToString("F4")
                + " / "
                + ports[1].position.ToString("F4")
        );
        factory.WriteObjectConn(valve, 0, true, first, 1);
        factory.WriteObjectConn(valve, 1, true, second, 1);
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        var settings = PipeValves.Get(factory, valve);
        settings.Enabled = true;
        settings.InfiniteEnabled = true;
        settings.InfiniteItemId = Water;
        settings.InfiniteTarget = 0.6;
        settings.InfiniteMode = InfiniteLevelMode.Exactly;
        RequirePort(
            PipeValves.Step(factory, valve, 1.0 / 60) == 6
                && PipePools.View(graph, first).Count == 6
                && PipePools.View(graph, second).Count == 0,
            "Sandbox infinite mode fills the selected isolated space to its target without power."
        );
        settings.InfiniteMode = InfiniteLevelMode.AtLeast;
        RequirePort(
            PipePools.Insert(graph, first, Water, 2, 8),
            "Infinite-level fixture accepts proliferated surplus."
        );
        RequirePort(
            PipeValves.Step(factory, valve, 1.0 / 60) == 0
                && PipePools.View(graph, first).Count == 8,
            "At-least mode preserves surplus fluid and points."
        );
        settings.InfiniteMode = InfiniteLevelMode.AtMost;
        RequirePort(
            PipeValves.Step(factory, valve, 1.0 / 60) == -2
                && PipePools.View(graph, first).Count == 6
                && PipePools.View(graph, first).ProliferatorPoints == 6,
            "At-most mode removes excess fluid with proportional proliferator points."
        );
        settings.InfiniteMode = InfiniteLevelMode.Exactly;
        settings.InfiniteTarget = 0;
        RequirePort(
            PipeValves.Step(factory, valve, 1.0 / 60) == -6
                && PipePools.View(graph, first).Count == 0,
            "An exact zero target drains the selected shared space."
        );
        RequirePort(
            PipePools.Insert(graph, first, Oil, 5, 20),
            "Conflicting-fluid fixture has conserved oil stock."
        );
        settings.InfiniteTarget = 1;
        RequirePort(
            PipeValves.Step(factory, valve, 1.0 / 60) == 0
                && PipePools.View(graph, first).Item == Oil
                && PipePools.View(graph, first).Count == 5
                && PipePools.View(graph, first).ProliferatorPoints == 20,
            "Infinite source selection preserves a different fluid already occupying the space."
        );
        settings.InfiniteSide = 1;
        settings.InfiniteTarget = 0.25;
        RequirePort(
            PipeValves.Step(factory, valve, 1.0 / 60) == 2
                && PipePools.View(graph, second).Count == 2,
            "Infinite-side selection controls the second endpoint independently."
        );
        PipePools.Empty(graph, second);
        bool sandbox = GameMain.data.gameDesc.isSandboxMode;
        try
        {
            GameMain.data.gameDesc.isSandboxMode = false;
            RequirePort(
                PipeValves.Step(factory, valve, 1.0 / 60) == 0
                    && PipePools.View(graph, second).Count == 0,
                "Saved infinite settings remain inactive outside sandbox mode."
            );
        }
        finally
        {
            GameMain.data.gameDesc.isSandboxMode = sandbox;
        }
        using (var stream = new MemoryStream())
        {
            PipeValves.Export(new BinaryWriter(stream));
            settings.InfiniteEnabled = false;
            stream.Position = 0;
            PipeValves.Import(new BinaryReader(stream));
        }
        settings = PipeValves.Get(factory, valve);
        RequirePort(
            settings.InfiniteEnabled
                && settings.InfiniteSide == 1
                && settings.InfiniteItemId == Water
                && settings.InfiniteTarget == 0.25
                && settings.InfiniteMode == InfiniteLevelMode.Exactly,
            "Infinite valve mode, side, fluid and target survive save serialization."
        );
        PipePools.Empty(graph, first);
        settings.Enabled = false;
        settings.InfiniteEnabled = false;
        settings.InfiniteSide = 0;
        settings.InfiniteMode = InfiniteLevelMode.Exactly;
        PipePanel.Update();
    }

    private int PlaceValvePipe(EntityData valve, Pose port)
    {
        Vector3 direction = valve.rot * port.forward;
        Vector3 position = valve.pos + valve.rot * port.position + direction * 1.2f;
        position = position.normalized * (factory.planet.realRadius + 0.2f);
        return PlaceFacility(
            Prototypes.ItemId,
            position,
            Quaternion.LookRotation(direction, position.normalized)
        );
    }

    private void CheckNativeButtonTypography(Transform root)
    {
        var template = UIRoot.instance.uiGame.tankWindow.inputButton.GetComponentInChildren<Text>(
            true
        );
        foreach (var button in root.GetComponentsInChildren<Button>(false))
        {
            var label = button.GetComponentInChildren<Text>();
            RequirePort(
                label.font == template.font
                    && label.material == template.material
                    && label.fontSize == template.fontSize
                    && label.fontStyle == template.fontStyle,
                "Action text uses the native tank-button font, material, size and style."
            );
        }
    }

    private void CheckPipePanelLayout(UITankWindow window)
    {
        var bounds = (RectTransform)window.transform;
        RequirePort(
            bounds.sizeDelta.x
                == ((RectTransform)UIRoot.instance.uiGame.tankWindow.transform).sizeDelta.x,
            "Pipe inspector uses the native tank-window width."
        );
        CheckNativeButtonTypography(window.transform.Find("pipe-controls"));
        var summary = (RectTransform)window.transform.Find("pipe-summary");
        RequirePort(
            summary.offsetMin.x >= 32 && summary.offsetMax.x <= -32,
            "Inventory properties share a padded content column."
        );
        foreach (var row in summary.GetComponentsInChildren<Text>(false))
        {
            RequirePort(
                row.fontSize == UIRoot.instance.uiGame.tankWindow.countLabel.fontSize
                    && row.fontStyle == FontStyle.Normal,
                "Pipe property typography follows the native inventory label: " + row.name
            );
        }
        foreach (
            var button in window
                .transform.Find("pipe-controls")
                .GetComponentsInChildren<Button>(false)
        )
        {
            var label = button.GetComponentInChildren<Text>();
            var textBounds = (RectTransform)label.transform;
            RequirePort(
                textBounds.offsetMin.x >= 10 && textBounds.offsetMax.x <= -10,
                "Action text retains native button content padding."
            );
        }
    }

    private void DumpNativeUi()
    {
        var lines = new List<string>();
        foreach (
            var root in new Transform[]
            {
                UIRoot.instance.uiGame.tankWindow.transform,
                UIRoot.instance.uiGame.splitterWindow.transform,
            }
        )
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                string path = child.name;
                for (
                    var parent = child.parent;
                    parent != null && child != root;
                    parent = parent.parent
                )
                {
                    path = parent.name + "/" + path;
                    if (parent == root)
                    {
                        break;
                    }
                }
                var rect = child as RectTransform;
                string controls = string.Join(
                    ",",
                    child
                        .GetComponents<Component>()
                        .Where(component => component != null)
                        .Select(component => component.GetType().Name)
                );
                lines.Add(
                    path
                        + " pos="
                        + rect?.anchoredPosition
                        + " size="
                        + rect?.sizeDelta
                        + " anchors="
                        + rect?.anchorMin
                        + "/"
                        + rect?.anchorMax
                        + " components="
                        + controls
                );
                var text = child.GetComponent<Text>();
                if (text != null)
                {
                    lines.Add(" TEXT " + text.text + " size=" + text.fontSize);
                }
            }
        }
        File.WriteAllLines(System.IO.Path.Combine(output, "native-ui-hierarchy.txt"), lines);
    }
}
