using System.Collections.Generic;
using System.Linq;
using PVDesigner.App;
using PVDesigner.Core.Meteo;
using PVDesigner.Core.Model;
using PVDesigner.Core.Simulation;
using PVDesigner.Core.Solar;
using PVDesigner.Rendering;
using UnityEngine;

namespace PVDesigner.UI.Panels
{
    public class LossesPanel : PanelBase
    {
        public override string Title => "Losses";

        protected override void Build()
        {
            var l = App.Project.Losses;
            var s = App.Project.Simulation;
            Form.Header("Irradiance model");
            Form.Choice("Transposition", () => s.Transposition, v => App.Edit(ChangeKind.Losses, () => s.Transposition = v), "Perez", "Hay-Davies", "Isotropic");
            Form.Number("IAM b0 (ASHRAE)", () => l.IamB0, v => App.Edit(ChangeKind.Losses, () => l.IamB0 = v), "", 0.01, 0, 0.2, "0.000");

            Form.Header("Thermal model (Faiman)");
            Form.Number("Uc", () => l.ThermalUc, v => App.Edit(ChangeKind.Losses, () => l.ThermalUc = v), "W/m²K", 1, 5, 60, "0.#");
            Form.Number("Uv", () => l.ThermalUv, v => App.Edit(ChangeKind.Losses, () => l.ThermalUv = v), "W/m²K/m/s", 0.5, 0, 10, "0.#");
            Form.Buttons(("Free standing (29)", () => App.Edit(ChangeKind.Losses, () => { l.ThermalUc = 29; l.ThermalUv = 0; })),
                ("Semi-integrated (20)", () => App.Edit(ChangeKind.Losses, () => { l.ThermalUc = 20; l.ThermalUv = 0; })),
                ("Integrated (15)", () => App.Edit(ChangeKind.Losses, () => { l.ThermalUc = 15; l.ThermalUv = 0; })));

            Form.Header("Soiling");
            Form.Percent("Soiling (all months)", () => l.MonthlySoiling.Average(), v => App.Edit(ChangeKind.Losses, () =>
            {
                for (int i = 0; i < 12; i++) l.MonthlySoiling[i] = v;
            }), 0.5, 0, 50);
            for (int m = 0; m < 12; m++)
            {
                int month = m;
                Form.Percent("  " + MonthlyMeteoSummary.MonthNames[m], () => l.MonthlySoiling[month], v => App.Edit(ChangeKind.Losses, () => l.MonthlySoiling[month] = v), 0.5, 0, 50);
            }

            Form.Header("Module and array");
            Form.Percent("Module quality", () => l.ModuleQuality, v => App.Edit(ChangeKind.Losses, () => l.ModuleQuality = v), 0.1, -5, 10);
            Form.Percent("LID", () => l.Lid, v => App.Edit(ChangeKind.Losses, () => l.Lid = v), 0.1, 0, 10);
            Form.Percent("Mismatch", () => l.Mismatch, v => App.Edit(ChangeKind.Losses, () => l.Mismatch = v), 0.1, 0, 10);
            Form.Percent("DC ohmic loss at STC", () => l.DcOhmicAtStc, v => App.Edit(ChangeKind.Losses, () => l.DcOhmicAtStc = v), 0.1, 0, 10);

            Form.Header("AC and system");
            Form.Percent("AC ohmic loss at Pnom", () => l.AcOhmicAtNominal, v => App.Edit(ChangeKind.Losses, () => l.AcOhmicAtNominal = v), 0.1, 0, 10);
            Form.Percent("MV transformer loss", () => l.TransformerAtNominal, v => App.Edit(ChangeKind.Losses, () => l.TransformerAtNominal = v), 0.1, 0, 10);
            Form.Percent("Unavailability", () => l.Unavailability, v => App.Edit(ChangeKind.Losses, () => l.Unavailability = v), 0.1, 0, 20);
            Form.Percent("Annual degradation", () => l.AnnualDegradation, v => App.Edit(ChangeKind.Losses, () => l.AnnualDegradation = v), 0.05, 0, 5);
        }
    }

    public class ResultsPanel : PanelBase
    {
        public override string Title => "Results";
        private BarChart _chart;

