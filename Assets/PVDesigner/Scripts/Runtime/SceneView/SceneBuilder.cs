using System.Collections.Generic;
using PVDesigner.Core.Components;
using PVDesigner.Core.Layout;
using PVDesigner.Core.Math;
using PVDesigner.Core.Model;
using PVDesigner.Core.Solar;
using PVDesigner.Rendering;
using UnityEngine;

namespace PVDesigner.SceneView
{
    public enum EntityKind
    {
        Table,
        Obstacle
    }

    public enum ColorMode
    {
        Realistic,
        ByInverter,
        ShadingHeatmap
    }

    /// <summary>Links a GameObject to the project element it represents (used by picking and selection).</summary>
    public class SceneEntity : MonoBehaviour
    {
        public EntityKind Kind;
        public int FieldIndex;
        public int TableIndex;
        public string ObstacleId;
        /// <summary>Global table index (order of <see cref="SceneBuilder.Tables"/>).</summary>
        public int GlobalTableIndex;
    }

    /// <summary>Converts the project model into GameObjects under a single world root.</summary>
    public class SceneBuilder : MonoBehaviour
    {
        public const int IgnoreRaycastLayer = 2;

        public class TableView
        {
            public GameObject Root;
            /// <summary>Rotating part for trackers, same as Root for fixed tables.</summary>
            public Transform Panel;
            public MeshRenderer Renderer;
            public int FieldIndex;
            public int TableIndex;
        }

        public Transform World { get; private set; }
        public MaterialLibrary Materials { get; private set; }
        public readonly List<TableView> Tables = new List<TableView>();
        public readonly Dictionary<string, GameObject> Obstacles = new Dictionary<string, GameObject>();

        private Transform _tablesRoot, _obstaclesRoot, _overlayRoot, _invertersRoot;
        private GameObject _ground;
        private LineRenderer _boundaryLine;
        private MeshFilter _boundaryFill;
        private readonly List<LineRenderer> _fieldLines = new List<LineRenderer>();
        private readonly List<Mesh> _ownedMeshes = new List<Mesh>();
        private MaterialPropertyBlock _mpb;
        private SceneEntity _selected;

        private PVProject _project;
        private PVModuleSpec _module;
        private readonly Dictionary<LineRenderer, float> _lineWidths = new Dictionary<LineRenderer, float>();
        private float _lineScale = 1f;

        /// <summary>
        /// LineRenderer widths are not affected by transform scale: keep lines proportional to the
        /// model when the world is scaled (VR table-top mode).
        /// </summary>
        public float LineScale => _lineScale;

        public void SetLineScale(float scale)
        {
            _lineScale = Mathf.Max(1e-4f, scale);
            foreach (var lr in World.GetComponentsInChildren<LineRenderer>(true))
            {
                if (lr.name == "Sun path") continue; // sized by SunController using LineScale
                if (!_lineWidths.TryGetValue(lr, out float w))
                {
                    w = lr.startWidth;
                    _lineWidths[lr] = w;
                }
                lr.startWidth = lr.endWidth = w * _lineScale;
            }
        }

        public ColorMode Mode { get; private set; } = ColorMode.Realistic;

        public void Initialize(int groundStyle)
        {
            _mpb = new MaterialPropertyBlock();
            Materials = new MaterialLibrary(groundStyle);
            World = new GameObject("World").transform;
            World.SetParent(transform, false);
            _tablesRoot = Child("PV Tables");
            _obstaclesRoot = Child("Obstacles");
            _overlayRoot = Child("Overlays");
            _invertersRoot = Child("Inverters");

            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _ground.name = "Ground";
            _ground.layer = IgnoreRaycastLayer;
            _ground.transform.SetParent(World, false);
            _ground.transform.localScale = new Vector3(400, 1, 400); // 4 km x 4 km
            Destroy(_ground.GetComponent<Collider>());
            var gr = _ground.GetComponent<MeshRenderer>();
            gr.sharedMaterial = Materials.Ground;
            gr.receiveShadows = true;
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Materials.Ground.mainTextureScale = new Vector2(400, 400);

            _boundaryLine = CreateLine("Site boundary", new Color(1f, 0.55f, 0.05f, 1f), 0.35f);
            var fill = new GameObject("Site area");
            fill.transform.SetParent(_overlayRoot, false);
            _boundaryFill = fill.AddComponent<MeshFilter>();
            var fr = fill.AddComponent<MeshRenderer>();
            fr.sharedMaterial = Materials.Overlay;
            fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            BuildCompass();
        }

