using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hearthvale
{
    /// <summary>Static layout of the island and the Old Quarter (port of the top of world.js). JS coordinates.</summary>
    public struct Rect2 { public float minX, maxX, minZ, maxZ; public string name; public bool roof;
        public Rect2(float a, float b, float c, float d, string n = null, bool r = false) { minX = a; maxX = b; minZ = c; maxZ = d; name = n; roof = r; }
        public bool Contains(float x, float z) { return x >= minX && x <= maxX && z >= minZ && z <= maxZ; } }

    public struct Road { public char o; public float at, a, b; public string name;
        public Road(char o, float at, float a, float b, string n) { this.o = o; this.at = at; this.a = a; this.b = b; name = n; } }

    public static class WorldData
    {
        public const int WorldSize = 900;
        public const float WaterLevel = 0f;
        public const float TownHeight = 3f;
        public const int TownR = 80;
        public const float RoadW = 7f;

        public static readonly Rect2 Arena = new Rect2(-74, -16, -146, -96);
        public static readonly Vector2 ArenaC = new Vector2((-74 + -16) / 2f, (-146 + -96) / 2f);
        public static float ArenaDist(float x, float z)
        { return Mathf.Max(Mathf.Max(Arena.minX - x, x - Arena.maxX), Mathf.Max(Arena.minZ - z, z - Arena.maxZ)); }

        public static readonly Road[] Roads =
        {
            new Road('n', 0, -80, 80, "Mid"),
            new Road('n', 60, -66, 80, "Long"),
            new Road('n', -58, -30, 80, "West Alley"),
            new Road('e', 62, -80, 80, "Bazaar Row"),
            new Road('e', -62, -80, 80, "Rampart Row"),
        };
        public static readonly Vector3 Plaza = new Vector3(0, 72, 7); // x, z, r
        public static readonly Rect2[] Sites =
        {
            new Rect2(20, 56.5f, -58.5f, -22, "A"),
            new Rect2(-54.5f, -20, -58.5f, -30, "B"),
        };
        public static readonly Rect2[] Alleys =
        {
            new Rect2(-54.5f, -3.5f, 11.5f, 16.5f, "Tunnel", true),
            new Rect2(-24, -20, -30, 11.5f, "B Short"),
            new Rect2(3.5f, 56.5f, -10.5f, -5.5f, "A Short"),
            new Rect2(27, 33, -22, -10.5f, "A Ramp"),
            new Rect2(18, 34, 22, 40, "Well Yard"),
            new Rect2(24, 28, 40, 58.5f),
            new Rect2(3.5f, 18, 29, 33),
            new Rect2(-40, -26, 32, 46, "Olive Yard"),
            new Rect2(-54.5f, -40, 37, 41),
            new Rect2(-34, -31, 16.5f, 32),
            new Rect2(-14, 14, -80, -65.5f, "North Gate"),
            new Rect2(-16, 16, 65.5f, 80, "South Gate"),
        };

        public static Rect2 RoadRect(Road r, float pad = 0)
        {
            return r.o == 'e'
                ? new Rect2(r.a, r.b, r.at - RoadW / 2 - pad, r.at + RoadW / 2 + pad)
                : new Rect2(r.at - RoadW / 2 - pad, r.at + RoadW / 2 + pad, r.a, r.b);
        }

        public static readonly List<Rect2> RoadRects = new List<Rect2>();
        public static readonly List<Rect2> OpenRects = new List<Rect2>();
        static WorldData()
        {
            foreach (var r in Roads) RoadRects.Add(RoadRect(r));
            OpenRects.AddRange(RoadRects); OpenRects.AddRange(Sites); OpenRects.AddRange(Alleys);
        }
        public static bool IsOnRoad(float x, float z) { foreach (var q in RoadRects) if (q.Contains(x, z)) return true; return false; }

        // ------------------------------------------------------------ terrain height
        const int HSeg = 300;
        const float HCell = (float)WorldSize / HSeg;
        const int HW = HSeg + 1;
        static float[] hGrid;
        public static float[] Grid { get { return hGrid; } }
        public static int GridSegments { get { return HSeg; } }

        public static void SetGrid(float[] g) { hGrid = g; }

        public static float HeightAt(float x, float z)
        {
            if (hGrid != null)
            {
                float fx = (x + WorldSize / 2f) / HCell, fz = (z + WorldSize / 2f) / HCell;
                if (fx >= 0 && fz >= 0 && fx < HSeg && fz < HSeg)
                {
                    int ix = (int)fx, iz = (int)fz; float u = fx - ix, v = fz - iz; int k = iz * HW + ix;
                    float h00 = hGrid[k], h10 = hGrid[k + 1], h01 = hGrid[k + HW], h11 = hGrid[k + HW + 1];
                    return u + v <= 1 ? h00 + (h10 - h00) * u + (h01 - h00) * v : h11 + (h01 - h11) * (1 - u) + (h10 - h11) * (1 - v);
                }
            }
            return (float)HeightExact(x, z);
        }

        public static double HeightExact(double x, double z)
        {
            double d = Math.Sqrt(x * x + z * z);
            if (d >= 430) return -14;
            double island = 1 - Hv.Smoothstep(300, 430, d);
            double h = Hv.Fbm(x * 0.006 + 10, z * 0.006 - 4, 5) * 40 - 9;
            h += Hv.Fbm(x * 0.025 + 50, z * 0.025, 3) * 4;
            double md = Math.Sqrt((x - 230) * (x - 230) + (z + 210) * (z + 210));
            h += Math.Pow(Math.Max(0, 1 - md / 170), 2) * 75;
            double town = 1 - Hv.Smoothstep(TownR + 8, TownR + 60, Math.Max(Math.Abs(x), Math.Abs(z)));
            h = Hv.Lerp(h, TownHeight, town);
            if (town > 0) h = Math.Max(h, Hv.Lerp(h, 1.2, town));
            double toGate = SegDist(x, z, 0, -84, ArenaC.x + 8, Arena.maxZ + 2);
            double flat = Math.Max(1 - Hv.Smoothstep(3, 28, ArenaDist((float)x, (float)z)), 1 - Hv.Smoothstep(5, 22, toGate));
            if (flat > 0) h = Hv.Lerp(h, TownHeight, flat);
            return h * island + -14 * (1 - island);
        }

        static double SegDist(double x, double z, double ax, double az, double bx, double bz)
        {
            double dx = bx - ax, dz = bz - az, t = Hv.Clamp(((x - ax) * dx + (z - az) * dz) / (dx * dx + dz * dz), 0, 1);
            double px = x - ax - dx * t, pz = z - az - dz * t;
            return Math.Sqrt(px * px + pz * pz);
        }

        /// <summary>Height lookup in UNITY coordinates (z already flipped).</summary>
        public static float HeightAtU(float x, float zUnity) { return HeightAt(x, -zUnity); }
    }
}
