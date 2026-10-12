using System;
using System.Collections.Generic;
using PVDesigner.Core.Components;
using PVDesigner.Core.Economics;
using PVDesigner.Core.Math;
using PVDesigner.Core.Meteo;
using PVDesigner.Core.Solar;

namespace PVDesigner.Core.Model
{
    /// <summary>Root document saved to *.pvdproj files (JSON).</summary>
    [Serializable]
    public class PVProject
    {
        public const string CurrentFormatVersion = "1.0";
        public const string FileExtension = ".pvdproj";

        public string FormatVersion = CurrentFormatVersion;
        public ProjectInfo Info = new ProjectInfo();
        public SiteLocation Location = new SiteLocation();

        /// <summary>Site boundary polygon in local metres (X east, Y north). Empty = unlimited.</summary>
        public List<Vec2> SiteBoundary = new List<Vec2>();
        /// <summary>Minimum distance between PV tables and the site boundary (m).</summary>
        public double BoundarySetback = 5.0;

        public List<ArrayField> Fields = new List<ArrayField>();
        public List<Obstacle> Obstacles = new List<Obstacle>();

        public SystemConfig SystemDesign = new SystemConfig();
        public LossParameters Losses = new LossParameters();
        public SimulationSettings Simulation = new SimulationSettings();
        public EconomicParameters Economics = new EconomicParameters();

        public MeteoDataset Meteo;

        /// <summary>Components imported from PAN / OND files, stored with the project so it stays portable.</summary>
        public List<PVModuleSpec> CustomModules = new List<PVModuleSpec>();
        public List<InverterSpec> CustomInverters = new List<InverterSpec>();

        public static PVProject CreateDefault()
        {
            var p = new PVProject();
            p.Info.Name = "New PV Project";
            p.Info.CreatedUtc = DateTime.UtcNow;
            p.Info.ModifiedUtc = p.Info.CreatedUtc;
            p.SiteBoundary.AddRange(new[]
            {
                new Vec2(-80, -60), new Vec2(80, -60), new Vec2(80, 60), new Vec2(-80, 60)
            });
            p.Fields.Add(ArrayField.CreateDefault("Field 1", p.Location.Latitude));
            return p;
        }

        public int TotalModules()
        {
            int n = 0;
            foreach (var f in Fields) n += f.ModuleCount;
            return n;
        }

        public int TotalTables()
        {
            int n = 0;
            foreach (var f in Fields) n += f.Tables.Count;
            return n;
        }

        public ArrayField FindField(string id) => Fields.Find(f => f.Id == id);
        public Obstacle FindObstacle(string id) => Obstacles.Find(o => o.Id == id);
    }

    [Serializable]
    public class ProjectInfo
    {
        public string Name = "";
        public string Client = "";
        public string Designer = "";
        public string Company = "";
        public string Description = "";
        public DateTime CreatedUtc;
        public DateTime ModifiedUtc;
    }

    [Serializable]
    public class SiteLocation
    {
        public string Name = "Riyadh, Saudi Arabia";
        public double Latitude = 24.71;
        public double Longitude = 46.68;
        public double Altitude = 610;
        /// <summary>Legal time zone offset from UTC (hours) used for displays and monthly binning.</summary>
        public double TimeZone = 3;
        public double Albedo = 0.2;
    }

    public enum MountingType
    {
        FixedTilt,
        SingleAxisTracker
    }

    public enum ModuleOrientation
    {
        Portrait,
        Landscape
    }

    /// <summary>A group of identical PV tables (sheds) or trackers sharing orientation and pitch.</summary>
    [Serializable]
    public class ArrayField
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Field";
        public MountingType Mounting = MountingType.FixedTilt;

        /// <summary>Plane tilt (deg) for fixed tables.</summary>
        public double Tilt = 25;
        /// <summary>
        /// PVsyst convention azimuth (deg): 0 = facing South, -90 = East, +90 = West, 180 = North.
        /// For trackers this is the azimuth of the rotation axis (0 = North-South axis).
        /// </summary>
        public double Azimuth = 0;

