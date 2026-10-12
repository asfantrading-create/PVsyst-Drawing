using System;
using System.Collections.Generic;
using PVDesigner.Core.Components;
using PVDesigner.Core.Layout;
using PVDesigner.Core.Math;
using PVDesigner.Core.Model;
using PVDesigner.Core.Solar;

namespace PVDesigner.Core.Shading
{
    /// <summary>A point on a PV table used to evaluate shading.</summary>
    public struct ShadingSample
    {
        public int FieldIndex;
        /// <summary>Index of the table occluder in <see cref="ShadingScene.Occluders"/>.</summary>
        public int OccluderIndex;
        /// <summary>Electrical group (module row of a table) global index.</summary>
        public int GroupIndex;
        public double Su;
        public double Sv;
    }

    /// <summary>
    /// Ray traceable representation of the 3D scene (tables + obstacles) with a uniform
    /// horizontal grid for acceleration. Tracker tables can be re-oriented per sun position.
    /// </summary>
    public sealed class ShadingScene
    {
        public readonly List<Occluder> OccluderList = new List<Occluder>();
        public Occluder[] Occluders;
        public readonly List<ShadingSample> Samples = new List<ShadingSample>();
        public int GroupCount;
        public int[] GroupField;
        public int FieldCount;
        public double MaxTop;

        /// <summary>For each table occluder: field index and table index (for tracker updates and per table results).</summary>
        public readonly List<(int field, int table)> TableRefs = new List<(int field, int table)>();
        /// <summary>Occluder index of each table, ordered by field then table.</summary>
        public readonly List<int> TableOccluderIndex = new List<int>();

        private PVProject _project;
        private PVModuleSpec _module;

        private double _gridMinX, _gridMinZ, _cell;
        private int _nx, _nz;
        private int[][] _cells;

        public static ShadingScene Build(PVProject project, PVModuleSpec module, int maxSamples)
        {
            var s = new ShadingScene { _project = project, _module = module, FieldCount = project.Fields.Count };
            var groupField = new List<int>();

            int totalTables = project.TotalTables();
            maxSamples = System.Math.Max(200, maxSamples);

            for (int fi = 0; fi < project.Fields.Count; fi++)
            {
                var field = project.Fields[fi];
                bool tracker = field.Mounting == MountingType.SingleAxisTracker;

                // Sample pattern per table.
                int moduleRows = System.Math.Max(1, field.ModulesUp);
                int groupRows = System.Math.Min(moduleRows, 4);
                int perRow = moduleRows <= 2 ? 2 : 1;
                int upSamples = groupRows * perRow;
                int budgetPerTable = totalTables > 0 ? maxSamples / totalTables : maxSamples;
                int acrossSamples = MathUtil.Clamp(budgetPerTable / upSamples, 2, 10);
                int stride = 1;
                if (budgetPerTable < 2 * upSamples)
                    stride = (int)System.Math.Ceiling(totalTables * 2.0 * upSamples / maxSamples);

                for (int ti = 0; ti < field.Tables.Count; ti++)
                {
                    var rect = TableGeometry.Build(field, field.Tables[ti], module);
                    int occIndex = s.OccluderList.Count;
                    s.OccluderList.Add(Occluder.FromRect(rect, tracker));
                    s.TableRefs.Add((fi, ti));
                    s.TableOccluderIndex.Add(occIndex);

                    if (ti % stride != 0) continue;
                    for (int g = 0; g < groupRows; g++)
                    {
                        int group = groupField.Count;
                        groupField.Add(fi);
                        for (int k = 0; k < perRow; k++)
                        {
                            double sv = -1.0 + (2.0 * g + (k + 0.5) * (2.0 / perRow) * 1.0) / groupRows;
                            sv = MathUtil.Clamp(sv, -0.98, 0.98);
                            for (int a = 0; a < acrossSamples; a++)
                            {
                                double su = -1.0 + (2.0 * a + 1.0) / acrossSamples;
                                s.Samples.Add(new ShadingSample
                                {
                                    FieldIndex = fi,
                                    OccluderIndex = occIndex,
                                    GroupIndex = group,
                                    Su = su * 0.98,
                                    Sv = sv
                                });
                            }
                        }
                    }
                }
            }

            foreach (var o in project.Obstacles) AddObstacle(s.OccluderList, o);

            s.GroupCount = groupField.Count;
            s.GroupField = groupField.ToArray();
            s.Occluders = s.OccluderList.ToArray();
            s.BuildGrid();
            return s;
        }

