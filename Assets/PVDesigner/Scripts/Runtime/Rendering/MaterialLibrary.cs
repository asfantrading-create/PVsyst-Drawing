using UnityEngine;

namespace PVDesigner.Rendering
{
    /// <summary>
    /// Runtime created materials and procedural textures. Shaders used here are added to
    /// "Always Included Shaders" by the editor build step so they exist in player builds.
    /// </summary>
    public class MaterialLibrary
    {
        public Material Module { get; private set; }
        public Material ModuleBack { get; private set; }
        public Material Structure { get; private set; }
        public Material Ground { get; private set; }
        public Material Building { get; private set; }
        public Material Roof { get; private set; }
        public Material TreeTrunk { get; private set; }
        public Material TreeCrown { get; private set; }
        public Material Metal { get; private set; }
        public Material Tank { get; private set; }
        public Material Inverter { get; private set; }
        /// <summary>Unlit, vertex coloured, alpha blended (lines, overlays).</summary>
        public Material Overlay { get; private set; }
        public Material Skybox { get; private set; }

        public static readonly Color[] GroundColors =
        {
            new Color(0.78f, 0.70f, 0.55f), // sand
            new Color(0.42f, 0.52f, 0.30f), // grass
            new Color(0.62f, 0.62f, 0.60f)  // concrete
        };

        public static readonly string[] GroundNames = { "Sand", "Grass", "Concrete" };

        public MaterialLibrary(int groundStyle)
        {
            var standard = Shader.Find("Standard");
            Module = new Material(standard) { name = "PV Module", enableInstancing = true };
            Module.mainTexture = CreateCellTexture();
            Module.SetFloat("_Glossiness", 0.85f);
            Module.SetFloat("_Metallic", 0.15f);

            ModuleBack = Lit(standard, "Module back", new Color(0.85f, 0.86f, 0.88f), 0.3f, 0.1f);
            Structure = Lit(standard, "Structure", new Color(0.62f, 0.64f, 0.67f), 0.55f, 0.7f);
            Metal = Lit(standard, "Metal", new Color(0.55f, 0.57f, 0.6f), 0.5f, 0.8f);
            Building = Lit(standard, "Building", new Color(0.86f, 0.84f, 0.80f), 0.15f, 0f);
            Roof = Lit(standard, "Roof", new Color(0.55f, 0.35f, 0.30f), 0.2f, 0f);
            TreeTrunk = Lit(standard, "Trunk", new Color(0.36f, 0.25f, 0.16f), 0.1f, 0f);
            TreeCrown = Lit(standard, "Crown", new Color(0.24f, 0.45f, 0.20f), 0.1f, 0f);
            Tank = Lit(standard, "Tank", new Color(0.92f, 0.92f, 0.90f), 0.4f, 0.3f);
            Inverter = Lit(standard, "Inverter", new Color(0.95f, 0.95f, 0.95f), 0.5f, 0.2f);

            Ground = new Material(standard) { name = "Ground" };
            Ground.mainTexture = CreateGridTexture();
            Ground.SetFloat("_Glossiness", 0.05f);
            SetGroundStyle(groundStyle);

            var sprite = Shader.Find("Sprites/Default");
            Overlay = new Material(sprite) { name = "Overlay" };

            var sky = Shader.Find("Skybox/Procedural");
            if (sky != null)
            {
                Skybox = new Material(sky) { name = "Sky" };
                Skybox.SetFloat("_SunSize", 0.04f);
                Skybox.SetFloat("_AtmosphereThickness", 0.9f);
                Skybox.SetFloat("_Exposure", 1.2f);
            }
        }

        public void SetGroundStyle(int style)
        {
            style = Mathf.Clamp(style, 0, GroundColors.Length - 1);
            Ground.color = GroundColors[style];
        }

        private static Material Lit(Shader s, string name, Color c, float smooth, float metal)
        {
            var m = new Material(s) { name = name, color = c, enableInstancing = true };
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metal);
            return m;
        }

        /// <summary>Crystalline module look: 6 x 12 half-cut cells with busbars and an aluminium frame.</summary>
        private static Texture2D CreateCellTexture()
        {
            const int w = 96, h = 192;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Cells", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            var cell = new Color(0.07f, 0.11f, 0.22f);
            var cellEdge = new Color(0.12f, 0.17f, 0.30f);
            var gap = new Color(0.75f, 0.78f, 0.82f);
            var frame = new Color(0.70f, 0.72f, 0.75f);
            var bus = new Color(0.32f, 0.36f, 0.45f);
            var px = new Color[w * h];
            const int frameW = 2;
            const int cols = 6, rows = 12;
            float cw = (w - 2f * frameW) / cols, ch = (h - 2f * frameW) / rows;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c;
                    if (x < frameW || y < frameW || x >= w - frameW || y >= h - frameW)
                    {
                        c = frame;
                    }
                    else
                    {
                        float lx = (x - frameW) % cw, ly = (y - frameW) % ch;
                        if (lx < 1f || ly < 1f) c = gap;
                        else if (Mathf.Abs(lx - cw * 0.33f) < 0.5f || Mathf.Abs(lx - cw * 0.66f) < 0.5f) c = bus;
                        else c = Color.Lerp(cell, cellEdge, Mathf.Abs(ly / ch - 0.5f));
                    }
                    // Half-cut separation in the middle of the module.
                    if (y == h / 2 || y == h / 2 - 1) c = gap;
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>1 tile = 10 m with a stronger line every tile and faint 1 m subdivisions.</summary>
        private static Texture2D CreateGridTexture()
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Grid", wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
            var px = new Color[n * n];
            var rnd = new System.Random(7);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float v = 1f - 0.06f * (float)rnd.NextDouble();
                    bool major = x < 2 || y < 2;
                    bool minor = x % (n / 10) == 0 || y % (n / 10) == 0;
                    if (major) v *= 0.72f;
                    else if (minor) v *= 0.92f;
                    px[y * n + x] = new Color(v, v, v, 1f);
                }
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        public static Color InverterColor(int index)
        {
            if (index < 0) return new Color(0.35f, 0.35f, 0.35f);
            float hue = (index * 0.61803398875f) % 1f;
            return Color.HSVToRGB(hue, 0.65f, 0.95f);
        }

        /// <summary>Heat map colour for a shaded fraction (0 = blue-ish module colour, 1 = red).</summary>
        public static Color ShadeColor(float shade)
        {
            if (shade < 0) return Color.white;
            if (shade < 0.5f) return Color.Lerp(new Color(0.55f, 0.85f, 1f), new Color(1f, 0.9f, 0.2f), shade * 2f);
            return Color.Lerp(new Color(1f, 0.9f, 0.2f), new Color(0.95f, 0.15f, 0.1f), (shade - 0.5f) * 2f);
        }
    }
}
