using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PVDesigner.App;
using PVDesigner.Core.Math;
using PVDesigner.Core.Meteo;
using PVDesigner.Core.Model;
using PVDesigner.Tools;
using UnityEngine;
using UnityEngine.UI;

namespace PVDesigner.UI.Panels
{
    public class ProjectPanel : PanelBase
    {
        public override string Title => "Project";
        private string _saveAsName = "";

        protected override void Build()
        {
            var p = App.Project;
            Form.Header("Project information");
            Form.TextField("Project name", () => p.Info.Name, v => App.Edit(ChangeKind.Info, () => p.Info.Name = v));
            Form.TextField("Client", () => p.Info.Client, v => App.Edit(ChangeKind.Info, () => p.Info.Client = v));
            Form.TextField("Designer", () => p.Info.Designer, v => App.Edit(ChangeKind.Info, () => p.Info.Designer = v));
            Form.TextField("Company", () => p.Info.Company, v => App.Edit(ChangeKind.Info, () => p.Info.Company = v));
            Form.TextField("Description", () => p.Info.Description, v => App.Edit(ChangeKind.Info, () => p.Info.Description = v));
            Form.Value("File", () => string.IsNullOrEmpty(App.ProjectPath) ? "not saved yet" : Path.GetFileName(App.ProjectPath));

            Form.Header("File");
            Form.Buttons(("New", () => Window.ConfirmDiscard(App.NewProject)), ("Save", () => App.SaveProject()));
            _saveAsName = p.Info.Name;
            Form.TextField("Save as name", () => _saveAsName, v => _saveAsName = v);
            Form.Button("Save as copy", () => App.SaveProjectAs(_saveAsName));
            Form.Buttons(("Open projects folder", () => AppPaths.OpenFolder(AppPaths.Projects)),
                ("Refresh list", RequestRebuild));

            Form.Header("Recent projects");
            var files = App.ListProjects().Take(15).ToList();
            if (files.Count == 0) Form.Note("No saved projects in " + AppPaths.Projects);
            foreach (var f in files)
            {
                var path = f;
                string label = Path.GetFileNameWithoutExtension(f) + "   (" + File.GetLastWriteTime(f).ToString("yyyy-MM-dd HH:mm") + ")";
                var b = Form.Button(label, () => Window.ConfirmDiscard(() =>
                {
                    try
                    {
                        App.LoadProjectFile(path);
                    }
                    catch (Exception e)
                    {
                        Window.Dialog.Show("Open failed", e.Message);
                    }
                }));
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }

            Form.Header("Export");
            Form.Buttons(("HTML report", App.ExportReport), ("Hourly CSV", App.ExportHourlyCsv));
            Form.Button("Open reports folder", () => AppPaths.OpenFolder(AppPaths.Reports));
        }

        public override void OnChanged(ChangeKind kind)
        {
            // The recent projects list is refreshed on load; edits only refresh the bound values.
            base.OnChanged(kind);
        }
    }

    public class SitePanel : PanelBase
    {
        public override string Title => "Site";