        private static void AddObstacle(List<Occluder> list, Obstacle o)
        {
            double baseY = o.BaseElevation;
            switch (o.Type)
            {
                case ObstacleType.Tree:
                {
                    double trunk = MathUtil.Clamp(o.TrunkHeight, 0, o.Height * 0.9);
                    double crownH = System.Math.Max(0.5, o.Height - trunk);
                    double trunkR = System.Math.Max(0.1, o.Width * 0.06);
                    list.Add(Occluder.Cylinder(new Vec3(o.X, baseY + trunk * 0.5, o.Z), trunkR, trunk * 0.5));
                    list.Add(Occluder.Ellipsoid(new Vec3(o.X, baseY + trunk + crownH * 0.5, o.Z), o.Width * 0.5, crownH * 0.5,
                        MathUtil.Clamp01(o.Transparency)));
                    break;
                }
                case ObstacleType.Pole:
                case ObstacleType.Tank:
                    list.Add(Occluder.Cylinder(new Vec3(o.X, baseY + o.Height * 0.5, o.Z), o.Width * 0.5, o.Height * 0.5));
                    break;
                default:
                    list.Add(Occluder.Box(new Vec3(o.X, baseY + o.Height * 0.5, o.Z),
                        new Vec3(o.Width * 0.5, o.Height * 0.5, o.Depth * 0.5), o.Rotation));
                    break;
            }
        }

        private void BuildGrid()
        {
            if (Occluders.Length == 0)
            {
                _nx = _nz = 0;
                _cells = new int[0][];
                return;
            }
            double minX = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxZ = double.MinValue;
            MaxTop = 0;
            foreach (var o in Occluders)
            {
                minX = System.Math.Min(minX, o.MinX);
                minZ = System.Math.Min(minZ, o.MinZ);
                maxX = System.Math.Max(maxX, o.MaxX);
                maxZ = System.Math.Max(maxZ, o.MaxZ);
                MaxTop = System.Math.Max(MaxTop, o.MaxY);
            }
            double w = System.Math.Max(1, maxX - minX), d = System.Math.Max(1, maxZ - minZ);
            // Aim for roughly 4 occluders per cell, cells between 2 m and 50 m.
            _cell = MathUtil.Clamp(System.Math.Sqrt(w * d * 4.0 / Occluders.Length), 2.0, 50.0);
            _nx = System.Math.Max(1, (int)System.Math.Ceiling(w / _cell));
            _nz = System.Math.Max(1, (int)System.Math.Ceiling(d / _cell));
            while ((long)_nx * _nz > 4_000_000)
            {
                _cell *= 1.5;
                _nx = System.Math.Max(1, (int)System.Math.Ceiling(w / _cell));
                _nz = System.Math.Max(1, (int)System.Math.Ceiling(d / _cell));
            }
            _gridMinX = minX;
            _gridMinZ = minZ;

            var tmp = new List<int>[_nx * _nz];
            for (int i = 0; i < Occluders.Length; i++)
            {
                var o = Occluders[i];
                int x0 = CellX(o.MinX), x1 = CellX(o.MaxX), z0 = CellZ(o.MinZ), z1 = CellZ(o.MaxZ);
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        int c = z * _nx + x;
                        (tmp[c] ??= new List<int>(4)).Add(i);
                    }
            }
            _cells = new int[tmp.Length][];
            for (int i = 0; i < tmp.Length; i++) _cells[i] = tmp[i]?.ToArray();
        }

        private int CellX(double x) => MathUtil.Clamp((int)((x - _gridMinX) / _cell), 0, _nx - 1);
        private int CellZ(double z) => MathUtil.Clamp((int)((z - _gridMinZ) / _cell), 0, _nz - 1);

