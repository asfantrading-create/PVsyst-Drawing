using System.Collections.Generic;
using PVDesigner.Core.Components;
using PVDesigner.Core.Math;
using PVDesigner.Core.Model;

namespace PVDesigner.Core.Layout
{
    public class LayoutResult
    {
        public List<PVTable> Tables = new List<PVTable>();
        public int Rows;
        public double Gcr;
        public double FootprintArea;
        public string Message = "";
    }

    /// <summary>Automatically fills a polygon with rows of tables (fixed tilt sheds or trackers).</summary>
    public static class LayoutGenerator
    {
        /// <summary>Minimum clearance kept around obstacles (m).</summary>
        public const double ObstacleClearance = 1.0;
        private const double ScanStep = 0.25;

        public static LayoutResult Generate(PVProject project, ArrayField field, PVModuleSpec module)
        {
            var boundary = field.Boundary != null && field.Boundary.Count >= 3 ? field.Boundary : project.SiteBoundary;
            var others = new List<Vec2[]>();
            foreach (var o in project.Obstacles) others.Add(TableGeometry.ObstacleFootprint(o, ObstacleClearance));
            foreach (var f in project.Fields)
            {
                if (f == field) continue;
                foreach (var t in f.Tables) others.Add(TableGeometry.Footprint(f, t, module, 0.3));
            }
            return Generate(field, module, boundary, project.BoundarySetback, others);
        }

        public static LayoutResult Generate(ArrayField field, PVModuleSpec module, IReadOnlyList<Vec2> boundary,
            double setback, IReadOnlyList<Vec2[]> keepOut)
        {
            var result = new LayoutResult();
            if (boundary == null || boundary.Count < 3)
            {
                result.Message = "Draw a site boundary first.";
                return result;
            }
            if (field.Pitch <= 0 || field.ModulesAcross <= 0 || field.ModulesUp <= 0)
            {
                result.Message = "Invalid table definition.";
                return result;
            }

            TableGeometry.TableSize(field, module, out double width, out double length);
            double depth = TableGeometry.FootprintDepth(field, module);
            if (field.Mounting == MountingType.FixedTilt && field.Pitch < depth)
                result.Message = "Pitch is smaller than the table footprint; rows will overlap.";

            Vec3 r3 = TableGeometry.RowDirection(field);
            Vec3 d3 = TableGeometry.PitchDirection(field);
            var ru = new Vec2(r3.X, r3.Z);
            var rd = new Vec2(d3.X, d3.Z);

            // Polygon in (s = along rows, t = along pitch) coordinates.
            var local = new List<Vec2>(boundary.Count);
            foreach (var p in boundary) local.Add(new Vec2(Vec2.Dot(p, ru), Vec2.Dot(p, rd)));
            Polygon2D.Bounds(local, out Vec2 min, out Vec2 max);

            var localKeepOut = new List<KeepOut>();
            if (keepOut != null)
            {
                foreach (var poly in keepOut)
                {
                    var lp = new Vec2[poly.Length];
                    for (int i = 0; i < poly.Length; i++) lp[i] = new Vec2(Vec2.Dot(poly[i], ru), Vec2.Dot(poly[i], rd));
                    Polygon2D.Bounds(lp, out Vec2 kmin, out Vec2 kmax);
                    localKeepOut.Add(new KeepOut { Poly = lp, Min = kmin, Max = kmax });
                }
            }

            double halfW = width * 0.5, halfD = depth * 0.5;
            int row = 0;
            // Rows are generated from the "back" (largest t) to the front so row 0 is the front row.
            for (double t = min.Y + setback + halfD; t <= max.Y - setback - halfD + 1e-6; t += field.Pitch)
            {
                int index = 0;
                double s = min.X + setback + halfW;
                bool any = false;
                while (s <= max.X - setback - halfW + 1e-6)
                {
                    if (IsValid(local, localKeepOut, s, t, halfW, halfD, setback))
                    {
                        var world = ru * s + rd * t;
                        result.Tables.Add(new PVTable(world.X, world.Y, row, index++));
                        any = true;
                        s += width + field.TableGap;
                    }
                    else
                    {
                        s += ScanStep;
                    }
                }
                if (any) row++;
            }

            result.Rows = row;
            result.Gcr = length / field.Pitch;
            result.FootprintArea = result.Tables.Count * width * depth;
            if (result.Tables.Count == 0 && string.IsNullOrEmpty(result.Message))
                result.Message = "No table fits inside the boundary with the current setback.";
            return result;
        }

        private struct KeepOut
        {
            public Vec2[] Poly;
            public Vec2 Min;
            public Vec2 Max;
        }

        private static bool IsValid(List<Vec2> poly, List<KeepOut> keepOut, double s, double t, double hw, double hd, double setback)
        {
            var rect = new[]
            {
                new Vec2(s - hw, t - hd), new Vec2(s + hw, t - hd), new Vec2(s + hw, t + hd), new Vec2(s - hw, t + hd)
            };
            foreach (var c in rect)
            {
                if (!Polygon2D.Contains(poly, c)) return false;
                if (setback > 0 && Polygon2D.DistanceToBoundary(poly, c) < setback) return false;
            }
            // Concave notches: no boundary vertex may fall inside the (inflated) rectangle.
            foreach (var v in poly)
            {
                if (v.X > s - hw - setback && v.X < s + hw + setback && v.Y > t - hd - setback && v.Y < t + hd + setback)
                    return false;
            }
            foreach (var k in keepOut)
            {
                if (k.Max.X < s - hw || k.Min.X > s + hw || k.Max.Y < t - hd || k.Min.Y > t + hd) continue;
                if (Polygon2D.Overlaps(k.Poly, rect)) return false;
            }
            return true;
        }
    }
}
