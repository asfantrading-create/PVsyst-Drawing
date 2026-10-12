using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PVDesigner.UI
{
    /// <summary>Modal message box rendered on the main canvas.</summary>
    public class Dialog
    {
        private readonly RectTransform _canvas;
        private GameObject _current;

        public Dialog(RectTransform canvas)
        {
            _canvas = canvas;
        }

        public bool IsOpen => _current != null;

        public void Close()
        {
            if (_current != null) UnityEngine.Object.Destroy(_current);
            _current = null;
        }

        public void Show(string title, string message, params (string label, Action action)[] buttons)
        {
            Close();
            var overlay = UIFactory.Panel(_canvas, "Dialog", new Color(0, 0, 0, 0.55f));
            UIFactory.Stretch(overlay.rectTransform);
            _current = overlay.gameObject;

            var box = UIFactory.Panel(overlay.transform, "Box", Theme.Panel);
            UIFactory.Anchor(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 380));
            var outline = box.gameObject.AddComponent<Outline>();
            outline.effectColor = Theme.Border;
            outline.effectDistance = new Vector2(1, -1);

            var head = UIFactory.Panel(box.transform, "Title", Theme.Header);
            head.rectTransform.anchorMin = new Vector2(0, 1);
            head.rectTransform.anchorMax = new Vector2(1, 1);
            head.rectTransform.pivot = new Vector2(0.5f, 1);
            head.rectTransform.sizeDelta = new Vector2(0, 44);
            var tt = UIFactory.Label(head.transform, title, 17, TextAnchor.MiddleLeft, Theme.Accent, FontStyle.Bold);
            UIFactory.Stretch(tt.rectTransform, 16, 0, 16, 0);

            var scroll = UIFactory.ScrollView(box.transform, out var content, 4, 14);
            UIFactory.Stretch((RectTransform)scroll.transform, 0, 44, 0, 56);
            var msg = UIFactory.Label(content, message, 15, TextAnchor.UpperLeft, Theme.Text);
            msg.verticalOverflow = VerticalWrapMode.Overflow;
            msg.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var row = UIFactory.HRow(box.transform, 0, 10, "Buttons");
            var rrt = (RectTransform)row.transform;
            rrt.anchorMin = new Vector2(0, 0);
            rrt.anchorMax = new Vector2(1, 0);
            rrt.pivot = new Vector2(0.5f, 0);
            rrt.sizeDelta = new Vector2(-28, 38);
            rrt.anchoredPosition = new Vector2(0, 10);
            row.childAlignment = TextAnchor.MiddleRight;
            var spacer = UIFactory.Rect("Spacer", row.transform);
            UIFactory.Layout(spacer, flexWidth: 1);
            if (buttons == null || buttons.Length == 0) buttons = new (string, Action)[] { ("OK", null) };
            for (int i = 0; i < buttons.Length; i++)
            {
                var (label, action) = buttons[i];
                bool primary = i == 0;
                var b = UIFactory.Button(row.transform, label, () =>
                {
                    Close();
                    action?.Invoke();
                }, primary ? Theme.Accent : Theme.Button, primary ? Theme.AccentText : Theme.Text);
                UIFactory.Layout(b, 140, flexWidth: 0, minWidth: 110);
            }
        }
    }

    /// <summary>Vertical bar chart built from UI images (monthly values).</summary>
    public class BarChart
    {
        private readonly RectTransform _root;
        private readonly RectTransform _bars;
        private readonly Text _title;

        public BarChart(Transform parent, float height, string title)
        {
            var bg = UIFactory.Panel(parent, "Chart", Theme.PanelAlt);
            _root = bg.rectTransform;
            UIFactory.Layout(bg, prefHeight: height, minHeight: height);
            _title = UIFactory.Label(_root, title, 12, TextAnchor.UpperLeft, Theme.Muted);
            UIFactory.Stretch(_title.rectTransform, 8, 4, 8, height - 20);
            _bars = UIFactory.Rect("Bars", _root);
            UIFactory.Stretch(_bars, 8, 22, 8, 18);
        }

        public void SetData(IList<double> values, IList<string> labels, Color color, string format = "0")
        {
            UIFactory.Clear(_bars);
            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                var c = _root.GetChild(i);
                if (c.name == "Axis") UnityEngine.Object.Destroy(c.gameObject);
            }
            if (values == null || values.Count == 0) return;
            double max = 0;
            foreach (var v in values) max = Math.Max(max, v);
            if (max <= 0) max = 1;
            int n = values.Count;
            for (int i = 0; i < n; i++)
            {
                float x0 = i / (float)n, x1 = (i + 1) / (float)n;
                var bar = UIFactory.Panel(_bars, "Bar", color);
                var rt = bar.rectTransform;
                rt.anchorMin = new Vector2(x0 + 0.12f / n, 0);
                rt.anchorMax = new Vector2(x1 - 0.12f / n, (float)(values[i] / max));
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                if (labels != null && i < labels.Count)
                {
                    var l = UIFactory.Label(_root, labels[i], 10, TextAnchor.LowerCenter, Theme.Muted);
                    l.name = "Axis";
                    var lrt = l.rectTransform;
                    lrt.anchorMin = new Vector2(0, 0);
                    lrt.anchorMax = new Vector2(1, 0);
                    lrt.pivot = new Vector2(0.5f, 0);
                    float w = (_root.rect.width > 0 ? _root.rect.width : 380) - 16;
                    lrt.sizeDelta = new Vector2(0, 16);
                    lrt.anchorMin = new Vector2((x0 * w + 8) / (w + 16), 0);
                    lrt.anchorMax = new Vector2((x1 * w + 8) / (w + 16), 0);
                    lrt.offsetMin = new Vector2(0, 1);
                    lrt.offsetMax = new Vector2(0, 17);
                }
            }
            _title.text = _title.text.Split('|')[0].TrimEnd() + "  |  max " + max.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
