using System;

namespace PVDesigner.Core.Components
{
    public enum InverterType
    {
        String,
        Central,
        Micro
    }

    [Serializable]
    public class InverterSpec
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Manufacturer = "Generic";
        public string Model = "Inverter";
        public InverterType Type = InverterType.String;

        /// <summary>Nominal AC power (kW).</summary>
        public double PacNom = 100;
        /// <summary>Maximum AC power (kW).</summary>
        public double PacMax = 110;
        /// <summary>Maximum DC input power (kW), 0 = unlimited.</summary>
        public double PdcMax = 150;

        public double VmpptMin = 200;
        public double VmpptMax = 1000;
        /// <summary>Absolute maximum DC input voltage (V).</summary>
        public double VdcMax = 1100;
        /// <summary>Minimum DC voltage to start feeding (V).</summary>
        public double VStart = 200;

        public int MpptCount = 10;
        /// <summary>Maximum input current per MPPT (A).</summary>
        public double ImaxPerMppt = 30;
        /// <summary>Maximum short circuit current per MPPT (A).</summary>
        public double IscMaxPerMppt = 40;
        public int StringsPerMppt = 2;

        /// <summary>Maximum efficiency (%).</summary>
        public double MaxEfficiency = 98.8;
        /// <summary>European weighted efficiency (%).</summary>
        public double EuroEfficiency = 98.4;
        /// <summary>Night consumption (W).</summary>
        public double NightConsumption = 2;

        public double AcVoltage = 400;
        public int Phases = 3;
        public double Weight = 85;

        public bool IsCustom;

        public string DisplayName => Manufacturer + " " + Model + " (" + PacNom.ToString("0.#") + " kWac)";

        public InverterSpec Clone() => (InverterSpec)MemberwiseClone();

        /// <summary>
        /// PVWatts v5 inverter efficiency curve scaled to the nominal efficiency.
        /// <paramref name="pdc"/> and the return value are in kW.
        /// </summary>
        public double AcOutput(double pdc, out double clippingLoss)
        {
            clippingLoss = 0;
            if (pdc <= 0) return 0;
            double etaNom = System.Math.Max(0.5, EuroEfficiency / 100.0);
            double pdc0 = PacNom / etaNom;
            double zeta = pdc / pdc0;
            if (zeta < 0.005) return 0; // below start-up threshold
            double eta = etaNom / 0.9637 * (-0.0162 * zeta - 0.0059 / zeta + 0.9858);
            eta = System.Math.Max(0, System.Math.Min(eta, MaxEfficiency / 100.0));
            double pac = pdc * eta;
            double limit = PacMax > 0 ? PacMax : PacNom;
            if (pac > limit)
            {
                clippingLoss = pac - limit;
                pac = limit;
            }
            return pac;
        }
    }
}
