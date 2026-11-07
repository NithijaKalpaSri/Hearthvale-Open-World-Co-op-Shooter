using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>
    /// Procedural organic modelling: a body is described as a smooth union of tapered capsules and ellipsoids
    /// (a signed distance field), meshed with Naive Surface Nets into ONE seamless surface, and then skinned to a
    /// bone skeleton with smooth weights, so elbows, knees and necks bend without gaps or visible joints.
    /// </summary>
    public sealed class Sculpt
    {
        enum T { Cone, Ellipsoid, Box }
        struct Prim
        {
            public T type; public Vector3 a, b; public float r1, r2, k; public Quaternion inv; public bool subtract; public Vector3 half;
        }
        readonly List<Prim> prims = new List<Prim>();
        public float inflate;                 // grow the whole surface (used for clothing shells)
        public float clipYMin = -1e9f, clipYMax = 1e9f; public float clipZMin = -1e9f, clipZMax = 1e9f; // flat cuts (hems, waistline)

        // ---- building blocks
        public Sculpt Cone(Vector3 a, Vector3 b, float r1, float r2, float k = 0.03f) { prims.Add(new Prim { type = T.Cone, a = a, b = b, r1 = r1, r2 = r2, k = k }); return this; }
        public Sculpt Ellipsoid(Vector3 c, Vector3 radii, float k = 0.03f, Vector3? euler = null)
        { prims.Add(new Prim { type = T.Ellipsoid, a = c, half = radii, k = k, inv = Quaternion.Inverse(Quaternion.Euler(euler ?? Vector3.zero)) }); return this; }
        public Sculpt SubEllipsoid(Vector3 c, Vector3 radii, float k = 0.01f, Vector3? euler = null)
        { prims.Add(new Prim { type = T.Ellipsoid, a = c, half = radii, k = k, subtract = true, inv = Quaternion.Inverse(Quaternion.Euler(euler ?? Vector3.zero)) }); return this; }
        public Sculpt SubCone(Vector3 a, Vector3 b, float r1, float r2, float k = 0.01f) { prims.Add(new Prim { type = T.Cone, a = a, b = b, r1 = r1, r2 = r2, k = k, subtract = true }); return this; }
        public Sculpt SubBox(Vector3 c, Vector3 half, float k = 0.01f) { prims.Add(new Prim { type = T.Box, a = c, half = half, k = k, subtract = true, inv = Quaternion.identity }); return this; }
        public int Count { get { return prims.Count; } }

        // ---- distance functions
        static float RoundCone(Vector3 p, Vector3 a, Vector3 b, float r1, float r2)
        {
            Vector3 ba = b - a; float l2 = Vector3.Dot(ba, ba), rr = r1 - r2, a2 = l2 - rr * rr, il2 = 1f / l2;
            Vector3 pa = p - a; float y = Vector3.Dot(pa, ba), z = y - l2; Vector3 q = pa * l2 - ba * y; float x2 = Vector3.Dot(q, q), y2 = y * y * l2, z2 = z * z * l2;
            float k = Mathf.Sign(rr) * rr * rr * x2;
            if (Mathf.Sign(z) * a2 * z2 > k) return Mathf.Sqrt(x2 + z2) * il2 - r2;
            if (Mathf.Sign(y) * a2 * y2 < k) return Mathf.Sqrt(x2 + y2) * il2 - r1;
            return (Mathf.Sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
        }
        static float Ell(Vector3 p, Vector3 r)
        {
            float k0 = new Vector3(p.x / r.x, p.y / r.y, p.z / r.z).magnitude, k1 = new Vector3(p.x / (r.x * r.x), p.y / (r.y * r.y), p.z / (r.z * r.z)).magnitude;
            return k1 < 1e-8f ? -Mathf.Min(r.x, Mathf.Min(r.y, r.z)) : k0 * (k0 - 1f) / k1;
        }
        static float SMin(float a, float b, float k) { if (k <= 1e-5f) return Mathf.Min(a, b); float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k; return Mathf.Min(a, b) - h * h * k * 0.25f; }
        static float SMax(float a, float b, float k) { return -SMin(-a, -b, k); }

        float Eval(ref Prim q, Vector3 p)
        {
            switch (q.type)
            {
                case T.Cone: return RoundCone(p, q.a, q.b, q.r1, q.r2);
                case T.Ellipsoid: return Ell(q.inv * (p - q.a), q.half);
                default: { Vector3 d = new Vector3(Mathf.Abs(p.x - q.a.x), Mathf.Abs(p.y - q.a.y), Mathf.Abs(p.z - q.a.z)) - q.half; return Vector3.Max(d, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(d.x, Mathf.Max(d.y, d.z)), 0f); }
            }
        }

        public float D(Vector3 p)
        {
            float d = 1e5f;
            for (int i = 0; i < prims.Count; i++)
            {
                var q = prims[i]; float v = Eval(ref q, p);
                if (q.subtract) d = SMax(d, -v, q.k); else d = SMin(d, v, q.k);
            }
            d -= inflate;
            d = Mathf.Max(d, Mathf.Max(clipYMin - p.y, p.y - clipYMax)); d = Mathf.Max(d, Mathf.Max(clipZMin - p.z, p.z - clipZMax));
            return d;
        }

        // ------------------------------------------------------------------ meshing (Naive Surface Nets)
        public sealed class Result { public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<int> t = new List<int>(); }

        public Result Mesh(Vector3 min, Vector3 max, float voxel)
        {
            int nx = Mathf.CeilToInt((max.x - min.x) / voxel) + 1, ny = Mathf.CeilToInt((max.y - min.y) / voxel) + 1, nz = Mathf.CeilToInt((max.z - min.z) / voxel) + 1;
            var cache = new float[nx * ny * nz]; for (int i = 0; i < cache.Length; i++) cache[i] = float.NaN;
            Func<int, int, int, float> G = (i, j, k) =>
            {
                int id = (k * ny + j) * nx + i; float v = cache[id];
                if (float.IsNaN(v)) { v = D(min + new Vector3(i, j, k) * voxel); cache[id] = v; }
                return v;
            };
            // coarse test: blocks of 6^3 cells whose centre is far from the surface are skipped
            const int B = 6; float blockR = B * voxel * 0.5f * 1.75f;
            int bx = (nx + B - 1) / B, by = (ny + B - 1) / B, bz = (nz + B - 1) / B; var active = new bool[bx * by * bz];
            for (int a = 0; a < bx; a++) for (int b = 0; b < by; b++) for (int c = 0; c < bz; c++)
                    { Vector3 ctr = min + new Vector3(a * B + B / 2f, b * B + B / 2f, c * B + B / 2f) * voxel; active[(c * by + b) * bx + a] = Mathf.Abs(D(ctr)) < blockR + voxel; }
            var vid = new int[(nx - 1) * (ny - 1) * (nz - 1)]; for (int i = 0; i < vid.Length; i++) vid[i] = -1;
            var res = new Result(); int cx = nx - 1, cy = ny - 1;
            int[,] corner = { { 0, 0, 0 }, { 1, 0, 0 }, { 0, 1, 0 }, { 1, 1, 0 }, { 0, 0, 1 }, { 1, 0, 1 }, { 0, 1, 1 }, { 1, 1, 1 } };
            int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            var cv = new float[8];
            for (int k = 0; k < nz - 1; k++) for (int j = 0; j < ny - 1; j++) for (int i = 0; i < nx - 1; i++)
                    {
                        if (!active[((k / B) * by + (j / B)) * bx + (i / B)]) continue;
                        int mask = 0;
                        for (int c = 0; c < 8; c++) { cv[c] = G(i + corner[c, 0], j + corner[c, 1], k + corner[c, 2]); if (cv[c] < 0) mask |= 1 << c; }
                        if (mask == 0 || mask == 255) continue;
                        Vector3 sum = Vector3.zero; int cnt = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            int c0 = edges[e, 0], c1 = edges[e, 1]; if (((mask >> c0) & 1) == ((mask >> c1) & 1)) continue;
                            float t = cv[c0] / (cv[c0] - cv[c1]);
                            Vector3 p0 = new Vector3(corner[c0, 0], corner[c0, 1], corner[c0, 2]), p1 = new Vector3(corner[c1, 0], corner[c1, 1], corner[c1, 2]);
                            sum += Vector3.Lerp(p0, p1, t); cnt++;
                        }
                        Vector3 pos = min + (new Vector3(i, j, k) + sum / cnt) * voxel;
                        vid[(k * cy + j) * cx + i] = res.v.Count; res.v.Add(pos);
                    }
            // quads along every sign-changing grid edge
            for (int k = 0; k < nz; k++) for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
                    {
                        if (!active[((Mathf.Min(k, nz - 2) / B) * by + (Mathf.Min(j, ny - 2) / B)) * bx + (Mathf.Min(i, nx - 2) / B)]) continue;
                        float s0 = G(i, j, k);
                        for (int ax = 0; ax < 3; ax++)
                        {
                            int ii = i + (ax == 0 ? 1 : 0), jj = j + (ax == 1 ? 1 : 0), kk = k + (ax == 2 ? 1 : 0);
                            if (ii >= nx || jj >= ny || kk >= nz) continue;
                            float s1 = G(ii, jj, kk); if ((s0 < 0) == (s1 < 0)) continue;
                            if (i == 0 && ax != 0 || j == 0 && ax != 1 || k == 0 && ax != 2) { if (i == 0 && ax != 0 || j == 0 && ax != 1 || k == 0 && ax != 2) continue; }
                            int u = (ax + 1) % 3, w = (ax + 2) % 3; int[] o = { i, j, k };
                            if (o[u] == 0 || o[w] == 0) continue;
                            int[] q = new int[4];
                            int[][] off = { new[] { 0, 0 }, new[] { 1, 0 }, new[] { 1, 1 }, new[] { 0, 1 } };
                            bool ok = true;
                            for (int m = 0; m < 4; m++) { int[] c = { o[0], o[1], o[2] }; c[u] -= off[m][0]; c[w] -= off[m][1]; if (c[0] >= cx || c[1] >= cy || c[2] >= nz - 1) { ok = false; break; } q[m] = vid[(c[2] * cy + c[1]) * cx + c[0]]; if (q[m] < 0) { ok = false; break; } }
                            if (!ok) continue;
                            bool flip = s0 < 0; if (ax == 1) flip = !flip;
                            if (!flip) { res.t.Add(q[0]); res.t.Add(q[1]); res.t.Add(q[2]); res.t.Add(q[0]); res.t.Add(q[2]); res.t.Add(q[3]); }
                            else { res.t.Add(q[0]); res.t.Add(q[2]); res.t.Add(q[1]); res.t.Add(q[0]); res.t.Add(q[3]); res.t.Add(q[2]); }
                        }
                    }
            // project onto the true surface and take exact normals from the field gradient
            float h = voxel * 0.5f;
            for (int i = 0; i < res.v.Count; i++)
            {
                Vector3 p = res.v[i];
                for (int it = 0; it < 2; it++) { Vector3 g = Grad(p, h); float d = D(p); if (g.sqrMagnitude > 1e-12f) p -= g.normalized * d * 0.9f; }
                res.v[i] = p; Vector3 gn = Grad(p, h); res.n.Add(gn.sqrMagnitude > 1e-12f ? gn.normalized : Vector3.up);
            }
            // make sure the triangles face the way the normals point
            for (int i = 0; i < res.t.Count; i += 3)
            {
                Vector3 fn = Vector3.Cross(res.v[res.t[i + 1]] - res.v[res.t[i]], res.v[res.t[i + 2]] - res.v[res.t[i]]);
                if (Vector3.Dot(fn, res.n[res.t[i]] + res.n[res.t[i + 1]] + res.n[res.t[i + 2]]) < 0) { int tmp = res.t[i + 1]; res.t[i + 1] = res.t[i + 2]; res.t[i + 2] = tmp; }
            }
            return res;
        }

        Vector3 Grad(Vector3 p, float h)
        {
            return new Vector3(D(p + new Vector3(h, 0, 0)) - D(p - new Vector3(h, 0, 0)), D(p + new Vector3(0, h, 0)) - D(p - new Vector3(0, h, 0)), D(p + new Vector3(0, 0, h)) - D(p - new Vector3(0, 0, h)));
        }

        /// <summary>Ambient occlusion estimate: how much of the space just outside the surface is blocked (creases get dark).</summary>
        public float AO(Vector3 p, Vector3 n)
        {
            float occ = 0, w = 1;
            for (int i = 1; i <= 4; i++) { float h = 0.015f * i; float d = D(p + n * h); occ += (h - d) * w; w *= 0.6f; }
            return Mathf.Clamp01(1f - occ * 6f);
        }
    }

    /// <summary>A bone: name, parent, position in the bind pose, and the segment used to weight vertices to it.</summary>
    public sealed class BoneDef
    {
        public string name; public int parent; public Vector3 pos; public Vector3 segA, segB; public float falloff;
        public BoneDef(string n, int p, Vector3 pos, Vector3 a, Vector3 b, float falloff = 0.05f) { name = n; parent = p; this.pos = pos; segA = a; segB = b; this.falloff = falloff; }
    }

    public static class SkinBuilder
    {
        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        { Vector3 ab = b - a; float l = ab.sqrMagnitude; float t = l < 1e-9f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, ab) / l); return (p - (a + ab * t)).magnitude; }

        /// <summary>Creates the bone GameObjects under root; returns them in bone order.</summary>
        public static Transform[] MakeBones(Transform root, BoneDef[] defs)
        {
            var t = new Transform[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                var go = new GameObject(defs[i].name); t[i] = go.transform; t[i].SetParent(defs[i].parent < 0 ? root : t[defs[i].parent], false);
                t[i].position = root.TransformPoint(defs[i].pos);
            }
            return t;
        }

        public static Mesh Build(Sculpt.Result r, BoneDef[] defs, Sculpt s, string name, Func<Vector3, Vector3, Color> color, float uvScale = 1.2f, bool ao = true)
        {
            var m = new Mesh { name = name, indexFormat = r.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            int n = r.v.Count; var weights = new BoneWeight[n]; var cols = new Color[n]; var uv0 = new Vector2[n]; var uv1 = new Vector2[n]; var bn = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = r.v[i];
                // smooth weights: closest bones win, with a soft falloff so joints blend
                var best = new float[4]; var idx = new int[4]; for (int q = 0; q < 4; q++) { best[q] = 0; idx[q] = 0; }
                float total = 0; var all = new float[defs.Length];
                float dmin = 1e9f; for (int b = 0; b < defs.Length; b++) { all[b] = SegDist(p, defs[b].segA, defs[b].segB); if (all[b] < dmin) dmin = all[b]; }
                for (int b = 0; b < defs.Length; b++)
                {
                    float w = Mathf.Exp(-Mathf.Pow(Mathf.Max(0, all[b] - dmin) / defs[b].falloff, 2f) * 3f); if (w < 0.02f) continue;
                    for (int q = 0; q < 4; q++) if (w > best[q]) { for (int z = 3; z > q; z--) { best[z] = best[z - 1]; idx[z] = idx[z - 1]; } best[q] = w; idx[q] = b; break; }
                }
                for (int q = 0; q < 4; q++) total += best[q]; if (total < 1e-6f) { best[0] = 1; total = 1; }
                weights[i] = new BoneWeight { boneIndex0 = idx[0], weight0 = best[0] / total, boneIndex1 = idx[1], weight1 = best[1] / total, boneIndex2 = idx[2], weight2 = best[2] / total, boneIndex3 = idx[3], weight3 = best[3] / total };
                Color c = color != null ? color(p, r.n[i]) : Color.white;
                if (ao) { float a = s.AO(p, r.n[i]); c.r *= a; c.g *= a; c.b *= a; }
                cols[i] = c; uv0[i] = new Vector2(p.x, p.y) * uvScale; uv1[i] = new Vector2(p.z, p.y) * uvScale; bn.Add(r.n[i]);
            }
            m.SetVertices(r.v); m.SetNormals(r.n); m.SetTriangles(r.t, 0); m.SetColors(cols); m.SetUVs(0, uv0); m.SetUVs(1, uv1); m.SetUVs(2, bn);
            m.boneWeights = weights;
            var bind = new Matrix4x4[defs.Length]; for (int b = 0; b < defs.Length; b++) bind[b] = Matrix4x4.Translate(-defs[b].pos); // bones have identity rotation in the bind pose
            m.bindposes = bind; m.RecalculateBounds(); return m;
        }
    }
}
