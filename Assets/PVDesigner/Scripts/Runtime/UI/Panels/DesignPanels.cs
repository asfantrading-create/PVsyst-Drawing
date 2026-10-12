using System.Collections.Generic;
using System.Linq;
using PVDesigner.App;
using PVDesigner.Core.Electrical;
using PVDesigner.Core.Layout;
using PVDesigner.Core.Math;
using PVDesigner.Core.Model;
using PVDesigner.SceneView;
using PVDesigner.Tools;
using UnityEngine;

namespace PVDesigner.UI.Panels
{
    public class LayoutPanel : PanelBase
    {
        public override string Title => "Layout";

        protected override void Build()
        {
            var p = App.Project;
            var f = App.ActiveField;
            Form.Header("Array fields");
            Form.Choice("Active field", () => p.Fields.Select(x => $"{x.Name} ({x.Tables.Count} tables)").ToList(),
                () => App.ActiveFieldIndex, i =>
                {
                    App.SetActiveField(i);
                    RequestRebuild();
                });
            Form.Buttons(("Add field", () =>
            {
                App.AddField();
                RequestRebuild();
            }), ("Remove field", () =>
            {
                App.RemoveActiveField();
                RequestRebuild();
            }));
            if (f == null) return;

            Form.TextField("Field name", () => f.Name, v => App.Edit(ChangeKind.Geometry, () => f.Name = v));

            Form.Header("Orientation");
            Form.Choice("Mounting", () => f.Mounting, v => App.Edit(ChangeKind.Geometry, () =>
            {
                f.Mounting = v;
                if (v == MountingType.SingleAxisTracker)
                {
                    f.Azimuth = 0;
                    f.ModulesUp = 1;
                    f.ModulesAcross = Mathf.Max(f.ModulesAcross, 30);
                    f.GroundClearance = 2.0;
                    f.Pitch = 6.0;
                }
                else
                {
                    f.Azimuth = p.Location.Latitude >= 0 ? 0 : 180;
                    f.GroundClearance = 0.8;
                }
                f.Tables.Clear();
            }), "Fixed tilted tables", "Single axis tracker (horizontal)");

            bool tracker = f.Mounting == MountingType.SingleAxisTracker;
            if (!tracker)
            {
                Form.Number("Plane tilt", () => f.Tilt, v => App.Edit(ChangeKind.Geometry, () => f.Tilt = v), "deg", 1, 0, 90, "0.#");
                Form.Number("Azimuth", () => f.Azimuth, v => App.Edit(ChangeKind.Geometry, () => f.Azimuth = MathUtil.Wrap180(v)), "deg", 5, -180, 180, "0.#");
                Form.Note("PVsyst convention: 0° = facing South, -90° = East, +90° = West, 180° = North.");
                Form.Button("Optimal tilt for latitude", () => App.Edit(ChangeKind.Geometry, () =>
                    f.Tilt = MathUtil.Clamp(System.Math.Round(System.Math.Abs(p.Location.Latitude) * 0.87 + 3.1), 5, 45)));
            }
            else
            {
                Form.Number("Axis azimuth", () => f.Azimuth, v => App.Edit(ChangeKind.Geometry, () => f.Azimuth = MathUtil.Wrap180(v)), "deg", 5, -90, 90, "0.#");
                Form.Note("0° = North-South axis (tracking East-West).");
                Form.Number("Rotation limit", () => f.TrackerMaxAngle, v => App.Edit(ChangeKind.Geometry, () => f.TrackerMaxAngle = v), "± deg", 5, 10, 90, "0");
                Form.Toggle("Backtracking", () => f.Backtracking, v => App.Edit(ChangeKind.Geometry, () => f.Backtracking = v));
            }

            Form.Header(tracker ? "Tracker" : "Table");
            Form.Integer(tracker ? "Modules along axis" : "Modules in width", () => f.ModulesAcross, v => App.Edit(ChangeKind.Geometry, () => f.ModulesAcross = v), "", 1, 200);
            Form.Integer(tracker ? "Modules across axis" : "Modules in height", () => f.ModulesUp, v => App.Edit(ChangeKind.Geometry, () => f.ModulesUp = v), "", 1, 8);
            Form.Choice("Module orientation", () => f.Orientation, v => App.Edit(ChangeKind.Geometry, () => f.Orientation = v), "Portrait", "Landscape");
            Form.Number("Gap between modules", () => f.ModuleGap, v => App.Edit(ChangeKind.Geometry, () => f.ModuleGap = v), "m", 0.01, 0, 0.5, "0.00");
            Form.Number("Gap between tables", () => f.TableGap, v => App.Edit(ChangeKind.Geometry, () => f.TableGap = v), "m", 0.1, 0, 20, "0.0#");
            Form.Number(tracker ? "Axis height" : "Ground clearance", () => f.GroundClearance, v => App.Edit(ChangeKind.Geometry, () => f.GroundClearance = v), "m", 0.1, 0, 10, "0.0#");
            Form.Number("Base elevation (roof)", () => f.BaseElevation, v => App.Edit(ChangeKind.Geometry, () => f.BaseElevation = v), "m", 0.5, -50, 300, "0.0#");
            Form.Value("Table size", () =>
            {
                TableGeometry.TableSize(f, App.Module, out double w, out double l);
                return $"{w:0.00} x {l:0.00} m, {f.ModulesPerTable} modules, {f.ModulesPerTable * App.Module.Pmax / 1000.0:0.00} kWp";
            });

            Form.Header("Rows");
            Form.Number("Pitch", () => f.Pitch, v => App.Edit(ChangeKind.Geometry, () => f.Pitch = v), "m", 0.1, 0.5, 100, "0.00");
            Form.Value("Ground coverage ratio", () => $"{TableGeometry.Gcr(f, App.Module) * 100:0.0} %");
            Form.Value("Shading limit angle", () =>
            {
                if (tracker) return "-";
                TableGeometry.TableSize(f, App.Module, out _, out double l);
                double depth = l * MathUtil.CosD(f.Tilt);
                double height = l * MathUtil.SinD(f.Tilt);
                double free = f.Pitch - depth;
                return free > 0 ? $"{System.Math.Atan2(height, free) * MathUtil.Rad2Deg:0.0}°" : "rows overlap";
            });

            Form.Header("Area");
            Form.Value("Field area", () => f.Boundary.Count >= 3 ? $"own polygon, {Polygon2D.Area(f.Boundary) / 10000.0:0.00} ha" : "uses the site boundary");
            Form.Buttons(("Draw field area", () => App.Tools.SetMode(ToolMode.DrawFieldBoundary)),
                ("Use site boundary", () => App.Edit(ChangeKind.Geometry, () => f.Boundary.Clear())));

            Form.Header("Generate");
            Form.Button("Generate layout", () => App.GenerateLayout(App.ActiveFieldIndex), true);
            Form.Button("Remove all tables of this field", () => App.ClearLayout(App.ActiveFieldIndex));
            Form.Value("Tables / modules", () => $"{f.Tables.Count} / {f.ModuleCount:#,0}");
            Form.Value("Field power", () => $"{f.ModuleCount * App.Module.Pmax / 1000.0:#,0.0} kWp");
            Form.Value("Project total", () => $"{p.TotalModules():#,0} modules, {p.TotalModules() * App.Module.Pmax / 1000.0:#,0.0} kWp");
            Form.Note("Tip: select a table in the 3D view and press Delete to remove it individually.");
        }

