using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using PVDesigner.Core.Components;
using PVDesigner.Core.Economics;
using PVDesigner.Core.Electrical;
using PVDesigner.Core.Layout;
using PVDesigner.Core.Math;
using PVDesigner.Core.Meteo;
using PVDesigner.Core.Model;
using PVDesigner.Core.Reports;
using PVDesigner.Core.Serialization;
using PVDesigner.Core.Shading;
using PVDesigner.Core.Simulation;
using PVDesigner.Core.Solar;
using PVDesigner.Core.Updates;
using Xunit;

namespace PVDesigner.Core.Tests
{
    internal static class TestData
    {
        public static string ReadGz(string name)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", name);
            using var fs = File.OpenRead(path);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var reader = new StreamReader(gz);
            return reader.ReadToEnd();
        }

        private static MeteoDataset _riyadh;
        public static MeteoDataset Riyadh => _riyadh ??= PvgisTmyParser.Parse(ReadGz("pvgis_tmy_riyadh.json.gz"));

        public static PVProject RiyadhProject(double tilt = 25, double pitch = 6.5)
        {
            var p = PVProject.CreateDefault();
            p.Location.Latitude = 24.7;
            p.Location.Longitude = 46.7;
            p.Location.TimeZone = 3;
            p.Meteo = Riyadh;
            var f = p.Fields[0];
            f.Tilt = tilt;
            f.Azimuth = 0;
            f.Pitch = pitch;
            var module = ComponentLibrary.CreateDefault().FindModule(p.SystemDesign.ModuleId);
            f.Tables = LayoutGenerator.Generate(p, f, module).Tables;
            return p;
        }
    }

    public class SolarTests
    {
        [Fact]
        public void SolarNoonAtEquinoxMatchesLatitude()
        {
            // 2019-03-20 ~ equinox. Reference: pvlib (SPA) apparent elevation 49.8503, azimuth 179.7814.
            var sun = SolarPosition.Compute(new DateTime(2019, 3, 20, 12, 7, 0, DateTimeKind.Utc), 40.0, 0.0);
            Assert.InRange(sun.Elevation, 49.8503 - 0.05, 49.8503 + 0.05);
            Assert.InRange(sun.Azimuth, 179.7814 - 0.1, 179.7814 + 0.1);
        }

        [Fact]
        public void KnownPositionRiyadhSummerMorning()
        {
            // 21 June 2019 06:00 UTC (09:00 local) in Riyadh. Reference: pvlib (SPA) apparent elevation 50.2246, azimuth 82.4394.
            var sun = SolarPosition.Compute(new DateTime(2019, 6, 21, 6, 0, 0, DateTimeKind.Utc), 24.71, 46.68);
            Assert.InRange(sun.Elevation, 50.2246 - 0.05, 50.2246 + 0.05);
            Assert.InRange(sun.Azimuth, 82.4394 - 0.1, 82.4394 + 0.1);
        }

        [Fact]
        public void SunIsDownAtMidnight()
        {
            var sun = SolarPosition.Compute(new DateTime(2019, 6, 21, 21, 0, 0, DateTimeKind.Utc), 24.71, 46.68);
            Assert.False(sun.IsUp);
        }

        [Fact]
        public void PerezIsCloseToIsotropicForOvercastSky()
        {
            var sun = SolarPosition.Compute(new DateTime(2019, 6, 21, 9, 0, 0, DateTimeKind.Utc), 24.71, 46.68);
            var n = Irradiance.PlaneNormal(25, 180);
            var iso = Irradiance.Transpose(TranspositionModel.Isotropic, 200, 200, 0, sun, n, 0.2, 172);
            var perez = Irradiance.Transpose(TranspositionModel.Perez, 200, 200, 0, sun, n, 0.2, 172);
            Assert.InRange(perez.SkyDiffuse / iso.SkyDiffuse, 0.85, 1.25);
        }

        [Fact]
        public void TrackerFollowsSunAndBacktracks()
        {
            var morning = new SunPosition { Azimuth = 90, Elevation = 10 };
            double ideal = SingleAxisTracker.RotationAngle(morning, 0, 90, false, 0.4);
            double bt = SingleAxisTracker.RotationAngle(morning, 0, 90, true, 0.4);
            Assert.InRange(ideal, 79, 81);
            Assert.True(bt < ideal && bt > 0);
            var normal = SingleAxisTracker.PlaneNormal(ideal, 0);
            Assert.True(normal.X > 0.9); // facing east
        }

        [Fact]
        public void AzimuthConventions()
        {
            Assert.Equal(180, MathUtil.PvsystToCompassAzimuth(0), 6);
            Assert.Equal(90, MathUtil.PvsystToCompassAzimuth(-90), 6);
            Assert.Equal(270, MathUtil.PvsystToCompassAzimuth(90), 6);
            Assert.Equal(-90, MathUtil.CompassToPvsystAzimuth(90), 6);
        }
    }

    public class MeteoTests
    {
        [Fact]
        public void ParsesPvgisTmy()
        {
            var ds = TestData.Riyadh;
            Assert.True(ds.IsValid());
            Assert.Equal(610, ds.Altitude, 0);
            double ghi = ds.AnnualGhiKWh();
            Assert.InRange(ghi, 2000, 2400);
            Assert.True(ds.HasDni());
            Assert.InRange(ds.SunTimeOffsetHours, 0, 1);
        }

        [Fact]
        public void SyntheticYearIsPlausible()
        {
            var ds = SyntheticMeteoGenerator.Generate(24.7, 46.7, 600);
            Assert.True(ds.IsValid());
            Assert.InRange(ds.AnnualGhiKWh(), 1600, 2600);
            Assert.True(ds.MaxTemperature() > ds.MinTemperature() + 10);
        }

        [Fact]
        public void ParsesTimeFormats()
        {
            Assert.True(PvgisTmyParser.TryParseTime("20070315:1300", out int m, out int d, out int h));
            Assert.Equal((3, 15, 13), (m, d, h));
            Assert.True(PvgisTmyParser.TryParseTime("2007-03-15 13:00", out m, out d, out h));
            Assert.Equal((3, 15, 13), (m, d, h));
        }
    }

    public class LayoutAndShadingTests
    {
        private static readonly PVModuleSpec Module = ComponentLibrary.CreateDefault().FindModule("gen-mono-550-144");

        [Fact]
        public void PolygonBasics()
        {
            var sq = new[] { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10) };
            Assert.Equal(100, Polygon2D.Area(sq), 6);
            Assert.True(Polygon2D.Contains(sq, new Vec2(5, 5)));
            Assert.False(Polygon2D.Contains(sq, new Vec2(15, 5)));
            Assert.Equal(6, Polygon2D.Triangulate(sq).Count);
            var concave = new[] { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(5, 5), new Vec2(0, 10) };
            Assert.Equal(9, Polygon2D.Triangulate(concave).Count);
        }

        [Fact]
        public void LayoutFillsBoundaryWithRows()
        {
            var p = PVProject.CreateDefault();
            var f = p.Fields[0];
            f.Tilt = 25;
            f.Pitch = 6.5;
            var res = LayoutGenerator.Generate(p, f, Module);
            Assert.True(res.Tables.Count > 20, "tables: " + res.Tables.Count);
            Assert.True(res.Rows > 5);
            // All tables inside the boundary with the setback.
            foreach (var t in res.Tables)
                foreach (var c in TableGeometry.Footprint(f, t, Module))
                    Assert.True(Polygon2D.DistanceToBoundary(p.SiteBoundary, c) >= p.BoundarySetback - 1e-6 && Polygon2D.Contains(p.SiteBoundary, c));
        }

        [Fact]
        public void LayoutAvoidsObstacles()
        {
            var p = PVProject.CreateDefault();
            var f = p.Fields[0];
            int without = LayoutGenerator.Generate(p, f, Module).Tables.Count;
            p.Obstacles.Add(Obstacle.Create(ObstacleType.Building, 0, 0));
            int with = LayoutGenerator.Generate(p, f, Module).Tables.Count;
            Assert.True(with < without);
        }

        [Fact]
        public void FixedTableGeometryIsConsistent()
        {
            var f = new ArrayField { Tilt = 30, Azimuth = 0, ModulesAcross = 10, ModulesUp = 2 };
            var r = TableGeometry.Build(f, new PVTable(0, 0, 0, 0), Module);
            Assert.True(r.N.Z < 0 && r.N.Y > 0); // facing south and up
            Assert.Equal(0, Vec3.Dot(r.N, r.U), 6);
            Assert.Equal(0, Vec3.Dot(r.N, r.V), 6);
            Assert.Equal(f.GroundClearance, System.Math.Min(r.Corner(0).Y, r.Corner(2).Y), 6);
        }

        [Fact]
        public void RowToRowShadingAppearsAtLowSunOnly()
        {
            var p = TestData.RiyadhProject(tilt: 25, pitch: 5.0);
            var scene = ShadingScene.Build(p, Module, 4000);
            var res = ShadingCalculator.Compute(p, scene);
            var t = res.Global;
            double lowSouth = t.Interpolate(t.LinearLoss, 180, 5);
            double highSouth = t.Interpolate(t.LinearLoss, 180, 60);
            Assert.True(lowSouth > 0.3, "low sun loss " + lowSouth);
            Assert.True(highSouth < 0.01, "high sun loss " + highSouth);
            double elec = t.Interpolate(t.ElectricalLoss, 180, 15);
            Assert.True(elec >= t.Interpolate(t.LinearLoss, 180, 15));
            Assert.InRange(t.DiffuseFactor, 0.7, 1.0);
            Assert.InRange(t.AlbedoFactor, 0.0, 1.0);
        }

        [Fact]
        public void ObstacleCastsShadow()
        {
            var p = PVProject.CreateDefault();
            var f = p.Fields[0];
            f.Tables.Add(new PVTable(0, 0, 0, 0));
            // 20 m high building south of the single table.
            var b = Obstacle.Create(ObstacleType.Building, 0, -15);
            b.Height = 20;
            b.Width = 30;
            b.Depth = 5;
            p.Obstacles.Add(b);
            var scene = ShadingScene.Build(p, Module, 1000);
            var shade = scene.InstantTableShade(180, 30);
            Assert.True(shade[0] > 0.9, "shade " + shade[0]);
            var noShade = scene.InstantTableShade(90, 30);
            Assert.True(noShade[0] < 0.1);
        }

        [Fact]
        public void VerticalSunOnOpenTableIsNotShaded()
        {
            // Regression: vertical rays used a shortcut that returned transmission instead of opacity.
            var p = PVProject.CreateDefault();
            p.Fields[0].Tables.Add(new PVTable(0, 0, 0, 0));
            var scene = ShadingScene.Build(p, Module, 1000);
            Assert.True(scene.InstantTableShade(180, 90)[0] < 0.01);
        }

        [Fact]
        public void BackRowsSeeLessGroundThanFrontRow()
        {
            // Regression: rays passing under the front table must not count as open ground.
            var p = TestData.RiyadhProject(tilt: 25, pitch: 5.0);
            var res = ShadingCalculator.Compute(p, ShadingScene.Build(p, Module, 4000));
            Assert.InRange(res.Global.AlbedoFactor, 0.05, 0.9);
        }

        [Fact]
        public void TreeIsPartiallyTransparent()
        {
            var p = PVProject.CreateDefault();
            p.Fields[0].Tables.Add(new PVTable(0, 0, 0, 0));
            var tree = Obstacle.Create(ObstacleType.Tree, 0, -12);
            tree.Width = 30;
            tree.Height = 25;
            tree.TrunkHeight = 0.5;
            tree.Transparency = 0.5;
            p.Obstacles.Add(tree);
            var scene = ShadingScene.Build(p, Module, 1000);
            var shade = scene.InstantTableShade(180, 40);
            Assert.InRange(shade[0], 0.3, 0.7);
        }
    }

    public class ElectricalTests
    {
        [Fact]
        public void StringSizingRespectsVoltageLimits()
        {
            var lib = ComponentLibrary.CreateDefault();
            var p = TestData.RiyadhProject();
            var m = lib.FindModule("gen-mono-550-144");
            var inv = lib.FindInverter("gen-str-110k");
            var r = SystemSizing.Size(p, m, inv);
            Assert.False(r.HasErrors, string.Join("\n", r.Messages));
            Assert.True(r.StringVocCold <= inv.VdcMax);
            Assert.True(r.ModulesPerString >= r.MinModulesPerString);
            Assert.InRange(r.DcAcRatio, 1.0, 1.5);
            var assign = SystemSizing.AssignStrings(p, r);
            Assert.Equal(p.TotalTables(), assign.Count);
        }

        [Fact]
        public void InverterCurveAndClipping()
        {
            var inv = ComponentLibrary.CreateDefault().FindInverter("gen-str-110k");
            double ac = inv.AcOutput(50, out double clip);
            Assert.InRange(ac / 50, 0.97, 0.99);
            Assert.Equal(0, clip);
            double acMax = inv.AcOutput(200, out clip);
            Assert.Equal(inv.PacMax, acMax, 6);
            Assert.True(clip > 0);
        }

        [Fact]
        public void ParsesPanAndOnd()
        {
            const string pan = @"PVObject_=pvModule
  Version=7.2.0
  PVObject_Commercial=pvCommercial
    Manufacturer=ACME Solar
    Model=AS-550M
    Width=1.134
    Height=2.278
    Depth=0.035
  End of PVObject pvCommercial
  Technol=mtSiMono
  NCelS=72
  NCelP=2
  NDiode=3
  PNom=550.0
  Isc=13.98
  Voc=49.90
  Imp=13.13
  Vmp=41.90
  muISC=6.71
  muVocSpec=-134.7
  muPmpReq=-0.350
  VMaxIEC=1500
End of PVObject pvModule";
            var m = PanOndParser.ParsePan(pan);
            Assert.Equal("ACME Solar", m.Manufacturer);
            Assert.Equal(550, m.Pmax);
            Assert.Equal(2.278, m.Length, 3);
            Assert.InRange(m.TempCoeffVoc, -0.28, -0.26);
            Assert.InRange(m.TempCoeffIsc, 0.04, 0.06);

            const string ond = @"PVObject_=pvGInverter
  PVObject_Commercial=pvCommercial
    Manufacturer=ACME Power
    Model=AP-100K
  End of PVObject pvCommercial
  Converter=TConverter
    PNomConv=100.000
    PMaxOUT=110.000
    VOutConv=400.0
    VMppMin=200.0
    VMPPMax=1000.0
    VAbsMax=1100.0
    EfficMax=98.80
    EfficEuro=98.50
    NbMPPT=10
    IMaxDC=300.0
  End of TConverter
End of PVObject pvGInverter";
            var inv = PanOndParser.ParseOnd(ond);
            Assert.Equal(100, inv.PacNom);
            Assert.Equal(10, inv.MpptCount);
            Assert.Equal(30, inv.ImaxPerMppt, 6);
        }
    }

    public class SimulationTests
    {
        [Fact]
        public void RiyadhFixedTiltYieldIsRealistic()
        {
            var lib = ComponentLibrary.CreateDefault();
            var p = TestData.RiyadhProject(tilt: 25, pitch: 7.0);
            var m = lib.FindModule(p.SystemDesign.ModuleId);
            var inv = lib.FindInverter(p.SystemDesign.InverterId);
            var scene = ShadingScene.Build(p, m, 3000);
            var shading = ShadingCalculator.Compute(p, scene);
            var res = EnergySimulator.Run(p, m, inv, shading);

            // PVGIS reference for the same site: ~2417 kWh/m2 in plane, ~1777 kWh/kWp with 14 % losses.
            Assert.InRange(res.Annual.GlobInc, 2250, 2600);
            Assert.InRange(res.SpecificYield, 1600, 1950);
            Assert.InRange(res.PerformanceRatio, 0.70, 0.85);
            Assert.Equal(12, res.Monthly.Length);
            Assert.True(res.Losses.Count > 10);
            Assert.Equal(EnergySimulator.ProjectionYears, res.YearlyEnergy.Length);
            Assert.True(res.YearlyEnergy[24] < res.YearlyEnergy[0]);

            string html = ReportBuilder.BuildHtml(new ReportBuilder.Input
            {
                Project = p, Module = m, Inverter = inv, Simulation = res, Shading = shading,
                Economics = EconomicAnalysis.Compute(p.Economics, res.PnomDcKw, res.Annual.EGrid, p.Losses.AnnualDegradation),
                Bom = BillOfMaterials.Build(p, m, inv, res.Sizing)
            });
            Assert.Contains("Loss diagram", html);
            Assert.Contains("<svg", html);
            string csv = ReportBuilder.BuildHourlyCsv(p, res);
            Assert.Equal(8761, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        }

        [Fact]
        public void TrackerProducesMoreThanFixed()
        {
            var lib = ComponentLibrary.CreateDefault();
            var fixedP = TestData.RiyadhProject(tilt: 25, pitch: 7.0);
            var m = lib.FindModule(fixedP.SystemDesign.ModuleId);
            var inv = lib.FindInverter(fixedP.SystemDesign.InverterId);
            var fixedRes = EnergySimulator.Run(fixedP, m, inv, null);

            var trackP = PVProject.CreateDefault();
            trackP.Location = fixedP.Location;
            trackP.Meteo = fixedP.Meteo;
            var f = trackP.Fields[0];
            f.Mounting = MountingType.SingleAxisTracker;
            f.Azimuth = 0;
            f.ModulesUp = 1;
            f.ModulesAcross = 60;
            f.Pitch = 6.0;
            f.GroundClearance = 2.0;
            f.Tables = LayoutGenerator.Generate(trackP, f, m).Tables;
            var trackRes = EnergySimulator.Run(trackP, m, inv, null);
            Assert.True(trackRes.SpecificYield > fixedRes.SpecificYield * 1.1,
                $"tracker {trackRes.SpecificYield:0} vs fixed {fixedRes.SpecificYield:0}");
        }
    }

    public class SerializationAndUpdateTests
    {
        [Fact]
        public void ProjectRoundTrip()
        {
            var p = TestData.RiyadhProject();
            p.Obstacles.Add(Obstacle.Create(ObstacleType.Tree, 3, 4));
            string json = ProjectSerializer.Serialize(p);
            Assert.DoesNotContain("\"Length\"", json.Substring(0, System.Math.Min(json.Length, 2000)).Replace("\"Length\": 2.278", ""));
            var q = ProjectSerializer.Deserialize(json);
            Assert.Equal(p.Info.Name, q.Info.Name);
            Assert.Equal(p.TotalTables(), q.TotalTables());
            Assert.Equal(ObstacleType.Tree, q.Obstacles[0].Type);
            Assert.Equal(p.Meteo.Ghi[4000], q.Meteo.Ghi[4000]);
            Assert.Equal(p.SiteBoundary.Count, q.SiteBoundary.Count);
            Assert.Contains("\"FixedTilt\"", json);
        }

        [Fact]
        public void UndoRedo()
        {
            var p = PVProject.CreateDefault();
            var undo = new UndoStack();
            undo.Record(p);
            p.Info.Name = "Changed";
            var back = undo.Undo(p);
            Assert.Equal("New PV Project", back.Info.Name);
            var fwd = undo.Redo(back);
            Assert.Equal("Changed", fwd.Info.Name);
        }

        [Fact]
        public void SemVerOrdering()
        {
            Assert.True(SemVer.TryParse("v1.2.3", out var a));
            Assert.True(SemVer.TryParse("1.10.0", out var b));
            Assert.True(SemVer.TryParse("1.10.0-beta.1", out var c));
            Assert.True(b > a);
            Assert.True(b > c);
            Assert.True(c > a);
            Assert.False(SemVer.TryParse("abc", out _));
        }

        [Fact]
        public void ParsesGitHubRelease()
        {
            const string json = @"[
              {""tag_name"":""v1.1.0"",""name"":""1.1.0"",""body"":""notes"",""prerelease"":false,""draft"":false,
               ""assets"":[{""name"":""PVDesigner-Windows-x64.zip"",""browser_download_url"":""https://example.com/a.zip"",""size"":10}]},
              {""tag_name"":""v1.2.0-beta.1"",""prerelease"":true,""draft"":false,""assets"":[]},
              {""tag_name"":""v0.9.0"",""prerelease"":false,""draft"":false,""assets"":[]}
            ]";
            var stable = GitHubReleases.PickNewest(json, false);
            Assert.Equal("v1.1.0", stable.TagName);
            Assert.NotNull(stable.FindAsset("pvdesigner-windows-x64.zip"));
            Assert.True(GitHubReleases.IsNewer(stable, "1.0.5"));
            Assert.False(GitHubReleases.IsNewer(stable, "1.1.0"));
            var beta = GitHubReleases.PickNewest(json, true);
            Assert.Equal("v1.2.0-beta.1", beta.TagName);
            Assert.Equal(new string('a', 64), GitHubReleases.ParseSha256File(new string('A', 64) + "  file.zip\n"));
        }

        [Fact]
        public void EconomicsAreConsistent()
        {
            var e = EconomicAnalysis.Compute(new EconomicParameters { CapexPerWp = 0.5, TariffPerKwh = 0.08 }, 1000, 1_800_000, 0.004);
            Assert.Equal(500_000, e.Capex, 3);
            Assert.True(e.Npv > 0);
            Assert.InRange(e.PaybackYears, 2, 6);
            Assert.InRange(e.Irr, 0.15, 0.40);
            Assert.InRange(e.Lcoe, 0.01, 0.06);
        }
    }
}
