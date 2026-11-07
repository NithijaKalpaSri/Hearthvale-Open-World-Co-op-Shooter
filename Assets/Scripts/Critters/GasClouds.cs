using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>
    /// Toxic fart clouds (port of gas.js). Each cloud is seven soft green puffs that swell up, drift with the wind
    /// and fade away. Standing in one costs health every second (less near the edge). All puffs of all clouds are
    /// drawn by one particle system (one draw call).
    /// </summary>
    public sealed class GasClouds : MonoBehaviour
    {
        const int MaxClouds = 40, Puffs = 7;
        sealed class Puff { public Vector3 off; public float spin, size, rot; }
        sealed class Cloud { public Vector3 pos, drift; public Color tint; public Puff[] puffs; public float r, dps, life, max, age; }
        readonly List<Cloud> clouds = new List<Cloud>();
        ParticleSystem ps; ParticleSystem.Particle[] buf = new ParticleSystem.Particle[MaxClouds * Puffs];
        static readonly Color[] Tints = { M.Hex("#9bd65a").gamma, M.Hex("#b8d94a").gamma, M.Hex("#7fc46a").gamma };
        public int Count { get { return clouds.Count; } }

        void Awake()
        {
            var go = new GameObject("GasPuffs"); go.transform.SetParent(transform, false);
            ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = buf.Length; main.startLifetime = 9999f; main.startSpeed = 0f;
            var em = ps.emission; em.enabled = false; var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "gasPuff" };
            m.SetTexture("_BaseMap", Resources.Load<Texture2D>("Textures/gascloud")); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_SrcBlend", 5f); m.SetFloat("_DstBlend", 10f); m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000;
            r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.sortingFudge = 5f;
            ps.Play();
        }

        public void Add(Vector3 p, float r, float dps, float life)
        {
            if (clouds.Count >= MaxClouds) clouds.RemoveAt(0);
            var c = new Cloud { pos = p, r = r, dps = dps, life = life, max = life, tint = Tints[Random.Range(0, 3)], puffs = new Puff[Puffs] };
            for (int i = 0; i < Puffs; i++)
            {
                float a = (float)i / Puffs * Mathf.PI * 2 + Random.value, d = i == 0 ? 0 : 0.35f + Random.value * 0.45f;
                c.puffs[i] = new Puff { off = new Vector3(Mathf.Cos(a) * d, (Random.value - 0.3f) * 0.5f, Mathf.Sin(a) * d), spin = (Random.value - 0.5f) * 0.6f, size = 0.9f + Random.value * 0.5f };
            }
            c.drift = new Vector3((Random.value - 0.5f) * 0.3f, 0.05f, (Random.value - 0.5f) * 0.3f);
            clouds.Add(c);
        }

        // how strong the cloud is right now: swells in over 1 s, fades over the last 1.5 s
        static float Strength(Cloud c) { return Mathf.Min(1f, c.age / 1.0f) * Mathf.Min(1f, c.life / 1.5f); }

        public void Tick(float dt, float wind = 2f)
        {
            int n = 0;
            for (int i = clouds.Count - 1; i >= 0; i--)
            {
                var c = clouds[i]; c.age += dt; c.life -= dt;
                if (c.life <= 0) { clouds.RemoveAt(i); continue; }
            }
            foreach (var c in clouds)
            {
                float k = Strength(c), grow = 0.45f + 0.55f * Mathf.Min(1f, c.age / 1.2f);
                c.pos += c.drift * dt; c.pos.x += dt * wind * 0.05f; // the breeze carries it
                float alpha = 0.55f * k, R = c.r * grow;
                foreach (var p in c.puffs)
                {
                    if (n >= buf.Length) break;
                    p.rot += p.spin * dt * Mathf.Rad2Deg;
                    float sz = R * p.size * (1 + Mathf.Sin(c.age * 1.3f + p.spin * 10) * 0.06f);
                    buf[n].position = c.pos + new Vector3(p.off.x * R, 0.4f + p.off.y * R * 0.6f + c.age * 0.05f, p.off.z * R);
                    buf[n].startSize = sz * 1.15f; buf[n].rotation = p.rot; buf[n].startColor = new Color(c.tint.r, c.tint.g, c.tint.b, alpha);
                    buf[n].remainingLifetime = 100f; buf[n].startLifetime = 200f;
                    n++;
                }
            }
            ps.SetParticles(buf, n);
        }

        /// <summary>damage per second at a point (sum of overlapping clouds), and 0..1 thickness for the screen tint</summary>
        public void Exposure(Vector3 pos, out float dps, out float thick)
        {
            dps = 0; thick = 0;
            foreach (var c in clouds)
            {
                float R = c.r * (0.45f + 0.55f * Mathf.Min(1f, c.age / 1.2f));
                float dx = pos.x - c.pos.x, dz = pos.z - c.pos.z, dy = pos.y + 1 - (c.pos.y + 0.4f);
                float d = Mathf.Sqrt(dx * dx + dz * dz + dy * dy * 0.49f);
                if (d >= R) continue;
                float k = Strength(c) * (1 - (d / R) * 0.6f); // thickest in the middle
                dps += c.dps * k; thick = Mathf.Max(thick, k);
            }
        }
    }
}
