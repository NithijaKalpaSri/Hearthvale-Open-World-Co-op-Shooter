#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hearthvale.EditorTools
{
    /// <summary>
    /// Port of src/textures.js: the same procedural textures (same seeds, same style),
    /// baked once to real PNG files with mipmaps and proper normal maps, so nothing is
    /// generated at runtime (faster start-up, GPU-compressed, mip-mapped = good on low-end).
    /// Menu: Hearthvale / Generate Textures
    /// </summary>
    public static class TextureGen
    {
        const string Dir = "Assets/Resources/Textures";

        static float[] TileNoise(int size, int period, int octaves, int seed)
        {
            var o = new float[size * size];
            var rng = new Mulberry32(seed);
            float amp = 1, norm = 0;
            for (int oc = 0; oc < octaves; oc++)
            {
                int P = period << oc;
                var lat = new float[P * P];
                for (int i = 0; i < lat.Length; i++) lat[i] = rng.Next();
                for (int y = 0; y < size; y++)
                {
                    float fy = (float)y / size * P; int y0 = (int)Mathf.Floor(fy); float ty = fy - y0, sy = ty * ty * (3 - 2 * ty);
                    int r0 = (y0 % P) * P, r1 = ((y0 + 1) % P) * P;
                    for (int x = 0; x < size; x++)
                    {
                        float fx = (float)x / size * P; int x0 = (int)Mathf.Floor(fx); float tx = fx - x0, sx = tx * tx * (3 - 2 * tx);
                        int c0 = x0 % P, c1 = (x0 + 1) % P;
                        float a = lat[r0 + c0], b = lat[r0 + c1], c = lat[r1 + c0], d = lat[r1 + c1];
                        o[y * size + x] += ((a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy) * amp;
                    }
                }
                norm += amp; amp *= 0.5f;
            }
            for (int i = 0; i < o.Length; i++) o[i] /= norm;
            return o;
        }

        static void Save(string name, int S, Func<int, int, Vector3> rgb255, bool srgb = true)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false, !srgb);
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    var c = rgb255(x, y);
                    px[y * S + x] = new Color32((byte)Mathf.Clamp(c.x, 0, 255), (byte)Mathf.Clamp(c.y, 0, 255), (byte)Mathf.Clamp(c.z, 0, 255), 255);
                }
            tex.SetPixels32(px);
            WritePng(name, tex);
        }

        static void WritePng(string name, Texture2D tex)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Dir + "/" + name + ".png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static void Normal(string name, float[] h, int S, float strength)
        {
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float xl = h[y * S + ((x - 1 + S) % S)], xr = h[y * S + ((x + 1) % S)];
                    float yd = h[((y - 1 + S) % S) * S + x], yu = h[((y + 1) % S) * S + x];
                    float nx = (xl - xr) * strength, ny = (yd - yu) * strength, nz = 1;
                    float l = Mathf.Sqrt(nx * nx + ny * ny + nz * nz); nx /= l; ny /= l; nz /= l;
                    px[y * S + x] = new Color32((byte)((nx * 0.5f + 0.5f) * 255), (byte)((ny * 0.5f + 0.5f) * 255), (byte)((nz * 0.5f + 0.5f) * 255), 255);
                }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            WritePng(name + "_n", tex);
        }

        [MenuItem("Hearthvale/Generate Textures")]
        public static void GenerateAll()
        {
            try
            {
                int S = 512;
                // ground detail
                { var n1 = TileNoise(S, 8, 5, 11); var n2 = TileNoise(S, 64, 2, 12); var h = new float[S * S];
                  for (int i = 0; i < h.Length; i++) h[i] = n1[i] * 0.55f + n2[i] * 0.45f;
                  Save("ground", S, (x, y) => { float v = 0.72f + h[y * S + x] * 0.38f + (n2[y * S + x] > 0.72f ? -0.08f : 0); return new Vector3(v, v, v) * 255; });
                  Normal("ground", h, S, 6); }
                // pavement slabs
                { int s = 512, T = 4; var n = TileNoise(s, 16, 4, 31); var h = new float[s * s]; int cell = s / T;
                  Save("pave", s, (x, y) => { int i = y * s + x; bool gx = (x % cell) < 6, gy = (y % cell) < 6; int slab = (x / cell) * 7 + (y / cell) * 13;
                      h[i] = gx || gy ? 0 : 0.7f + n[i] * 0.3f; float v = gx || gy ? 120 : 186 + n[i] * 35 + (slab % 5) * 4; return new Vector3(v, v * 0.98f, v * 0.95f); });
                  Normal("pave", h, s, 4); }
                // plaster
                { var n = TileNoise(S, 16, 5, 41); Save("plaster", S, (x, y) => { float v = 220 + n[y * S + x] * 35; return new Vector3(v, v, v); }); Normal("plaster", n, S, 2.5f); }
                // brick
                { int rows = 16, cols = 8; var n = TileNoise(S, 32, 4, 51); var c = TileNoise(S, 8, 2, 52); var h = new float[S * S]; float bh = (float)S / rows, bw = (float)S / cols;
                  Save("brick", S, (x, y) => { int row = (int)(y / bh); float off = row % 2 == 1 ? bw / 2 : 0; float bx = (x + off) % bw, by = y % bh; bool mortar = bx < 4 || by < 4;
                      int id = (int)((x + off) / bw) * 31 + row * 17; int i = y * S + x; h[i] = mortar ? 0.1f : 0.8f + n[i] * 0.2f;
                      if (mortar) { float v = 175 + n[i] * 30; return new Vector3(v, v * 0.97f, v * 0.92f); }
                      float tone = (((long)id * 9301 + 49297) % 233) / 233f;
                      return new Vector3(150 + tone * 50 + n[i] * 30 - c[i] * 20, 78 + tone * 22 + n[i] * 15, 62 + tone * 16 + n[i] * 12); });
                  Normal("brick", h, S, 5); }
                // wood siding
                { int boards = 8; var n = TileNoise(S, 4, 4, 61); var g = TileNoise(S, 64, 1, 62); var h = new float[S * S]; float bs = (float)S / boards;
                  Save("siding", S, (x, y) => { float by = (y % bs) / bs; int i = y * S + x; h[i] = by < 0.08f ? 0 : 0.6f + by * 0.4f;
                      float v = (by < 0.08f ? 150 : 215 + by * 25) + (g[i] - 0.5f) * 20 + n[i] * 10; return new Vector3(v, v, v); });
                  Normal("siding", h, S, 4); }
                // wood
                { var n = TileNoise(S, 4, 4, 81); var g = TileNoise(S, 2, 3, 82);
                  Save("wood", S, (x, y) => { int i = y * S + x; float grain = Mathf.Sin((float)x / S * 60 + g[i] * 14) * 0.5f + 0.5f;
                      return new Vector3(120 + grain * 40 + n[i] * 20, 76 + grain * 26 + n[i] * 12, 48 + grain * 16); });
                  Normal("wood", g, S, 2); }
                // bark
                { var n = TileNoise(S, 16, 4, 91); var h = new float[S * S];
                  Save("bark", S, (x, y) => { int i = y * S + x; float s = Mathf.Abs(Mathf.Sin((float)x / S * Mathf.PI * 12 + n[i] * 6)); h[i] = s; float v = 70 + s * 45 + n[i] * 20; return new Vector3(v * 1.05f, v * 0.82f, v * 0.62f); });
                  Normal("bark", h, S, 6); }
                // rock
                { var n = TileNoise(S, 8, 6, 101); Save("rock", S, (x, y) => { float v = 120 + n[y * S + x] * 90; return new Vector3(v, v * 0.97f, v * 0.93f); }); Normal("rock", n, S, 9); }
                // leaf + water normals
                { var n = TileNoise(S, 32, 3, 111); Normal("leaf", n, S, 10); }
                { var a = TileNoise(S, 8, 4, 121); var b = TileNoise(S, 24, 3, 122); var h = new float[S * S]; for (int i = 0; i < h.Length; i++) h[i] = a[i] * 0.6f + b[i] * 0.4f; Normal("water", h, S, 7); }
                // sandstone ashlar
                { int rows = 8; var n = TileNoise(S, 16, 5, 141); var w = TileNoise(S, 4, 3, 142); var pit = TileNoise(S, 64, 2, 143); var h = new float[S * S]; float bh = (float)S / rows;
                  var rr = new Mulberry32(144); var joints = new System.Collections.Generic.List<int>[rows];
                  for (int r = 0; r < rows; r++) { var js = new System.Collections.Generic.List<int>(); int xx = (int)Mathf.Floor(rr.Next() * 40); while (xx < S) { js.Add(xx); xx += 70 + (int)Mathf.Floor(rr.Next() * 80); } joints[r] = js; }
                  Save("sandstone", S, (x, y) => { int row = (int)(y / bh); float by = y % bh; var js = joints[row]; float jd = S; int id = 0;
                      for (int k = 0; k < js.Count; k++) { float d = Mathf.Abs(x - js[k]); if (d < jd) jd = d; if (x >= js[k]) id = k; }
                      int i = y * S + x; bool mortar = by < 3 || jd < 2; float edge = Mathf.Min(by, bh - by, jd) / 6f;
                      h[i] = mortar ? 0 : Mathf.Min(1, 0.55f + edge * 0.2f) * (0.8f + n[i] * 0.2f) - (pit[i] > 0.8f ? 0.15f : 0);
                      if (mortar) { float v = 168 + n[i] * 30; return new Vector3(v, v * 0.95f, v * 0.86f); }
                      float tone = (((long)id * 7919 + (long)row * 104729) % 97) / 97f;
                      float vv = 206 + tone * 26 + (n[i] - 0.5f) * 34 - (w[i] > 0.7f ? 18 : 0) - (pit[i] > 0.82f ? 22 : 0); return new Vector3(vv, vv * 0.97f, vv * 0.9f); });
                  Normal("sandstone", h, S, 5); }
                // cobblestone lanes
                { int s = 512, G = 8; float cell = (float)s / G; var rr = new Mulberry32(151); var pts = new Vector3[G * G];
                  for (int j = 0; j < G; j++) for (int i = 0; i < G; i++) pts[j * G + i] = new Vector3((i + 0.15f + rr.Next() * 0.7f) * cell, (j + 0.15f + rr.Next() * 0.7f) * cell, rr.Next());
                  var n = TileNoise(s, 16, 4, 152); var dust = TileNoise(s, 4, 3, 153); var h = new float[s * s];
                  Save("cobble", s, (x, y) => { int ci = (int)(x / cell), cj = (int)(y / cell); float d1 = 1e9f, d2 = 1e9f, tone = 0;
                      for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) { int ii = (ci + di + G) % G, jj = (cj + dj + G) % G; var p = pts[jj * G + ii];
                          float px = p.x + (ci + di - ii) * cell, py = p.y + (cj + dj - jj) * cell; float d = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                          if (d < d1) { d2 = d1; d1 = d; tone = p.z; } else if (d < d2) d2 = d; }
                      int i2 = y * s + x; float gap = d2 - d1; h[i2] = gap < 5f ? 0 : Mathf.Min(1, 0.5f + gap / 36f) * (0.85f + n[i2] * 0.15f);
                      if (gap < 5f) { float v = 120 + dust[i2] * 40; return new Vector3(v * 1.05f, v * 0.95f, v * 0.82f); }
                      float vv = 172 + tone * 40 + (n[i2] - 0.5f) * 30 + (dust[i2] - 0.5f) * 30; return new Vector3(vv, vv * 0.96f, vv * 0.88f); });
                  Normal("cobble", h, s, 5); }
                SiteDecal("A"); SiteDecal("B");
            // ---- living-things detail normals
            { // skin pores: fine isotropic bumps with a few creases
                int n2 = 256; var a = TileNoise(n2, 64, 3, 2001); var b = TileNoise(n2, 16, 3, 2002); var h = new float[n2 * n2];
                for (int i = 0; i < h.Length; i++) h[i] = a[i] * 0.7f + b[i] * 0.3f - (a[i] > 0.62f ? 0.18f : 0f);
                Normal("skin", h, n2, 3.2f);
            }
            { // woven cloth
                int n2 = 256; var a = TileNoise(n2, 32, 2, 2011); var h = new float[n2 * n2];
                for (int y = 0; y < n2; y++) for (int x = 0; x < n2; x++) { float wx = Mathf.Sin(x * Mathf.PI * 2 / 4f), wy = Mathf.Sin(y * Mathf.PI * 2 / 4f); h[y * n2 + x] = 0.5f + 0.25f * wx * wy + 0.25f * a[y * n2 + x]; }
                Normal("cloth", h, n2, 2.2f);
            }
            { // hide / wrinkled leathery skin
                int n2 = 256; var a = TileNoise(n2, 8, 4, 2021); var b = TileNoise(n2, 32, 3, 2022); var h = new float[n2 * n2];
                for (int i = 0; i < h.Length; i++) { float w = Mathf.Abs(a[i] - 0.5f) * 2f; h[i] = Mathf.Pow(w, 0.6f) * 0.8f + b[i] * 0.2f; }
                Normal("hide", h, n2, 4.5f);
            }
            { // reptile scales: hexagon-ish cells
                int n2 = 256, G = 16; float cell = (float)n2 / G; var rr = new Mulberry32(2031); var pts = new Vector2[G * G]; var h = new float[n2 * n2];
                for (int j = 0; j < G; j++) for (int i = 0; i < G; i++) pts[j * G + i] = new Vector2((i + 0.2f + rr.Next() * 0.6f) * cell, (j + 0.2f + rr.Next() * 0.6f) * cell);
                for (int y = 0; y < n2; y++) for (int x = 0; x < n2; x++)
                {
                    int ci = (int)(x / cell), cj = (int)(y / cell); float d1 = 1e9f, d2 = 1e9f;
                    for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) { int ii = (ci + di + G) % G, jj = (cj + dj + G) % G; var p = pts[jj * G + ii]; float px = p.x + (ci + di - ii) * cell, py = p.y + (cj + dj - jj) * cell; float d = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py)); if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d; }
                    float gap = d2 - d1; h[y * n2 + x] = gap < 2.5f ? 0f : Mathf.Min(1f, 0.4f + gap / 14f);
                }
                Normal("scales", h, n2, 5f);
            }

                PlantTextures();
            }
            finally { AssetDatabase.Refresh(); }
            Configure();
            Debug.Log("[Hearthvale] Textures generated in " + Dir);
        }

        // spray-painted marker with ring, letter, overspray and drips (RGBA)
        static void SiteDecal(string letter)
        {
            int S = 256; var px = new Color32[S * S]; var rr = new Mulberry32(letter[0] * 31);
            Action<float, float, float, byte> dot = (cx, cy, r, a) =>
            {
                for (int y = Mathf.Max(0, (int)(cy - r - 1)); y <= Mathf.Min(S - 1, (int)(cy + r + 1)); y++)
                    for (int x = Mathf.Max(0, (int)(cx - r - 1)); x <= Mathf.Min(S - 1, (int)(cx + r + 1)); x++)
                    { float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)); if (d <= r) { var o = px[y * S + x]; if (a > o.a) px[y * S + x] = new Color32(214, 72, 38, a); } }
            };
            Action<Vector2, Vector2, float> seg = (a, b, w) => { int n = (int)(Vector2.Distance(a, b) / 1.5f) + 1; for (int i = 0; i <= n; i++) { var p = Vector2.Lerp(a, b, (float)i / n); dot(p.x, p.y, w / 2, 235); } };
            for (int i = 0; i < 720; i++) { float a = i / 720f * Mathf.PI * 2; dot(S / 2 + Mathf.Cos(a) * S * 0.4f, S / 2 + Mathf.Sin(a) * S * 0.4f, 7, 235); }
            float c = S / 2f;
            if (letter == "A") { seg(new Vector2(c - 48, c - 62), new Vector2(c, c + 66), 18); seg(new Vector2(c + 48, c - 62), new Vector2(c, c + 66), 18); seg(new Vector2(c - 28, c - 18), new Vector2(c + 28, c - 18), 16); }
            else
            {
                seg(new Vector2(c - 36, c - 64), new Vector2(c - 36, c + 64), 18);
                for (int k = 0; k <= 40; k++) { float a = -Mathf.PI / 2 + k / 40f * Mathf.PI; dot(c - 36 + Mathf.Cos(a) * 36 + 10, c + 32 + Mathf.Sin(a) * 30, 8.5f, 235); dot(c - 36 + Mathf.Cos(a) * 42 + 8, c - 32 + Mathf.Sin(a) * 34, 8.5f, 235); }
            }
            for (int k = 0; k < 900; k++) { float a = rr.Next() * Mathf.PI * 2, r = S * (0.33f + rr.Next() * 0.16f); dot(S / 2 + Mathf.Cos(a) * r, S / 2 + Mathf.Sin(a) * r, 1.2f, (byte)(rr.Next() * 120)); }
            for (int k = 0; k < 5; k++) { float x = S * (0.3f + rr.Next() * 0.4f); float len = 10 + rr.Next() * 26; seg(new Vector2(x, S * 0.2f), new Vector2(x, S * 0.2f - len), 3); }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false); tex.SetPixels32(px); WritePng("decal" + letter, tex);
        }


        // ------------------------------------------------------------------ nature textures (new realistic look)
        sealed class Canvas
        {
            public int W, H; public Color32[] px;
            public Canvas(int w, int h) { W = w; H = h; px = new Color32[w * h]; }
            public void Plot(float x, float y, Color c)
            {
                int ix = (int)x, iy = (int)y; if (ix < 0 || iy < 0 || ix >= W || iy >= H) return;
                px[iy * W + ix] = new Color32((byte)(Mathf.Clamp01(c.r) * 255), (byte)(Mathf.Clamp01(c.g) * 255), (byte)(Mathf.Clamp01(c.b) * 255), 255);
            }
            public void Disc(float cx, float cy, float r, Color c)
            {
                for (int y = (int)(cy - r); y <= (int)(cy + r); y++) for (int x = (int)(cx - r); x <= (int)(cx + r); x++)
                        if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) Plot(x, y, c);
            }
            public void Line(Vector2 a, Vector2 b, float w, Color c0, Color c1)
            {
                int n = (int)(Vector2.Distance(a, b) * 1.5f) + 1;
                for (int i = 0; i <= n; i++) { float t = (float)i / n; Disc(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t), w * 0.5f, Color.Lerp(c0, c1, t)); }
            }
            // a pointed leaf: base at (x,y), pointing along `ang`
            public void Leaf(float x, float y, float ang, float len, float wid, float lum, Color tint, Mulberry32 r)
            {
                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                int steps = (int)(len * 2.2f), across = (int)(wid * 2.2f);
                for (int i = 0; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    float hw = wid * 0.5f * Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.75f)) * (1f - 0.25f * t);
                    for (int j = -across; j <= across; j++)
                    {
                        float s = (float)j / across;
                        float px_ = x + ca * len * t - sa * hw * s, py_ = y + sa * len * t + ca * hw * s;
                        float shade = lum * (0.82f + 0.2f * t) * (1f - 0.18f * Mathf.Abs(s));
                        // midrib + side veins
                        if (Mathf.Abs(s) < 0.07f) shade *= 1.18f;
                        else if (Mathf.Abs(Mathf.Repeat(t * 7f + Mathf.Abs(s) * 2.2f, 1f) - 0.5f) < 0.04f) shade *= 1.1f;
                        Plot(px_, py_, new Color(shade * tint.r, shade * tint.g, shade * tint.b));
                    }
                }
            }
            public Texture2D ToTex()
            {
                var t = new Texture2D(W, H, TextureFormat.RGBA32, false); t.SetPixels32(px); return t;
            }
        }

        static void PlantTextures()
        {
            // leaf cluster: ~46 leaves on transparent background (grey-green: instance colour tints the hue)
            {
                var c = new Canvas(512, 512); var r = new Mulberry32(777);
                for (int i = 0; i < 52; i++)
                {
                    float cx = 40 + r.Next() * 432, cy = 40 + r.Next() * 432;
                    float len = 74 + r.Next() * 52, wid = 30 + r.Next() * 18, ang = r.Next() * Mathf.PI * 2;
                    float lum = 0.62f + r.Next() * 0.3f;
                    var tint = new Color(0.92f + r.Next() * 0.08f, 1f, 0.82f + r.Next() * 0.14f);
                    c.Leaf(cx, cy, ang, len, wid, lum, tint, r);
                }
                WritePng("leafcluster", c.ToTex());
            }
            // pine branch card: twig along the card with drooping needle sprays (wide card, 512x256)
            {
                var c = new Canvas(512, 256); var r = new Mulberry32(888);
                var twigA = new Color(0.30f, 0.20f, 0.12f); var twigB = new Color(0.22f, 0.15f, 0.09f);
                Vector2 a = new Vector2(8, 128), b = new Vector2(500, 120);
                c.Line(a, b, 5f, twigA, twigB);
                for (int i = 0; i < 520; i++)
                {
                    float t = r.Next(); Vector2 root = Vector2.Lerp(a, b, t) + new Vector2(0, (r.Next() - 0.5f) * 3);
                    float side = r.Next() < 0.5f ? -1 : 1;
                    float len = (46 + r.Next() * 30) * (1.05f - t * 0.55f);
                    float ang = side * (0.55f + r.Next() * 0.8f) + 1.57f * 0 + (side > 0 ? 0 : 0);
                    Vector2 dir = new Vector2(Mathf.Cos(ang * 0.55f + 0.6f * (1 - t)), Mathf.Sin(ang));
                    Vector2 tip = root + dir.normalized * len + new Vector2(10 + r.Next() * 8, 0);
                    float lum = 0.72f + r.Next() * 0.28f;
                    var base_ = new Color(0.55f * lum, 0.8f * lum, 0.5f * lum);
                    c.Line(root, tip, 3.4f, base_ * 0.7f, new Color(0.7f * lum, 1f * lum, 0.6f * lum));
                }
                WritePng("pinebranch", c.ToTex());
            }
            // palm frond: rachis along u, drooping leaflets both sides, bright tips
            {
                var c = new Canvas(512, 256); var r = new Mulberry32(999);
                c.Line(new Vector2(0, 128), new Vector2(510, 128), 5f, new Color(0.45f, 0.55f, 0.22f), new Color(0.35f, 0.5f, 0.2f));
                for (int i = 0; i < 70; i++)
                {
                    float t = i / 70f; float x0 = 6 + t * 480; float len = (20 + 100 * Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.6f))) * (0.9f + r.Next() * 0.2f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 root = new Vector2(x0, 128 + side * 2);
                        Vector2 tip = root + new Vector2(28 + r.Next() * 14, side * len);
                        float lum = 0.8f + r.Next() * 0.2f;
                        c.Line(root, tip, 4.5f, new Color(0.38f * lum, 0.62f * lum, 0.2f * lum), new Color(0.6f * lum, 0.85f * lum, 0.32f * lum));
                    }
                }
                WritePng("palmfrond", c.ToTex());
            }
            // soft gas/cloud puff (alpha), port of cloudTex()
            {
                int S = 256; var n = TileNoise(S, 8, 5, 131); var px = new Color32[S * S];
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float dx = (float)x / S - 0.5f, dy = (float)y / S - 0.5f; float r = Mathf.Sqrt(dx * dx + dy * dy * 1.69f) * 2;
                    float a = Mathf.Pow(Mathf.Max(0, 1 - r), 1.4f) * (0.55f + n[y * S + x] * 0.7f);
                    float shade = 235 + dy * 20 - n[y * S + x] * 20;
                    px[y * S + x] = new Color32((byte)Mathf.Clamp(shade, 0, 255), (byte)Mathf.Clamp(shade, 0, 255), (byte)Mathf.Clamp(shade + 6, 0, 255), (byte)Mathf.Clamp(a * 255, 0, 255));
                }
                var t = new Texture2D(S, S, TextureFormat.RGBA32, false); t.SetPixels32(px); WritePng("gascloud", t);
            }
            // birch bark: pale with dark horizontal lenticels + normal map
            {
                int S = 256; var n = TileNoise(S, 8, 4, 1201); var g = TileNoise(S, 16, 3, 1202); var rr = new Mulberry32(1203);
                var mark = new bool[S * S]; var h = new float[S * S];
                for (int k = 0; k < 90; k++) { int x0 = (int)(rr.Next() * S), y0 = (int)(rr.Next() * S), len = 10 + (int)(rr.Next() * 34), th = 1 + (int)(rr.Next() * 3);
                    for (int dx = 0; dx < len; dx++) for (int dy = 0; dy < th; dy++) mark[((y0 + dy) % S) * S + ((x0 + dx) % S)] = true; }
                Save("birchbark", S, (x, y) => { int i = y * S + x; float v = 215 + n[i] * 30 - (g[i] > 0.62f ? 22 : 0); h[i] = mark[i] ? 0f : 0.6f + n[i] * 0.3f;
                    if (mark[i]) { v = 52 + n[i] * 30; } return new Vector3(v, v * 0.98f, v * 0.94f); });
                Normal("birchbark", h, S, 4);
            }
            // soft glow (fireflies, moon halo)
            {
                int S = 64; var px = new Color32[S * S];
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float d = Mathf.Sqrt((x - S / 2f) * (x - S / 2f) + (y - S / 2f) * (y - S / 2f)) / (S / 2f); float a = Mathf.Clamp01(1 - d); a = a * a * (0.4f + a); px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255)); }
                var t = new Texture2D(S, S, TextureFormat.RGBA32, false); t.SetPixels32(px); WritePng("glow", t);
            }
        }

        static void Configure()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter; if (imp == null) continue;
                bool normal = path.EndsWith("_n.png"), decal = path.Contains("/decal") || path.Contains("/leafcluster") || path.Contains("/pinebranch") || path.Contains("/palmfrond") || path.Contains("/glow") || path.Contains("/gascloud");
                imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.wrapMode = decal ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                imp.mipmapEnabled = true; imp.anisoLevel = 4; imp.filterMode = FilterMode.Trilinear;
                imp.alphaIsTransparency = decal;
                if (decal) { imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = 0.5f; imp.maxTextureSize = 512; }
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.maxTextureSize = 512;
                imp.SaveAndReimport();
            }
        }
    }
}
#endif
