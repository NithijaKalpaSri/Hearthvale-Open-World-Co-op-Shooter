using System.Collections.Generic;
using UnityEngine;

namespace Hearthvale
{
    /// <summary>
    /// Hand bombs: press B to lob one (you start with 5). It bounces on real physics, blinks while the fuse burns (2.2 s),
    /// then blows up with the "bombo" sound: smashes doors, launches animals, hurts you if you stand too close.
    /// </summary>
    public sealed class HandBombs : MonoBehaviour
    {
        public PlayerController player; public HvWorld world; public CritterSystem critters;
        public static int Count = 5;
        const float Fuse = 2.2f, Radius = 7f, Dmg = 160f;
        sealed class Bomb { public GameObject go; public float t; public Renderer lamp; public bool lit; }
        readonly List<Bomb> bombs = new List<Bomb>(); Material black, red; float cd; GUIStyle st;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { Count = 5; }

        void Update()
        {
            float dt = Time.deltaTime; cd -= dt; var cam = Camera.main;
            if (cam != null && player != null && Count > 0 && cd <= 0 && !player.dead && !player.frozen && player.vehicle == null && !player.pillion && HvInput.Hit("B")) Throw(cam);
            for (int i = bombs.Count - 1; i >= 0; i--)
            {
                var b = bombs[i]; b.t += dt;
                bool on = (b.t * (2f + b.t * 3f)) % 1f < 0.5f; if (on != b.lit) { b.lit = on; b.lamp.sharedMaterial = on ? red : black; }
                if (b.t >= Fuse) { Explode(b.go.transform.position); Destroy(b.go); bombs.RemoveAt(i); }
            }
        }

        void Throw(Camera cam)
        {
            Count--; cd = 0.6f;
            if (black == null)
            {
                black = new Material(Shader.Find("Universal Render Pipeline/Lit")); black.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.09f)); black.SetFloat("_Smoothness", 0.5f);
                red = new Material(Shader.Find("Universal Render Pipeline/Unlit")); red.SetColor("_BaseColor", new Color(1f, 0.15f, 0.1f));
            }
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.name = "HandBomb"; g.transform.localScale = Vector3.one * 0.3f;
            g.GetComponent<Renderer>().sharedMaterial = black; g.GetComponent<SphereCollider>().material = new PhysicsMaterial { bounciness = 0.35f, dynamicFriction = 0.6f, frictionCombine = PhysicsMaterialCombine.Average };
            var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(cap.GetComponent<Collider>()); cap.transform.SetParent(g.transform, false); cap.transform.localPosition = new Vector3(0, 0.55f, 0); cap.transform.localScale = Vector3.one * 0.35f; cap.GetComponent<Renderer>().sharedMaterial = red;
            g.transform.position = cam.transform.position + cam.transform.forward * 0.7f - cam.transform.up * 0.15f;
            var rb = g.AddComponent<Rigidbody>(); rb.mass = 0.8f; rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.linearVelocity = cam.transform.forward * 15f + Vector3.up * 3.5f + player.Velocity * 0.5f; rb.angularVelocity = Random.insideUnitSphere * 8f;
            bombs.Add(new Bomb { go = g, lamp = cap.GetComponent<Renderer>() });
            Sfx.Hatch(); Sfx.Voice("bombo", 1f);   // the bombo sound plays when you throw it
        }

        void Explode(Vector3 p)
        {
            var fx = Effects.Instance;
            if (fx != null) { fx.Burst(p, "#fff1a8", 16, 10f, 0.7f); fx.Burst(p, "#ff9f6b", 14, 8f, 0.8f); fx.Burst(p, "#57505f", 8, 4f, 1.1f); }
            var cam = Camera.main; float d = cam != null ? Vector3.Distance(p, cam.transform.position) : 999f;
            if (d < 160f) Sfx.Crash(Mathf.Max(0.2f, 1f - d / 160f) * 0.6f);
            if (world != null) world.doors.DamageNear(p, Radius, 400f, i => { });
            if (critters != null)
                foreach (var c in new List<Critter>(critters.list.Values))
                {
                    if (c == null) continue; float dd = Vector3.Distance(c.transform.position, p); if (dd >= Radius) continue;
                    critters.Hit(c, Dmg * (1 - dd / Radius), c.transform.position - p, 14f * (1 - dd / Radius) + 4f, c.transform.position + Vector3.up);
                }
            if (player != null && !player.dead && player.vehicle == null)
            {
                float dd = Vector3.Distance(player.transform.position, p);
                if (dd < Radius) { player.Damage(70f * (1 - dd / Radius)); player.Launch(player.Velocity + (player.transform.position - p).normalized * 6f * (1 - dd / Radius) + Vector3.up * 5f * (1 - dd / Radius)); }
            }
        }

        void OnGUI()
        {
            if (player == null || player.dead) return;
            if (st == null) { st = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold }; st.normal.textColor = new Color(1f, 0.85f, 0.5f); }
            GUI.Label(new Rect(14, Screen.height - 112, 300, 26), "💣 Hand bombs ×" + Count + " · press B", st);
        }
    }
}
