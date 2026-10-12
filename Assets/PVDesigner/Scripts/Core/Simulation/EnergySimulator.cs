using System;
using System.Diagnostics;
using System.Threading;
using PVDesigner.Core.Components;
using PVDesigner.Core.Electrical;
using PVDesigner.Core.Layout;
using PVDesigner.Core.Math;
using PVDesigner.Core.Meteo;
using PVDesigner.Core.Model;
using PVDesigner.Core.Shading;
using PVDesigner.Core.Solar;

namespace PVDesigner.Core.Simulation
{
    /// <summary>
    /// Hourly grid-connected PV simulation following the PVsyst loss chain:
    /// transposition, near shading, IAM, soiling, PV conversion (irradiance level, temperature),
    /// module quality / LID / mismatch, DC wiring, inverter efficiency and clipping, AC losses, availability.
    /// </summary>
    public static class EnergySimulator
    {
        /// <summary>Absorption coefficient of the Faiman / PVsyst thermal model.</summary>
        public const double ThermalAlpha = 0.9;
        public const int ProjectionYears = 25;

        public static SimulationResult Run(PVProject project, PVModuleSpec module, InverterSpec inverter,
            ShadingResult shading, IProgress<float> progress = null, CancellationToken cancel = default)
        {
            var sw = Stopwatch.StartNew();
            var meteo = project.Meteo;
            if (meteo == null || !meteo.IsValid()) throw new InvalidOperationException("No meteo data: load PVGIS or synthetic data first.");
            if (module == null || inverter == null) throw new InvalidOperationException("Select a PV module and an inverter.");

            var sizing = SystemSizing.Size(project, module, inverter);
            var res = new SimulationResult { Sizing = sizing, MeteoDescription = meteo.Description };
            for (int m = 0; m < 12; m++) res.Monthly[m] = new MonthlyResult { Month = m + 1 };
            int totalModules = project.TotalModules();
            int connected = sizing.StringCount * sizing.ModulesPerString;
            if (connected <= 0 || sizing.InverterCount <= 0)
                throw new InvalidOperationException("The system has no complete string. Generate the layout and check the electrical design.");

            double connectedRatio = connected / (double)totalModules;
            res.ModulesConnected = connected;
            res.ModuleArea = connected * module.Area;
            res.PnomDcKw = connected * module.Pmax / 1000.0;
            res.PnomAcKw = sizing.InverterCount * inverter.PacNom;
            foreach (var msg in sizing.Messages)
                if (msg.Level != DesignMessageLevel.Info) res.Warnings.Add(msg.Text);

            var losses = project.Losses;
            var sim = project.Simulation;
            int nf = project.Fields.Count;

            // Per field constants.
            var fieldModules = new double[nf];
            var fieldPnom = new double[nf];
            var fieldArea = new double[nf];
            var fieldGcr = new double[nf];
            var fieldShading = new ShadingFactorTable[nf];
            for (int f = 0; f < nf; f++)
            {
                var field = project.Fields[f];
                fieldModules[f] = field.ModuleCount * connectedRatio;
                fieldPnom[f] = fieldModules[f] * module.Pmax / 1000.0;
                fieldArea[f] = fieldModules[f] * module.Area;
                fieldGcr[f] = TableGeometry.Gcr(field, module);
                fieldShading[f] = shading != null && shading.PerField != null && f < shading.PerField.Length && sim.Shading != ShadingMode.None
                    ? shading.PerField[f]
                    : null;
            }
            double totalArea = 0;
            foreach (var a in fieldArea) totalArea += a;
            if (totalArea <= 0) throw new InvalidOperationException("No PV modules in the project.");

            double lowIrrCoeff = module.LowIrradianceLoss / System.Math.Log(5.0);
            double gamma = module.TempCoeffPmax / 100.0;
            double etaModule = module.Efficiency;
            double pStc = res.PnomDcKw;
            double pacTotalNom = res.PnomAcKw;
            int nInv = sizing.InverterCount;
            bool hasDni = meteo.HasDni();

            // Loss chain accumulators (irradiation in kWh/m2 area weighted, energy in kWh).
            double sGlobHor = 0, sGlobInc = 0, sAfterShade = 0, sAfterIam = 0, sGlobEff = 0;
            double eNominal = 0, eAfterIrr = 0, eAfterTemp = 0, eAfterQuality = 0, eAfterLid = 0, eAfterMismatch = 0, eArray = 0;
            double eInvOut = 0, eInvClipped = 0, eNight = 0, eAfterAc = 0, eAfterTrf = 0, eGrid = 0;

            res.HourlyEGrid = new float[MeteoDataset.HoursPerYear];
            res.HourlyEArray = new float[MeteoDataset.HoursPerYear];
            res.HourlyGlobInc = new float[MeteoDataset.HoursPerYear];
            res.HourlyTCell = new float[MeteoDataset.HoursPerYear];
            res.HourlySunElevation = new float[MeteoDataset.HoursPerYear];
            var tampSum = new double[12];
            var tampCnt = new int[12];
            var monthlyGlobIncWeighted = new double[12];

            double tz = project.Location.TimeZone;
            for (int h = 0; h < MeteoDataset.HoursPerYear; h++)
            {
                if ((h & 255) == 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    progress?.Report(h / (float)MeteoDataset.HoursPerYear);
                }

                DateTime tUtc = MeteoDataset.HourStartUtc(h).AddHours(meteo.SunTimeOffsetHours);
                int month = MeteoDataset.HourStartUtc(h).AddHours(tz).Month - 1;
                int doy = tUtc.DayOfYear;
                var sun = SolarPosition.Compute(tUtc, project.Location.Latitude, project.Location.Longitude);
                res.HourlySunElevation[h] = (float)sun.Elevation;

                double ghi = System.Math.Max(0, meteo.Ghi[h]);
                double dhi = MathUtil.Clamp(meteo.Dhi[h], 0, ghi);
                double tamb = meteo.TempAmbient[h];
                double ws = System.Math.Max(0, meteo.WindSpeed[h]);
                tampSum[month] += tamb;
                tampCnt[month]++;

                var mr = res.Monthly[month];
                mr.GlobHor += ghi / 1000.0;
                mr.DiffHor += dhi / 1000.0;
                sGlobHor += ghi / 1000.0;

                double dni;
                if (!sun.IsUp)
                {
                    dni = 0;
                    dhi = ghi;
                }
                else if (hasDni)
                {
                    dni = System.Math.Max(0, meteo.Dni[h]);
                }
                else
                {
                    double cz = System.Math.Max(0.05, MathUtil.CosD(sun.Zenith));
                    dni = System.Math.Min((ghi - dhi) / cz, SolarPosition.ExtraterrestrialIrradiance(doy));
                }

                double soiling = losses.MonthlySoiling != null && losses.MonthlySoiling.Length == 12 ? losses.MonthlySoiling[month] : 0;
                double pArrayKw = 0;
                double hourGlobInc = 0, hourGeff = 0, hourTcell = 0;

                for (int f = 0; f < nf; f++)
                {
                    if (fieldModules[f] <= 0) continue;
                    var field = project.Fields[f];
                    Vec3 normal;
                    if (field.Mounting == MountingType.SingleAxisTracker)
                    {
                        double rot = SingleAxisTracker.RotationAngle(sun, field.TrackerAxisCompassAzimuth, field.TrackerMaxAngle, field.Backtracking, fieldGcr[f]);
                        normal = SingleAxisTracker.PlaneNormal(rot, field.TrackerAxisCompassAzimuth);
                    }
                    else
                    {
                        normal = Irradiance.PlaneNormal(field.Tilt, field.CompassAzimuth);
                    }
                    double tilt = System.Math.Acos(MathUtil.Clamp(normal.Y, -1, 1)) * MathUtil.Rad2Deg;

                    var poa = Irradiance.Transpose(sim.Transposition, ghi, dhi, dni, sun, normal, project.Location.Albedo, doy);
                    double cosAoi = sun.IsUp ? Vec3.Dot(sun.Direction, normal) : 0;
                    double aoi = System.Math.Acos(MathUtil.Clamp(cosAoi, -1, 1)) * MathUtil.Rad2Deg;

                    double gInc = poa.Global;
                    double beam = poa.Beam, sky = poa.SkyDiffuse, gnd = poa.GroundReflected;
                    var st = fieldShading[f];
                    if (st != null)
                    {
                        if (beam > 0) beam *= 1.0 - st.BeamLoss(sun.Azimuth, sun.Elevation, sim.Shading, sim.ElectricalEffectFraction);
                        sky *= st.DiffuseFactor;
                        gnd *= st.AlbedoFactor;
                    }
                    double gShaded = beam + sky + gnd;

                    double b0 = losses.IamB0;
                    double gIam = beam * Irradiance.IamAshrae(aoi, b0)
                                  + sky * Irradiance.IamAshrae(Irradiance.EquivalentSkyDiffuseAngle(tilt), b0)
                                  + gnd * Irradiance.IamAshrae(Irradiance.EquivalentGroundAngle(tilt), b0);
                    double gEff = gIam * (1.0 - soiling);

                    double w = fieldArea[f] / totalArea;
                    sGlobInc += w * gInc / 1000.0;
                    sAfterShade += w * gShaded / 1000.0;
                    sAfterIam += w * gIam / 1000.0;
                    sGlobEff += w * gEff / 1000.0;
                    mr.GlobInc += w * gInc / 1000.0;
                    mr.GlobEff += w * gEff / 1000.0;
                    hourGlobInc += w * gInc;
                    hourGeff += w * gEff;

                    if (gEff <= 0) continue;

                    double pNom = fieldPnom[f] * gEff / 1000.0;
                    double etaRel = MathUtil.Clamp(1.0 + lowIrrCoeff * System.Math.Log(gEff / 1000.0), 0, 1.05);
                    double pIrr = pNom * etaRel;

                    double uTotal = System.Math.Max(1, losses.ThermalUc + losses.ThermalUv * ws);
                    double tCell = tamb + ThermalAlpha * gEff * (1.0 - etaModule) / uTotal;
                    hourTcell += w * tCell;
                    double pTemp = pIrr * System.Math.Max(0, 1.0 + gamma * (tCell - 25.0));
                    double pQual = pTemp * (1.0 - losses.ModuleQuality);
                    double pLid = pQual * (1.0 - losses.Lid);
                    double pMm = pLid * (1.0 - losses.Mismatch);

                    eNominal += pNom;
                    eAfterIrr += pIrr;
                    eAfterTemp += pTemp;
                    eAfterQuality += pQual;
                    eAfterLid += pLid;
                    eAfterMismatch += pMm;
                    pArrayKw += pMm;
                }

                // DC ohmic loss scales with the square of the current, i.e. its fraction with the power.
                double ohmFrac = losses.DcOhmicAtStc * (pStc > 0 ? pArrayKw / pStc : 0);
                double pArr = pArrayKw * (1.0 - MathUtil.Clamp01(ohmFrac));
                eArray += pArr;
                mr.EArray += pArr;

                double pAc;
                if (pArr > 0)
                {
                    double pdcInv = pArr / nInv;
                    double acInv = inverter.AcOutput(pdcInv, out double clip);
                    pAc = acInv * nInv;
                    eInvOut += (acInv + clip) * nInv;
                    eInvClipped += acInv * nInv;
                }
                else
                {
                    pAc = -inverter.NightConsumption / 1000.0 * nInv;
                    eNight += pAc;
                }

                double pAfterAc = pAc > 0 ? pAc * (1.0 - losses.AcOhmicAtNominal * pAc / pacTotalNom) : pAc;
                double pAfterTrf = pAfterAc > 0 ? pAfterAc * (1.0 - losses.TransformerAtNominal * pAfterAc / pacTotalNom) : pAfterAc;
                double pGrid = pAfterTrf * (1.0 - losses.Unavailability);
                eAfterAc += pAfterAc;
                eAfterTrf += pAfterTrf;
                eGrid += pGrid;
                mr.EGrid += pGrid;

                res.HourlyEArray[h] = (float)pArr;
                res.HourlyEGrid[h] = (float)pGrid;
                res.HourlyGlobInc[h] = (float)hourGlobInc;
                res.HourlyTCell[h] = (float)(hourGeff > 0 ? hourTcell : tamb);
            }

            // Monthly / annual summaries.
            var annual = res.Annual;
            annual.Month = 0;
            for (int m = 0; m < 12; m++)
            {
                var mr = res.Monthly[m];
                mr.TAmb = tampCnt[m] > 0 ? tampSum[m] / tampCnt[m] : 0;
                mr.PR = mr.GlobInc > 0 && pStc > 0 ? mr.EGrid / (mr.GlobInc * pStc) : 0;
                annual.GlobHor += mr.GlobHor;
                annual.DiffHor += mr.DiffHor;
                annual.GlobInc += mr.GlobInc;
                annual.GlobEff += mr.GlobEff;
                annual.EArray += mr.EArray;
                annual.EGrid += mr.EGrid;
                annual.TAmb += mr.TAmb / 12.0;
            }
            annual.PR = annual.GlobInc > 0 && pStc > 0 ? annual.EGrid / (annual.GlobInc * pStc) : 0;
            res.PerformanceRatio = annual.PR;
            res.SpecificYield = pStc > 0 ? annual.EGrid / pStc : 0;
            res.CapacityFactor = pStc > 0 ? annual.EGrid / (pStc * 8760.0) : 0;

            // Loss diagram.
            var L = res.Losses;
            L.Add(LossItem.Milestone("Global horizontal irradiation", sGlobHor, LossKind.Irradiation));
            L.Add(LossItem.Loss("Global incident in coll. plane", sGlobHor, sGlobInc, LossKind.Irradiation));
            L.Add(LossItem.Loss("Near shadings: irradiance loss", sGlobInc, sAfterShade, LossKind.Irradiation));
            L.Add(LossItem.Loss("IAM factor on global", sAfterShade, sAfterIam, LossKind.Irradiation));
            L.Add(LossItem.Loss("Soiling loss factor", sAfterIam, sGlobEff, LossKind.Irradiation));
            L.Add(LossItem.Milestone("Effective irradiation on collectors", sGlobEff, LossKind.Irradiation));
            L.Add(LossItem.Milestone("Array nominal energy (at STC effic.)", eNominal, LossKind.Energy));
            L.Add(LossItem.Loss("PV loss due to irradiance level", eNominal, eAfterIrr, LossKind.Energy));
            L.Add(LossItem.Loss("PV loss due to temperature", eAfterIrr, eAfterTemp, LossKind.Energy));
            L.Add(LossItem.Loss("Module quality loss", eAfterTemp, eAfterQuality, LossKind.Energy));
            L.Add(LossItem.Loss("LID - Light induced degradation", eAfterQuality, eAfterLid, LossKind.Energy));
            L.Add(LossItem.Loss("Mismatch loss, modules and strings", eAfterLid, eAfterMismatch, LossKind.Energy));
            L.Add(LossItem.Loss("Ohmic wiring loss", eAfterMismatch, eArray, LossKind.Energy));
            L.Add(LossItem.Milestone("Array virtual energy at MPP", eArray, LossKind.Energy));
            L.Add(LossItem.Loss("Inverter loss during operation (efficiency)", eArray, eInvOut, LossKind.Energy));
            L.Add(LossItem.Loss("Inverter loss over nominal inv. power", eInvOut, eInvClipped, LossKind.Energy));
            L.Add(LossItem.Loss("Night consumption", eInvClipped, eInvClipped + eNight, LossKind.Energy));
            L.Add(LossItem.Milestone("Available energy at inverter output", eInvClipped + eNight, LossKind.Energy));
            L.Add(LossItem.Loss("AC ohmic loss", eInvClipped + eNight, eAfterAc, LossKind.Energy));
            if (losses.TransformerAtNominal > 0)
                L.Add(LossItem.Loss("MV transformer loss", eAfterAc, eAfterTrf, LossKind.Energy));
            L.Add(LossItem.Loss("System unavailability", eAfterTrf, eGrid, LossKind.Energy));
            L.Add(LossItem.Milestone("Energy injected into grid", eGrid, LossKind.Energy));

            res.YearlyEnergy = new double[ProjectionYears];
            for (int y = 0; y < ProjectionYears; y++)
                res.YearlyEnergy[y] = eGrid * System.Math.Pow(1.0 - losses.AnnualDegradation, y);

            if (totalModules > connected)
                res.Warnings.Add($"{totalModules - connected} module(s) are not part of a complete string and are ignored.");

            res.ComputeSeconds = sw.Elapsed.TotalSeconds;
            progress?.Report(1f);
            return res;
        }
    }
}