        private Transform Child(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(World, false);
            return t;
        }

        public LineRenderer CreateLine(string name, Color color, float width, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : _overlayRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Materials.Overlay;
            lr.startColor = lr.endColor = color;
            lr.startWidth = lr.endWidth = width;
            lr.useWorldSpace = false;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.positionCount = 0;
            return lr;
        }

        private void BuildCompass()
        {
            var go = new GameObject("North arrow");
            go.transform.SetParent(_overlayRoot, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Materials.Overlay;
            var arrow = new List<Vector2> { new Vector2(0, 6), new Vector2(2.2f, -3), new Vector2(0, -1.5f), new Vector2(-2.2f, -3) };
            mf.sharedMesh = MeshBuilder.BuildPolygon(arrow, 0.05f, new Color(0.9f, 0.15f, 0.1f, 0.9f));
            var label = new GameObject("N");
            label.transform.SetParent(go.transform, false);
            label.transform.localPosition = new Vector3(0, 0.06f, 9f);
            label.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var tm = label.AddComponent<TextMesh>();
            tm.text = "N";
            tm.fontSize = 64;
            tm.characterSize = 0.12f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.9f, 0.15f, 0.1f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                tm.font = font;
                label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            _compass = go.transform;
        }

        private Transform _compass;

        // ------------------------------------------------------------------ rebuild

        public void RebuildAll(PVProject project, PVModuleSpec module, SunPosition sun)
        {
            _project = project;
            _module = module;
            RebuildBoundary(project);
            RebuildTables(project, module, sun);
            RebuildObstacles(project);
        }

        public void RebuildBoundary(PVProject project)
        {
            _project = project;
            var pts = project.SiteBoundary;
            SetLoop(_boundaryLine, pts, 0.06f);
            if (_boundaryFill.sharedMesh != null) Destroy(_boundaryFill.sharedMesh);
            _boundaryFill.sharedMesh = null;
            if (pts.Count >= 3)
            {
                var poly = new List<Vector2>();
                foreach (var p in pts) poly.Add(new Vector2((float)p.X, (float)p.Y));
                _boundaryFill.sharedMesh = MeshBuilder.BuildPolygon(poly, 0.03f, new Color(1f, 0.6f, 0.1f, 0.10f));
                Polygon2D.Bounds(pts, out Vec2 min, out Vec2 max);
                _compass.localPosition = new Vector3((float)max.X + 12f, 0, (float)max.Y + 12f);
            }
            else
            {
                _compass.localPosition = new Vector3(20, 0, 20);
            }

            foreach (var l in _fieldLines) Destroy(l.gameObject);
            _fieldLines.Clear();
            foreach (var f in project.Fields)
            {
                if (f.Boundary == null || f.Boundary.Count < 3) continue;
                var lr = CreateLine("Field area " + f.Name, new Color(0.2f, 0.75f, 1f, 1f), 0.25f);
                SetLoop(lr, f.Boundary, 0.08f);
                _fieldLines.Add(lr);
            }
        }

        public static void SetLoop(LineRenderer lr, IReadOnlyList<Vec2> pts, float y)
        {
            lr.loop = pts.Count >= 3;
            lr.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++) lr.SetPosition(i, new Vector3((float)pts[i].X, y, (float)pts[i].Y));
        }

