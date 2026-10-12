using System;
using PVDesigner.Core.Math;

namespace PVDesigner.Core.Solar
{
    public struct SunPosition
    {
        /// <summary>Compass azimuth in degrees (0 = North, 90 = East, 180 = South).</summary>
        public double Azimuth;
        /// <summary>Apparent elevation above the horizon in degrees (refraction corrected).</summary>
        public double Elevation;
        /// <summary>Apparent zenith angle in degrees.</summary>
        public double Zenith => 90.0 - Elevation;
        public double DeclinationDeg;
        public double EquationOfTimeMin;
        public double HourAngleDeg;

        public bool IsUp => Elevation > 0;

        /// <summary>Unit vector pointing from the scene towards the sun (X east, Y up, Z north).</summary>
        public Vec3 Direction => Vec3.FromAzimuthElevation(Azimuth, Elevation);
    }

    /// <summary>
    /// NOAA solar position algorithm (accuracy about 0.01 degree between 1901 and 2099),
    /// with Saemundsson style atmospheric refraction correction.
    /// </summary>
    public static class SolarPosition
    {
        private static readonly DateTime J2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        public static double JulianDay(DateTime utc) => (utc - J2000).TotalDays + 2451545.0;

        public static SunPosition Compute(DateTime utc, double latitudeDeg, double longitudeDeg)
        {
            double jd = JulianDay(utc);
            double jc = (jd - 2451545.0) / 36525.0;

            double l0 = MathUtil.Wrap360(280.46646 + jc * (36000.76983 + jc * 0.0003032));
            double m = 357.52911 + jc * (35999.05029 - 0.0001537 * jc);
            double e = 0.016708634 - jc * (0.000042037 + 0.0000001267 * jc);

            double c = MathUtil.SinD(m) * (1.914602 - jc * (0.004817 + 0.000014 * jc))
                       + MathUtil.SinD(2 * m) * (0.019993 - 0.000101 * jc)
                       + MathUtil.SinD(3 * m) * 0.000289;
            double trueLong = l0 + c;
            double omega = 125.04 - 1934.136 * jc;
            double appLong = trueLong - 0.00569 - 0.00478 * MathUtil.SinD(omega);

            double meanObliq = 23.0 + (26.0 + (21.448 - jc * (46.815 + jc * (0.00059 - jc * 0.001813))) / 60.0) / 60.0;
            double obliq = meanObliq + 0.00256 * MathUtil.CosD(omega);

            double decl = System.Math.Asin(MathUtil.SinD(obliq) * MathUtil.SinD(appLong)) * MathUtil.Rad2Deg;

            double y = MathUtil.TanD(obliq / 2.0);
            y *= y;
            double eqTime = 4.0 * MathUtil.Rad2Deg * (
                y * MathUtil.SinD(2 * l0)
                - 2 * e * MathUtil.SinD(m)
                + 4 * e * y * MathUtil.SinD(m) * MathUtil.CosD(2 * l0)
                - 0.5 * y * y * MathUtil.SinD(4 * l0)
                - 1.25 * e * e * MathUtil.SinD(2 * m));

            double minutes = utc.TimeOfDay.TotalMinutes;
            double tst = (minutes + eqTime + 4.0 * longitudeDeg) % 1440.0;
            if (tst < 0) tst += 1440.0;
            double ha = tst / 4.0 < 0 ? tst / 4.0 + 180.0 : tst / 4.0 - 180.0;

            double cosZen = MathUtil.SinD(latitudeDeg) * MathUtil.SinD(decl)
                            + MathUtil.CosD(latitudeDeg) * MathUtil.CosD(decl) * MathUtil.CosD(ha);
            cosZen = MathUtil.Clamp(cosZen, -1, 1);
            double zen = System.Math.Acos(cosZen) * MathUtil.Rad2Deg;

            double az;
            double denom = MathUtil.CosD(latitudeDeg) * MathUtil.SinD(zen);
            if (System.Math.Abs(denom) < 1e-9)
            {
                az = latitudeDeg > 0 ? 180.0 : 0.0;
            }
            else
            {
                double cosAz = MathUtil.Clamp((MathUtil.SinD(latitudeDeg) * MathUtil.CosD(zen) - MathUtil.SinD(decl)) / denom, -1, 1);
                double a = System.Math.Acos(cosAz) * MathUtil.Rad2Deg;
                az = ha > 0 ? MathUtil.Wrap360(a + 180.0) : MathUtil.Wrap360(540.0 - a);
            }

            double elev = 90.0 - zen;
            elev += Refraction(elev);

            return new SunPosition
            {
                Azimuth = az,
                Elevation = elev,
                DeclinationDeg = decl,
                EquationOfTimeMin = eqTime,
                HourAngleDeg = ha
            };
        }

        /// <summary>Atmospheric refraction in degrees for a geometric elevation (NOAA approximation).</summary>
        public static double Refraction(double elevDeg)
        {
            if (elevDeg > 85.0) return 0;
            double te = MathUtil.TanD(elevDeg);
            double r;
            if (elevDeg > 5.0) r = 58.1 / te - 0.07 / (te * te * te) + 0.000086 / System.Math.Pow(te, 5);
            else if (elevDeg > -0.575) r = 1735.0 + elevDeg * (-518.2 + elevDeg * (103.4 + elevDeg * (-12.79 + elevDeg * 0.711)));
            else r = -20.772 / te;
            return r / 3600.0;
        }

        /// <summary>Extraterrestrial normal irradiance (W/m2) for a day of year (Spencer).</summary>
        public static double ExtraterrestrialIrradiance(int dayOfYear)
        {
            double b = 2 * System.Math.PI * (dayOfYear - 1) / 365.0;
            double r = 1.00011 + 0.034221 * System.Math.Cos(b) + 0.00128 * System.Math.Sin(b)
                       + 0.000719 * System.Math.Cos(2 * b) + 0.000077 * System.Math.Sin(2 * b);
            return 1361.0 * r;
        }

        /// <summary>Kasten and Young (1989) relative air mass for an apparent zenith angle (degrees).</summary>
        public static double RelativeAirMass(double zenithDeg)
        {
            if (zenithDeg >= 90) return 38.0;
            return 1.0 / (MathUtil.CosD(zenithDeg) + 0.50572 * System.Math.Pow(96.07995 - zenithDeg, -1.6364));
        }

        /// <summary>Sunrise and sunset (UTC hours of the day) using the standard -0.833 degree horizon.</summary>
        public static bool SunriseSunset(DateTime dateUtc, double lat, double lon, out double sunriseUtcHours, out double sunsetUtcHours)
        {
            var noon = new DateTime(dateUtc.Year, dateUtc.Month, dateUtc.Day, 12, 0, 0, DateTimeKind.Utc);
            var p = Compute(noon, lat, lon);
            double cosH = (MathUtil.SinD(-0.833) - MathUtil.SinD(lat) * MathUtil.SinD(p.DeclinationDeg))
                          / (MathUtil.CosD(lat) * MathUtil.CosD(p.DeclinationDeg));
            double solarNoonUtc = (720.0 - 4.0 * lon - p.EquationOfTimeMin) / 60.0;
            if (cosH > 1 || cosH < -1)
            {
                sunriseUtcHours = sunsetUtcHours = solarNoonUtc;
                return false;
            }
            double h = System.Math.Acos(cosH) * MathUtil.Rad2Deg / 15.0;
            sunriseUtcHours = solarNoonUtc - h;
            sunsetUtcHours = solarNoonUtc + h;
            return true;
        }
    }
}
