using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Modules.SafeSiteApiService.Testbench
{
    /// <summary>Builds the testbench's uGUI/TextMeshPro controls in code, so the scene stays tiny.</summary>
    internal static class Ui
    {
        public static readonly Color Background = new Color(0.10f, 0.11f, 0.13f, 0.97f);
        public static readonly Color Panel = new Color(0.15f, 0.16f, 0.19f, 1f);
        public static readonly Color Field = new Color(0.22f, 0.23f, 0.27f, 1f);
        public static readonly Color Accent = new Color(0.20f, 0.45f, 0.85f, 1f);
        public static readonly Color Text = new Color(0.92f, 0.93f, 0.95f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.65f, 0.70f, 1f);
        public static readonly Color Good = new Color(0.35f, 0.82f, 0.45f, 1f);
        public static readonly Color Warn = new Color(0.97f, 0.72f, 0.22f, 1f);
        public static readonly Color Bad = new Color(0.96f, 0.38f, 0.38f, 1f);

        public const float RowHeight = 34f;
        public const float FontSize = 17f;
        public const float LabelWidth = 170f;

        private static TMP_DefaultControls.Resources Resources => new TMP_DefaultControls.Resources();

        public static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

        public static string Colored(string text, Color color) => $"<color={Hex(color)}>{text}</color>";

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static Image Fill(Component target, Color color)
        {
            if (!target.TryGetComponent(out Image image))
                image = target.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static LayoutElement Layout(Component target, float minHeight = -1, float preferredWidth = -1, float flexibleWidth = -1, float flexibleHeight = -1, float preferredHeight = -1)
        {
            if (!target.TryGetComponent(out LayoutElement element))
                element = target.gameObject.AddComponent<LayoutElement>();
            element.minHeight = minHeight;
            element.preferredWidth = preferredWidth;
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
            element.preferredHeight = preferredHeight;
            return element;
        }

        public static RectTransform Column(Transform parent, float spacing = 6f, int padding = 0, string name = "Column")
        {
            var rect = Rect(name, parent);
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = new RectOffset(padding, padding, padding, padding);
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return rect;
        }

        public static RectTransform Row(Transform parent, float spacing = 6f, string name = "Row")
        {
            var rect = Rect(name, parent);
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            Layout(rect, minHeight: RowHeight);
            return rect;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size = FontSize, Color? color = null, float width = -1f, FontStyles style = FontStyles.Normal)
        {
            var go = TMP_DefaultControls.CreateText(Resources);
            go.name = "Label";
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color ?? Text;
            label.richText = true;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            Layout(label, minHeight: size + 8, preferredWidth: width, flexibleWidth: width > 0 ? -1 : 1);
            return label;
        }

        public static TextMeshProUGUI Heading(Transform parent, string text) =>
            Label(parent, text, FontSize + 4, Text, style: FontStyles.Bold);

        public static Button Button(Transform parent, string text, Action onClick, float width = -1f, Color? color = null)
        {
            var go = TMP_DefaultControls.CreateButton(Resources);
            go.name = "Button " + text;
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color ?? Field;
            var label = go.GetComponentInChildren<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = FontSize;
            label.color = Text;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            button.colors = colors;
            if (onClick != null)
                button.onClick.AddListener(() => onClick());
            Layout(button, minHeight: RowHeight, preferredWidth: width > 0 ? width : label.GetPreferredValues(text).x + 28f);
            return button;
        }

        public static void SetButtonText(Button button, string text) => button.GetComponentInChildren<TextMeshProUGUI>().text = text;

        public static TMP_InputField Input(Transform parent, string label, string value = "",
            TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard, float labelWidth = LabelWidth)
        {
            var row = Row(parent);
            if (!string.IsNullOrEmpty(label))
                Label(row, label, width: labelWidth, color: Muted);
            return InputField(row, value, contentType);
        }

        public static TMP_InputField InputField(Transform parent, string value = "", TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard, float width = -1f)
        {
            var go = TMP_DefaultControls.CreateInputField(Resources);
            go.name = "Input";
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Field;
            var input = go.GetComponent<TMP_InputField>();
            input.contentType = contentType;
            input.pointSize = FontSize;
            input.textComponent.color = Text;
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.text = "";
                placeholder.color = Muted;
            }

            input.caretColor = Text;
            input.customCaretColor = true;
            input.text = value ?? "";
            Layout(input, minHeight: RowHeight, preferredWidth: width, flexibleWidth: width > 0 ? -1 : 1);
            return input;
        }

        public static TMP_Dropdown Dropdown(Transform parent, string label, IEnumerable<string> options = null, float labelWidth = LabelWidth)
        {
            var row = Row(parent);
            if (!string.IsNullOrEmpty(label))
                Label(row, label, width: labelWidth, color: Muted);
            return DropdownField(row, options);
        }

        public static TMP_Dropdown DropdownField(Transform parent, IEnumerable<string> options = null, float width = -1f)
        {
            var go = TMP_DefaultControls.CreateDropdown(Resources);
            go.name = "Dropdown";
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Field;
            var dropdown = go.GetComponent<TMP_Dropdown>();
            dropdown.captionText.fontSize = FontSize;
            dropdown.captionText.color = Text;
            dropdown.itemText.fontSize = FontSize;
            dropdown.itemText.color = Text;

            var template = dropdown.template;
            template.sizeDelta = new Vector2(template.sizeDelta.x, 320f);
            template.GetComponent<Image>().color = Panel;
            var item = template.Find("Viewport/Content/Item");
            if (item != null)
            {
                ((RectTransform)item).sizeDelta = new Vector2(0, RowHeight);
                var content = (RectTransform)item.parent;
                content.sizeDelta = new Vector2(0, RowHeight + 4);
                var itemBackground = item.Find("Item Background")?.GetComponent<Image>();
                if (itemBackground != null)
                    itemBackground.color = Field;
            }

            SetOptions(dropdown, options);
            Layout(dropdown, minHeight: RowHeight, preferredWidth: width, flexibleWidth: width > 0 ? -1 : 1);
            return dropdown;
        }

        public static void SetOptions(TMP_Dropdown dropdown, IEnumerable<string> options, int selected = 0)
        {
            dropdown.ClearOptions();
            if (options != null)
                dropdown.AddOptions(new List<string>(options));
            if (dropdown.options.Count > 0)
                dropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0, dropdown.options.Count - 1));
            dropdown.RefreshShownValue();
        }

        public static Toggle Toggle(Transform parent, string label, bool value, Action<bool> onChanged = null)
        {
            var row = Row(parent);
            var box = Rect("Toggle", row);
            var boxImage = Fill(box, Field);
            Layout(box, minHeight: 26f, preferredWidth: 26f, preferredHeight: 26f);
            var check = Rect("Check", box);
            Stretch(check);
            check.offsetMin = new Vector2(5, 5);
            check.offsetMax = new Vector2(-5, -5);
            var checkImage = Fill(check, Accent);

            var toggle = box.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = boxImage;
            toggle.graphic = checkImage;
            toggle.isOn = value;
            if (onChanged != null)
                toggle.onValueChanged.AddListener(v => onChanged(v));

            Label(row, label);
            return toggle;
        }

        public static RectTransform ScrollView(Transform parent, out ScrollRect scroll, string name = "Scroll")
        {
            var root = Rect(name, parent);
            Fill(root, Panel);
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var viewport = Rect("Viewport", root);
            Stretch(viewport);
            viewport.offsetMax = new Vector2(-16, 0);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            var group = content.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = 6f;
            group.padding = new RectOffset(12, 12, 10, 10);
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollbarGo = TMP_DefaultControls.CreateScrollbar(Resources);
            scrollbarGo.transform.SetParent(root, false);
            var scrollbarRect = (RectTransform)scrollbarGo.transform;
            scrollbarRect.anchorMin = new Vector2(1, 0);
            scrollbarRect.anchorMax = new Vector2(1, 1);
            scrollbarRect.pivot = new Vector2(1, 0.5f);
            scrollbarRect.sizeDelta = new Vector2(14, 0);
            scrollbarRect.anchoredPosition = Vector2.zero;
            scrollbarGo.GetComponent<Image>().color = Background;
            var scrollbar = scrollbarGo.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            if (scrollbar.handleRect != null)
                scrollbar.handleRect.GetComponent<Image>().color = Field;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            return content;
        }

        public static ProgressBar Progress(Transform parent)
        {
            var root = Rect("Progress", parent);
            Fill(root, Field);
            Layout(root, minHeight: 26f, flexibleWidth: 1);
            var fill = Rect("Fill", root);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0, 1);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            Fill(fill, Accent);
            var textGo = TMP_DefaultControls.CreateText(Resources);
            textGo.transform.SetParent(root, false);
            var text = textGo.GetComponent<TextMeshProUGUI>();
            Stretch(text.rectTransform);
            text.fontSize = FontSize - 2;
            text.color = Text;
            text.alignment = TextAlignmentOptions.Center;
            text.text = "";
            return new ProgressBar(fill, text);
        }

        public static string Bytes(double bytes)
        {
            if (bytes >= 1024 * 1024 * 1024)
                return $"{bytes / (1024 * 1024 * 1024):0.00} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024 * 1024):0.0} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024:0.0} KB";
            return $"{bytes:0} B";
        }

        public static string Mask(string secret) =>
            string.IsNullOrEmpty(secret) ? "(none)" : secret.Length <= 8 ? secret : secret.Substring(0, 8) + "...";
    }

    internal sealed class ProgressBar
    {
        private readonly RectTransform _fill;
        private readonly TextMeshProUGUI _text;

        public ProgressBar(RectTransform fill, TextMeshProUGUI text)
        {
            _fill = fill;
            _text = text;
        }

        public void Set(float fraction, string text)
        {
            _fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1);
            _text.text = text;
        }
    }
}
