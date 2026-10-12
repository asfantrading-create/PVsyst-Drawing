using System.Collections.Generic;
using PVDesigner.App;
using PVDesigner.SceneView;
using PVDesigner.Tools;
using PVDesigner.UI.Panels;
using UnityEngine;
using UnityEngine.UI;

namespace PVDesigner.UI
{
    /// <summary>Application window: top bar, navigation, side panel, viewport tools and the time bar.</summary>
    public class MainWindow : MonoBehaviour
    {
        public const float NavWidth = 118f;
        public const float PanelWidth = 440f;
        public const float TopHeight = 46f;
        public const float BottomHeight = 46f;

        public Canvas Canvas { get; private set; }
        public RectTransform Root { get; private set; }
        public Dialog Dialog { get; private set; }

        private PVDesignerApp _app;
        private CanvasScaler _scaler;
        private readonly List<PanelBase> _panels = new List<PanelBase>();
        private readonly Dictionary<string, Image> _navButtons = new Dictionary<string, Image>();
        private readonly Dictionary<ToolMode, Image> _toolButtons = new Dictionary<ToolMode, Image>();
        private PanelBase _current;
        private RectTransform _panelHost;
        private Text _projectTitle, _status, _sunInfo, _dateLabel, _hourLabel, _jobLabel;
        private Image _jobFill;
        private GameObject _jobBox;
        private Slider _day, _hour;
        private Button _play, _colorMode, _vrButton, _updateButton;
        private bool _syncingTime;
        private float _statusTimer;

        public void Initialize(PVDesignerApp app)
        {
            _app = app;
            var go = new GameObject("UI Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            _scaler = go.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920, 1080) / Mathf.Max(0.5f, AppSettings.UiScale);
            _scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            Root = (RectTransform)go.transform;
            Dialog = new Dialog(Root);

            BuildTopBar();
            BuildNav();
            BuildBottomBar();
            BuildViewportTools();

            AddPanel(new ProjectPanel());
            AddPanel(new SitePanel());
            AddPanel(new MeteoPanel());
            AddPanel(new ComponentsPanel());
            AddPanel(new LayoutPanel());
            AddPanel(new ObstaclesPanel());
            AddPanel(new ElectricalPanel());
            AddPanel(new ShadingPanel());
            AddPanel(new LossesPanel());
            AddPanel(new ResultsPanel());
            AddPanel(new EconomicsPanel());
            AddPanel(new SettingsPanel());

            app.Changed += OnChanged;
            app.ResultsChanged += OnResults;
            app.StatusChanged += OnStatus;
            app.Sun.SunChanged += _ => SyncTime();
            app.Tools.ModeChanged += _ => RefreshToolButtons();
            app.Jobs.JobStarted += _ => _jobBox.SetActive(true);
            app.Jobs.JobFinished += _ => _jobBox.SetActive(false);
            ShowTab("Site");
        }

        public void ApplyUiScale()
        {
            _scaler.referenceResolution = new Vector2(1920, 1080) / Mathf.Max(0.5f, AppSettings.UiScale);
        }

        // ------------------------------------------------------------ layout

        private void BuildTopBar()
        {
            var bar = UIFactory.Panel(Root, "Top bar", Theme.Background);
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, TopHeight);
            var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(12, 12, 7, 7);
            h.spacing = 6;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;

            var logo = UIFactory.Label(bar.transform, "<b><color=#f2a900>PV</color> Designer</b>", 20, TextAnchor.MiddleLeft);
            logo.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Layout(logo, 130, flexWidth: 0);
            _projectTitle = UIFactory.Label(bar.transform, "", 15, TextAnchor.MiddleLeft, Theme.Muted);
            _projectTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Layout(_projectTitle, 260, flexWidth: 1);

            TopButton(bar.transform, "New", () => ConfirmDiscard(_app.NewProject));
            TopButton(bar.transform, "Open", () => ShowTab("Project"));
            TopButton(bar.transform, "Save", () =>
            {
                if (_app.SaveProject()) RefreshTitle();
            });
            TopButton(bar.transform, "Undo", _app.Undo);
            TopButton(bar.transform, "Redo", _app.Redo);
            TopButton(bar.transform, "Simulate", _app.RunSimulation, Theme.Accent, Theme.AccentText);
            TopButton(bar.transform, "Report", _app.ExportReport);
            _vrButton = TopButton(bar.transform, "Enter VR", () => _app.VR.Toggle(), Theme.Blue);
            _updateButton = TopButton(bar.transform, "Update", () => _app.Updater.ShowUpdateDialog(), Theme.Good, Theme.AccentText);
            _updateButton.gameObject.SetActive(false);
        }

