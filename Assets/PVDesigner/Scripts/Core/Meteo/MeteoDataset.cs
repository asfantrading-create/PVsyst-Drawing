using System;
using System.Collections.Generic;

namespace PVDesigner.Core.Meteo
{
    /// <summary>
    /// Hourly typical meteorological year. Index 0 is 1 January 00:00-01:00 UTC of a non-leap reference year.
    /// Irradiances are hourly averages in W/m2, temperatures in deg C, wind speed in m/s.
    /// </summary>
    [Serializable]
    public class MeteoDataset
    {
        public const int HoursPerYear = 8760;
        public const int ReferenceYear = 2019;

        public string Source = "";
        public string Description = "";
        public double Latitude;
        public double Longitude;
        public double Altitude;
        /// <summary>
        /// Offset in hours from the start of each record to the instant used for the sun position.
        /// 0.5 for hour-averaged data; PVGIS provides its own value (time of the satellite scan).
        /// </summary>
        public double SunTimeOffsetHours = 0.5;

        public float[] Ghi = new float[HoursPerYear];
        public float[] Dhi = new float[HoursPerYear];
        /// <summary>Optional direct normal irradiance. When all zero it is derived from GHI and DHI.</summary>
        public float[] Dni = new float[HoursPerYear];
        public float[] TempAmbient = new float[HoursPerYear];
        public float[] WindSpeed = new float[HoursPerYear];

        public static DateTime HourStartUtc(int hourIndex) =>
            new DateTime(ReferenceYear, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(hourIndex);

        public static int HourIndex(int month, int day, int hour)
        {
            var dt = new DateTime(ReferenceYear, month, day, hour, 0, 0, DateTimeKind.Utc);
            return (int)(dt - new DateTime(ReferenceYear, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalHours;
        }

        public bool IsValid()
        {
            return Ghi != null && Ghi.Length == HoursPerYear
                && Dhi != null && Dhi.Length == HoursPerYear
                && TempAmbient != null && TempAmbient.Length == HoursPerYear
                && WindSpeed != null && WindSpeed.Length == HoursPerYear;
        }

        public bool HasDni()
        {
            if (Dni == null || Dni.Length != HoursPerYear) return false;
            for (int i = 0; i < Dni.Length; i++) if (Dni[i] > 0) return true;
            return false;
        }

        public MonthlyMeteoSummary[] MonthlySummary(double timeZoneHours)
        {
            var res = new MonthlyMeteoSummary[12];
            var count = new int[12];
            for (int m = 0; m < 12; m++) res[m] = new MonthlyMeteoSummary { Month = m + 1 };
            for (int h = 0; h < HoursPerYear; h++)
            {
                int m = HourStartUtc(h).AddHours(timeZoneHours).Month - 1;
                res[m].GlobalHorizontal += Ghi[h] / 1000.0;
                res[m].DiffuseHorizontal += Dhi[h] / 1000.0;
                res[m].TempAmbient += TempAmbient[h];
                res[m].WindSpeed += WindSpeed[h];
                count[m]++;
            }
            for (int m = 0; m < 12; m++)
            {
                if (count[m] == 0) continue;
                res[m].TempAmbient /= count[m];
                res[m].WindSpeed /= count[m];
            }
            return res;
        }

        public double AnnualGhiKWh()
        {
            double s = 0;
            for (int i = 0; i < Ghi.Length; i++) s += Ghi[i];
            return s / 1000.0;
        }

        public double MinTemperature()
        {
            double m = double.MaxValue;
            foreach (var t in TempAmbient) if (t < m) m = t;
            return m;
        }

        public double MaxTemperature()
        {
            double m = double.MinValue;
            foreach (var t in TempAmbient) if (t > m) m = t;
            return m;
        }
    }

    [Serializable]
    public class MonthlyMeteoSummary
    {
        public int Month;
        /// <summary>kWh/m2</summary>
        public double GlobalHorizontal;
        /// <summary>kWh/m2</summary>
        public double DiffuseHorizontal;
        /// <summary>deg C</summary>
        public double TempAmbient;
        /// <summary>m/s</summary>
        public double WindSpeed;

        public static readonly string[] MonthNames =
            { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        public static IReadOnlyList<string> Names => MonthNames;
    }
}
