using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PVDesigner.Core.Math;
using PVDesigner.Core.Model;

namespace PVDesigner.Core.Shading
{
    /// <summary>
    /// Beam shading loss table as a function of sun azimuth / elevation (like the PVsyst
    /// "shading factor table"), plus diffuse and albedo shading factors.
    /// </summary>
    [Serializable]
    public class ShadingFactorTable
    {
        /// <summary>Compass azimuths, 0..360 step 10 (360 duplicates 0 for interpolation).</summary>
        public double[] Azimuths;
        public double[] Elevations;
        /// <summary>Linear beam loss [azimuthIndex, elevationIndex] in 0..1.</summary>
        public float[,] LinearLoss;
        /// <summary>Electrical (string-wise) beam loss in 0..1.</summary>
        public float[,] ElectricalLoss;
        /// <summary>Fraction of isotropic sky diffuse reaching the modules (1 = no loss).</summary>
        public double DiffuseFactor = 1;
        /// <summary>Fraction of ground reflected irradiance reaching the modules (1 = no loss).</summary>
        public double AlbedoFactor = 1;

        public static ShadingFactorTable NoShading()
        {
            var t = new ShadingFactorTable
            {
                Azimuths = new double[] { 0, 360 },
                Elevations = new double[] { 0, 90 },
                LinearLoss = new float[2, 2],
                ElectricalLoss = new float[2, 2]
            };
            return t;
        }

        /// <summary>Beam loss for a sun position; <paramref name="electricalFraction"/> blends towards the electrical table.</summary>
        public double BeamLoss(double azimuth, double elevation, ShadingMode mode, double electricalFraction)
        {
            if (mode == ShadingMode.None) return 0;
            double lin = Interpolate(LinearLoss, azimuth, elevation);
            if (mode == ShadingMode.Linear) return lin;
            double el = Interpolate(ElectricalLoss, azimuth, elevation);
            return MathUtil.Clamp01(lin + (System.Math.Max(el, lin) - lin) * MathUtil.Clamp01(electricalFraction));
        }

        public double Interpolate(float[,] table, double azimuth, double elevation)
        {
            azimuth = MathUtil.Wrap360(azimuth);
            int na = Azimuths.Length, ne = Elevations.Length;
            int ia = 0;
            while (ia < na - 2 && Azimuths[ia + 1] < azimuth) ia++;
            double fa = MathUtil.Clamp01((azimuth - Azimuths[ia]) / (Azimuths[ia + 1] - Azimuths[ia]));

            if (elevation <= Elevations[0]) return MathUtil.Lerp(table[ia, 0], table[ia + 1, 0], fa);
            if (elevation >= Elevations[ne - 1]) return MathUtil.Lerp(table[ia, ne - 1], table[ia + 1, ne - 1], fa);
            int ie = 0;
            while (ie < ne - 2 && Elevations[ie + 1] < elevation) ie++;
            double fe = MathUtil.Clamp01((elevation - Elevations[ie]) / (Elevations[ie + 1] - Elevations[ie]));
            double a = MathUtil.Lerp(table[ia, ie], table[ia, ie + 1], fe);
            double b = MathUtil.Lerp(table[ia + 1, ie], table[ia + 1, ie + 1], fe);
            return MathUtil.Lerp(a, b, fa);
        }
    }

    public class ShadingResult
    {
        /// <summary>Weighted over all fields (by module count).</summary>
        public ShadingFactorTable Global;
        public ShadingFactorTable[] PerField;
        public int SampleCount;
        public int OccluderCount;
        public double ComputeSeconds;
    }

    public static class ShadingCalculator
    {
        public static readonly double[] TableElevations = { 2, 5, 10, 15, 20, 25, 30, 40, 50, 60, 70, 80, 90 };
        public const double AzimuthStep = 10;
        private static readonly double[] GroundElevations = { -10, -30, -50, -70 };
        private const double GroundAzimuthStep = 30;