        private Button TopButton(Transform parent, string label, System.Action action, Color? color = null, Color? text = null)
        {
            var b = UIFactory.Button(parent, label, action, color, text);
            UIFactory.Layout(b, label.Length * 9 + 26, flexWidth: 0);
            return b;
        }

        private RectTransform _nav;

        private void BuildNav()
        {
            var nav = UIFactory.Panel(Root, "Navigation", Theme.Background);
            var rt = nav.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = new Vector2(0, BottomHeight);
            rt.offsetMax = new Vector2(NavWidth, -TopHeight);
            var v = nav.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(6, 6, 8, 8);
            v.spacing = 3;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandHeight = false;
            _nav = rt;

            var host = UIFactory.Panel(Root, "Panel", Theme.Panel);
            _panelHost = host.rectTransform;
            _panelHost.anchorMin = new Vector2(0, 0);
            _panelHost.anchorMax = new Vector2(0, 1);
            _panelHost.pivot = new Vector2(0, 0.5f);
            _panelHost.offsetMin = new Vector2(NavWidth, BottomHeight);
            _panelHost.offsetMax = new Vector2(NavWidth + PanelWidth, -TopHeight);
        }

        private void BuildBottomBar()
        {
            var bar = UIFactory.Panel(Root, "Bottom bar", Theme.Background);
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(0, BottomHeight);
            var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(12, 12, 8, 8);
            h.spacing = 8;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;

            _play = UIFactory.Button(bar.transform, "Play", () =>
            {
                _app.Sun.Animate = !_app.Sun.Animate;
            });
            UIFactory.Layout(_play, 64, flexWidth: 0);

            _dateLabel = UIFactory.Label(bar.transform, "", 14, TextAnchor.MiddleRight);
            UIFactory.Layout(_dateLabel, 60, flexWidth: 0);
            _day = UIFactory.Slider(bar.transform, 1, 365, _app.Sun.DayOfYear, true, v =>
            {
                if (!_syncingTime) _app.Sun.SetDayAndHour((int)v, _app.Sun.Hour);
            });
            UIFactory.Layout(_day, 260, flexWidth: 0);

            _hourLabel = UIFactory.Label(bar.transform, "", 14, TextAnchor.MiddleRight);
            UIFactory.Layout(_hourLabel, 50, flexWidth: 0);
            _hour = UIFactory.Slider(bar.transform, 0, 23.75f, _app.Sun.Hour, false, v =>
            {
                if (!_syncingTime) _app.Sun.SetDayAndHour(_app.Sun.DayOfYear, Mathf.Round(v * 4f) / 4f);
            });
            UIFactory.Layout(_hour, 260, flexWidth: 0);

            foreach (var (label, day) in new[] { ("21 Mar", 80), ("21 Jun", 172), ("21 Dec", 355) })
            {
                var d = day;
                var b = UIFactory.Button(bar.transform, label, () => _app.Sun.SetDayAndHour(d, _app.Sun.Hour), null, null, 12);
                UIFactory.Layout(b, 58, flexWidth: 0);
            }

            _sunInfo = UIFactory.Label(bar.transform, "", 13, TextAnchor.MiddleLeft, Theme.Muted);
            _sunInfo.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Layout(_sunInfo, 200, flexWidth: 0);

            _jobBox = UIFactory.Rect("Job", bar.transform).gameObject;
            UIFactory.Layout(_jobBox.GetComponent<RectTransform>(), 200, flexWidth: 0);
            var jbBg = UIFactory.Panel(_jobBox.transform, "Bar", Theme.Input);
            UIFactory.Stretch(jbBg.rectTransform, 0, 4, 0, 4);
            _jobFill = UIFactory.Panel(jbBg.transform, "Fill", Theme.Blue);
            _jobFill.rectTransform.anchorMin = Vector2.zero;
            _jobFill.rectTransform.anchorMax = new Vector2(0, 1);
            _jobFill.rectTransform.offsetMin = _jobFill.rectTransform.offsetMax = Vector2.zero;
            _jobLabel = UIFactory.Label(jbBg.transform, "", 12, TextAnchor.MiddleCenter);
            UIFactory.Stretch(_jobLabel.rectTransform);
            UIFactory.AddClick(jbBg, () => _app.Jobs.Cancel());
            _jobBox.SetActive(false);

            _status = UIFactory.Label(bar.transform, "", 13, TextAnchor.MiddleLeft, Theme.Text);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            _status.resizeTextForBestFit = true;
            _status.resizeTextMinSize = 10;
            _status.resizeTextMaxSize = 13;
            UIFactory.Layout(_status, 200, flexWidth: 1);
            SyncTime();
        }

