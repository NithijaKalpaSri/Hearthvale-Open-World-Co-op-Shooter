using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>
    /// The four painted ghost faces (faceCanvas in ghosts.js): mottled skin, sunken shadowed sockets, small wet eyes,
    /// a mouth, damage, then a soft fade into darkness at the edges. Painted once at start-up with a tiny software painter.
    /// kind: 0 Hollow Man, 1 Cinder Woman, 2 The Patient, 3 a lost fan from the Arena.
    /// </summary>
    public static class GhostArt
    {
        const int S = 384; static System.Random rng = new System.Random(666);
        static float Rnd(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

        sealed class Canvas
        {
            public Color[] px = new Color[S * S];
            public void Clear(Color c) { for (int i = 0; i < px.Length; i++) px[i] = c; }
            Color Over(Color dst, Color src, float a) { return Color.Lerp(dst, src, Mathf.Clamp01(a)); }
            public void Blob(float x, float y, float r, Color c, float a, float sx = 1f)
            {
                x *= S / 512f; y = (512 - y) * S / 512f; r *= S / 512f;
                int x0 = Mathf.Max(0, (int)(x - r * sx)), x1 = Mathf.Min(S - 1, (int)(x + r * sx)), y0 = Mathf.Max(0, (int)(y - r)), y1 = Mathf.Min(S - 1, (int)(y + r));
                for (int j = y0; j <= y1; j++) for (int i = x0; i <= x1; i++)
                    { float dx = (i - x) / sx, dy = j - y; float d = Mathf.Sqrt(dx * dx + dy * dy) / r; if (d >= 1) continue; px[j * S + i] = Over(px[j * S + i], c, a * (1 - d)); }
            }
            public void Ellipse(float cx, float cy, float rx, float ry, Color c)
            {
                cx *= S / 512f; cy = (512 - cy) * S / 512f; rx *= S / 512f; ry *= S / 512f;
                for (int j = Mathf.Max(0, (int)(cy - ry)); j <= Mathf.Min(S - 1, (int)(cy + ry)); j++) for (int i = Mathf.Max(0, (int)(cx - rx)); i <= Mathf.Min(S - 1, (int)(cx + rx)); i++)
                    { float dx = (i - cx) / rx, dy = (j - cy) / ry; if (dx * dx + dy * dy <= 1) px[j * S + i] = c; }
            }
            public void Dot(float x, float y, float r, Color c)
            { x *= S / 512f; y = (512 - y) * S / 512f; r *= S / 512f; for (int j = (int)(y - r); j <= y + r; j++) for (int i = (int)(x - r); i <= x + r; i++) if (i >= 0 && j >= 0 && i < S && j < S && (i - x) * (i - x) + (j - y) * (j - y) <= r * r) px[j * S + i] = c; }
            public void Line(float x0, float y0, float x1, float y1, float w, Color c, float a = 1)
            {
                int n = (int)(Vector2.Distance(new Vector2(x0, y0), new Vector2(x1, y1)) * S / 512f) + 1;
                for (int k = 0; k <= n; k++) { float t = (float)k / n; float x = Mathf.Lerp(x0, x1, t) * S / 512f, y = (512 - Mathf.Lerp(y0, y1, t)) * S / 512f; float r = w * S / 512f / 2f;
                    for (int j = (int)(y - r); j <= y + r; j++) for (int i = (int)(x - r); i <= x + r; i++) if (i >= 0 && j >= 0 && i < S && j < S && (i - x) * (i - x) + (j - y) * (j - y) <= r * r) px[j * S + i] = Over(px[j * S + i], c, a); }
            }
            public void Crack(float x, float y, int len, Color c, float w) { for (int s = 0; s < len; s++) { float nx = x + Rnd(-14, 14), ny = y + Rnd(-16, 6); Line(x, y, nx, ny, w, c); x = nx; y = ny; } }
            public void Drip(float x, float y, float len, Color c) { for (int k = 0; k < 3; k++) Line(x + Rnd(-3, 3), y, x + Rnd(-6, 6), y + len * Rnd(0.7f, 1f), Rnd(2, 6), c, Rnd(0.5f, 0.9f)); }
            public void Mottle(int n, Color[] cols, float rmin, float rmax, float a) { for (int i = 0; i < n; i++) Blob(Rnd(60, 452), Rnd(30, 500), Rnd(rmin, rmax), cols[i % cols.Length], a); }
            public void Grain(float amt) { for (int i = 0; i < px.Length; i++) { float v = ((float)rng.NextDouble() - 0.5f) * amt / 255f; px[i].r += v; px[i].g += v; px[i].b += v; } }
            public Texture2D Finish()
            { // fade the edges into darkness: the face emerges from the black, no outline
                for (int j = 0; j < S; j++) for (int i = 0; i < S; i++)
                    { float dx = (i / (float)S - 0.5f) * 2f, dy = (j / (float)S - 0.5f) * 2f - 0.02f; float d = Mathf.Sqrt(dx * dx + dy * dy); float a = 1f - Mathf.SmoothStep(0.45f, 1f, d); px[j * S + i].a = a; }
                var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp }; t.SetPixels(px); t.Apply(true); return t;
            }
        }

        static Color C(string hex) { Color c; ColorUtility.TryParseHtmlString(hex, out c); return c; }
        static void Socket(Canvas g, float x, float y, float r, float depth) { g.Blob(x, y, r * 1.5f, new Color(0.03f, 0.015f, 0.015f), depth, 0.8f); g.Blob(x, y, r * 0.9f, Color.black, depth); }

        public static Texture2D Face(int kind)
        {
            var g = new Canvas(); g.Clear(Color.black);
            if (kind == 0)
            { // Hollow Man: waxy, bloodless, stretched; huge black pits, one small white glint each
                g.Ellipse(256, 250, 165, 235, C("#b8b3a8"));
                g.Mottle(90, new[] { C("#d8d3c6"), C("#8f897d"), C("#a49d8f"), C("#6f6a60") }, 30, 90, 0.35f); g.Blob(256, 110, 160, C("#e6e1d5"), 0.35f);
                foreach (int sx in new[] { -1, 1 }) g.Blob(256 + sx * 120, 300, 90, C("#3a3630"), 0.6f);
                Socket(g, 190, 210, 60, 1); Socket(g, 322, 214, 64, 1); g.Dot(196, 222, 4, Color.white); g.Dot(316, 226, 3, Color.white);
                g.Blob(256, 380, 80, Color.black, 1f, 0.45f); for (int k = 0; k < 6; k++) g.Crack(Rnd(150, 360), Rnd(60, 160), 8, new Color(0.16f, 0.14f, 0.12f), 1.5f);
            }
            else if (kind == 1)
            { // Cinder Woman: charred, blistered, glowing from inside, eyes still burning
                g.Ellipse(256, 255, 160, 230, C("#1a100b"));
                g.Mottle(120, new[] { C("#2e1a10"), C("#0b0605"), C("#45261a"), C("#120a07") }, 20, 70, 0.6f);
                for (int k = 0; k < 26; k++) g.Crack(Rnd(120, 390), Rnd(40, 420), 7, new Color(1f, Rnd(0.27f, 0.55f), 0.08f), Rnd(1.5f, 3.5f));
                Socket(g, 192, 215, 52, 1); Socket(g, 320, 218, 54, 1);
                foreach (var e in new[] { new Vector2(194, 220), new Vector2(318, 222) }) { g.Blob(e.x, e.y, 26, new Color(1f, 0.7f, 0.25f), 0.8f); g.Dot(e.x, e.y, 8, C("#fff6d8")); }
                g.Ellipse(256, 372, 96, 46, Color.black);
                for (int x = 176; x <= 334; x += 15) { g.Line(x + 5, 352, x + 5, 352 + Rnd(18, 30), 9, C("#d8b48a")); g.Line(x + 9, 392, x + 9, 392 - Rnd(14, 24), 9, C("#d8b48a")); }
            }
            else if (kind == 2)
            { // The Patient: grey-green, veined, bloodshot eyes rolled wide, lips stitched shut, bleeding
                g.Ellipse(256, 255, 160, 230, C("#7d8873"));
                g.Mottle(110, new[] { C("#9aa58e"), C("#5c6655"), C("#6e6058"), C("#48513f") }, 25, 80, 0.4f);
                foreach (int sx in new[] { -1, 1 }) g.Blob(256 + sx * 110, 310, 80, C("#2a2f25"), 0.55f);
                for (int k = 0; k < 18; k++) g.Crack(Rnd(110, 400), Rnd(50, 440), 9, new Color(0.24f, 0.12f, 0.21f), 1.5f);
                Socket(g, 190, 214, 66, 0.95f); Socket(g, 324, 220, 62, 0.95f);
                foreach (var e in new[] { new Vector3(192, 218, 24), new Vector3(322, 223, 21) })
                { g.Dot(e.x, e.y, e.z, C("#d8cdbd")); g.Blob(e.x, e.y, e.z, C("#6a2a26"), 0.6f); for (int k = 0; k < 8; k++) { float a = Rnd(0, 6.28f); g.Line(e.x + Mathf.Cos(a) * e.z, e.y + Mathf.Sin(a) * e.z, e.x + Mathf.Cos(a) * e.z * 0.4f, e.y + Mathf.Sin(a) * e.z * 0.4f, 1.2f, new Color(0.6f, 0.08f, 0.08f), 0.6f); } g.Dot(e.x, e.y, 4, C("#050505")); }
                g.Line(170, 380, 256, 396, 6, C("#1c0d0c")); g.Line(256, 396, 342, 378, 6, C("#1c0d0c"));
                for (int x = 180; x <= 332; x += 15) g.Line(x, 362 + Rnd(-3, 3), x + Rnd(-3, 3), 404, 3, C("#0b0606"));
                foreach (int x in new[] { 186, 200, 312, 326 }) g.Drip(x, 245, Rnd(110, 200), C("#5e0608")); g.Drip(250, 395, 90, C("#4a0406")); g.Drip(290, 396, 70, C("#4a0406"));
            }
            else
            { // a lost fan from the last concert: pale, washed-out, silent scream, smeared glow-paint
                g.Ellipse(256, 255, 158, 228, C("#c9cfd6"));
                g.Mottle(100, new[] { C("#e2e6ea"), C("#9aa1aa"), C("#b3b9c1"), C("#7b828c") }, 25, 80, 0.4f);
                foreach (int sx in new[] { -1, 1 }) g.Blob(256 + sx * 112, 300, 80, C("#4a4f58"), 0.55f);
                Socket(g, 190, 212, 60, 1); Socket(g, 322, 216, 60, 1);
                foreach (var e in new[] { new Vector2(193, 222), new Vector2(319, 224) }) { g.Blob(e.x, e.y, 18, C("#7fe8ff"), 0.7f); g.Dot(e.x, e.y, 4, C("#d6f6ff")); }
                g.Blob(256, 382, 78, Color.black, 1f, 0.55f);
                for (int k = 0; k < 3; k++) { g.Line(186, 270 + k * 18, 126, 262 + k * 18, 9, C("#ff3bd4"), 0.55f); g.Line(326, 270 + k * 18, 386, 262 + k * 18, 9, C("#3bd9ff"), 0.55f); }
                foreach (int x in new[] { 190, 322 }) g.Drip(x, 250, Rnd(90, 160), C("#2a2f38"));
            }
            g.Grain(26);
            return g.Finish();
        }

        /// <summary>Ragged cloth strips (alpha cut-out) for coats and gowns.</summary>
        public static Texture2D Rag(string hex)
        {
            int n = 128; var px = new Color[n * n]; Color c = C(hex);
            for (int x = 0; x < n; x += 4) { int h = (int)(n * Rnd(0.6f, 1f)); for (int y = 0; y < n; y++) for (int i = x; i < Mathf.Min(n, x + 4); i++) px[y * n + i] = y >= n - h ? new Color(c.r, c.g, c.b, 1) : new Color(c.r, c.g, c.b, 0); }
            for (int k = 0; k < 40; k++) { float cx = Rnd(0, n), cy = Rnd(0, n * 0.7f), r = Rnd(2, 7); for (int j = (int)(cy - r); j <= cy + r; j++) for (int i = (int)(cx - r); i <= cx + r; i++) if (i >= 0 && j >= 0 && i < n && j < n && (i - cx) * (i - cx) + (j - cy) * (j - cy) <= r * r) px[j * n + i].a = 0; }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat }; t.SetPixels(px); t.Apply(true); return t;
        }

        /// <summary>Glowing ember cracks for the Cinder Woman's skin (emission map).</summary>
        public static Texture2D Ember()
        {
            var c = new Canvas(); c.Clear(Color.black);
            for (int k = 0; k < 40; k++) { float x = Rnd(0, 512), y = Rnd(0, 512); for (int s = 0; s < 5; s++) { float nx = x + Rnd(-40, 40), ny = y + Rnd(-40, 40); c.Line(x, y, nx, ny, 3, new Color(1f, 0.35f, 0.06f)); x = nx; y = ny; } }
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat }; t.SetPixels(c.px); t.Apply(true); return t;
        }
    }
}
