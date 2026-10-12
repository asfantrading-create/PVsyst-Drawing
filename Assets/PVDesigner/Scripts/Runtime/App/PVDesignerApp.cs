using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PVDesigner.CameraControl;
using PVDesigner.Core.Components;
using PVDesigner.Core.Economics;
using PVDesigner.Core.Electrical;
using PVDesigner.Core.Layout;
using PVDesigner.Core.Meteo;
using PVDesigner.Core.Model;
using PVDesigner.Core.Reports;
using PVDesigner.Core.Serialization;
using PVDesigner.Core.Shading;
using PVDesigner.Core.Simulation;
using PVDesigner.InputHandling;
using PVDesigner.SceneView;
using PVDesigner.Tools;
using PVDesigner.UI;
using PVDesigner.Updates;
using PVDesigner.VR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

namespace PVDesigner.App
{
    public enum ChangeKind
    {
        ProjectLoaded,
        Info,
        Site,
        Boundary,
        Geometry,
        Obstacles,
        Components,
        Electrical,
        Losses,
        Meteo,
        Economics
    }

    /// <summary>
    /// Application root. Created automatically when any scene loads, so the project does not depend
    /// on scene files: it builds the camera, light, 3D scene, UI, VR rig and updater in code.
    /// </summary>
    public class PVDesignerApp : MonoBehaviour
    {
        public static PVDesignerApp Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null || FindFirstObjectByType<PVDesignerApp>() != null) return;
            var go = new GameObject("PV Designer");
            go.AddComponent<PVDesignerApp>();
        }

        // ------------------------------------------------------------------ state

        public PVProject Project { get; private set; }
        public string ProjectPath { get; private set; } = "";
        public bool IsDirty { get; private set; }
        public ComponentLibrary Library { get; private set; }
        public readonly UndoStack UndoStack = new UndoStack();

        public ShadingResult Shading { get; private set; }
        public bool ShadingUpToDate { get; private set; }
        public SimulationResult Simulation { get; private set; }
        public EconomicResult Economics { get; private set; }

        public int ActiveFieldIndex { get; private set; }
        public ColorMode ColorMode { get; private set; } = ColorMode.Realistic;

        // ------------------------------------------------------------------ services

        public SceneBuilder Scene { get; private set; }
        public SunController Sun { get; private set; }
        public OrbitCamera OrbitCamera { get; private set; }
        public ToolController Tools { get; private set; }
        public MainWindow Window { get; private set; }
        public VRManager VR { get; private set; }
        public AutoUpdater Updater { get; private set; }
        public JobRunner Jobs { get; private set; }

        public event Action<ChangeKind> Changed;
        public event Action ResultsChanged;
        public event Action<string, bool> StatusChanged;

        public string Version => Application.version;

        private SystemSizingResult _sizing;
        /// <summary>Incremented on every project change; background results computed for an older revision are discarded.</summary>
        private int _revision;
        private ShadingScene _liveShadingScene;
        private float _heatmapTimer;
        private bool _heatmapDirty;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            if (!Application.isMobilePlatform) Application.targetFrameRate = 120;
            AppPaths.EnsureCreated();

            Jobs = gameObject.AddComponent<JobRunner>();
            Library = ComponentLibrary.CreateDefault();

            // Remove template scene lights; the app has its own sun.
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) l.gameObject.SetActive(false);

            Scene = new GameObject("Scene").AddComponent<SceneBuilder>();
            Scene.transform.SetParent(transform, false);
            Scene.Initialize(AppSettings.GroundStyle);

            Sun = gameObject.AddComponent<SunController>();
            Sun.Initialize(Scene.World, Scene);
            Sun.SunChanged += OnSunChanged;

            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
            }
            cam.transform.SetParent(null, false);
            DontDestroyOnLoad(cam.gameObject);
            OrbitCamera = cam.gameObject.GetOrAdd<OrbitCamera>();
            OrbitCamera.Initialize();

            EnsureEventSystem();

            Tools = gameObject.AddComponent<ToolController>();
            Tools.Initialize(this);

            Window = gameObject.AddComponent<MainWindow>();
            Window.Initialize(this);

            VR = gameObject.AddComponent<VRManager>();
            VR.Initialize(this);

            Updater = gameObject.AddComponent<AutoUpdater>();
            Updater.Initialize(this);
        }

        private void Start()
        {
            string last = AppSettings.LastProjectPath;
            if (!string.IsNullOrEmpty(last) && File.Exists(last))
            {
                try
                {
                    LoadProjectFile(last);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Could not reopen last project: " + e.Message);
                    NewProject();
                }
            }
            else
            {
                NewProject();
            }

            if (AppSettings.CheckUpdatesOnStartup && !Application.isEditor) Updater.CheckForUpdates(false);
        }

        private void EnsureEventSystem()
        {
            var es = FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                es = new GameObject("EventSystem").AddComponent<EventSystem>();
                DontDestroyOnLoad(es.gameObject);
            }
            if (es.GetComponent<BaseInputModule>() == null) AddDesktopInputModule(es.gameObject);
        }

        public static BaseInputModule AddDesktopInputModule(GameObject go)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return go.GetOrAdd<StandaloneInputModule>();
