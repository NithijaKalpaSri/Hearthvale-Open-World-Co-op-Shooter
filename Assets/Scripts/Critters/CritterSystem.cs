using System.Collections.Generic;
using UnityEngine;

namespace Hearthvale
{
    /// <summary>
    /// The Stinkers' brain (port of Critters host logic + combat glue in critters.js / main.js):
    /// spawning, wandering, noticing you, waddling up, turning round and farting, steering around walls,
    /// being shot, the cartoon knock-out, petals, and your gas exposure. Movement uses Unity's CharacterController,
    /// so animals collide with real walls, crates, trees and stairs.
    /// </summary>
    public sealed class CritterSystem : MonoBehaviour
    {
        public const int CritterLayer = 9, TerrainLayer = 10;
        public HvWorld world; public PlayerController player; public GasClouds gas; public Arsenal arsenal;
        public readonly Dictionary<int, Critter> list = new Dictionary<int, Critter>();
        public int petals; int nextId = 1; float spawnTimer;
        float gasAcc, coughT, gasThick;
        readonly int feelerMask = ~((1 << PlayerController.PlayerLayer) | (1 << CritterLayer) | (1 << TerrainLayer));
        readonly int groundMask = ~((1 << PlayerController.PlayerLayer) | (1 << CritterLayer));

        sealed class Dying { public Critter c; public Vector3 vel, spin, rot; public float t; public bool tooted; }
        readonly List<Dying> dying = new List<Dying>();

        void Start() { spawnTimer = 12 + Random.value * 15; } // the first animal turns up after a little while

        // ------------------------------------------------------------------ creation
        public Critter Create(int type, Vector3 pos)
        {
            var rig = AnimalModels.Build(type); var t = AnimalModels.Types[type];
            var go = rig.root; go.transform.SetParent(transform, false); go.transform.position = pos; go.layer = CritterLayer;
            var cc = go.AddComponent<CharacterController>(); cc.radius = 0.6f; cc.height = 1.9f; cc.center = new Vector3(0, 0.95f, 0); cc.stepOffset = 0.4f; cc.slopeLimit = 50; cc.skinWidth = 0.04f;
            var c = go.AddComponent<Critter>();
            c.id = nextId++; c.type = type; c.t = t; c.rig = rig; c.system = this; c.cc = cc; c.hp = t.hp;
            c.yaw = c.targetYaw = Random.value * 6.28f; c.walk = Random.value * 6; c.wander = Random.value * 6.28f; c.fartCd = 1 + Random.value * 2; c.ambientCd = 6 + Random.value * 10;
            list[c.id] = c; return c;
        }

        Vector3 RearOf(Critter c)
        {
            float s = c.t.scale, back = 0.75f * s;
            return new Vector3(c.transform.position.x - Mathf.Sin(c.yaw) * back, c.transform.position.y + 0.6f * s, c.transform.position.z - Mathf.Cos(c.yaw) * back);
        }

