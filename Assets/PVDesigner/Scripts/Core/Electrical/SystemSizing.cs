using System;
using System.Collections.Generic;
using PVDesigner.Core.Components;
using PVDesigner.Core.Model;

namespace PVDesigner.Core.Electrical
{
    public enum DesignMessageLevel
    {
        Info,
        Warning,
        Error
    }

    public class DesignMessage
    {
        public DesignMessageLevel Level;
        public string Text;

        public DesignMessage(DesignMessageLevel level, string text)
        {
            Level = level;
            Text = text;
        }

        public override string ToString() => Level + ": " + Text;
    }

    /// <summary>Result of the electrical sizing of the system (strings, inverters, voltage windows).</summary>
    public class SystemSizingResult
    {
        public int TotalModules;
        public int ModulesPerString;
        public int MinModulesPerString;
        public int MaxModulesPerString;
        public int MaxModulesPerStringMppt;
        public int StringCount;
        public int UnusedModules;
        public int InverterCount;
        public int StringsPerInverter;
        public int StringsPerMppt;

        public double PnomDcKw;
        public double PnomAcKw;
        public double DcAcRatio;

        public double StringVocCold;
        public double StringVmpHot;
        public double StringVmpCold;
        public double StringIsc;
        public double MpptCurrent;

        public readonly List<DesignMessage> Messages = new List<DesignMessage>();
        public bool HasErrors => Messages.Exists(m => m.Level == DesignMessageLevel.Error);
    }

