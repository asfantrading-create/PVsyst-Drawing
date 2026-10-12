using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace PVDesigner.Core.Meteo
{
    /// <summary>
    /// Parses the JSON typical meteorological year returned by the EU JRC PVGIS API
    /// (https://re.jrc.ec.europa.eu/api/v5_3/tmy?lat=..&amp;lon=..&amp;outputformat=json).
    /// </summary>
    public static class PvgisTmyParser
    {
        public static string BuildUrl(double lat, double lon, string apiVersion = "v5_3") =>
            string.Format(CultureInfo.InvariantCulture,
                "https://re.jrc.ec.europa.eu/api/{0}/tmy?lat={1:0.#####}&lon={2:0.#####}&outputformat=json",
                apiVersion, lat, lon);

        public static MeteoDataset Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("Empty PVGIS response.");
            var root = JObject.Parse(json);

            var msg = root["message"];
            if (root["outputs"] == null)
                throw new FormatException("PVGIS error: " + (msg?.ToString() ?? "unexpected response"));

            var hourly = root["outputs"]?["tmy_hourly"] as JArray;
            if (hourly == null || hourly.Count < 8000)
                throw new FormatException("PVGIS response does not contain hourly TMY data.");

            var ds = new MeteoDataset { Source = "PVGIS TMY" };
            var loc = root["inputs"]?["location"];
            if (loc != null)
            {
                ds.Latitude = loc.Value<double?>("latitude") ?? 0;
                ds.Longitude = loc.Value<double?>("longitude") ?? 0;
                ds.Altitude = loc.Value<double?>("elevation") ?? 0;
                ds.SunTimeOffsetHours = loc.Value<double?>("irradiance_time_offset") ?? 0.5;
            }
            var meta = root["inputs"]?["meteo_data"];
            ds.Description = meta != null
                ? "PVGIS TMY " + (meta.Value<string>("radiation_db") ?? "") + " " + (meta.Value<int?>("year_min") ?? 0) + "-" + (meta.Value<int?>("year_max") ?? 0)
                : "PVGIS TMY";

            var filled = new bool[MeteoDataset.HoursPerYear];
            foreach (var rec in hourly)
            {
                string t = rec.Value<string>("time(UTC)");
                if (!TryParseTime(t, out int month, out int day, out int hour)) continue;
                if (month == 2 && day == 29) continue;
                int idx = MeteoDataset.HourIndex(month, day, hour);
                if (idx < 0 || idx >= MeteoDataset.HoursPerYear) continue;
                ds.Ghi[idx] = (float)System.Math.Max(0, rec.Value<double?>("G(h)") ?? 0);
                ds.Dni[idx] = (float)System.Math.Max(0, rec.Value<double?>("Gb(n)") ?? 0);
                ds.Dhi[idx] = (float)System.Math.Max(0, rec.Value<double?>("Gd(h)") ?? 0);
                ds.TempAmbient[idx] = (float)(rec.Value<double?>("T2m") ?? 20);
                ds.WindSpeed[idx] = (float)System.Math.Max(0, rec.Value<double?>("WS10m") ?? 1);
                filled[idx] = true;
            }

            // Fill any gap with the previous valid hour so the year is always complete.
            for (int i = 0; i < filled.Length; i++)
            {
                if (filled[i]) continue;
                int src = i > 0 ? i - 1 : Array.IndexOf(filled, true);
                if (src < 0) throw new FormatException("PVGIS TMY data could not be read.");
                ds.Ghi[i] = ds.Ghi[src];
                ds.Dni[i] = ds.Dni[src];
                ds.Dhi[i] = ds.Dhi[src];
                ds.TempAmbient[i] = ds.TempAmbient[src];
                ds.WindSpeed[i] = ds.WindSpeed[src];
            }
            return ds;
        }

        /// <summary>Accepts "20070101:0000", "20070101:00:00", "2007-01-01 00:00" and similar formats.</summary>
        public static bool TryParseTime(string s, out int month, out int day, out int hour)
        {
            month = day = hour = 0;
            if (string.IsNullOrEmpty(s)) return false;
            var digits = new char[12];
            int n = 0;
            foreach (char c in s)
            {
                if (char.IsDigit(c)) { if (n < 12) digits[n++] = c; }
            }
            if (n < 10) return false;
            var str = new string(digits, 0, n);
            month = int.Parse(str.Substring(4, 2), CultureInfo.InvariantCulture);
            day = int.Parse(str.Substring(6, 2), CultureInfo.InvariantCulture);
            hour = int.Parse(str.Substring(8, 2), CultureInfo.InvariantCulture);
            return month >= 1 && month <= 12 && day >= 1 && day <= 31 && hour >= 0 && hour <= 23;
        }
    }
}
