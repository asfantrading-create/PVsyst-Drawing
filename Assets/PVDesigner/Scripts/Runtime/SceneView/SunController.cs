using System;
using PVDesigner.Core.Math;
using PVDesigner.Core.Meteo;
using PVDesigner.Core.Model;
using PVDesigner.Core.Solar;
using UnityEngine;
using UnityEngine.Rendering;

namespace PVDesigner.SceneView
{
    /// <summary>Drives the directional light, sky and sun path from the project location and the chosen date / time.</summary>
    public class SunController : MonoBehaviour
    {
        public Light Light { get; private set; }
        public SunPosition Sun { get; private set; }

        /// <summary>Day of the year (1..365).</summary>
        public int DayOfYear { get; private set; } = 172;
        /// <summary>Local standard time in hours (0..24).</summary>
        public float Hour { get; private set; } = 10f;
        public bool Animate;
        /// <summary>Simulated hours per real second while animating.</summary>
        public float AnimationSpeed = 1.5f;

        public event Action<SunPosition> SunChanged;

        private SiteLocation _location = new SiteLocation();
        private LineRenderer _sunPath;
        private Transform _pathRoot;
        private float _pathRadius = 80f;
        private Vector3 _pathCenter;

        private SceneBuilder _builder;

        public void Initialize(Transform world, SceneBuilder builder)
        {
            _builder = builder;
            var go = new GameObject("Sun");
            go.transform.SetParent(world, false);
            Light = go.AddComponent<Light>();
            Light.type = LightType.Directional;
            Light.shadows = App.AppSettings.SoftShadows ? LightShadows.Soft : LightShadows.Hard;
            Light.shadowStrength = 0.85f;
            Light.shadowBias = 0.03f;
            Light.shadowNormalBias = 0.25f;
            Light.color = new Color(1f, 0.97f, 0.9f);

            if (builder.Materials.Skybox != null) RenderSettings.skybox = builder.Materials.Skybox;
            RenderSettings.sun = Light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            QualitySettings.shadowDistance = 600f;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;

            _pathRoot = new GameObject("Sun path").transform;
            _pathRoot.SetParent(world, false);
            _sunPath = builder.CreateLine("Sun path", new Color(1f, 0.85f, 0.2f, 0.85f), 0.6f, _pathRoot);
            _sunPath.useWorldSpace = false;
        }

        public void SetLocation(SiteLocation location)
        {
            _location = location ?? new SiteLocation();
            Recompute();
        }

        public void SetDayAndHour(int dayOfYear, float hour)
        {
            DayOfYear = Mathf.Clamp(dayOfYear, 1, 365);
            Hour = Mathf.Repeat(hour, 24f);
            Recompute();
        }

        public void SetPathFrame(Vector3 center, float radius)
        {
            _pathCenter = center;
            _pathRadius = Mathf.Max(30f, radius);
            UpdateSunPath();
        }

        public DateTime LocalTime => new DateTime(MeteoDataset.ReferenceYear, 1, 1).AddDays(DayOfYear - 1).AddHours(Hour);

        public DateTime UtcTime => DateTime.SpecifyKind(LocalTime.AddHours(-_location.TimeZone), DateTimeKind.Utc);

        public void Recompute()
        {
            Sun = SolarPosition.Compute(UtcTime, _location.Latitude, _location.Longitude);
            ApplyLight();
            UpdateSunPath();
            SunChanged?.Invoke(Sun);
        }

        private void ApplyLight()
        {
            if (Light == null) return;
            var dir = SceneBuilder.ToUnity(Sun.Direction);
            float elev = (float)Sun.Elevation;
            if (elev > 0)
            {
                Light.transform.localRotation = Quaternion.LookRotation(-dir, Vector3.up);
                // Warm, dimmer light near the horizon.
                float k = Mathf.Clamp01(elev / 25f);
                Light.intensity = Mathf.Lerp(0.35f, 1.25f, k);
                Light.color = Color.Lerp(new Color(1f, 0.62f, 0.38f), new Color(1f, 0.97f, 0.92f), k);
                RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.25f, 0.28f, 0.38f), new Color(0.55f, 0.65f, 0.8f), k);
                RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.3f, 0.25f, 0.25f), new Color(0.5f, 0.52f, 0.55f), k);
                RenderSettings.ambientGroundColor = new Color(0.25f, 0.23f, 0.2f);
                Light.shadows = App.AppSettings.SoftShadows ? LightShadows.Soft : LightShadows.Hard;
            }
            else
            {
                // Night: keep a faint moon-like light so the model stays readable.
                Light.transform.localRotation = Quaternion.Euler(60, 180, 0);
                Light.intensity = 0.08f;
                Light.color = new Color(0.6f, 0.7f, 1f);
                Light.shadows = LightShadows.None;
                RenderSettings.ambientSkyColor = new Color(0.08f, 0.1f, 0.16f);
                RenderSettings.ambientEquatorColor = new Color(0.06f, 0.07f, 0.1f);
                RenderSettings.ambientGroundColor = new Color(0.04f, 0.04f, 0.05f);
            }
        }

        private void UpdateSunPath()
        {
            if (_sunPath == null) return;
            const int steps = 96;
            var pts = new System.Collections.Generic.List<Vector3>();
            var date = new DateTime(MeteoDataset.ReferenceYear, 1, 1).AddDays(DayOfYear - 1);
            for (int i = 0; i <= steps; i++)
            {
                var t = DateTime.SpecifyKind(date.AddHours(24.0 * i / steps - _location.TimeZone), DateTimeKind.Utc);
                var p = SolarPosition.Compute(t, _location.Latitude, _location.Longitude);
                if (p.Elevation < 0) continue;
                pts.Add(_pathCenter + SceneBuilder.ToUnity(p.Direction) * _pathRadius);
            }
            _sunPath.positionCount = pts.Count;
            _sunPath.SetPositions(pts.ToArray());
            _sunPath.startWidth = _sunPath.endWidth = _pathRadius * 0.006f * (_builder != null ? _builder.LineScale : 1f);
        }

        public void ShowSunPath(bool visible)
        {
            if (_pathRoot != null) _pathRoot.gameObject.SetActive(visible);
        }

        private void Update()
        {
            if (!Animate) return;
            float h = Hour + Time.deltaTime * AnimationSpeed;
            int d = DayOfYear;
            if (h >= 24f)
            {
                h -= 24f;
                d = d % 365 + 1;
            }
            SetDayAndHour(d, h);
        }

        public static string FormatDate(int dayOfYear)
        {
            var d = new DateTime(MeteoDataset.ReferenceYear, 1, 1).AddDays(dayOfYear - 1);
            return d.ToString("dd MMM", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string FormatHour(float hour)
        {
            int h = Mathf.FloorToInt(hour);
            int m = Mathf.FloorToInt((hour - h) * 60f + 0.5f);
            if (m == 60)
            {
                h++;
                m = 0;
            }
            return $"{h:00}:{m:00}";
        }

        public string Describe() =>
            Sun.IsUp
                ? $"Sun  az {MathUtil.CompassToPvsystAzimuth(Sun.Azimuth):0.0}°  h {Sun.Elevation:0.0}°"
                : "Sun below horizon";
    }
}