        /// <summary>Row to row distance (m), measured horizontally between equivalent points.</summary>
        public double Pitch = 6.5;

        public int ModulesAcross = 20;
        public int ModulesUp = 2;
        public ModuleOrientation Orientation = ModuleOrientation.Portrait;
        public double ModuleGap = 0.02;
        /// <summary>Gap between neighbouring tables of the same row (m).</summary>
        public double TableGap = 0.5;
        /// <summary>Lowest edge height above the base (m). For trackers: axis height.</summary>
        public double GroundClearance = 0.8;
        /// <summary>Base elevation of the field (m), e.g. a roof height.</summary>
        public double BaseElevation = 0;

        public double TrackerMaxAngle = 60;
        public bool Backtracking = true;

        /// <summary>Optional field specific boundary. When empty the site boundary is used.</summary>
        public List<Vec2> Boundary = new List<Vec2>();

        public List<PVTable> Tables = new List<PVTable>();

        public int ModulesPerTable => ModulesAcross * ModulesUp;
        public int ModuleCount => Tables.Count * ModulesPerTable;

        /// <summary>Compass azimuth (0 = N) that fixed tables face, or tracker axis compass azimuth.</summary>
        public double CompassAzimuth => MathUtil.PvsystToCompassAzimuth(Azimuth) % 360.0;

        public double TrackerAxisCompassAzimuth => MathUtil.Wrap360(Azimuth);

        public static ArrayField CreateDefault(string name, double latitude)
        {
            var f = new ArrayField { Name = name };
            f.Tilt = MathUtil.Clamp(System.Math.Round(System.Math.Abs(latitude) * 0.87 + 3.1), 5, 45);
            f.Azimuth = latitude >= 0 ? 0 : 180;
            return f;
        }

