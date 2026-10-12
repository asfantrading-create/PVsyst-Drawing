using System.Collections.Generic;

namespace PVDesigner.Core.Math
{
    /// <summary>Planar polygon utilities (site boundaries, keep-out zones).</summary>
    public static class Polygon2D
    {
        /// <summary>Signed area; positive when counter-clockwise (seen from above, X east / Y north).</summary>
        public static double SignedArea(IReadOnlyList<Vec2> poly)
        {
            if (poly == null || poly.Count < 3) return 0;
            double a = 0;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                a += poly[j].X * poly[i].Y - poly[i].X * poly[j].Y;
            return a * 0.5;
        }

        public static double Area(IReadOnlyList<Vec2> poly) => System.Math.Abs(SignedArea(poly));

        public static double Perimeter(IReadOnlyList<Vec2> poly)
        {
            if (poly == null || poly.Count < 2) return 0;
            double p = 0;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                p += Vec2.Distance(poly[i], poly[j]);
            return p;
        }

        public static Vec2 Centroid(IReadOnlyList<Vec2> poly)
        {
            if (poly == null || poly.Count == 0) return new Vec2(0, 0);
            double a = SignedArea(poly);
            if (System.Math.Abs(a) < 1e-9)
            {
                double sx = 0, sy = 0;
                foreach (var p in poly) { sx += p.X; sy += p.Y; }
                return new Vec2(sx / poly.Count, sy / poly.Count);
            }
            double cx = 0, cy = 0;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                double f = poly[j].X * poly[i].Y - poly[i].X * poly[j].Y;
                cx += (poly[j].X + poly[i].X) * f;
                cy += (poly[j].Y + poly[i].Y) * f;
            }
            return new Vec2(cx / (6 * a), cy / (6 * a));
        }

        /// <summary>Even-odd point in polygon test.</summary>
        public static bool Contains(IReadOnlyList<Vec2> poly, Vec2 p)
        {
            if (poly == null || poly.Count < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i];
                var b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    double x = (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X;
                    if (p.X < x) inside = !inside;
                }
            }
            return inside;
        }

        public static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            var ab = b - a;
            double len2 = Vec2.Dot(ab, ab);
            double t = len2 > 1e-12 ? MathUtil.Clamp01(Vec2.Dot(p - a, ab) / len2) : 0;
            return Vec2.Distance(p, a + ab * t);
        }

        public static double DistanceToBoundary(IReadOnlyList<Vec2> poly, Vec2 p)
        {
            double d = double.MaxValue;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                d = System.Math.Min(d, DistanceToSegment(p, poly[j], poly[i]));
            return d;
        }

        public static void Bounds(IReadOnlyList<Vec2> poly, out Vec2 min, out Vec2 max)
        {
            min = new Vec2(double.MaxValue, double.MaxValue);
            max = new Vec2(double.MinValue, double.MinValue);
            foreach (var p in poly)
            {
                if (p.X < min.X) min.X = p.X;
                if (p.Y < min.Y) min.Y = p.Y;
                if (p.X > max.X) max.X = p.X;
                if (p.Y > max.Y) max.Y = p.Y;
            }
        }

        public static bool SegmentsIntersect(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
        {
            double d1 = Vec2.Cross(q2 - q1, p1 - q1);
            double d2 = Vec2.Cross(q2 - q1, p2 - q1);
            double d3 = Vec2.Cross(p2 - p1, q1 - p1);
            double d4 = Vec2.Cross(p2 - p1, q2 - p1);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        /// <summary>True when the convex quad <paramref name="quad"/> overlaps the polygon (edge crossing or containment).</summary>
        public static bool Overlaps(IReadOnlyList<Vec2> poly, IReadOnlyList<Vec2> quad)
        {
            foreach (var q in quad) if (Contains(poly, q)) return true;
            foreach (var p in poly) if (Contains(quad, p)) return true;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                for (int k = 0, l = quad.Count - 1; k < quad.Count; l = k++)
                    if (SegmentsIntersect(poly[j], poly[i], quad[l], quad[k])) return true;
            return false;
        }

        /// <summary>
        /// Ear clipping triangulation. Returns vertex indices (3 per triangle) wound counter-clockwise.
        /// Works for simple (non self-intersecting) polygons.
        /// </summary>
        public static List<int> Triangulate(IReadOnlyList<Vec2> poly)
        {
            var result = new List<int>();
            int n = poly?.Count ?? 0;
            if (n < 3) return result;

            var idx = new List<int>(n);
            if (SignedArea(poly) > 0)
                for (int i = 0; i < n; i++) idx.Add(i);
            else
                for (int i = n - 1; i >= 0; i--) idx.Add(i);

            int guard = 0;
            while (idx.Count > 3 && guard++ < n * n)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count];
                    int ib = idx[i];
                    int ic = idx[(i + 1) % idx.Count];
                    Vec2 a = poly[ia], b = poly[ib], c = poly[ic];
                    if (Vec2.Cross(b - a, c - b) <= 1e-12) continue; // reflex

                    bool containsOther = false;
                    for (int k = 0; k < idx.Count; k++)
                    {
                        int ik = idx[k];
                        if (ik == ia || ik == ib || ik == ic) continue;
                        if (PointInTriangle(poly[ik], a, b, c)) { containsOther = true; break; }
                    }
                    if (containsOther) continue;

                    result.Add(ia);
                    result.Add(ib);
                    result.Add(ic);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break; // degenerate polygon, give up gracefully
            }
            if (idx.Count == 3)
            {
                result.Add(idx[0]);
                result.Add(idx[1]);
                result.Add(idx[2]);
            }
            return result;
        }

        private static bool PointInTriangle(Vec2 p, Vec2 a, Vec2 b, Vec2 c)
        {
            double d1 = Vec2.Cross(b - a, p - a);
            double d2 = Vec2.Cross(c - b, p - b);
            double d3 = Vec2.Cross(a - c, p - c);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos);
        }
    }
}
