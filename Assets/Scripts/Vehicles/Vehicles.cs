using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hearthvale
{
    /// <summary>
    /// Alien hover-craft (port of vehicles.js): no doors, no windows, a sculpted open-cockpit shell whose side panels swing up like wings.
    /// Three styles (Bubble Pod, Sky Manta, Moon Beetle), hover over land and water, drift, crash into things, lose parts and finally
    /// break down (then get repaired after a while). Authored in the JS right-handed space (nose toward -z) and mirrored by Geo.ToUnityMesh.
    /// </summary>
    public static class CraftModel
    {
        public sealed class Design { public float len, wid, hgt, taper, nose, open, seatZ, seatY; }
        public static readonly Dictionary<string, Design> Designs = new Dictionary<string, Design>
        {
            { "pod",    new Design { len = 2.15f, wid = 1.15f, hgt = 0.62f, taper = 0.45f, nose = 0.35f, open = 0.3f,  seatZ = 0.3f,  seatY = -0.12f } },
            { "manta",  new Design { len = 2.55f, wid = 1.0f,  hgt = 0.48f, taper = 0.62f, nose = 0.25f, open = 0.34f, seatZ = 0.45f, seatY = -0.08f } },
            { "beetle", new Design { len = 2.35f, wid = 1.32f, hgt = 0.82f, taper = 0.28f, nose = 0.2f,  open = 0.32f, seatZ = 0.2f,  seatY = -0.22f } },
        };
        public static readonly string[] StyleList = { "pod", "manta", "beetle" };
        public static readonly string[] Paints = { "#d94f7a", "#3f8fd9", "#7b5ad6", "#2fae86", "#e08a3a", "#c24fc9", "#21a3a8" };
        public static readonly string[] Glows = { "#7ff9ff", "#ff8cf0", "#b6ff8f", "#ffe27a", "#9fb0ff", "#ff9f8a", "#8affd7" };

        // ------------------------------------------------------------ geometry helpers (JS space, CCW winding)
        static Vector3 Shape(Design d, float ux, float uy, float uz, float scale = 1f)
        {
            float front = Mathf.Max(0, -uz), back = Mathf.Max(0, uz);
            float x = ux * d.wid * (1 - d.taper * Mathf.Pow(front, 1.6f));
            float y = uy * d.hgt; if (y < 0) y *= 0.55f;
            y -= d.nose * Mathf.Pow(front, 2.2f) * d.hgt;
            y += Mathf.Pow(back, 3) * 0.18f * d.hgt;
            x *= 1 + 0.08f * (1 - Mathf.Abs(uy));
            return new Vector3(x * scale, y * scale, uz * d.len * scale);
        }

        /// <summary>three.js SphereGeometry section; every point is passed through map(ux,uy,uz).</summary>
        static Geo SphereSection(int nu, int nv, float phiStart, float phiLen, float thStart, float thLen, Func<float, float, float, Vector3> map)
        {
            var g = new Geo(); float thEnd = thStart + thLen;
            for (int iy = 0; iy <= nv; iy++) for (int ix = 0; ix <= nu; ix++)
            {
                float th = thStart + (float)iy / nv * thLen, ph = phiStart + (float)ix / nu * phiLen;
                g.v.Add(map(-Mathf.Cos(ph) * Mathf.Sin(th), Mathf.Cos(th), Mathf.Sin(ph) * Mathf.Sin(th))); g.n.Add(Vector3.up); g.uv.Add(new Vector2((float)ix / nu, (float)iy / nv));
            }
            int row = nu + 1;
            for (int iy = 0; iy < nv; iy++) for (int ix = 0; ix < nu; ix++)
            {
                int a = iy * row + ix + 1, b = iy * row + ix, c = (iy + 1) * row + ix, d = (iy + 1) * row + ix + 1;
                if (iy != 0 || thStart > 0) { g.t.Add(a); g.t.Add(b); g.t.Add(d); }
                if (iy != nv - 1 || thEnd < Mathf.PI) { g.t.Add(b); g.t.Add(c); g.t.Add(d); }
            }
            return Smooth(g);
        }

        /// <summary>Smooth normals from the faces, welding vertices that share a position.</summary>
        public static Geo Smooth(Geo g)
        {
            var acc = new Dictionary<Vector3Int, Vector3>(); Func<Vector3, Vector3Int> key = p => new Vector3Int(Mathf.RoundToInt(p.x * 2000f), Mathf.RoundToInt(p.y * 2000f), Mathf.RoundToInt(p.z * 2000f));
            for (int i = 0; i < g.t.Count; i += 3)
            {
                Vector3 a = g.v[g.t[i]], b = g.v[g.t[i + 1]], c = g.v[g.t[i + 2]]; Vector3 fn = Vector3.Cross(b - a, c - a);
                for (int k = 0; k < 3; k++) { var kk = key(g.v[g.t[i + k]]); Vector3 o; acc.TryGetValue(kk, out o); acc[kk] = o + fn; }
            }
            for (int i = 0; i < g.v.Count; i++) { Vector3 o; if (acc.TryGetValue(key(g.v[i]), out o) && o.sqrMagnitude > 1e-14f) g.n[i] = o.normalized; }
            return g;
        }

        /// <summary>Flip winding (and normals) if more than half of the triangles face the inside of the solid.</summary>
        static Geo Orient(Geo g, bool outward = true)
        {
            Vector3 c = Vector3.zero; foreach (var p in g.v) c += p; c /= Mathf.Max(1, g.v.Count); float score = 0;
            for (int i = 0; i < g.t.Count; i += 3) { Vector3 a = g.v[g.t[i]], b = g.v[g.t[i + 1]], d = g.v[g.t[i + 2]]; score += Vector3.Dot(Vector3.Cross(b - a, d - a), (a + b + d) / 3f - c); }
            if ((score < 0) == outward) Flip(g);
            return Smooth(g);
        }
        static void Flip(Geo g) { for (int i = 0; i < g.t.Count; i += 3) { int tmp = g.t[i + 1]; g.t[i + 1] = g.t[i + 2]; g.t[i + 2] = tmp; } for (int i = 0; i < g.n.Count; i++) g.n[i] = -g.n[i]; }

        static Geo Lathe(Vector2[] prof, int seg)
        {
            var g = new Geo();
            for (int i = 0; i <= seg; i++) { float a = (float)i / seg * Mathf.PI * 2; for (int j = 0; j < prof.Length; j++) { g.v.Add(new Vector3(Mathf.Sin(a) * prof[j].x, prof[j].y, Mathf.Cos(a) * prof[j].x)); g.n.Add(Vector3.up); g.uv.Add(Vector2.zero); } }
            int n = prof.Length;
            for (int i = 0; i < seg; i++) for (int j = 0; j < n - 1; j++) { int a = i * n + j, b = a + n, c = b + 1, d = a + 1; g.t.AddRange(new[] { a, b, d, b, c, d }); }
            return Orient(g);
        }

        /// <summary>Flat polygon (XY) extruded along z (depth), centred on z = 0. Fan-triangulated from the centroid (shapes are star-shaped).</summary>
        static Geo Extrude(List<Vector2> poly, float depth)
        {
            var g = new Geo(); Vector2 c = Vector2.zero; foreach (var p in poly) c += p; c /= poly.Count; int n = poly.Count; float h = depth / 2;
            for (int layer = 0; layer < 2; layer++) { float z = layer == 0 ? -h : h; g.v.Add(new Vector3(c.x, c.y, z)); foreach (var p in poly) g.v.Add(new Vector3(p.x, p.y, z)); }
            for (int layer = 0; layer < 2; layer++) { int o = layer * (n + 1); for (int i = 0; i < n; i++) { int a = o + 1 + i, b = o + 1 + (i + 1) % n; if (layer == 0) g.t.AddRange(new[] { o, b, a }); else g.t.AddRange(new[] { o, a, b }); } }
            int s0 = g.v.Count; for (int i = 0; i < n; i++) { g.v.Add(new Vector3(poly[i].x, poly[i].y, -h)); g.v.Add(new Vector3(poly[i].x, poly[i].y, h)); }
            for (int i = 0; i < n; i++) { int a = s0 + i * 2, b = s0 + i * 2 + 1, c2 = s0 + ((i + 1) % n) * 2 + 1, d = s0 + ((i + 1) % n) * 2; g.t.AddRange(new[] { a, d, c2, a, c2, b }); }
            while (g.n.Count < g.v.Count) { g.n.Add(Vector3.up); g.uv.Add(Vector2.zero); }
            return Orient(g);
        }
        static List<Vector2> Quad(Vector2 a, Vector2 c, Vector2 b, int n = 8) { var l = new List<Vector2>(); for (int i = 1; i <= n; i++) { float t = (float)i / n; l.Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b); } return l; }

        static Geo Sphere(float r, int seg = 10) { return SphereSection(seg, seg - 2, 0, Mathf.PI * 2, 0, Mathf.PI, (x, y, z) => new Vector3(x, y, z) * r); }
        static Geo Annulus(float rin, float rout, int seg)
        {
            var g = new Geo();
            for (int i = 0; i <= seg; i++) { float a = (float)i / seg * Mathf.PI * 2; Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0); g.v.Add(d * rin); g.v.Add(d * rout); g.n.Add(Vector3.forward); g.n.Add(Vector3.forward); g.uv.Add(Vector2.zero); g.uv.Add(Vector2.zero); }
            for (int i = 0; i < seg; i++) { int a = i * 2, b = a + 1, c = a + 3, d = a + 2; g.t.AddRange(new[] { a, b, c, a, c, d }); }
            return g;
        }
        public static Matrix4x4 X(float x, float y, float z, float rx = 0, float ry = 0, float rz = 0, float sx = 1, float sy = 1, float sz = 1)
        { return M.T(x, y, z) * M.RotX(rx) * M.RotY(ry) * M.RotZ(rz) * Matrix4x4.Scale(new Vector3(sx, sy, sz)); }
        static Geo Mapped(Geo g, Func<Vector3, Vector3> f) { for (int i = 0; i < g.v.Count; i++) g.v[i] = f(g.v[i]); return Smooth(g); }

        // ------------------------------------------------------------ materials
        static Dictionary<string, Material> mats = new Dictionary<string, Material>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { mats = new Dictionary<string, Material>(); }
        static Material Shared(string key, Func<Material> make) { Material m; if (mats.TryGetValue(key, out m) && m != null) return m; m = make(); mats[key] = m; return m; }
        public static Material Unlit(Color c) { var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", c); return m; }
        static Material Lit(string n, string hex, float smooth, float metal, bool dbl = false) { return HvMaterials.Make(n, null, null, smooth, metal, hex, false, false, dbl); }

        sealed class Group
        {
            public readonly Dictionary<Material, Geo> geo = new Dictionary<Material, Geo>();
            public void Add(Material m, Geo g, Matrix4x4 mx) { Geo a; if (!geo.TryGetValue(m, out a)) geo[m] = a = new Geo(); a.Append(g, mx); }
            /// <summary>Creates a GameObject (children per material). centred: its origin sits at the middle of the parts, so debris tumbles around itself.</summary>
            public GameObject Make(string name, Transform parent, bool centred)
            {
                Vector3 c = Vector3.zero; int cnt = 0; foreach (var kv in geo) foreach (var p in kv.Value.v) { c += p; cnt++; } if (cnt > 0) c /= cnt; if (!centred) c = Vector3.zero;
                var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = new Vector3(c.x, c.y, -c.z);
                foreach (var kv in geo)
                {
                    if (centred) for (int i = 0; i < kv.Value.v.Count; i++) kv.Value.v[i] -= c;
                    var ch = new GameObject(kv.Key.name); ch.transform.SetParent(go.transform, false); ch.AddComponent<MeshFilter>().sharedMesh = kv.Value.ToUnityMesh(name);
                    var r = ch.AddComponent<MeshRenderer>(); r.sharedMaterial = kv.Key; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                return go;
            }
        }

        public sealed class Parts
        {
            public GameObject root; public Material paint, glow; public Color glowColor, paintColor; public List<GameObject> detachable = new List<GameObject>();
            public List<KeyValuePair<Transform, int>> hatches = new List<KeyValuePair<Transform, int>>(); public Transform seat, headAnchor; public Vector3 headPos;
        }

        public static Parts Build(string style, string paintHex, string glowHex)
        {
            var d = Designs[style]; var P = new Parts(); var root = new GameObject("HoverCraft_" + style); P.root = root;
            P.paintColor = M.Hex(paintHex); P.glowColor = M.Hex(glowHex);
            var paint = new Material(HvMaterials.Make("craftPaint", null, null, 0.88f, 0.5f, paintHex, false)); P.paint = paint;
            var glow = Unlit(P.glowColor * 2f); glow.name = "craftGlow"; P.glow = glow;
            var pearl = Shared("pearl", () => Lit("pearl", "#dfe3ea", 0.86f, 1f)); var dark = Shared("dark", () => Lit("craftDark", "#25272e", 0.65f, 0.75f));
            var interior = Shared("interior", () => Lit("craftInterior", "#2b2832", 0.2f, 0f)); var leather = Shared("leather", () => Lit("craftLeather", "#3a2f35", 0.55f, 0f));
            var holoM = new Material(Shader.Find("Sprites/Default")) { name = "craftHolo" }; holoM.color = new Color(P.glowColor.r, P.glowColor.g, P.glowColor.b, 0.55f);
            var headMat = Shared("head", () => Unlit(new Color(3.2f, 3.4f, 3.6f))); var tailMat = Shared("tail", () => Unlit(new Color(3f, 0.2f, 0.2f)));
            float th = d.open * Mathf.PI, rimR = Mathf.Sin(th), rimY = Mathf.Cos(th);
            Func<float, float, float, Vector3> shape = (a, b, c) => Shape(d, a, b, c); Func<float, float, float, Vector3> shape96 = (a, b, c) => Shape(d, a, b, c, 0.96f);
            Func<float, float, float, Vector3> surf = shape;

            var body = new Group(); var I = Matrix4x4.identity;
            body.Add(paint, SphereSection(72, 40, 0, Mathf.PI * 2, th, Mathf.PI - th, shape), I);
            var liner = SphereSection(48, 24, 0, Mathf.PI * 2, th, Mathf.PI - th, shape96); Flip(liner); body.Add(interior, liner, I);
            var rim = Mapped(Geo.Torus(1f, 0.035f, 96, 10), p => Shape(d, p.x * rimR, rimY + p.y * 0.9f, p.z * rimR));
            body.Add(pearl, rim, I);
            var seam = Mapped(Geo.Torus(1f, 0.018f, 96, 6), p => { var s = Shape(d, p.x * 1.003f, p.y, p.z * 1.003f); return new Vector3(s.x, s.y - 0.02f, s.z); });
            body.Add(glow, seam, I);
            var floorMat = Shared("floor", () => Lit("craftFloor", "#2b2832", 0.2f, 0f));
            body.Add(floorMat, Geo.Box(d.wid * 1.2f, 0.04f, d.len * 1.1f), CraftModel.X(0, -d.hgt * 0.42f, d.seatZ * 0.5f));

            // seat, console, yoke, holographic display
            float seatY = d.seatY, consoleZ = d.seatZ - 0.95f;
            body.Add(leather, Geo.RoundedBox(0.6f, 0.14f, 0.58f, 0.05f), X(0, seatY, d.seatZ));
            body.Add(leather, Geo.RoundedBox(0.58f, 0.75f, 0.14f, 0.05f), X(0, seatY + 0.42f, d.seatZ + 0.32f, 0.18f));
            body.Add(leather, Geo.RoundedBox(0.34f, 0.2f, 0.12f, 0.04f), X(0, seatY + 0.9f, d.seatZ + 0.4f));
            body.Add(dark, Geo.RoundedBox(0.9f, 0.22f, 0.45f, 0.06f), X(0, seatY + 0.32f, consoleZ, -0.35f));
            var holo = new Group(); holo.Add(holoM, Geo.Plane(0.62f, 0.26f), X(0, seatY + 0.56f, consoleZ - 0.08f, -0.55f)); holo.Add(holoM, Geo.Plane(0.24f, 0.12f), X(0.32f, seatY + 0.5f, consoleZ + 0.02f, -0.7f, -0.4f));
            body.Add(dark, Geo.Torus(0.16f, 0.022f, 24, 8), X(0, seatY + 0.5f, consoleZ + 0.32f, Mathf.PI / 2));
            body.Add(pearl, Geo.Cylinder(0.02f, 0.02f, 0.28f, 8), X(0, seatY + 0.42f, consoleZ + 0.3f, 0.6f));
            for (int i = 0; i < 5; i++) body.Add(glow, Sphere(0.018f, 6), X(-0.3f + i * 0.07f, seatY + 0.42f, consoleZ + 0.1f));

            // lights: headlights at the nose, taillights at the back
            Vector3 headPos = Vector3.zero;
            foreach (int s in new[] { -1, 1 })
            {
                var hp = surf(s * 0.38f, 0.05f, -0.93f); if (s == -1) headPos = hp;
                body.Add(headMat, Geo.RoundedBox(0.32f, 0.1f, 0.1f, 0.045f), X(hp.x, hp.y, hp.z, 0, s * 0.5f));
                var tp = surf(s * 0.5f, 0.15f, 0.95f); body.Add(tailMat, Geo.RoundedBox(0.37f, 0.07f, 0.07f, 0.03f), X(tp.x, tp.y, tp.z));
            }
            // engines: lathe-turned nacelles with glowing exhausts
            var prof = new[] { new Vector2(0, -0.55f), new Vector2(0.14f, -0.52f), new Vector2(0.22f, -0.3f), new Vector2(0.24f, 0.1f), new Vector2(0.2f, 0.45f), new Vector2(0.16f, 0.55f), new Vector2(0.13f, 0.55f) };
            Func<float, float, float, float, GameObject> nacelle = (x, y, z, sc) =>
            {
                var gp = new Group(); var m0 = X(x, y, z, 0, 0, 0, sc, sc, sc);
                gp.Add(dark, Lathe(prof, 28), m0 * M.RotX(Mathf.PI / 2));
                foreach (float rz in new[] { -0.25f, 0.15f }) gp.Add(pearl, Geo.Torus(0.235f, 0.02f, 32, 8), m0 * M.T(0, 0, rz) * M.RotX(Mathf.PI / 2));
                gp.Add(glow, Geo.Cylinder(0.13f, 0.13f, 0.01f, 24), m0 * M.T(0, 0, 0.56f) * M.RotX(Mathf.PI / 2));
                gp.Add(holoM, Geo.Cylinder(0.0f, 0.12f, 0.5f, 20), m0 * M.T(0, 0, 0.82f) * M.RotX(-Mathf.PI / 2));
                return gp.Make("nacelle", root.transform, true);
            };
            // fins (swept extrusions) and wings
            var fp = new List<Vector2> { new Vector2(0, 0), new Vector2(0.95f, 0) }; fp.AddRange(Quad(new Vector2(0.95f, 0), new Vector2(0.7f, 0.25f), new Vector2(0.85f, 0.7f))); fp.AddRange(Quad(new Vector2(0.85f, 0.7f), new Vector2(0.35f, 0.5f), new Vector2(0, 0)));
            var finG = Extrude(fp, 0.06f); finG = Mapped(finG, p => new Vector3(p.z, p.y, -p.x)); finG = Orient(finG);   // rotateY(90deg): shape x runs toward -z
            Func<float, float, float, float, float, GameObject> fin = (x, y, z, rz, sc) => { var gp = new Group(); gp.Add(paint, finG, X(x, y, z, 0, 0, rz, sc, sc, sc)); return gp.Make("fin", root.transform, true); };

            var det = P.detachable;
            if (style == "pod")
            {
                foreach (int s in new[] { -1, 1 }) det.Add(nacelle(s * (d.wid + 0.08f), -0.02f, d.len * 0.45f, 0.95f));
                foreach (int s in new[] { -1, 1 }) det.Add(fin(s * 0.35f, d.hgt * 0.4f, d.len * 0.62f, s * 0.35f, 0.8f));
            }
            else if (style == "manta")
            {
                var wp = new List<Vector2> { new Vector2(0, -0.9f), new Vector2(1.6f, 0.3f) }; wp.AddRange(Quad(new Vector2(1.6f, 0.3f), new Vector2(1.7f, 0.75f), new Vector2(1.3f, 0.8f))); wp.Add(new Vector2(0, 0.9f));
                var wingG = Mapped(Extrude(wp, 0.1f), p => new Vector3(p.x, -p.z, p.y));   // rotateX(90deg): shape y runs along z
                wingG = Orient(wingG);
                foreach (int s in new[] { -1, 1 })
                {
                    var gp = new Group(); var m0 = X(s * d.wid * 0.85f, -0.02f, d.len * 0.25f, 0, 0, s * -0.08f, s, 1, 1);
                    var wg = new Geo().Append(wingG, Matrix4x4.identity); if (s < 0) Flip(wg);
                    gp.Add(paint, wg, m0); gp.Add(glow, Geo.Box(1.4f, 0.02f, 0.04f), m0 * M.T(0.75f, 0.09f, 0.85f));
                    det.Add(gp.Make("wing", root.transform, true));
                    nacelle(s * 1.15f, -0.02f, d.len * 0.72f, 1.1f);
                }
                det.Add(fin(0, d.hgt * 0.5f, d.len * 0.7f, 0, 1.1f));
            }
            else
            {   // roll cage over the open cabin + four anti-grav pods
                var a0 = surf(-rimR * 0.95f, rimY, 0.2f); var a3 = surf(rimR * 0.95f, rimY, 0.2f);
                var cage = new Geo(); Vector3 p1 = new Vector3(-0.55f, d.hgt + 0.85f, 0.35f), p2 = new Vector3(0.55f, d.hgt + 0.85f, 0.35f);
                for (int i = 0; i < 24; i++) { float t0 = (float)i / 24, t1 = (float)(i + 1) / 24; Vector3 q0 = Catmull(a0, p1, p2, a3, t0), q1 = Catmull(a0, p1, p2, a3, t1); Vector3 mid = (q0 + q1) / 2; Vector3 dir = (q1 - q0); float len = dir.magnitude; Quaternion qq = Quaternion.FromToRotation(Vector3.up, dir.normalized); cage.Append(Geo.Cylinder(0.045f, 0.045f, len * 1.02f, 8), Matrix4x4.TRS(mid, qq, Vector3.one)); }
                var gp = new Group(); gp.Add(pearl, cage, I); det.Add(gp.Make("cage", root.transform, true));
                var gb = new Group(); gb.Add(glow, Sphere(0.09f, 10), X(0, d.hgt + 0.9f, 0.35f)); det.Add(gb.Make("beacon", root.transform, true));
                foreach (var xz in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                {
                    var pg = new Group(); var m0 = M.T(xz.x * d.wid * 0.85f, -d.hgt * 0.35f, xz.y * d.len * 0.55f);
                    pg.Add(dark, Geo.Cylinder(0.32f, 0.36f, 0.22f, 24), m0); pg.Add(glow, Geo.Torus(0.3f, 0.04f, 28, 8), m0 * M.T(0, -0.12f, 0));
                    if (xz.y > 0) det.Add(pg.Make("pad", root.transform, true)); else body.Add(dark, Geo.Cylinder(0.32f, 0.36f, 0.22f, 24), m0);
                    if (xz.y <= 0) body.Add(glow, Geo.Torus(0.3f, 0.04f, 28, 8), m0 * M.T(0, -0.12f, 0));
                }
                foreach (int s in new[] { -1, 1 }) det.Add(nacelle(s * 0.45f, d.hgt * 0.1f, d.len * 0.98f, 0.7f));
            }
            // anti-grav underglow
            body.Add(glow, Annulus(0.35f, d.wid * 0.95f, 48), X(0, -d.hgt * 0.56f, 0, Mathf.PI / 2, 0, 0, 1, d.len / d.wid * 0.85f, 1));
            body.Make("body", root.transform, false); holo.Make("holo", root.transform, false);

            // gull-wing side panels (no doors, they just lift)
            foreach (int side in new[] { -1, 1 })
            {
                float phiStart = side < 0 ? -0.26f * Mathf.PI : 0.74f * Mathf.PI;
                var hinge = surf(side * rimR, rimY, 0);
                var pg = SphereSection(40, 12, phiStart, 0.52f * Mathf.PI, th, 0.22f * Mathf.PI, (a, b, c) => Shape(d, a * 1.015f, b * 1.015f, c * 1.015f) - hinge);
                var hg = new Group(); hg.Add(paint, pg, I);
                var low = surf(side * Mathf.Sin(th + 0.22f * Mathf.PI), Mathf.Cos(th + 0.22f * Mathf.PI), 0);
                hg.Add(glow, Geo.Box(0.03f, 0.03f, d.len * 0.9f), M.T(low.x - hinge.x + side * 0.02f, low.y - hinge.y, 0));
                var pivot = hg.Make("hatch", root.transform, false); pivot.transform.localPosition = new Vector3(hinge.x, hinge.y, -hinge.z);
                P.hatches.Add(new KeyValuePair<Transform, int>(pivot.transform, side));
            }
            var seatGo = new GameObject("seat"); seatGo.transform.SetParent(root.transform, false); seatGo.transform.localPosition = new Vector3(0, seatY + 0.07f, -(d.seatZ + 0.02f)); P.seat = seatGo.transform;
            var ha = new GameObject("headAnchor"); ha.transform.SetParent(root.transform, false); ha.transform.localPosition = new Vector3(0, headPos.y, -(headPos.z - 0.1f)); P.headAnchor = ha.transform;
            // a hit box so bullets and the walking player see the craft
            var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 0, 0); bc.size = new Vector3(d.wid * 2.1f, d.hgt * 1.4f, d.len * 2f);
            return P;
        }

        static Vector3 Catmull(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            // centripetal-ish uniform Catmull-Rom through the four points
            float t3 = t * 3f; int seg = Mathf.Min(2, (int)t3); float u = t3 - seg;
            Vector3[] pts = { p0, p0, p1, p2, p3, p3 }; Vector3 a = pts[seg], b = pts[seg + 1], c = pts[seg + 2], dd = pts[seg + 3];
            return 0.5f * ((2 * b) + (-a + c) * u + (2 * a - 5 * b + 4 * c - dd) * u * u + (-a + 3 * b - 3 * c + dd) * u * u * u);
        }
    }
}