        public void RebuildTables(PVProject project, PVModuleSpec module, SunPosition sun)
        {
            _project = project;
            _module = module;
            // Destroy the per-field containers (and their tables).
            for (int i = _tablesRoot.childCount - 1; i >= 0; i--) Destroy(_tablesRoot.GetChild(i).gameObject);
            Tables.Clear();
            foreach (var m in _ownedMeshes) Destroy(m);
            _ownedMeshes.Clear();
            _selected = null;
            if (module == null) return;

            var mats = new[] { Materials.Module, Materials.Structure };
            for (int fi = 0; fi < project.Fields.Count; fi++)
            {
                var field = project.Fields[fi];
                if (field.Tables.Count == 0) continue;
                var fieldRoot = new GameObject(field.Name).transform;
                fieldRoot.SetParent(_tablesRoot, false);
                bool tracker = field.Mounting == MountingType.SingleAxisTracker;

                // Shared mesh for all tables of the field.
                var sample = TableGeometry.Build(field, new PVTable(0, 0, 0, 0), module);
                var rot = Quaternion.LookRotation(ToUnity(sample.V), ToUnity(sample.N));
                var mesh = MeshBuilder.BuildTableMesh(field, module, !tracker, Quaternion.Inverse(rot), (float)(sample.Center.Y - field.BaseElevation));
                _ownedMeshes.Add(mesh);
                TableGeometry.TableSize(field, module, out double tw, out double tl);
                Mesh trackerMesh = null;
                if (tracker)
                {
                    trackerMesh = MeshBuilder.BuildTrackerStructure((float)tw, (float)field.GroundClearance);
                    _ownedMeshes.Add(trackerMesh);
                }
                var colliderSize = new Vector3((float)tw, 0.1f, (float)tl);

                for (int ti = 0; ti < field.Tables.Count; ti++)
                {
                    var table = field.Tables[ti];
                    var go = new GameObject("Table " + (ti + 1));
                    go.transform.SetParent(fieldRoot, false);
                    var view = new TableView { Root = go, FieldIndex = fi, TableIndex = ti };

                    GameObject panel = go;
                    if (tracker)
                    {
                        double axisAz = field.TrackerAxisCompassAzimuth;
                        var cross = SingleAxisTracker.CrossAxisDirection(axisAz);
                        go.transform.localPosition = new Vector3((float)table.X, (float)(field.BaseElevation + table.Elevation + field.GroundClearance), (float)table.Z);
                        go.transform.localRotation = Quaternion.LookRotation(ToUnity(cross), Vector3.up);
                        var structure = new GameObject("Structure");
                        structure.transform.SetParent(go.transform, false);
                        structure.AddComponent<MeshFilter>().sharedMesh = trackerMesh;
                        structure.AddComponent<MeshRenderer>().sharedMaterial = Materials.Metal;
                        panel = new GameObject("Panel");
                        panel.transform.SetParent(go.transform, false);
                    }
                    else
                    {
                        var r = TableGeometry.Build(field, table, module);
                        go.transform.localPosition = ToUnity(r.Center);
                        go.transform.localRotation = rot;
                    }

                    panel.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = panel.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = mats;
                    var bc = panel.AddComponent<BoxCollider>();
                    bc.size = colliderSize;
                    var e = panel.AddComponent<SceneEntity>();
                    e.Kind = EntityKind.Table;
                    e.FieldIndex = fi;
                    e.TableIndex = ti;
                    e.GlobalTableIndex = Tables.Count;

                    view.Panel = panel.transform;
                    view.Renderer = mr;
                    Tables.Add(view);
                }
            }
            UpdateTrackers(sun);
        }

        /// <summary>Rotates tracker panels for the given sun position.</summary>
        public void UpdateTrackers(SunPosition sun)
        {
            if (_project == null || _module == null) return;
            foreach (var t in Tables)
            {
                if (t.FieldIndex >= _project.Fields.Count) continue;
                var field = _project.Fields[t.FieldIndex];
                if (field.Mounting != MountingType.SingleAxisTracker) continue;
                double gcr = TableGeometry.Gcr(field, _module);
                double angle = SingleAxisTracker.RotationAngle(sun, field.TrackerAxisCompassAzimuth, field.TrackerMaxAngle, field.Backtracking, gcr);
                // Panel local frame: X along the axis, Y normal, Z across. Root: X = axis, Y = up, Z = cross-axis.
                // A positive X rotation tilts the normal (Y) towards +Z, i.e. towards the cross-axis direction,
                // which matches the sign convention of SingleAxisTracker.
                t.Panel.localRotation = Quaternion.Euler((float)angle, 0, 0);
            }
        }

