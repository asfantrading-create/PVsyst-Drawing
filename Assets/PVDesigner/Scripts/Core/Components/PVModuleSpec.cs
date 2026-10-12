using System;

namespace PVDesigner.Core.Components
{
    public enum ModuleTechnology
    {
        MonoPerc,
        TopCon,
        Hjt,
        PolyCrystalline,
        ThinFilmCdTe,
        Other
    }

    /// <summary>PV module datasheet parameters at STC (1000 W/m2, 25 deg C, AM1.5).</summary>
    [Serializable]
    public class PVModuleSpec
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Manufacturer = "Generic";
        public string Model = "Module";
        public ModuleTechnology Technology = ModuleTechnology.MonoPerc;

        /// <summary>Nominal power (Wp).</summary>
        public double Pmax = 550;
        public double Vmp = 41.9;
        public double Imp = 13.13;
        public double Voc = 49.9;
        public double Isc = 13.98;

        /// <summary>Temperature coefficient of Pmax (%/degC, negative).</summary>
        public double TempCoeffPmax = -0.35;
        /// <summary>Temperature coefficient of Voc (%/degC, negative).</summary>
        public double TempCoeffVoc = -0.27;
        /// <summary>Temperature coefficient of Isc (%/degC, positive).</summary>
        public double TempCoeffIsc = 0.048;

        /// <summary>Long side (m).</summary>
        public double Length = 2.278;
        /// <summary>Short side (m).</summary>
        public double Width = 1.134;
        public double Thickness = 0.035;
        public double Weight = 28;

        public int CellsInSeries = 144;
        public int BypassDiodes = 3;
        /// <summary>Maximum system voltage (V).</summary>
        public double MaxSystemVoltage = 1500;

        /// <summary>Relative efficiency loss at 200 W/m2 compared to STC (fraction, e.g. 0.03).</summary>
        public double LowIrradianceLoss = 0.03;

        public bool Bifacial;
        public double Bifaciality = 0.7;

        /// <summary>True when the module was loaded from a user file (PAN) rather than the built-in library.</summary>
        public bool IsCustom;

        public double Area => Length * Width;
        public double Efficiency => Area > 0 ? Pmax / (Area * 1000.0) : 0.2;
        public string DisplayName => Manufacturer + " " + Model + " (" + Pmax.ToString("0") + " Wp)";

        public double VocAt(double cellTemp) => Voc * (1.0 + TempCoeffVoc / 100.0 * (cellTemp - 25.0));
        /// <summary>Vmp temperature behaviour approximated with the Voc coefficient (common datasheet practice).</summary>
        public double VmpAt(double cellTemp) => Vmp * (1.0 + TempCoeffVoc / 100.0 * (cellTemp - 25.0));
        public double IscAt(double cellTemp) => Isc * (1.0 + TempCoeffIsc / 100.0 * (cellTemp - 25.0));

        public PVModuleSpec Clone() => (PVModuleSpec)MemberwiseClone();
    }
}
