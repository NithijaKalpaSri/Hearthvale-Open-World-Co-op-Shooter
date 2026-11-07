using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>A small CPU mesh in JS (right-handed) space, counter-clockwise winding.</summary>
    public sealed class Geo
    {
        public List<Vector3> v = new List<Vector3>();
        public List<Vector3> n = new List<Vector3>();
        public List<Vector2> uv = new List<Vector2>();
        public List<int> t = new List<int>();

        // ---- primitives (same orientation conventions as three.js geometries)
        public static Geo Box(float w, float h, float d)
        {
            var g = new Geo();
            float x = w / 2, y = h / 2, z = d / 2;
            // +x, -x, +y, -y, +z, -z
            Face(g, new Vector3(1, 0, 0), new Vector3(x, -y, z), new Vector3(x, -y, -z), new Vector3(x, y, -z), new Vector3(x, y, z));
            Face(g, new Vector3(-1, 0, 0), new Vector3(-x, -y, -z), new Vector3(-x, -y, z), new Vector3(-x, y, z), new Vector3(-x, y, -z));
            Face(g, new Vector3(0, 1, 0), new Vector3(-x, y, z), new Vector3(x, y, z), new Vector3(x, y, -z), new Vector3(-x, y, -z));
            Face(g, new Vector3(0, -1, 0), new Vector3(-x, -y, -z), new Vector3(x, -y, -z), new Vector3(x, -y, z), new Vector3(-x, -y, z));
            Face(g, new Vector3(0, 0, 1), new Vector3(-x, -y, z), new Vector3(x, -y, z), new Vector3(x, y, z), new Vector3(-x, y, z));
            Face(g, new Vector3(0, 0, -1), new Vector3(x, -y, -z), new Vector3(-x, -y, -z), new Vector3(-x, y, -z), new Vector3(x, y, -z));
            return g;
        }
        static void Face(Geo g, Vector3 n, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int o = g.v.Count;
            g.v.Add(a); g.v.Add(b); g.v.Add(c); g.v.Add(d);
            for (int i = 0; i < 4; i++) g.n.Add(n);
            g.uv.Add(new Vector2(0, 0)); g.uv.Add(new Vector2(1, 0)); g.uv.Add(new Vector2(1, 1)); g.uv.Add(new Vector2(0, 1));
            g.t.AddRange(new[] { o, o + 1, o + 2, o, o + 2, o + 3 });
        }

        /// <summary>Box with rounded edges (like three.js RoundedBoxGeometry): w,h,d, corner radius r, seg subdivisions.</summary>
        public static Geo RoundedBox(float w, float h, float d, float r, int seg = 3)
        {
            var g = new Geo(); Vector3 half = new Vector3(w, h, d) * 0.5f; r = Mathf.Min(r, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * 0.99f);
            Vector3 inner = half - new Vector3(r, r, r);
            int n = seg * 2 + 1;
            Vector3[] axisN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (int f = 0; f < 6; f++)
            {
                Vector3 nn = axisN[f]; Vector3 u = f < 2 ? Vector3.forward : Vector3.right; Vector3 v = f < 2 || f >= 4 ? Vector3.up : Vector3.forward;
                int o = g.v.Count;
                for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
                    {
                        float a = (float)i / (n - 1) * 2 - 1, b = (float)j / (n - 1) * 2 - 1;
                        Vector3 p = nn * 1f + u * a + v * b; // point on the unit cube surface, [-1,1]
                        Vector3 pos = Vector3.Scale(p, half);
                        Vector3 c = new Vector3(Mathf.Clamp(pos.x, -inner.x, inner.x), Mathf.Clamp(pos.y, -inner.y, inner.y), Mathf.Clamp(pos.z, -inner.z, inner.z));
                        Vector3 dir = pos - c; Vector3 nrm = dir.sqrMagnitude < 1e-10f ? nn : dir.normalized;
                        g.v.Add(c + nrm * r); g.n.Add(nrm); g.uv.Add(new Vector2((a + 1) / 2, (b + 1) / 2));
                    }
                for (int j = 0; j < n - 1; j++) for (int i = 0; i < n - 1; i++)
                    {
                        int a0 = o + j * n + i, b0 = a0 + 1, c0 = a0 + n + 1, d0 = a0 + n;
                        g.Tri(a0, b0, c0); g.Tri(a0, c0, d0);
                    }
            }
            return g;
        }
        // adds a triangle, flipped if needed so it faces the way its vertex normals point (counter-clockwise outward)
        void Tri(int a, int b, int c)
        {
            Vector3 fn = Vector3.Cross(v[b] - v[a], v[c] - v[a]); Vector3 avg = n[a] + n[b] + n[c];
            if (Vector3.Dot(fn, avg) >= 0) { t.Add(a); t.Add(b); t.Add(c); } else { t.Add(a); t.Add(c); t.Add(b); }
        }

        /// <summary>Torus lying in the XZ plane (axis Y): major radius R, tube radius r.</summary>
        public static Geo Torus(float R, float r, int radial = 24, int tubular = 8)
        {
            var g = new Geo();
            for (int i = 0; i <= radial; i++)
            {
                float a = (float)i / radial * Mathf.PI * 2; Vector3 c = new Vector3(Mathf.Cos(a) * R, 0, Mathf.Sin(a) * R); Vector3 outv = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int j = 0; j <= tubular; j++)
                {
                    float b = (float)j / tubular * Mathf.PI * 2; Vector3 nn = outv * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    g.v.Add(c + nn * r); g.n.Add(nn); g.uv.Add(new Vector2((float)i / radial, (float)j / tubular));
                }
            }
            int row = tubular + 1;
            for (int i = 0; i < radial; i++) for (int j = 0; j < tubular; j++) { int a0 = i * row + j; g.Tri(a0, a0 + row, a0 + row + 1); g.Tri(a0, a0 + row + 1, a0 + 1); }
            return g;
        }

        public Geo Append(Geo o, Matrix4x4 m)
        {
            int off = v.Count; Matrix4x4 nm = m.inverse.transpose;
            for (int i = 0; i < o.v.Count; i++) { v.Add(m.MultiplyPoint3x4(o.v[i])); n.Add(nm.MultiplyVector(o.n[i]).normalized); uv.Add(o.uv[i]); }
            foreach (int idx in o.t) t.Add(idx + off);
            return this;
        }

        /// <summary>Convert this JS-space mesh to a Unity mesh (z mirrored, winding flipped).</summary>
        public Mesh ToUnityMesh(string name)
        {
            var V = new Vector3[v.Count]; var N = new Vector3[v.Count]; var T = new int[t.Count];
            for (int i = 0; i < V.Length; i++) { V[i] = new Vector3(v[i].x, v[i].y, -v[i].z); N[i] = new Vector3(n[i].x, n[i].y, -n[i].z); }
            for (int i = 0; i < T.Length; i += 3) { T[i] = t[i]; T[i + 1] = t[i + 2]; T[i + 2] = t[i + 1]; }
            var m = new Mesh { name = name };
            m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, uv); m.SetTriangles(T, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>Plane in the XY plane facing +z (three.js PlaneGeometry).</summary>
        public static Geo Plane(float w, float h)
        {
            var g = new Geo();
            float x = w / 2, y = h / 2;
            Face(g, new Vector3(0, 0, 1), new Vector3(-x, -y, 0), new Vector3(x, -y, 0), new Vector3(x, y, 0), new Vector3(-x, y, 0));
            return g;
        }

        public static Geo Cylinder(float rTop, float rBot, float h, int seg)
        {
            var g = new Geo();
            float half = h / 2;
            float slope = (rBot - rTop) / h;
            for (int i = 0; i <= seg; i++)
            {
                float u = (float)i / seg, th = u * Mathf.PI * 2;
                float s = Mathf.Sin(th), c = Mathf.Cos(th);
                var nn = new Vector3(s, slope, c).normalized;
                g.v.Add(new Vector3(rTop * s, half, rTop * c)); g.n.Add(nn); g.uv.Add(new Vector2(u, 1));
                g.v.Add(new Vector3(rBot * s, -half, rBot * c)); g.n.Add(nn); g.uv.Add(new Vector2(u, 0));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, b = a + 1, c2 = a + 2, d = a + 3;
                g.t.AddRange(new[] { a, b, c2, b, d, c2 });
            }
            Cap(g, rTop, half, seg, 1);
            Cap(g, rBot, -half, seg, -1);
            return g;
        }
        static void Cap(Geo g, float r, float y, int seg, int dir)
        {
            int c0 = g.v.Count;
            g.v.Add(new Vector3(0, y, 0)); g.n.Add(new Vector3(0, dir, 0)); g.uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= seg; i++)
            {
                float th = (float)i / seg * Mathf.PI * 2, s = Mathf.Sin(th), c = Mathf.Cos(th);
                g.v.Add(new Vector3(r * s, y, r * c)); g.n.Add(new Vector3(0, dir, 0));
                g.uv.Add(new Vector2(s * 0.5f + 0.5f, c * 0.5f * dir + 0.5f));
            }
            for (int i = 0; i < seg; i++)
            {
                if (dir > 0) g.t.AddRange(new[] { c0 + i + 1, c0 + i + 2, c0 });
                else g.t.AddRange(new[] { c0 + i + 2, c0 + i + 1, c0 });
            }
        }

        /// <summary>Round tube along a quadratic bezier (palm trunks, cables).</summary>
        public static Geo Tube(Vector3 p0, Vector3 p1, Vector3 p2, int tubular, float radius, int radial)
        {
            var g = new Geo();
            Vector3 prevT = Vector3.zero;
            for (int i = 0; i <= tubular; i++)
            {
                float t = (float)i / tubular, it = 1 - t;
                Vector3 p = it * it * p0 + 2 * it * t * p1 + t * t * p2;
                Vector3 tan = (2 * it * (p1 - p0) + 2 * t * (p2 - p1)).normalized;
                Vector3 up = Mathf.Abs(tan.y) > 0.99f ? Vector3.right : Vector3.up;
                Vector3 side = Vector3.Cross(up, tan).normalized, bin = Vector3.Cross(tan, side).normalized;
                for (int j = 0; j <= radial; j++)
                {
                    float a = (float)j / radial * Mathf.PI * 2;
                    Vector3 nn = (Mathf.Cos(a) * side + Mathf.Sin(a) * bin).normalized;
                    g.v.Add(p + nn * radius); g.n.Add(nn); g.uv.Add(new Vector2(t, (float)j / radial));
                }
            }
            int row = radial + 1;
            for (int i = 0; i < tubular; i++)
                for (int j = 0; j < radial; j++)
                {
                    int a = i * row + j, b = (i + 1) * row + j, c = (i + 1) * row + j + 1, d = i * row + j + 1;
                    g.t.AddRange(new[] { a, d, b, d, c, b });
                }
            return g;
        }
    }

    /// <summary>three.js-style matrix helpers (JS space).</summary>
    public static class M
    {
        public static Matrix4x4 T(float x, float y, float z) { return Matrix4x4.Translate(new Vector3(x, y, z)); }
        public static Matrix4x4 RotY(float a)
        { float c = Mathf.Cos(a), s = Mathf.Sin(a); var m = Matrix4x4.identity; m.m00 = c; m.m02 = s; m.m20 = -s; m.m22 = c; return m; }
        public static Matrix4x4 RotX(float a)
        { float c = Mathf.Cos(a), s = Mathf.Sin(a); var m = Matrix4x4.identity; m.m11 = c; m.m12 = -s; m.m21 = s; m.m22 = c; return m; }
        public static Matrix4x4 RotZ(float a)
        { float c = Mathf.Cos(a), s = Mathf.Sin(a); var m = Matrix4x4.identity; m.m00 = c; m.m01 = -s; m.m10 = s; m.m11 = c; return m; }
        public static Matrix4x4 SetPos(Matrix4x4 m, float x, float y, float z) { m.m03 = x; m.m13 = y; m.m23 = z; return m; }
        public static Matrix4x4 SetPos(Matrix4x4 m, Vector3 p) { return SetPos(m, p.x, p.y, p.z); }
        public static Color Hex(string hex)
        {
            Color c; ColorUtility.TryParseHtmlString(hex, out c); return c.linear; // three.js stores linear working colours
        }
    }

    /// <summary>
    /// Collects geometry per material key and merges it into one mesh per 80 m chunk
    /// (so Unity can frustum-cull chunks). `detail` pieces go into separate meshes that are
    /// hidden at range and never cast shadows.
    /// </summary>
    public sealed class MeshBatch
    {
        const int Chunk = 80;
        sealed class Part { public Vector3[] v; public Vector3[] n; public Vector2[] uv; public Color[] c; public int[] t; public Vector3 center; }
        readonly Dictionary<string, List<Part>> parts = new Dictionary<string, List<Part>>();

        public void Add(string key, Geo geo, Matrix4x4? matrix, string color = "#ffffff", float uvTile = 0, bool detail = false)
        { Add(key, geo, matrix, M.Hex(color), uvTile, detail); }

        public void Add(string key, Geo geo, Matrix4x4? matrix, Color color, float uvTile = 0, bool detail = false)
        {
            if (detail) key += "|d";
            int n = geo.v.Count;
            var p = new Part { v = new Vector3[n], n = new Vector3[n], uv = geo.uv.ToArray(), c = new Color[n], t = geo.t.ToArray() };
            Matrix4x4 m = matrix ?? Matrix4x4.identity;
            Matrix4x4 nm = m.inverse.transpose;
            Vector3 mn = new Vector3(1e9f, 1e9f, 1e9f), mx = -mn;
            for (int i = 0; i < n; i++)
            {
                Vector3 w = m.MultiplyPoint3x4(geo.v[i]);
                p.v[i] = w;
                p.n[i] = nm.MultiplyVector(geo.n[i]).normalized;
                p.c[i] = color;
                mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w);
                if (uvTile > 0)
                {
                    // world-space box mapping (port of textures.js boxUV)
                    Vector3 nn = p.n[i];
                    float ax = Mathf.Abs(nn.x), ay = Mathf.Abs(nn.y), az = Mathf.Abs(nn.z), u, vv;
                    if (ay >= ax && ay >= az) { u = w.x; vv = w.z; }
                    else if (ax >= az) { u = w.z; vv = w.y; }
                    else { u = w.x; vv = w.y; }
                    p.uv[i] = new Vector2(u / uvTile, vv / uvTile);
                }
            }
            p.center = (mn + mx) / 2;
            List<Part> list;
            if (!parts.TryGetValue(key, out list)) parts[key] = list = new List<Part>();
            list.Add(p);
        }

        public class Result
        {
            public Dictionary<string, List<MeshRenderer>> meshes = new Dictionary<string, List<MeshRenderer>>();
            public List<MeshRenderer> details = new List<MeshRenderer>();
        }

        public Result Build(Transform parent, Func<string, Material> materials, ICollection<string> noShadow = null)
        {
            var res = new Result();
            foreach (var kv in parts)
            {
                string[] sp = kv.Key.Split('|');
                string key = sp[0]; bool detail = sp.Length > 1 && sp[1] == "d";
                var chunks = new Dictionary<long, List<Part>>();
                foreach (var p in kv.Value)
                {
                    long k = (long)Mathf.Floor(p.center.x / Chunk) * 4096 + (long)Mathf.Floor(p.center.z / Chunk);
                    List<Part> l; if (!chunks.TryGetValue(k, out l)) chunks[k] = l = new List<Part>();
                    l.Add(p);
                }
                foreach (var l in chunks.Values)
                {
                    int vc = 0, tc = 0; foreach (var p in l) { vc += p.v.Length; tc += p.t.Length; }
                    var V = new Vector3[vc]; var N = new Vector3[vc]; var UV = new Vector2[vc]; var C = new Color[vc]; var T = new int[tc];
                    int vo = 0, to = 0;
                    foreach (var p in l)
                    {
                        for (int i = 0; i < p.v.Length; i++)
                        {
                            // JS -> Unity: mirror z (and mirror the normal)
                            V[vo + i] = new Vector3(p.v[i].x, p.v[i].y, -p.v[i].z);
                            N[vo + i] = new Vector3(p.n[i].x, p.n[i].y, -p.n[i].z);
                            UV[vo + i] = p.uv[i]; C[vo + i] = p.c[i];
                        }
                        for (int i = 0; i < p.t.Length; i += 3)
                        { // flip winding because of the mirror
                            T[to + i] = p.t[i] + vo; T[to + i + 1] = p.t[i + 2] + vo; T[to + i + 2] = p.t[i + 1] + vo;
                        }
                        vo += p.v.Length; to += p.t.Length;
                    }
                    var mesh = new Mesh { name = key + (detail ? "_detail" : "") };
                    mesh.indexFormat = vc > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    mesh.SetVertices(V); mesh.SetNormals(N); mesh.SetUVs(0, UV); mesh.SetColors(C); mesh.SetTriangles(T, 0);
                    mesh.RecalculateBounds();
                    mesh.UploadMeshData(true);
                    var go = new GameObject(mesh.name);
                    go.transform.SetParent(parent, false);
                    go.isStatic = true;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = materials(key);
                    bool noSh = detail || (noShadow != null && noShadow.Contains(key));
                    mr.shadowCastingMode = noSh ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    mr.receiveShadows = true;
                    List<MeshRenderer> list;
                    if (!res.meshes.TryGetValue(key, out list)) res.meshes[key] = list = new List<MeshRenderer>();
                    list.Add(mr);
                    if (detail) res.details.Add(mr);
                }
            }
            parts.Clear();
            return res;
        }
    }
}