        /// <summary>Orients all tracker tables for the given sun direction (not thread safe).</summary>
        public void OrientTrackers(double sunAzimuth, double sunElevation)
        {
            var sun = new SunPosition { Azimuth = sunAzimuth, Elevation = sunElevation };
            for (int k = 0; k < TableRefs.Count; k++)
            {
                var (fi, ti) = TableRefs[k];
                var field = _project.Fields[fi];
                if (field.Mounting != MountingType.SingleAxisTracker) continue;
                double gcr = TableGeometry.Gcr(field, _module);
                double rot = SingleAxisTracker.RotationAngle(sun, field.TrackerAxisCompassAzimuth, field.TrackerMaxAngle, field.Backtracking, gcr);
                int oi = TableOccluderIndex[k];
                Occluders[oi].Rect = TableGeometry.Build(field, field.Tables[ti], _module, rot);
            }
        }

        public Vec3 SamplePosition(in ShadingSample s)
        {
            var r = Occluders[s.OccluderIndex].Rect;
            return r.PointAt(s.Su, s.Sv) + r.N * 0.002;
        }

        public Vec3 SampleNormal(in ShadingSample s) => Occluders[s.OccluderIndex].Rect.N;

        /// <summary>Per thread mailbox to avoid testing an occluder twice for the same ray.</summary>
        public sealed class TraceContext
        {
            public readonly int[] Stamp;
            public int Ray;

            public TraceContext(int n)
            {
                Stamp = new int[n];
            }
        }

        public TraceContext CreateContext() => new TraceContext(Occluders.Length);

        /// <summary>
        /// Shade value of a ray in [0,1]: 0 = fully visible, 1 = fully blocked
        /// (partially transparent tree crowns give intermediate values).
        /// Rays going downwards stop at the ground plane (y = 0).
        /// </summary>
        public double Trace(Vec3 o, Vec3 d, int ignoreOccluder, TraceContext ctx)
        {
            if (_nx == 0) return 0;
            int ray = ++ctx.Ray;
            if (ray == int.MaxValue)
            {
                Array.Clear(ctx.Stamp, 0, ctx.Stamp.Length);
                ctx.Ray = ray = 1;
            }
            if (ignoreOccluder >= 0) ctx.Stamp[ignoreOccluder] = ray;

            double tLimit;
            if (d.Y > 1e-9)
            {
                if (o.Y >= MaxTop) return 0;
                tLimit = (MaxTop - o.Y) / d.Y;
            }
            else if (d.Y < -1e-9)
            {
                tLimit = System.Math.Max(0, o.Y) / -d.Y;
            }
            else
            {
                tLimit = 1e5;
            }

            double transmission = 1.0;
            double h = System.Math.Sqrt(d.X * d.X + d.Z * d.Z);

            // Clip the ray against the grid bounds in XZ.
            double gx0 = _gridMinX, gz0 = _gridMinZ, gx1 = _gridMinX + _nx * _cell, gz1 = _gridMinZ + _nz * _cell;
            double tEnter = 0, tExit = tLimit;
            if (!ClipAxis(o.X, d.X, gx0, gx1, ref tEnter, ref tExit)) return 0;
            if (!ClipAxis(o.Z, d.Z, gz0, gz1, ref tEnter, ref tExit)) return 0;

            double px = o.X + d.X * tEnter, pz = o.Z + d.Z * tEnter;
            int cx = CellX(px), cz = CellZ(pz);

            if (h < 1e-9)
            {
                return TestCell(cx, cz, o, d, tLimit, ray, ctx, ref transmission);
            }

            int stepX = d.X > 0 ? 1 : (d.X < 0 ? -1 : 0);
            int stepZ = d.Z > 0 ? 1 : (d.Z < 0 ? -1 : 0);
            double tMaxX = stepX != 0 ? ((_gridMinX + (cx + (stepX > 0 ? 1 : 0)) * _cell) - o.X) / d.X : double.MaxValue;
            double tMaxZ = stepZ != 0 ? ((_gridMinZ + (cz + (stepZ > 0 ? 1 : 0)) * _cell) - o.Z) / d.Z : double.MaxValue;
            double tDeltaX = stepX != 0 ? _cell / System.Math.Abs(d.X) : double.MaxValue;
            double tDeltaZ = stepZ != 0 ? _cell / System.Math.Abs(d.Z) : double.MaxValue;

            double tCur = tEnter;
            int guard = _nx + _nz + 4;
            while (guard-- > 0 && tCur <= tExit)
            {
                if (TestCell(cx, cz, o, d, tLimit, ray, ctx, ref transmission) >= 0.999) return 1.0;
                if (tMaxX < tMaxZ)
                {
                    tCur = tMaxX;
                    tMaxX += tDeltaX;
                    cx += stepX;
                    if (cx < 0 || cx >= _nx) break;
                }
                else
                {
                    tCur = tMaxZ;
                    tMaxZ += tDeltaZ;
                    cz += stepZ;
                    if (cz < 0 || cz >= _nz) break;
                }
            }

            if (d.Y < -1e-9)
            {
                // The ray reaches the ground: ground covered by a table or obstacle is in its shadow
                // and reflects much less light than open ground.
                var g = o + d * tLimit;
                double covered = Trace(new Vec3(g.X, 0.01, g.Z), Vec3.Up, -1, ctx);
                transmission *= 1.0 - CoveredGroundDarkening * covered;
            }
            return 1.0 - transmission;
        }