    public static class SystemSizing
    {
        public static SystemSizingResult Size(PVProject project, PVModuleSpec module, InverterSpec inverter)
        {
            var cfg = project.SystemDesign;
            var r = new SystemSizingResult { TotalModules = project.TotalModules() };
            if (module == null || inverter == null)
            {
                r.Messages.Add(new DesignMessage(DesignMessageLevel.Error, "Select a PV module and an inverter."));
                return r;
            }

            double vocCold = module.VocAt(cfg.DesignTempMin);
            double vmpHot = module.VmpAt(cfg.DesignCellTempHot);
            double vmpCold = module.VmpAt(cfg.DesignCellTempMppCold);
            double vMaxAbs = System.Math.Min(inverter.VdcMax, module.MaxSystemVoltage > 0 ? module.MaxSystemVoltage : double.MaxValue);

            r.MaxModulesPerString = System.Math.Max(1, (int)System.Math.Floor(vMaxAbs / vocCold));
            r.MaxModulesPerStringMppt = System.Math.Max(1, (int)System.Math.Floor(inverter.VmpptMax / vmpCold));
            r.MinModulesPerString = System.Math.Max(1, (int)System.Math.Ceiling(System.Math.Max(inverter.VmpptMin, inverter.VStart) / vmpHot));

            int n = cfg.ModulesPerString > 0 ? cfg.ModulesPerString : System.Math.Min(r.MaxModulesPerString, r.MaxModulesPerStringMppt);
            if (cfg.ModulesPerString <= 0 && n < r.MinModulesPerString) n = r.MaxModulesPerString;
            r.ModulesPerString = System.Math.Max(1, n);

            r.StringVocCold = r.ModulesPerString * vocCold;
            r.StringVmpHot = r.ModulesPerString * vmpHot;
            r.StringVmpCold = r.ModulesPerString * vmpCold;
            r.StringIsc = module.IscAt(70);

            if (r.MinModulesPerString > r.MaxModulesPerString)
                r.Messages.Add(new DesignMessage(DesignMessageLevel.Error,
                    "Module and inverter voltage windows are incompatible (no valid string length)."));
            if (r.StringVocCold > vMaxAbs)
                r.Messages.Add(new DesignMessage(DesignMessageLevel.Error,
                    $"String Voc at {cfg.DesignTempMin:0} degC = {r.StringVocCold:0} V exceeds the maximum voltage {vMaxAbs:0} V."));
            if (r.StringVmpHot < inverter.VmpptMin)
                r.Messages.Add(new DesignMessage(DesignMessageLevel.Warning,
                    $"String Vmpp at {cfg.DesignCellTempHot:0} degC = {r.StringVmpHot:0} V is below the MPPT minimum {inverter.VmpptMin:0} V."));
            if (r.StringVmpCold > inverter.VmpptMax)
                r.Messages.Add(new DesignMessage(DesignMessageLevel.Warning,
                    $"String Vmpp at {cfg.DesignCellTempMppCold:0} degC = {r.StringVmpCold:0} V is above the MPPT maximum {inverter.VmpptMax:0} V."));

            r.StringCount = r.TotalModules / r.ModulesPerString;
            r.UnusedModules = r.TotalModules - r.StringCount * r.ModulesPerString;
            r.PnomDcKw = r.StringCount * r.ModulesPerString * module.Pmax / 1000.0;

            if (r.StringCount == 0)
            {
                r.Messages.Add(new DesignMessage(DesignMessageLevel.Warning, "No complete string: place more PV tables."));
                r.InverterCount = cfg.InverterCount > 0 ? cfg.InverterCount : 0;
                return r;
            }

            if (cfg.InverterCount > 0)
            {
                r.InverterCount = cfg.InverterCount;
            }
            else
            {
                double target = System.Math.Max(0.5, cfg.TargetDcAcRatio);
                r.InverterCount = System.Math.Max(1, (int)System.Math.Round(r.PnomDcKw / (inverter.PacNom * target)));
            }

            r.PnomAcKw = r.InverterCount * inverter.PacNom;
            r.DcAcRatio = r.PnomAcKw > 0 ? r.PnomDcKw / r.PnomAcKw : 0;
            r.StringsPerInverter = (int)System.Math.Ceiling(r.StringCount / (double)r.InverterCount);
            r.StringsPerMppt = (int)System.Math.Ceiling(r.StringsPerInverter / (double)System.Math.Max(1, inverter.MpptCount));
            r.MpptCurrent = r.StringsPerMppt * module.Imp;

            if (r.UnusedModules > 0)
                r.Messages.Add(new DesignMessage(DesignMessageLevel.Info,
                    $"{r.UnusedModules} module(s) do not complete a string and are not connected."));
            if (r.StringsPerMppt * module.Imp > inverter.ImaxPerMppt * 1.0001)
                r.Messages.Add(new DesignMessage(DesignMessageLevel.Warning,
                    $"MPPT current {r.MpptCurrent:0.0} A exceeds the inverter limit {inverter.ImaxPerMppt:0.0} A (power limitation)."));
            if (r.StringsPerMppt * r.StringIsc > inverter.IscMaxPerMppt * 1.0001)
                r.Messages.Add(new DesignMessage(DesignMessageLevel.Error,
                    $"MPPT short circuit current {r.StringsPerMppt * r.StringIsc:0.0} A exceeds the inverter limit {inverter.IscMaxPerMppt:0.0} A."));
            if (inverter.PdcMax > 0 && r.PnomDcKw / r.InverterCount > inverter.PdcMax * 1.0001)
                r.Messages.Add(new DesignMessage(DesignMessageLevel.Warning,
                    $"DC power per inverter {r.PnomDcKw / r.InverterCount:0.0} kW exceeds the inverter maximum DC power {inverter.PdcMax:0.0} kW."));
            if (r.DcAcRatio > 1.5)
                r.Messages.Add(new DesignMessage(DesignMessageLevel.Warning,
                    $"DC/AC ratio {r.DcAcRatio:0.00} is high: expect significant clipping losses."));
            else if (r.DcAcRatio < 0.9)
                r.Messages.Add(new DesignMessage(DesignMessageLevel.Info,
                    $"DC/AC ratio {r.DcAcRatio:0.00} is low: inverters are oversized."));

            if (!r.HasErrors)
                r.Messages.Add(new DesignMessage(DesignMessageLevel.Info,
                    $"{r.StringCount} strings of {r.ModulesPerString} modules on {r.InverterCount} inverter(s), DC/AC = {r.DcAcRatio:0.00}."));
            return r;
        }

        /// <summary>
        /// Assigns modules to strings and strings to inverters following the table order (row by row).
        /// Returns, for every table (field order), the inverter index of each module (-1 = not connected).
        /// </summary>
        public static List<int[]> AssignStrings(PVProject project, SystemSizingResult sizing)
        {
            var result = new List<int[]>();
            int moduleCounter = 0;
            int connected = sizing.StringCount * sizing.ModulesPerString;
            int stringsPerInverter = System.Math.Max(1, sizing.StringsPerInverter);
            foreach (var f in project.Fields)
            {
                foreach (var t in f.Tables)
                {
                    var arr = new int[f.ModulesPerTable];
                    for (int m = 0; m < arr.Length; m++)
                    {
                        if (moduleCounter < connected && sizing.ModulesPerString > 0)
                        {
                            int stringIndex = moduleCounter / sizing.ModulesPerString;
                            arr[m] = System.Math.Min(stringIndex / stringsPerInverter, System.Math.Max(0, sizing.InverterCount - 1));
                        }
                        else
                        {
                            arr[m] = -1;
                        }
                        moduleCounter++;
                    }
                    result.Add(arr);
                }
            }
            return result;
        }
    }
}