        protected override void Build()
        {
            Form.Button("Run simulation", App.RunSimulation, true);
            var r = App.Simulation;
            if (r == null)
            {
                Form.Note("No results yet. The simulation computes near shading if needed, then runs 8760 hourly steps.", Theme.Muted, 14);
                return;
            }

            Form.Header("Main results");
            Form.Value("Produced energy", () => $"{r.Annual.EGrid / 1000:#,0.0} MWh/year");
            Form.Value("Specific production", () => $"{r.SpecificYield:#,0} kWh/kWp/year");
            Form.Value("Performance ratio", () => $"{r.PerformanceRatio * 100:0.00} %");
            Form.Value("System power", () => $"{r.PnomDcKw:#,0.0} kWp / {r.PnomAcKw:#,0.0} kWac");
            Form.Value("Global incident in plane", () => $"{r.Annual.GlobInc:#,0} kWh/m²");
            Form.Value("Capacity factor", () => $"{r.CapacityFactor * 100:0.0} %");

            _chart = new BarChart(Form.Root, 150, "Energy injected into grid (MWh)");
            _chart.SetData(r.Monthly.Select(m => m.EGrid / 1000.0).ToList(), MonthlyMeteoSummary.MonthNames.Select(n => n.Substring(0, 1)).ToList(), Theme.Accent, "#,0");

            Form.Header("Monthly balance");
            Form.Table(new[] { "", "GlobHor", "GlobInc", "EArray", "E_Grid", "PR" }, () =>
            {
                var rows = new List<string[]>();
                foreach (var m in r.Monthly)
                    rows.Add(new[] { MonthlyMeteoSummary.MonthNames[m.Month - 1], m.GlobHor.ToString("0.0"), m.GlobInc.ToString("0.0"), (m.EArray / 1000).ToString("#,0.0"), (m.EGrid / 1000).ToString("#,0.0"), (m.PR * 100).ToString("0.0") });
                var a = r.Annual;
                rows.Add(new[] { "Year", a.GlobHor.ToString("0"), a.GlobInc.ToString("0"), (a.EArray / 1000).ToString("#,0"), (a.EGrid / 1000).ToString("#,0"), (a.PR * 100).ToString("0.0") });
                return rows;
            }, new float[] { 44, 60, 60, 66, 66, 46 });
            Form.Note("Irradiation in kWh/m², energy in MWh, PR in %.");

            Form.Header("Loss diagram");
            foreach (var l in r.Losses)
            {
                if (l.IsMilestone)
                {
                    string v = l.Kind == LossKind.Irradiation ? $"{l.Value:#,0.0} kWh/m²" : $"{l.Value / 1000:#,0.0} MWh";
                    Form.Value("<b>" + l.Label + "</b>", () => v);
                }
                else
                {
                    var color = l.Percent < 0 ? "#f85149" : "#3fb950";
                    string txt = $"<color={color}>{(l.Percent >= 0 ? "+" : "")}{l.Percent:0.00} %</color>";
                    Form.Value("   " + l.Label, () => txt);
                }
            }

            if (r.Warnings.Count > 0)
            {
                Form.Header("Warnings");
                foreach (var w in r.Warnings) Form.Note(w, Theme.Warning);
            }

            Form.Header("Export");
            Form.Buttons(("HTML report", App.ExportReport), ("Hourly CSV", App.ExportHourlyCsv));
            Form.Note($"Computed in {r.ComputeSeconds:0.00} s with {r.MeteoDescription}.");
        }

        public override void OnResults() => RequestRebuild();
    }

    public class EconomicsPanel : PanelBase
    {
        public override string Title => "Economics";

