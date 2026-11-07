using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hearthvale
{
    /// <summary>
    /// Port of map.js: press M for the big island map (drag to pan, wheel to zoom, left click = waypoint, right click = clear).
    /// Works in JS coordinates (x, z; z grows downwards on the map, north is up); Unity objects are mirrored (jsZ = -unityZ).
    /// A waypoint also gets a pink beam + ring in the world and a distance readout on the HUD.
    /// </summary>
    public sealed class MapUI : MonoBehaviour
    {
        public PlayerController player; public HvWorld world; public Pickups pickups; public CritterSystem critters; public Garage garage;
        public bool open;

        const int RES = 2048; const float WS = WorldData.WorldSize; const float PPM = RES / WS;
        static float ToTex(float v) { return (v / WS + 0.5f) * RES; }

        struct Label { public string name; public float x, z, size; public Label(string n, float x, float z, float s) { name = n; this.x = x; this.z = z; size = s; } }
        static readonly Label[] Labels =
        {
            new Label("The Arena", WorldData.ArenaC.x, WorldData.ArenaC.y, 0.9f), new Label("The Old Quarter", 0, -WorldData.TownR - 14, 1.25f),
            new Label("Site A", 38, -46, 0.9f), new Label("Site B", -37, -50, 0.9f), new Label("Mid", 0, 24, 0.6f), new Label("Long", 60, 8, 0.6f),
            new Label("West Alley", -58, -12, 0.55f), new Label("Tunnel", -14, 21, 0.55f), new Label("Bazaar Row", 40, 68, 0.55f), new Label("Rampart Row", -40, -68, 0.55f),
            new Label("Well Yard", 26, 20, 0.5f), new Label("Olive Yard", -33, 51, 0.5f), new Label("Mount Puffin", 230, -250, 1f), new Label("Ring Trail", -170, 175, 0.8f),
            new Label("Sunset Shore", -330, 40, 0.9f), new Label("Sparkle Bay", 300, 260, 0.9f), new Label("South Gate", 0, 84, 0.6f), new Label("North Gate", 0, -84, 0.6f),
        };
        static readonly Dictionary<string, KeyValuePair<string, string>> Icons = new Dictionary<string, KeyValuePair<string, string>>
        {
            { "rifle", P("#8fe3c1", "R") }, { "shotgun", P("#ff8fb1", "S") }, { "sniper", P("#b9a5ff", "L") }, { "dmr", P("#d9c27a", "D") }, { "bolt", P("#8fb6d9", "K") },
            { "ammo", P("#7fc8a9", "A") }, { "heal", P("#ff9fb5", "+") }, { "scope2", P("#9fe8ff", "2") }, { "scope3", P("#8fe3c1", "3") }, { "scope4", P("#ffe38a", "4") }, { "scope6", P("#ff9fb5", "6") }, { "trap", P("#b98cff", "G") },
        };
        static KeyValuePair<string, string> P(string a, string b) { return new KeyValuePair<string, string>(a, b); }

        Texture2D baseTex, disc, white, arrowTex, radarTex; bool built;
        Color32[] radarBase, radarPx; float radarT; Vector3 radarMe; float radarYaw;   // corner radar: 512px canvas-order copy of the map + the 180px output
        const int RN = 512, RS = 180; const float RadarMetres = 170f;
        float cx, cz, zoom = 1;                 // view centre (JS metres) and screen pixels per metre
        bool hasWp; Vector2 wp;
        GameObject beacon; Transform ring;
        GUIStyle label, small, hint; Vector2 hover; bool hoverValid;
        // mouse drag
        bool dragging; Vector2 dragStart; float dragCx, dragCz, dragMoved; int dragButton;

        float FitZoom() { return Mathf.Min(Screen.width, Screen.height) / WS * 0.95f; }
        Vector2 ToScreen(float x, float z) { return new Vector2((x - cx) * zoom + Screen.width / 2f, (z - cz) * zoom + Screen.height / 2f); }
        Vector2 ToWorld(Vector2 s) { return new Vector2((s.x - Screen.width / 2f) / zoom + cx, (s.y - Screen.height / 2f) / zoom + cz); }

        // ------------------------------------------------------------------ texture (island, contours, roads, floor plan of the town)
        void Build()
        {
            built = true;
            const int N = 512; float cell = WS / N;
            var H = new float[(N + 2) * (N + 2)];
            for (int j = 0; j < N + 2; j++) for (int i = 0; i < N + 2; i++) H[j * (N + 2) + i] = WorldData.HeightAt((i - 1 + 0.5f) * cell - WS / 2, (j - 1 + 0.5f) * cell - WS / 2);
            System.Func<int, int, float> at = (i, j) => H[(j + 1) * (N + 2) + (i + 1)];
            var small = new Color[N * N];
            for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                float h = at(i, j); float r, g, b;
                if (h < WorldData.WaterLevel) { float depth = Mathf.Clamp01(-h / 14f); r = 150 - depth * 60; g = 215 - depth * 45; b = 232 - depth * 20; }
                else if (h < 1.6f) { r = 245; g = 226; b = 172; }
                else if (h > 68) { r = 246; g = 243; b = 238; }
                else if (h > 42) { r = 196; g = 182; b = 166; }
                else { r = 158 + h * 0.6f; g = 212 - h * 0.4f; b = 118 + h * 0.3f; }
                if (h >= WorldData.WaterLevel)
                {
                    float dx = at(i + 1, j) - at(i - 1, j), dz = at(i, j + 1) - at(i, j - 1);
                    float shade = Mathf.Clamp(1 - (dx + dz) * 0.06f, 0.72f, 1.18f); r *= shade; g *= shade; b *= shade;
                    if (Mathf.Floor(h / 10) != Mathf.Floor(at(i + 1, j) / 10) || Mathf.Floor(h / 10) != Mathf.Floor(at(i, j + 1) / 10)) { r *= 0.9f; g *= 0.9f; b *= 0.9f; }
                }
                small[j * N + i] = new Color(Mathf.Clamp01(r / 255f), Mathf.Clamp01(g / 255f), Mathf.Clamp01(b / 255f), 1);
            }
            // bilinear upscale to the full map texture (row 0 = south in texture space, so the image is stored flipped)
            var px = new Color32[RES * RES];
            for (int py = 0; py < RES; py++)
            {
                float fy = (py + 0.5f) * N / RES - 0.5f; int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, N - 1), y1 = Mathf.Min(N - 1, y0 + 1); float ty = Mathf.Clamp01(fy - y0);
                for (int pxx = 0; pxx < RES; pxx++)
                {
                    float fx = (pxx + 0.5f) * N / RES - 0.5f; int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, N - 1), x1 = Mathf.Min(N - 1, x0 + 1); float tx = Mathf.Clamp01(fx - x0);
                    Color c = Color.Lerp(Color.Lerp(small[y0 * N + x0], small[y0 * N + x1], tx), Color.Lerp(small[y1 * N + x0], small[y1 * N + x1], tx), ty);
                    px[(RES - 1 - py) * RES + pxx] = c;
                }
            }
            // town ground
            FillRect(px, -WorldData.TownR, WorldData.TownR, -WorldData.TownR, WorldData.TownR, new Color32(214, 196, 160, 255), 0.95f, 0);
            // ring trail (dashed, 235 m radius)
            { var tc = new Color32(200, 160, 110, 255); float circ = 2 * Mathf.PI * 235 * PPM; for (float s = 0; s < circ; s += 1f) { if (s % 22 > 14) continue; float a = s / circ * Mathf.PI * 2; Stamp(px, ToTex(Mathf.Cos(a) * 235), ToTex(Mathf.Sin(a) * 235), 3, tc, 0.9f); } }
            // the concert arena: turf and walls
            { var A = WorldData.Arena; FillRect(px, A.minX, A.maxX, A.minZ, A.maxZ, new Color32(110, 150, 80, 255), 0.9f, 0);
              var wallC = new Color32(156, 127, 98, 255); FillRect(px, A.minX, A.maxX, A.minZ, A.minZ + 1.2f, wallC, 1, 1.5f); FillRect(px, A.minX, A.minX + 1.2f, A.minZ, A.maxZ, wallC, 1, 1.5f);
              FillRect(px, A.maxX - 1.2f, A.maxX, A.minZ, -118, wallC, 1, 1.5f); FillRect(px, A.maxX - 1.2f, A.maxX, -110, A.maxZ, wallC, 1, 1.5f);
              FillRect(px, A.minX, -51, A.maxZ - 1.2f, A.maxZ, wallC, 1, 1.5f); FillRect(px, -39, A.maxX, A.maxZ - 1.2f, A.maxZ, wallC, 1, 1.5f); }
            var stone = new Color32(156, 127, 98, 255);
            foreach (var b in world.town.boxes) { if (b.bottom.HasValue) continue; FillRect(px, b.minX, b.maxX, b.minZ, b.maxZ, stone, 1, 1.5f); }
            var lane = new Color32(239, 226, 200, 255);
            foreach (var q in WorldData.Alleys) FillRect(px, q.minX, q.maxX, q.minZ, q.maxZ, q.roof ? new Color32(191, 174, 144, 255) : lane, 1, 0);
            foreach (var r in WorldData.Roads) { var q = WorldData.RoadRect(r); FillRect(px, q.minX, q.maxX, q.minZ, q.maxZ, lane, 1, 0); }
            foreach (var s in WorldData.Sites) FillRect(px, s.minX, s.maxX, s.minZ, s.maxZ, new Color32(232, 128, 92, 255), 0.45f, 0);
            var cover = new Color32(125, 106, 85, 255);
            foreach (var b in world.town.boxes) { if (b.bottom.HasValue || b.top > 6) continue; FillRect(px, b.minX, b.maxX, b.minZ, b.maxZ, cover, 1, 1.5f); }

            radarBase = new Color32[RN * RN];
            for (int ty = 0; ty < RN; ty++) for (int tx = 0; tx < RN; tx++)
            {
                int r = 0, g = 0, b = 0; for (int j = 0; j < 4; j++) { int row = (RES - 1 - (ty * 4 + j)) * RES + tx * 4; for (int i = 0; i < 4; i++) { var c = px[row + i]; r += c.r; g += c.g; b += c.b; } }
                radarBase[ty * RN + tx] = new Color32((byte)(r / 16), (byte)(g / 16), (byte)(b / 16), 255);
            }
            radarPx = new Color32[RS * RS]; radarTex = new Texture2D(RS, RS, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            baseTex = new Texture2D(RES, RES, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            baseTex.SetPixels32(px); baseTex.Apply(true, true);
        }

        static void FillRect(Color32[] px, float minX, float maxX, float minZ, float maxZ, Color32 c, float a, float minPx)
        {
            float x0 = ToTex(minX), x1 = ToTex(maxX), y0 = ToTex(minZ), y1 = ToTex(maxZ);
            if (x1 - x0 < minPx) x1 = x0 + minPx; if (y1 - y0 < minPx) y1 = y0 + minPx;
            int ix0 = Mathf.Clamp(Mathf.RoundToInt(x0), 0, RES), ix1 = Mathf.Clamp(Mathf.RoundToInt(x1), 0, RES), iy0 = Mathf.Clamp(Mathf.RoundToInt(y0), 0, RES), iy1 = Mathf.Clamp(Mathf.RoundToInt(y1), 0, RES);
            if (ix1 <= ix0) ix1 = Mathf.Min(RES, ix0 + 1); if (iy1 <= iy0) iy1 = Mathf.Min(RES, iy0 + 1);
            for (int y = iy0; y < iy1; y++) { int row = (RES - 1 - y) * RES; for (int x = ix0; x < ix1; x++) Blend(ref px[row + x], c, a); }
        }
        static void Stamp(Color32[] px, float tx, float ty, int r, Color32 c, float a)
        {
            for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++) { int X = Mathf.RoundToInt(tx) + x, Y = Mathf.RoundToInt(ty) + y; if (X < 0 || Y < 0 || X >= RES || Y >= RES || x * x + y * y > r * r) continue; Blend(ref px[(RES - 1 - Y) * RES + X], c, a); }
        }
        static void Blend(ref Color32 d, Color32 c, float a)
        { d.r = (byte)(d.r + (c.r - d.r) * a); d.g = (byte)(d.g + (c.g - d.g) * a); d.b = (byte)(d.b + (c.b - d.b) * a); }

        // ------------------------------------------------------------------ small drawing helpers
        void MakeShapes()
        {
            white = Texture2D.whiteTexture;
            disc = new Texture2D(64, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var d = new Color32[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { float r = Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f)); d[y * 64 + x] = new Color(1, 1, 1, Mathf.Clamp01(31f - r)); }
            disc.SetPixels32(d); disc.Apply();
            // arrow (tip up), polygon (0,-8) (6,6) (0,3) (-6,6) in a 24x24 unit box, 4x4 supersampled
            arrowTex = new Texture2D(96, 96, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var a = new Color32[96 * 96]; Vector2[] poly = { new Vector2(0, -8), new Vector2(6, 6), new Vector2(0, 3), new Vector2(-6, 6) };
            for (int y = 0; y < 96; y++) for (int x = 0; x < 96; x++)
            {
                float cov = 0; for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
                { float u = ((x + (sx + 0.5f) / 4f) / 96f - 0.5f) * 24f, v = (0.5f - (y + (sy + 0.5f) / 4f) / 96f) * 24f; if (Inside(poly, u, v)) cov += 1f / 16f; }
                a[y * 96 + x] = new Color(1, 1, 1, cov);
            }
            arrowTex.SetPixels32(a); arrowTex.Apply();
            label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };
            small = new GUIStyle(label); hint = new GUIStyle(label) { alignment = TextAnchor.MiddleLeft };
        }
        static bool Inside(Vector2[] p, float x, float y)
        { bool c = false; for (int i = 0, j = p.Length - 1; i < p.Length; j = i++) if (((p[i].y > y) != (p[j].y > y)) && (x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x)) c = !c; return c; }

        void Disc(Vector2 c, float r, Color col) { GUI.color = col; GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2, r * 2), disc); GUI.color = Color.white; }
        void Arrow(Vector2 c, float yawRad, Color col, float scale)
        {
            var m = GUI.matrix; GUIUtility.RotateAroundPivot(yawRad * Mathf.Rad2Deg, c);
            float s = 24f * scale;
            GUI.color = Color.white; float so = s * 1.3f; GUI.DrawTexture(new Rect(c.x - so / 2, c.y - so / 2, so, so), arrowTex);
            GUI.color = col; GUI.DrawTexture(new Rect(c.x - s / 2, c.y - s / 2, s, s), arrowTex);
            GUI.color = Color.white; GUI.matrix = m;
        }
        void Line(Vector2 a, Vector2 b, float w, Color col)
        {
            var m = GUI.matrix; Vector2 d = b - a; float len = d.magnitude; if (len < 0.5f) return;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.color = col; GUI.DrawTexture(new Rect(a.x, a.y - w / 2, len, w), white); GUI.color = Color.white; GUI.matrix = m;
        }
        void Pin(Vector2 p, float s)
        {
            Disc(p + new Vector2(0, -10 * s), 8.5f * s, Color.white); Disc(p + new Vector2(0, -10 * s), 6.5f * s, new Color(1, 0.435f, 0.71f));
            Line(p + new Vector2(0, -6 * s), p, 4f * s, new Color(1, 0.435f, 0.71f)); Disc(p + new Vector2(0, -10 * s), 2.4f * s, Color.white);
        }
        void Text(string t, Vector2 c, int size, Color col, Color outline, GUIStyle st = null)
        {
            st = st ?? label; st.fontSize = size; var r = new Rect(c.x - 200, c.y - size, 400, size * 2f);
            if (outline.a > 0) { st.normal.textColor = outline; foreach (var o in new[] { new Vector2(-1.5f, 0), new Vector2(1.5f, 0), new Vector2(0, -1.5f), new Vector2(0, 1.5f) }) GUI.Label(new Rect(r.x + o.x, r.y + o.y, r.width, r.height), t, st); }
            st.normal.textColor = col; GUI.Label(r, t, st);
        }

        void Start() { if (!built && world != null && world.town != null) Build(); }

        // ------------------------------------------------------------------ corner radar (port of drawRadar): you always face up, 170 m radius
        Color32 SampleBase(float x, float z)
        {
            float fx = (x / WS + 0.5f) * RN - 0.5f, fy = (z / WS + 0.5f) * RN - 0.5f;
            if (fx < -1 || fy < -1 || fx > RN || fy > RN) return new Color32(0x86, 0xc9, 0xdc, 255);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, RN - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, RN - 1), x1 = Mathf.Min(RN - 1, x0 + 1), y1 = Mathf.Min(RN - 1, y0 + 1);
            float tx = Mathf.Clamp01(fx - x0), ty = Mathf.Clamp01(fy - y0);
            Color a = Color.Lerp(radarBase[y0 * RN + x0], radarBase[y0 * RN + x1], tx), b = Color.Lerp(radarBase[y1 * RN + x0], radarBase[y1 * RN + x1], tx);
            return Color.Lerp(a, b, ty);
        }
        void RefreshRadar()
        {
            var me = MyPos(); radarMe = new Vector3(me.x, 0, -me.z); radarYaw = -MyYaw();   // JS yaw = -Unity yaw
            float cs = Mathf.Cos(radarYaw), sn = Mathf.Sin(radarYaw), mpp = RadarMetres * 2f / RS, Rr = RS / 2f;
            for (int y = 0; y < RS; y++)
            {
                float sy = (RS - 1 - y) + 0.5f - Rr;   // texture rows run bottom-up, screen y runs down
                for (int x = 0; x < RS; x++)
                {
                    float sx = x + 0.5f - Rr; float r2 = sx * sx + sy * sy;
                    if (r2 > Rr * Rr) { radarPx[y * RS + x] = new Color32(0, 0, 0, 0); continue; }
                    float wx = (sx * cs + sy * sn) * mpp + radarMe.x, wz = (-sx * sn + sy * cs) * mpp + radarMe.z;
                    var c = SampleBase(wx, wz); if (r2 > (Rr - 1.5f) * (Rr - 1.5f)) c.a = (byte)(255 * Mathf.Clamp01(Rr - Mathf.Sqrt(r2))); radarPx[y * RS + x] = c;
                }
            }
            radarTex.SetPixels32(radarPx); radarTex.Apply();
        }
        Vector2 RadarTr(float x, float z, Vector2 centre, float R)
        {
            float cs = Mathf.Cos(radarYaw), sn = Mathf.Sin(radarYaw), k = R / RadarMetres, dx = (x - radarMe.x) * k, dz = (z - radarMe.z) * k;
            return centre + new Vector2(dx * cs - dz * sn, dx * sn + dz * cs);
        }
        static Vector2 ClampToEdge(Vector2 p, Vector2 c, float R, float pad)
        { Vector2 d = p - c; float len = d.magnitude, max = R - pad; return len > max ? c + d / len * max : p; }

        void DrawRadar(int W)
        {
            if (radarTex == null) return;
            float size = Screen.height >= 600 ? 180f : 120f, R = size / 2f; var centre = new Vector2(W - 16 - 4 - R, 46 + 4 + R);
            Disc(centre + new Vector2(0, 5), R + 6, new Color(0.23f, 0.2f, 0.32f, 0.3f)); Disc(centre, R + 4, new Color(1f, 0.973f, 0.937f));
            GUI.DrawTexture(new Rect(centre.x - R, centre.y - R, size, size), radarTex);
            System.Func<Vector2, float, bool> inside = (p, pad) => (p - centre).sqrMagnitude < (R - pad) * (R - pad);
            if (garage != null) foreach (var v in garage.list)
            {
                if (v == null) continue; var q = v.transform.position; var p = RadarTr(q.x, -q.z, centre, R); if (!inside(p, 4)) continue;
                Color col = v.broken ? new Color(0.35f, 0.33f, 0.4f) : new Color(0.45f, 0.9f, 1f);
                Disc(p, 5f, new Color(0.23f, 0.2f, 0.32f)); Disc(p, 3.8f, col);
            }
            if (critters != null) foreach (var c in critters.list.Values) { if (c == null) continue; var q = c.transform.position; var p = RadarTr(q.x, -q.z, centre, R); if (inside(p, 4)) Disc(p, 2.5f, new Color(0.435f, 0.682f, 0.227f)); }
            if (hasWp) Pin(ClampToEdge(RadarTr(wp.x, wp.y, centre, R), centre, R, 8), 1f);
            var n = ClampToEdge(RadarTr(radarMe.x, radarMe.z - 10000f, centre, R), centre, R, 9);
            Disc(n, 8f, new Color(0.23f, 0.2f, 0.32f)); Text("N", n, 11, Color.white, Color.clear, small);
            Arrow(centre, 0f, new Color(1f, 0.62f, 0.71f), 0.9f);
            Text("M · map", new Vector2(centre.x, centre.y + R + 18), 12, new Color(1, 0.973f, 0.937f), new Color(0.23f, 0.2f, 0.32f, 0.7f));
            if (hasWp)
            {
                var me = MyPos(); float d = Mathf.Sqrt((wp.x - me.x) * (wp.x - me.x) + (wp.y + me.z) * (wp.y + me.z));
                GUI.color = new Color(1f, 0.435f, 0.71f, 0.7f); GUI.DrawTexture(new Rect(centre.x - R - 4, centre.y + R + 34, size + 8, 24), white); GUI.color = Color.white;
                Text("Waypoint  " + Mathf.RoundToInt(d) + " m", new Vector2(centre.x, centre.y + R + 46), 14, Color.white, Color.clear);
            }
        }

        // ------------------------------------------------------------------ waypoint
        public void SetWaypoint(float x, float z)
        {
            hasWp = true; wp = new Vector2(x, z);
            if (beacon == null) MakeBeacon();
            beacon.SetActive(true);
            beacon.transform.position = new Vector3(x, Mathf.Max(WorldData.HeightAt(x, z), WorldData.WaterLevel), -z);
        }
        public void ClearWaypoint() { hasWp = false; if (beacon != null) beacon.SetActive(false); }
        void MakeBeacon()
        {
            beacon = new GameObject("WaypointBeacon");
            var beamMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.56f, 0.82f, 0.35f) };
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(beam.GetComponent<Collider>()); beam.transform.SetParent(beacon.transform, false);
            beam.transform.localScale = new Vector3(2.2f, 130f, 2.2f); beam.transform.localPosition = new Vector3(0, 130f, 0);
            var br = beam.GetComponent<MeshRenderer>(); br.sharedMaterial = beamMat; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var rg = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(rg.GetComponent<Collider>()); rg.transform.SetParent(beacon.transform, false);
            rg.transform.localScale = new Vector3(6f, 0.05f, 6f); rg.transform.localPosition = new Vector3(0, 0.4f, 0); ring = rg.transform;
            var rr = rg.GetComponent<MeshRenderer>(); rr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.82f, 0.93f, 0.85f) }; rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Vector3 MyPos() { return player.vehicle != null ? player.vehicle.transform.position : player.transform.position; }
        float MyYaw() { return player.vehicle != null ? player.vehicle.yaw : player.Yaw; }

        // ------------------------------------------------------------------ open / close + input
        public void Open()
        {
            if (!built) Build();
            open = true; player.mapOpen = true; player.SetCursorFree(true); dragging = false;
            var p = MyPos(); cx = p.x; cz = -p.z; zoom = FitZoom() * 2.2f;
        }
        public void Close()
        {
            open = false; player.mapOpen = false; player.mapClosedFrame = Time.frameCount; player.SetCursorFree(false); dragging = false;
        }

        void Update()
        {
            if (player == null) return;
            if (HvInput.Hit("M")) { if (open) Close(); else if (!player.dead) Open(); }
            if (hasWp)
            {
                if (ring != null) ring.Rotate(0, 60f * Time.deltaTime, 0);
                var p = MyPos(); if (Mathf.Sqrt((wp.x - p.x) * (wp.x - p.x) + (wp.y + p.z) * (wp.y + p.z)) < 10f) { ClearWaypoint(); player.Toast("You arrived at your waypoint"); }
            }
            if (built && !open) { radarT -= Time.unscaledDeltaTime; if (radarT <= 0) { radarT = 1f / 30f; RefreshRadar(); } }
            if (!open) return;
            var kb = Keyboard.current; var ms = Mouse.current; if (ms == null) return;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            Vector2 mp = new Vector2(ms.position.x.ReadValue(), Screen.height - ms.position.y.ReadValue());
            hover = ToWorld(mp); hoverValid = true;
            if (ms.leftButton.wasPressedThisFrame || ms.rightButton.wasPressedThisFrame)
            { dragging = true; dragStart = mp; dragCx = cx; dragCz = cz; dragMoved = 0; dragButton = ms.rightButton.wasPressedThisFrame ? 1 : 0; }
            if (dragging)
            {
                Vector2 d = mp - dragStart; dragMoved = Mathf.Max(dragMoved, d.magnitude); cx = dragCx - d.x / zoom; cz = dragCz - d.y / zoom;
                if (ms.leftButton.wasReleasedThisFrame || ms.rightButton.wasReleasedThisFrame)
                {
                    if (dragMoved < 5f) { var w = ToWorld(mp); if (dragButton == 1) ClearWaypoint(); else if (w.magnitude < 440f) SetWaypoint(w.x, w.y); }
                    dragging = false;
                }
            }
            float wheel = ms.scroll.ReadValue().y;
            if (HvInput.testWheel != 0) { wheel = -HvInput.testWheel; HvInput.testWheel = 0; }
            if (Mathf.Abs(wheel) > 0.5f)
            {
                var before = ToWorld(mp); float fit = FitZoom();
                zoom = Mathf.Clamp(zoom * (wheel > 0 ? 1.18f : 1f / 1.18f), fit * 0.8f, 12f);
                cx = before.x - (mp.x - Screen.width / 2f) / zoom; cz = before.y - (mp.y - Screen.height / 2f) / zoom;
            }
        }

        // ------------------------------------------------------------------ drawing
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || player == null) return;
            if (disc == null) MakeShapes();
            GUI.depth = -1000;   // the open map covers the other HUD text
            int W = Screen.width, Hh = Screen.height;
            if (!open) { if (built) DrawRadar(W); return; }
            GUI.color = new Color(0.498f, 0.769f, 0.847f); GUI.DrawTexture(new Rect(0, 0, W, Hh), white); GUI.color = Color.white;
            var tl = ToScreen(-WS / 2, -WS / 2); GUI.DrawTexture(new Rect(tl.x, tl.y, WS * zoom, WS * zoom), baseTex, ScaleMode.StretchToFill);
            // grid
            var gc = new Color(1, 1, 1, 0.18f);
            for (int g = -400; g <= 400; g += 100)
            {
                var a = ToScreen(g, -450); GUI.color = gc; GUI.DrawTexture(new Rect(a.x - 0.5f, a.y, 1, WS * zoom), white);
                var b = ToScreen(-450, g); GUI.DrawTexture(new Rect(b.x, b.y - 0.5f, WS * zoom, 1), white); GUI.color = Color.white;
            }
            float fit = FitZoom(), zr = Mathf.Sqrt(zoom / fit);
            // bomb-site letters scale with the map (14 m tall, like the texture text in the browser game)
            foreach (var s in WorldData.Sites) Text(s.name, ToScreen((s.minX + s.maxX) / 2, (s.minZ + s.maxZ) / 2), Mathf.Clamp(Mathf.RoundToInt(14f * zoom), 8, 160), new Color(0.745f, 0.275f, 0.157f, 0.9f), Color.clear);
            foreach (var L in Labels) Text(L.name, ToScreen(L.x, L.z), Mathf.RoundToInt(Mathf.Clamp(14 * L.size * zr * 0.7f, 11, 30)), new Color(1, 0.973f, 0.937f), new Color(0.23f, 0.2f, 0.32f, 0.55f));
            float scale = Mathf.Clamp(zoom / fit * 0.5f, 0.8f, 1.6f);
            // loot
            if (pickups != null) foreach (var it in pickups.items)
            {
                if (!it.active || it.group == null) continue; var pp = it.group.transform.position; var sp = ToScreen(pp.x, -pp.z);
                if (sp.x < -10 || sp.y < -10 || sp.x > W + 10 || sp.y > Hh + 10) continue;
                KeyValuePair<string, string> ic; if (!Icons.TryGetValue(it.type, out ic)) continue; Color col; ColorUtility.TryParseHtmlString(ic.Key, out col);
                Disc(sp, 7f * scale, Color.white); Disc(sp, 5.6f * scale, col); Text(ic.Value, sp, Mathf.RoundToInt(9 * scale + 2), new Color(0.23f, 0.2f, 0.32f), Color.clear, small);
            }
            if (critters != null) foreach (var c in critters.list.Values) { if (c == null) continue; var p = c.transform.position; Disc(ToScreen(p.x, -p.z), 3.2f * scale, new Color(0.435f, 0.682f, 0.227f)); }
            if (garage != null) foreach (var v in garage.list)
            {
                if (v == null) continue; var p = v.transform.position; var sp = ToScreen(p.x, -p.z); Color col = v.broken ? new Color(0.35f, 0.33f, 0.4f) : new Color(0.45f, 0.9f, 1f);
                var m = GUI.matrix; GUIUtility.RotateAroundPivot(v.yaw * Mathf.Rad2Deg, sp);
                GUI.color = new Color(0.23f, 0.2f, 0.32f); GUI.DrawTexture(new Rect(sp.x - 6 * scale - 1, sp.y - 9 * scale - 1, 12 * scale + 2, 18 * scale + 2), disc);
                GUI.color = col; GUI.DrawTexture(new Rect(sp.x - 6 * scale, sp.y - 9 * scale, 12 * scale, 18 * scale), disc); GUI.color = Color.white; GUI.matrix = m;
            }
            // route + waypoint
            var me = MyPos(); var mePt = ToScreen(me.x, -me.z);
            if (hasWp)
            {
                var wpt = ToScreen(wp.x, wp.y); Vector2 dir = wpt - mePt; float len = dir.magnitude;
                for (float t = 0; t < len; t += 14f) Line(mePt + dir * (t / len), mePt + dir * (Mathf.Min(len, t + 8f) / len), 3f, new Color(1, 0.435f, 0.71f));
                Pin(wpt, 1.6f); float d = Mathf.Sqrt((wp.x - me.x) * (wp.x - me.x) + (wp.y + me.z) * (wp.y + me.z));
                Text(Mathf.RoundToInt(d) + " m", wpt + new Vector2(0, 14), 13, Color.white, new Color(0.23f, 0.2f, 0.32f, 0.6f));
            }
            Arrow(mePt, MyYaw(), new Color(1f, 0.62f, 0.71f), 1.5f);
            Text("You", mePt + new Vector2(0, -22), 12, Color.white, new Color(0.23f, 0.2f, 0.32f, 0.6f));
            // title, coordinates and help
            GUI.color = new Color(0.23f, 0.2f, 0.32f, 0.75f); GUI.DrawTexture(new Rect(0, 0, W, 38), white); GUI.DrawTexture(new Rect(0, Hh - 34, W, 34), white); GUI.color = Color.white;
            Text("Hearthvale Island", new Vector2(W / 2f, 19), 18, Color.white, Color.clear);
            string coords = hoverValid ? Mathf.RoundToInt(hover.x) + ", " + Mathf.RoundToInt(hover.y) : "";
            Text("Left click: set waypoint    Right click: clear    Drag: pan    Wheel: zoom    M / Esc: close", new Vector2(W / 2f, Hh - 17), 14, new Color(1, 0.973f, 0.937f), Color.clear);
            Text(coords, new Vector2(W - 70, Hh - 17), 14, new Color(1, 0.973f, 0.937f), Color.clear);
        }

        void OnDestroy() { if (beacon != null) Destroy(beacon); }
    }
}
