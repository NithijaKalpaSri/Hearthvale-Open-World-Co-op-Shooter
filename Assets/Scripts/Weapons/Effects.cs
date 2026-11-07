using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>
    /// Bullet tracers and spark bursts (port of the Effects class in weapons.js).
    /// Tracers are pooled line renderers; sparks are one shared particle system (one draw call for all of them).
    /// </summary>
    public sealed class Effects : MonoBehaviour
    {
        public static Effects Instance;
        sealed class TracerRec { public LineRenderer line; public float life; }
        readonly List<TracerRec> active = new List<TracerRec>(); readonly Stack<LineRenderer> free = new Stack<LineRenderer>();
        Material tracerMat, sparkMat; ParticleSystem sparks, petals;

        void Awake()
        {
            Instance = this;
            tracerMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "tracer" };
            tracerMat.SetColor("_BaseColor", new Color(3.2f, 2.9f, 1.4f));
            sparkMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "sparks" };
            sparkMat.SetColor("_BaseColor", Color.white);
            var go = new GameObject("Sparks"); go.transform.SetParent(transform, false);
            sparks = go.AddComponent<ParticleSystem>();
            var main = sparks.main; main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.92f; main.maxParticles = 600; main.startLifetime = 0.35f; main.startSpeed = 0f; main.startSize = 0.12f;
            var em = sparks.emission; em.enabled = false;
            var sh = sparks.shape; sh.enabled = false;
            var sol = sparks.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.05f));
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = sparkMat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            sparks.Play();
            // petals: pink/yellow flakes that tumble and drift down (animal knock-out)
            var pgo = new GameObject("Petals"); pgo.transform.SetParent(transform, false);
            petals = pgo.AddComponent<ParticleSystem>();
            var pm = petals.main; pm.loop = false; pm.playOnAwake = false; pm.simulationSpace = ParticleSystemSimulationSpace.World; pm.gravityModifier = 0.08f; pm.maxParticles = 200; pm.startLifetime = 2.2f; pm.startSpeed = 0f; pm.startSize = 0.24f;
            var pe = petals.emission; pe.enabled = false; var ps2 = petals.shape; ps2.enabled = false;
            var rot = petals.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var col = petals.colorOverLifetime; col.enabled = true; var grad = new Gradient(); grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) }); col.color = grad;
            var prr = pgo.GetComponent<ParticleSystemRenderer>(); prr.sharedMaterial = sparkMat; prr.shadowCastingMode = ShadowCastingMode.Off;
            petals.Play();
        }

        public void Petals(Vector3 p)
        {
            string[] cols = { "#ffc8dd", "#ffafcc", "#fff1a8", "#cdb4db" };
            for (int i = 0; i < 10; i++)
            {
                var ep = new ParticleSystem.EmitParams(); ep.position = p; Color c = M.Hex(cols[i % 4]).gamma * 1.6f; c.a = 1; ep.startColor = c;
                ep.velocity = new Vector3((Random.value - 0.5f) * 3, 2 + Random.value * 2, (Random.value - 0.5f) * 3); ep.startLifetime = 2.2f; ep.startSize = 0.22f;
                petals.Emit(ep, 1);
            }
        }

        /// <summary>One bullet streak from the muzzle to the impact (fades in 0.07 s).</summary>
        public void Tracer(Vector3 from, Vector3 to)
        {
            LineRenderer l;
            if (free.Count > 0) { l = free.Pop(); l.gameObject.SetActive(true); }
            else
            {
                var go = new GameObject("tracer"); go.transform.SetParent(transform, false);
                l = go.AddComponent<LineRenderer>(); l.sharedMaterial = tracerMat; l.positionCount = 2; l.useWorldSpace = true; l.numCapVertices = 2;
                l.shadowCastingMode = ShadowCastingMode.Off; l.receiveShadows = false;
            }
            l.startWidth = 0.025f; l.endWidth = 0.012f; l.SetPosition(0, from); l.SetPosition(1, to);
            active.Add(new TracerRec { line = l, life = 0.07f });
        }

        public void Burst(Vector3 point, string hexColor = "#fff3b0", int count = 5, float speed = 4f, float life = 0.35f)
        {
            if (sparks.particleCount > 220) count = Mathf.Min(count, 2); // cap work during big fights
            Color c = M.Hex(hexColor).gamma * 2.2f; c.a = 1;
            for (int i = 0; i < count; i++)
            {
                var ep = new ParticleSystem.EmitParams();
                ep.position = point;
                ep.velocity = new Vector3(Random.value - 0.5f, Random.value * 0.8f + 0.2f, Random.value - 0.5f) * speed;
                ep.startColor = c; ep.startLifetime = life; ep.startSize = 0.12f * 1.4f;
                sparks.Emit(ep, 1);
            }
        }

        void Update()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var t = active[i]; t.life -= Time.deltaTime;
                if (t.life <= 0) { t.line.gameObject.SetActive(false); free.Push(t.line); active.RemoveAt(i); }
                else { float k = t.life / 0.07f; t.line.startWidth = 0.025f * k; t.line.endWidth = 0.012f * k; }
            }
        }
    }
}