        protected override void Build()
        {
            var e = App.Project.Economics;
            Form.Header("Assumptions");
            Form.TextField("Currency", () => e.Currency, v => App.Edit(ChangeKind.Economics, () => e.Currency = v));
            Form.Number("Investment", () => e.CapexPerWp, v => App.Edit(ChangeKind.Economics, () => e.CapexPerWp = v), "/Wp", 0.01, 0, 10, "0.000");
            Form.Percent("O&M (of CAPEX / year)", () => e.OpexFractionOfCapex, v => App.Edit(ChangeKind.Economics, () => e.OpexFractionOfCapex = v), 0.1, 0, 20);
            Form.Number("Energy price", () => e.TariffPerKwh, v => App.Edit(ChangeKind.Economics, () => e.TariffPerKwh = v), "/kWh", 0.005, 0, 5, "0.0000");
            Form.Percent("Price escalation", () => e.TariffEscalation, v => App.Edit(ChangeKind.Economics, () => e.TariffEscalation = v), 0.5, -10, 20);
            Form.Percent("O&M escalation", () => e.OpexEscalation, v => App.Edit(ChangeKind.Economics, () => e.OpexEscalation = v), 0.5, -10, 20);
            Form.Percent("Discount rate", () => e.DiscountRate, v => App.Edit(ChangeKind.Economics, () => e.DiscountRate = v), 0.5, 0, 30);
            Form.Integer("Lifetime", () => e.LifetimeYears, v => App.Edit(ChangeKind.Economics, () => e.LifetimeYears = v), "years", 1, 60);

            Form.Header("Results");
            Form.Value("Investment", () => App.Economics != null ? $"{App.Economics.Capex:#,0} {e.Currency}" : "run the simulation");
            Form.Value("LCOE", () => App.Economics != null ? $"{App.Economics.Lcoe:0.0000} {e.Currency}/kWh" : "-");
            Form.Value("NPV", () => App.Economics != null ? $"{App.Economics.Npv:#,0} {e.Currency}" : "-");
            Form.Value("IRR", () => App.Economics != null && !double.IsNaN(App.Economics.Irr) ? $"{App.Economics.Irr * 100:0.0} %" : "-");
            Form.Value("Payback", () => App.Economics != null && !double.IsNaN(App.Economics.PaybackYears) ? $"{App.Economics.PaybackYears:0.0} years" : "-");
            Form.Table(new[] { "Year", "Energy MWh", "Cash flow", "Cumulative" }, () =>
            {
                var rows = new List<string[]>();
                if (App.Economics == null) return rows;
                foreach (var y in App.Economics.Years.Where(y => y.Year <= 5 || y.Year % 5 == 0))
                    rows.Add(new[] { y.Year.ToString(), (y.EnergyKwh / 1000).ToString("#,0"), y.CashFlow.ToString("#,0"), y.Cumulative.ToString("#,0") });
                return rows;
            }, new float[] { 50, 90, 100, 110 });
        }
    }

    public class SettingsPanel : PanelBase
    {
        public override string Title => "Settings";

        protected override void Build()
        {
            Form.Header("View");
            Form.Choice("Ground", () => MaterialLibrary.GroundNames, () => AppSettings.GroundStyle, i =>
            {
                AppSettings.GroundStyle = i;
                App.Scene.Materials.SetGroundStyle(i);
            });
            Form.Toggle("Soft shadows", () => AppSettings.SoftShadows, v =>
            {
                AppSettings.SoftShadows = v;
                App.Sun.Recompute();
            });
            bool path = true;
            Form.Toggle("Sun path", () => path, v =>
            {
                path = v;
                App.Sun.ShowSunPath(v);
            });
            Form.Number("UI scale", () => AppSettings.UiScale, v =>
            {
                AppSettings.UiScale = (float)v;
                Window.ApplyUiScale();
            }, "x", 0.1, 0.6, 2.0, "0.0");
            Form.Number("Grid snap", () => App.Tools.GridSnap, v => App.Tools.GridSnap = (float)v, "m", 0.5, 0, 50, "0.0#");
            Form.Number("Animation speed", () => App.Sun.AnimationSpeed, v => App.Sun.AnimationSpeed = (float)v, "h/s", 0.5, 0.1, 24, "0.0");

            Form.Header("Virtual reality");
            Form.Note("Supports OpenXR headsets: Meta Quest (Link / Air Link), Valve Index, HTC Vive, Pico, Windows Mixed Reality. " +
                      "Right trigger: click / teleport. Grip: grab the model. A: walk / table-top mode. Left stick: time of day / date. Right stick: turn. Menu or Y: show the panel.");
            Form.Button("Enter / exit VR", () => App.VR.Toggle(), true);
            Form.Value("VR status", () => App.VR.StatusText);

            Form.Header("Updates");
            Form.Value("Installed version", () => App.Version);
            Form.Toggle("Check at startup", () => AppSettings.CheckUpdatesOnStartup, v => AppSettings.CheckUpdatesOnStartup = v);
            Form.Toggle("Include beta versions", () => AppSettings.IncludePreReleases, v => AppSettings.IncludePreReleases = v);
            Form.Button("Check for updates now", () => App.Updater.CheckForUpdates(true));
            Form.Value("Update status", () => App.Updater.StatusText);
            Form.Note("Updates are published as GitHub releases of " + Updates.UpdateConfig.Owner + "/" + Updates.UpdateConfig.Repository + ".");

            Form.Header("Data folders");
            Form.Note(AppPaths.Root);
            Form.Button("Open data folder", () => AppPaths.OpenFolder(AppPaths.Root));

            Form.Header("About");
            Form.Note("PV Designer - 3D PV system design and hourly yield simulation (solar geometry: NOAA, transposition: Perez 1990, meteo: PVGIS). " +
                      "Results are estimates; validate bankable studies with measured data.");
        }

        public override void OnResults()
        {
            if (Visible) Form.Refresh();
        }
    }
}
