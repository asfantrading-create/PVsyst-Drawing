using System;
using System.Collections.Generic;
using PVDesigner.Core.Electrical;

namespace PVDesigner.Core.Simulation
{
    [Serializable]
    public class MonthlyResult
    {
        public int Month;
        /// <summary>Global horizontal irradiation (kWh/m2).</summary>
        public double GlobHor;
        /// <summary>Diffuse horizontal irradiation (kWh/m2).</summary>
        public double DiffHor;
        /// <summary>Mean ambient temperature (deg C).</summary>
        public double TAmb;
        /// <summary>Global incident in collector plane (kWh/m2).</summary>
        public double GlobInc;
        /// <summary>Effective global after optical losses (kWh/m2).</summary>
        public double GlobEff;
        /// <summary>Effective energy at the output of the array (kWh).</summary>
        public double EArray;
        /// <summary>Energy injected into the grid (kWh).</summary>
        public double EGrid;
        /// <summary>Performance ratio.</summary>
        public double PR;
    }

    public enum LossKind
    {
        Irradiation,
        Energy
    }

    [Serializable]
    public class LossItem
    {
        public string Label;
        /// <summary>Relative change against the previous step in percent (negative = loss).</summary>
        public double Percent;
        /// <summary>Value after this step (kWh/m2 for irradiation, kWh for energy). NaN for pure loss rows.</summary>
        public double Value = double.NaN;
        public LossKind Kind;
        /// <summary>Milestone rows display a value instead of a percentage.</summary>
        public bool IsMilestone;

        public static LossItem Milestone(string label, double value, LossKind kind) =>
            new LossItem { Label = label, Value = value, Kind = kind, IsMilestone = true };

        public static LossItem Loss(string label, double before, double after, LossKind kind) =>
            new LossItem
            {
                Label = label,
                Percent = before > 0 ? (after - before) / before * 100.0 : 0,
                Value = after,
                Kind = kind
            };
    }

    public class SimulationResult
    {
        public MonthlyResult[] Monthly = new MonthlyResult[12];
        public MonthlyResult Annual = new MonthlyResult();
        public List<LossItem> Losses = new List<LossItem>();

        public double PnomDcKw;
        public double PnomAcKw;
        public int ModulesConnected;
        public double ModuleArea;

        /// <summary>kWh/kWp/year.</summary>
        public double SpecificYield;
        public double PerformanceRatio;
        public double CapacityFactor;

        /// <summary>Energy injected into the grid for each operating year (kWh), including degradation.</summary>
        public double[] YearlyEnergy = new double[0];

        public float[] HourlyEGrid;
        public float[] HourlyEArray;
        public float[] HourlyGlobInc;
        public float[] HourlyTCell;
        public float[] HourlySunElevation;

        public SystemSizingResult Sizing;
        public string MeteoDescription = "";
        public DateTime ComputedUtc = DateTime.UtcNow;
        public double ComputeSeconds;
        public readonly List<string> Warnings = new List<string>();
    }
}
