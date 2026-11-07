using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>
    /// Procedural plant meshes (Unity space). New realistic look compared with the JS game's lumpy blobs:
    /// branching trunks with root flare, hundreds of alpha-cut leaf cards, needle-card conifers.
    /// Vertex colour RGB = baked ambient occlusion / hue variation, ALPHA = wind weight (see HVPlant.shader).
    /// </summary>
    public sealed class MeshAcc
    {
        public List<Vector3> v = new List<Vector3>(); public List<Vector3> n = new List<Vector3>();
        public List<Vector2> uv = new List<Vector2>(); public List<Color> c = new List<Color>(); public List<int> t = new List<int>();

        public void Tube(Vector3[] pts, float[] radii, int seg, float ao0, float ao1, float wind0, float wind1, float uvScale = 1f)
        {
            int rings = pts.Length; int baseIdx = v.Count; float len = 0;
            Vector3 S = Vector3.zero;
            for (int i = 0; i < rings; i++)
            {
                Vector3 T = i == 0 ? pts[1] - pts[0] : i == rings - 1 ? pts[i] - pts[i - 1] : pts[i + 1] - pts[i - 1];
                T.Normalize();
                if (i == 0) { Vector3 up = Mathf.Abs(T.y) > 0.95f ? Vector3.right : Vector3.up; S = Vector3.Cross(T, up).normalized; }
                else { S = (S - T * Vector3.Dot(S, T)).normalized; } // parallel transport
                Vector3 B = Vector3.Cross(S, T);
                if (i > 0) len += Vector3.Distance(pts[i], pts[i - 1]);
                float f = (float)i / (rings - 1);
                float ao = Mathf.Lerp(ao0, ao1, f), w = Mathf.Lerp(wind0, wind1, f * f);
                for (int j = 0; j <= seg; j++)
                {
                    float a = (float)j / seg * Mathf.PI * 2;
                    Vector3 nn = Mathf.Cos(a) * S + Mathf.Sin(a) * B;
                    v.Add(pts[i] + nn * radii[i]); n.Add(nn);
                    uv.Add(new Vector2((float)j / seg * Mathf.Max(1f, radii[0] * 4f) * uvScale, len / 1.4f * uvScale));
                    c.Add(new Color(ao, ao, ao, w));
                }
            }
            int row = seg + 1;
            for (int i = 0; i < rings - 1; i++)
                for (int j = 0; j < seg; j++)
                {
                    int a = baseIdx + i * row + j, b = a + row, c2 = b + 1, d = a + 1;
                    t.Add(a); t.Add(b); t.Add(d); t.Add(d); t.Add(b); t.Add(c2);
                }
        }

        /// <summary>A quad (shader is two-sided). centre, right/up axes, width, height.</summary>
        public void Card(Vector3 center, Vector3 right, Vector3 up, float w, float h, Vector3 normal, Color col, float windTip, Vector2? uv0 = null, Vector2? uv1 = null)
        {
            Vector2 a = uv0 ?? Vector2.zero, b = uv1 ?? Vector2.one;
            int o = v.Count; right = right.normalized * w * 0.5f; up = up.normalized * h * 0.5f;
            v.Add(center - right - up); v.Add(center + right - up); v.Add(center + right + up); v.Add(center - right + up);
            for (int i = 0; i < 4; i++) n.Add(normal);
            uv.Add(new Vector2(a.x, a.y)); uv.Add(new Vector2(b.x, a.y)); uv.Add(new Vector2(b.x, b.y)); uv.Add(new Vector2(a.x, b.y));
            // tips (upper half) flutter more than the attachment point
            var lo = new Color(col.r * 0.85f, col.g * 0.85f, col.b * 0.85f, col.a); var hi = new Color(col.r, col.g, col.b, Mathf.Clamp01(col.a + windTip));
            c.Add(lo); c.Add(lo); c.Add(hi); c.Add(hi);
            t.Add(o); t.Add(o + 3); t.Add(o + 1); t.Add(o + 1); t.Add(o + 3); t.Add(o + 2);
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateBounds(); m.UploadMeshData(true); return m;
        }
    }

    public sealed class PlantMeshes
    {
        public Mesh trunk, leaves, trunkFar, leavesFar; public float height;
    }

    public static class PlantGeo
    {
        static Vector3 RandDir(Mulberry32 r) { float a = r.Next() * Mathf.PI * 2, y = r.Next() * 2 - 1, s = Mathf.Sqrt(1 - y * y); return new Vector3(Mathf.Cos(a) * s, y, Mathf.Sin(a) * s); }

        static Vector3[] Curve(Vector3 a, Vector3 b, Vector3 bend, int n)
        {
            var p = new Vector3[n];
            for (int i = 0; i < n; i++) { float t = (float)i / (n - 1); p[i] = Vector3.Lerp(a, b, t) + bend * Mathf.Sin(t * Mathf.PI); }
            return p;
        }

        static void LeafCluster(MeshAcc m, Mulberry32 r, Vector3 pos, Vector3 crownC, Vector3 crownR, float size, float baseWind)
        {
            Vector3 rel = pos - crownC; Vector3 q = new Vector3(rel.x / crownR.x, rel.y / crownR.y, rel.z / crownR.z);
            float f = Mathf.Clamp01(q.magnitude);                 // 0 = middle of the crown, 1 = outer shell
            float top = Mathf.Clamp01(0.5f + 0.5f * q.y);
            float ao = Mathf.Lerp(0.42f, 1.05f, f) * Mathf.Lerp(0.9f, 1.12f, top);
            Color col = new Color(ao * (0.9f + r.Next() * 0.14f), ao, ao * (0.86f + r.Next() * 0.18f), baseWind + 0.45f * f);
            Vector3 nrm = Vector3.Lerp(rel.normalized, Vector3.up, 0.3f).normalized;
            float yaw = r.Next() * Mathf.PI * 2, tilt = (r.Next() - 0.5f) * 1.1f;
            Vector3 right = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw));
            Vector3 fwd = new Vector3(-Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
            Vector3 up1 = (Vector3.up * Mathf.Cos(tilt) + fwd * Mathf.Sin(tilt)).normalized;
            float s = size * (0.85f + r.Next() * 0.4f);
            m.Card(pos, right, up1, s, s, nrm, col, 0.2f);
            // crossing card gives volume from every direction
            Vector3 right2 = Quaternion.AngleAxis(60f + r.Next() * 60f, up1) * right;
            m.Card(pos + up1 * 0.03f, right2, up1, s * 0.95f, s * 0.95f, nrm, col, 0.2f);
        }

        // ------------------------------------------------------------------ broadleaf trees (4 shapes)
        public static PlantMeshes Broadleaf(int seed, int type)
        {
            var r = new Mulberry32(seed);
            float th, baseR, ry, rx; int nBranch, nLeaf, nLeafFar; float leafSize;
            switch (type)
            {
                case 1: th = 4.2f + r.Next() * 0.8f; baseR = 0.26f; rx = 1.55f; ry = 3.3f; nBranch = 7; nLeaf = 210; nLeafFar = 54; leafSize = 1.05f; break;   // tall, narrow (poplar)
                case 2: th = 2.0f + r.Next() * 0.6f; baseR = 0.36f; rx = 3.5f; ry = 1.7f; nBranch = 8; nLeaf = 270; nLeafFar = 64; leafSize = 1.25f; break;   // low, spreading (umbrella)
                case 3: th = 3.2f + r.Next() * 0.8f; baseR = 0.15f; rx = 1.9f; ry = 2.6f; nBranch = 6; nLeaf = 190; nLeafFar = 50; leafSize = 0.95f; break;   // slender birch
                default: th = 2.6f + r.Next() * 1.0f; baseR = 0.32f; rx = 2.7f; ry = 2.2f; nBranch = 8; nLeaf = 250; nLeafFar = 60; leafSize = 1.2f; break;  // classic round oak
            }
            Vector3 crownC = new Vector3(0, th + ry * 0.62f, 0), crownR = new Vector3(rx, ry, rx);
            var trunk = new MeshAcc(); var leaves = new MeshAcc(); var trunkF = new MeshAcc(); var leavesF = new MeshAcc();

            // main trunk with a slight lean/wobble, root flare and a leader into the crown
            int steps = 9; float topY = th + ry * 0.8f; var tp = new Vector3[steps]; var tr = new float[steps];
            float wob = 0.14f + r.Next() * 0.1f, ph = r.Next() * 6f;
            for (int i = 0; i < steps; i++)
            {
                float t = (float)i / (steps - 1), y = t * topY;
                tp[i] = new Vector3(Mathf.Sin(t * 3.1f + ph) * wob * t, y, Mathf.Cos(t * 2.3f + ph) * wob * t);
                float flare = 1f + 1.1f * Mathf.Exp(-y * 3.2f);
                tr[i] = baseR * flare * (1f - 0.86f * Mathf.Pow(t, 0.8f)) + 0.02f;
            }
            trunk.Tube(tp, tr, 10, 0.75f, 1f, 0f, 0.12f);
            trunkF.Tube(new[] { tp[0], tp[steps / 2], tp[steps - 1] }, new[] { tr[0], tr[steps / 2], tr[steps - 1] }, 5, 0.8f, 1f, 0f, 0.1f);

            // branches + twigs; their tips are where leaf clusters hang
            var anchors = new List<Vector3>();
            for (int b = 0; b < nBranch; b++)
            {
                float a = (float)b / nBranch * Mathf.PI * 2 + (r.Next() - 0.5f) * 0.6f;
                float h0 = th * (0.5f + r.Next() * 0.45f) + (type == 2 ? 0.2f : 0);
                float tNorm = Mathf.Clamp01(h0 / topY);
                Vector3 s0 = new Vector3(Mathf.Sin(tNorm * 3.1f + ph) * wob * tNorm, h0, Mathf.Cos(tNorm * 2.3f + ph) * wob * tNorm);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Vector3 e = crownC + dir * rx * (0.62f + r.Next() * 0.25f) + Vector3.up * ry * ((r.Next() - 0.35f) * 0.8f);
                float r0 = Mathf.Max(0.04f, baseR * 0.42f * (1f - tNorm * 0.5f));
                var pts = Curve(s0, e, Vector3.up * (0.25f + r.Next() * 0.45f), 6);
                var rad = new float[6]; for (int i = 0; i < 6; i++) rad[i] = Mathf.Lerp(r0, 0.018f, Mathf.Pow((float)i / 5, 0.85f));
                trunk.Tube(pts, rad, 6, 0.8f, 1f, 0.02f, 0.2f);
                anchors.Add(e);
                for (int s = 0; s < 3; s++)
                {
                    float at = 0.38f + 0.2f * s; Vector3 sp = Vector3.Lerp(s0, e, at) + Vector3.up * 0.2f;
                    Vector3 sd = (Quaternion.AngleAxis((r.Next() - 0.5f) * 110f, Vector3.up) * dir + Vector3.up * (0.25f + r.Next() * 0.4f)).normalized;
                    Vector3 se = sp + sd * (0.9f + r.Next() * 0.9f);
                    var sp2 = Curve(sp, se, Vector3.up * 0.12f, 4); var sr = new float[4]; for (int i = 0; i < 4; i++) sr[i] = Mathf.Lerp(r0 * 0.4f, 0.012f, (float)i / 3);
                    trunk.Tube(sp2, sr, 5, 0.85f, 1f, 0.1f, 0.35f);
                    anchors.Add(se);
                }
            }
            // leaf clusters: first at every branch tip, then scattered through the crown shell
            int placed = 0;
            foreach (var an in anchors) { for (int k = 0; k < 2 && placed < nLeaf; k++, placed++) LeafCluster(leaves, r, an + RandDir(r) * 0.4f, crownC, crownR, leafSize, 0.25f); }
            while (placed < nLeaf)
            {
                Vector3 d = RandDir(r); float rad = 0.45f + r.Next() * 0.55f;
                Vector3 p = crownC + new Vector3(d.x * rx * rad, d.y * ry * rad * (d.y < 0 ? 0.65f : 1f), d.z * rx * rad);
                LeafCluster(leaves, r, p, crownC, crownR, leafSize, 0.25f); placed++;
            }
            for (int i = 0; i < nLeafFar; i++)
            {
                Vector3 d = RandDir(r); float rad = 0.5f + r.Next() * 0.5f;
                Vector3 p = crownC + new Vector3(d.x * rx * rad, d.y * ry * rad * (d.y < 0 ? 0.65f : 1f), d.z * rx * rad);
                LeafCluster(leavesF, r, p, crownC, crownR, leafSize * 2.1f, 0.25f);
            }
            return new PlantMeshes { trunk = trunk.Build("trunk" + seed), leaves = leaves.Build("leaves" + seed), trunkFar = trunkF.Build("trunkF" + seed), leavesFar = leavesF.Build("leavesF" + seed), height = th + ry * 1.7f };
        }

        // ------------------------------------------------------------------ conifers: whorls of needle-branch cards
        public static PlantMeshes Conifer(int seed, int type)
        {
            var r = new Mulberry32(seed);
            float th = 1.4f + r.Next() * 0.8f; float topH = th + (type == 0 ? 6.2f : 7.8f);
            int whorls = type == 0 ? 11 : 10; float maxLen = type == 0 ? 2.5f : 2.0f;
            var trunk = new MeshAcc(); var leaves = new MeshAcc(); var trunkF = new MeshAcc(); var leavesF = new MeshAcc();
            int steps = 8; var tp = new Vector3[steps]; var tr = new float[steps];
            for (int i = 0; i < steps; i++) { float t = (float)i / (steps - 1); tp[i] = new Vector3(Mathf.Sin(t * 2f + seed) * 0.05f * t, t * topH, 0); tr[i] = 0.27f * (1f + 0.7f * Mathf.Exp(-t * topH * 3f)) * (1f - 0.96f * t) + 0.01f; }
            trunk.Tube(tp, tr, 8, 0.7f, 0.9f, 0f, 0.1f);
            trunkF.Tube(new[] { tp[0], tp[steps - 1] }, new[] { tr[0], tr[steps - 1] }, 5, 0.75f, 0.9f, 0f, 0.1f);
            Vector3 crownC = new Vector3(0, th + (topH - th) * 0.45f, 0), crownR = new Vector3(maxLen, (topH - th) * 0.5f, maxLen);
            for (int w = 0; w < whorls; w++)
            {
                float t = (float)w / (whorls - 1); float y = Mathf.Lerp(th, topH - 0.5f, t);
                float len = Mathf.Lerp(maxLen, 0.55f, Mathf.Pow(t, 0.9f)) * (0.9f + r.Next() * 0.2f);
                int nb = Mathf.RoundToInt(Mathf.Lerp(7, 4, t)); float off = r.Next() * 6f;
                float droop = Mathf.Lerp(-0.42f, 0.12f, t);
                for (int b = 0; b < nb; b++)
                {
                    float a = (float)b / nb * Mathf.PI * 2 + off + (r.Next() - 0.5f) * 0.3f;
                    Vector3 dir = new Vector3(Mathf.Cos(a), droop, Mathf.Sin(a)).normalized;
                    float L = len * (0.9f + r.Next() * 0.25f);
                    Vector3 mid = new Vector3(0, y + r.Next() * 0.25f, 0) + dir * (L * 0.5f);
                    float f = Mathf.Clamp01(L / maxLen);
                    float ao = Mathf.Lerp(0.55f, 1.05f, Mathf.Clamp01(0.4f + t * 0.5f));
                    Color col = new Color(ao * (0.92f + r.Next() * 0.1f), ao, ao * (0.9f + r.Next() * 0.12f), 0.25f + 0.2f * (1 - t));
                    Vector3 nrm = Vector3.Lerp(new Vector3(dir.x, 0, dir.z).normalized, Vector3.up, 0.4f).normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
                    // flat fan + a tilted fan crossing it = a full-looking branch
                    leaves.Card(mid, dir, side, L, L * 0.62f, nrm, col, 0.25f);
                    Vector3 up2 = (Vector3.up + side * 0.0f).normalized;
                    leaves.Card(mid + Vector3.up * 0.04f, dir, up2, L, L * 0.5f, nrm, col, 0.25f);
                    // far LOD: fewer, bigger
                    if (b % 2 == 0) { leavesF.Card(mid, dir, side, L * 1.4f, L * 1.0f, nrm, col, 0.25f); leavesF.Card(mid + Vector3.up * 0.05f, dir, Vector3.up, L * 1.3f, L * 0.8f, nrm, col, 0.25f); }
                }
            }
            for (int k = 0; k < 3; k++)
            {
                float a = k * 2.094f; Vector3 dir = new Vector3(Mathf.Cos(a), 0.0f, Mathf.Sin(a));
                leaves.Card(new Vector3(0, topH - 0.45f, 0), dir, Vector3.up, 0.5f, 1.0f, Vector3.up, new Color(0.9f, 1f, 0.9f, 0.45f), 0.3f, new Vector2(0.5f, 0), new Vector2(1f, 1f));
            }
            leavesF.Card(new Vector3(0, topH - 0.6f, 0), Vector3.right, Vector3.up, 0.9f, 1.4f, Vector3.up, new Color(0.9f, 1f, 0.9f, 0.4f), 0.3f);
            return new PlantMeshes { trunk = trunk.Build("ptrunk" + seed), leaves = leaves.Build("pleaves" + seed), trunkFar = trunkF.Build("ptrunkF" + seed), leavesFar = leavesF.Build("pleavesF" + seed), height = topH + 0.5f };
        }


        // ------------------------------------------------------------------ date palm: ringed curved trunk + arching leaflet fronds
        public static PlantMeshes Palm(Vector3 baseUnity, Vector3 topUnity, float[] ry, float[] droop, int seed)
        {
            var r = new Mulberry32(seed); var trunk = new MeshAcc(); var fronds = new MeshAcc();
            Vector3 b = baseUnity, top = topUnity; Vector3 mid = new Vector3(b.x, b.y + (top.y - b.y) * 0.6f, b.z);
            int rings = 22; var pts = new Vector3[rings]; var rad = new float[rings];
            for (int i = 0; i < rings; i++)
            {
                float t = (float)i / (rings - 1); float it = 1 - t;
                pts[i] = it * it * b + 2 * it * t * mid + t * t * top;
                float bump = 1f + 0.09f * Mathf.Sin(t * (top.y - b.y) * 9f);        // leaf-scar rings
                rad[i] = (0.24f * (1f + 0.55f * Mathf.Exp(-t * 14f)) * (1f - 0.35f * t)) * bump;
            }
            trunk.Tube(pts, rad, 10, 0.7f, 0.95f, 0f, 0.12f, 1f);
            int nf = ry.Length + 6;
            for (int k = 0; k < nf; k++)
            {
                float yaw = k < ry.Length ? -ry[k] : r.Next() * Mathf.PI * 2; float dr = k < ry.Length ? droop[k] : 0.3f + r.Next() * 0.4f;
                float L = 3.2f + r.Next() * 0.9f; Vector3 dir = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw));
                Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
                int segs = 6; Vector3 prev = top + Vector3.up * 0.1f;
                float rise = 0.55f + (1f - dr) * 0.7f;
                for (int s = 1; s <= segs; s++)
                {
                    float t = (float)s / segs;
                    Vector3 p = top + Vector3.up * 0.1f + dir * (t * L) + Vector3.up * (Mathf.Sin(t * Mathf.PI * 0.62f) * rise - dr * t * t * L * 0.55f);
                    Vector3 d = (p - prev); float seg = d.magnitude; Vector3 c = (p + prev) * 0.5f;
                    float wd = 1.45f * (1f - 0.45f * t);
                    float ao = Mathf.Lerp(0.6f, 1.05f, t); Color col = new Color(ao, ao, ao, 0.12f + 0.45f * t);
                    fronds.Card(c, d, side, seg * 1.02f, wd, Vector3.up, col, 0.25f, new Vector2((float)(s - 1) / segs, 0), new Vector2(t, 1));
                    prev = p;
                }
            }
            return new PlantMeshes { trunk = trunk.Build("palmtrunk" + seed), leaves = fronds.Build("palmfronds" + seed), height = (top.y - b.y) + 3f };
        }

        // ------------------------------------------------------------------ bush
        public static Mesh Bush(int seed, int clusters, float size)
        {
            var r = new Mulberry32(seed); var m = new MeshAcc();
            Vector3 c = new Vector3(0, 0.45f, 0), R = new Vector3(1f, 0.75f, 1f);
            for (int i = 0; i < clusters; i++)
            {
                Vector3 d = RandDir(r); if (d.y < -0.15f) d.y = -d.y * 0.3f;
                float rad = 0.5f + r.Next() * 0.5f;
                Vector3 p = c + new Vector3(d.x * R.x * rad, Mathf.Max(0f, d.y) * R.y * rad + 0.1f, d.z * R.z * rad);
                LeafCluster(m, r, p, c, R, size, 0.3f);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2; Vector3 dir = new Vector3(Mathf.Cos(a), 0.9f, Mathf.Sin(a)).normalized;
                m.Tube(new[] { new Vector3(0, 0, 0), dir * 0.5f + Vector3.up * 0.1f }, new[] { 0.035f, 0.012f }, 4, 0.6f, 0.9f, 0f, 0.3f);
            }
            return m.Build("bush" + seed);
        }

        // ------------------------------------------------------------------ wildflower (petals tinted per instance, stem + centre fixed)
        public static void Flower(int seed, out Mesh petals, out Mesh stem)
        {
            var r = new Mulberry32(seed); var pm = new MeshAcc(); var sm = new MeshAcc();
            float h = 0.32f;
            Color stemC = new Color(0.16f, 0.4f, 0.12f, 0.7f);
            for (int k = 0; k < 2; k++)
            {
                Vector3 rt = k == 0 ? Vector3.right : Vector3.forward;
                sm.Card(new Vector3(0, h * 0.5f, 0), rt, Vector3.up, 0.018f, h, Vector3.up, new Color(stemC.r, stemC.g, stemC.b, 0.05f), 0.9f);
            }
            // leaves on the stem
            sm.Card(new Vector3(0.05f, 0.1f, 0), new Vector3(0.7f, 0.4f, 0), Vector3.Cross(Vector3.forward, new Vector3(0.7f, 0.4f, 0)), 0.12f, 0.05f, Vector3.up, new Color(0.2f, 0.5f, 0.15f, 0.2f), 0.5f);
            sm.Card(new Vector3(0, h + 0.012f, 0), Vector3.right, Vector3.forward, 0.045f, 0.045f, Vector3.up, new Color(0.95f, 0.75f, 0.15f, 0.8f), 0.2f);
            int petalsN = 6;
            for (int i = 0; i < petalsN; i++)
            {
                float a = (float)i / petalsN * Mathf.PI * 2 + r.Next() * 0.2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0.28f, Mathf.Sin(a)).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
                pm.Card(new Vector3(0, h, 0) + dir * 0.045f, dir, side, 0.09f, 0.055f, Vector3.up, new Color(1f, 1f, 1f, 0.8f), 0.2f);
            }
            petals = pm.Build("petals"); stem = sm.Build("stem");
        }

        // ------------------------------------------------------------------ rocks: welded icosphere, noise-displaced, with strata
        public static Mesh Rock(int seed, float stretch)
        {
            var verts = new List<Vector3>(); var tris = new List<int>(); var cache = new Dictionary<long, int>();
            float tt = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] b = { new Vector3(-1, tt, 0), new Vector3(1, tt, 0), new Vector3(-1, -tt, 0), new Vector3(1, -tt, 0), new Vector3(0, -1, tt), new Vector3(0, 1, tt), new Vector3(0, -1, -tt), new Vector3(0, 1, -tt), new Vector3(tt, 0, -1), new Vector3(tt, 0, 1), new Vector3(-tt, 0, -1), new Vector3(-tt, 0, 1) };
            foreach (var p in b) verts.Add(p.normalized);
            int[] f0 = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            var faces = new List<int>(f0);
            Func<int, int, int> mid = (a, c) => { long k = a < c ? ((long)a << 32) + c : ((long)c << 32) + a; int idx; if (cache.TryGetValue(k, out idx)) return idx; verts.Add(((verts[a] + verts[c]) * 0.5f).normalized); idx = verts.Count - 1; cache[k] = idx; return idx; };
            for (int s = 0; s < 3; s++)
            {
                var nf = new List<int>();
                for (int i = 0; i < faces.Count; i += 3) { int a = faces[i], bb = faces[i + 1], c = faces[i + 2]; int ab = mid(a, bb), bc = mid(bb, c), ca = mid(c, a); nf.AddRange(new[] { a, ab, ca, bb, bc, ab, c, ca, bc, ab, bc, ca }); }
                faces = nf;
            }
            float off = seed * 13.7f;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = verts[i];
                double n1 = Hv.Fbm(p.x * 1.6 + off, p.z * 1.6 + p.y * 1.1 - off, 4) - 0.5;
                double n2 = Hv.Fbm(p.x * 4.5 + off * 2, p.y * 4.5 + p.z * 3.0, 2) - 0.5;
                float rad = 1f + (float)n1 * 0.7f + (float)n2 * 0.16f + 0.035f * Mathf.Sin(p.y * 13f + (float)n1 * 6f);
                p *= rad;
                p.x *= stretch; if (p.y < 0) p.y *= 0.35f; else p.y *= 0.85f;
                verts[i] = p;
            }
            // flip winding: Unity front faces are clockwise
            for (int i = 0; i < faces.Count; i += 3) { tris.Add(faces[i]); tris.Add(faces[i + 2]); tris.Add(faces[i + 1]); }
            var uv = new List<Vector2>(); var col = new List<Color>();
            var m = new Mesh { name = "rock" + seed, indexFormat = IndexFormat.UInt16 };
            m.SetVertices(verts); m.SetTriangles(tris, 0); m.RecalculateNormals();
            var nrm = m.normals;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = verts[i], nn = nrm[i];
                float ax = Mathf.Abs(nn.x), ay = Mathf.Abs(nn.y), az = Mathf.Abs(nn.z);
                if (ay >= ax && ay >= az) uv.Add(new Vector2(p.x, p.z) * 0.8f); else if (ax >= az) uv.Add(new Vector2(p.z, p.y) * 0.8f); else uv.Add(new Vector2(p.x, p.y) * 0.8f);
                float ao = Mathf.Lerp(0.55f, 1.0f, Mathf.Clamp01(0.5f + nn.y * 0.5f + 0.2f)); // darker underneath
                col.Add(new Color(ao, ao, ao, 0f));
            }
            m.SetUVs(0, uv); m.SetColors(col); m.RecalculateBounds(); m.UploadMeshData(true);
            return m;
        }

        // ------------------------------------------------------------------ grass tuft: 7 tapered, curved blades
        public static Mesh GrassTuft(int seed)
        {
            var r = new Mulberry32(seed); var m = new MeshAcc();
            int blades = 11, segs = 3;
            for (int b = 0; b < blades; b++)
            {
                float h = 0.18f + r.Next() * 0.34f, w = 0.022f + r.Next() * 0.016f;
                float ang = r.Next() * Mathf.PI * 2, lean = 0.12f + r.Next() * 0.3f;
                float ox = (r.Next() - 0.5f) * 0.34f, oz = (r.Next() - 0.5f) * 0.34f;
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)); Vector3 side = new Vector3(-Mathf.Sin(ang), 0, Mathf.Cos(ang));
                int o = m.v.Count;
                for (int s = 0; s <= segs; s++)
                {
                    float t = (float)s / segs; float ww = w * (1f - t * 0.92f);
                    Vector3 c = new Vector3(ox, t * h, oz) + dir * (lean * t * t);
                    Vector3 nrm = Vector3.Lerp(Vector3.up, Vector3.Cross(side, Vector3.up), 0.25f).normalized;
                    Color col = new Color(0.30f + t * 0.36f, 0.42f + t * 0.40f, 0.15f + t * 0.14f, t * t);
                    m.v.Add(c - side * ww); m.n.Add(Vector3.up); m.uv.Add(new Vector2(0, t)); m.c.Add(col);
                    m.v.Add(c + side * ww); m.n.Add(Vector3.up); m.uv.Add(new Vector2(1, t)); m.c.Add(col);
                }
                for (int s = 0; s < segs; s++) { int a = o + s * 2; m.t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            }
            return m.Build("grass");
        }
    }
}
