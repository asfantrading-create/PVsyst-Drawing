using System;
using PVDesigner.Core.Math;
using PVDesigner.Core.Solar;

namespace PVDesigner.Core.Meteo
{
    /// <summary>
    /// Generates an approximate hourly year from a clear sky model and a mean clear sky ratio.
    /// Intended for offline previews only: use measured or PVGIS data for bankable studies.
    /// </summary>
    public static class SyntheticMeteoGenerator
    {
        public class Options
        {
            /// <summary>Average ratio between actual and clear sky irradiation (0.4 cloudy .. 0.85 desert).</summary>
            public double ClearSkyRatio = 0.75;
            /// <summary>Annual mean ambient temperature in deg C. NaN = estimated from latitude.</summary>
            public double AnnualMeanTemp = double.NaN;
            /// <summary>Half of the difference between the warmest and coldest monthly means. NaN = estimated.</summary>
            public double SeasonalAmplitude = double.NaN;
            /// <summary>Half of the typical day/night temperature swing.</summary>
            public double DailyAmplitude = 6.0;
            public double MeanWindSpeed = 2.5;
            public int Seed = 12345;
        }

        public static MeteoDataset Generate(double lat, double lon, double altitude, Options options = null)
        {
            options ??= new Options();
            double absLat = System.Math.Abs(lat);
            double tMean = double.IsNaN(options.AnnualMeanTemp) ? 28.0 - 0.45 * System.Math.Max(0, absLat - 12.0) - altitude * 0.0065 : options.AnnualMeanTemp;
            double tSeason = double.IsNaN(options.SeasonalAmplitude) ? MathUtil.Clamp(2.0 + absLat * 0.25, 2, 18) : options.SeasonalAmplitude;
            // Warmest day of year: ~ 200 (mid July) north, ~ 20 (mid January) south.
            int warmestDay = lat >= 0 ? 200 : 17;

            var rng = new Random(options.Seed);
            var ds = new MeteoDataset
            {
                Source = "Synthetic",
                Description = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Synthetic clear-sky based year (ratio {0:0.00}) - approximate", options.ClearSkyRatio),
                Latitude = lat,
                Longitude = lon,
                Altitude = altitude,
                SunTimeOffsetHours = 0.5
            };

            double dayFactor = options.ClearSkyRatio;
            int currentDay = -1;
            double windDay = options.MeanWindSpeed;
            for (int h = 0; h < MeteoDataset.HoursPerYear; h++)
            {
                DateTime t = MeteoDataset.HourStartUtc(h).AddMinutes(30);
                int doy = t.DayOfYear;
                if (doy != currentDay)
                {
                    currentDay = doy;
                    // Daily cloudiness: mostly clear days with occasional overcast ones.
                    double u = rng.NextDouble();
                    double spread = (1.0 - options.ClearSkyRatio);
                    dayFactor = MathUtil.Clamp(options.ClearSkyRatio + spread * 1.6 * (u - 0.35), 0.15, 1.0);
                    windDay = System.Math.Max(0.3, options.MeanWindSpeed * (0.6 + 0.8 * rng.NextDouble()));
                }

                var sun = SolarPosition.Compute(t, lat, lon);
                double ghi = 0, dhi = 0, dni = 0;
                if (sun.Elevation > 0)
                {
                    double clear = Irradiance.ClearSkyGhiHaurwitz(sun.Zenith);
                    double altitudeGain = 1.0 + System.Math.Min(altitude, 4000) * 0.00005;
                    double noise = 1.0 + 0.05 * (rng.NextDouble() - 0.5);
                    ghi = clear * dayFactor * altitudeGain * noise;
                    Irradiance.DecomposeErbs(ghi, sun.Zenith, doy, out dhi, out dni);
                }

                double localSolarHour = (t.TimeOfDay.TotalHours + lon / 15.0 + 24.0) % 24.0;
                double seasonal = tSeason * System.Math.Cos(2 * System.Math.PI * (doy - warmestDay) / 365.0);
                double daily = options.DailyAmplitude * System.Math.Cos(2 * System.Math.PI * (localSolarHour - 15.0) / 24.0);
                double temp = tMean + seasonal + daily * (0.6 + 0.4 * dayFactor);

                ds.Ghi[h] = (float)ghi;
                ds.Dhi[h] = (float)dhi;
                ds.Dni[h] = (float)dni;
                ds.TempAmbient[h] = (float)temp;
                ds.WindSpeed[h] = (float)windDay;
            }
            return ds;
        }
    }
}
