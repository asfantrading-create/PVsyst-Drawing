using PVDesigner.Core.Math;

namespace PVDesigner.Core.Solar
{
    public enum TranspositionModel
    {
        Perez,
        HayDavies,
        Isotropic
    }

    /// <summary>Irradiance on a tilted plane split by component (W/m2).</summary>
    public struct PlaneIrradiance
    {
        public double Beam;
        public double SkyDiffuse;
        public double GroundReflected;
        public double Global => Beam + SkyDiffuse + GroundReflected;
    }

    public static class Irradiance
    {
        /// <summary>Cosine of the angle of incidence between the sun and a plane normal.</summary>
        public static double CosIncidence(Vec3 sunDir, Vec3 planeNormal) => Vec3.Dot(sunDir, planeNormal);

        /// <summary>Plane normal for a surface with tilt (deg) facing a compass azimuth (deg).</summary>
        public static Vec3 PlaneNormal(double tiltDeg, double compassAzimuthDeg)
        {
            double st = MathUtil.SinD(tiltDeg);
            return new Vec3(st * MathUtil.SinD(compassAzimuthDeg), MathUtil.CosD(tiltDeg), st * MathUtil.CosD(compassAzimuthDeg));
        }

        /// <summary>Erbs et al. (1982) diffuse fraction from the clearness index.</summary>
        public static double ErbsDiffuseFraction(double kt)
        {
            if (kt <= 0.22) return 1.0 - 0.09 * kt;
            if (kt <= 0.80)
                return 0.9511 - 0.1604 * kt + 4.388 * kt * kt - 16.638 * kt * kt * kt + 12.336 * kt * kt * kt * kt;
            return 0.165;
        }

        /// <summary>Splits global horizontal irradiance into diffuse and direct normal components.</summary>
        public static void DecomposeErbs(double ghi, double zenithDeg, int dayOfYear, out double dhi, out double dni)
        {
            double cosZ = MathUtil.CosD(zenithDeg);
            if (ghi <= 0 || cosZ <= 0.0175)
            {
                dhi = System.Math.Max(0, ghi);
                dni = 0;
                return;
            }
            double e0 = SolarPosition.ExtraterrestrialIrradiance(dayOfYear);
            double kt = MathUtil.Clamp(ghi / (e0 * cosZ), 0, 1.2);
            dhi = ghi * ErbsDiffuseFraction(kt);
            dni = System.Math.Max(0, (ghi - dhi) / cosZ);
            dni = System.Math.Min(dni, e0);
        }

        /// <summary>Simple clear sky global horizontal irradiance (Haurwitz 1945).</summary>
        public static double ClearSkyGhiHaurwitz(double zenithDeg)
        {
            double cz = MathUtil.CosD(zenithDeg);
            if (cz <= 0) return 0;
            return 1098.0 * cz * System.Math.Exp(-0.059 / cz);
        }

        /// <summary>Ground reflected irradiance on a tilted plane (isotropic ground).</summary>
        public static double GroundReflected(double ghi, double albedo, double tiltDeg) =>
            ghi * albedo * (1.0 - MathUtil.CosD(tiltDeg)) * 0.5;

        public static double IsotropicSky(double dhi, double tiltDeg) => dhi * (1.0 + MathUtil.CosD(tiltDeg)) * 0.5;

        public static double HayDaviesSky(double dhi, double dni, double cosAoi, double zenithDeg, double tiltDeg, int dayOfYear)
        {
            double e0 = SolarPosition.ExtraterrestrialIrradiance(dayOfYear);
            double ai = MathUtil.Clamp(dni / e0, 0, 1);
            double cosZ = System.Math.Max(MathUtil.CosD(zenithDeg), 0.01745);
            double rb = System.Math.Max(cosAoi, 0) / cosZ;
            return dhi * (ai * rb + (1 - ai) * (1.0 + MathUtil.CosD(tiltDeg)) * 0.5);
        }