        public static ShadingResult Compute(PVProject project, ShadingScene scene, IProgress<float> progress = null,
            CancellationToken cancel = default)
        {
            var sw = Stopwatch.StartNew();
            int nf = System.Math.Max(1, project.Fields.Count);
            int na = (int)(360 / AzimuthStep) + 1;
            int ne = TableElevations.Length;

            var azimuths = new double[na];
            for (int i = 0; i < na; i++) azimuths[i] = i * AzimuthStep;

            var perField = new ShadingFactorTable[nf];
            for (int f = 0; f < nf; f++)
            {
                perField[f] = new ShadingFactorTable
                {
                    Azimuths = azimuths,
                    Elevations = (double[])TableElevations.Clone(),
                    LinearLoss = new float[na, ne],
                    ElectricalLoss = new float[na, ne]
                };
            }

            // Field weights (module count) and sample counts.
            var fieldSamples = new int[nf];
            foreach (var s in scene.Samples) fieldSamples[s.FieldIndex]++;
            var fieldGroups = new int[nf];
            foreach (var g in scene.GroupField) fieldGroups[g]++;

            int groundDirs = (int)(360 / GroundAzimuthStep) * GroundElevations.Length;
            int totalSteps = (na - 1) * ne + groundDirs;
            int step = 0;

            var sampleShade = new double[scene.Samples.Count];
            var groupMax = new double[scene.GroupCount];

            // Diffuse weights accumulated per field.
            var diffW = new double[nf];
            var diffV = new double[nf];
            var albW = new double[nf];
            var albV = new double[nf];

            for (int ia = 0; ia < na - 1; ia++)
            {
                for (int ie = 0; ie < ne; ie++)
                {
                    cancel.ThrowIfCancellationRequested();
                    double az = azimuths[ia], el = TableElevations[ie];
                    TraceDirection(scene, az, el, sampleShade, az, el);

                    var sumF = new double[nf];
                    Array.Clear(groupMax, 0, groupMax.Length);
                    for (int i = 0; i < sampleShade.Length; i++)
                    {
                        var s = scene.Samples[i];
                        sumF[s.FieldIndex] += sampleShade[i];
                        if (sampleShade[i] > groupMax[s.GroupIndex]) groupMax[s.GroupIndex] = sampleShade[i];
                    }
                    var grpF = new double[nf];
                    for (int g = 0; g < groupMax.Length; g++) grpF[scene.GroupField[g]] += groupMax[g];

                    // Elevation band width for the diffuse integration.
                    double lo = ie == 0 ? 0 : (TableElevations[ie - 1] + el) * 0.5;
                    double hi = ie == ne - 1 ? 90 : (TableElevations[ie + 1] + el) * 0.5;
                    double band = MathUtil.SinD(hi) - MathUtil.SinD(lo); // integral of cos(el) d(el)
                    var dir = Vec3.FromAzimuthElevation(az, el);

                    for (int f = 0; f < nf; f++)
                    {
                        double lin = fieldSamples[f] > 0 ? sumF[f] / fieldSamples[f] : 0;
                        double ele = fieldGroups[f] > 0 ? grpF[f] / fieldGroups[f] : 0;
                        perField[f].LinearLoss[ia, ie] = (float)lin;
                        perField[f].ElectricalLoss[ia, ie] = (float)System.Math.Max(lin, ele);

                        double cosInc = Vec3.Dot(dir, FieldMeanNormal(project, f));
                        if (cosInc > 0)
                        {
                            double w = band * cosInc;
                            diffW[f] += w;
                            diffV[f] += w * (1 - lin);
                        }
                    }
                    progress?.Report(++step / (float)totalSteps);
                }
            }
            for (int f = 0; f < nf; f++)
            {
                for (int ie = 0; ie < ne; ie++)
                {
                    perField[f].LinearLoss[na - 1, ie] = perField[f].LinearLoss[0, ie];
                    perField[f].ElectricalLoss[na - 1, ie] = perField[f].ElectricalLoss[0, ie];
                }
                perField[f].DiffuseFactor = diffW[f] > 0 ? diffV[f] / diffW[f] : 1;
            }

            // Ground reflected (albedo) visibility.
            for (int ia = 0; ia < (int)(360 / GroundAzimuthStep); ia++)
            {
                foreach (double el in GroundElevations)
                {
                    cancel.ThrowIfCancellationRequested();
                    double az = ia * GroundAzimuthStep;
                    // Trackers are evaluated in a mid-day position for the ground view.
                    TraceDirection(scene, az, el, sampleShade, 180, 60);
                    var sumF = new double[nf];
                    for (int i = 0; i < sampleShade.Length; i++) sumF[scene.Samples[i].FieldIndex] += sampleShade[i];
                    var dir = Vec3.FromAzimuthElevation(az, el);
                    double w0 = MathUtil.CosD(el);
                    for (int f = 0; f < nf; f++)
                    {
                        double cosInc = Vec3.Dot(dir, FieldMeanNormal(project, f));
                        if (cosInc <= 0) continue;
                        double lin = fieldSamples[f] > 0 ? sumF[f] / fieldSamples[f] : 0;
                        albW[f] += w0 * cosInc;
                        albV[f] += w0 * cosInc * (1 - lin);
                    }
                    progress?.Report(++step / (float)totalSteps);
                }
            }
            for (int f = 0; f < nf; f++) perField[f].AlbedoFactor = albW[f] > 0 ? albV[f] / albW[f] : 1;

            var result = new ShadingResult
            {
                PerField = perField,
                Global = Combine(project, perField),
                SampleCount = scene.Samples.Count,
                OccluderCount = scene.Occluders.Length,
                ComputeSeconds = sw.Elapsed.TotalSeconds
            };
            return result;
        }