        private static readonly (string name, double lat, double lon, double alt, double tz)[] Presets =
        {
            ("Riyadh, Saudi Arabia", 24.71, 46.68, 610, 3),
            ("Jeddah, Saudi Arabia", 21.49, 39.19, 15, 3),
            ("Dammam, Saudi Arabia", 26.43, 50.10, 10, 3),
            ("Tabuk, Saudi Arabia", 28.38, 36.57, 770, 3),
            ("Dubai, UAE", 25.20, 55.27, 5, 4),
            ("Abu Dhabi, UAE", 24.45, 54.38, 5, 4),
            ("Doha, Qatar", 25.29, 51.53, 10, 3),
            ("Kuwait City, Kuwait", 29.37, 47.98, 15, 3),
            ("Manama, Bahrain", 26.23, 50.59, 5, 3),
            ("Muscat, Oman", 23.59, 58.41, 15, 4),
            ("Amman, Jordan", 31.95, 35.93, 780, 3),
            ("Baghdad, Iraq", 33.31, 44.36, 34, 3),
            ("Erbil, Iraq", 36.19, 44.01, 420, 3),
            ("Damascus, Syria", 33.51, 36.29, 690, 3),
            ("Beirut, Lebanon", 33.89, 35.50, 50, 2),
            ("Cairo, Egypt", 30.04, 31.24, 23, 2),
            ("Aswan, Egypt", 24.09, 32.90, 190, 2),
            ("Khartoum, Sudan", 15.50, 32.56, 380, 2),
            ("Tripoli, Libya", 32.89, 13.19, 20, 2),
            ("Tunis, Tunisia", 36.81, 10.18, 10, 1),
            ("Algiers, Algeria", 36.75, 3.06, 20, 1),
            ("Casablanca, Morocco", 33.57, -7.59, 50, 1),
            ("Ouarzazate, Morocco", 30.92, -6.89, 1150, 1),
            ("Sanaa, Yemen", 15.37, 44.19, 2250, 3),
            ("Istanbul, Turkey", 41.01, 28.98, 40, 3),
            ("Karachi, Pakistan", 24.86, 67.01, 10, 5),
            ("Mumbai, India", 19.08, 72.88, 15, 5.5),
            ("Madrid, Spain", 40.42, -3.70, 650, 1),
            ("Berlin, Germany", 52.52, 13.40, 35, 1),
            ("London, UK", 51.51, -0.13, 20, 0),
            ("Phoenix, USA", 33.45, -112.07, 330, -7),
            ("Johannesburg, South Africa", -26.20, 28.05, 1750, 2),
            ("Sydney, Australia", -33.87, 151.21, 40, 10)
        };

        private int _preset;
        private double _rectWidth = 200, _rectDepth = 150;

        protected override void Build()
        {
            var p = App.Project;
            var loc = p.Location;
            Form.Header("Geographical location");
            Form.TextField("Site name", () => loc.Name, v => App.Edit(ChangeKind.Site, () => loc.Name = v));
            Form.Number("Latitude", () => loc.Latitude, v => App.Edit(ChangeKind.Site, () => loc.Latitude = v), "deg N", 0.1, -89, 89, "0.0000");
            Form.Number("Longitude", () => loc.Longitude, v => App.Edit(ChangeKind.Site, () => loc.Longitude = v), "deg E", 0.1, -180, 180, "0.0000");
            Form.Number("Altitude", () => loc.Altitude, v => App.Edit(ChangeKind.Site, () => loc.Altitude = v), "m", 10, -400, 6000, "0");
            Form.Number("Time zone", () => loc.TimeZone, v => App.Edit(ChangeKind.Site, () => loc.TimeZone = v), "h", 0.5, -12, 14, "0.##");
            Form.Number("Albedo", () => loc.Albedo, v => App.Edit(ChangeKind.Site, () => loc.Albedo = v), "", 0.05, 0, 1, "0.00");
            Form.Note("Albedo: 0.2 typical soil, 0.3 desert sand, 0.6 fresh snow.");

            Form.Choice("City preset", () => Presets.Select(x => x.name).ToList(), () => _preset, i => _preset = i);
            Form.Button("Apply preset location", () =>
            {
                var c = Presets[_preset];
                App.Edit(ChangeKind.Site, () =>
                {
                    loc.Name = c.name;
                    loc.Latitude = c.lat;
                    loc.Longitude = c.lon;
                    loc.Altitude = c.alt;
                    loc.TimeZone = c.tz;
                    foreach (var f in p.Fields)
                    {
                        if (f.Mounting != MountingType.FixedTilt) continue;
                        f.Azimuth = c.lat >= 0 ? 0 : 180;
                    }
                });
                App.Status("Location set to " + c.name + ". Download meteo data in the Meteo tab.");
            });

            Form.Header("Site boundary");
            Form.Value("Area", () => p.SiteBoundary.Count >= 3 ? $"{Polygon2D.Area(p.SiteBoundary) / 10000.0:0.000} ha  ({Polygon2D.Area(p.SiteBoundary):#,0} m²)" : "-");
            Form.Value("Perimeter", () => p.SiteBoundary.Count >= 3 ? $"{Polygon2D.Perimeter(p.SiteBoundary):#,0.0} m" : "-");
            Form.Value("Vertices", () => p.SiteBoundary.Count.ToString());
            Form.Number("Setback from boundary", () => p.BoundarySetback, v => App.Edit(ChangeKind.Boundary, () => p.BoundarySetback = v), "m", 1, 0, 200, "0.#");
            Form.Button("Draw site boundary in 3D view", () => App.Tools.SetMode(ToolMode.DrawSiteBoundary), true);
            Form.Note("Click points on the ground, right click or Enter to close. Points snap to 0.5 m.");

            Form.Header("Rectangular site");
            Form.Number("Width (E-W)", () => _rectWidth, v => _rectWidth = v, "m", 10, 5, 20000, "0");
            Form.Number("Depth (N-S)", () => _rectDepth, v => _rectDepth = v, "m", 10, 5, 20000, "0");
            Form.Button("Create rectangular boundary", () =>
            {
                double w = _rectWidth / 2, d = _rectDepth / 2;
                App.Edit(ChangeKind.Boundary, () => p.SiteBoundary = new List<Vec2> { new Vec2(-w, -d), new Vec2(w, -d), new Vec2(w, d), new Vec2(-w, d) });
                App.FrameSite(false);
            });
        }