        public ArrayField CloneWithoutTables()
        {
            var c = (ArrayField)MemberwiseClone();
            c.Id = Guid.NewGuid().ToString("N");
            c.Boundary = new List<Vec2>(Boundary);
            c.Tables = new List<PVTable>();
            return c;
        }
    }

    /// <summary>A single table / tracker. Position is the centre of its horizontal footprint.</summary>
    [Serializable]
    public class PVTable
    {
        public double X;
        public double Z;
        /// <summary>Additional base offset (terrain), added to the field base elevation.</summary>
        public double Elevation;
        public int Row;
        public int IndexInRow;

        public PVTable() { }

        public PVTable(double x, double z, int row, int index)
        {
            X = x;
            Z = z;
            Row = row;
            IndexInRow = index;
        }
    }

    public enum ObstacleType
    {
        Building,
        Tree,
        Wall,
        Pole,
        Tank
    }

    [Serializable]
    public class Obstacle
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Obstacle";
        public ObstacleType Type = ObstacleType.Building;
        public double X;
        public double Z;
        public double BaseElevation;
        /// <summary>Size along the local X axis (m). Diameter for trees, poles and tanks.</summary>
        public double Width = 10;
        /// <summary>Size along the local Z axis (m).</summary>
        public double Depth = 8;
        public double Height = 6;
        /// <summary>Clockwise rotation seen from above (deg).</summary>
        public double Rotation;
        /// <summary>Trees: height of the trunk below the crown (m).</summary>
        public double TrunkHeight = 2;
        /// <summary>Light transmission of the crown (0 opaque .. 1 transparent), trees only.</summary>
        public double Transparency = 0.2;

        public static Obstacle Create(ObstacleType type, double x, double z)
        {
            var o = new Obstacle { Type = type, X = x, Z = z, Name = type.ToString() };
            switch (type)
            {
                case ObstacleType.Tree:
                    o.Width = o.Depth = 5;
                    o.Height = 8;
                    o.TrunkHeight = 2.5;
                    break;
                case ObstacleType.Wall:
                    o.Width = 20;
                    o.Depth = 0.3;
                    o.Height = 2.5;
                    o.Transparency = 0;
                    break;
                case ObstacleType.Pole:
                    o.Width = o.Depth = 0.4;
                    o.Height = 10;
                    o.Transparency = 0;
                    break;
                case ObstacleType.Tank:
                    o.Width = o.Depth = 8;
                    o.Height = 7;
                    o.Transparency = 0;
                    break;
                default:
                    o.Width = 12;
                    o.Depth = 8;
                    o.Height = 6;
                    o.Transparency = 0;
                    break;
            }
            return o;
        }
    }

    public enum ShadingMode
    {
        None,
        Linear,
        Electrical
    }

    [Serializable]
    public class SystemConfig
    {
        public string ModuleId = "gen-mono-550-144";
        public string InverterId = "gen-str-110k";
        /// <summary>Modules in series per string. 0 = automatic (maximum allowed).</summary>
        public int ModulesPerString;
        /// <summary>Number of inverters. 0 = automatic from the target DC/AC ratio.</summary>
        public int InverterCount;
        public double TargetDcAcRatio = 1.25;
        /// <summary>Minimum site ambient temperature for the Voc check (deg C).</summary>
        public double DesignTempMin = -5;
        /// <summary>Hot cell temperature for the Vmpp min check (deg C).</summary>
        public double DesignCellTempHot = 70;
        /// <summary>Cold cell temperature for the Vmpp max check (deg C).</summary>
        public double DesignCellTempMppCold = 20;
        /// <summary>DC cable cross section of string cables (mm2).</summary>
        public double StringCableSection = 6;
        public double AcCableSection = 240;
    }

    [Serializable]
    public class LossParameters
    {
        /// <summary>Monthly soiling loss (fraction). 12 values.</summary>
        public double[] MonthlySoiling = { 0.02, 0.02, 0.02, 0.02, 0.02, 0.02, 0.02, 0.02, 0.02, 0.02, 0.02, 0.02 };
        /// <summary>Module quality loss (fraction, negative = gain).</summary>
        public double ModuleQuality = -0.004;
        /// <summary>Light induced degradation (fraction).</summary>
        public double Lid = 0.015;
        /// <summary>Module mismatch loss at MPP (fraction).</summary>
        public double Mismatch = 0.01;
        /// <summary>DC ohmic loss at STC (fraction of power).</summary>
        public double DcOhmicAtStc = 0.015;
        /// <summary>AC ohmic loss at nominal power (fraction).</summary>
        public double AcOhmicAtNominal = 0.01;
        /// <summary>MV transformer loss at nominal power (fraction). 0 for LV connections.</summary>
        public double TransformerAtNominal = 0.0;
        /// <summary>System unavailability (fraction of the year).</summary>
        public double Unavailability = 0.005;
        /// <summary>Annual degradation for long term projections (fraction / year).</summary>
        public double AnnualDegradation = 0.004;
        /// <summary>Faiman thermal constant Uc (W/m2K). 29 free mounted, 20 semi-integrated, 15 integrated.</summary>
        public double ThermalUc = 29;
        /// <summary>Faiman wind dependent factor Uv (W/m2K / m/s).</summary>
        public double ThermalUv = 0;
        /// <summary>ASHRAE IAM parameter.</summary>
        public double IamB0 = 0.05;
    }

    [Serializable]
    public class SimulationSettings
    {
        public TranspositionModel Transposition = TranspositionModel.Perez;
        public ShadingMode Shading = ShadingMode.Linear;
        /// <summary>Fraction of the electrical shading effect applied in Electrical mode (PVsyst default 100%).</summary>
        public double ElectricalEffectFraction = 1.0;
        /// <summary>Maximum number of sample points used by the shading engine.</summary>
        public int MaxShadingSamples = 12000;
    }
}
