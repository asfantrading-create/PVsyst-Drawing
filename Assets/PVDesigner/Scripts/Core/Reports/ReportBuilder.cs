using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using PVDesigner.Core.Components;
using PVDesigner.Core.Economics;
using PVDesigner.Core.Electrical;
using PVDesigner.Core.Layout;
using PVDesigner.Core.Math;
using PVDesigner.Core.Meteo;
using PVDesigner.Core.Model;
using PVDesigner.Core.Shading;
using PVDesigner.Core.Simulation;

namespace PVDesigner.Core.Reports
{
    /// <summary>Builds a self contained, print ready HTML simulation report.</summary>
    public static class ReportBuilder
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public class Input
        {
            public PVProject Project;
            public PVModuleSpec Module;
            public InverterSpec Inverter;
            public SimulationResult Simulation;
            public ShadingResult Shading;
            public EconomicResult Economics;
            public List<BomLine> Bom;
            /// <summary>Optional PNG screenshot of the 3D scene.</summary>
            public byte[] ScreenshotPng;
            public string AppVersion = "";
        }

        private static string E(string s) => WebUtility.HtmlEncode(s ?? "");
        private static string F(double v, string fmt) => double.IsNaN(v) || double.IsInfinity(v) ? "-" : v.ToString(fmt, Inv);

