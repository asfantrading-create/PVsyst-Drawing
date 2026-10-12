using PVDesigner.Core.Layout;
using PVDesigner.Core.Math;

namespace PVDesigner.Core.Shading
{
    public enum OccluderKind
    {
        Quad,
        Box,
        Cylinder,
        Ellipsoid
    }

    /// <summary>Flat ray-traceable primitive. Kept as a single struct for cache friendly brute force tests.</summary>
    public struct Occluder
    {
        public OccluderKind Kind;
        /// <summary>Fraction of light passing through (0 = opaque).</summary>
        public double Transmission;

        // Quad
        public OrientedRect Rect;

        // Box (yaw rotated), Cylinder (vertical), Ellipsoid (vertical axis)
        public Vec3 Center;
        public Vec3 HalfExtents;
        public double CosYaw;
        public double SinYaw;

        // Horizontal bounds for grid insertion
        public double MinX, MaxX, MinZ, MaxZ, MaxY;

        public static Occluder FromRect(OrientedRect r, bool allRotations)
        {
            var o = new Occluder { Kind = OccluderKind.Quad, Rect = r };
            if (allRotations)
            {
                // Trackers: bound the swept volume around the axis.
                double e = r.HalfL;
                var a = r.U;
                double ex = System.Math.Abs(a.X) * r.HalfW + System.Math.Abs(a.Z) * e;
                double ez = System.Math.Abs(a.Z) * r.HalfW + System.Math.Abs(a.X) * e;
                o.MinX = r.Center.X - ex;
                o.MaxX = r.Center.X + ex;
                o.MinZ = r.Center.Z - ez;
                o.MaxZ = r.Center.Z + ez;
                o.MaxY = r.Center.Y + e;
            }
            else
            {
                o.MinX = o.MinZ = double.MaxValue;
                o.MaxX = o.MaxZ = o.MaxY = double.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    var c = r.Corner(i);
                    o.MinX = System.Math.Min(o.MinX, c.X);
                    o.MaxX = System.Math.Max(o.MaxX, c.X);
                    o.MinZ = System.Math.Min(o.MinZ, c.Z);
                    o.MaxZ = System.Math.Max(o.MaxZ, c.Z);
                    o.MaxY = System.Math.Max(o.MaxY, c.Y);
                }
            }
            return o;
        }

        public static Occluder Box(Vec3 center, Vec3 halfExtents, double yawDeg, double transmission = 0)
        {
            var o = new Occluder
            {
                Kind = OccluderKind.Box,
                Center = center,
                HalfExtents = halfExtents,
                CosYaw = MathUtil.CosD(yawDeg),
                SinYaw = MathUtil.SinD(yawDeg),
                Transmission = transmission
            };
            double ex = System.Math.Abs(o.CosYaw) * halfExtents.X + System.Math.Abs(o.SinYaw) * halfExtents.Z;
            double ez = System.Math.Abs(o.SinYaw) * halfExtents.X + System.Math.Abs(o.CosYaw) * halfExtents.Z;
            o.MinX = center.X - ex;
            o.MaxX = center.X + ex;
            o.MinZ = center.Z - ez;
            o.MaxZ = center.Z + ez;
            o.MaxY = center.Y + halfExtents.Y;
            return o;
        }

        /// <summary>Vertical cylinder, <paramref name="center"/> is the centre of the volume.</summary>
        public static Occluder Cylinder(Vec3 center, double radius, double halfHeight, double transmission = 0)
        {
            return new Occluder
            {
                Kind = OccluderKind.Cylinder,
                Center = center,
                HalfExtents = new Vec3(radius, halfHeight, radius),
                Transmission = transmission,
                MinX = center.X - radius,
                MaxX = center.X + radius,
                MinZ = center.Z - radius,
                MaxZ = center.Z + radius,
                MaxY = center.Y + halfHeight
            };
        }

        public static Occluder Ellipsoid(Vec3 center, double radiusH, double radiusV, double transmission)
        {
            return new Occluder
            {
                Kind = OccluderKind.Ellipsoid,
                Center = center,
                HalfExtents = new Vec3(radiusH, radiusV, radiusH),
                Transmission = transmission,
                MinX = center.X - radiusH,
                MaxX = center.X + radiusH,
                MinZ = center.Z - radiusH,
                MaxZ = center.Z + radiusH,
                MaxY = center.Y + radiusV
            };
        }

