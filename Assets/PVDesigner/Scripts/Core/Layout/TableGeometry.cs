using System.Collections.Generic;
using PVDesigner.Core.Components;
using PVDesigner.Core.Math;
using PVDesigner.Core.Model;
using PVDesigner.Core.Solar;

namespace PVDesigner.Core.Layout
{
    /// <summary>World space rectangle of a PV table (or a single module).</summary>
    public struct OrientedRect
    {
        public Vec3 Center;
        /// <summary>Unit vector along the width (row direction).</summary>
        public Vec3 U;
        /// <summary>Unit vector along the length (up the slope).</summary>
        public Vec3 V;
        /// <summary>Front face normal.</summary>
        public Vec3 N;
        public double HalfW;
        public double HalfL;

        public Vec3 Corner(int i)
        {
            switch (i)
            {
                case 0: return Center - U * HalfW - V * HalfL;
                case 1: return Center + U * HalfW - V * HalfL;
                case 2: return Center + U * HalfW + V * HalfL;
                default: return Center - U * HalfW + V * HalfL;
            }
        }

        public Vec3 PointAt(double su, double sv) => Center + U * (su * HalfW) + V * (sv * HalfL);

        public double MaxY
        {
            get
            {
                double m = double.MinValue;
                for (int i = 0; i < 4; i++) m = System.Math.Max(m, Corner(i).Y);
                return m;
            }
        }
    }

    public static class TableGeometry
    {
        public static void ModuleSize(ArrayField field, PVModuleSpec module, out double across, out double up)
        {
            if (field.Orientation == ModuleOrientation.Portrait)
            {
                across = module.Width;
                up = module.Length;
            }
            else
            {
                across = module.Length;
                up = module.Width;
            }
        }

        public static void TableSize(ArrayField field, PVModuleSpec module, out double width, out double length)
        {
            ModuleSize(field, module, out double a, out double u);
            width = field.ModulesAcross * a + (field.ModulesAcross - 1) * field.ModuleGap;
            length = field.ModulesUp * u + (field.ModulesUp - 1) * field.ModuleGap;
        }

        /// <summary>Horizontal unit vector along the rows.</summary>
        public static Vec3 RowDirection(ArrayField field)
        {
            if (field.Mounting == MountingType.SingleAxisTracker)
                return SingleAxisTracker.AxisDirection(field.TrackerAxisCompassAzimuth);
            double g = field.CompassAzimuth;
            return new Vec3(MathUtil.CosD(g), 0, -MathUtil.SinD(g));
        }

        /// <summary>Horizontal unit vector perpendicular to the rows (fixed: facing direction).</summary>
        public static Vec3 PitchDirection(ArrayField field)
        {
            if (field.Mounting == MountingType.SingleAxisTracker)
                return SingleAxisTracker.CrossAxisDirection(field.TrackerAxisCompassAzimuth);
            double g = field.CompassAzimuth;
            return new Vec3(MathUtil.SinD(g), 0, MathUtil.CosD(g));
        }

        /// <summary>Footprint depth of a table measured along the pitch direction (m).</summary>
        public static double FootprintDepth(ArrayField field, PVModuleSpec module)
        {
            TableSize(field, module, out _, out double length);
            return field.Mounting == MountingType.SingleAxisTracker ? length : length * MathUtil.CosD(field.Tilt);
        }

        /// <summary>Ground coverage ratio (collector length / pitch).</summary>
        public static double Gcr(ArrayField field, PVModuleSpec module)
        {
            TableSize(field, module, out _, out double length);
            return field.Pitch > 0 ? length / field.Pitch : 0;
        }