        public override void OnChanged(ChangeKind kind)
        {
            if (kind == ChangeKind.ProjectLoaded) RequestRebuild();
            else if (Visible) Form.Refresh();
        }
    }

    public class MeteoPanel : PanelBase
    {
        public override string Title => "Meteo";
        private double _clearSky = 0.75;

        protected override void Build()
        {
            var p = App.Project;
            Form.Header("Meteo data");
            Form.Value("Source", () => p.Meteo == null ? "none" : p.Meteo.Source);
            Form.Note(() => p.Meteo == null ? "No meteo data loaded yet." : p.Meteo.Description);
            Form.Value("Annual GHI", () => p.Meteo == null ? "-" : $"{p.Meteo.AnnualGhiKWh():#,0} kWh/m²");
            Form.Value("Temperature min / max", () => p.Meteo == null ? "-" : $"{p.Meteo.MinTemperature():0.0} / {p.Meteo.MaxTemperature():0.0} °C");

            Form.Header("Import");
            Form.Button("Download PVGIS TMY (internet)", App.LoadPvgis, true);
            Form.Note("Typical meteorological year from the EU JRC PVGIS database (satellite data, worldwide coverage except polar regions). Uses the site latitude / longitude.");
            Form.Number("Clear sky ratio", () => _clearSky, v => _clearSky = v, "", 0.05, 0.3, 0.95, "0.00");
            Form.Button("Generate synthetic year (offline)", () => App.GenerateSyntheticMeteo(_clearSky));
            Form.Note("Synthetic data is approximate and only meant for previews without internet access.");

            Form.Header("Monthly values");
            Form.Table(new[] { "Month", "GHI", "DHI", "T amb", "Wind" }, () =>
            {
                var rows = new List<string[]>();
                if (p.Meteo == null) return rows;
                var m = p.Meteo.MonthlySummary(p.Location.TimeZone);
                double g = 0, d = 0, t = 0, w = 0;
                foreach (var x in m)
                {
                    rows.Add(new[] { MonthlyMeteoSummary.MonthNames[x.Month - 1], x.GlobalHorizontal.ToString("0.0"), x.DiffuseHorizontal.ToString("0.0"), x.TempAmbient.ToString("0.0"), x.WindSpeed.ToString("0.0") });
                    g += x.GlobalHorizontal;
                    d += x.DiffuseHorizontal;
                    t += x.TempAmbient / 12;
                    w += x.WindSpeed / 12;
                }
                rows.Add(new[] { "Year", g.ToString("0"), d.ToString("0"), t.ToString("0.0"), w.ToString("0.0") });
                return rows;
            }, new float[] { 70, 70, 70, 60, 60 });
            Form.Note("GHI / DHI in kWh/m², temperature in °C, wind in m/s.");
        }
    }