        public void RebuildObstacles(PVProject project)
        {
            foreach (var kv in Obstacles) Destroy(kv.Value);
            Obstacles.Clear();
            foreach (var o in project.Obstacles) Obstacles[o.Id] = BuildObstacle(o);
        }

        public void RefreshObstacle(Obstacle o)
        {
            if (Obstacles.TryGetValue(o.Id, out var old)) Destroy(old);
            Obstacles[o.Id] = BuildObstacle(o);
        }

        private GameObject BuildObstacle(Obstacle o)
        {
            var root = new GameObject(o.Name);
            root.transform.SetParent(_obstaclesRoot, false);
            root.transform.localPosition = new Vector3((float)o.X, (float)o.BaseElevation, (float)o.Z);
            root.transform.localRotation = Quaternion.Euler(0, (float)o.Rotation, 0);
            float w = (float)o.Width, d = (float)o.Depth, h = (float)o.Height;

            switch (o.Type)
            {
                case ObstacleType.Tree:
                {
                    float trunk = Mathf.Clamp((float)o.TrunkHeight, 0, h * 0.9f);
                    float crown = Mathf.Max(0.5f, h - trunk);
                    Part(root, PrimitiveType.Cylinder, new Vector3(0, trunk * 0.5f, 0), new Vector3(w * 0.12f, trunk * 0.5f, w * 0.12f), Materials.TreeTrunk, o);
                    Part(root, PrimitiveType.Sphere, new Vector3(0, trunk + crown * 0.5f, 0), new Vector3(w, crown, w), Materials.TreeCrown, o);
                    break;
                }
                case ObstacleType.Pole:
                    Part(root, PrimitiveType.Cylinder, new Vector3(0, h * 0.5f, 0), new Vector3(w, h * 0.5f, w), Materials.Metal, o);
                    break;
                case ObstacleType.Tank:
                    Part(root, PrimitiveType.Cylinder, new Vector3(0, h * 0.5f, 0), new Vector3(w, h * 0.5f, w), Materials.Tank, o);
                    break;
                case ObstacleType.Wall:
                    Part(root, PrimitiveType.Cube, new Vector3(0, h * 0.5f, 0), new Vector3(w, h, d), Materials.Building, o);
                    break;
                default:
                    Part(root, PrimitiveType.Cube, new Vector3(0, h * 0.5f, 0), new Vector3(w, h, d), Materials.Building, o);
                    // Parapet / roof edge for a more readable silhouette.
                    Part(root, PrimitiveType.Cube, new Vector3(0, h + 0.15f, 0), new Vector3(w + 0.1f, 0.3f, d + 0.1f), Materials.Roof, o);
                    break;
            }
            return root;
        }

        private static void Part(GameObject root, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Obstacle o)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var e = go.AddComponent<SceneEntity>();
            e.Kind = EntityKind.Obstacle;
            e.ObstacleId = o.Id;
        }

        // ------------------------------------------------------------------ colours & selection

        public void ApplyColors(ColorMode mode, float[] tableShade, List<int[]> inverterAssignment)
        {
            Mode = mode;
            for (int i = 0; i < Tables.Count; i++)
            {
                var t = Tables[i];
                Color c = Color.white;
                if (mode == ColorMode.ShadingHeatmap && tableShade != null && i < tableShade.Length)
                {
                    c = MaterialLibrary.ShadeColor(tableShade[i]);
                }
                else if (mode == ColorMode.ByInverter && inverterAssignment != null && i < inverterAssignment.Count)
                {
                    c = MaterialLibrary.InverterColor(Majority(inverterAssignment[i]));
                }
                if (_selected != null && _selected.Kind == EntityKind.Table && _selected.GlobalTableIndex == i)
                    c = Color.Lerp(c, new Color(1f, 0.6f, 0.1f), 0.7f);
                _mpb.Clear();
                _mpb.SetColor("_Color", c);
                t.Renderer.SetPropertyBlock(_mpb, 0);
            }
        }