        private void BuildViewportTools()
        {
            var bar = UIFactory.Panel(Root, "Viewport tools", new Color(0.09f, 0.11f, 0.14f, 0.88f));
            var rt = bar.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(NavWidth + PanelWidth + 12, -TopHeight - 10);
            var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(6, 6, 5, 5);
            h.spacing = 4;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            var fit = bar.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ToolButton(bar.transform, "Select", ToolMode.Select);
            ToolButton(bar.transform, "Site boundary", ToolMode.DrawSiteBoundary);
            ToolButton(bar.transform, "Building", ToolMode.PlaceBuilding);
            ToolButton(bar.transform, "Tree", ToolMode.PlaceTree);
            ToolButton(bar.transform, "Measure", ToolMode.Measure);
            Spacer(bar.transform, 10);
            SmallButton(bar.transform, "Top", () => _app.FrameSite(true));
            SmallButton(bar.transform, "3D", () => _app.FrameSite(false));
            _colorMode = SmallButton(bar.transform, "Colour: realistic", CycleColorMode);
            UIFactory.Layout(_colorMode, 150, prefHeight: 30);
            RefreshToolButtons();
        }

        private void Spacer(Transform parent, float w)
        {
            var s = UIFactory.Rect("Spacer", parent);
            UIFactory.Layout(s, w, prefHeight: 30);
        }

        private Button SmallButton(Transform parent, string label, System.Action action)
        {
            var b = UIFactory.Button(parent, label, action, null, null, 13);
            UIFactory.Layout(b, label.Length * 8 + 22, prefHeight: 30);
            return b;
        }

        private void ToolButton(Transform parent, string label, ToolMode mode)
        {
            var b = SmallButton(parent, label, () => _app.Tools.SetMode(mode));
            _toolButtons[mode] = (Image)b.targetGraphic;
        }

        private void RefreshToolButtons()
        {
            foreach (var kv in _toolButtons)
                kv.Value.color = _app.Tools != null && _app.Tools.Mode == kv.Key ? Theme.Accent * 0.85f : Theme.Button;
        }

        private void CycleColorMode()
        {
            var next = (ColorMode)(((int)_app.ColorMode + 1) % 3);
            _app.SetColorMode(next);
            string[] names = { "Colour: realistic", "Colour: inverters", "Colour: shading" };
            UIFactory.SetButtonText(_colorMode, names[(int)next]);
            if (next == ColorMode.ShadingHeatmap) _app.Status("Shading heat map at the current date and time (move the time sliders).");
        }

        // ------------------------------------------------------------ panels

        private void AddPanel(PanelBase panel)
        {
            var host = UIFactory.Rect(panel.Title, _panelHost);
            UIFactory.Stretch(host);
            var head = UIFactory.Panel(host, "Header", Theme.Header);
            head.rectTransform.anchorMin = new Vector2(0, 1);
            head.rectTransform.anchorMax = new Vector2(1, 1);
            head.rectTransform.pivot = new Vector2(0.5f, 1);
            head.rectTransform.sizeDelta = new Vector2(0, 40);
            var title = UIFactory.Label(head.transform, panel.Title, 18, TextAnchor.MiddleLeft, Theme.Text, FontStyle.Bold);
            UIFactory.Stretch(title.rectTransform, 14, 0, 10, 0);
            var scroll = UIFactory.ScrollView(host, out var content, 5, 12);
            UIFactory.Stretch((RectTransform)scroll.transform, 0, 40, 0, 0);
            panel.Host = host;
            panel.Attach(_app, this, content);
            host.gameObject.SetActive(false);
            _panels.Add(panel);

            var nav = UIFactory.Button(_nav, panel.Title, () => ShowTab(panel.Title), Theme.Background, Theme.Text, 14);
            UIFactory.Layout(nav, prefHeight: 34, minHeight: 34);
            var t = nav.GetComponentInChildren<Text>();
            t.alignment = TextAnchor.MiddleLeft;
            _navButtons[panel.Title] = (Image)nav.targetGraphic;
        }