        private string StructureKey()
        {
            var f = App.ActiveField;
            return App.ActiveFieldIndex + "|" + App.Project.Fields.Count + "|" + (f != null ? f.Mounting.ToString() + f.Name : "");
        }

        private string _builtKey = "";

        protected override void AfterBuild() => _builtKey = StructureKey();

        public override void OnChanged(ChangeKind kind)
        {
            // Only rebuild when the form structure changes (field list, mounting type), otherwise refresh
            // values in place so a click on another control is not lost.
            if (kind == ChangeKind.ProjectLoaded || (App.Project != null && StructureKey() != _builtKey)) RequestRebuild();
            else base.OnChanged(kind);
        }
    }

    public class ObstaclesPanel : PanelBase
    {
        public override string Title => "Obstacles";

        public override void OnShow()
        {
            base.OnShow();
            App.Tools.SelectionChanged -= OnSelection;
            App.Tools.SelectionChanged += OnSelection;
        }

        private void OnSelection(SceneEntity e)
        {
            if (StructureKey() != _builtKey) RequestRebuild();
        }

        protected override void Build()
        {
            var p = App.Project;
            Form.Header("Place near shading objects");
            Form.Buttons(("Building", () => App.Tools.SetMode(ToolMode.PlaceBuilding)), ("Tree", () => App.Tools.SetMode(ToolMode.PlaceTree)),
                ("Wall", () => App.Tools.SetMode(ToolMode.PlaceWall)));
            Form.Buttons(("Pole / mast", () => App.Tools.SetMode(ToolMode.PlacePole)), ("Tank / silo", () => App.Tools.SetMode(ToolMode.PlaceTank)),
                ("Select", () => App.Tools.SetMode(ToolMode.Select)));
            Form.Note("Click in the 3D view to place. Select an object to edit it below, drag to move, R rotates by 15°, Delete removes.");

            var o = App.Tools.SelectedObstacle;
            if (o != null)
            {
                string id = o.Id;
                Form.Header("Selected: " + o.Name);
                Form.TextField("Name", () => o.Name, v => Change(id, () => o.Name = v));
                Form.Choice("Type", () => o.Type, v => Change(id, () => o.Type = v), "Building", "Tree", "Wall", "Pole", "Tank");
                Form.Number("Position X (East)", () => o.X, v => Change(id, () => o.X = v), "m", 1, -1e5, 1e5, "0.0");
                Form.Number("Position Y (North)", () => o.Z, v => Change(id, () => o.Z = v), "m", 1, -1e5, 1e5, "0.0");
                bool round = o.Type == ObstacleType.Tree || o.Type == ObstacleType.Pole || o.Type == ObstacleType.Tank;
                Form.Number(round ? "Diameter" : "Width", () => o.Width, v => Change(id, () => { o.Width = v; if (round) o.Depth = v; }), "m", 0.5, 0.1, 1000, "0.0#");
                if (!round) Form.Number("Depth", () => o.Depth, v => Change(id, () => o.Depth = v), "m", 0.5, 0.05, 1000, "0.0#");
                Form.Number("Height", () => o.Height, v => Change(id, () => o.Height = v), "m", 0.5, 0.1, 500, "0.0#");
                Form.Number("Rotation", () => o.Rotation, v => Change(id, () => o.Rotation = MathUtil.Wrap360(v)), "deg", 5, -360, 720, "0");
                Form.Number("Base elevation", () => o.BaseElevation, v => Change(id, () => o.BaseElevation = v), "m", 0.5, -50, 500, "0.0#");
                if (o.Type == ObstacleType.Tree)
                {
                    Form.Number("Trunk height", () => o.TrunkHeight, v => Change(id, () => o.TrunkHeight = v), "m", 0.5, 0, 100, "0.0#");
                    Form.Percent("Crown transparency", () => o.Transparency, v => Change(id, () => o.Transparency = MathUtil.Clamp01(v)), 5, 0, 100);
                }
                Form.Buttons(("Duplicate", () =>
                {
                    var c = Core.Serialization.ProjectSerializer.Clone(o);
                    c.Id = System.Guid.NewGuid().ToString("N");
                    c.X += o.Width + 2;
                    c.Name = o.Name + " copy";
                    App.Edit(ChangeKind.Obstacles, () => p.Obstacles.Add(c));
                }), ("Delete", () => App.Tools.DeleteSelected()));
            }

            Form.Header($"All obstacles ({p.Obstacles.Count})");
            foreach (var ob in p.Obstacles.Take(60))
            {
                var id = ob.Id;
                Form.Button($"{ob.Name}  -  {ob.Type}", () => SelectById(id));
            }
            if (p.Obstacles.Count > 60) Form.Note($"... and {p.Obstacles.Count - 60} more.");
        }

