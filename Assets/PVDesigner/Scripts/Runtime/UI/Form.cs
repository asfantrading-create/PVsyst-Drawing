using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace PVDesigner.UI
{
    /// <summary>
    /// Builds labelled, data-bound controls into a vertical container.
    /// Controls read their value through getters, so <see cref="Refresh"/> re-syncs them with the model.
    /// </summary>
    public class Form
    {
        public readonly RectTransform Root;
        private readonly List<Action> _refreshers = new List<Action>();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public float LabelWidth = 150f;

        public Form(RectTransform root)
        {
            Root = root;
        }

        public void Refresh()
        {
            foreach (var r in _refreshers)
            {
                try
                {
                    r();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("UI refresh failed: " + e.Message);
                }
            }
        }

        public void Clear()
        {
            _refreshers.Clear();
            UIFactory.Clear(Root);
        }

        public void OnRefresh(Action a) => _refreshers.Add(a);

        // ------------------------------------------------------------ text

        public Text Header(string text)
        {
            var row = UIFactory.HRow(Root, 34, 0, "Header");
            var t = UIFactory.Label(row.transform, text.ToUpperInvariant(), 13, TextAnchor.LowerLeft, Theme.Accent, FontStyle.Bold);
            UIFactory.Layout(t, flexWidth: 1);
            return t;
        }

        public Text Note(string text, Color? color = null, int size = 13)
        {
            var t = UIFactory.Label(Root, text, size, TextAnchor.UpperLeft, color ?? Theme.Muted);
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var fitter = t.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return t;
        }

        public Text Note(Func<string> text, Color? color = null, int size = 13)
        {
            var t = Note(text(), color, size);
            _refreshers.Add(() => t.text = text());
            return t;
        }

        public Text Value(string label, Func<string> value)
        {
            var row = UIFactory.HRow(Root, 24, 6, label);
            var l = UIFactory.Label(row.transform, label, 14, TextAnchor.MiddleLeft, Theme.Muted);
            UIFactory.Layout(l, LabelWidth + 30, flexWidth: 0);
            var v = UIFactory.Label(row.transform, value(), 14, TextAnchor.MiddleRight, Theme.Text);
            v.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Layout(v, flexWidth: 1);
            _refreshers.Add(() => v.text = value());
            return v;
        }

        public void Separator()
        {
            var img = UIFactory.Panel(Root, "Separator", Theme.Border);
            UIFactory.Layout(img, prefHeight: 1, minHeight: 1);
        }

        public void Space(float h = 8)
        {
            var rt = UIFactory.Rect("Space", Root);
            UIFactory.Layout(rt, prefHeight: h, minHeight: h);
        }

        private HorizontalLayoutGroup LabelledRow(string label, out Text labelText)
        {
            var row = UIFactory.HRow(Root, Theme.RowHeight, 4, label);
            labelText = UIFactory.Label(row.transform, label, 14, TextAnchor.MiddleLeft, Theme.Text);
            UIFactory.Layout(labelText, LabelWidth, flexWidth: 0, minWidth: LabelWidth);
            return row;
        }

        // ------------------------------------------------------------ inputs

        /// <summary>Numeric field with -/+ steppers (VR friendly) and an optional unit.</summary>
        public InputField Number(string label, Func<double> get, Action<double> set, string unit = "",
            double step = 1, double min = double.MinValue, double max = double.MaxValue, string format = "0.##")
        {
            var row = LabelledRow(label, out _);
            InputField input = null;

            void Commit(double v)
            {
                v = Math.Max(min, Math.Min(max, v));
                set(v);
                if (input != null) input.text = get().ToString(format, Inv);
            }

            var minus = UIFactory.Button(row.transform, "-", () => Commit(get() - step));
            UIFactory.Layout(minus, 26, flexWidth: 0, minWidth: 26);
            input = UIFactory.Input(row.transform, get().ToString(format, Inv), s =>
            {
                if (double.TryParse(s.Replace(',', '.'), NumberStyles.Float, Inv, out double v)) Commit(v);
                else input.text = get().ToString(format, Inv);
            }, InputField.ContentType.Standard);
            UIFactory.Layout(input, 80, flexWidth: 1, minWidth: 60);
            var plus = UIFactory.Button(row.transform, "+", () => Commit(get() + step));
            UIFactory.Layout(plus, 26, flexWidth: 0, minWidth: 26);
            var u = UIFactory.Label(row.transform, unit, 13, TextAnchor.MiddleLeft, Theme.Muted);
            u.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Layout(u, 46, flexWidth: 0, minWidth: 46);

            _refreshers.Add(() =>
            {
                if (!input.isFocused) input.text = get().ToString(format, Inv);
            });
            return input;
        }

        public InputField Integer(string label, Func<int> get, Action<int> set, string unit = "", int min = int.MinValue, int max = int.MaxValue, int step = 1) =>
            Number(label, () => get(), v => set((int)Math.Round(v)), unit, step, min, max, "0");

        /// <summary>Percent field: the model stores a fraction, the UI shows percent.</summary>
        public InputField Percent(string label, Func<double> getFraction, Action<double> setFraction, double stepPercent = 0.1, double minPercent = -100, double maxPercent = 100) =>
            Number(label, () => getFraction() * 100.0, v => setFraction(v / 100.0), "%", stepPercent, minPercent, maxPercent, "0.##");

        public InputField TextField(string label, Func<string> get, Action<string> set)
        {
            var row = LabelledRow(label, out _);
            var input = UIFactory.Input(row.transform, get() ?? "", s => set(s), InputField.ContentType.Standard, TextAnchor.MiddleLeft);
            UIFactory.Layout(input, 120, flexWidth: 1);
            _refreshers.Add(() =>
            {
                if (!input.isFocused) input.text = get() ?? "";
            });
            return input;
        }

        /// <summary>Option selector with previous / next buttons (works without dropdown templates and in VR).</summary>
        public Text Choice(string label, Func<IList<string>> options, Func<int> get, Action<int> set)
        {
            var row = LabelledRow(label, out _);
            Text value = null;

            void Step(int d)
            {
                var opts = options();
                if (opts == null || opts.Count == 0) return;
                int i = ((get() + d) % opts.Count + opts.Count) % opts.Count;
                set(i);
                Update();
            }

            void Update()
            {
                var opts = options();
                int i = get();
                value.text = opts != null && i >= 0 && i < opts.Count ? opts[i] : "-";
            }

            var prev = UIFactory.Button(row.transform, "<", () => Step(-1));
            UIFactory.Layout(prev, 26, flexWidth: 0, minWidth: 26);
            var bg = UIFactory.Panel(row.transform, "Value", Theme.Input);
            UIFactory.Layout(bg, 120, flexWidth: 1);
            value = UIFactory.Label(bg.transform, "", 14, TextAnchor.MiddleCenter, Theme.Text);
            value.horizontalOverflow = HorizontalWrapMode.Wrap;
            value.resizeTextForBestFit = true;
            value.resizeTextMinSize = 10;
            value.resizeTextMaxSize = 14;
            UIFactory.Stretch(value.rectTransform, 4, 0, 4, 0);
            var next = UIFactory.Button(row.transform, ">", () => Step(1));
            UIFactory.Layout(next, 26, flexWidth: 0, minWidth: 26);
            Update();
            _refreshers.Add(Update);
            return value;
        }

        public Text Choice<TEnum>(string label, Func<TEnum> get, Action<TEnum> set, params string[] names) where TEnum : struct, Enum
        {
            var values = (TEnum[])Enum.GetValues(typeof(TEnum));
            var labels = new List<string>();
            for (int i = 0; i < values.Length; i++) labels.Add(i < names.Length ? names[i] : values[i].ToString());
            return Choice(label, () => labels, () => Array.IndexOf(values, get()), i => set(values[i]));
        }

        public void Toggle(string label, Func<bool> get, Action<bool> set)
        {
            var row = LabelledRow(label, out _);
            var box = UIFactory.Panel(row.transform, "Toggle", Theme.Input);
            UIFactory.Layout(box, 46, flexWidth: 0, minWidth: 46);
            var mark = UIFactory.Label(box.transform, "", 13, TextAnchor.MiddleCenter, Theme.AccentText, FontStyle.Bold);
            UIFactory.Stretch(mark.rectTransform);

            void Update()
            {
                bool on = get();
                box.color = on ? Theme.Accent : Theme.Input;
                mark.text = on ? "ON" : "OFF";
                mark.color = on ? Theme.AccentText : Theme.Muted;
            }

            UIFactory.AddClick(box, () =>
            {
                set(!get());
                Update();
            });
            var spacer = UIFactory.Rect("Spacer", row.transform);
            UIFactory.Layout(spacer, flexWidth: 1);
            Update();
            _refreshers.Add(Update);
        }

        public Button Button(string text, Action onClick, bool primary = false)
        {
            var row = UIFactory.HRow(Root, 34, 6, text);
            var b = UIFactory.Button(row.transform, text, onClick, primary ? Theme.Accent : Theme.Button, primary ? Theme.AccentText : Theme.Text);
            UIFactory.Layout(b, flexWidth: 1);
            return b;
        }

        public void Buttons(params (string text, Action action)[] buttons)
        {
            var row = UIFactory.HRow(Root, 32, 6, "Buttons");
            foreach (var (text, action) in buttons)
            {
                var b = UIFactory.Button(row.transform, text, action);
                UIFactory.Layout(b, flexWidth: 1);
            }
        }

        /// <summary>Simple table with fixed column widths (first column flexible).</summary>
        public void Table(string[] headers, Func<List<string[]>> rows, float[] widths = null)
        {
            var col = UIFactory.VColumn(Root, 1, null, "Table");
            UIFactory.Layout(col, flexWidth: 1);
            void Build()
            {
                UIFactory.Clear(col.transform);
                AddRow(col.transform, headers, widths, true);
                foreach (var r in rows()) AddRow(col.transform, r, widths, false);
            }
            Build();
            _refreshers.Add(Build);
        }

        private static void AddRow(Transform parent, string[] cells, float[] widths, bool header)
        {
            var bg = UIFactory.Panel(parent, "Row", header ? Theme.Header : Theme.PanelAlt);
            var h = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.padding = new RectOffset(4, 4, 0, 0);
            h.spacing = 2;
            UIFactory.Layout(bg, prefHeight: 22, minHeight: 22);
            for (int i = 0; i < cells.Length; i++)
            {
                var t = UIFactory.Label(bg.transform, cells[i], 12, i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight,
                    header ? Theme.Accent : Theme.Text, header ? FontStyle.Bold : FontStyle.Normal);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                float w = widths != null && i < widths.Length ? widths[i] : 50;
                if (i == 0) UIFactory.Layout(t, w, flexWidth: 1, minWidth: w);
                else UIFactory.Layout(t, w, flexWidth: 0, minWidth: w);
            }
        }
    }
}