        private static void TraceDirection(ShadingScene scene, double az, double el, double[] sampleShade,
            double trackerSunAz, double trackerSunEl)
        {
            scene.OrientTrackers(trackerSunAz, System.Math.Max(trackerSunEl, 0.5));
            var dir = Vec3.FromAzimuthElevation(az, el);
            Parallel.For(0, scene.Samples.Count,
                () => scene.CreateContext(),
                (i, state, ctx) =>
                {
                    var s = scene.Samples[i];
                    var n = scene.SampleNormal(s);
                    // Sun behind the module plane: beam does not reach the front face anyway.
                    sampleShade[i] = Vec3.Dot(n, dir) <= 0 ? 0 : scene.Trace(scene.SamplePosition(s), dir, s.OccluderIndex, ctx);
                    return ctx;
                },
                ctx => { });
        }

        /// <summary>Mean plane normal used for diffuse / albedo weighting (trackers use horizontal).</summary>
        public static Vec3 FieldMeanNormal(PVProject project, int fieldIndex)
        {
            if (fieldIndex >= project.Fields.Count) return Vec3.Up;
            var f = project.Fields[fieldIndex];
            if (f.Mounting == MountingType.SingleAxisTracker) return Vec3.Up;
            return Solar.Irradiance.PlaneNormal(f.Tilt, f.CompassAzimuth);
        }

        private static ShadingFactorTable Combine(PVProject project, ShadingFactorTable[] perField)
        {
            if (perField.Length == 1) return perField[0];
            var g = new ShadingFactorTable
            {
                Azimuths = perField[0].Azimuths,
                Elevations = perField[0].Elevations,
                LinearLoss = new float[perField[0].Azimuths.Length, perField[0].Elevations.Length],
                ElectricalLoss = new float[perField[0].Azimuths.Length, perField[0].Elevations.Length],
                DiffuseFactor = 0,
                AlbedoFactor = 0
            };
            double total = 0;
            for (int f = 0; f < perField.Length; f++) total += f < project.Fields.Count ? project.Fields[f].ModuleCount : 0;
            if (total <= 0) return perField[0];
            for (int f = 0; f < perField.Length; f++)
            {
                double w = project.Fields[f].ModuleCount / total;
                for (int a = 0; a < g.Azimuths.Length; a++)
                    for (int e = 0; e < g.Elevations.Length; e++)
                    {
                        g.LinearLoss[a, e] += (float)(w * perField[f].LinearLoss[a, e]);
                        g.ElectricalLoss[a, e] += (float)(w * perField[f].ElectricalLoss[a, e]);
                    }
                g.DiffuseFactor += w * perField[f].DiffuseFactor;
                g.AlbedoFactor += w * perField[f].AlbedoFactor;
            }
            return g;
        }
    }
}