        private void Change(string id, System.Action mutate)
        {
            App.Edit(ChangeKind.Obstacles, mutate);
            SelectById(id);
        }

        private void SelectById(string id)
        {
            if (App.Scene.Obstacles.TryGetValue(id, out var go) && go != null)
                App.Tools.Select(go.GetComponentInChildren<SceneEntity>());
        }

        private string StructureKey()
        {
            var o = App.Tools.SelectedObstacle;
            var sb = new System.Text.StringBuilder();
            sb.Append(o != null ? o.Id + o.Type : "-");
            foreach (var ob in App.Project.Obstacles) sb.Append('|').Append(ob.Id).Append(ob.Name).Append(ob.Type);
            return sb.ToString();
        }

        private string _builtKey = "";

        protected override void AfterBuild() => _builtKey = StructureKey();

        public override void OnChanged(ChangeKind kind)
        {
            if (kind == ChangeKind.ProjectLoaded || (App.Project != null && kind == ChangeKind.Obstacles && StructureKey() != _builtKey)) RequestRebuild();
            else base.OnChanged(kind);
        }
    }

    public class ElectricalPanel : PanelBase
    {
        public override string Title => "Electrical";

        protected override void Build()
        {
            var cfg = App.Project.SystemDesign;
            Form.Header("Design conditions");
            Form.Number("Min. ambient temp. (Voc)", () => cfg.DesignTempMin, v => App.Edit(ChangeKind.Electrical, () => cfg.DesignTempMin = v), "°C", 1, -50, 40, "0");
            Form.Number("Hot cell temp. (Vmpp min)", () => cfg.DesignCellTempHot, v => App.Edit(ChangeKind.Electrical, () => cfg.DesignCellTempHot = v), "°C", 1, 20, 100, "0");
            Form.Number("Cold cell temp. (Vmpp max)", () => cfg.DesignCellTempMppCold, v => App.Edit(ChangeKind.Electrical, () => cfg.DesignCellTempMppCold = v), "°C", 1, -40, 40, "0");
            Form.Button("Use meteo min. temperature", () =>
            {
                if (App.Project.Meteo == null)
                {
                    App.Status("Load meteo data first.", true);
                    return;
                }
                App.Edit(ChangeKind.Electrical, () => cfg.DesignTempMin = System.Math.Floor(App.Project.Meteo.MinTemperature()));
            });

            Form.Header("Array design");
            Form.Integer("Modules in series", () => cfg.ModulesPerString, v => App.Edit(ChangeKind.Electrical, () => cfg.ModulesPerString = v), "0=auto", 0, 200);
            Form.Integer("Number of inverters", () => cfg.InverterCount, v => App.Edit(ChangeKind.Electrical, () => cfg.InverterCount = v), "0=auto", 0, 100000);
            Form.Number("Target DC/AC ratio", () => cfg.TargetDcAcRatio, v => App.Edit(ChangeKind.Electrical, () => cfg.TargetDcAcRatio = v), "", 0.05, 0.5, 2.5, "0.00");
            Form.Number("String cable section", () => cfg.StringCableSection, v => App.Edit(ChangeKind.Electrical, () => cfg.StringCableSection = v), "mm²", 2, 1.5, 35, "0.#");

            Form.Header("Sizing results");
            Form.Value("Modules per string (min / max)", () => $"{App.Sizing.ModulesPerString}  ({App.Sizing.MinModulesPerString} - {System.Math.Min(App.Sizing.MaxModulesPerString, App.Sizing.MaxModulesPerStringMppt)})");
            Form.Value("Strings / unused modules", () => $"{App.Sizing.StringCount} / {App.Sizing.UnusedModules}");
            Form.Value("Inverters", () => $"{App.Sizing.InverterCount} x {App.Inverter.PacNom:0.#} kW");
            Form.Value("Strings per inverter / MPPT", () => $"{App.Sizing.StringsPerInverter} / {App.Sizing.StringsPerMppt}");
            Form.Value("Pnom DC / AC", () => $"{App.Sizing.PnomDcKw:#,0.0} kWp / {App.Sizing.PnomAcKw:#,0.0} kW");
            Form.Value("DC/AC ratio", () => $"{App.Sizing.DcAcRatio:0.00}");
            Form.Value("Voc string at min temp.", () => $"{App.Sizing.StringVocCold:0} V (max {System.Math.Min(App.Inverter.VdcMax, App.Module.MaxSystemVoltage):0} V)");
            Form.Value("Vmpp string hot / cold", () => $"{App.Sizing.StringVmpHot:0} / {App.Sizing.StringVmpCold:0} V");
            Form.Value("MPPT window", () => $"{App.Inverter.VmpptMin:0} - {App.Inverter.VmpptMax:0} V");

            Form.Header("Checks");
            Form.Note(MessagesText, null, 14);
            Form.Button("Show strings by inverter colour", () => App.SetColorMode(ColorMode.ByInverter));
        }

