using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hearthvale
{
    public sealed class SolidBox
    {
        public float minX, maxX, minZ, maxZ, top; public float? bottom; public bool off; public int door = -1;
    }
    public sealed class CircleCol { public float x, z, r, h = 2f; public bool off; }
    public struct PickupSpot { public Vector3 p; public bool fixedY; }
    public struct VehicleSpot { public float x, z, ry; }
    public sealed class BuildingInfo { public string type; public float x, z; public int f; public float W, D, FH; public int floors; public float yawIn; }
    public sealed class DoorSpec
    {
        public Vector3 hinge, center; public float yaw, width, height; public bool roller, locked; public SolidBox box;
    }

    /// <summary>
    /// Port of World.buildTown / addBuilding (world.js): the Old Quarter.
    /// The random-number call order is kept identical to the JS version, so every building,
    /// door, window and rooftop lands exactly where it did in the browser game.
    /// All maths here is in JS (right-handed) space; MeshBatch mirrors z when emitting.
    /// </summary>
    public sealed class TownBuilder
    {
        const float Y = WorldData.TownHeight;
        const int TownR = WorldData.TownR;

        public readonly Mulberry32 rng;
        public readonly MeshBatch B = new MeshBatch();
        public readonly List<SolidBox> boxes = new List<SolidBox>();
        public readonly List<CircleCol> circles = new List<CircleCol>();
        public readonly List<PickupSpot> pickupSpots = new List<PickupSpot>();
        public readonly List<VehicleSpot> vehicleSpots = new List<VehicleSpot>();
        public readonly List<Vector2> townTreeSpots = new List<Vector2>();
        public readonly List<DoorSpec> doors = new List<DoorSpec>();
        public readonly List<BuildingInfo> buildings = new List<BuildingInfo>();
        public sealed class PalmSpec { public float x, z, hgt; public Vector3 top; public float[] ry, droop; }
        public readonly List<PalmSpec> palms = new List<PalmSpec>();
        public readonly List<Vector3> poleSpots = new List<Vector3>(); // x, z, face
        public byte[] districtOpen;
        public Vector3 spawnPoint = new Vector3(0, Y + 2, 12);

        public TownBuilder(Mulberry32 rng) { this.rng = rng; }

        T Pick<T>(T[] arr) { return arr[(int)Math.Floor(rng.NextD() * arr.Length)]; }
        void Solid(float minX, float maxX, float minZ, float maxZ, float top, float? bottom = null)
        { boxes.Add(new SolidBox { minX = minX, maxX = maxX, minZ = minZ, maxZ = maxZ, top = top, bottom = bottom }); }

        void BlockAt(string key, float x, float y0, float z, float w, float h, float d, string color = "#ffffff", float tile = 0, bool isSolid = false)
        {
            B.Add(key, Geo.Box(w, h, d), M.T(x, y0 + h / 2, z), color, tile);
            if (isSolid) Solid(x - w / 2, x + w / 2, z - d / 2, z + d / 2, y0 + h, y0 > Y + 0.6f ? (float?)y0 : null);
        }

        bool inOpenRects(float x, float z) { foreach (var q in WorldData.OpenRects) if (q.Contains(x, z)) return true; return false; }

        public void Build()
        {
            int N = TownR * 2;
            var open = new byte[N * N]; var used = new byte[N * N];
            for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
                {
                    float x = i - TownR + 0.5f, z = j - TownR + 0.5f;
                    if (inOpenRects(x, z)) open[j * N + i] = 1;
                }
            districtOpen = open;
            Func<float, float, bool> isOpen = (x, z) =>
            {
                int i = (int)Math.Floor(x + TownR), j = (int)Math.Floor(z + TownR);
                return i < 0 || j < 0 || i >= N || j >= N || open[j * N + i] == 1;
            };
            Func<int, int, bool> free = (i, j) => i >= 0 && j >= 0 && i < N && j < N && open[j * N + i] == 0 && used[j * N + i] == 0;

            // --- ground: packed sandy earth, cobbled lanes, flagstone sites
            BlockAt("dirt", 0, Y - 0.06f, 0, N + 12, 0.1f, N + 12, "#dcc69c", 7);
            var laneRects = new List<Rect2>(WorldData.RoadRects); laneRects.AddRange(WorldData.Alleys);
            foreach (var q in laneRects)
                BlockAt("cobble", (q.minX + q.maxX) / 2, Y - 0.02f, (q.minZ + q.maxZ) / 2, q.maxX - q.minX, 0.09f, q.maxZ - q.minZ, "#e3d3b4", 1.8f);
            foreach (var s in WorldData.Sites)
                BlockAt("pave", (s.minX + s.maxX) / 2, Y - 0.02f, (s.minZ + s.maxZ) / 2, s.maxX - s.minX, 0.12f, s.maxZ - s.minZ, "#f3d3a2", 3);
            foreach (var r in WorldData.Roads)
            {
                var q = WorldData.RoadRect(r);
                if (r.o == 'e') BlockAt("stone", (q.minX + q.maxX) / 2, Y, r.at, q.maxX - q.minX, 0.1f, 0.6f, "#c2ae8c", 1.5f);
                else BlockAt("stone", r.at, Y, (q.minZ + q.maxZ) / 2, 0.6f, 0.1f, q.maxZ - q.minZ, "#c2ae8c", 1.5f);
            }

            // --- enterable buildings (loot inside), each with its door on open ground
            string[] sand = { "#e2c9a0", "#d9bc8f", "#e6d2ae", "#cfb187", "#dcc19a", "#d4b896" };
            string[] wash = { "#ece4d4", "#e9ddc8", "#f0e9dc", "#dfd3bf" };
            var specs = new Dictionary<string, int[]> { { "apartment", new[] { 12, 11 } }, { "store", new[] { 13, 10 } }, { "warehouse", new[] { 17, 12 } }, { "house", new[] { 10, 9 } } };
            string[] order = { "apartment", "store", "warehouse", "house", "apartment", "store", "house", "apartment", "store", "warehouse", "house", "apartment",
                "house", "store", "apartment", "house", "store", "apartment", "house", "house", "store", "apartment", "house", "house", "apartment", "house" };
            int[][] FRONT = { new[] { 0, 1 }, new[] { 1, 0 }, new[] { 0, -1 }, new[] { -1, 0 } };
            int placed = 0;
            for (int t = 0; t < 9000 && placed < order.Length; t++)
            {
                string type = order[placed]; int W = specs[type][0], D = specs[type][1];
                int f = (int)Math.Floor(rng.NextD() * 4);
                int sx = f % 2 == 1 ? D : W, sz = f % 2 == 1 ? W : D;
                int i0 = (int)Math.Floor(rng.NextD() * (N - sx)), j0 = (int)Math.Floor(rng.NextD() * (N - sz));
                bool ok = true;
                for (int j = j0; j < j0 + sz && ok; j++) for (int i = i0; i < i0 + sx; i++) if (!free(i, j)) { ok = false; break; }
                if (!ok) continue;
                float cx = i0 - TownR + sx / 2f, cz = j0 - TownR + sz / 2f; int fx = FRONT[f][0], fz = FRONT[f][1];
                float reach = (f % 2 == 1 ? sx : sz) / 2f;
                for (int a = 1; a <= 3 && ok; a++) for (float s = -1.5f; s <= 1.5f; s += 1.5f)
                    {
                        float px = cx + fx * (reach + a - 0.5f) + (fz != 0 ? s : 0), pz = cz + fz * (reach + a - 0.5f) + (fx != 0 ? s : 0);
                        if (!isOpen(px, pz)) { ok = false; break; }
                    }
                if (!ok) continue;
                for (int j = j0; j < j0 + sz; j++) for (int i = i0; i < i0 + sx; i++) used[j * N + i] = 1;
                AddBuilding(type, cx, cz, f, sand);
                placed++;
            }

            // --- pack the rest of the built-up ground with solid stone blocks of varied height
            var blocks = new List<int[]>();
            for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
                {
                    if (!free(i, j)) continue;
                    int tw = 6 + (int)Math.Floor(rng.NextD() * 8), td = 6 + (int)Math.Floor(rng.NextD() * 8);
                    int w = 0; while (w < tw && free(i + w, j)) w++;
                    int d = 1;
                    bool stop = false;
                    while (d < td && !stop) { for (int k = 0; k < w; k++) if (!free(i + k, j + d)) { stop = true; break; } if (!stop) d++; }
                    for (int jj = j; jj < j + d; jj++) for (int ii = i; ii < i + w; ii++) used[jj * N + ii] = 1;
                    blocks.Add(new[] { i - TownR, j - TownR, w, d });
                }
            string[] shutterCols = { "#3e6f7a", "#2f5d75", "#6b4a32", "#55703f", "#8a5a3a", "#4a5f80", "#7a3f35" };
            string[] doorCols = { "#b07a4c", "#7fa3ad", "#8aa37a", "#c29256", "#7f97b8", "#a8664f" };
            string[] clothCols = { "#b5483a", "#3b6e8f", "#d9a441", "#6d8f4e", "#e8e0d0", "#8c3f5f" };
            const float STOREY = 3.1f;
            foreach (var blk in blocks)
            {
                float x0 = blk[0], z0 = blk[1], w = blk[2], d = blk[3];
                float cx = x0 + w / 2, cz = z0 + d / 2;
                bool small = w < 4 || d < 4;
                int storeys = small ? 1 : 1 + (int)Math.Floor(rng.NextD() * 3);
                float h = 1.5f + storeys * STOREY;
                bool washed = rng.NextD() < 0.3;
                BlockAt(washed ? "plaster" : "sandstone", cx, Y, cz, w, h, d, washed ? Pick(wash) : Pick(sand), washed ? 2.5f : 4, true);
                BlockAt("stone", cx, Y, cz, w + 0.16f, 0.7f, d + 0.16f, "#b39b78", 2);
                BlockAt("stone", cx, Y + h - 0.45f, cz, w + 0.3f, 0.22f, d + 0.3f, "#cdb895", 2);
                foreach (int s in new[] { -1, 1 })
                {
                    BlockAt("stone", cx, Y + h, cz + s * (d / 2 - 0.12f), w, 0.45f, 0.24f, "#cdb895", 2);
                    BlockAt("stone", cx + s * (w / 2 - 0.12f), Y + h, cz, 0.24f, 0.45f, d - 0.48f, "#cdb895", 2);
                }
                // rooftop clutter
                if (!small && rng.NextD() < 0.45)
                {
                    double r = rng.NextD();
                    if (r < 0.4)
                    {
                        B.Add("metal", Geo.Cylinder(0.7f, 0.7f, 1.4f, 14), M.T(cx + w * 0.2f, Y + h + 1.2f, cz - d * 0.15f), "#a3a7ab");
                        foreach (var l in new[] { new[] { -0.5f, -0.5f }, new[] { 0.5f, 0.5f }, new[] { -0.5f, 0.5f }, new[] { 0.5f, -0.5f } })
                            BlockAt("metal", cx + w * 0.2f + l[0], Y + h, cz - d * 0.15f + l[1], 0.08f, 0.5f, 0.08f, "#3a3a3e");
                    }
                    else if (r < 0.75) BlockAt(washed ? "plaster" : "sandstone", cx - w * 0.2f, Y + h, cz + d * 0.15f, 2.4f, 2.4f, 2.4f, washed ? Pick(wash) : Pick(sand), 4, true);
                    else BlockAt("metal", cx, Y + h, cz, 1.4f, 0.9f, 1.0f, "#9aa0a8");
                }
                if (small) continue;

                // facades: only faces that look onto open ground get windows, doors and clutter
                var faces = new[]
                {
                    new Face(0, 1, x0 + w / 2, z0 + d, w, 0),
                    new Face(0, -1, x0 + w / 2, z0, w, Mathf.PI),
                    new Face(1, 0, x0 + w, z0 + d / 2, d, Mathf.PI / 2),
                    new Face(-1, 0, x0, z0 + d / 2, d, -Mathf.PI / 2),
                };
                foreach (var fc in faces)
                {
                    Matrix4x4 baseM = M.SetPos(M.RotY(fc.ry), fc.cx, Y, fc.cz);
                    Action<string, float, float, float, float, float, float, string, float, Matrix4x4?> put = (key, gw, gh, gd, lx, ly, lz, color, tile, rot) =>
                    {
                        Matrix4x4 local = rot.HasValue ? M.SetPos(rot.Value, lx, ly, lz) : M.T(lx, ly, lz);
                        B.Add(key, Geo.Box(gw, gh, gd), baseM * local, color, tile, key != "glass" && key != "glassDark");
                    };
                    int slots = (int)Math.Floor((fc.len - 1) / 3);
                    string shutter = Pick(shutterCols);
                    for (int s = 0; s < slots; s++)
                    {
                        float lx = -fc.len / 2 + (fc.len - slots * 3) / 2 + 1.5f + s * 3;
                        float wx = fc.cx + (fc.nz != 0 ? lx * (fc.ry != 0 ? -1 : 1) : 0), wz = fc.cz + (fc.nx != 0 ? -lx * fc.nx : 0);
                        if (!isOpen(wx + fc.nx * 0.6f, wz + fc.nz * 0.6f)) continue;
                        for (int k = 0; k < storeys; k++)
                        {
                            float fy = k * STOREY;
                            if (k == 0 && rng.NextD() < 0.3)
                            {
                                string dc = Pick(doorCols);
                                put("wood", 1.3f, 2.3f, 0.12f, lx, fy + 1.15f, 0.1f, dc, 1, null);
                                foreach (float py in new[] { 0.55f, 1.6f }) put("wood", 1.0f, 0.75f, 0.04f, lx, fy + py, 0.17f, dc, 1, null);
                                put("metal", 0.08f, 0.08f, 0.08f, lx + 0.45f, fy + 1.1f, 0.2f, "#c9a648", 0, null);
                                put("stone", 1.8f, 0.3f, 0.3f, lx, fy + 2.45f, 0.12f, "#bfa784", 1.5f, null);
                                foreach (float sx in new[] { -0.8f, 0.8f }) put("stone", 0.24f, 2.3f, 0.3f, lx + sx, fy + 1.15f, 0.12f, "#bfa784", 1.5f, null);
                                if (rng.NextD() < 0.4) put("porch", 0.22f, 0.3f, 0.18f, lx + 1.1f, fy + 2.3f, 0.12f, "#ffffff", 0, null);
                                if (rng.NextD() < 0.45) put("cloth", 2.3f, 0.05f, 1.2f, lx, fy + 2.85f, 0.6f, Pick(clothCols), 0, M.RotX(0.32f));
                                continue;
                            }
                            if (rng.NextD() < 0.2) continue;
                            float wy = fy + (k == 0 ? 1.0f : 0.6f);
                            put(rng.NextD() < 0.3 ? "glass" : "glassDark", 1.0f, 1.4f, 0.06f, lx, wy + 0.7f, 0.01f, "#ffffff", 0, null);
                            put("stone", 1.4f, 0.2f, 0.2f, lx, wy + 1.5f, 0.08f, "#c4ad8a", 1.5f, null);
                            put("stone", 1.3f, 0.1f, 0.26f, lx, wy - 0.05f, 0.1f, "#c4ad8a", 1.5f, null);
                            if (rng.NextD() < 0.55) foreach (float sx in new[] { -0.78f, 0.78f }) put("wood", 0.52f, 1.4f, 0.05f, lx + sx, wy + 0.7f, 0.06f, shutter, 1, null);
                            if (k == 0 && rng.NextD() < 0.5) foreach (float bx in new[] { -0.3f, 0f, 0.3f }) put("metal", 0.04f, 1.4f, 0.04f, lx + bx, wy + 0.7f, 0.1f, "#2a2a2e", 0, null);
                            if (k > 0 && rng.NextD() < 0.18)
                            {
                                put("stone", 1.9f, 0.14f, 0.9f, lx, wy - 0.12f, 0.45f, "#c4ad8a", 1.5f, null);
                                put("metal", 1.9f, 0.05f, 0.05f, lx, wy + 0.85f, 0.88f, "#2a2a2e", 0, null);
                                for (float b = -0.9f; b <= 0.91f; b += 0.3f) put("metal", 0.03f, 0.9f, 0.03f, lx + b, wy + 0.4f, 0.88f, "#2a2a2e", 0, null);
                            }
                            if (k > 0 && rng.NextD() < 0.1) put("metal", 0.9f, 0.6f, 0.5f, lx + 1.2f, wy, 0.25f, "#b9bcc0", 0, null);
                        }
                    }
                    if (fc.len > 8 && rng.NextD() < 0.35) put("metal", 0.12f, h - 0.4f, 0.12f, fc.len / 2 - 0.4f, (h - 0.4f) / 2, 0.1f, "#6d6a66", 0, null);
                }
            }

            // --- the covered tunnel: stone roof on timber beams, hanging lamps
            foreach (var q in WorldData.Alleys) if (q.roof)
                {
                    float w = q.maxX - q.minX, d = q.maxZ - q.minZ, cx = (q.minX + q.maxX) / 2, cz = (q.minZ + q.maxZ) / 2;
                    BlockAt("stone", cx, Y + 4.2f, cz, w, 0.45f, d + 0.4f, "#a8916f", 2, true);
                    for (float x = q.minX + 1.5f; x < q.maxX; x += 3) BlockAt("wood", x, Y + 3.9f, cz, 0.3f, 0.3f, d, "#6b4f36", 1);
                    for (float x = q.minX + 4; x < q.maxX - 2; x += 9)
                    {
                        BlockAt("metal", x, Y + 3.55f, cz, 0.04f, 0.4f, 0.04f, "#2a2a2e");
                        BlockAt("porch", x, Y + 3.3f, cz, 0.3f, 0.25f, 0.3f);
                    }
                    pickupSpots.Add(new PickupSpot { p = new Vector3(cx - 8, Y + 1, cz) });
                }

            // --- Mid gate: a chokepoint wall with a narrow doorway and swung-open doors
            float gz = 2;
            BlockAt("sandstone", -2.4f, Y, gz, 2.2f, 4.8f, 0.8f, "#d6bb92", 4, true);
            BlockAt("sandstone", 2.4f, Y, gz, 2.2f, 4.8f, 0.8f, "#d6bb92", 4, true);
            BlockAt("stone", 0, Y + 3.0f, gz, 2.6f, 1.8f, 0.9f, "#c9b28c", 2, true);
            BlockAt("wood", -1.22f, Y + 0.05f, gz + 1.05f, 0.08f, 2.9f, 1.25f, "#5c4330", 1);
            BlockAt("wood", 1.22f, Y + 0.05f, gz + 1.05f, 0.08f, 2.9f, 1.25f, "#5c4330", 1);
            BlockAt("stone", 6, Y + 3.4f, -8, 5, 1.4f, 5.4f, "#c9b28c", 2, true); // archway over the A Short entrance

            // --- cover props
            Action<float, float, float, int> crate = (x, z, s, stack) =>
            {
                for (int k = 0; k < stack; k++)
                {
                    float y0 = Y + k * s;
                    BlockAt("wood", x, y0, z, s, s, s, "#e3c48f", 1.2f);
                    foreach (float yy in new[] { 0.04f, s - 0.16f }) BlockAt("wood", x, y0 + yy, z, s + 0.04f, 0.12f, s + 0.04f, "#a07c52", 1.2f);
                }
                Solid(x - s / 2, x + s / 2, z - s / 2, z + s / 2, Y + s * stack);
            };
            Action<float, float, float, bool> barrier = (x, z, len, alongX) =>
            {
                float w = alongX ? len : 0.5f, d = alongX ? 0.5f : len;
                BlockAt("concrete", x, Y, z, alongX ? len : 0.8f, 0.3f, alongX ? 0.8f : len, "#bdb6aa", 1.5f);
                BlockAt("concrete", x, Y + 0.3f, z, w, 0.8f, d, "#c6c0b4", 1.5f);
                Solid(x - (alongX ? len : 0.8f) / 2, x + (alongX ? len : 0.8f) / 2, z - (alongX ? 0.8f : len) / 2, z + (alongX ? 0.8f : len) / 2, Y + 1.1f);
            };
            Action<float, float, float, bool> sandbags = (x, z, len, alongX) =>
            {
                for (int row = 0; row < 3; row++)
                {
                    int n = (int)Math.Floor(len / 0.7f);
                    for (int k = 0; k < n; k++)
                    {
                        float u = -len / 2 + 0.35f + k * 0.7f + (row % 2 == 1 ? 0.35f : 0);
                        if (u > len / 2 - 0.3f) continue;
                        BlockAt("cloth", alongX ? x + u : x, Y + row * 0.28f, alongX ? z : z + u, alongX ? 0.66f : 0.42f, 0.27f, alongX ? 0.42f : 0.66f, "#b3a27a");
                    }
                }
                Solid(x - (alongX ? len / 2 : 0.25f), x + (alongX ? len / 2 : 0.25f), z - (alongX ? 0.25f : len / 2), z + (alongX ? 0.25f : len / 2), Y + 0.85f);
            };
            Action<float, float, float, bool, float> lowWall = (x, z, len, alongX, h) =>
                BlockAt("stone", x, Y, z, alongX ? len : 0.6f, h, alongX ? 0.6f : len, "#c8b18c", 2, true);
            Action<float, float, string> barrel = (x, z, color) =>
            {
                B.Add("metal", Geo.Cylinder(0.42f, 0.42f, 1.05f, 16), M.T(x, Y + 0.525f, z), color);
                circles.Add(new CircleCol { x = x, z = z, r = 0.45f, h = 1.05f });
            };
            Action<string, float, float, float, float, float, bool> decal = (key, x, y, z, size, ry, flat) =>
            {
                Matrix4x4 mx = flat ? M.RotY(ry) * M.RotX(-Mathf.PI / 2) : M.RotY(ry);
                B.Add(key, Geo.Plane(size, size), M.SetPos(mx, x, y, z));
            };
            Action<string, float, float, bool> markerWall = (site, x, z, alongX) =>
            {
                BlockAt("sandstone", x, Y, z, alongX ? 4.6f : 0.7f, 2.5f, alongX ? 0.7f : 4.6f, "#d9c09a", 4, true);
                string key = "decal" + site;
                if (alongX) { decal(key, x, Y + 1.3f, z + 0.37f, 2.2f, 0, false); decal(key, x, Y + 1.3f, z - 0.37f, 2.2f, Mathf.PI, false); }
                else { decal(key, x + 0.37f, Y + 1.3f, z, 2.2f, Mathf.PI / 2, false); decal(key, x - 0.37f, Y + 1.3f, z, 2.2f, -Mathf.PI / 2, false); }
            };

            // Site A
            markerWall("A", 38, -40, true);
            decal("decalA", 38, Y + 0.12f, -32, 6, 0, true);
            crate(30, -46, 1.3f, 2); crate(31.3f, -46, 1.3f, 1); crate(30, -44.7f, 1.3f, 1);
            crate(46, -33, 1.6f, 1); crate(47.6f, -33, 1.3f, 2); crate(25, -29, 1.3f, 1);
            barrier(44, -50, 4, true); sandbags(35, -27, 3.2f, true); lowWall(51, -42, 6, false, 1.15f);
            barrel(22, -56, "#3f6b4a"); barrel(23, -55.1f, "#8a4a32"); barrel(54.5f, -24, "#3f5f7a");
            // Site B
            markerWall("B", -37, -44, false);
            decal("decalB", -44, Y + 0.12f, -50, 6, 0, true);
            crate(-46, -50, 1.3f, 2); crate(-44.7f, -50, 1.3f, 1); crate(-28, -36, 1.6f, 1); crate(-29.6f, -36, 1.3f, 1); crate(-30, -52, 1.3f, 2);
            barrier(-38, -34.5f, 4, true); sandbags(-50, -38, 3.2f, false); lowWall(-24, -46, 5, false, 1.15f);
            barrel(-52, -56, "#8a4a32"); barrel(-51.1f, -56.8f, "#3f6b4a");
            // lanes: break up the long sightlines
            crate(2, 38, 1.3f, 1); crate(-2, -30, 1.3f, 2); crate(-0.7f, -30, 1.3f, 1);
            crate(-56, 6, 1.3f, 2); crate(-56, 7.3f, 1.3f, 1);
            crate(-56, 24, 1.3f, 1); sandbags(-58, 52, 2.8f, true);
            barrier(-58, 34, 3, true);
            // the well in Well Yard
            B.Add("stone", Geo.Cylinder(1.3f, 1.4f, 0.9f, 20), M.T(26, Y + 0.45f, 31), "#c2aa86", 1.5f);
            B.Add("fountainWater", Geo.Cylinder(1.05f, 1.05f, 0.05f, 20), M.T(26, Y + 0.55f, 31));
            foreach (int s in new[] { -1, 1 }) BlockAt("wood", 26 + s * 1.15f, Y + 0.9f, 31, 0.14f, 1.6f, 0.14f, "#6b4f36", 1);
            BlockAt("wood", 26, Y + 2.45f, 31, 2.6f, 0.12f, 0.14f, "#6b4f36", 1);
            circles.Add(new CircleCol { x = 26, z = 31, r = 1.45f, h = 1.5f });
            crate(20, 23.5f, 1.3f, 1); crate(32, 38, 1.3f, 2);

            // --- palms in the gate yards, olive trees in the courtyards
            string[] frondCols = { "#5d7d34", "#6b8a3a", "#56722f" };
            Action<float, float, float> palm = (x, z, hgt) =>
            {
                float a = (float)(rng.NextD() * Math.PI * 2), lean = (float)(0.6 + rng.NextD() * 0.8);
                Vector3 top = new Vector3(x + Mathf.Cos(a) * lean, Y + hgt, z + Mathf.Sin(a) * lean);
                var spec = new PalmSpec { x = x, z = z, hgt = hgt, top = top, ry = new float[9], droop = new float[9] };
                for (int k = 0; k < 9; k++)
                {
                    spec.ry[k] = (float)(k / 9.0 * Math.PI * 2 + rng.NextD() * 0.3); spec.droop[k] = (float)(0.35 + rng.NextD() * 0.35);
                    Pick(frondCols); // keep the random sequence identical to the JS game (it picked a frond colour here)
                }
                palms.Add(spec);   // the realistic palm mesh is built later by NatureBuilder
                circles.Add(new CircleCol { x = x, z = z, r = 0.3f, h = hgt });
            };
            palm(-12, 76, 7.5f); palm(12, 75, 8.5f); palm(-9, -75, 8); palm(10, -76, 7);
            palm(-12, 68, 7); palm(13, -68, 7.5f);
            townTreeSpots.Add(new Vector2(-36, 36)); townTreeSpots.Add(new Vector2(-30, 43)); townTreeSpots.Add(new Vector2(31, 25));

            // --- sagging power cables strung across the lanes between rooftops
            foreach (var r in WorldData.Roads)
            {
                for (float t = r.a + 6; t < r.b - 6; t += 11)
                {
                    float hw = WorldData.RoadW / 2 + 0.2f;
                    float x1, z1, x2, z2;
                    if (r.o == 'e') { x1 = t; z1 = r.at - hw; x2 = t + 1.5f; z2 = r.at + hw; }
                    else { x1 = r.at - hw; z1 = t; x2 = r.at + hw; z2 = t + 1.5f; }
                    bool ns = r.o == 'n', ew = r.o == 'e';
                    if (isOpen(x1 + (ns ? -0.6f : 0), z1 + (ew ? -0.6f : 0)) || isOpen(x2 + (ns ? 0.6f : 0), z2 + (ew ? 0.6f : 0))) continue;
                    float y = Y + 4.3f + (float)(rng.NextD() * 0.2);
                    var mid = new Vector3((x1 + x2) / 2, y - 0.5f, (z1 + z2) / 2);
                    B.Add("metal", Geo.Tube(new Vector3(x1, y, z1), mid, new Vector3(x2, y, z2), 8, 0.025f, 4), null, "#1f1f22", 0, true);
                }
            }

            // --- loot in the open: sites, yards and lanes
            foreach (var p in new[] { new[] { 33, -36 }, new[] { 50, -52 }, new[] { -48, -44 }, new[] { -27, -54 }, new[] { 24, 34 }, new[] { -33, 40 }, new[] { 0, 50 }, new[] { 60, 0 }, new[] { -58, 0 }, new[] { 0, -48 } })
                pickupSpots.Add(new PickupSpot { p = new Vector3(p[0], Y + 1, p[1]) });
            // spawn in the south gate yard; hover-craft parked along the lanes
            spawnPoint = new Vector3(WorldData.Plaza.x, Y + 2, WorldData.Plaza.y);
            foreach (var v in new[] { new[] { 0f, 44, 0 }, new[] { 0f, -44, Mathf.PI }, new[] { -58f, 46, 0 }, new[] { -58f, -8, Mathf.PI }, new[] { 24f, 34, 0 }, new[] { -33f, 40, 0 } })
                vehicleSpots.Add(new VehicleSpot { x = v[0], z = v[1], ry = v[2] });

            BuildLampPostSpots();
        }

        struct Face { public int nx, nz; public float cx, cz, len, ry; public Face(int nx, int nz, float cx, float cz, float len, float ry) { this.nx = nx; this.nz = nz; this.cx = cx; this.cz = cz; this.len = len; this.ry = ry; } }

        // ------------------------------------------------------------ street lamps
        void BuildLampPostSpots()
        {
            foreach (var r in WorldData.Roads)
            {
                var cross = new List<float>();
                foreach (var c in WorldData.Roads) if (c.o != r.o && r.at >= c.a && r.at <= c.b && c.at >= r.a && c.at <= r.b) cross.Add(c.at);
                for (float t = r.a + 8; t <= r.b - 8; t += 22)
                {
                    bool near = false; foreach (var c in cross) if (Mathf.Abs(t - c) < 9) near = true;
                    if (near) continue;
                    foreach (int side in new[] { -1, 1 })
                    {
                        float off = side * (WorldData.RoadW / 2 - 0.55f);
                        float lx, lz, face;
                        if (r.o == 'e') { lx = t; lz = r.at + off; face = side > 0 ? Mathf.PI : 0; }
                        else { lx = r.at + off; lz = t; face = side > 0 ? -Mathf.PI / 2 : Mathf.PI / 2; }
                        bool blocked = false;
                        foreach (var b in boxes) if (lx > b.minX - 0.5f && lx < b.maxX + 0.5f && lz > b.minZ - 0.5f && lz < b.maxZ + 0.5f) { blocked = true; break; }
                        if (blocked) continue;
                        poleSpots.Add(new Vector3(lx, lz, face));
                    }
                }
            }
        }

        // ------------------------------------------------------------ enterable buildings
        void AddBuilding(string type, float x, float z, int f, string[] plaster)
        {
            const float T = 0.25f; // wall thickness
            float W, D, FH, door, doorH; int floors;
            switch (type)
            {
                case "apartment": W = 12; D = 11; floors = 2; FH = 3.4f; door = 1.6f; doorH = 2.4f; break;
                case "store": W = 13; D = 10; floors = 1; FH = 4.2f; door = 2.4f; doorH = 2.7f; break;
                case "warehouse": W = 17; D = 12; floors = 1; FH = 6.0f; door = 5.0f; doorH = 4.4f; break;
                default: W = 10; D = 9; floors = 1; FH = 3.4f; door = 1.3f; doorH = 2.3f; break;
            }
            string wallType = type == "warehouse" ? "siding" : rng.NextD() < 0.6 ? "sandstone" : "plaster";
            string wallCol = wallType == "siding"
                ? new[] { "#8e9aa6", "#9aa58f", "#a89a8a", "#8b8fa8" }[(int)Math.Floor(rng.NextD() * 4)]
                : plaster[(int)Math.Floor(rng.NextD() * plaster.Length)];
            float th = f * Mathf.PI / 2, cs = Mathf.Cos(th), sn = Mathf.Sin(th);
            Matrix4x4 baseM = M.SetPos(M.RotY(th), x, Y, z);
            var tw = new Dictionary<string, float> { { "stone", 2 }, { "sandstone", 4 }, { "plaster", 2.5f }, { "siding", 2.5f }, { "brick", 2.2f }, { "concrete", 1.5f }, { "wood", 1.2f }, { "metal", 0 }, { "trim", 0 } };

            Action<string, float, float, float, float, float, float, string, bool> part = (key, w, h, d, lx, ly, lz, color, solid) =>
            {
                float tile; tw.TryGetValue(key, out tile);
                B.Add(key, Geo.Box(w, h, d), baseM * M.T(lx, ly, lz), color, tile);
                if (!solid) return;
                float wx = x + lx * cs + lz * sn, wz = z - lx * sn + lz * cs;
                float hw = (f % 2 == 1 ? d : w) / 2, hd = (f % 2 == 1 ? w : d) / 2;
                float bottom = Y + ly - h / 2, topY = Y + ly + h / 2;
                var b = new SolidBox { minX = wx - hw, maxX = wx + hw, minZ = wz - hd, maxZ = wz + hd, top = topY };
                if (bottom > Y + 0.6f) b.bottom = bottom;
                boxes.Add(b);
            };
            Func<float, float, float, Vector3> toWorld = (lx, ly, lz) => new Vector3(x + lx * cs + lz * sn, Y + ly, z - lx * sn + lz * cs);
            buildings.Add(new BuildingInfo { type = type, x = x, z = z, f = f, W = W, D = D, FH = FH, floors = floors, yawIn = th });

            Action<string, float, float, List<Opening>> wall = (side, y0, h, openings) =>
            {
                bool alongX = side == "front" || side == "back";
                float len = alongX ? W : D - 2 * T;
                float v = side == "front" ? D / 2 - T / 2 : side == "back" ? -D / 2 + T / 2 : side == "right" ? W / 2 - T / 2 : -W / 2 + T / 2;
                Action<float, float, float, float> put = (u0, u1, a, b) =>
                {
                    if (u1 - u0 < 0.02f || b - a < 0.02f) return;
                    float uc = (u0 + u1) / 2, yc = y0 + (a + b) / 2;
                    if (alongX) part(wallType, u1 - u0, b - a, T, uc, yc, v, wallCol, true);
                    else part(wallType, T, b - a, u1 - u0, v, yc, uc, wallCol, true);
                };
                Action<float, float, float, float, bool> glassAt = (u0, u1, a, b, dark) =>
                {
                    float uc = (u0 + u1) / 2, yc = y0 + (a + b) / 2; string key = dark ? "glassDark" : "glass";
                    if (alongX) part(key, u1 - u0, b - a, 0.05f, uc, yc, v, "#ffffff", true); else part(key, 0.05f, b - a, u1 - u0, v, yc, uc, "#ffffff", true);
                    Action<float, float, float, float> fr = (fw, fh, fu, fy) =>
                    { if (alongX) part("trim", fw, fh, T + 0.06f, fu, y0 + fy, v, "#f3efe8", false); else part("trim", T + 0.06f, fh, fw, v, y0 + fy, fu, "#f3efe8", false); };
                    fr(u1 - u0 + 0.12f, 0.08f, uc, b + 0.04f); fr(u1 - u0 + 0.2f, 0.1f, uc, a - 0.05f);
                    fr(0.08f, b - a, u0 - 0.04f, (a + b) / 2); fr(0.08f, b - a, u1 + 0.04f, (a + b) / 2);
                };
                var ops = new List<Opening>(openings); ops.Sort((p, q) => p.u0.CompareTo(q.u0));
                float u = -len / 2;
                foreach (var o in ops)
                {
                    put(u, o.u0, 0, h);
                    put(o.u0, o.u1, 0, o.y0);
                    put(o.u0, o.u1, o.y1, h);
                    if (o.glass) glassAt(o.u0, o.u1, o.y0, o.y1, rng.NextD() < 0.35);
                    u = o.u1;
                }
                put(u, len / 2, 0, h);
            };
            Func<float, int, float, float, float, List<Opening>> windowsAlong = (len, count, y0, y1, skip) =>
            {
                var o = new List<Opening>();
                for (int i = 0; i < count; i++)
                {
                    float c = -len / 2 + (len / count) * (i + 0.5f);
                    if (skip > 0 && Mathf.Abs(c) < skip) continue;
                    o.Add(new Opening { u0 = c - 0.8f, u1 = c + 0.8f, y0 = y0, y1 = y1, glass = true });
                }
                return o;
            };

            // ground slab
            part(type == "warehouse" ? "concrete" : "wood", W, 0.2f, D, 0, 0.1f, 0, type == "warehouse" ? "#a9a49c" : "#c9a27e", true);
            {
                float ph = 0.34f; string pc = "#b39b78"; float side = (W / 2 + 0.06f - door / 2);
                part("stone", W + 0.12f, ph, 0.1f, 0, ph / 2, -D / 2 - 0.02f, pc, false);
                foreach (int s in new[] { -1, 1 }) part("stone", 0.1f, ph, D + 0.12f, s * (W / 2 + 0.02f), ph / 2, 0, pc, false);
                foreach (int s in new[] { -1, 1 }) part("stone", side, ph, 0.1f, s * (door / 2 + side / 2), ph / 2, D / 2 + 0.02f, pc, false);
            }
            bool stairs = type == "apartment";
            float stairW = 1.3f, run = 0.32f, sx0 = W / 2 - T - stairW, z0 = 1.8f;
            int nSteps = (int)Math.Ceiling(FH / 0.29f); float rise = FH / nSteps;
            for (int k = 0; k < floors; k++)
            {
                float fy = 0.2f + k * FH;
                float h = FH - (k == 0 ? 0.2f : 0);
                float wy = k == 0 ? 0.2f : fy;
                float winY0 = 1.0f, winY1 = Mathf.Min(2.4f, FH - 0.7f);
                List<Opening> front;
                if (k == 0)
                {
                    front = new List<Opening> { new Opening { u0 = -door / 2, u1 = door / 2, y0 = 0, y1 = doorH, glass = false } };
                    front.AddRange(windowsAlong(W, type == "store" ? 4 : 3, type == "store" ? 0.6f : winY0, type == "store" ? 2.6f : winY1, door / 2 + 1.0f));
                }
                else front = windowsAlong(W, 3, winY0, winY1, 0);
                wall("front", wy, h, type == "warehouse" && k == 0 ? new List<Opening> { front[0] } : front);
                wall("back", wy, h, type == "warehouse" ? windowsAlong(W, 3, 3.6f, 5.0f, 0) : windowsAlong(W, 3, winY0, winY1, 0));
                wall("left", wy, h, type == "warehouse" ? windowsAlong(D - 2 * T, 2, 3.6f, 5.0f, 0) : windowsAlong(D - 2 * T, 2, winY0, winY1, 0));
                wall("right", wy, h, type == "warehouse" ? windowsAlong(D - 2 * T, 2, 3.6f, 5.0f, 0) : stairs ? new List<Opening>() : windowsAlong(D - 2 * T, 2, winY0, winY1, 0));
                if (k > 0)
                {
                    float sTop = fy, yc = sTop - 0.125f, hz0 = z0 - nSteps * run, hz1 = z0 + 0.2f;
                    float iw = W / 2 - T;
                    part("wood", sx0 - 0.1f + iw, 0.25f, D - 2 * T, (-iw + sx0 - 0.1f) / 2, yc, 0, "#c9a27e", true);
                    part("wood", iw - sx0 + 0.1f, 0.25f, hz0 + (D / 2 - T), (sx0 - 0.1f + iw) / 2, yc, (hz0 - (D / 2 - T)) / 2, "#c9a27e", true);
                    part("wood", iw - sx0 + 0.1f, 0.25f, (D / 2 - T) - hz1, (sx0 - 0.1f + iw) / 2, yc, (hz1 + (D / 2 - T)) / 2, "#c9a27e", true);
                    part("metal", 0.05f, 0.9f, hz1 - hz0, sx0 - 0.1f, sTop + 0.45f, (hz0 + hz1) / 2, "#2f3238", false);
                }
            }
            if (stairs) for (int i = 0; i < nSteps; i++)
                {
                    float top = 0.2f + rise * (i + 1);
                    part("wood", stairW, top, run, sx0 + stairW / 2, top / 2, z0 - run * (i + 0.5f), "#b88f68", true);
                }
            float RH = 0.2f + floors * FH;
            part("concrete", W + 0.3f, 0.3f, D + 0.3f, 0, RH + 0.15f, 0, "#8f8b86", true);
            foreach (int s in new[] { -1, 1 })
            {
                part("concrete", W + 0.3f, 0.6f, 0.2f, 0, RH + 0.6f, s * (D / 2 + 0.05f), "#a8a39c", false);
                part("concrete", 0.2f, 0.6f, D + 0.3f, s * (W / 2 + 0.05f), RH + 0.6f, 0, "#a8a39c", false);
            }
            part("metal", 1.6f, 1.0f, 1.2f, W * 0.25f, RH + 0.8f, -D * 0.2f, "#9aa0a8", false);
            for (int k = 0; k < floors; k++)
            {
                float cy = 0.2f + (k + 1) * FH - (k == floors - 1 ? -0.0f : 0.25f) - 0.012f;
                if (stairs && k < floors - 1)
                {
                    float iw = W / 2 - T;
                    part("ceiling", sx0 - 0.1f + iw, 0.02f, D - 2 * T, (-iw + sx0 - 0.1f) / 2, cy, 0, "#ffffff", false);
                }
                else part("ceiling", W - 2 * T, 0.02f, D - 2 * T, 0, cy, 0, "#ffffff", false);
            }
            for (int k = 0; k < floors; k++) part("porch", 1.2f, 0.05f, 0.4f, -W * 0.2f, 0.2f + (k + 1) * FH - (k == floors - 1 ? 0 : 0.25f) - 0.05f, 0, "#ffffff", false);
            part("concrete", door + 1.2f, 0.2f, 0.8f, 0, 0.1f, D / 2 + 0.4f, "#bdb8af", true);
            if (type == "store")
            {
                part("trim", W + 0.3f, 0.6f, 0.15f, 0, FH - 0.5f, D / 2 + 0.1f, new[] { "#c94f4f", "#3f7fbf", "#3f9f6f", "#d9a03a" }[(int)Math.Floor(rng.NextD() * 4)], false);
                part("trim", door + 2, 0.1f, 1.4f, 0, doorH + 0.35f, D / 2 + 0.7f, "#4a4e57", false);
            }

            // front door
            {
                float zc = D / 2 - T / 2, dw = door, dh = doorH;
                Vector3 a = toWorld(-dw / 2, 0.2f, zc - 0.13f), b2 = toWorld(dw / 2, 0.2f, zc + 0.13f);
                var dbox = new SolidBox { minX = Mathf.Min(a.x, b2.x), maxX = Mathf.Max(a.x, b2.x), minZ = Mathf.Min(a.z, b2.z), maxZ = Mathf.Max(a.z, b2.z), top = Y + 0.2f + dh };
                boxes.Add(dbox);
                doors.Add(new DoorSpec
                {
                    hinge = toWorld(-dw / 2, 0.2f, zc), yaw = th, width = dw, height = dh, roller = type == "warehouse",
                    locked = rng.NextD() < 0.45, box = dbox, center = toWorld(0, 0.2f + 1.0f, zc)
                });
                dbox.door = doors.Count - 1;
            }

            // furniture + loot
            var loot = new List<float[]>();
            if (type == "store")
            {
                foreach (float lx in new[] { -3.5f, 0.5f }) part("metal", 3.2f, 1.8f, 0.6f, lx, 1.1f, -1.5f, "#9aa0a8", true);
                part("wood", 3.5f, 1.1f, 0.8f, 4.0f, 0.75f, -3.2f, "#8a6a4f", true);
                loot.Add(new[] { -1.5f, 0.2f, 0.6f }); loot.Add(new[] { 4.5f, 0.2f, 1.5f }); loot.Add(new[] { -4.5f, 0.2f, -3.6f });
            }
            else if (type == "warehouse")
            {
                Action<float, float, int> crate = (lx, lz, stack) => { for (int s = 0; s < stack; s++) part("wood", 1.3f, 1.3f, 1.3f, lx, 0.2f + 0.65f + s * 1.3f, lz, "#c49a6c", true); };
                crate(-6, -4, 2); crate(-4.6f, -4, 1); crate(-6, -2.6f, 1); crate(5.5f, -3.8f, 2); crate(5.5f, 2.5f, 1); crate(1, -4.2f, 1);
                part("wood", 2.4f, 0.15f, 1.6f, -2, 0.28f, 3, "#a8845e", true);
                loot.Add(new[] { -2f, 0.4f, 3f }); loot.Add(new[] { 6.5f, 0.2f, -1f }); loot.Add(new[] { 0f, 0.2f, -2f }); loot.Add(new[] { -6f, 2.8f, -4f });
            }
            else if (type == "house")
            {
                float iw = W / 2 - T;
                part(wallType, 3.2f, FH - 0.2f, T, -iw + 1.6f, 0.2f + (FH - 0.2f) / 2, -0.6f, wallCol, true);
                part(wallType, iw * 2 - 4.6f, FH - 0.2f, T, iw - (iw * 2 - 4.6f) / 2, 0.2f + (FH - 0.2f) / 2, -0.6f, wallCol, true);
                part(wallType, 1.4f, FH - 2.4f, T, -iw + 3.9f, 2.4f + (FH - 2.4f) / 2, -0.6f, wallCol, true);
                part("wood", 2.0f, 0.5f, 1.5f, -2.6f, 0.45f, -2.6f, "#e6dccf", true);
                part("wood", 1.4f, 0.75f, 0.9f, 2.6f, 0.58f, 2.2f, "#8a6a4f", true);
                part("metal", 2.4f, 0.95f, 0.6f, 2.8f, 0.67f, -3.8f, "#b9bcc0", true);
                loot.Add(new[] { -2.6f, 0.7f, -2.6f }); loot.Add(new[] { 2.6f, 0.95f, 2.2f }); loot.Add(new[] { 0.6f, 0.2f, 2.6f });
            }
            else
            {
                part("wood", 2.2f, 0.75f, 1.0f, -3, 0.58f, -2.5f, "#7a5a42", true);
                part("wood", 1.2f, 0.75f, 1.2f, -1, 0.58f, 2.5f, "#9a7a5a", true);
                part("wood", 2.2f, 0.5f, 1.6f, -3.4f, FH + 0.45f, -3, "#e6dccf", true);
                loot.Add(new[] { -1f, 0.2f, 0.5f }); loot.Add(new[] { 2f, 0.2f, -3.5f }); loot.Add(new[] { -1f, FH + 0.2f, 1.5f }); loot.Add(new[] { 2.4f, FH + 0.2f, -3.8f });
            }
            foreach (var l in loot)
                pickupSpots.Add(new PickupSpot { p = toWorld(l[0], l[1] + 0.9f, l[2]), fixedY = true });
        }

        struct Opening { public float u0, u1, y0, y1; public bool glass; }
    }
}