        // Perez et al. (1990) "allsitescomposite1990" coefficients.
        private static readonly double[] EpsBins = { 1.065, 1.23, 1.5, 1.95, 2.8, 4.5, 6.2 };
        private static readonly double[,] PerezF =
        {
            // F11,   F12,    F13,    F21,    F22,    F23
            { -0.008, 0.588, -0.062, -0.060, 0.072, -0.022 },
            { 0.130, 0.683, -0.151, -0.019, 0.066, -0.029 },
            { 0.330, 0.487, -0.221, 0.055, -0.064, -0.026 },
            { 0.568, 0.187, -0.295, 0.109, -0.152, -0.014 },
            { 0.873, -0.392, -0.362, 0.226, -0.462, 0.001 },
            { 1.132, -1.237, -0.412, 0.288, -0.823, 0.056 },
            { 1.060, -1.600, -0.359, 0.264, -1.127, 0.131 },
            { 0.678, -0.327, -0.250, 0.156, -1.377, 0.251 }
        };

        public static double PerezSky(double dhi, double dni, double cosAoi, double zenithDeg, double tiltDeg, int dayOfYear)
        {
            if (dhi <= 0) return 0;
            double z = zenithDeg * MathUtil.Deg2Rad;
            const double kappa = 1.041;
            double z3 = kappa * z * z * z;
            double eps = ((dhi + dni) / dhi + z3) / (1.0 + z3);

            int bin = 0;
            while (bin < EpsBins.Length && eps >= EpsBins[bin]) bin++;

            double am = SolarPosition.RelativeAirMass(zenithDeg);
            double e0 = SolarPosition.ExtraterrestrialIrradiance(dayOfYear);
            double delta = dhi * am / e0;

            double f1 = System.Math.Max(0, PerezF[bin, 0] + PerezF[bin, 1] * delta + PerezF[bin, 2] * z);
            double f2 = PerezF[bin, 3] + PerezF[bin, 4] * delta + PerezF[bin, 5] * z;

            double a = System.Math.Max(0, cosAoi);
            double b = System.Math.Max(MathUtil.CosD(85), MathUtil.CosD(zenithDeg));

            double sky = dhi * ((1 - f1) * (1 + MathUtil.CosD(tiltDeg)) * 0.5 + f1 * a / b + f2 * MathUtil.SinD(tiltDeg));
            return System.Math.Max(0, sky);
        }

        public static PlaneIrradiance Transpose(
            TranspositionModel model, double ghi, double dhi, double dni,
            SunPosition sun, Vec3 planeNormal, double albedo, int dayOfYear)
        {
            double tilt = System.Math.Acos(MathUtil.Clamp(planeNormal.Y, -1, 1)) * MathUtil.Rad2Deg;
            double cosAoi = sun.IsUp ? CosIncidence(sun.Direction, planeNormal) : 0;

            var r = new PlaneIrradiance
            {
                Beam = System.Math.Max(0, dni * cosAoi),
                GroundReflected = GroundReflected(ghi, albedo, tilt)
            };
            switch (model)
            {
                case TranspositionModel.Perez:
                    r.SkyDiffuse = PerezSky(dhi, dni, cosAoi, sun.Zenith, tilt, dayOfYear);
                    break;
                case TranspositionModel.HayDavies:
                    r.SkyDiffuse = HayDaviesSky(dhi, dni, cosAoi, sun.Zenith, tilt, dayOfYear);
                    break;
                default:
                    r.SkyDiffuse = IsotropicSky(dhi, tilt);
                    break;
            }
            return r;
        }

        /// <summary>ASHRAE incidence angle modifier.</summary>
        public static double IamAshrae(double aoiDeg, double b0)
        {
            if (aoiDeg >= 90) return 0;
            double c = MathUtil.CosD(aoiDeg);
            if (c <= 1e-6) return 0;
            return MathUtil.Clamp(1.0 - b0 * (1.0 / c - 1.0), 0, 1);
        }

        /// <summary>Brandemuehl and Beckman equivalent incidence angle for isotropic sky diffuse (deg).</summary>
        public static double EquivalentSkyDiffuseAngle(double tiltDeg) =>
            59.7 - 0.1388 * tiltDeg + 0.001497 * tiltDeg * tiltDeg;

        /// <summary>Brandemuehl and Beckman equivalent incidence angle for ground reflected irradiance (deg).</summary>
        public static double EquivalentGroundAngle(double tiltDeg) =>
            90.0 - 0.5788 * tiltDeg + 0.002693 * tiltDeg * tiltDeg;
    }
}
