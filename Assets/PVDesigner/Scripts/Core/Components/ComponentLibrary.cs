using System.Collections.Generic;
using System.Linq;

namespace PVDesigner.Core.Components
{
    /// <summary>
    /// Built-in generic component database. Values are representative of current market products;
    /// import manufacturer PAN / OND files for project specific equipment.
    /// </summary>
    public class ComponentLibrary
    {
        public readonly List<PVModuleSpec> Modules = new List<PVModuleSpec>();
        public readonly List<InverterSpec> Inverters = new List<InverterSpec>();

        public static ComponentLibrary CreateDefault()
        {
            var lib = new ComponentLibrary();
            lib.Modules.AddRange(DefaultModules());
            lib.Inverters.AddRange(DefaultInverters());
            return lib;
        }

        public PVModuleSpec FindModule(string id) => Modules.FirstOrDefault(m => m.Id == id);
        public InverterSpec FindInverter(string id) => Inverters.FirstOrDefault(i => i.Id == id);

        public void AddOrReplace(PVModuleSpec m)
        {
            Modules.RemoveAll(x => x.Id == m.Id);
            Modules.Add(m);
        }

        public void AddOrReplace(InverterSpec inv)
        {
            Inverters.RemoveAll(x => x.Id == inv.Id);
            Inverters.Add(inv);
        }

        public static IEnumerable<PVModuleSpec> DefaultModules()
        {
            yield return new PVModuleSpec
            {
                Id = "gen-mono-410-108", Manufacturer = "Generic", Model = "Mono PERC 410 (108 half-cells)",
                Technology = ModuleTechnology.MonoPerc, Pmax = 410, Vmp = 31.4, Imp = 13.06, Voc = 37.5, Isc = 13.85,
                TempCoeffPmax = -0.35, TempCoeffVoc = -0.28, TempCoeffIsc = 0.048,
                Length = 1.722, Width = 1.134, Thickness = 0.030, Weight = 21.5, CellsInSeries = 108, MaxSystemVoltage = 1500
            };
            yield return new PVModuleSpec
            {
                Id = "gen-mono-550-144", Manufacturer = "Generic", Model = "Mono PERC 550 (144 half-cells)",
                Technology = ModuleTechnology.MonoPerc, Pmax = 550, Vmp = 41.9, Imp = 13.13, Voc = 49.9, Isc = 13.98,
                TempCoeffPmax = -0.35, TempCoeffVoc = -0.27, TempCoeffIsc = 0.048,
                Length = 2.278, Width = 1.134, Thickness = 0.035, Weight = 28.0, CellsInSeries = 144, MaxSystemVoltage = 1500
            };
            yield return new PVModuleSpec
            {
                Id = "gen-topcon-580-144", Manufacturer = "Generic", Model = "N-type TOPCon 580 (144 half-cells)",
                Technology = ModuleTechnology.TopCon, Pmax = 580, Vmp = 43.1, Imp = 13.46, Voc = 51.6, Isc = 14.25,
                TempCoeffPmax = -0.30, TempCoeffVoc = -0.25, TempCoeffIsc = 0.046,
                Length = 2.278, Width = 1.134, Thickness = 0.030, Weight = 32.0, CellsInSeries = 144, MaxSystemVoltage = 1500,
                LowIrradianceLoss = 0.025, Bifacial = true, Bifaciality = 0.8
            };
            yield return new PVModuleSpec
            {
                Id = "gen-topcon-620-132", Manufacturer = "Generic", Model = "N-type TOPCon 620 (132 half-cells, 210mm)",
                Technology = ModuleTechnology.TopCon, Pmax = 620, Vmp = 38.9, Imp = 15.94, Voc = 46.5, Isc = 16.85,
                TempCoeffPmax = -0.29, TempCoeffVoc = -0.25, TempCoeffIsc = 0.045,
                Length = 2.382, Width = 1.134, Thickness = 0.030, Weight = 33.0, CellsInSeries = 132, MaxSystemVoltage = 1500,
                LowIrradianceLoss = 0.025, Bifacial = true, Bifaciality = 0.8
            };
            yield return new PVModuleSpec
            {
                Id = "gen-hjt-700-132", Manufacturer = "Generic", Model = "HJT 700 (132 half-cells, 210mm)",
                Technology = ModuleTechnology.Hjt, Pmax = 700, Vmp = 40.4, Imp = 17.33, Voc = 48.6, Isc = 18.25,
                TempCoeffPmax = -0.24, TempCoeffVoc = -0.22, TempCoeffIsc = 0.040,
                Length = 2.384, Width = 1.303, Thickness = 0.033, Weight = 38.0, CellsInSeries = 132, MaxSystemVoltage = 1500,
                LowIrradianceLoss = 0.02, Bifacial = true, Bifaciality = 0.85
            };
            yield return new PVModuleSpec
            {
                Id = "gen-poly-330-72", Manufacturer = "Generic", Model = "Poly 330 (72 cells)",
                Technology = ModuleTechnology.PolyCrystalline, Pmax = 330, Vmp = 37.6, Imp = 8.78, Voc = 46.2, Isc = 9.29,
                TempCoeffPmax = -0.40, TempCoeffVoc = -0.31, TempCoeffIsc = 0.06,
                Length = 1.956, Width = 0.992, Thickness = 0.040, Weight = 22.5, CellsInSeries = 72, MaxSystemVoltage = 1000,
                LowIrradianceLoss = 0.04
            };
            yield return new PVModuleSpec
            {
                Id = "gen-cdte-475", Manufacturer = "Generic", Model = "CdTe thin film 475",
                Technology = ModuleTechnology.ThinFilmCdTe, Pmax = 475, Vmp = 190.0, Imp = 2.50, Voc = 228.0, Isc = 2.65,
                TempCoeffPmax = -0.28, TempCoeffVoc = -0.25, TempCoeffIsc = 0.04,
                Length = 2.009, Width = 1.232, Thickness = 0.049, Weight = 35.0, CellsInSeries = 264, BypassDiodes = 0,
                MaxSystemVoltage = 1500, LowIrradianceLoss = 0.01
            };
        }

