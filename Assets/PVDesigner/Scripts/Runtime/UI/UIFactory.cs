using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PVDesigner.UI
{
    public static class Theme
    {
        public static readonly Color Background = Hex(0x161b22);
        public static readonly Color Panel = Hex(0x1f2630);
        public static readonly Color PanelAlt = Hex(0x262f3b);
        public static readonly Color Header = Hex(0x2b3542);
        public static readonly Color Border = Hex(0x3a4656);
        public static readonly Color Button = Hex(0x334050);
        public static readonly Color ButtonHover = Hex(0x415165);
        public static readonly Color Accent = Hex(0xf2a900);
        public static readonly Color AccentText = Hex(0x1b1300);
        public static readonly Color Blue = Hex(0x2f8fff);
        public static readonly Color Input = Hex(0x10151b);
        public static readonly Color Text = Hex(0xe6edf3);
        public static readonly Color Muted = Hex(0x9aa7b4);
        public static readonly Color Good = Hex(0x3fb950);
        public static readonly Color Warning = Hex(0xe3b341);
        public static readonly Color Error = Hex(0xf85149);

        public const int FontSize = 15;
        public const float RowHeight = 30f;

        public static Color Hex(int rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, a);
    }

    /// <summary>Code-only uGUI construction helpers (no prefabs, no editor setup required).</summary>
    public static class UIFactory
    {
        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static void Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Anchors a rect to an edge or corner with a fixed size.</summary>
        public static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        public static Image Panel(Transform parent, string name, Color color)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(Transform parent, string text, int size = Theme.FontSize, TextAnchor align = TextAnchor.MiddleLeft,
            Color? color = null, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect("Label", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color ?? Theme.Text;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        public static LayoutElement Layout(Component c, float prefWidth = -1, float prefHeight = -1, float flexWidth = -1, float flexHeight = -1, float minWidth = -1, float minHeight = -1)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = prefWidth;
            le.preferredHeight = prefHeight;
            le.flexibleWidth = flexWidth;
            le.flexibleHeight = flexHeight;
            le.minWidth = minWidth;
            le.minHeight = minHeight;
            return le;
        }

        public static HorizontalLayoutGroup HRow(Transform parent, float height, float spacing = 6, string name = "Row")
        {
            var rt = Rect(name, parent);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            if (height > 0) Layout(rt, prefHeight: height, minHeight: height);
            return h;
        }

        public static VerticalLayoutGroup VColumn(Transform parent, float spacing = 6, RectOffset padding = null, string name = "Column")
        {
            var rt = Rect(name, parent);
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset(0, 0, 0, 0);
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static Button Button(Transform parent, string label, Action onClick, Color? color = null, Color? textColor = null, int fontSize = Theme.FontSize)
        {
            var img = Panel(parent, "Button " + label, color ?? Theme.Button);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
            colors.colorMultiplier = 1.2f;
            b.colors = colors;
            b.targetGraphic = img;
            var t = Label(img.transform, label, fontSize, TextAnchor.MiddleCenter, textColor ?? Theme.Text);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(t.rectTransform, 6, 0, 6, 0);
            if (onClick != null)
                b.onClick.AddListener(() =>
                {
                    ReleaseSelection();
                    onClick();
                });
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;
            return b;
        }

        public static void SetButtonText(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        public static InputField Input(Transform parent, string value, Action<string> onEndEdit,
            InputField.ContentType type = InputField.ContentType.Standard, TextAnchor align = TextAnchor.MiddleRight)
        {
            var img = Panel(parent, "Input", Theme.Input);
            var input = img.gameObject.AddComponent<InputField>();
            var text = Label(img.transform, value, Theme.FontSize, align, Theme.Text);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(text.rectTransform, 6, 2, 6, 2);
            var ph = Label(img.transform, "", Theme.FontSize, align, Theme.Muted, FontStyle.Italic);
            Stretch(ph.rectTransform, 6, 2, 6, 2);
            input.textComponent = text;
            input.placeholder = ph;
            input.contentType = type;
            input.lineType = InputField.LineType.SingleLine;
            input.text = value;
            input.caretColor = Theme.Accent;
            input.selectionColor = new Color(Theme.Blue.r, Theme.Blue.g, Theme.Blue.b, 0.45f);
            var nav = input.navigation;
            nav.mode = Navigation.Mode.None;
            input.navigation = nav;
            if (onEndEdit != null) input.onEndEdit.AddListener(s => onEndEdit(s));
            return input;
        }

        public static Slider Slider(Transform parent, float min, float max, float value, bool whole, Action<float> onChange)
        {
            var root = Rect("Slider", parent);
            var slider = root.gameObject.AddComponent<Slider>();
            var bg = Panel(root, "Background", Theme.Input);
            bg.rectTransform.anchorMin = new Vector2(0, 0.35f);
            bg.rectTransform.anchorMax = new Vector2(1, 0.65f);
            bg.rectTransform.offsetMin = bg.rectTransform.offsetMax = Vector2.zero;

            var fillArea = Rect("Fill Area", root);
            fillArea.anchorMin = new Vector2(0, 0.35f);
            fillArea.anchorMax = new Vector2(1, 0.65f);
            fillArea.offsetMin = new Vector2(4, 0);
            fillArea.offsetMax = new Vector2(-4, 0);
            var fill = Panel(fillArea, "Fill", Theme.Accent);
            fill.rectTransform.sizeDelta = Vector2.zero;

            var handleArea = Rect("Handle Area", root);
            Stretch(handleArea, 8, 0, 8, 0);
            var handle = Panel(handleArea, "Handle", Theme.Text);
            handle.rectTransform.sizeDelta = new Vector2(14, 0);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = whole;
            slider.value = value;
            var nav = slider.navigation;
            nav.mode = Navigation.Mode.None;
            slider.navigation = nav;
            if (onChange != null) slider.onValueChanged.AddListener(v => onChange(v));
            return slider;
        }

        /// <summary>Vertical scroll view; returns the scroll rect, content is a vertical layout.</summary>
        public static ScrollRect ScrollView(Transform parent, out RectTransform content, float spacing = 6, int padding = 10)
        {
            var root = Panel(parent, "Scroll View", new Color(0, 0, 0, 0));
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35f;
            scroll.inertia = true;

            var viewport = Rect("Viewport", root.transform);
            Stretch(viewport, 0, 0, 10, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vimg = viewport.gameObject.AddComponent<Image>();
            vimg.color = new Color(0, 0, 0, 0.001f);

            var col = VColumn(viewport, spacing, new RectOffset(padding, padding, padding, padding), "Content");
            content = (RectTransform)col.transform;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sbImg = Panel(root.transform, "Scrollbar", Theme.Background);
            var sbRt = sbImg.rectTransform;
            sbRt.anchorMin = new Vector2(1, 0);
            sbRt.anchorMax = new Vector2(1, 1);
            sbRt.pivot = new Vector2(1, 0.5f);
            sbRt.sizeDelta = new Vector2(8, 0);
            sbRt.anchoredPosition = Vector2.zero;
            var sb = sbImg.gameObject.AddComponent<Scrollbar>();
            var area = Rect("Sliding Area", sbRt);
            Stretch(area);
            var handle = Panel(area, "Handle", Theme.Border);
            handle.rectTransform.sizeDelta = Vector2.zero;
            sb.handleRect = handle.rectTransform;
            sb.targetGraphic = handle;
            sb.direction = Scrollbar.Direction.BottomToTop;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                c.SetActive(false);
                UnityEngine.Object.Destroy(c);
            }
        }

        public static void AddClick(Graphic g, UnityAction action)
        {
            var b = g.gameObject.GetComponent<Button>() ?? g.gameObject.AddComponent<Button>();
            b.targetGraphic = g;
            b.onClick.AddListener(() =>
            {
                ReleaseSelection();
                action();
            });
        }

        /// <summary>
        /// Clears the uGUI selection after a click so keyboard shortcuts (Space, Enter) cannot re-trigger the button.
        /// </summary>
        public static void ReleaseSelection()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<InputField>() == null)
                es.SetSelectedGameObject(null);
        }
    }
}
