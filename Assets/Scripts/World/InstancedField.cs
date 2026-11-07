using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    public sealed class InstPart
    {
        public Mesh mesh; public Material mat; public bool castShadow; public int lod = 2; // 0 near, 1 far, 2 all
        internal List<Batch> normalB = new List<Batch>(), shadowB = new List<Batch>();
    }
    internal sealed class Batch
    {
        public Matrix4x4[] m = new Matrix4x4[1023]; public Vector4[] c = new Vector4[1023]; public int n; public MaterialPropertyBlock mpb = new MaterialPropertyBlock();
    }

    /// <summary>
    /// Port of instancing.js CulledInstances: draws many copies of a mesh with GPU instancing, but
    /// each frame only the copies that matter: in view (or near enough to matter), detailed model up
    /// close and a lighter one far away, shadows only inside a radius around the player.
    /// </summary>
    public sealed class InstancedField
    {
        readonly InstPart[] parts; readonly int n;
        readonly Matrix4x4[] mats; readonly Vector4[] cols; readonly float[] pos; // x,y,z,r per item
        readonly float lod2, max2, shadowR2, alwaysR2 = 60f * 60f;
        readonly Plane[] planes = new Plane[6];
        Vector3 lastCam, lastFocus; Quaternion lastRot; bool dirty = true;

        public InstancedField(InstPart[] parts, List<Matrix4x4> m, List<Vector4> col, List<Vector4> bounds, float lodDist = 140, float maxDist = float.PositiveInfinity, float shadowRadius = 0)
        {
            this.parts = parts; n = m.Count; mats = m.ToArray(); cols = col.ToArray();
            pos = new float[n * 4]; for (int i = 0; i < n; i++) { pos[i * 4] = bounds[i].x; pos[i * 4 + 1] = bounds[i].y; pos[i * 4 + 2] = bounds[i].z; pos[i * 4 + 3] = bounds[i].w; }
            lod2 = lodDist * lodDist; max2 = float.IsInfinity(maxDist) ? float.MaxValue : maxDist * maxDist; shadowR2 = shadowRadius * shadowRadius;
            foreach (var p in parts) if (p.mat != null) p.mat.enableInstancing = true;
        }

        public int Count { get { return n; } }

        static Batch Next(List<Batch> list, ref int cursor)
        {
            if (cursor >= list.Count) list.Add(new Batch());
            return list[cursor];
        }

        void Cull(Camera cam, Vector3 focus)
        {
            Vector3 cp = cam.transform.position; Quaternion cr = cam.transform.rotation;
            if (!dirty && (cp - lastCam).sqrMagnitude < 0.06f && Quaternion.Angle(cr, lastRot) < 0.25f && (focus - lastFocus).sqrMagnitude < 0.06f) return;
            dirty = false; lastCam = cp; lastRot = cr; lastFocus = focus;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            // working lists per part (normal + shadow groups)
            int np = parts.Length;
            var curN = new int[np]; var curS = new int[np];
            var bN = new Batch[np]; var bS = new Batch[np];
            for (int k = 0; k < np; k++) { foreach (var b in parts[k].normalB) b.n = 0; foreach (var b in parts[k].shadowB) b.n = 0; }
            for (int i = 0; i < n; i++)
            {
                float x = pos[i * 4], y = pos[i * 4 + 1], z = pos[i * 4 + 2], r = pos[i * 4 + 3];
                float dx = x - cp.x, dy = y - cp.y, dz = z - cp.z; float d2 = dx * dx + dy * dy + dz * dz;
                if (d2 > max2) continue;
                bool inShadow = shadowR2 > 0 && (x - focus.x) * (x - focus.x) + (z - focus.z) * (z - focus.z) < shadowR2;
                if (!inShadow && d2 > alwaysR2)
                {
                    bool vis = true; Vector3 c = new Vector3(x, y, z);
                    for (int p = 0; p < 6; p++) if (planes[p].GetDistanceToPoint(c) < -r) { vis = false; break; }
                    if (!vis) continue;
                }
                bool far = d2 > lod2;
                for (int k = 0; k < np; k++)
                {
                    var part = parts[k];
                    if ((part.lod == 0 && far) || (part.lod == 1 && !far)) continue;
                    bool useShadow = part.castShadow && inShadow;
                    var list = useShadow ? part.shadowB : part.normalB;
                    ref int cur = ref (useShadow ? ref curS[k] : ref curN[k]);
                    var batches = useShadow ? bS : bN;
                    var b = batches[k];
                    if (b == null || b.n >= 1023)
                    {
                        if (b != null) cur++;
                        b = Next(list, ref cur); batches[k] = b; b.n = 0;
                    }
                    b.m[b.n] = mats[i]; b.c[b.n] = cols[i]; b.n++;
                }
            }
            for (int k = 0; k < np; k++)
            {
                foreach (var b in parts[k].normalB) if (b.n > 0) b.mpb.SetVectorArray("_InstColor", b.c);
                foreach (var b in parts[k].shadowB) if (b.n > 0) b.mpb.SetVectorArray("_InstColor", b.c);
            }
        }

        public void Render(Camera cam, Vector3 focus)
        {
            if (cam == null || n == 0) return;
            Cull(cam, focus);
            foreach (var p in parts)
            {
                foreach (var b in p.normalB) if (b.n > 0) Graphics.DrawMeshInstanced(p.mesh, 0, p.mat, b.m, b.n, b.mpb, ShadowCastingMode.Off, true, 0);
                foreach (var b in p.shadowB) if (b.n > 0) Graphics.DrawMeshInstanced(p.mesh, 0, p.mat, b.m, b.n, b.mpb, ShadowCastingMode.On, true, 0);
            }
        }
    }
}
