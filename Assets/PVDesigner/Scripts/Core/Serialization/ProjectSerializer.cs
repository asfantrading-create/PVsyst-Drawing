using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using PVDesigner.Core.Model;

namespace PVDesigner.Core.Serialization
{
    /// <summary>JSON persistence of <see cref="PVProject"/> (public fields only, enums as strings).</summary>
    public static class ProjectSerializer
    {
        private sealed class FieldsOnlyResolver : DefaultContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                return objectType
                    .GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => !f.IsInitOnly && !f.IsDefined(typeof(NonSerializedAttribute), true))
                    .Cast<MemberInfo>()
                    .ToList();
            }

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var p = base.CreateProperty(member, memberSerialization);
                p.Readable = true;
                p.Writable = true;
                return p;
            }
        }

        public static JsonSerializerSettings CreateSettings(bool indented = true)
        {
            var s = new JsonSerializerSettings
            {
                ContractResolver = new FieldsOnlyResolver(),
                Formatting = indented ? Formatting.Indented : Formatting.None,
                NullValueHandling = NullValueHandling.Include,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                FloatFormatHandling = FloatFormatHandling.DefaultValue,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };
            s.Converters.Add(new StringEnumConverter());
            return s;
        }

        private static readonly JsonSerializerSettings Indented = CreateSettings(true);
        private static readonly JsonSerializerSettings Compact = CreateSettings(false);

        public static string Serialize(PVProject project, bool includeMeteo = true, bool indented = true)
        {
            var meteo = project.Meteo;
            try
            {
                if (!includeMeteo) project.Meteo = null;
                return JsonConvert.SerializeObject(project, indented ? Indented : Compact);
            }
            finally
            {
                project.Meteo = meteo;
            }
        }

        public static PVProject Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("Empty project file.");
            var p = JsonConvert.DeserializeObject<PVProject>(json, Indented);
            if (p == null) throw new FormatException("Invalid project file.");
            Upgrade(p);
            return p;
        }

        public static T Clone<T>(T obj)
        {
            var json = JsonConvert.SerializeObject(obj, Compact);
            return JsonConvert.DeserializeObject<T>(json, Compact);
        }

        /// <summary>Fills members missing in files written by older versions.</summary>
        private static void Upgrade(PVProject p)
        {
            p.Info ??= new ProjectInfo();
            p.Location ??= new SiteLocation();
            p.SiteBoundary ??= new List<Math.Vec2>();
            p.Fields ??= new List<ArrayField>();
            p.Obstacles ??= new List<Obstacle>();
            p.SystemDesign ??= new SystemConfig();
            p.Losses ??= new LossParameters();
            p.Simulation ??= new SimulationSettings();
            p.Economics ??= new Economics.EconomicParameters();
            p.CustomModules ??= new List<Components.PVModuleSpec>();
            p.CustomInverters ??= new List<Components.InverterSpec>();
            if (p.Losses.MonthlySoiling == null || p.Losses.MonthlySoiling.Length != 12)
                p.Losses.MonthlySoiling = new LossParameters().MonthlySoiling;
            foreach (var f in p.Fields)
            {
                f.Tables ??= new List<PVTable>();
                f.Boundary ??= new List<Math.Vec2>();
            }
            p.FormatVersion = PVProject.CurrentFormatVersion;
        }
    }
}
