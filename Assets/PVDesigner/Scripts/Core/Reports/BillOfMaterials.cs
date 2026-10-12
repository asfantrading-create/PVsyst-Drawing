using System.Collections.Generic;
using PVDesigner.Core.Components;
using PVDesigner.Core.Electrical;
using PVDesigner.Core.Layout;
using PVDesigner.Core.Math;
using PVDesigner.Core.Model;

namespace PVDesigner.Core.Reports
{
    public class BomLine
    {
        public string Item;
        public string Description;
        public double Quantity;
        public string Unit;

        public BomLine(string item, string description, double quantity, string unit)
        {
            Item = item;
            Description = description;
            Quantity = quantity;
            Unit = unit;
        }
    }

    public static class BillOfMaterials
    {
        public static List<BomLine> Build(PVProject project, PVModuleSpec module, InverterSpec inverter, SystemSizingResult sizing)
        {
            var list = new List<BomLine>();
            if (module == null || inverter == null || sizing == null) return list;

            list.Add(new BomLine("PV module", module.DisplayName, sizing.StringCount * sizing.ModulesPerString, "pcs"));
            list.Add(new BomLine("Inverter", inverter.DisplayName, sizing.InverterCount, "pcs"));

            int fixedTables = 0, trackers = 0;
            foreach (var f in project.Fields)
            {
                if (f.Mounting == MountingType.SingleAxisTracker) trackers += f.Tables.Count;
                else fixedTables += f.Tables.Count;
            }
            if (fixedTables > 0) list.Add(new BomLine("Mounting structure", "Fixed tilt table", fixedTables, "pcs"));
            if (trackers > 0) list.Add(new BomLine("Tracker", "Horizontal single axis tracker", trackers, "pcs"));

            // Cable estimates: string cable = string length + 2 x distance to the inverter location.
            var assignment = SystemSizing.AssignStrings(project, sizing);
            int n = System.Math.Max(1, sizing.InverterCount);
            var cx = new double[n];
            var cz = new double[n];
            var cnt = new int[n];
            var tablePos = new List<Vec2>();
            int k = 0;
            foreach (var f in project.Fields)
            {
                foreach (var t in f.Tables)
                {
                    tablePos.Add(new Vec2(t.X, t.Z));
                    foreach (int inv in assignment[k])
                    {
                        if (inv < 0) continue;
                        cx[inv] += t.X;
                        cz[inv] += t.Z;
                        cnt[inv]++;
                    }
                    k++;
                }
            }
            for (int i = 0; i < n; i++)
            {
                if (cnt[i] == 0) continue;
                cx[i] /= cnt[i];
                cz[i] /= cnt[i];
            }

            double homeRun = 0;
            k = 0;
            foreach (var f in project.Fields)
            {
                foreach (var t in f.Tables)
                {
                    foreach (int inv in assignment[k])
                    {
                        if (inv < 0) continue;
                        homeRun += Vec2.Distance(new Vec2(t.X, t.Z), new Vec2(cx[inv], cz[inv]));
                    }
                    k++;
                }
            }
            // Home run distance counted per module: convert to per string (plus and minus conductors).
            double stringCable = sizing.ModulesPerString > 0 ? 2.0 * homeRun / sizing.ModulesPerString : 0;
            stringCable += sizing.StringCount * sizing.ModulesPerString * module.Width * 1.1;
            list.Add(new BomLine("DC string cable", $"{project.SystemDesign.StringCableSection:0.#} mm2 solar cable (estimate)", System.Math.Round(stringCable), "m"));
            list.Add(new BomLine("MC4 connector pairs", "Connectors at string ends", sizing.StringCount * 2, "pairs"));

            double acCable = 0;
            var poi = Polygon2D.Centroid(project.SiteBoundary.Count >= 3 ? project.SiteBoundary : tablePos);
            for (int i = 0; i < n; i++)
                if (cnt[i] > 0) acCable += Vec2.Distance(poi, new Vec2(cx[i], cz[i])) * 1.15;
            list.Add(new BomLine("AC cable", $"{project.SystemDesign.AcCableSection:0} mm2 (estimate, to site centroid)", System.Math.Round(acCable), "m"));

            if (project.SiteBoundary.Count >= 3)
                list.Add(new BomLine("Fence", "Site perimeter fence", System.Math.Round(Polygon2D.Perimeter(project.SiteBoundary)), "m"));
            return list;
        }
    }
}