        public static string BuildHtml(Input input)
        {
            var p = input.Project;
            var sim = input.Simulation;
            var sb = new StringBuilder(64 * 1024);
            sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>").Append(E(p.Info.Name)).Append(" - PV simulation report</title>");
            sb.Append("<style>");
            sb.Append(@"
:root{--ink:#1d2733;--muted:#5d6b7a;--line:#d9e0e7;--accent:#0b6bcb;--accent2:#f2a900;--bg:#ffffff;--soft:#f4f7fa}
*{box-sizing:border-box}body{font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:var(--ink);background:var(--bg);margin:0;padding:24px;max-width:1100px;margin:auto;font-size:14px}
h1{font-size:24px;margin:0}h2{font-size:17px;border-bottom:2px solid var(--accent);padding-bottom:4px;margin-top:28px}
.header{display:flex;justify-content:space-between;align-items:flex-end;border-bottom:3px solid var(--ink);padding-bottom:10px}
.muted{color:var(--muted)}table{border-collapse:collapse;width:100%;margin:8px 0}th,td{border:1px solid var(--line);padding:5px 8px;text-align:right}
th{background:var(--soft);font-weight:600}td:first-child,th:first-child{text-align:left}
.kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:10px;margin:12px 0}
.kpi{background:var(--soft);border-left:4px solid var(--accent);padding:10px 12px}.kpi b{display:block;font-size:20px}
.grid2{display:grid;grid-template-columns:1fr 1fr;gap:18px}@media(max-width:800px){.grid2{grid-template-columns:1fr}}
.warn{background:#fff6e0;border-left:4px solid var(--accent2);padding:8px 12px;margin:4px 0}
.loss td{text-align:left}.loss .bar{height:10px;background:var(--accent);display:inline-block;vertical-align:middle}
.loss .neg{background:#d9534f}.loss .pos{background:#3c9d5d}.ms{font-weight:600;background:var(--soft)}
img.shot{width:100%;border:1px solid var(--line)}
@media print{body{padding:0}h2{break-after:avoid}table,.kpis,svg{break-inside:avoid}}
");
            sb.Append("</style></head><body>");

            sb.Append("<div class=\"header\"><div><h1>").Append(E(p.Info.Name)).Append("</h1><div class=\"muted\">Grid-connected PV system - simulation report</div></div>");
            sb.Append("<div class=\"muted\" style=\"text-align:right\">");
            if (!string.IsNullOrEmpty(p.Info.Company)) sb.Append(E(p.Info.Company)).Append("<br>");
            if (!string.IsNullOrEmpty(p.Info.Designer)) sb.Append("Designer: ").Append(E(p.Info.Designer)).Append("<br>");
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv));
            if (!string.IsNullOrEmpty(input.AppVersion)) sb.Append("<br>PV Designer ").Append(E(input.AppVersion));
            sb.Append("</div></div>");

            if (sim != null)
            {
                sb.Append("<div class=\"kpis\">");
                Kpi(sb, "Produced energy", F(sim.Annual.EGrid / 1000.0, "#,0.0") + " MWh/year");
                Kpi(sb, "Specific production", F(sim.SpecificYield, "#,0") + " kWh/kWp/year");
                Kpi(sb, "Performance ratio", F(sim.PerformanceRatio * 100, "0.00") + " %");
                Kpi(sb, "System power", F(sim.PnomDcKw, "#,0.0") + " kWp / " + F(sim.PnomAcKw, "#,0.0") + " kWac");
                sb.Append("</div>");
            }

            // Project & site
            sb.Append("<h2>Project and site</h2><div class=\"grid2\"><table>");
            Row(sb, "Client", E(p.Info.Client));
            Row(sb, "Site", E(p.Location.Name));
            Row(sb, "Latitude / Longitude", F(p.Location.Latitude, "0.0000") + " / " + F(p.Location.Longitude, "0.0000"));
            Row(sb, "Altitude", F(p.Location.Altitude, "0") + " m");
            Row(sb, "Time zone", "UTC" + (p.Location.TimeZone >= 0 ? "+" : "") + F(p.Location.TimeZone, "0.##"));
            Row(sb, "Albedo", F(p.Location.Albedo, "0.00"));
            sb.Append("</table><table>");
            Row(sb, "Meteo data", E(p.Meteo?.Description ?? "none"));
            if (p.SiteBoundary.Count >= 3)
            {
                Row(sb, "Site area", F(Polygon2D.Area(p.SiteBoundary) / 10000.0, "0.00") + " ha");
                Row(sb, "Perimeter", F(Polygon2D.Perimeter(p.SiteBoundary), "0") + " m");
            }
            Row(sb, "Obstacles (near shading)", p.Obstacles.Count.ToString(Inv));
            if (!string.IsNullOrEmpty(p.Info.Description)) Row(sb, "Description", E(p.Info.Description));
            sb.Append("</table></div>");

            if (input.ScreenshotPng != null && input.ScreenshotPng.Length > 0)
            {
                sb.Append("<h2>3D scene</h2><img class=\"shot\" alt=\"3D scene\" src=\"data:image/png;base64,")
                  .Append(Convert.ToBase64String(input.ScreenshotPng)).Append("\">");
            }

            // System
            var sz = sim?.Sizing;
            sb.Append("<h2>PV system</h2><div class=\"grid2\"><table>");
            if (input.Module != null)
            {
                var m = input.Module;
                Row(sb, "PV module", E(m.Manufacturer + " " + m.Model));
                Row(sb, "Pnom / efficiency", F(m.Pmax, "0") + " Wp / " + F(m.Efficiency * 100, "0.0") + " %");
                Row(sb, "Vmp / Imp", F(m.Vmp, "0.0") + " V / " + F(m.Imp, "0.00") + " A");
                Row(sb, "Voc / Isc", F(m.Voc, "0.0") + " V / " + F(m.Isc, "0.00") + " A");
                Row(sb, "Temp. coeff. Pmax", F(m.TempCoeffPmax, "0.00") + " %/&deg;C");
                Row(sb, "Dimensions", F(m.Length, "0.000") + " x " + F(m.Width, "0.000") + " m");
            }
            sb.Append("</table><table>");
            if (input.Inverter != null)
            {
                var inv = input.Inverter;
                Row(sb, "Inverter", E(inv.Manufacturer + " " + inv.Model));
                Row(sb, "Pnom AC", F(inv.PacNom, "0.#") + " kW");
                Row(sb, "MPPT window", F(inv.VmpptMin, "0") + " - " + F(inv.VmpptMax, "0") + " V");
                Row(sb, "Max. DC voltage", F(inv.VdcMax, "0") + " V");
                Row(sb, "MPPT inputs", inv.MpptCount.ToString(Inv));
                Row(sb, "Max / Euro efficiency", F(inv.MaxEfficiency, "0.0") + " / " + F(inv.EuroEfficiency, "0.0") + " %");
            }
            sb.Append("</table></div>");
            if (sz != null)
            {
                sb.Append("<table><tr><th>Strings</th><th>Modules / string</th><th>Inverters</th><th>Strings / inverter</th><th>Pnom DC</th><th>Pnom AC</th><th>DC/AC</th><th>Voc string (cold)</th><th>Vmpp string (hot)</th></tr><tr>");
                sb.Append("<td>").Append(sz.StringCount).Append("</td><td>").Append(sz.ModulesPerString).Append("</td><td>")
                  .Append(sz.InverterCount).Append("</td><td>").Append(sz.StringsPerInverter).Append("</td><td>")
                  .Append(F(sz.PnomDcKw, "#,0.0")).Append(" kWp</td><td>").Append(F(sz.PnomAcKw, "#,0.0")).Append(" kW</td><td>")
                  .Append(F(sz.DcAcRatio, "0.00")).Append("</td><td>").Append(F(sz.StringVocCold, "0")).Append(" V</td><td>")
                  .Append(F(sz.StringVmpHot, "0")).Append(" V</td></tr></table>");
            }

            // Fields
            sb.Append("<h2>Array layout</h2><table><tr><th>Field</th><th>Mounting</th><th>Tilt / Azimuth</th><th>Tables</th><th>Modules</th><th>Table</th><th>Pitch</th><th>GCR</th></tr>");
            foreach (var f in p.Fields)
            {
                string tilt = f.Mounting == MountingType.SingleAxisTracker
                    ? "&plusmn;" + F(f.TrackerMaxAngle, "0") + "&deg; / axis " + F(f.Azimuth, "0") + "&deg;"
                    : F(f.Tilt, "0.#") + "&deg; / " + F(f.Azimuth, "0") + "&deg;";
                double gcr = input.Module != null ? TableGeometry.Gcr(f, input.Module) : 0;
                sb.Append("<tr><td>").Append(E(f.Name)).Append("</td><td>")
                  .Append(f.Mounting == MountingType.SingleAxisTracker ? "1-axis tracker" + (f.Backtracking ? " (backtracking)" : "") : "Fixed tilt")
                  .Append("</td><td>").Append(tilt).Append("</td><td>").Append(f.Tables.Count).Append("</td><td>").Append(f.ModuleCount)
                  .Append("</td><td>").Append(f.ModulesUp).Append(f.Orientation == ModuleOrientation.Portrait ? "P" : "L").Append(" x ").Append(f.ModulesAcross)
                  .Append("</td><td>").Append(F(f.Pitch, "0.00")).Append(" m</td><td>").Append(F(gcr * 100, "0.0")).Append(" %</td></tr>");
            }
            sb.Append("</table>");

            // Shading
            if (input.Shading != null && p.Simulation.Shading != ShadingMode.None)
            {
                var g = input.Shading.Global;
                sb.Append("<h2>Near shadings</h2><table>");
                Row(sb, "Mode", p.Simulation.Shading == ShadingMode.Electrical
                    ? "According to module strings (" + F(p.Simulation.ElectricalEffectFraction * 100, "0") + " % electrical effect)"
                    : "Linear shadings");
                Row(sb, "Shading factor on diffuse", F(g.DiffuseFactor, "0.000"));
                Row(sb, "Shading factor on albedo", F(g.AlbedoFactor, "0.000"));
                Row(sb, "Sample points / objects", input.Shading.SampleCount + " / " + input.Shading.OccluderCount);
                sb.Append("</table>");
                AppendShadingTable(sb, g, p.Simulation.Shading == ShadingMode.Electrical);
            }

            if (sim != null)
            {
                sb.Append("<h2>Main results</h2>");
                AppendMonthlyTable(sb, sim);
                AppendMonthlyChart(sb, sim);
                sb.Append("<h2>Loss diagram</h2>");
                AppendLossDiagram(sb, sim);

                sb.Append("<h2>Long term production</h2><table><tr><th>Year</th><th>Energy (MWh)</th><th>Year</th><th>Energy (MWh)</th><th>Year</th><th>Energy (MWh)</th></tr>");
                int n = sim.YearlyEnergy.Length;
                int rows = (n + 2) / 3;
                for (int r = 0; r < rows; r++)
                {
                    sb.Append("<tr>");
                    for (int c = 0; c < 3; c++)
                    {
                        int y = r + c * rows;
                        if (y < n) sb.Append("<td>").Append(y + 1).Append("</td><td>").Append(F(sim.YearlyEnergy[y] / 1000.0, "#,0.0")).Append("</td>");
                        else sb.Append("<td></td><td></td>");
                    }
                    sb.Append("</tr>");
                }
                sb.Append("</table>");
            }

            if (input.Economics != null)
            {
                var ec = input.Economics;
                var ep = p.Economics;
                string cur = E(ep.Currency);
                sb.Append("<h2>Economic evaluation</h2><div class=\"kpis\">");
                Kpi(sb, "Investment", F(ec.Capex, "#,0") + " " + cur);
                Kpi(sb, "LCOE", F(ec.Lcoe, "0.0000") + " " + cur + "/kWh");
                Kpi(sb, "NPV", F(ec.Npv, "#,0") + " " + cur);
                Kpi(sb, "Payback / IRR", F(ec.PaybackYears, "0.0") + " y / " + F(ec.Irr * 100, "0.0") + " %");
                sb.Append("</div><p class=\"muted\">Assumptions: ").Append(F(ep.CapexPerWp, "0.###")).Append(" ").Append(cur)
                  .Append("/Wp, O&amp;M ").Append(F(ep.OpexFractionOfCapex * 100, "0.0")).Append(" % of CAPEX/year, tariff ")
                  .Append(F(ep.TariffPerKwh, "0.####")).Append(" ").Append(cur).Append("/kWh, discount rate ")
                  .Append(F(ep.DiscountRate * 100, "0.0")).Append(" %, ").Append(ep.LifetimeYears).Append(" years.</p>");
            }

            if (input.Bom != null && input.Bom.Count > 0)
            {
                sb.Append("<h2>Bill of materials (estimate)</h2><table><tr><th>Item</th><th>Description</th><th>Quantity</th><th>Unit</th></tr>");
                foreach (var b in input.Bom)
                    sb.Append("<tr><td>").Append(E(b.Item)).Append("</td><td style=\"text-align:left\">").Append(E(b.Description))
                      .Append("</td><td>").Append(F(b.Quantity, "#,0.##")).Append("</td><td>").Append(E(b.Unit)).Append("</td></tr>");
                sb.Append("</table>");
            }

            if (sim != null && sim.Warnings.Count > 0)
            {
                sb.Append("<h2>Design warnings</h2>");
                foreach (var w in sim.Warnings) sb.Append("<div class=\"warn\">").Append(E(w)).Append("</div>");
            }

            sb.Append("<p class=\"muted\" style=\"margin-top:30px\">Simulation: hourly model, ")
              .Append(E(p.Simulation.Transposition.ToString())).Append(" transposition, Faiman thermal model (Uc=")
              .Append(F(p.Losses.ThermalUc, "0.#")).Append(", Uv=").Append(F(p.Losses.ThermalUv, "0.#"))
              .Append("), ASHRAE IAM (b0=").Append(F(p.Losses.IamB0, "0.00"))
              .Append("). Results are estimates; validate bankable studies with measured data.</p>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static void Kpi(StringBuilder sb, string label, string value) =>
            sb.Append("<div class=\"kpi\"><span class=\"muted\">").Append(E(label)).Append("</span><b>").Append(value).Append("</b></div>");

        private static void Row(StringBuilder sb, string label, string valueHtml) =>
            sb.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(valueHtml).Append("</td></tr>");

        private static void AppendMonthlyTable(StringBuilder sb, SimulationResult sim)
        {
            sb.Append("<table><tr><th></th><th>GlobHor<br>kWh/m&sup2;</th><th>DiffHor<br>kWh/m&sup2;</th><th>T_Amb<br>&deg;C</th><th>GlobInc<br>kWh/m&sup2;</th><th>GlobEff<br>kWh/m&sup2;</th><th>EArray<br>MWh</th><th>E_Grid<br>MWh</th><th>PR<br>%</th></tr>");
            foreach (var m in sim.Monthly) MonthRow(sb, MonthlyMeteoSummary.MonthNames[m.Month - 1], m);
            sb.Append("<tr class=\"ms\">");
            MonthCells(sb, "Year", sim.Annual);
            sb.Append("</tr></table>");
        }

        private static void MonthRow(StringBuilder sb, string label, MonthlyResult m)
        {
            sb.Append("<tr>");
            MonthCells(sb, label, m);
            sb.Append("</tr>");
        }

        private static void MonthCells(StringBuilder sb, string label, MonthlyResult m)
        {
            sb.Append("<td>").Append(label).Append("</td><td>").Append(F(m.GlobHor, "0.0")).Append("</td><td>").Append(F(m.DiffHor, "0.0"))
              .Append("</td><td>").Append(F(m.TAmb, "0.0")).Append("</td><td>").Append(F(m.GlobInc, "0.0")).Append("</td><td>")
              .Append(F(m.GlobEff, "0.0")).Append("</td><td>").Append(F(m.EArray / 1000.0, "#,0.0")).Append("</td><td>")
              .Append(F(m.EGrid / 1000.0, "#,0.0")).Append("</td><td>").Append(F(m.PR * 100, "0.0")).Append("</td>");
        }

        private static void AppendMonthlyChart(StringBuilder sb, SimulationResult sim)
        {
            const int w = 900, h = 260, left = 60, bottom = 30, top = 20;
            double max = 0;
            foreach (var m in sim.Monthly) max = System.Math.Max(max, m.EGrid);
            if (max <= 0) return;
            double scale = NiceCeiling(max / 1000.0);
            double bw = (w - left - 20) / 12.0;
            sb.Append("<svg viewBox=\"0 0 ").Append(w).Append(' ').Append(h).Append("\" width=\"100%\" role=\"img\" aria-label=\"Monthly energy\" style=\"font:12px sans-serif\">");
            for (int i = 0; i <= 4; i++)
            {
                double v = scale * i / 4.0;
                double y = h - bottom - (h - bottom - top) * i / 4.0;
                sb.Append("<line x1=\"").Append(left).Append("\" x2=\"").Append(w - 10).Append("\" y1=\"").Append(F(y, "0.#")).Append("\" y2=\"").Append(F(y, "0.#"))
                  .Append("\" stroke=\"#d9e0e7\"/><text x=\"").Append(left - 6).Append("\" y=\"").Append(F(y + 4, "0.#")).Append("\" text-anchor=\"end\" fill=\"#5d6b7a\">")
                  .Append(F(v, "#,0.#")).Append("</text>");
            }
            sb.Append("<text x=\"14\" y=\"").Append(top + 80).Append("\" transform=\"rotate(-90 14 ").Append(top + 80).Append(")\" fill=\"#5d6b7a\">MWh</text>");
            for (int i = 0; i < 12; i++)
            {
                double e = sim.Monthly[i].EGrid / 1000.0;
                double bh = (h - bottom - top) * e / scale;
                double x = left + i * bw + bw * 0.15;
                sb.Append("<rect x=\"").Append(F(x, "0.#")).Append("\" y=\"").Append(F(h - bottom - bh, "0.#")).Append("\" width=\"").Append(F(bw * 0.7, "0.#"))
                  .Append("\" height=\"").Append(F(bh, "0.#")).Append("\" fill=\"#0b6bcb\"><title>")
                  .Append(MonthlyMeteoSummary.MonthNames[i]).Append(": ").Append(F(e, "#,0.0")).Append(" MWh</title></rect>");
                sb.Append("<text x=\"").Append(F(x + bw * 0.35, "0.#")).Append("\" y=\"").Append(h - 10).Append("\" text-anchor=\"middle\" fill=\"#1d2733\">")
                  .Append(MonthlyMeteoSummary.MonthNames[i]).Append("</text>");
            }
            sb.Append("</svg>");
        }

        private static double NiceCeiling(double v)
        {
            if (v <= 0) return 1;
            double mag = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(v)));
            foreach (double s in new[] { 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 })
                if (s * mag >= v) return s * mag;
            return 10 * mag;
        }

        private static void AppendLossDiagram(StringBuilder sb, SimulationResult sim)
        {
            sb.Append("<table class=\"loss\">");
            foreach (var l in sim.Losses)
            {
                if (l.IsMilestone)
                {
                    string unit = l.Kind == LossKind.Irradiation ? " kWh/m&sup2;" : " MWh";
                    double v = l.Kind == LossKind.Irradiation ? l.Value : l.Value / 1000.0;
                    sb.Append("<tr class=\"ms\"><td>").Append(E(l.Label)).Append("</td><td style=\"text-align:right\">")
                      .Append(F(v, "#,0.0")).Append(unit).Append("</td><td></td></tr>");
                }
                else
                {
                    double width = System.Math.Min(300, System.Math.Abs(l.Percent) * 20);
                    string cls = l.Percent < 0 ? "neg" : "pos";
                    sb.Append("<tr><td>").Append(E(l.Label)).Append("</td><td style=\"text-align:right\">")
                      .Append(l.Percent >= 0 ? "+" : "").Append(F(l.Percent, "0.00")).Append(" %</td><td><span class=\"bar ")
                      .Append(cls).Append("\" style=\"width:").Append(F(width, "0")).Append("px\"></span></td></tr>");
                }
            }
            sb.Append("</table>");
        }

        private static void AppendShadingTable(StringBuilder sb, ShadingFactorTable t, bool electrical)
        {
            var table = electrical ? t.ElectricalLoss : t.LinearLoss;
            sb.Append("<p class=\"muted\">Beam shading loss (%) by sun azimuth (columns, 0&deg; = North, 180&deg; = South) and height (rows).</p>");
            sb.Append("<div style=\"overflow-x:auto\"><table style=\"font-size:11px\"><tr><th>Height</th>");
            for (int a = 4; a < t.Azimuths.Length - 4; a += 2) sb.Append("<th>").Append(F(t.Azimuths[a], "0")).Append("</th>");
            sb.Append("</tr>");
            for (int e = t.Elevations.Length - 1; e >= 0; e--)
            {
                sb.Append("<tr><th>").Append(F(t.Elevations[e], "0")).Append("&deg;</th>");
                for (int a = 4; a < t.Azimuths.Length - 4; a += 2)
                {
                    double v = table[a, e] * 100;
                    string bg = v <= 0.05 ? "#ffffff" : v < 5 ? "#fff3cd" : v < 20 ? "#ffd8a8" : "#ffb3b3";
                    sb.Append("<td style=\"background:").Append(bg).Append("\">").Append(F(v, "0.0")).Append("</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</table></div>");
        }

        /// <summary>Hourly results as CSV (semicolon separated, invariant culture).</summary>
        public static string BuildHourlyCsv(PVProject project, SimulationResult sim)
        {
            var sb = new StringBuilder(8760 * 64);
            sb.Append("Date (local);GHI W/m2;DHI W/m2;T_amb C;Sun elevation deg;GlobInc W/m2;T_cell C;EArray kW;E_Grid kW\n");
            var meteo = project.Meteo;
            for (int h = 0; h < MeteoDataset.HoursPerYear; h++)
            {
                var t = MeteoDataset.HourStartUtc(h).AddHours(project.Location.TimeZone);
                sb.Append(t.ToString("MM-dd HH:mm", Inv)).Append(';')
                  .Append(F(meteo?.Ghi[h] ?? 0, "0.#")).Append(';')
                  .Append(F(meteo?.Dhi[h] ?? 0, "0.#")).Append(';')
                  .Append(F(meteo?.TempAmbient[h] ?? 0, "0.#")).Append(';')
                  .Append(F(sim.HourlySunElevation[h], "0.##")).Append(';')
                  .Append(F(sim.HourlyGlobInc[h], "0.#")).Append(';')
                  .Append(F(sim.HourlyTCell[h], "0.#")).Append(';')
                  .Append(F(sim.HourlyEArray[h], "0.###")).Append(';')
                  .Append(F(sim.HourlyEGrid[h], "0.###")).Append('\n');
            }
            return sb.ToString();
        }
    }
}