        public void ShowTab(string title)
        {
            foreach (var p in _panels)
            {
                bool on = p.Title == title;
                p.Host.gameObject.SetActive(on);
                if (on)
                {
                    _current = p;
                    p.OnShow();
                }
            }
            foreach (var kv in _navButtons) kv.Value.color = kv.Key == title ? Theme.PanelAlt : Theme.Background;
            foreach (var kv in _navButtons)
            {
                var txt = kv.Value.GetComponentInChildren<Text>();
                txt.color = kv.Key == title ? Theme.Accent : Theme.Text;
            }
        }

        // ------------------------------------------------------------ events

        private void OnChanged(ChangeKind kind)
        {
            RefreshTitle();
            foreach (var p in _panels) p.OnChanged(kind);
        }

        private void OnResults()
        {
            foreach (var p in _panels) p.OnResults();
        }

        private void OnStatus(string msg, bool error)
        {
            _status.text = msg;
            _status.color = error ? Theme.Error : Theme.Text;
            _statusTimer = 0;
        }

        private void RefreshTitle()
        {
            if (_app.Project == null) return;
            _projectTitle.text = _app.Project.Info.Name + (_app.IsDirty ? "  *" : "") +
                                 (string.IsNullOrEmpty(_app.ProjectPath) ? "  (not saved)" : "");
        }

        private void SyncTime()
        {
            if (_day == null) return;
            _syncingTime = true;
            _day.value = _app.Sun.DayOfYear;
            _hour.value = _app.Sun.Hour;
            _syncingTime = false;
            _dateLabel.text = SunController.FormatDate(_app.Sun.DayOfYear);
            _hourLabel.text = SunController.FormatHour(_app.Sun.Hour);
            _sunInfo.text = _app.Sun.Describe();
        }

        public void ConfirmDiscard(System.Action action)
        {
            if (!_app.IsDirty)
            {
                action();
                return;
            }
            Dialog.Show("Unsaved changes", "The current project has unsaved changes.",
                ("Save first", () =>
                {
                    if (_app.SaveProject()) action();
                }),
                ("Discard", action),
                ("Cancel", null));
        }

        public void SetUpdateAvailable(string version)
        {
            _updateButton.gameObject.SetActive(!string.IsNullOrEmpty(version));
            if (!string.IsNullOrEmpty(version)) UIFactory.SetButtonText(_updateButton, "Update " + version);
        }

        public void SetVrState(bool active)
        {
            UIFactory.SetButtonText(_vrButton, active ? "Exit VR" : "Enter VR");
        }

        private bool _shownAnimate;

        private void Update()
        {
            // Animation can also be toggled with Space or a VR controller button.
            if (_shownAnimate != _app.Sun.Animate)
            {
                _shownAnimate = _app.Sun.Animate;
                UIFactory.SetButtonText(_play, _shownAnimate ? "Pause" : "Play");
            }
            if (_app.Jobs.IsBusy)
            {
                float p = _app.Jobs.Progress;
                _jobFill.rectTransform.anchorMax = new Vector2(p, 1);
                _jobLabel.text = $"{_app.Jobs.CurrentJob} {p * 100:0}%  (click to cancel)";
            }
            _statusTimer += Time.unscaledDeltaTime;
        }

        // ------------------------------------------------------------ VR support

        /// <summary>Switches the canvas between screen overlay (desktop) and a world-space panel (VR).</summary>
        public void SetWorldSpace(bool worldSpace, Camera eventCamera)
        {
            if (worldSpace)
            {
                Canvas.renderMode = RenderMode.WorldSpace;
                _scaler.enabled = false;
                Root.sizeDelta = new Vector2(1920, 1080);
                Root.localScale = Vector3.one * 0.0009f;
                Canvas.worldCamera = eventCamera;
            }
            else
            {
                Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Root.SetParent(transform, false);
                Root.localScale = Vector3.one;
                _scaler.enabled = true;
                Canvas.worldCamera = null;
            }
        }
    }
}