        public static IEnumerable<InverterSpec> DefaultInverters()
        {
            yield return new InverterSpec
            {
                Id = "gen-str-5k", Manufacturer = "Generic", Model = "String 5 kW 1-phase", Type = InverterType.String,
                PacNom = 5, PacMax = 5.5, PdcMax = 7.5, VmpptMin = 90, VmpptMax = 560, VdcMax = 600, VStart = 100,
                MpptCount = 2, ImaxPerMppt = 16, IscMaxPerMppt = 20, StringsPerMppt = 1,
                MaxEfficiency = 97.8, EuroEfficiency = 97.0, AcVoltage = 230, Phases = 1, Weight = 12
            };
            yield return new InverterSpec
            {
                Id = "gen-str-20k", Manufacturer = "Generic", Model = "String 20 kW 3-phase", Type = InverterType.String,
                PacNom = 20, PacMax = 22, PdcMax = 30, VmpptMin = 160, VmpptMax = 950, VdcMax = 1100, VStart = 200,
                MpptCount = 2, ImaxPerMppt = 26, IscMaxPerMppt = 32, StringsPerMppt = 2,
                MaxEfficiency = 98.6, EuroEfficiency = 98.2, AcVoltage = 400, Phases = 3, Weight = 25
            };
            yield return new InverterSpec
            {
                Id = "gen-str-50k", Manufacturer = "Generic", Model = "String 50 kW 3-phase", Type = InverterType.String,
                PacNom = 50, PacMax = 55, PdcMax = 75, VmpptMin = 200, VmpptMax = 1000, VdcMax = 1100, VStart = 200,
                MpptCount = 4, ImaxPerMppt = 32, IscMaxPerMppt = 40, StringsPerMppt = 2,
                MaxEfficiency = 98.7, EuroEfficiency = 98.4, AcVoltage = 400, Phases = 3, Weight = 55
            };
            yield return new InverterSpec
            {
                Id = "gen-str-110k", Manufacturer = "Generic", Model = "String 110 kW 3-phase", Type = InverterType.String,
                PacNom = 110, PacMax = 121, PdcMax = 165, VmpptMin = 200, VmpptMax = 1000, VdcMax = 1100, VStart = 200,
                MpptCount = 10, ImaxPerMppt = 30, IscMaxPerMppt = 40, StringsPerMppt = 2,
                MaxEfficiency = 98.8, EuroEfficiency = 98.5, AcVoltage = 400, Phases = 3, Weight = 85
            };
            yield return new InverterSpec
            {
                Id = "gen-str-320k-1500", Manufacturer = "Generic", Model = "String 320 kW 1500 V", Type = InverterType.String,
                PacNom = 320, PacMax = 352, PdcMax = 480, VmpptMin = 500, VmpptMax = 1500, VdcMax = 1500, VStart = 550,
                MpptCount = 6, ImaxPerMppt = 65, IscMaxPerMppt = 115, StringsPerMppt = 4,
                MaxEfficiency = 99.0, EuroEfficiency = 98.8, AcVoltage = 800, Phases = 3, Weight = 115
            };
            yield return new InverterSpec
            {
                Id = "gen-central-3125k", Manufacturer = "Generic", Model = "Central 3125 kW 1500 V", Type = InverterType.Central,
                PacNom = 3125, PacMax = 3437, PdcMax = 4700, VmpptMin = 875, VmpptMax = 1300, VdcMax = 1500, VStart = 900,
                MpptCount = 1, ImaxPerMppt = 3997, IscMaxPerMppt = 6000, StringsPerMppt = 400,
                MaxEfficiency = 99.0, EuroEfficiency = 98.7, AcVoltage = 600, Phases = 3, Weight = 3500
            };
        }
    }
}