        /// <summary>Relative reflection loss of ground lying under a table or obstacle.</summary>
        public const double CoveredGroundDarkening = 0.7;

        private static bool ClipAxis(double o, double d, double min, double max, ref double t0, ref double t1)
        {
            if (System.Math.Abs(d) < 1e-12) return o >= min && o <= max;
            double ta = (min - o) / d, tb = (max - o) / d;
            if (ta > tb)
            {
                double t = ta;
                ta = tb;
                tb = t;
            }
            t0 = System.Math.Max(t0, ta);
            t1 = System.Math.Min(t1, tb);
            return t0 <= t1;
        }

        /// <summary>Returns the accumulated opacity (1 - transmission) after testing one cell.</summary>
        private double TestCell(int cx, int cz, Vec3 o, Vec3 d, double tLimit, int ray, TraceContext ctx, ref double transmission)
        {
            var cell = _cells[cz * _nx + cx];
            if (cell == null) return 1.0 - transmission;
            for (int k = 0; k < cell.Length; k++)
            {
                int idx = cell[k];
                if (ctx.Stamp[idx] == ray) continue;
                ctx.Stamp[idx] = ray;
                ref Occluder occ = ref Occluders[idx];
                if (!occ.Intersects(o, d, tLimit)) continue;
                transmission *= occ.Transmission;
                if (transmission <= 0.001) return 1.0;
            }
            return 1.0 - transmission;
        }

        /// <summary>Shaded fraction of each table for one sun position (for 3D heat maps). Order: TableRefs.</summary>
        public float[] InstantTableShade(double sunAzimuth, double sunElevation)
        {
            var res = new float[TableRefs.Count];
            if (sunElevation <= 0)
            {
                for (int i = 0; i < res.Length; i++) res[i] = 1f;
                return res;
            }
            OrientTrackers(sunAzimuth, sunElevation);
            var dir = Vec3.FromAzimuthElevation(sunAzimuth, sunElevation);
            var ctx = CreateContext();
            var sum = new double[Occluders.Length];
            var cnt = new int[Occluders.Length];
            foreach (var s in Samples)
            {
                var n = SampleNormal(s);
                double shade = Vec3.Dot(n, dir) <= 0 ? 1.0 : Trace(SamplePosition(s), dir, s.OccluderIndex, ctx);
                sum[s.OccluderIndex] += shade;
                cnt[s.OccluderIndex]++;
            }
            for (int k = 0; k < TableOccluderIndex.Count; k++)
            {
                int oi = TableOccluderIndex[k];
                res[k] = cnt[oi] > 0 ? (float)(sum[oi] / cnt[oi]) : -1f;
            }
            return res;
        }
    }
}
