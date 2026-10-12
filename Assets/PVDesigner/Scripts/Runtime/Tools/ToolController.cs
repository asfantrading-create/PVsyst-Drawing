using System;
using System.Collections.Generic;
using PVDesigner.App;
using PVDesigner.Core.Math;
using PVDesigner.Core.Model;
using PVDesigner.InputHandling;
using PVDesigner.SceneView;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PVDesigner.Tools
{
    public enum ToolMode
    {
        Select,
        DrawSiteBoundary,
        DrawFieldBoundary,
        PlaceBuilding,
        PlaceTree,
        PlaceWall,
        PlacePole,
        PlaceTank,
        Measure
    }

    /// <summary>Mouse driven editing tools for the 3D / plan view (desktop mode).</summary>
    public class ToolController : MonoBehaviour
    {
        public ToolMode Mode { get; private set; } = ToolMode.Select;
        public SceneEntity Selected { get; private set; }
        public float GridSnap = 0.5f;
        public bool SnapEnabled = true;

        public event Action<ToolMode> ModeChanged;
        public event Action<SceneEntity> SelectionChanged;

        private PVDesignerApp _app;
        private readonly List<Vec2> _draft = new List<Vec2>();
        private LineRenderer _draftLine;
        private LineRenderer _measureLine;
        private Vector3? _measureStart;
        private bool _dragging;
        private Vector3 _dragOffset;
        private bool _dragRecorded;

        public void Initialize(PVDesignerApp app)
        {
            _app = app;
            _draftLine = app.Scene.CreateLine("Draft", new Color(0.2f, 0.8f, 1f, 1f), 0.3f);
            _measureLine = app.Scene.CreateLine("Measure", new Color(1f, 1f, 1f, 1f), 0.15f);
        }

        public void SetMode(ToolMode mode)
        {
            CancelDraft();
            Mode = mode;
            _measureStart = null;
            _measureLine.positionCount = 0;
            ModeChanged?.Invoke(mode);
            switch (mode)
            {
                case ToolMode.DrawSiteBoundary:
                case ToolMode.DrawFieldBoundary:
                    _app.Status("Click to add boundary points. Right click or Enter to finish, Backspace to undo a point, Esc to cancel.");
                    break;
                case ToolMode.Measure:
                    _app.Status("Click two points to measure a distance.");
                    break;
                case ToolMode.Select:
                    _app.Status("Select: click an object. Drag obstacles to move them, R to rotate, Delete to remove.");
                    break;
                default:
                    _app.Status("Click on the ground to place the object. Esc to stop.");
                    break;
            }
        }

        public void ClearSelection() => Select(null);

        public void Select(SceneEntity e)
        {
            Selected = e;
            _app.Scene.SetSelection(e);
            _app.ApplyColors();
            SelectionChanged?.Invoke(e);
        }

        public Obstacle SelectedObstacle =>
            Selected != null && Selected.Kind == EntityKind.Obstacle ? _app.Project.FindObstacle(Selected.ObstacleId) : null;

        private bool PointerOverUi => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private bool Typing
        {
            get
            {
                var es = EventSystem.current;
                return es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null;
            }
        }

        private void Update()
        {
            if (_app == null || _app.Project == null || _app.VR.IsActive) return;
            var cam = _app.OrbitCamera;
            if (cam == null || cam.Camera == null) return;

            if (!Typing)
            {
                if (InputAdapter.KeyDown(InputAdapter.Key.Escape))
                {
                    if (_draft.Count > 0) CancelDraft();
                    else if (Mode != ToolMode.Select) SetMode(ToolMode.Select);
                    else ClearSelection();
                }
                if (InputAdapter.KeyDown(InputAdapter.Key.Delete)) DeleteSelected();
                if (InputAdapter.KeyDown(InputAdapter.Key.R)) RotateSelected(15);
                if (InputAdapter.KeyDown(InputAdapter.Key.Backspace) && _draft.Count > 0)
                {
                    _draft.RemoveAt(_draft.Count - 1);
                    UpdateDraftLine(null);
                }
                if (InputAdapter.KeyDown(InputAdapter.Key.Enter)) FinishDraft();
            }

            Vector2 mouse = InputAdapter.MousePosition;
            bool overUi = PointerOverUi;
            cam.GroundPoint(mouse, out Vector3 ground);
            var snapped = Snap(ground);

            switch (Mode)
            {
                case ToolMode.Select:
                    UpdateSelect(mouse, ground, overUi);
                    break;
                case ToolMode.DrawSiteBoundary:
                case ToolMode.DrawFieldBoundary:
                    UpdateDraftLine(snapped);
                    if (!overUi && InputAdapter.MouseDown(0)) AddDraftPoint(snapped);
                    if (!overUi && InputAdapter.MouseUp(1) && _draft.Count >= 3) FinishDraft();
                    break;
                case ToolMode.Measure:
                    UpdateMeasure(snapped, overUi);
                    break;
                default:
                    if (!overUi && InputAdapter.MouseDown(0)) PlaceObstacle(snapped);
                    break;
            }
        }

        private Vector3 Snap(Vector3 p)
        {
            if (!SnapEnabled || GridSnap <= 0) return p;
            return new Vector3(Mathf.Round(p.x / GridSnap) * GridSnap, p.y, Mathf.Round(p.z / GridSnap) * GridSnap);
        }

        // ------------------------------------------------------------ select / move

        private void UpdateSelect(Vector2 mouse, Vector3 ground, bool overUi)
        {
            if (!overUi && InputAdapter.MouseDown(0))
            {
                var ray = _app.OrbitCamera.Camera.ScreenPointToRay(mouse);
                SceneEntity hit = null;
                if (Physics.Raycast(ray, out RaycastHit info, 10000f, ~(1 << SceneBuilder.IgnoreRaycastLayer)))
                    hit = info.collider.GetComponent<SceneEntity>();
                Select(hit);
                var o = SelectedObstacle;
                if (o != null)
                {
                    _dragging = true;
                    _dragRecorded = false;
                    _dragOffset = new Vector3((float)o.X, 0, (float)o.Z) - ground;
                }
            }
            if (_dragging && InputAdapter.MouseHeld(0))
            {
                var o = SelectedObstacle;
                if (o != null)
                {
                    var target = Snap(ground + _dragOffset);
                    if (Mathf.Abs(target.x - (float)o.X) > 0.01f || Mathf.Abs(target.z - (float)o.Z) > 0.01f)
                    {
                        if (!_dragRecorded)
                        {
                            _app.UndoStack.Record(_app.Project);
                            _dragRecorded = true;
                        }
                        o.X = target.x;
                        o.Z = target.z;
                        _app.Scene.RefreshObstacle(o);
                        _app.Scene.SetSelection(Selected);
                    }
                }
            }
            if (_dragging && InputAdapter.MouseUp(0))
            {
                _dragging = false;
                if (_dragRecorded)
                {
                    var id = Selected != null ? Selected.ObstacleId : null;
                    _app.MarkChanged(ChangeKind.Obstacles);
                    ReselectObstacle(id);
                }
            }
        }

        private void ReselectObstacle(string id)
        {
            if (id == null || !_app.Scene.Obstacles.TryGetValue(id, out var go) || go == null)
            {
                ClearSelection();
                return;
            }
            Select(go.GetComponentInChildren<SceneEntity>());
        }

        public void DeleteSelected()
        {
            if (Selected == null) return;
            if (Selected.Kind == EntityKind.Obstacle)
            {
                string id = Selected.ObstacleId;
                ClearSelection();
                _app.Edit(ChangeKind.Obstacles, () => _app.Project.Obstacles.RemoveAll(o => o.Id == id));
                _app.Status("Obstacle deleted.");
            }
            else
            {
                int fi = Selected.FieldIndex, ti = Selected.TableIndex;
                ClearSelection();
                if (fi < _app.Project.Fields.Count && ti < _app.Project.Fields[fi].Tables.Count)
                {
                    _app.Edit(ChangeKind.Geometry, () => _app.Project.Fields[fi].Tables.RemoveAt(ti));
                    _app.Status("Table deleted.");
                }
            }
        }

        public void RotateSelected(double degrees)
        {
            var o = SelectedObstacle;
            if (o == null) return;
            string id = o.Id;
            _app.Edit(ChangeKind.Obstacles, () => o.Rotation = MathUtil.Wrap360(o.Rotation + degrees));
            ReselectObstacle(id);
        }

        // ------------------------------------------------------------ boundaries

        private void AddDraftPoint(Vector3 p)
        {
            var v = new Vec2(p.x, p.z);
            if (_draft.Count > 0 && Vec2.Distance(_draft[_draft.Count - 1], v) < 0.25) return;
            // Clicking near the first point closes the polygon.
            if (_draft.Count >= 3 && Vec2.Distance(_draft[0], v) < Mathf.Max(1f, _app.OrbitCamera.Distance * 0.01f))
            {
                FinishDraft();
                return;
            }
            _draft.Add(v);
            UpdateDraftLine(p);
            _app.Status($"{_draft.Count} point(s). Area {Polygon2D.Area(_draft) / 10000.0:0.000} ha. Right click / Enter to finish.");
        }

        private void UpdateDraftLine(Vector3? cursor)
        {
            int n = _draft.Count;
            if (n == 0)
            {
                _draftLine.positionCount = 0;
                return;
            }
            _draftLine.loop = false;
            _draftLine.positionCount = n + (cursor.HasValue ? 1 : 0);
            for (int i = 0; i < n; i++) _draftLine.SetPosition(i, new Vector3((float)_draft[i].X, 0.1f, (float)_draft[i].Y));
            if (cursor.HasValue) _draftLine.SetPosition(n, new Vector3(cursor.Value.x, 0.1f, cursor.Value.z));
        }

        private void FinishDraft()
        {
            if (_draft.Count < 3)
            {
                CancelDraft();
                return;
            }
            var pts = new List<Vec2>(_draft);
            var mode = Mode;
            CancelDraft();
            if (mode == ToolMode.DrawSiteBoundary)
            {
                _app.Edit(ChangeKind.Boundary, () => _app.Project.SiteBoundary = pts);
                _app.Status($"Site boundary set: {Polygon2D.Area(pts) / 10000.0:0.00} ha. Generate the layout in the Layout tab.");
            }
            else if (mode == ToolMode.DrawFieldBoundary && _app.ActiveField != null)
            {
                var f = _app.ActiveField;
                _app.Edit(ChangeKind.Geometry, () => f.Boundary = pts);
                _app.Status($"Area of {f.Name} set: {Polygon2D.Area(pts) / 10000.0:0.00} ha.");
            }
            SetMode(ToolMode.Select);
        }

        private void CancelDraft()
        {
            _draft.Clear();
            if (_draftLine != null) _draftLine.positionCount = 0;
        }

        // ------------------------------------------------------------ obstacles

        private void PlaceObstacle(Vector3 p)
        {
            ObstacleType type;
            switch (Mode)
            {
                case ToolMode.PlaceTree: type = ObstacleType.Tree; break;
                case ToolMode.PlaceWall: type = ObstacleType.Wall; break;
                case ToolMode.PlacePole: type = ObstacleType.Pole; break;
                case ToolMode.PlaceTank: type = ObstacleType.Tank; break;
                default: type = ObstacleType.Building; break;
            }
            var o = Obstacle.Create(type, p.x, p.z);
            o.Name = type + " " + (_app.Project.Obstacles.Count + 1);
            _app.Edit(ChangeKind.Obstacles, () => _app.Project.Obstacles.Add(o));
            ReselectObstacle(o.Id);
            _app.Status($"{o.Name} placed. Edit its size in the Obstacles tab.");
        }

        // ------------------------------------------------------------ measure

        private void UpdateMeasure(Vector3 p, bool overUi)
        {
            if (_measureStart.HasValue)
            {
                _measureLine.positionCount = 2;
                _measureLine.SetPosition(0, _measureStart.Value + Vector3.up * 0.15f);
                _measureLine.SetPosition(1, p + Vector3.up * 0.15f);
                var d = Vector3.Distance(_measureStart.Value, p);
                var dir = p - _measureStart.Value;
                double az = MathUtil.Wrap360(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
                if (!overUi && InputAdapter.MouseDown(0))
                {
                    _app.Status($"Distance {d:0.00} m, bearing {az:0.0} deg from north.");
                    _measureStart = null;
                    return;
                }
                _app.Status($"Distance {d:0.00} m, bearing {az:0.0} deg");
            }
            else if (!overUi && InputAdapter.MouseDown(0))
            {
                _measureStart = p;
            }
        }
    }
}