        float GroundAt(float x, float z, float fromY)
        {
            RaycastHit h;
            if (Physics.Raycast(new Vector3(x, fromY, z), Vector3.down, out h, 400f, groundMask, QueryTriggerInteraction.Ignore)) return h.point.y;
            return WorldData.HeightAtU(x, z);
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (world == null || player == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            HostUpdate(dt);
            foreach (var c in list.Values) c.Animate(dt, Camera.main);
            UpdateDeaths(dt);
            gas.Tick(dt, world.windSpeed);
            // your own gas exposure: damage over time, coughing, green screen tint
            float dps = 0, thick = 0;
            if (!player.dead) gas.Exposure(player.transform.position, out dps, out thick);
            gasThick += (thick - gasThick) * Mathf.Min(1f, dt * 4f);
            if (dps > 0)
            {
                gasAcc += dps * dt;
                if (gasAcc >= 2) { int n = Mathf.FloorToInt(gasAcc); gasAcc -= n; player.Damage(n, true); }
                coughT -= dt; if (coughT <= 0) { coughT = 1.6f + Random.value; Sfx.Cough(); }
            }
            else { gasAcc = 0; coughT = Mathf.Min(coughT, 0.3f); }
        }

        void HostUpdate(float dt)
        {
            bool alive = !player.dead; Vector3 pp = player.transform.position;
            spawnTimer -= dt;
            bool night = world.nightFactor > 0.25f;
            int want = night ? 0 : 2; // no animals at night
            if (night) foreach (var k in new List<Critter>(list.Values)) Remove(k);
            if (spawnTimer <= 0 && list.Count < want && alive)
            {
                spawnTimer = 20 + Random.value * 35; // a random encounter every half a minute or so
                for (int k = 0; k < 8; k++)
                {
                    float a = Random.value * Mathf.PI * 2, d = 40 + Random.value * 30;
                    float x = pp.x + Mathf.Cos(a) * d, z = pp.z + Mathf.Sin(a) * d;
                    float h = WorldData.HeightAtU(x, z);
                    if (h < 1.5f || Mathf.Sqrt(x * x + z * z) > 420 || world.IsBlockedU(x, z)) continue;
                    var sp = world.town.spawnPoint; // JS spawn (x, z) -> Unity (x, -z)
                    if (Mathf.Sqrt((x - sp.x) * (x - sp.x) + (z + sp.z) * (z + sp.z)) < 28) continue; // keep the spawn yard safe
                    float roll = Random.value; int type = roll < 0.18f ? 0 : roll < 0.45f ? 1 : roll < 0.7f ? 2 : 3;
                    Create(type, new Vector3(x, GroundAt(x, z, 200f) + 0.05f, z));
                    break;
                }
            }
            var ids = new List<int>(list.Keys);
            foreach (int id in ids)
            {
                Critter c; if (!list.TryGetValue(id, out c)) continue;
                var pos = c.transform.position; Vector3 to = pp - pos; to.y = 0; float dist = to.magnitude;
                if (Vector3.Distance(pos, pp) > 150 || WorldData.ArenaDist(pos.x, -pos.z) < 3f) { Remove(c); continue; }   // never inside (or at the gates of) the concert Arena
                c.fartCd -= dt; c.ambientCd -= dt;
                Vector3 move = Vector3.zero; float speed = c.t.speed;
                if (c.knock.sqrMagnitude > 0.01f) { move += c.knock * dt; c.knock *= Mathf.Pow(0.02f, dt); }
                if (c.stagger > 0) { c.stagger -= dt; Move(c, move, dt); c.moving = 0; continue; }
                if (c.fartT > 0)
                {   // mid-fart: stand still, back turned, tail up; the gas comes out half-way through
                    float before = c.fartT; c.fartT -= dt;
                    if (before > 0.45f && c.fartT <= 0.45f) { c.yaw = c.targetYaw; SpawnFart(c, RearOf(c), !c.ambient); }
                    Move(c, move, dt); c.moving = 0; continue;
                }
                float sight = 30 + world.nightFactor * 30; Vector3 dir;
                if (alive && dist < sight)
                {
                    if (!c.chasing) { c.chasing = true; }
                    dir = to.normalized;
                    if (dist < c.t.fartRange)
                    {
                        speed = 0;
                        if (c.fartCd <= 0)
                        {   // turn the rear toward the player and let rip
                            c.fartCd = c.t.fartCd * (0.8f + Random.value * 0.4f); c.fartT = 0.9f; c.ambient = false;
                            c.targetYaw = Mathf.Atan2(-dir.x, -dir.z); Move(c, move, dt); c.moving = 0; continue;
                        }
                    }
                }
                else
                {
                    c.chasing = false; c.wander += (Random.value - 0.5f) * dt * 2;
                    dir = new Vector3(Mathf.Cos(c.wander), 0, Mathf.Sin(c.wander)); speed *= 0.35f;
                    // wandering around town they leave little clouds behind them every so often
                    if (c.ambientCd <= 0) { c.ambientCd = 8 + Random.value * 10; c.fartT = 0.9f; c.ambient = true; c.targetYaw = c.yaw; Move(c, move, dt); c.moving = 0; continue; }
                }
                // steering: look ahead and turn away from walls, crates and water before touching them
                if (speed > 0.1f)
                {
                    if (c.detourT > 0) { c.detourT -= dt; dir = c.detourDir; }
                    Vector3 free; bool ok = ClearDir(c, dir, out free);
                    if (ok) dir = free; else { speed = 0; c.wander += Mathf.PI * (0.5f + Random.value); } // boxed in: turn around
                    if (!c.chasing && ok) c.wander = Mathf.Atan2(dir.z, dir.x);
                }
                Vector3 before2 = c.transform.position;
                if (speed > 0.1f) move += dir * speed * dt;
                Move(c, move, dt);
                float moved = new Vector2(c.transform.position.x - before2.x, c.transform.position.z - before2.z).magnitude;
                if (speed > 0.1f && moved < speed * dt * 0.35f) c.stuck += dt; else c.stuck = Mathf.Max(0, c.stuck - dt * 2);
                if (c.stuck > 0.35f)
                {   // barely moved although walking: pick a detour to the side
                    c.stuck = 0; float side = Random.value < 0.5f ? 1f : -1f; Vector3 d2;
                    if (!ClearDir(c, Quaternion.AngleAxis(side * 90f, Vector3.up) * dir, out d2)) d2 = -dir;
                    c.detourDir = d2; c.detourT = 1.2f + Random.value; c.wander = Mathf.Atan2(d2.z, d2.x);
                }
                // face the way it is ACTUALLY going (no walking on the spot into a wall)
                if (moved > speed * dt * 0.3f && moved > 1e-4f)
                { Vector3 dd = c.transform.position - before2; c.targetYaw = Mathf.Atan2(dd.x, dd.z); }
                c.moving = moved / Mathf.Max(dt, 1e-4f) > 0.3f ? speed : 0;
            }
        }

        void Move(Critter c, Vector3 delta, float dt)
        {
            c.velY = c.cc.isGrounded ? -2f : c.velY - 20f * dt; delta.y = c.velY * dt;
            c.cc.Move(delta);
        }

        // The wanted direction if nothing is in the way, otherwise the nearest clear one, or false if boxed in.
        bool ClearDir(Critter c, Vector3 dir, out Vector3 result)
        {
            float r = 0.6f * c.t.scale, look = r + 1.3f; Vector3 pos = c.transform.position;
            foreach (float ang in new[] { 0f, 0.45f, -0.45f, 0.9f, -0.9f, 1.4f, -1.4f, 2.0f, -2.0f, 2.6f, -2.6f })
            {
                Vector3 d = ang != 0 ? Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * dir : dir;
                float px = pos.x + d.x * look, pz = pos.z + d.z * look;
                if (world.IsBlockedU(px, pz) || WorldData.HeightAtU(px, pz) < 0.8f) continue;
                Vector3 eye = pos + Vector3.up * 0.7f, side = new Vector3(d.z, 0, -d.x) * r * 0.8f;
                if (Physics.Raycast(eye, d, look, feelerMask, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.Raycast(eye + side, d, look, feelerMask, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.Raycast(eye - side, d, look, feelerMask, QueryTriggerInteraction.Ignore)) continue;
                result = d; return true;
            }
            result = dir; return false;
        }

        void SpawnFart(Critter c, Vector3 at, bool big)
        {
            var t = c.t; float k = big ? 1f : 0.6f;
            gas.Add(new Vector3(at.x, at.y - 0.4f, at.z), t.gasR * k, t.gasDps * k, t.gasLife * (big ? 1f : 0.7f));
            var cam = Camera.main; if (cam == null) return;
            float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(cam.transform.position.x, cam.transform.position.z));
            if (d < 70) Sfx.Fart(t.sound, Mathf.Max(0.08f, 1 - d / 70f) * k, big ? 1f : 0.7f);
        }

        // ------------------------------------------------------------------ being shot
        public void Hit(Critter c, float dmg, Vector3 dir, float force, Vector3 point)
        {
            Vector3 dh = new Vector3(dir.x, 0, dir.z); if (dh.sqrMagnitude < 1e-6f) dh = Vector3.forward; dh.Normalize();
            c.flash = 0.12f; c.wobble = 1; c.lean = dh; Sfx.Hit();
            if (Effects.Instance != null) Effects.Instance.Burst(point, "#c9a7ff", 4, 3f, 0.35f);
            c.hp -= dmg;
            if (c.hp <= 0) { petals += c.t.petals; if (arsenal != null) arsenal.Toast("+" + c.t.petals + " 🌸 petals"); StartDeath(c, dh, force); return; }
            c.knock = dh * (force * 1.2f / c.t.scale); c.stagger = 0.25f;
        }

        public void TestCreate(int type, Vector3 pos) { Create(type, pos); }
        void Remove(Critter c) { list.Remove(c.id); Destroy(c.gameObject); }

        // Cartoon knock-out: launched by the hit, tumbles and bounces, lands on its back with its legs in the air,
        // lets out one last sad toot, then puffs away into petals.
        void StartDeath(Critter c, Vector3 dir, float force)
        {
            list.Remove(c.id); c.dying = true; c.fartT = 0;
            if (c.cc != null) Destroy(c.cc);
            var d = new Dying { c = c, vel = new Vector3(dir.x * force * 0.7f, 3 + force * 0.35f, dir.z * force * 0.7f), spin = new Vector3(dir.z * force, -(Random.value - 0.5f) * 5f, -dir.x * force), rot = new Vector3(0, c.yaw, 0) };
            dying.Add(d);
            AnimalModels.SetFlash(c.rig, 0.6f);
            if (Effects.Instance != null) Effects.Instance.Burst(c.transform.position + Vector3.up, "#ffffff", 6, 5f, 0.25f);
        }

        void UpdateDeaths(float dt)
        {
            var cam = Camera.main;
            for (int i = dying.Count - 1; i >= 0; i--)
            {
                var d = dying[i]; var c = d.c; if (c == null) { dying.RemoveAt(i); continue; }
                d.t += dt; var g = c.transform;
                if (d.t < 1.0f)
                {
                    d.vel.y -= 20 * dt; g.position += d.vel * dt;
                    d.rot += d.spin * dt; d.spin *= Mathf.Pow(0.3f, dt);
                    g.rotation = Quaternion.Euler(d.rot.x * Mathf.Rad2Deg, d.rot.y * Mathf.Rad2Deg, d.rot.z * Mathf.Rad2Deg);
                    float floor = GroundAt(g.position.x, g.position.z, g.position.y + 1f) + 0.5f * c.t.scale;
                    if (g.position.y < floor) { var p = g.position; p.y = floor; g.position = p; d.vel.y = d.vel.y < -2 ? -d.vel.y * 0.35f : 0; d.vel.x *= 0.5f; d.vel.z *= 0.5f; }
                    AnimalModels.SetFlash(c.rig, Mathf.Max(0, 0.6f - d.t));
                }
                else if (d.t < 2.0f)
                {   // flop onto its back, legs twitching in the air, one last toot
                    d.rot.x = Mathf.Lerp(d.rot.x, 0, Mathf.Min(1, dt * 8)); d.rot.z = Mathf.Lerp(d.rot.z, Mathf.PI, Mathf.Min(1, dt * 8));
                    g.rotation = Quaternion.Euler(d.rot.x * Mathf.Rad2Deg, d.rot.y * Mathf.Rad2Deg, d.rot.z * Mathf.Rad2Deg);
                    foreach (var l in c.rig.legs) l.localRotation = Quaternion.Euler(Mathf.Sin(d.t * 30 + l.localPosition.x * 10) * 0.25f * Mathf.Rad2Deg, 0, 0);
                    if (!d.tooted) { d.tooted = true; if (cam != null) { float dist = Vector3.Distance(g.position, cam.transform.position); if (dist < 60) Sfx.Fart(c.t.sound, 0.5f * (1 - dist / 60f), 0.35f); } }
                }
                else
                {
                    float k = Mathf.Min(1, (d.t - 2.0f) / 0.4f); g.localScale = Vector3.one * c.t.scale * (1 - k);
                    if (k >= 1)
                    {
                        if (Effects.Instance != null) { Vector3 p = g.position + Vector3.up * 0.6f; Effects.Instance.Burst(p, "#ffffff", 12, 5f, 0.6f); Effects.Instance.Burst(p, "#ffc8dd", 8, 4f, 0.7f); Effects.Instance.Petals(p); }
                        Destroy(c.gameObject); dying.RemoveAt(i);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ HUD: health bars over hurt animals, petals, gas tint
        Texture2D vignette;
        void OnGUI()
        {
            var cam = Camera.main; if (cam == null) return;
            if (gasThick > 0.01f) { GUI.color = new Color(0.45f, 0.75f, 0.2f, Mathf.Clamp01(gasThick * 0.6f)); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = Color.white; }
            foreach (var c in list.Values)
            {
                float frac = Mathf.Max(0, c.hp / c.t.hp); if (frac >= 1) continue;
                Vector3 sp = cam.WorldToScreenPoint(c.transform.position + Vector3.up * 2.35f * c.t.scale);
                if (sp.z <= 0) continue;
                float w = 70, h = 8, x = sp.x - w / 2, y = Screen.height - sp.y;
                GUI.color = new Color(0.23f, 0.2f, 0.33f, 0.6f); GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
                GUI.color = new Color(0.61f, 0.89f, 0.42f, 1f); GUI.DrawTexture(new Rect(x, y, w * frac, h), Texture2D.whiteTexture); GUI.color = Color.white;
            }
            var st = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold }; st.normal.textColor = Color.white;
            GUI.Label(new Rect(14, 38, 300, 28), "🌸 " + petals + " petals", st);
        }
    }
}