        /// <summary>Builds the world space rectangle of a table. For trackers <paramref name="trackerRotationDeg"/> is used.</summary>
        public static OrientedRect Build(ArrayField field, PVTable table, PVModuleSpec module, double trackerRotationDeg = 0)
        {
            TableSize(field, module, out double w, out double l);
            var r = new OrientedRect { HalfW = w * 0.5, HalfL = l * 0.5 };
            double baseY = field.BaseElevation + table.Elevation;

            if (field.Mounting == MountingType.SingleAxisTracker)
            {
                double ax = field.TrackerAxisCompassAzimuth;
                Vec3 a = SingleAxisTracker.AxisDirection(ax);
                Vec3 p = SingleAxisTracker.CrossAxisDirection(ax);
                double c = MathUtil.CosD(trackerRotationDeg), s = MathUtil.SinD(trackerRotationDeg);
                r.N = (Vec3.Up * c + p * s).Normalized;
                r.U = a;
                r.V = (p * c - Vec3.Up * s).Normalized;
                r.Center = new Vec3(table.X, baseY + field.GroundClearance, table.Z);
            }
            else
            {
                double g = field.CompassAzimuth;
                double b = field.Tilt;
                Vec3 f = new Vec3(MathUtil.SinD(g), 0, MathUtil.CosD(g));
                r.U = new Vec3(MathUtil.CosD(g), 0, -MathUtil.SinD(g));
                r.V = (Vec3.Up * MathUtil.SinD(b) - f * MathUtil.CosD(b)).Normalized;
                r.N = (Vec3.Up * MathUtil.CosD(b) + f * MathUtil.SinD(b)).Normalized;
                r.Center = new Vec3(table.X, baseY + field.GroundClearance + r.HalfL * MathUtil.SinD(b), table.Z);
            }
            return r;
        }

        /// <summary>Module rectangles of a table (row major: across then up).</summary>
        public static List<OrientedRect> Modules(ArrayField field, OrientedRect table, PVModuleSpec module)
        {
            ModuleSize(field, module, out double a, out double u);
            var list = new List<OrientedRect>(field.ModulesPerTable);
            for (int j = 0; j < field.ModulesUp; j++)
            {
                for (int i = 0; i < field.ModulesAcross; i++)
                {
                    double ou = -table.HalfW + a * 0.5 + i * (a + field.ModuleGap);
                    double ov = -table.HalfL + u * 0.5 + j * (u + field.ModuleGap);
                    list.Add(new OrientedRect
                    {
                        Center = table.Center + table.U * ou + table.V * ov,
                        U = table.U,
                        V = table.V,
                        N = table.N,
                        HalfW = a * 0.5,
                        HalfL = u * 0.5
                    });
                }
            }
            return list;
        }

        /// <summary>Horizontal footprint corners (X east, Y north) of a table at its rest position.</summary>
        public static Vec2[] Footprint(ArrayField field, PVTable table, PVModuleSpec module, double inflate = 0)
        {
            TableSize(field, module, out double w, out _);
            double d = FootprintDepth(field, module);
            Vec3 ru = RowDirection(field), rd = PitchDirection(field);
            return RectFootprint(new Vec2(table.X, table.Z), new Vec2(ru.X, ru.Z), new Vec2(rd.X, rd.Z), w * 0.5 + inflate, d * 0.5 + inflate);
        }

        public static Vec2[] RectFootprint(Vec2 c, Vec2 axisA, Vec2 axisB, double halfA, double halfB)
        {
            return new[]
            {
                c - axisA * halfA - axisB * halfB,
                c + axisA * halfA - axisB * halfB,
                c + axisA * halfA + axisB * halfB,
                c - axisA * halfA + axisB * halfB
            };
        }

        /// <summary>Obstacle footprint corners (rotated rectangle or bounding square for round objects).</summary>
        public static Vec2[] ObstacleFootprint(Obstacle o, double inflate = 0)
        {
            double rot = o.Rotation * MathUtil.Deg2Rad;
            // Clockwise rotation seen from above (Unity convention).
            var ax = new Vec2(System.Math.Cos(rot), -System.Math.Sin(rot));
            var az = new Vec2(System.Math.Sin(rot), System.Math.Cos(rot));
            return RectFootprint(new Vec2(o.X, o.Z), ax, az, o.Width * 0.5 + inflate, o.Depth * 0.5 + inflate);
        }
    }
}