    public class ComponentsPanel : PanelBase
    {
        public override string Title => "Components";

        protected override void Build()
        {
            var p = App.Project;
            Form.Header("PV module");
            Form.Choice("Module", () => App.Library.Modules.Select(m => m.DisplayName).ToList(),
                () => App.Library.Modules.FindIndex(m => m.Id == App.Module.Id),
                i => App.Edit(ChangeKind.Components, () => p.SystemDesign.ModuleId = App.Library.Modules[i].Id));
            Form.Value("Technology", () => App.Module.Technology.ToString());
            Form.Value("Pnom / efficiency", () => $"{App.Module.Pmax:0} Wp / {App.Module.Efficiency * 100:0.0} %");
            Form.Value("Vmp / Imp", () => $"{App.Module.Vmp:0.0} V / {App.Module.Imp:0.00} A");
            Form.Value("Voc / Isc", () => $"{App.Module.Voc:0.0} V / {App.Module.Isc:0.00} A");
            Form.Value("Temp. coeff. Pmax / Voc", () => $"{App.Module.TempCoeffPmax:0.00} / {App.Module.TempCoeffVoc:0.00} %/°C");
            Form.Value("Dimensions", () => $"{App.Module.Length:0.000} x {App.Module.Width:0.000} m");
            Form.Value("Max. system voltage", () => $"{App.Module.MaxSystemVoltage:0} V");

            Form.Header("Inverter");
            Form.Choice("Inverter", () => App.Library.Inverters.Select(m => m.DisplayName).ToList(),
                () => App.Library.Inverters.FindIndex(m => m.Id == App.Inverter.Id),
                i => App.Edit(ChangeKind.Electrical, () => p.SystemDesign.InverterId = App.Library.Inverters[i].Id));
            Form.Value("Type", () => App.Inverter.Type.ToString());
            Form.Value("Pnom / Pmax AC", () => $"{App.Inverter.PacNom:0.#} / {App.Inverter.PacMax:0.#} kW");
            Form.Value("MPPT window", () => $"{App.Inverter.VmpptMin:0} - {App.Inverter.VmpptMax:0} V");
            Form.Value("Max. DC voltage", () => $"{App.Inverter.VdcMax:0} V");
            Form.Value("MPPT inputs / Imax", () => $"{App.Inverter.MpptCount} x {App.Inverter.ImaxPerMppt:0.#} A");
            Form.Value("Efficiency max / euro", () => $"{App.Inverter.MaxEfficiency:0.0} / {App.Inverter.EuroEfficiency:0.0} %");

            Form.Header("PVsyst component files");
            Form.Note("Copy manufacturer .PAN (modules) and .OND (inverters) files into the components folder, then import them. Imported components are stored inside the project file.");
            Form.Buttons(("Import PAN / OND", App.ImportComponentFiles), ("Open folder", () => AppPaths.OpenFolder(AppPaths.Components)));
            Form.Value("Imported modules / inverters", () => $"{p.CustomModules.Count} / {p.CustomInverters.Count}");
        }

        public override void OnChanged(ChangeKind kind)
        {
            if (kind == ChangeKind.ProjectLoaded || kind == ChangeKind.Components) RequestRebuild();
            else base.OnChanged(kind);
        }
    }
}
