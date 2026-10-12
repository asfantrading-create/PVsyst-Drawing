using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PVDesigner.Core.Components
{
    /// <summary>
    /// Reader for the text versions of PVsyst component files (.PAN for modules, .OND for inverters).
    /// Only the parameters needed by the simplified simulation model are extracted.
    /// Binary (very old) files are not supported.
    /// </summary>
    public static class PanOndParser
    {
        /// <summary>Reads all "Key=Value" pairs; keys of nested sections are stored without prefix (first occurrence wins).</summary>
        public static Dictionary<string, string> ReadKeyValues(string text)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = new StringReader(text ?? ""))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    if (key.Length == 0 || dict.ContainsKey(key)) continue;
                    dict[key] = value;
                }
            }
            return dict;
        }

        private static double Num(Dictionary<string, string> d, double fallback, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (!d.TryGetValue(k, out var s)) continue;
                int comma = s.IndexOf(',');
                if (comma > 0) s = s.Substring(0, comma);
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return v;
            }
            return fallback;
        }

        private static string Str(Dictionary<string, string> d, string fallback, params string[] keys)
        {
            foreach (var k in keys)
                if (d.TryGetValue(k, out var s) && !string.IsNullOrWhiteSpace(s)) return s.Trim();
            return fallback;
        }

        public static bool LooksLikePan(string text) =>
            text != null && text.IndexOf("pvModule", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool LooksLikeOnd(string text) =>
            text != null && (text.IndexOf("pvGInverter", StringComparison.OrdinalIgnoreCase) >= 0
                             || text.IndexOf("PNomConv", StringComparison.OrdinalIgnoreCase) >= 0);

        public static PVModuleSpec ParsePan(string text)
        {
            if (!LooksLikePan(text)) throw new FormatException("Not a text PAN file (pvModule section not found).");
            var d = ReadKeyValues(text);
            var m = new PVModuleSpec
            {
                Id = "pan-" + Guid.NewGuid().ToString("N").Substring(0, 12),
                IsCustom = true,
                Manufacturer = Str(d, "Unknown", "Manufacturer"),
                Model = Str(d, "PAN module", "Model"),
                Pmax = Num(d, 0, "PNom"),
                Isc = Num(d, 0, "Isc"),
                Voc = Num(d, 0, "Voc"),
                Imp = Num(d, 0, "Imp"),
                Vmp = Num(d, 0, "Vmp"),
                CellsInSeries = (int)Num(d, 60, "NCelS"),
                BypassDiodes = (int)Num(d, 3, "NDiode"),
                MaxSystemVoltage = Num(d, 1000, "VMaxIEC", "VMaxUL"),
                Length = Num(d, 2.0, "Height"),
                Width = Num(d, 1.0, "Width"),
                Thickness = Num(d, 0.035, "Depth"),
                Weight = Num(d, 25, "Weight")
            };
            if (m.Pmax <= 0 && m.Vmp > 0 && m.Imp > 0) m.Pmax = m.Vmp * m.Imp;
            if (m.Pmax <= 0 || m.Voc <= 0 || m.Isc <= 0) throw new FormatException("PAN file is missing PNom, Voc or Isc.");
            if (m.Vmp <= 0) m.Vmp = m.Voc * 0.82;
            if (m.Imp <= 0) m.Imp = m.Pmax / m.Vmp;

            // Dimensions are in metres in PVsyst; ensure the long side is the length.
            if (m.Width > m.Length)
            {
                double t = m.Width;
                m.Width = m.Length;
                m.Length = t;
            }

            // muISC in mA/degC, muVocSpec in mV/degC, muPmpReq in %/degC
            double muIsc = Num(d, double.NaN, "muISC");
            double muVoc = Num(d, double.NaN, "muVocSpec");
            double muP = Num(d, double.NaN, "muPmpReq");
            if (!double.IsNaN(muIsc)) m.TempCoeffIsc = muIsc / 1000.0 / m.Isc * 100.0;
            if (!double.IsNaN(muVoc)) m.TempCoeffVoc = muVoc / 1000.0 / m.Voc * 100.0;
            if (!double.IsNaN(muP)) m.TempCoeffPmax = muP;

            string tech = Str(d, "", "Technol");
            if (tech.IndexOf("CdTe", StringComparison.OrdinalIgnoreCase) >= 0) m.Technology = ModuleTechnology.ThinFilmCdTe;
            else if (tech.IndexOf("HIT", StringComparison.OrdinalIgnoreCase) >= 0 || tech.IndexOf("HJT", StringComparison.OrdinalIgnoreCase) >= 0) m.Technology = ModuleTechnology.Hjt;
            else if (tech.IndexOf("Poly", StringComparison.OrdinalIgnoreCase) >= 0 || tech.IndexOf("mtSiPoly", StringComparison.OrdinalIgnoreCase) >= 0) m.Technology = ModuleTechnology.PolyCrystalline;
            else m.Technology = ModuleTechnology.MonoPerc;

            double bifi = Num(d, 0, "BifacialityFactor");
            if (bifi > 0)
            {
                m.Bifacial = true;
                m.Bifaciality = bifi;
            }
            return m;
        }

        public static InverterSpec ParseOnd(string text)
        {
            if (!LooksLikeOnd(text)) throw new FormatException("Not a text OND file (pvGInverter section not found).");
            var d = ReadKeyValues(text);
            var inv = new InverterSpec
            {
                Id = "ond-" + Guid.NewGuid().ToString("N").Substring(0, 12),
                IsCustom = true,
                Manufacturer = Str(d, "Unknown", "Manufacturer"),
                Model = Str(d, "OND inverter", "Model"),
                PacNom = Num(d, 0, "PNomConv"),
                PacMax = Num(d, 0, "PMaxOUT"),
                PdcMax = Num(d, 0, "PMaxDC", "PNomDC"),
                VmpptMin = Num(d, 0, "VMppMin"),
                VmpptMax = Num(d, 0, "VMPPMax", "VMppMax"),
                VdcMax = Num(d, 0, "VAbsMax"),
                VStart = Num(d, 0, "VMppMin"),
                MaxEfficiency = Num(d, 98, "EfficMax"),
                EuroEfficiency = Num(d, 0, "EfficEuro"),
                MpptCount = (int)Num(d, 1, "NbMPPT", "NbInputs"),
                AcVoltage = Num(d, 400, "VOutConv"),
                Phases = (int)Num(d, 3, "MonoTri") == 1 ? 1 : 3,
                NightConsumption = Num(d, 1, "Night_Loss"),
                Weight = Num(d, 50, "Weight")
            };
            if (inv.PacNom <= 0) throw new FormatException("OND file is missing PNomConv.");
            if (inv.PacMax <= 0) inv.PacMax = inv.PacNom;
            if (inv.EuroEfficiency <= 0) inv.EuroEfficiency = inv.MaxEfficiency - 0.3;
            if (inv.VdcMax <= 0) inv.VdcMax = inv.VmpptMax;
            if (inv.MpptCount < 1) inv.MpptCount = 1;

            double imaxTotal = Num(d, 0, "IMaxDC");
            if (imaxTotal > 0) inv.ImaxPerMppt = imaxTotal / inv.MpptCount;
            double iscMax = Num(d, 0, "IMaxSC", "ISCMax");
            if (iscMax > 0) inv.IscMaxPerMppt = iscMax / inv.MpptCount;
            else inv.IscMaxPerMppt = inv.ImaxPerMppt * 1.25;

            inv.Type = inv.PacNom >= 1000 ? InverterType.Central : InverterType.String;
            inv.StringsPerMppt = inv.Type == InverterType.Central ? 400 : 2;
            return inv;
        }
    }
}