        private static int Majority(int[] values)
        {
            if (values == null || values.Length == 0) return -1;
            var counts = new Dictionary<int, int>();
            int best = values[0], bestCount = 0;
            foreach (var v in values)
            {
                counts.TryGetValue(v, out int c);
                counts[v] = ++c;
                if (c > bestCount)
                {
                    bestCount = c;
                    best = v;
                }
            }
            return best;
        }

        public void SetSelection(SceneEntity entity)
        {
            if (_selected != null && _selected.Kind == EntityKind.Obstacle) TintObstacle(_selected.ObstacleId, false);
            _selected = entity;
            if (_selected != null && _selected.Kind == EntityKind.Obstacle) TintObstacle(_selected.ObstacleId, true);
        }

        private void TintObstacle(string id, bool on)
        {
            if (id == null || !Obstacles.TryGetValue(id, out var go) || go == null) return;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                _mpb.Clear();
                if (on) _mpb.SetColor("_Color", new Color(1f, 0.65f, 0.25f));
                r.SetPropertyBlock(_mpb);
            }
        }

        public void ShowInverters(PVProject project, List<int[]> assignment, int inverterCount, bool visible)
        {
            for (int i = _invertersRoot.childCount - 1; i >= 0; i--) Destroy(_invertersRoot.GetChild(i).gameObject);
            if (!visible || assignment == null || inverterCount <= 0) return;
            var sx = new double[inverterCount];
            var sz = new double[inverterCount];
            var n = new int[inverterCount];
            int k = 0;
            foreach (var f in project.Fields)
            {
                foreach (var t in f.Tables)
                {
                    if (k >= assignment.Count) break;
                    foreach (int inv in assignment[k])
                    {
                        if (inv < 0 || inv >= inverterCount) continue;
                        sx[inv] += t.X;
                        sz[inv] += t.Z;
                        n[inv]++;
                    }
                    k++;
                }
            }
            for (int i = 0; i < inverterCount; i++)
            {
                if (n[i] == 0) continue;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Inverter " + (i + 1);
                go.layer = IgnoreRaycastLayer;
                go.transform.SetParent(_invertersRoot, false);
                go.transform.localPosition = new Vector3((float)(sx[i] / n[i]), 1.0f, (float)(sz[i] / n[i]));
                go.transform.localScale = new Vector3(1.0f, 2.0f, 0.6f);
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = Materials.Inverter;
                _mpb.Clear();
                _mpb.SetColor("_Color", MaterialLibrary.InverterColor(i));
                mr.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>Bounds of the site (boundary and tables) in world-root local coordinates.</summary>
        public Bounds SiteBounds()
        {
            var b = new Bounds(Vector3.zero, new Vector3(40, 5, 40));
            bool init = false;
            if (_project != null)
            {
                foreach (var p in _project.SiteBoundary)
                {
                    var v = new Vector3((float)p.X, 0, (float)p.Y);
                    if (!init)
                    {
                        b = new Bounds(v, Vector3.one);
                        init = true;
                    }
                    else b.Encapsulate(v);
                }
            }
            foreach (var t in Tables)
            {
                if (!init)
                {
                    b = new Bounds(t.Root.transform.localPosition, Vector3.one);
                    init = true;
                }
                else b.Encapsulate(t.Root.transform.localPosition);
            }
            return b;
        }

        public static Vector3 ToUnity(Vec3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        private void OnDestroy()
        {
            foreach (var m in _ownedMeshes) if (m != null) Destroy(m);
        }
    }
}
