namespace PVDesigner.Core.Math
{
    public static class MathUtil
    {
        public const double Deg2Rad = System.Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / System.Math.PI;

        public static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
        public static double Clamp01(double v) => Clamp(v, 0, 1);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public static double SinD(double deg) => System.Math.Sin(deg * Deg2Rad);
        public static double CosD(double deg) => System.Math.Cos(deg * Deg2Rad);
        public static double TanD(double deg) => System.Math.Tan(deg * Deg2Rad);

        /// <summary>Wraps an angle to [0, 360).</summary>
        public static double Wrap360(double deg)
        {
            deg %= 360.0;
            return deg < 0 ? deg + 360.0 : deg;
        }

        /// <summary>Wraps an angle to (-180, 180].</summary>
        public static double Wrap180(double deg)
        {
            deg = Wrap360(deg);
            return deg > 180.0 ? deg - 360.0 : deg;
        }

        /// <summary>
        /// Converts a PVsyst style plane azimuth (0 = South, -90 = East, +90 = West)
        /// to a compass azimuth (0 = North, 90 = East, 180 = South).
        /// </summary>
        public static double PvsystToCompassAzimuth(double pvsystAzimuth) => Wrap360(180.0 + pvsystAzimuth);

        public static double CompassToPvsystAzimuth(double compassAzimuth) => Wrap180(compassAzimuth - 180.0);
    }
}
