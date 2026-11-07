using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hearthvale
{
    /// <summary>Gun HUD: name, ammo, weapon slots, crosshair, hit marker, scope overlay with a reticle per magnification, toasts.</summary>
    public sealed class WeaponHud : MonoBehaviour
    {
        public Arsenal arsenal; public PlayerController player;
        readonly List<KeyValuePair<string, float>> toasts = new List<KeyValuePair<string, float>>();
        float hitT; Texture2D lensTex, ringTex; int scopeN;
        GUIStyle big, small, slot, mag;

        void Start()
        {
            arsenal.OnToast += t => { toasts.Add(new KeyValuePair<string, float>(t, Time.realtimeSinceStartup + 3.6f)); if (toasts.Count > 4) toasts.RemoveAt(0); };
            arsenal.OnHitMarker += () => hitT = 0.18f;
            arsenal.OnScope += n => scopeN = n;
            lensTex = MakeLens(512); ringTex = MakeRing(128, 0.88f);
        }

        // black everywhere except a round lens (full-screen overlay while scoped)
        static Texture2D MakeLens(int s)
        {
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2 - 1, dy = (y + 0.5f) / s * 2 - 1; float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.93f, 0.99f, d)); // soft lens edge
                    a = Mathf.Max(a, Mathf.SmoothStep(0, 0.25f, Mathf.InverseLerp(0.7f, 0.93f, d)) * 0.35f); // light vignette
                    px[y * s + x] = new Color32(0, 0, 0, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(); return t;
        }
        static Texture2D MakeRing(int s, float inner)
        {
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2 - 1, dy = (y + 0.5f) / s * 2 - 1; float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = d < 1 && d > inner ? 1f : 0f; px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(); return t;
        }

        static void Rect(float x, float y, float w, float h, Color c) { GUI.color = c; GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture); GUI.color = Color.white; }

        void OnGUI()
        {
            if (arsenal == null || !arsenal.owned.ContainsKey(arsenal.current)) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight, richText = true }; big.normal.textColor = Color.white;
                small = new GUIStyle(big) { fontSize = 20 }; slot = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                mag = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter }; mag.normal.textColor = new Color(1, 1, 1, 0.8f);
            }
            float W = Screen.width, H = Screen.height, cx = W / 2, cy = H / 2;
            var st = arsenal.Stats; var s = arsenal.CurSlot;

            if (arsenal.scoped && scopeN > 0)
            {
                float r = H; Color blk = Color.black;                       // the lens is a circle as tall as the screen
                Rect(0, 0, cx - r / 2, H, blk); Rect(cx + r / 2, 0, W - cx - r / 2, H, blk);   // black bars left and right of it
                DrawReticle(cx, cy, r * 0.5f, scopeN);
                GUI.DrawTexture(new Rect(cx - r / 2, cy - r / 2, r, r), lensTex);
                GUI.Label(new Rect(cx - 220, cy + r * 0.5f - 10, 440, 28), scopeN + "x · hold Shift to steady", mag);
            }
            else if (!arsenal.scoped)
            {
                // crosshair: opens up with spread
                float gap = 5 + st.spread * 260 * (1 - arsenal.aim * 0.7f); Color c = new Color(1, 1, 1, 0.9f * (1 - arsenal.aim * 0.6f));
                Rect(cx - 1, cy - gap - 8, 2, 8, c); Rect(cx - 1, cy + gap, 2, 8, c); Rect(cx - gap - 8, cy - 1, 8, 2, c); Rect(cx + gap, cy - 1, 8, 2, c);
            }
            if (hitT > 0)
            {
                hitT -= Time.unscaledDeltaTime; Color c = new Color(1, 0.35f, 0.3f, Mathf.Clamp01(hitT / 0.18f)); float o = 12;
                for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2)
                    { var m = GUI.matrix; GUIUtility.RotateAroundPivot(sx * sy * 45, new Vector2(cx + sx * o, cy + sy * o)); Rect(cx + sx * o - 1, cy + sy * o - 5, 2, 10, c); GUI.matrix = m; }
            }

            // bottom-right: ammo + weapon name + slots
            string label = st.name + (arsenal.Scope > 0 ? " · " + arsenal.Scope + "x" : "") + (arsenal.reloading > 0 ? " · reloading" : "");
            GUI.Label(new Rect(W - 440, H - 130, 420, 40), s.ammo + " <size=22>/ " + s.reserve + "</size>", big);
            GUI.Label(new Rect(W - 440, H - 88, 420, 28), label, small);
            float x = W - 20; var order = WeaponData.SlotOrder;
            for (int i = order.Length - 1; i >= 0; i--)
            {
                if (!arsenal.owned.ContainsKey(order[i])) continue;
                bool on = order[i] == arsenal.current; Rect(x - 92, H - 52, 90, 30, on ? new Color(1f, 0.85f, 0.4f, 0.9f) : new Color(0, 0, 0, 0.45f));
                slot.normal.textColor = on ? Color.black : Color.white; GUI.Label(new Rect(x - 92, H - 52, 90, 30), (i + 1) + " " + order[i], slot); x -= 96;
            }
            // toasts
            float ty = 100; toasts.RemoveAll(t => t.Value < Time.realtimeSinceStartup);
            foreach (var t in toasts) { var w = new Rect(W / 2 - 330, ty, 660, 30); Rect(w.x, w.y, w.width, w.height, new Color(0, 0, 0, 0.55f)); GUI.Label(w, t.Key, mag); ty += 34; }
        }

        // per-magnification reticles (simplified from the SVG sets in main.js)
        void DrawReticle(float cx, float cy, float R, int n)
        {
            Color k = new Color(0.05f, 0.05f, 0.05f, 0.95f), red = new Color(1f, 0.23f, 0.19f, 1f); float u = R / 100f;
            Action<float, float, float, float, Color> line = (x0, y0, w, h, c) => Rect(cx + x0 * u, cy + y0 * u, w * u, h * u, c);
            switch (n)
            {
                case 2:
                    line(-100, -0.5f, 76, 1.1f, k); line(24, -0.5f, 76, 1.1f, k); line(-0.5f, 24, 1.1f, 76, k);
                    GUI.DrawTexture(new Rect(cx - 13 * u, cy - 13 * u, 26 * u, 26 * u), ringTex, ScaleMode.StretchToFill, true, 0, red, 0, 0); line(-2.2f, -2.2f, 4.4f, 4.4f, red); break;
                case 3:
                    line(-100, -0.8f, 70, 1.6f, k); line(30, -0.8f, 70, 1.6f, k); line(-0.8f, 30, 1.6f, 70, k); line(-7, -0.8f, 14, 1.6f, red);
                    line(-14, 16, 28, 0.7f, k); line(-10, 26, 20, 0.7f, k); line(-6, 36, 12, 0.7f, k); break;
                case 4:
                    line(-100, -1.6f, 60, 3.2f, k); line(40, -1.6f, 60, 3.2f, k); line(-1.6f, 40, 3.2f, 60, k); line(-1.6f, -100, 3.2f, 60, k);
                    line(-40, -0.3f, 80, 0.6f, k); line(-0.3f, -40, 0.6f, 80, k); line(-1, -1, 2, 2, red);
                    line(-9, 10, 18, 0.6f, k); line(-7, 18, 14, 0.6f, k); line(-5, 26, 10, 0.6f, k); line(-3, 34, 6, 0.6f, k); break;
                default:
                    line(-100, -1.5f, 45, 3f, k); line(55, -1.5f, 45, 3f, k); line(-1.5f, 55, 3f, 45, k); line(-1.5f, -100, 3f, 45, k);
                    line(-55, -0.2f, 110, 0.4f, k); line(-0.2f, -55, 0.4f, 110, k);
                    for (int d = -50; d <= 50; d += 10) { if (d == 0) continue; line(d - 0.9f, -0.9f, 1.8f, 1.8f, k); line(-0.9f, d - 0.9f, 1.8f, 1.8f, k); }
                    break;
            }
        }
    }
}