#elif ENABLE_INPUT_SYSTEM && PVD_INPUT_SYSTEM
            return go.GetOrAdd<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            return go.GetOrAdd<StandaloneInputModule>();
#endif
        }

        private void Update()
        {
            HandleShortcuts();
            if (_heatmapDirty && ColorMode == ColorMode.ShadingHeatmap)
            {
                _heatmapTimer -= Time.unscaledDeltaTime;
                if (_heatmapTimer <= 0f)
                {
                    _heatmapDirty = false;
                    _heatmapTimer = 0.12f;
                    ApplyColors();
                }
            }
        }

        private void HandleShortcuts()
        {
            var es = EventSystem.current;
            bool typing = es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null;
            if (typing) return;
            bool ctrl = InputAdapter.KeyHeld(InputAdapter.Key.Ctrl);
            if (ctrl && InputAdapter.KeyDown(InputAdapter.Key.Z)) Undo();
            else if (ctrl && InputAdapter.KeyDown(InputAdapter.Key.Y)) Redo();
            else if (ctrl && InputAdapter.KeyDown(InputAdapter.Key.S)) SaveProject();
            else if (!ctrl && InputAdapter.KeyDown(InputAdapter.Key.F)) FrameSite(false);
            else if (!ctrl && InputAdapter.KeyDown(InputAdapter.Key.T)) FrameSite(true);
            else if (!ctrl && InputAdapter.KeyDown(InputAdapter.Key.Space)) Sun.Animate = !Sun.Animate;
        }

        private void OnApplicationQuit()
        {
            AppSettings.Save();
        }

        // ------------------------------------------------------------------ status

        public void Status(string message, bool error = false)
        {
            if (error) Debug.LogWarning(message);
            StatusChanged?.Invoke(message, error);
        }

        // ------------------------------------------------------------------ components

        public PVModuleSpec Module =>
            Library.FindModule(Project?.SystemDesign.ModuleId) ?? Library.Modules.FirstOrDefault();

        public InverterSpec Inverter =>
            Library.FindInverter(Project?.SystemDesign.InverterId) ?? Library.Inverters.FirstOrDefault();

        public ArrayField ActiveField =>
            Project != null && Project.Fields.Count > 0 ? Project.Fields[Mathf.Clamp(ActiveFieldIndex, 0, Project.Fields.Count - 1)] : null;

        public SystemSizingResult Sizing => _sizing ??= SystemSizing.Size(Project, Module, Inverter);

        private void RebuildLibrary()
        {
            Library = ComponentLibrary.CreateDefault();
            if (Project == null) return;
            foreach (var m in Project.CustomModules) Library.AddOrReplace(m);
            foreach (var i in Project.CustomInverters) Library.AddOrReplace(i);
        }

        // ------------------------------------------------------------------ editing

        /// <summary>Records an undo step, applies the change and notifies listeners.</summary>
        public void Edit(ChangeKind kind, Action mutate)
        {
            if (Project == null) return;
            UndoStack.Record(Project);
            mutate();
            MarkChanged(kind);
        }

        public void MarkChanged(ChangeKind kind)
        {
            if (kind != ChangeKind.Info && kind != ChangeKind.Economics) _revision++;
            IsDirty = kind != ChangeKind.ProjectLoaded || IsDirty;
            _sizing = null;
            switch (kind)
            {
                case ChangeKind.ProjectLoaded:
                    RebuildLibrary();
                    InvalidateShading();
                    InvalidateSimulation();
                    Sun.SetLocation(Project.Location);
                    Scene.RebuildAll(Project, Module, Sun.Sun);
                    UpdateSunPathFrame();
                    FrameSite(false);
                    break;
                case ChangeKind.Site:
                    Sun.SetLocation(Project.Location);
                    InvalidateSimulation();
                    break;
                case ChangeKind.Boundary:
                    Scene.RebuildBoundary(Project);
                    UpdateSunPathFrame();
                    break;
                case ChangeKind.Geometry:
                    Scene.RebuildBoundary(Project);
                    Scene.RebuildTables(Project, Module, Sun.Sun);
                    InvalidateShading();
                    InvalidateSimulation();
                    UpdateSunPathFrame();
                    break;
                case ChangeKind.Obstacles:
                    Scene.RebuildObstacles(Project);
                    InvalidateShading();
                    InvalidateSimulation();
                    break;
                case ChangeKind.Components:
                    RebuildLibrary();
                    Scene.RebuildTables(Project, Module, Sun.Sun);
                    InvalidateShading();
                    InvalidateSimulation();
                    break;
                case ChangeKind.Electrical:
                case ChangeKind.Losses:
                case ChangeKind.Meteo:
                    InvalidateSimulation();
                    break;
                case ChangeKind.Economics:
                    RecomputeEconomics();
                    break;
            }
            ApplyColors();
            Changed?.Invoke(kind);
        }

        private void InvalidateShading()
        {
            ShadingUpToDate = false;
            _liveShadingScene = null;
        }

        private void InvalidateSimulation()
        {
            if (Simulation == null && Economics == null) return;
            Simulation = null;
            Economics = null;
            ResultsChanged?.Invoke();
        }

        public void Undo()
        {
            if (!UndoStack.CanUndo) return;
            Project = UndoStack.Undo(Project);
            AfterHistoryChange("Undo");
        }

        public void Redo()
        {
            if (!UndoStack.CanRedo) return;
            Project = UndoStack.Redo(Project);
            AfterHistoryChange("Redo");
        }

        private void AfterHistoryChange(string what)
        {
            _revision++;
            Tools.ClearSelection();
            ActiveFieldIndex = Mathf.Clamp(ActiveFieldIndex, 0, Math.Max(0, Project.Fields.Count - 1));
            RebuildLibrary();
            _sizing = null;
            InvalidateShading();
            InvalidateSimulation();
            Sun.SetLocation(Project.Location);
            Scene.RebuildAll(Project, Module, Sun.Sun);
            IsDirty = true;
            ApplyColors();
            Changed?.Invoke(ChangeKind.ProjectLoaded);
            Status(what);
        }

        public void SetActiveField(int index)
        {
            if (Project == null || Project.Fields.Count == 0) return;
            ActiveFieldIndex = Mathf.Clamp(index, 0, Project.Fields.Count - 1);
            Changed?.Invoke(ChangeKind.Info);
        }

        // ------------------------------------------------------------------ project files

        public void NewProject()
        {
            Project = PVProject.CreateDefault();
            Project.Info.Designer = Environment.UserName;
            ProjectPath = "";
            ActiveFieldIndex = 0;
            UndoStack.Clear();
            Shading = null;
            MarkChanged(ChangeKind.ProjectLoaded);
            var f = Project.Fields[0];
            var layout = LayoutGenerator.Generate(Project, f, Module);
            f.Tables = layout.Tables;
            MarkChanged(ChangeKind.Geometry);
            IsDirty = false;
            Status("New project. Set the site location, then draw the boundary and generate the layout.");
        }

        public IEnumerable<string> ListProjects()
        {
            try
            {
                return Directory.GetFiles(AppPaths.Projects, "*" + PVProject.FileExtension)
                    .OrderByDescending(File.GetLastWriteTimeUtc).ToList();
            }
            catch (Exception)
            {
                return Enumerable.Empty<string>();
            }
        }

        public void LoadProjectFile(string path)
        {
            var json = File.ReadAllText(path);
            var p = ProjectSerializer.Deserialize(json);
            Project = p;
            ProjectPath = path;
            ActiveFieldIndex = 0;
            UndoStack.Clear();
            Shading = null;
            Tools?.ClearSelection();
            MarkChanged(ChangeKind.ProjectLoaded);
            IsDirty = false;
            AppSettings.LastProjectPath = path;
            Status("Opened " + Path.GetFileName(path));
        }

        public bool SaveProject(string path = null)
        {
            if (Project == null) return false;
            if (string.IsNullOrEmpty(path)) path = ProjectPath;
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(AppPaths.Projects, AppPaths.SanitizeFileName(Project.Info.Name) + PVProject.FileExtension);
            try
            {
                Project.Info.ModifiedUtc = DateTime.UtcNow;
                if (Project.Info.CreatedUtc == default) Project.Info.CreatedUtc = Project.Info.ModifiedUtc;
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppPaths.Projects);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, ProjectSerializer.Serialize(Project));
                // Atomic replacement: a crash never leaves the project without a valid file.
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                ProjectPath = path;
                IsDirty = false;
                AppSettings.LastProjectPath = path;
                Status("Saved " + path);
                Changed?.Invoke(ChangeKind.Info);
                return true;
            }
            catch (Exception e)
            {
                Status("Save failed: " + e.Message, true);
                return false;
            }
        }

        public bool SaveProjectAs(string name)
        {
            if (Project == null) return false;
            Project.Info.Name = string.IsNullOrWhiteSpace(name) ? Project.Info.Name : name.Trim();
            var path = Path.Combine(AppPaths.Projects, AppPaths.SanitizeFileName(Project.Info.Name) + PVProject.FileExtension);
            return SaveProject(path);
        }

        // ------------------------------------------------------------------ layout

        public void GenerateLayout(int fieldIndex)
        {
            if (Project == null || fieldIndex < 0 || fieldIndex >= Project.Fields.Count) return;
            var field = Project.Fields[fieldIndex];
            LayoutResult res = null;
            Edit(ChangeKind.Geometry, () =>
            {
                res = LayoutGenerator.Generate(Project, field, Module);
                field.Tables = res.Tables;
            });
            var msg = $"{field.Name}: {res.Tables.Count} tables, {res.Rows} rows, {field.ModuleCount} modules, GCR {res.Gcr * 100:0.0} %";
            if (!string.IsNullOrEmpty(res.Message)) msg += ". " + res.Message;
            Status(msg, res.Tables.Count == 0);
        }

        public void ClearLayout(int fieldIndex)
        {
            if (Project == null || fieldIndex < 0 || fieldIndex >= Project.Fields.Count) return;
            Edit(ChangeKind.Geometry, () => Project.Fields[fieldIndex].Tables.Clear());
        }

        public void AddField()
        {
            Edit(ChangeKind.Geometry, () =>
            {
                var src = ActiveField;
                var f = src != null ? src.CloneWithoutTables() : ArrayField.CreateDefault("Field", Project.Location.Latitude);
                f.Name = "Field " + (Project.Fields.Count + 1);
                Project.Fields.Add(f);
            });
            SetActiveField(Project.Fields.Count - 1);
        }

        public void RemoveActiveField()
        {
            if (Project.Fields.Count <= 1)
            {
                Status("A project needs at least one field.", true);
                return;
            }
            int idx = ActiveFieldIndex;
            Edit(ChangeKind.Geometry, () => Project.Fields.RemoveAt(idx));
            SetActiveField(Math.Max(0, idx - 1));
        }

        // ------------------------------------------------------------------ meteo

        public void LoadPvgis()
        {
            if (Project == null) return;
            StartCoroutine(DownloadPvgis(Project.Location.Latitude, Project.Location.Longitude));
        }

        private IEnumerator DownloadPvgis(double lat, double lon)
        {
            Status("Downloading PVGIS TMY data ...");
            string[] versions = { "v5_3", "v5_2" };
            string lastError = "";
            foreach (var v in versions)
            {
                using (var req = UnityWebRequest.Get(PvgisTmyParser.BuildUrl(lat, lon, v)))
                {
                    req.timeout = 90;
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        lastError = req.error + " " + (req.downloadHandler?.text ?? "");
                        continue;
                    }
                    try
                    {
                        var ds = PvgisTmyParser.Parse(req.downloadHandler.text);
                        Edit(ChangeKind.Meteo, () =>
                        {
                            Project.Meteo = ds;
                            if (ds.Altitude > 0) Project.Location.Altitude = ds.Altitude;
                        });
                        Status($"Meteo loaded: {ds.Description}, GHI {ds.AnnualGhiKWh():0} kWh/m2/year");
                        yield break;
                    }
                    catch (Exception e)
                    {
                        lastError = e.Message;
                    }
                }
            }
            Status("PVGIS download failed: " + Shorten(lastError, 200), true);
            Window?.Dialog.Show("PVGIS", "Could not download meteo data from PVGIS.\n\n" + Shorten(lastError, 600) +
                "\n\nCheck the internet connection, or generate a synthetic year to continue offline.");
        }

        public void GenerateSyntheticMeteo(double clearSkyRatio)
        {
            var loc = Project.Location;
            var ds = SyntheticMeteoGenerator.Generate(loc.Latitude, loc.Longitude, loc.Altitude,
                new SyntheticMeteoGenerator.Options { ClearSkyRatio = clearSkyRatio });
            Edit(ChangeKind.Meteo, () => Project.Meteo = ds);
            Status($"Synthetic meteo generated: GHI {ds.AnnualGhiKWh():0} kWh/m2/year (approximate).");
        }

        private static string Shorten(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "...";

        // ------------------------------------------------------------------ component import

        /// <summary>Imports every .PAN / .OND file found in the Components folder.</summary>
        public void ImportComponentFiles()
        {
            int modules = 0, inverters = 0;
            var errors = new List<string>();
            string[] files;
            try
            {
                files = Directory.GetFiles(AppPaths.Components);
            }
            catch (Exception e)
            {
                Status("Cannot read " + AppPaths.Components + ": " + e.Message, true);
                return;
            }

            var newModules = new List<PVModuleSpec>();
            var newInverters = new List<InverterSpec>();
            foreach (var file in files)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".pan" && ext != ".ond") continue;
                try
                {
                    string text = File.ReadAllText(file, System.Text.Encoding.UTF8);
                    if (ext == ".pan") newModules.Add(PanOndParser.ParsePan(text));
                    else newInverters.Add(PanOndParser.ParseOnd(text));
                }
                catch (Exception e)
                {
                    errors.Add(Path.GetFileName(file) + ": " + e.Message);
                }
            }
            Edit(ChangeKind.Components, () =>
            {
                foreach (var m in newModules)
                {
                    Project.CustomModules.RemoveAll(x => x.Manufacturer == m.Manufacturer && x.Model == m.Model);
                    Project.CustomModules.Add(m);
                    modules++;
                }
                foreach (var i in newInverters)
                {
                    Project.CustomInverters.RemoveAll(x => x.Manufacturer == i.Manufacturer && x.Model == i.Model);
                    Project.CustomInverters.Add(i);
                    inverters++;
                }
            });
            string msg = $"Imported {modules} module(s) and {inverters} inverter(s) from {AppPaths.Components}.";
            if (errors.Count > 0) msg += "\n\nSkipped:\n" + string.Join("\n", errors);
            if (modules + inverters == 0 && errors.Count == 0)
                msg = "No .PAN or .OND files found.\nCopy PVsyst component files into:\n" + AppPaths.Components;
            Window.Dialog.Show("Import components", msg, ("OK", null), ("Open folder", () => AppPaths.OpenFolder(AppPaths.Components)));
        }

        // ------------------------------------------------------------------ calculations

        private PVProject Snapshot()
        {
            var snap = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(Project, includeMeteo: false, indented: false));
            snap.Meteo = Project.Meteo;
            return snap;
        }

        public void RunShading(Action then = null)
        {
            if (Project.TotalTables() == 0)
            {
                Status("No PV tables: generate the layout first.", true);
                return;
            }
            var snap = Snapshot();
            var module = Module;
            int samples = Project.Simulation.MaxShadingSamples;
            int revision = _revision;
            bool started = Jobs.Run("Near shading", (progress, token) =>
            {
                var scene = ShadingScene.Build(snap, module, samples);
                return ShadingCalculator.Compute(snap, scene, progress, token);
            }, res =>
            {
                if (revision != _revision)
                {
                    Status("The project changed during the shading calculation: run it again.", true);
                    return;
                }
                Shading = res;
                ShadingUpToDate = true;
                Status($"Near shading computed: {res.SampleCount} points, {res.OccluderCount} objects, {res.ComputeSeconds:0.0} s. " +
                       $"Diffuse factor {res.Global.DiffuseFactor:0.000}.");
                ResultsChanged?.Invoke();
                then?.Invoke();
            }, e => Status("Shading failed: " + e.Message, true));
            if (!started) Status("Another calculation is running.", true);
        }

        public void RunSimulation()
        {
            if (Project.Meteo == null)
            {
                Window.Dialog.Show("Meteo data required", "Load PVGIS meteo data (internet) or generate a synthetic year before simulating.",
                    ("Download PVGIS", LoadPvgis), ("Synthetic year", () => GenerateSyntheticMeteo(0.75)), ("Cancel", null));
                return;
            }
            if (Project.Simulation.Shading != ShadingMode.None && !ShadingUpToDate)
            {
                RunShading(RunSimulation);
                return;
            }
            var sizing = Sizing;
            if (sizing.HasErrors)
            {
                Window.Dialog.Show("Electrical design errors",
                    string.Join("\n", sizing.Messages.Where(m => m.Level == DesignMessageLevel.Error).Select(m => "- " + m.Text)) +
                    "\n\nFix the electrical design before simulating.");
                return;
            }
            var snap = Snapshot();
            var module = Module;
            var inverter = Inverter;
            var shading = Project.Simulation.Shading == ShadingMode.None ? null : Shading;
            int simRevision = _revision;
            bool started = Jobs.Run("Simulation", (progress, token) => EnergySimulator.Run(snap, module, inverter, shading, progress, token),
                res =>
                {
                    if (simRevision != _revision)
                    {
                        Status("The project changed during the simulation: run it again.", true);
                        return;
                    }
                    Simulation = res;
                    RecomputeEconomics();
                    Status($"Simulation done: {res.Annual.EGrid / 1000:#,0.0} MWh/year, {res.SpecificYield:0} kWh/kWp, PR {res.PerformanceRatio * 100:0.0} %.");
                    ResultsChanged?.Invoke();
                    Window.ShowTab("Results");
                }, e => Status("Simulation failed: " + e.Message, true));
            if (!started) Status("Another calculation is running.", true);
        }

        private void RecomputeEconomics()
        {
            Economics = Simulation == null
                ? null
                : EconomicAnalysis.Compute(Project.Economics, Simulation.PnomDcKw, Simulation.Annual.EGrid, Project.Losses.AnnualDegradation);
            ResultsChanged?.Invoke();
        }

        // ------------------------------------------------------------------ reports

        public void ExportReport()
        {
            if (Simulation == null)
            {
                Window.Dialog.Show("Report", "Run the simulation first.", ("Simulate now", RunSimulation), ("Cancel", null));
                return;
            }
            try
            {
                var html = ReportBuilder.BuildHtml(new ReportBuilder.Input
                {
                    Project = Project,
                    Module = Module,
                    Inverter = Inverter,
                    Simulation = Simulation,
                    Shading = Project.Simulation.Shading == ShadingMode.None ? null : Shading,
                    Economics = Economics,
                    Bom = BillOfMaterials.Build(Project, Module, Inverter, Simulation.Sizing),
                    ScreenshotPng = CaptureScreenshot(1600, 900),
                    AppVersion = Version
                });
                string name = AppPaths.SanitizeFileName(Project.Info.Name) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".html";
                string path = Path.Combine(AppPaths.Reports, name);
                Directory.CreateDirectory(AppPaths.Reports);
                File.WriteAllText(path, html);
                Status("Report saved: " + path);
                Application.OpenURL(new Uri(path).AbsoluteUri);
            }
            catch (Exception e)
            {
                Status("Report failed: " + e.Message, true);
            }
        }

        public void ExportHourlyCsv()
        {
            if (Simulation == null)
            {
                Status("Run the simulation first.", true);
                return;
            }
            try
            {
                string path = Path.Combine(AppPaths.Reports, AppPaths.SanitizeFileName(Project.Info.Name) + "_hourly.csv");
                File.WriteAllText(path, ReportBuilder.BuildHourlyCsv(Project, Simulation));
                Status("Hourly CSV saved: " + path);
                AppPaths.OpenFolder(AppPaths.Reports);
            }
            catch (Exception e)
            {
                Status("Export failed: " + e.Message, true);
            }
        }

        public byte[] CaptureScreenshot(int width, int height)
        {
            var cam = OrbitCamera != null ? OrbitCamera.Camera : null;
            if (cam == null) return null;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            // Render the report picture in daylight with realistic colours, whatever the current view state.
            int prevDay = Sun.DayOfYear;
            float prevHour = Sun.Hour;
            bool prevAnimate = Sun.Animate;
            var prevMode = ColorMode;
            Sun.Animate = false;
            Sun.SetDayAndHour(Project != null && Project.Location.Latitude < 0 ? 355 : 172, 10.5f);
            if (prevMode != ColorMode.Realistic) Scene.ApplyColors(ColorMode.Realistic, null, null);
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                var png = tex.EncodeToPNG();
                Destroy(tex);
                return png;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Screenshot failed: " + e.Message);
                return null;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                Sun.SetDayAndHour(prevDay, prevHour);
                Sun.Animate = prevAnimate;
                if (prevMode != ColorMode.Realistic) ApplyColors();
            }
        }

        // ------------------------------------------------------------------ view

        public void FrameSite(bool top)
        {
            if (OrbitCamera == null || Scene == null) return;
            OrbitCamera.Frame(Scene.SiteBounds(), top);
        }

        private void UpdateSunPathFrame()
        {
            var b = Scene.SiteBounds();
            Sun.SetPathFrame(new Vector3(b.center.x, 0, b.center.z), Mathf.Max(b.size.x, b.size.z) * 0.8f + 20f); // outside the site so the arc never crosses the tables
        }

        public void SetColorMode(ColorMode mode)
        {
            ColorMode = mode;
            ApplyColors();
            Changed?.Invoke(ChangeKind.Info);
        }

        public void ApplyColors()
        {
            if (Scene == null || Project == null) return;
            float[] shade = null;
            List<int[]> assignment = null;
            if (ColorMode == ColorMode.ShadingHeatmap && Project.TotalTables() > 0)
            {
                _liveShadingScene ??= ShadingScene.Build(Project, Module, 4000);
                shade = _liveShadingScene.InstantTableShade(Sun.Sun.Azimuth, Sun.Sun.Elevation);
            }
            var sizing = Sizing;
            if (ColorMode == ColorMode.ByInverter) assignment = SystemSizing.AssignStrings(Project, sizing);
            Scene.ApplyColors(ColorMode, shade, assignment);
            Scene.ShowInverters(Project, assignment, sizing.InverterCount, ColorMode == ColorMode.ByInverter);
        }

        private void OnSunChanged(Core.Solar.SunPosition sun)
        {
            Scene?.UpdateTrackers(sun);
            if (ColorMode == ColorMode.ShadingHeatmap) _heatmapDirty = true;
        }
    }
}