        private string MessagesText()
        {
            var lines = new List<string>();
            foreach (var m in App.Sizing.Messages)
            {
                string col = m.Level == DesignMessageLevel.Error ? "#f85149" : m.Level == DesignMessageLevel.Warning ? "#e3b341" : "#3fb950";
                lines.Add($"<color={col}>{m.Level}:</color> {m.Text}");
            }
            return lines.Count == 0 ? "No messages." : string.Join("\n", lines);
        }
    }

    public class ShadingPanel : PanelBase
    {
        public override string Title => "Shading";

        protected override void Build()
        {
            var sim = App.Project.Simulation;
            Form.Header("Near shading model");
            Form.Choice("Mode", () => sim.Shading, v => App.Edit(ChangeKind.Losses, () => sim.Shading = v),
                "No shading", "Linear shadings", "According to module strings");
            Form.Percent("Electrical effect", () => sim.ElectricalEffectFraction, v => App.Edit(ChangeKind.Losses, () => sim.ElectricalEffectFraction = MathUtil.Clamp01(v)), 10, 0, 100);
            Form.Integer("Max. sample points", () => sim.MaxShadingSamples, v => App.Edit(ChangeKind.Losses, () => sim.MaxShadingSamples = v), "", 500, 200000, 1000);
            Form.Note("Shading is ray traced on the 3D scene (tables, buildings, trees ...) for a grid of sun positions, like the PVsyst shading factor table. Electrical mode treats a shaded module row as lost.");

            Form.Button("Compute shading factor table", () => App.RunShading(), true);
            Form.Value("Status", () => App.ShadingUpToDate ? "up to date" : "needs to be computed");
            Form.Value("Diffuse shading factor", () => App.Shading != null ? App.Shading.Global.DiffuseFactor.ToString("0.000") : "-");
            Form.Value("Albedo shading factor", () => App.Shading != null ? App.Shading.Global.AlbedoFactor.ToString("0.000") : "-");
            Form.Value("Sample points / objects", () => App.Shading != null ? $"{App.Shading.SampleCount} / {App.Shading.OccluderCount}" : "-");

            Form.Header("Beam loss (%) by sun height");
            Form.Table(new[] { "Height", "East", "SE", "South", "SW", "West" }, () =>
            {
                var rows = new List<string[]>();
                if (App.Shading == null) return rows;
                var t = App.Shading.Global;
                bool elec = sim.Shading == ShadingMode.Electrical;
                foreach (double el in new double[] { 5, 10, 15, 20, 30, 45, 60 })
                {
                    var r = new List<string> { el.ToString("0") + "°" };
                    foreach (double az in new double[] { 90, 135, 180, 225, 270 })
                    {
                        double az2 = App.Project.Location.Latitude >= 0 ? az : MathUtil.Wrap360(az + 180);
                        r.Add((t.BeamLoss(az2, el, elec ? ShadingMode.Electrical : ShadingMode.Linear, sim.ElectricalEffectFraction) * 100).ToString("0.0"));
                    }
                    rows.Add(r.ToArray());
                }
                return rows;
            }, new float[] { 60, 56, 56, 56, 56, 56 });

            Form.Header("Visualisation");
            Form.Buttons(("Shading heat map", () => App.SetColorMode(ColorMode.ShadingHeatmap)), ("Realistic", () => App.SetColorMode(ColorMode.Realistic)));
            Form.Buttons(("Winter solstice 9:00", () => App.Sun.SetDayAndHour(App.Project.Location.Latitude >= 0 ? 355 : 172, 9)),
                ("Winter solstice 12:00", () => App.Sun.SetDayAndHour(App.Project.Location.Latitude >= 0 ? 355 : 172, 12)));
            Form.Note("Use the time bar at the bottom (Play) to animate the shadows over the day.");
        }

        public override void OnResults()
        {
            if (Visible) Form.Refresh();
        }
    }
}
