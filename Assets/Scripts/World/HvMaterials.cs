using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>Builds the shared materials (port of the material table in buildTown). One shader, ~20 materials.</summary>
    public static class HvMaterials
    {
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        public static Material Window, Porch, Bulb, Ceiling; // emissive ones, driven by the day/night cycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cache.Clear(); Window = Porch = Bulb = Ceiling = null; }

        static Texture2D Tex(string n) { return Resources.Load<Texture2D>("Textures/" + n); }

        public static Material Make(string name, string tex, string normal, float smooth, float metal, string tint = "#ffffff", bool vertexColor = true,
            bool transparent = false, bool doubleSided = false, float normalScale = 1f)
        {
            var sh = Shader.Find("Hearthvale/Lit");
            var m = new Material(sh) { name = name };
            if (tex != null) m.SetTexture("_BaseMap", Tex(tex));
            if (normal != null && Tex(normal) != null) { m.SetTexture("_BumpMap", Tex(normal)); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_UseNormal", 1); m.SetFloat("_BumpScale", normalScale); }
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            m.SetColor("_BaseColor", M.Hex(tint));
            m.SetFloat("_VertexColorAmount", vertexColor ? 1 : 0);
            m.SetFloat("_Cull", doubleSided ? 0 : 2);
            if (transparent)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0); m.renderQueue = (int)RenderQueue.Transparent;
            }
            return m;
        }

        public static Material Get(string key)
        {
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            switch (key)
            {
                case "cobble": m = Make(key, "cobble", "cobble_n", 0.05f, 0); break;
                case "dirt": m = Make(key, "ground", "ground_n", 0.02f, 0); break;
                case "pave": m = Make(key, "pave", "pave_n", 0.1f, 0); break;
                case "concrete": m = Make(key, "plaster", "plaster_n", 0.08f, 0); break;
                case "stone": m = Make(key, "rock", "rock_n", 0.12f, 0, "#ffffff", true, false, false, 0.6f); break;
                case "sandstone": m = Make(key, "sandstone", "sandstone_n", 0.07f, 0); break;
                case "wood": m = Make(key, "wood", "wood_n", 0.25f, 0); break;
                case "metal": m = Make(key, null, null, 0.55f, 0.7f); break;
                case "plaster": m = Make(key, "plaster", "plaster_n", 0.05f, 0); break;
                case "siding": m = Make(key, "siding", "siding_n", 0.3f, 0.3f); break;
                case "brick": m = Make(key, "brick", "brick_n", 0.1f, 0); break;
                case "cloth": m = Make(key, null, null, 0.05f, 0, "#ffffff", true, false, true); break;
                case "trim": m = Make(key, null, null, 0.45f, 0); break;
                case "glass":
                    m = Make(key, null, null, 0.95f, 0.1f, "#2d3a48", true, true); m.SetFloat("_Alpha", 0.6f); Window = m; break;
                case "glassDark": m = Make(key, null, null, 0.4f, 0, "#1d1a17"); break;
                case "porch": m = Make(key, null, null, 0.2f, 0, "#fff3d6", false); Porch = m; break;
                case "ceiling": m = Make(key, null, null, 0.1f, 0, "#e9e0d0", false); m.SetColor("_EmissionColor", M.Hex("#fff4e4") * 0.3f); Ceiling = m; break;
                case "hedge": m = Make(key, null, "leaf_n", 0.15f, 0, "#ffffff", true, false, true, 1.2f); break;
                case "fountainWater": m = Make(key, null, null, 0.95f, 0, "#1f4f5c", false); break;
                case "decalA": m = Make(key, "decalA", null, 0.15f, 0, "#ffffff", false, true, true); break;
                case "decalB": m = Make(key, "decalB", null, 0.15f, 0, "#ffffff", false, true, true); break;
                default: m = Make(key, null, null, 0.3f, 0); break;
            }
            cache[key] = m;
            return m;
        }

        /// <summary>Night glow: windows, porch/ceiling lights and street lamps.</summary>
        public static void SetNightGlow(float glow)
        {
            if (Window != null) Window.SetColor("_EmissionColor", M.Hex("#ffb765") * glow * 0.9f);
            if (Porch != null) Porch.SetColor("_EmissionColor", M.Hex("#ffcf8a") * glow * 4f);
            if (Bulb != null) Bulb.SetColor("_EmissionColor", M.Hex("#ffcf8a") * glow * 4f);
        }

        public static Material BulbMat()
        {
            if (Bulb == null) Bulb = Make("bulb", null, null, 0.3f, 0, "#fff4dc", false);
            return Bulb;
        }
    }
}
