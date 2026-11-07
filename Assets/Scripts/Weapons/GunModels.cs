using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>
    /// The six guns and four scopes (buildGunModel / buildScopeModel in weapons.js): same shapes and colours,
    /// now with properly rounded edges, a glossy painted body, blued steel and real wood grain.
    /// The models are authored in the JS (right-handed) space, so the barrel points to -z, and mirrored on build.
    /// </summary>
    public static class GunModels
    {
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { mats.Clear(); meshCache.Clear(); }
        static readonly Dictionary<string, Mesh[]> meshCache = new Dictionary<string, Mesh[]>();

        static Material Mat(string key)
        {
            Material m;
            if (mats.TryGetValue(key, out m) && m != null) return m;
            if (key.StartsWith("main_"))
            {
                Color c = M.Hex(WeaponData.W[key.Substring(5)].color) * 0.8f; c.a = 1;
                m = HvMaterials.Make(key, null, null, 0.72f, 0.1f, "#ffffff", false); m.SetColor("_BaseColor", c);
            }
            else if (key == "dark") m = HvMaterials.Make(key, null, null, 0.7f, 0.85f, "#26272c", false);
            else if (key == "wood") m = HvMaterials.Make(key, "wood", "wood_n", 0.45f, 0f, "#ffffff", false);
            else if (key == "scope") m = HvMaterials.Make(key, null, null, 0.6f, 0.7f, "#1d1e22", false);
            else if (key == "lens") { m = HvMaterials.Make(key, null, null, 0.95f, 0.2f, "#3a5f7a", false); m.SetColor("_EmissionColor", M.Hex("#10202c")); }
            else m = HvMaterials.Make(key, null, null, 0.4f, 0f, "#ffffff", false);
            mats[key] = m; return m;
        }

        static void RB(Geo g, float w, float h, float d, float x, float y, float z, float rx = 0)
        { g.Append(Geo.RoundedBox(w, h, d, Mathf.Min(w, Mathf.Min(h, d)) * 0.25f, 2), M.T(x, y, z) * M.RotX(rx)); }

        /// <summary>A gun as a GameObject with 3 renderers (body / steel / wood). Barrel points along +z (Unity).</summary>
        public static GameObject Build(string kind, bool castShadow = false)
        {
            Mesh[] ms;
            if (!meshCache.TryGetValue(kind, out ms) || ms[0] == null)
            {
                Geo main = new Geo(), dark = new Geo(), wood = new Geo();
                // shared details: rear sight and trigger guard
                dark.Append(Geo.Box(0.015f, 0.03f, 0.02f), M.T(0, 0.075f, -0.25f));
                dark.Append(Geo.Tube(new Vector3(0, -0.06f, -0.055f), new Vector3(0, -0.13f, -0.02f), new Vector3(0, -0.06f, 0.015f), 8, 0.008f, 6), Matrix4x4.identity);
                switch (kind)
                {
                    case "pistol":
                        RB(main, 0.09f, 0.11f, 0.32f, 0, 0, -0.08f); RB(dark, 0.07f, 0.17f, 0.09f, 0, -0.11f, 0.03f, -0.25f); RB(dark, 0.04f, 0.04f, 0.08f, 0, 0, -0.27f); break;
                    case "rifle":
                        RB(main, 0.09f, 0.13f, 0.62f, 0, 0, -0.2f); RB(dark, 0.05f, 0.05f, 0.3f, 0, 0.01f, -0.62f); RB(dark, 0.07f, 0.2f, 0.09f, 0, -0.14f, -0.15f);
                        RB(wood, 0.08f, 0.13f, 0.22f, 0, -0.03f, 0.18f); RB(dark, 0.04f, 0.05f, 0.12f, 0, 0.1f, -0.15f); break;
                    case "shotgun":
                        RB(main, 0.11f, 0.12f, 0.55f, 0, 0, -0.2f); RB(dark, 0.06f, 0.06f, 0.4f, 0, 0.04f, -0.55f); RB(wood, 0.08f, 0.08f, 0.2f, 0, -0.06f, -0.45f); RB(wood, 0.09f, 0.16f, 0.25f, 0, -0.05f, 0.15f); break;
                    case "dmr":
                        RB(main, 0.085f, 0.12f, 0.7f, 0, 0, -0.22f); RB(dark, 0.045f, 0.045f, 0.38f, 0, 0.01f, -0.75f); RB(dark, 0.06f, 0.16f, 0.08f, 0, -0.13f, -0.18f);
                        RB(dark, 0.08f, 0.13f, 0.24f, 0, -0.03f, 0.2f); RB(dark, 0.03f, 0.03f, 0.4f, 0, 0.075f, -0.2f); break;
                    case "bolt":
                        RB(wood, 0.08f, 0.11f, 0.85f, 0, 0, -0.25f); RB(dark, 0.04f, 0.04f, 0.55f, 0, 0.02f, -0.92f); RB(main, 0.06f, 0.07f, 0.22f, 0, 0.06f, -0.12f);
                        dark.Append(Geo.Cylinder(0.012f, 0.012f, 0.09f, 8), M.T(0.06f, 0.06f, -0.02f) * M.RotZ(Mathf.PI / 2));
                        RB(wood, 0.085f, 0.15f, 0.28f, 0, -0.04f, 0.22f); break;
                    default: // sniper
                        RB(main, 0.08f, 0.11f, 0.8f, 0, 0, -0.25f); RB(dark, 0.04f, 0.04f, 0.45f, 0, 0.01f, -0.85f); RB(wood, 0.08f, 0.14f, 0.25f, 0, -0.04f, 0.2f); break;
                }
                ms = new[] { main.ToUnityMesh(kind + "_main"), dark.ToUnityMesh(kind + "_steel"), wood.ToUnityMesh(kind + "_wood") };
                meshCache[kind] = ms;
            }
            var go = new GameObject("Gun_" + kind);
            string[] keys = { "main_" + kind, "dark", "wood" };
            for (int i = 0; i < 3; i++)
            {
                if (ms[i].vertexCount == 0) continue;
                var c = new GameObject(keys[i]); c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = ms[i];
                var r = c.AddComponent<MeshRenderer>(); r.sharedMaterial = Mat(keys[i]);
                r.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off; r.receiveShadows = false;
            }
            return go;
        }

        /// <summary>A scope that sits on top of the gun: longer and wider for more magnification.</summary>
        public static GameObject BuildScope(int n)
        {
            Mesh[] ms; string key = "scope" + n;
            if (!meshCache.TryGetValue(key, out ms) || ms[0] == null)
            {
                Geo dark = new Geo(), lens = new Geo();
                float len = 0.14f + n * 0.035f, r = 0.026f + n * 0.004f;
                dark.Append(Geo.Cylinder(r * 0.75f, r * 0.75f, len, 14), M.RotX(Mathf.PI / 2));
                foreach (int s in new[] { -1, 1 })
                {
                    dark.Append(Geo.Cylinder(r, r * 0.8f, 0.05f, 14), M.T(0, 0, s * (len / 2)) * M.RotX(s * Mathf.PI / 2));
                    lens.Append(Geo.Cylinder(r * 0.85f, r * 0.85f, 0.004f, 14), M.T(0, 0, s * (len / 2 + 0.026f)) * M.RotX(Mathf.PI / 2));
                }
                foreach (float z in new[] { -len * 0.25f, len * 0.25f }) dark.Append(Geo.Box(0.03f, 0.04f, 0.03f), M.T(0, -r - 0.01f, z));
                if (n >= 4) dark.Append(Geo.Cylinder(0.014f, 0.014f, 0.03f, 8), M.T(0, r * 0.75f + 0.012f, 0));
                ms = new[] { dark.ToUnityMesh(key + "_body"), lens.ToUnityMesh(key + "_lens") };
                meshCache[key] = ms;
            }
            var go = new GameObject("Scope_" + n); string[] keys = { "scope", "lens" };
            for (int i = 0; i < 2; i++)
            {
                var c = new GameObject(keys[i]); c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = ms[i];
                var rr = c.AddComponent<MeshRenderer>(); rr.sharedMaterial = Mat(keys[i]); rr.shadowCastingMode = ShadowCastingMode.Off; rr.receiveShadows = false;
            }
            return go;
        }

        public static readonly Dictionary<int, string> ScopeTint = new Dictionary<int, string> { { 2, "#9fe8ff" }, { 3, "#8fe3c1" }, { 4, "#ffe38a" }, { 6, "#ff9fb5" } };
    }
}
