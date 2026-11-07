using System;
using UnityEngine;

namespace Hearthvale
{
    /// <summary>
    /// Exact port of src/utils.js: seeded random + value noise.
    /// Every friend (and this Unity port) must generate the identical island,
    /// so the integer maths replicates JavaScript's 32-bit behaviour bit for bit.
    ///
    /// COORDINATES: all world-generation code works in the original (three.js, right-handed)
    /// coordinate system so the layout matches the JS game exactly. Meshes and colliders are
    /// converted to Unity space (z -> -z) when they are emitted. See WorldSpace.
    /// </summary>
    public sealed class Mulberry32
    {
        uint a;
        public Mulberry32(int seed) { a = unchecked((uint)seed); }
        public float Next() { return (float)NextD(); }
        public double NextD()
        {
            unchecked
            {
                a += 0x6d2b79f5u;
                uint t = (a ^ (a >> 15)) * (1u | a);
                t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
        // matches Math.floor(rng() * n)
        public int Int(int n) { return (int)Math.Floor(NextD() * n); }
    }

    public static class Hv
    {
        public const int WorldSeed = 1337;

        static double Hash2(int x, int z)
        {
            unchecked
            {
                int h = (int)((long)x * 374761393L + (long)z * 668265263L + (long)WorldSeed * 982451653L);
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                return ((uint)(h ^ (int)((uint)h >> 16))) / 4294967296.0;
            }
        }
        static double Fade(double t) { return t * t * (3 - 2 * t); }

        public static double Noise2(double x, double z)
        {
            int xi = (int)Math.Floor(x), zi = (int)Math.Floor(z);
            double xf = x - xi, zf = z - zi;
            double a = Hash2(xi, zi), b = Hash2(xi + 1, zi);
            double c = Hash2(xi, zi + 1), d = Hash2(xi + 1, zi + 1);
            double u = Fade(xf), v = Fade(zf);
            return (a + (b - a) * u) + ((c + (d - c) * u) - (a + (b - a) * u)) * v;
        }

        public static double Fbm(double x, double z, int oct = 4)
        {
            double sum = 0, amp = 1, freq = 1, norm = 0;
            for (int i = 0; i < oct; i++)
            {
                sum += Noise2(x * freq, z * freq) * amp;
                norm += amp; amp *= 0.5; freq *= 2;
            }
            return sum / norm;
        }

        public static double Clamp(double v, double a, double b) { return Math.Max(a, Math.Min(b, v)); }
        public static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
        public static double Smoothstep(double a, double b, double v)
        {
            double t = Clamp((v - a) / (b - a), 0, 1);
            return t * t * (3 - 2 * t);
        }
    }

    /// <summary>JS (right-handed, +z toward viewer) <-> Unity (left-handed) conversion.</summary>
    public static class WorldSpace
    {
        public static Vector3 ToUnity(Vector3 js) { return new Vector3(js.x, js.y, -js.z); }
        public static Vector3 ToUnity(float x, float y, float z) { return new Vector3(x, y, -z); }
        public static Vector3 ToJs(Vector3 u) { return new Vector3(u.x, u.y, -u.z); }
    }
}
