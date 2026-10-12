using System;
using System.Collections.Generic;

namespace PVDesigner.Core.Economics
{
    [Serializable]
    public class EconomicParameters
    {
        public string Currency = "USD";
        /// <summary>Total installed cost per Wp DC.</summary>
        public double CapexPerWp = 0.55;
        /// <summary>Annual O&amp;M cost as a fraction of CAPEX.</summary>
        public double OpexFractionOfCapex = 0.015;
        /// <summary>Electricity value / feed-in tariff per kWh.</summary>
        public double TariffPerKwh = 0.08;
        public double TariffEscalation = 0.0;
        public double OpexEscalation = 0.02;
        public double DiscountRate = 0.07;
        public int LifetimeYears = 25;
    }

    public class EconomicYear
    {
        public int Year;
        public double EnergyKwh;
        public double Revenue;
        public double Opex;
        public double CashFlow;
        public double Cumulative;
    }

    public class EconomicResult
    {
        public double Capex;
        public double Npv;
        public double Lcoe;
        /// <summary>Simple payback in years (NaN when never reached).</summary>
        public double PaybackYears = double.NaN;
        /// <summary>Internal rate of return (NaN when undefined).</summary>
        public double Irr = double.NaN;
        public readonly List<EconomicYear> Years = new List<EconomicYear>();
    }

    public static class EconomicAnalysis
    {
        public static EconomicResult Compute(EconomicParameters p, double pnomDcKw, double firstYearEnergyKwh, double annualDegradation)
        {
            var r = new EconomicResult { Capex = pnomDcKw * 1000.0 * p.CapexPerWp };
            double cumulative = -r.Capex;
            double discountedCost = r.Capex, discountedEnergy = 0;
            var cashFlows = new List<double> { -r.Capex };
            r.Npv = -r.Capex;

            for (int y = 1; y <= System.Math.Max(1, p.LifetimeYears); y++)
            {
                double energy = firstYearEnergyKwh * System.Math.Pow(1.0 - annualDegradation, y - 1);
                double revenue = energy * p.TariffPerKwh * System.Math.Pow(1.0 + p.TariffEscalation, y - 1);
                double opex = r.Capex * p.OpexFractionOfCapex * System.Math.Pow(1.0 + p.OpexEscalation, y - 1);
                double cf = revenue - opex;
                double prev = cumulative;
                cumulative += cf;
                double df = System.Math.Pow(1.0 + p.DiscountRate, y);
                r.Npv += cf / df;
                discountedCost += opex / df;
                discountedEnergy += energy / df;
                cashFlows.Add(cf);

                if (double.IsNaN(r.PaybackYears) && cumulative >= 0 && cf > 0)
                    r.PaybackYears = y - 1 + (-prev / cf);

                r.Years.Add(new EconomicYear
                {
                    Year = y,
                    EnergyKwh = energy,
                    Revenue = revenue,
                    Opex = opex,
                    CashFlow = cf,
                    Cumulative = cumulative
                });
            }
            r.Lcoe = discountedEnergy > 0 ? discountedCost / discountedEnergy : 0;
            r.Irr = ComputeIrr(cashFlows);
            return r;
        }

        /// <summary>IRR by bisection between -99% and +100%.</summary>
        public static double ComputeIrr(IReadOnlyList<double> cashFlows)
        {
            double Npv(double rate)
            {
                double v = 0;
                for (int i = 0; i < cashFlows.Count; i++) v += cashFlows[i] / System.Math.Pow(1 + rate, i);
                return v;
            }

            double lo = -0.99, hi = 1.0;
            double flo = Npv(lo), fhi = Npv(hi);
            if (double.IsNaN(flo) || double.IsNaN(fhi) || flo * fhi > 0) return double.NaN;
            for (int i = 0; i < 200; i++)
            {
                double mid = (lo + hi) * 0.5;
                double fm = Npv(mid);
                if (System.Math.Abs(fm) < 1e-6) return mid;
                if (flo * fm < 0)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                    flo = fm;
                }
            }
            return (lo + hi) * 0.5;
        }
    }
}