        /// <summary>Returns true when the ray (o + d t, 0 &lt; t &lt; tMax) intersects the primitive.</summary>
        public bool Intersects(Vec3 o, Vec3 d, double tMax)
        {
            switch (Kind)
            {
                case OccluderKind.Quad: return IntersectQuad(o, d, tMax);
                case OccluderKind.Box: return IntersectBox(o, d, tMax);
                case OccluderKind.Cylinder: return IntersectCylinder(o, d, tMax);
                default: return IntersectEllipsoid(o, d, tMax);
            }
        }

        private bool IntersectQuad(Vec3 o, Vec3 d, double tMax)
        {
            double denom = Vec3.Dot(d, Rect.N);
            if (System.Math.Abs(denom) < 1e-9) return false;
            double t = Vec3.Dot(Rect.Center - o, Rect.N) / denom;
            if (t <= 1e-4 || t > tMax) return false;
            var p = o + d * t - Rect.Center;
            return System.Math.Abs(Vec3.Dot(p, Rect.U)) <= Rect.HalfW && System.Math.Abs(Vec3.Dot(p, Rect.V)) <= Rect.HalfL;
        }

        private bool IntersectBox(Vec3 o, Vec3 d, double tMax)
        {
            // Transform into the box frame (inverse yaw rotation, Unity clockwise convention).
            var rel = o - Center;
            double lox = rel.X * CosYaw - rel.Z * SinYaw;
            double loz = rel.X * SinYaw + rel.Z * CosYaw;
            double ldx = d.X * CosYaw - d.Z * SinYaw;
            double ldz = d.X * SinYaw + d.Z * CosYaw;
            double t0 = 1e-4, t1 = tMax;
            if (!Slab(lox, ldx, HalfExtents.X, ref t0, ref t1)) return false;
            if (!Slab(rel.Y, d.Y, HalfExtents.Y, ref t0, ref t1)) return false;
            if (!Slab(loz, ldz, HalfExtents.Z, ref t0, ref t1)) return false;
            return true;
        }

        private static bool Slab(double o, double d, double h, ref double t0, ref double t1)
        {
            if (System.Math.Abs(d) < 1e-12) return o >= -h && o <= h;
            double inv = 1.0 / d;
            double ta = (-h - o) * inv, tb = (h - o) * inv;
            if (ta > tb)
            {
                double tmp = ta;
                ta = tb;
                tb = tmp;
            }
            if (ta > t0) t0 = ta;
            if (tb < t1) t1 = tb;
            return t0 <= t1;
        }

        private bool IntersectCylinder(Vec3 o, Vec3 d, double tMax)
        {
            double r = HalfExtents.X;
            double ox = o.X - Center.X, oz = o.Z - Center.Z;
            double a = d.X * d.X + d.Z * d.Z;
            double t0 = 1e-4, t1 = tMax;
            if (a < 1e-12)
            {
                if (ox * ox + oz * oz > r * r) return false;
            }
            else
            {
                double b = 2 * (ox * d.X + oz * d.Z);
                double c = ox * ox + oz * oz - r * r;
                double disc = b * b - 4 * a * c;
                if (disc < 0) return false;
                double sq = System.Math.Sqrt(disc);
                double ta = (-b - sq) / (2 * a), tb = (-b + sq) / (2 * a);
                if (ta > t0) t0 = ta;
                if (tb < t1) t1 = tb;
                if (t0 > t1) return false;
            }
            return Slab(o.Y - Center.Y, d.Y, HalfExtents.Y, ref t0, ref t1);
        }

        private bool IntersectEllipsoid(Vec3 o, Vec3 d, double tMax)
        {
            double sy = HalfExtents.X / System.Math.Max(1e-6, HalfExtents.Y);
            var oc = o - Center;
            var lo = new Vec3(oc.X, oc.Y * sy, oc.Z);
            var ld = new Vec3(d.X, d.Y * sy, d.Z);
            double r = HalfExtents.X;
            double a = Vec3.Dot(ld, ld);
            double b = 2 * Vec3.Dot(lo, ld);
            double c = Vec3.Dot(lo, lo) - r * r;
            double disc = b * b - 4 * a * c;
            if (disc < 0) return false;
            double sq = System.Math.Sqrt(disc);
            double t1 = (-b + sq) / (2 * a);
            double t0 = (-b - sq) / (2 * a);
            return t1 > 1e-4 && t0 < tMax;
        }
    }
}
