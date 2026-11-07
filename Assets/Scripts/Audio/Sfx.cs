using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hearthvale
{
    /// <summary>
    /// Port of the synthesized sound kit in audio.js (no audio files): oscillator tones with exponential
    /// envelopes and low-passed noise bursts, rendered once into AudioClips. Volumes follow the same
    /// channels as the original (master x sfx).
    /// </summary>
    public static class Sfx
    {
        const int Rate = 44100;
        public static float Master = 0.8f, Effects = 0.8f;
        static float Gain { get { return 0.4f * Master * Effects; } } // master bus gain in applyVolumes()

        static GameObject host; static AudioSource[] pool; static int next;
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        static System.Random rnd = new System.Random(4242);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { host = null; pool = null; droneSrc = null; cache.Clear(); }

        static void Init()
        {
            if (host != null) return;
            host = new GameObject("Sfx"); UnityEngine.Object.DontDestroyOnLoad(host);
            pool = new AudioSource[16];
            for (int i = 0; i < pool.Length; i++) { pool[i] = host.AddComponent<AudioSource>(); pool[i].playOnAwake = false; pool[i].spatialBlend = 0f; }
        }

        // ------------------------------------------------------------------ synthesis building blocks
        sealed class Mix
        {
            public float[] d; public Mix(float seconds) { d = new float[Mathf.CeilToInt(seconds * Rate)]; }
            static float Env(float t, float vol, float attack, float decay)
            {
                if (t < attack) return 0.0001f * Mathf.Pow(Mathf.Max(0.0002f, vol) / 0.0001f, t / attack);
                float u = (t - attack) / decay; if (u >= 1) return 0;
                return Mathf.Max(0.0002f, vol) * Mathf.Pow(0.0001f / Mathf.Max(0.0002f, vol), u);
            }
            /// type: 0 sine, 1 square, 2 triangle, 3 sawtooth
            public void Tone(float start, float freq, int type, float vol, float decay, float slideTo = 0)
            {
                int i0 = Mathf.RoundToInt(start * Rate); int n = Mathf.CeilToInt((decay + 0.05f) * Rate); double ph = 0;
                for (int i = 0; i < n && i0 + i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float f = slideTo > 0 ? freq * Mathf.Pow(slideTo / freq, Mathf.Min(1f, t / decay)) : freq;
                    ph += f / Rate; float x = (float)(ph - Math.Floor(ph)); float s;
                    switch (type) { case 1: s = x < 0.5f ? 1f : -1f; break; case 2: s = 4f * Mathf.Abs(x - 0.5f) - 1f; break; case 3: s = 2f * x - 1f; break; default: s = Mathf.Sin(x * Mathf.PI * 2); break; }
                    d[i0 + i] += s * Env(t, vol, 0.005f, decay);
                }
            }
            /// low-passed (or high-passed) noise that fades linearly, shaped by the same kind of envelope as envGain()
            public void Noise(float start, float dur, float cutoff, float vol, float attack, bool highpass = false)
            {
                int i0 = Mathf.RoundToInt(start * Rate); int n = Mathf.CeilToInt(dur * Rate);
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate), y = 0;
                for (int i = 0; i < n && i0 + i < d.Length; i++)
                {
                    float t = (float)i / Rate;
                    float x = ((float)rnd.NextDouble() * 2f - 1f) * (1f - (float)i / n);
                    y += a * (x - y); float s = highpass ? x - y : y;
                    d[i0 + i] += s * Env(t, vol, attack, dur);
                }
            }
            public AudioClip ToClip(string name)
            {
                float peak = 0.0001f; for (int i = 0; i < d.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
                if (peak > 1f) for (int i = 0; i < d.Length; i++) d[i] /= peak; // soft-protect against clipping
                var c = AudioClip.Create(name, d.Length, 1, Rate, false); c.SetData(d, 0); return c;
            }
        }

        static AudioClip Get(string key, Func<AudioClip> make)
        {
            Init(); AudioClip c;
            if (cache.TryGetValue(key, out c) && c != null) return c;
            c = make(); cache[key] = c; return c;
        }

        public static void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            Init(); if (clip == null) return;
            var s = pool[next]; next = (next + 1) % pool.Length;
            s.pitch = pitch; s.volume = Mathf.Clamp01(volume * Gain * 2.5f);
            s.clip = clip; s.Play();
        }

        // ------------------------------------------------------------------ the sounds (same numbers as audio.js)
        public static void Shot(string kind = "pistol", float volume = 1f)
        {
            if (kind == "dmr" || kind == "bolt") kind = "sniper";
            int variant = rnd.Next(3);
            var clip = Get("shot_" + kind + variant, () =>
            {
                float dur = kind == "shotgun" ? 0.35f : kind == "sniper" ? 0.5f : 0.18f;
                var m = new Mix(dur + 0.1f);
                float cutoff = kind == "rifle" ? 2600 : kind == "shotgun" ? 1200 : kind == "sniper" ? 1600 : 2000;
                m.Noise(0, dur, cutoff, 0.9f, 0.002f);
                m.Tone(0, kind == "shotgun" ? 90 : 140, 0, 0.5f, 0.12f, 50);
                return m.ToClip("shot_" + kind);
            });
            Play(clip, volume, 0.97f + (float)rnd.NextDouble() * 0.06f);
        }

        // ------------------------------------------------------------------ farts and coughs (audio.js playFart / playCough)
        sealed class Biquad
        {
            float b0, b1, b2, a1, a2, x1, x2, y1, y2;
            public Biquad(bool bandpass, float freq, float q)
            {
                float w0 = 2f * Mathf.PI * freq / Rate, cs = Mathf.Cos(w0), al = Mathf.Sin(w0) / (2f * q), a0 = 1 + al;
                if (bandpass) { b0 = al / a0; b1 = 0; b2 = -al / a0; }
                else { b0 = (1 - cs) / 2 / a0; b1 = (1 - cs) / a0; b2 = b0; }
                a1 = -2 * cs / a0; a2 = (1 - al) / a0;
            }
            public float Process(float x) { float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y; return y; }
        }

        // kind: squeak (high + tight), deep (long low rumble), ripple (rapid flutter), wet. f0,f1 Hz, dur s, flutter Hz, cutoff Hz, breath
        static readonly Dictionary<string, float[]> FartKinds = new Dictionary<string, float[]>
        {
            { "squeak", new[] { 170f, 120f, 0.55f, 34f, 1500f, 0.25f } }, { "deep", new[] { 58f, 42f, 1.3f, 16f, 520f, 0.45f } },
            { "ripple", new[] { 105f, 75f, 0.8f, 26f, 900f, 0.3f } },     { "wet", new[] { 82f, 60f, 0.95f, 21f, 700f, 0.6f } },
        };
        /// <summary>A buzzy low oscillator (the "lips") whose pitch wobbles and sags, chopped by a fast flutter, plus breathy filtered noise. Every one is a little different.</summary>
        public static void Fart(string kind = "ripple", float volume = 1f, float durScale = 1f)
        {
            if (volume <= 0.01f) return; Init();
            float[] p; if (!FartKinds.TryGetValue(kind, out p)) p = FartKinds["ripple"];
            System.Func<float> R = () => (float)rnd.NextDouble();
            float dur = p[2] * durScale * (0.8f + R() * 0.5f), f0 = p[0] * (0.85f + R() * 0.3f);
            var m = new Mix(dur + 0.08f);
            int steps = Mathf.Max(4, Mathf.RoundToInt(dur * 14)); var tk = new float[steps + 1]; var fk = new float[steps + 1]; tk[0] = 0; fk[0] = f0;
            for (int i = 1; i <= steps; i++) { float k = (float)i / steps; tk[i] = dur * k; fk[i] = Mathf.Lerp(f0, p[1] * (0.9f + R() * 0.2f), k * k) * (0.85f + R() * 0.3f); }
            float lfoEnd = p[3] * (0.6f + R() * 0.6f);
            var lp = new Biquad(false, p[4], 1.995f); var bp = new Biquad(true, p[4] * 0.8f, 1.2f);
            double ph = 0, lph = 0; int seg = 0; int n = Mathf.CeilToInt(dur * Rate);
            for (int i = 0; i < n && i < m.d.Length; i++)
            {
                float t = (float)i / Rate; while (seg < steps - 1 && t > tk[seg + 1]) seg++;
                float f = Mathf.Lerp(fk[seg], fk[seg + 1], Mathf.InverseLerp(tk[seg], tk[seg + 1], t));
                ph += f / Rate; float saw = 2f * (float)(ph - System.Math.Floor(ph)) - 1f; float voice = lp.Process(saw);
                lph += Mathf.Lerp(p[3], lfoEnd, t / dur) / Rate; float sq = (lph - System.Math.Floor(lph)) < 0.5 ? 1f : -1f; voice *= 0.55f + 0.45f * sq;
                float env; float v = volume;
                if (t < 0.03f) env = 0.0001f * Mathf.Pow(0.9f * v / 0.0001f, t / 0.03f);
                else if (t < 0.6f * dur) env = 0.9f * v;
                else env = 0.75f * v * Mathf.Pow(0.0001f / (0.75f * v), (t - 0.6f * dur) / (0.4f * dur));
                float air = ((float)rnd.NextDouble() * 2f - 1f) * (1f - (float)i / n); air = bp.Process(air);
                float aenv = t < 0.05f ? 0.0001f * Mathf.Pow(p[5] * v / 0.0001f, t / 0.05f) : p[5] * v * Mathf.Pow(0.0001f / (p[5] * v), (t - 0.05f) / (dur - 0.05f));
                m.d[i] += voice * env + air * aenv;
            }
            Play(m.ToClip("fart"));
        }
        /// <summary>coughing in the gas: two or three short, rough bursts</summary>
        public static void Cough()
        {
            int n = 2 + rnd.Next(2); var m = new Mix(n * 0.17f + 0.2f);
            for (int i = 0; i < n; i++) { m.Noise(i * 0.17f, 0.13f, 700 + (float)rnd.NextDouble() * 300, 0.45f, 0.003f); m.Tone(i * 0.17f, 160 + (float)rnd.NextDouble() * 40, 3, 0.12f, 0.1f, 110); }
            Play(m.ToClip("cough"));
        }

        // ------------------------------------------------------------------ horror kit (audio.js)
        static AudioSource droneSrc;
        public static void Static(float vol = 0.5f) { var m = new Mix(0.45f); m.Noise(0, 0.35f, 3000, vol, 0.003f, true); Play(m.ToClip("static")); }
        public static void Stinger(float vol = 0.7f) { var m = new Mix(1.9f); foreach (float f in new[] { 110f, 116.5f, 155.6f, 233f, 246.9f }) m.Tone(0, f, 3, 0.12f * vol, 1.6f, f * 0.97f); m.Noise(0, 0.25f, 300, 0.5f * vol, 0.003f); Play(m.ToClip("stinger")); }
        public static void Heartbeat(float vol = 0.6f) { var m = new Mix(0.5f); m.Tone(0, 62, 0, 0.9f * vol, 0.16f, 40); m.Tone(0.17f, 55, 0, 0.7f * vol, 0.2f, 36); Play(m.ToClip("heart")); }
        public static void Crackle(float vol = 0.3f) { var m = new Mix(0.5f); for (int i = 0; i < 6; i++) m.Noise((float)rnd.NextDouble() * 0.4f, 0.03f, 1500 + (float)rnd.NextDouble() * 2500, vol * (float)rnd.NextDouble(), 0.002f, true); Play(m.ToClip("crackle")); }
        public static void Distant(float vol = 0.3f)
        {
            var m = new Mix(2.4f); float r = (float)rnd.NextDouble();
            if (r < 0.35f) m.Tone(0, 180 + (float)rnd.NextDouble() * 80, 3, 0.18f * vol, 1.4f, 120); else if (r < 0.7f) m.Tone(0, 600 + (float)rnd.NextDouble() * 300, 0, 0.24f * vol, 2.2f, 380); else for (int i = 0; i < 3; i++) m.Noise(i * 0.33f, 0.08f, 220, 0.5f * vol, 0.003f);
            Play(m.ToClip("distant"));
        }
        public static void Whisper(float vol = 0.5f)
        {
            float dur = 1.6f + (float)rnd.NextDouble() * 1.2f; var m = new Mix(dur + 0.2f);
            for (int k = 0; k < dur * 6; k++) m.Noise(k / 6f, 0.15f, 2400 + (float)rnd.NextDouble() * 1200, vol * (0.4f + (float)rnd.NextDouble() * 0.6f), 0.06f, true);
            Play(m.ToClip("whisper"));
        }
        public static void Scream(float vol = 1f)
        {
            var m = new Mix(1.2f); float[] fs = { 920, 1010, 1340, 655 }; int[] ty = { 3, 3, 1, 3 };
            for (int i = 0; i < 4; i++) m.Tone(0, fs[i], ty[i], 0.25f * vol, 1.1f, fs[i] * 0.55f);
            m.Noise(0, 0.6f, 4000, 0.9f * vol, 0.003f, true); m.Tone(0, 48, 0, 0.9f * vol, 0.7f, 30); Play(m.ToClip("scream"), 1f);
        }
        /// <summary>low droning dread under the whole night (call every frame; 0 = silent)</summary>
        public static void SetDrone(float level)
        {
            Init();
            if (droneSrc == null)
            {
                float[] fs = { 41f, 41.5f, 61.75f, 83f }; var m = new Mix(4f); double[] ph = new double[4]; float lp = 0, a = 1f - Mathf.Exp(-2f * Mathf.PI * 260f / Rate);
                for (int i = 0; i < m.d.Length; i++) { float v = 0; for (int k = 0; k < 4; k++) { double q = (fs[k] * i / Rate) % 1.0; v += (2f * (float)q - 1f) * 0.18f; } lp += a * (v - lp); m.d[i] = lp * 2.2f; }
                var clip = m.ToClip("drone"); droneSrc = host.AddComponent<AudioSource>(); droneSrc.clip = clip; droneSrc.loop = true; droneSrc.spatialBlend = 0; droneSrc.volume = 0; droneSrc.Play();
            }
            droneSrc.volume = Mathf.Lerp(droneSrc.volume, Mathf.Clamp01(level * 0.55f * Gain * 5f), 0.05f);
        }
        public static void Hit() { Play(Get("hit", () => { var m = new Mix(0.2f); m.Tone(0, 900, 2, 0.25f, 0.08f, 1400); return m.ToClip("hit"); })); }
        public static void Pop() { Play(Get("pop", () => { var m = new Mix(0.4f); m.Tone(0, 500, 0, 0.35f, 0.15f, 1300); m.Tone(0.06f, 1200, 0, 0.2f, 0.2f, 1800); return m.ToClip("pop"); })); }
        public static void Pickup() { Play(Get("pickup", () => { var m = new Mix(0.5f); float[] f = { 660, 880, 1100 }; for (int i = 0; i < 3; i++) m.Tone(i * 0.07f, f[i], 2, 0.25f, 0.18f); return m.ToClip("pickup"); })); }
        public static void Hurt() { Play(Get("hurt", () => { var m = new Mix(0.35f); m.Tone(0, 220, 3, 0.25f, 0.2f, 90); return m.ToClip("hurt"); })); }
        public static void Reload() { Play(Get("reload", () => { var m = new Mix(0.45f); m.Tone(0, 300, 1, 0.1f, 0.05f); m.Tone(0.25f, 420, 1, 0.12f, 0.05f); return m.ToClip("reload"); })); }
        public static void Empty() { Play(Get("empty", () => { var m = new Mix(0.15f); m.Tone(0, 180, 1, 0.08f, 0.04f); return m.ToClip("empty"); })); }
        public static void Hatch() { Play(Get("hatch", () => { var m = new Mix(0.3f); m.Tone(0, 160, 2, 0.2f, 0.12f, 110); m.Noise(0, 0.1f, 900, 0.3f, 0.002f); return m.ToClip("hatch"); })); }
        public static void Crash(float v = 0.7f) { Play(Get("crash", () => { var m = new Mix(0.7f); m.Noise(0, 0.6f, 1800, 0.9f, 0.003f); m.Tone(0, 110, 0, 0.5f, 0.3f, 45); return m.ToClip("crash"); }), v); }
    }
}
