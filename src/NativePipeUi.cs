using System;
using System.Collections.Generic;
using FluidPipes.Integration.Reservoirs;
using UnityEngine;
using UnityEngine.UI;

namespace FluidPipes
{
    internal static class NativePipeUi
    {
        private static readonly Dictionary<string, List<Text>> worldLabels =
            new Dictionary<string, List<Text>>();

        private static UITankWindow Template => UIRoot.instance.uiGame.tankWindow;

        internal static UITankWindow Window(string name, Action close)
        {
            var window = UnityEngine.Object.Instantiate(Template, Template.transform.parent, false);
            window.name = name;
            ((RectTransform)window.transform).sizeDelta = TankNetworkControls.TemplateSize(
                Template
            );
            var inventory = window.transform.Find("produce") as RectTransform;
            if (inventory != null)
            {
                Layout(inventory, 40, 260, 0);
            }
            var networkControls = window.transform.Find("FluidPipes.NetworkControls");
            if (networkControls != null)
            {
                networkControls.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(networkControls.gameObject);
            }
            StripLocalizers(window.titleText.gameObject);
            foreach (var button in window.GetComponentsInChildren<UIButton>(true))
            {
                if (button.name.IndexOf("close", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                button.eventReceiver = null;
                button.onClick += _ => close();
                if (button.button != null)
                {
                    button.button.onClick = new Button.ButtonClickedEvent();
                }
            }
            return window;
        }

        internal static void HideInventory(UITankWindow window)
        {
            window.transform.Find("produce").gameObject.SetActive(false);
            window.menuButton.gameObject.SetActive(false);
        }

        internal static GameObject SettingsWindow(
            string name,
            string title,
            Vector2 position,
            Vector2 size,
            Action close
        )
        {
            var window = Window(name, close);
            HideInventory(window);
            window.titleText.text = title;
            var transform = (RectTransform)window.transform;
            transform.anchorMin = transform.anchorMax = transform.pivot = new Vector2(0, 1);
            transform.anchoredPosition = position;
            transform.sizeDelta = size;
            var root = window.gameObject;
            UnityEngine.Object.Destroy(window);
            root.SetActive(false);
            return root;
        }

        internal static Text Label(
            Transform parent,
            float top,
            float height,
            string value,
            int fontSize = 14
        )
        {
            var label = UnityEngine.Object.Instantiate(Template.countLabel, parent, false);
            label.name = "label";
            StripLocalizers(label.gameObject);
            Layout((RectTransform)label.transform, top, height);
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Normal;
            label.resizeTextForBestFit = false;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.text = value;
            label.color = Template.countLabel.color;
            label.gameObject.SetActive(true);
            return label;
        }

        internal static Text Button(
            Transform parent,
            float top,
            string value,
            Action action,
            float inset = 32
        )
        {
            var native = UnityEngine.Object.Instantiate(Template.inputButton, parent, false);
            native.name = "button";
            StripLocalizers(native.gameObject);
            Layout((RectTransform)native.transform, top, 28, inset);
            native.tips.tipTitle = "";
            native.tips.tipText = "";
            native.tips.itemId = 0;
            native.tips.type = UIButton.ItemTipType.None;
            native.eventReceiver = null;
            if (native.transitions.Length > 0)
            {
                native.transitions[0].normalColor = Template.openNormalColor;
                native.transitions[0].mouseoverColor = Template.openMouseOverColor;
                native.transitions[0].pressedColor = Template.openPressColor;
            }
            var button = native.button ?? native.GetComponent<Button>();
            if (button == null)
            {
                button = native.gameObject.AddComponent<Button>();
                button.targetGraphic = native.GetComponent<Graphic>();
                native.button = button;
            }
            button.onClick = new Button.ButtonClickedEvent();
            button.interactable = true;
            native.highlighted = false;
            button.onClick.AddListener(() => action());
            var label = native.GetComponentInChildren<Text>(true);
            if (label == null)
            {
                label = Label(native.transform, 0, 28, value);
            }
            else
            {
                label.transform.SetParent(native.transform, false);
                Layout((RectTransform)label.transform, 0, 28, 10);
            }
            label.alignment = TextAnchor.MiddleCenter;
            var templateLabel = Template.inputButton.GetComponentInChildren<Text>(true);
            label.font = templateLabel.font;
            label.material = templateLabel.material;
            label.fontSize = templateLabel.fontSize;
            label.fontStyle = templateLabel.fontStyle;
            label.resizeTextForBestFit = false;
            label.text = value;
            foreach (var otherLabel in native.GetComponentsInChildren<Text>(true))
            {
                if (otherLabel != label)
                {
                    otherLabel.gameObject.SetActive(false);
                }
            }
            label.gameObject.SetActive(true);
            native.gameObject.SetActive(true);
            foreach (var transition in native.transitions)
            {
                if (transition.target is Text)
                {
                    transition.normalColor = Template.titleText.color;
                    transition.mouseoverColor = Template.titleText.color;
                    transition.pressedColor = Template.titleText.color;
                    transition.disabledColor = Template.countLabel.color;
                    transition.alphaOnly = false;
                }
            }
            native.MReset();
            label.color = Template.titleText.color;
            return label;
        }

        internal static Text Property(Transform parent, float top, string caption)
        {
            var row = UnityEngine.Object.Instantiate(
                Template.countLabel.transform.parent.gameObject,
                parent,
                false
            );
            row.name = "property-" + caption;
            StripLocalizers(row);
            Layout((RectTransform)row.transform, top, 22, 0);
            var label = row.transform.Find("label").GetComponent<Text>();
            var value = row.transform.Find("cnt-text").GetComponent<Text>();
            label.text = caption;
            value.text = "";
            foreach (var text in new[] { label, value })
            {
                text.fontStyle = FontStyle.Normal;
                text.raycastTarget = false;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                Layout((RectTransform)text.transform, 0, 22, 0);
            }
            label.alignment = TextAnchor.MiddleLeft;
            value.alignment = TextAnchor.MiddleRight;
            row.SetActive(true);
            return value;
        }

        internal static InputField Input(
            Transform parent,
            float top,
            string label,
            Action<string> change
        )
        {
            var caption = Label(parent, top, 22, label);
            Layout((RectTransform)caption.transform, top, 22, 0);
            var template = UIRoot.instance.uiGame.beltWindow.iconTagCountInput;
            var input = UnityEngine.Object.Instantiate(template, parent, false);
            input.name = "input";
            StripLocalizers(input.gameObject);
            Layout((RectTransform)input.transform, top + 25, 28, 0);
            input.onValueChanged.RemoveAllListeners();
            input.onEndEdit.RemoveAllListeners();
            input.onEndEdit.AddListener(value => change(value));
            input.contentType = InputField.ContentType.DecimalNumber;
            input.characterLimit = 12;
            input.gameObject.SetActive(true);
            return input;
        }

        internal static void Layout(
            RectTransform transform,
            float top,
            float height,
            float inset = 32
        )
        {
            transform.anchorMin = new Vector2(0, 1);
            transform.anchorMax = new Vector2(1, 1);
            transform.pivot = new Vector2(0.5f, 1);
            transform.offsetMin = new Vector2(inset, -top - height);
            transform.offsetMax = new Vector2(-inset, -top);
            transform.localScale = Vector3.one;
            transform.localRotation = Quaternion.identity;
        }

        internal static void StripLocalizers(GameObject gameObject)
        {
            foreach (var behaviour in gameObject.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour.GetType().Name == "Localizer")
                {
                    UnityEngine.Object.Destroy(behaviour);
                }
            }
        }

        internal static void HideWorldLabels(string owner)
        {
            if (!worldLabels.TryGetValue(owner, out var labels))
            {
                return;
            }
            foreach (var label in labels)
            {
                label.gameObject.SetActive(false);
            }
        }

        internal static void WorldLabel(
            string owner,
            int index,
            Vector3 position,
            string value,
            RectTransform occlusion = null
        )
        {
            if (GameCamera.main == null || UIRoot.instance?.overlayCanvas == null)
            {
                return;
            }
            var screen = GameCamera.main.WorldToScreenPoint(position);
            if (screen.z <= 0)
            {
                return;
            }

            var canvas = UIRoot.instance.overlayCanvas;
            if (
                occlusion != null
                && RectTransformUtility.RectangleContainsScreenPoint(
                    occlusion,
                    screen,
                    canvas.worldCamera
                )
            )
            {
                return;
            }
            if (!worldLabels.TryGetValue(owner, out var labels))
            {
                labels = new List<Text>();
                worldLabels.Add(owner, labels);
            }
            while (labels.Count <= index)
            {
                var label = Label(canvas.transform, 0, 28, "", 14);
                label.name = owner + ".WorldLabel";
                label.alignment = TextAnchor.MiddleCenter;
                var labelTransform = (RectTransform)label.transform;
                labelTransform.anchorMin = labelTransform.anchorMax = new Vector2(0.5f, 0.5f);
                labelTransform.pivot = new Vector2(0.5f, 0.5f);
                labelTransform.sizeDelta = new Vector2(220, 28);
                labels.Add(label);
            }
            var current = labels[index];
            var camera =
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)canvas.transform,
                screen,
                camera,
                out var point
            );
            ((RectTransform)current.transform).anchoredPosition = point;
            current.text = value;
            current.gameObject.SetActive(true);
        }

        internal static void DisposeWorldLabels()
        {
            foreach (var labels in worldLabels.Values)
            {
                foreach (var label in labels)
                {
                    UnityEngine.Object.Destroy(label.gameObject);
                }
            }
            worldLabels.Clear();
        }
    }
}
