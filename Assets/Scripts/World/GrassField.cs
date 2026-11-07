using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>
    /// Port of grass.js: wind-swayed grass tufts that follow the player. Positions come from a fixed world
    /// grid so grass never "swims" when it recentres. GPU instanced: ~11k tufts in ~11 draw calls.
    /// </summary>
    public sealed class GrassField
    {
        readonly Mesh mesh; readonly Material mat; readonly float R, S; readonly int side;
        readonly List<Matrix4x4[]> mats = new List<Matrix4x4[]>(); readonly List<Vector4[]> cols = new List<Vector4[]>();
        readonly List<MaterialPropertyBlock> mpbs = new List<MaterialPropertyBlock>(); int[] counts = new int[0]; int nBatches;
        Vector2 center = new Vector2(1e9f, 1e9f);
        readonly Dictionary<long, float[]> cache = new Dictionary<long, float[]>();

        public GrassField(float radius, float spacing)
        {
            R = radius; S = spacing; side = Mathf.CeilToInt(radius * 2 / spacing);
            mesh = PlantGeo.GrassTuft(2024);
            mat = NatureBuilder.PlantMat("grass", null, null, new Color(1.15f, 1.15f, 1.15f), 0.08f, 0.38f, 0.5f, 0.0f, false, true, 1f);
            mat.enableInstancing = true;
        }

        float[] Cell(int ix, int iz)
        {
            long key = (long)ix * 100003L + iz; float[] v;
            if (cache.TryGetValue(key, out v)) return v;
            double h1 = Math.Sin(ix * 12.9898 + iz * 78.233) * 43758.5453, r1 = h1 - Math.Floor(h1);
            double h2 = Math.Sin(ix * 39.346 + iz * 11.135) * 24634.6345, r2 = h2 - Math.Floor(h2);
            float x = (float)((ix + r1) * S), z = (float)((iz + r2) * S);
            float h = WorldData.HeightAt(x, z);
            bool ok = h > 1.8f && h < 40 && Hv.Fbm(x * 0.05 + 3, z * 0.05 - 7, 2) > 0.33;
            if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < WorldData.TownR + 40 || WorldData.ArenaDist(x, z) < 2) ok = false;
            v = ok ? new[] { x, h, z, (float)r1, (float)r2 } : null;
            if (cache.Count > 120000) cache.Clear();
            cache[key] = v; return v;
        }

        static Color Hsl(float h, float s, float l)
        {
            Func<float, float, float, float> f = (p, q, t) => { if (t < 0) t += 1; if (t > 1) t -= 1; if (t < 1f / 6) return p + (q - p) * 6 * t; if (t < 0.5f) return q; if (t < 2f / 3) return p + (q - p) * 6 * (2f / 3 - t); return p; };
            float q2 = l <= 0.5f ? l * (1 + s) : l + s - l * s, p2 = 2 * l - q2; return new Color(f(p2, q2, h + 1f / 3), f(p2, q2, h), f(p2, q2, h - 1f / 3));
        }

        /// <summary>focus in Unity space.</summary>
        public void Update(Vector3 focus)
        {
            Vector2 fj = new Vector2(focus.x, -focus.z); // JS space
            if (Vector2.Distance(fj, center) < 10) return;
            center = fj;
            int cx = Mathf.FloorToInt(fj.x / S) - (side >> 1), cz = Mathf.FloorToInt(fj.y / S) - (side >> 1);
            int total = 0;
            for (int a = 0; a < side; a++) for (int b = 0; b < side; b++)
                {
                    var v = Cell(cx + a, cz + b); if (v == null) continue;
                    float d = Mathf.Sqrt((v[0] - fj.x) * (v[0] - fj.x) + (v[2] - fj.y) * (v[2] - fj.y));
                    float fade = Mathf.Clamp01((R - d) / 8f); if (fade <= 0) continue;
                    int bi = total / 1023, bo = total % 1023;
                    while (bi >= mats.Count) { mats.Add(new Matrix4x4[1023]); cols.Add(new Vector4[1023]); mpbs.Add(new MaterialPropertyBlock()); }
                    float sc = (0.75f + v[4] * 0.7f) * fade;
                    mats[bi][bo] = Matrix4x4.TRS(new Vector3(v[0], v[1] - 0.02f, -v[2]), Quaternion.Euler(0, -v[3] * 360f, 0), new Vector3(sc, sc * (0.8f + v[3] * 0.5f), sc));
                    Color c = Hsl(0.235f + v[4] * 0.08f, 0.5f + v[3] * 0.2f, 0.6f + v[4] * 0.2f);
                    cols[bi][bo] = new Vector4(c.r, c.g, c.b, 1f);
                    total++;
                }
            nBatches = (total + 1022) / 1023; counts = new int[nBatches];
            for (int i = 0; i < nBatches; i++) { counts[i] = Mathf.Min(1023, total - i * 1023); mpbs[i].SetVectorArray("_InstColor", cols[i]); }
        }

        public void Render()
        {
            for (int i = 0; i < nBatches; i++) Graphics.DrawMeshInstanced(mesh, 0, mat, mats[i], counts[i], mpbs[i], ShadowCastingMode.Off, true, 0);
        }
    }

    /// <summary>Fireflies at night (port of buildFireflies): 160 additive glow sprites drifting around the player.</summary>
    public sealed class Fireflies
    {
        const int N = 160;
        readonly Mesh quad; readonly Material mat; readonly Vector3[] bases = new Vector3[N]; readonly Matrix4x4[] mats = new Matrix4x4[N];

        public Fireflies()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); quad = g.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.Destroy(g);
            // tiny, very bright unlit spheres: the bloom post effect turns them into glowing fireflies
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "fireflies" };
            mat.SetColor("_BaseColor", new Color(3.2f, 2.6f, 0.5f)); mat.enableInstancing = true;
            var r = new System.Random(5);
            for (int i = 0; i < N; i++)
                bases[i] = new Vector3((float)(r.NextDouble() - 0.5) * 80, 0.5f + (float)r.NextDouble() * 4, (float)(r.NextDouble() - 0.5) * 80);
        }

        public void Update(Vector3 focus, float night)
        {
            if (night <= 0.01f) return;
            float t = Time.time; float k = Mathf.Clamp01(night * 1.2f);
            for (int i = 0; i < N; i++)
            {
                float x = focus.x + bases[i].x + Mathf.Sin(t * 0.7f + i) * 2, z = focus.z + bases[i].z + Mathf.Cos(t * 0.6f + i * 1.3f) * 2;
                float y = WorldData.HeightAtU(x, z) + bases[i].y + Mathf.Sin(t * 2 + i) * 0.4f;
                mats[i] = Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.identity, Vector3.one * (0.075f * k * (0.7f + 0.3f * Mathf.Sin(t * 3f + i * 2.1f))));
            }
            Graphics.DrawMeshInstanced(quad, 0, mat, mats, N, null, ShadowCastingMode.Off, false, 0);
        }
    }
}
