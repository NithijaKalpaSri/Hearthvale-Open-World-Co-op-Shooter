using System.Collections.Generic;
using UnityEngine;

namespace Hearthvale
{
    /// <summary>
    /// Weapons built into every hover-craft (port of vehweapons.js), GTA style:
    ///   Left mouse: twin plasma cannons (fast, overheat if held too long).   Right mouse: rockets (slow reload, splash damage).
    /// Both aim at the crosshair, i.e. wherever the vehicle camera is looking.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class VehicleWeapons : MonoBehaviour
    {
        const float GunDmg = 14f, GunRate = 0.09f, GunHeat = 0.05f, GunCool = 0.4f, GunLockout = 2.2f, GunRange = 220f, GunSpread = 0.01f;
        const float RocketDmg = 150f, RocketSplash = 7.5f, RocketSpeed = 70f, RocketReload = 1.6f, RocketLife = 3.5f;

        public Garage garage; public PlayerController player; public CritterSystem critters; public HvWorld world;
        float heat, overheated, gunCd, rocketCd; int side = 1;
        sealed class Rocket { public GameObject go; public Vector3 vel; public HoverCraft owner; public float life = RocketLife, smokeT; }
        readonly List<Rocket> rockets = new List<Rocket>();
        Mesh bodyMesh, glowMesh; Material bodyMat, glowMat;

        // test hooks
        public int RocketCount { get { return rockets.Count; } }
        public float Heat { get { return heat; } }
        public bool Overheated { get { return overheated > 0; } }
        public bool testFireGun, testFireRocket;

        void Start()
        {
            var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule); bodyMesh = cap.GetComponent<MeshFilter>().sharedMesh; Destroy(cap);
            var sph = GameObject.CreatePrimitive(PrimitiveType.Sphere); glowMesh = sph.GetComponent<MeshFilter>().sharedMesh; Destroy(sph);
            bodyMat = HvMaterials.Make("rocketBody", null, null, 0.65f, 0.6f, "#d9d4de", false); glowMat = CraftModel.Unlit(new Color(3f, 1.6f, 0.6f));
        }

        Vector3 Muzzle(HoverCraft c, float s) { return c.transform.TransformPoint(new Vector3(s * c.Radius * 0.5f, 0.25f, c.Radius * 0.95f)); }

        // where the crosshair points, skipping your own craft and yourself
        Vector3 AimPoint()
        {
            var cam = player.cam.transform; RaycastHit h; int mask = ~((1 << PlayerController.PlayerLayer) | (1 << Garage.VehicleLayer));
            return Physics.Raycast(cam.position, cam.forward, out h, 400f, mask, QueryTriggerInteraction.Ignore) ? h.point : cam.position + cam.forward * 400f;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f); gunCd -= dt; rocketCd -= dt; var v = player.vehicle;
            bool lmb = HvInput.Down("LMB") || testFireGun, rmb = HvInput.Down("RMB") || testFireRocket;
            if (overheated > 0) { overheated -= dt; if (overheated <= 0) heat = 0.5f; }
            else heat = Mathf.Max(0, heat - GunCool * dt * (lmb && v != null ? 0.3f : 1f));
            if (v != null && !v.broken && (player.InputActive || testFireGun || testFireRocket))
            {
                bool wantGun = lmb && overheated <= 0 && gunCd <= 0, wantRocket = rmb && rocketCd <= 0;
                if (wantGun || wantRocket)
                {
                    Vector3 aim = AimPoint(), fwd = v.Forward, m, dir; var fx = Effects.Instance;
                    if (wantGun)
                    {
                        gunCd = GunRate; side = -side; m = Muzzle(v, side); dir = (aim - m).normalized; if (Vector3.Dot(dir, fwd) < 0.15f) dir = fwd;   // can't fire backwards through yourself
                        dir += new Vector3(Random.value - 0.5f, Random.value - 0.5f, 0) * GunSpread; dir.Normalize();
                        RaycastHit h; Vector3 point = m + dir * GunRange; float dist = GunRange; IShootable target = null;
                        if (Physics.Raycast(m, dir, out h, GunRange, ~((1 << PlayerController.PlayerLayer) | (1 << Garage.VehicleLayer) * 0), QueryTriggerInteraction.Ignore) && h.collider.GetComponentInParent<HoverCraft>() != v)
                        { point = h.point; dist = h.distance; target = h.collider.GetComponentInParent<IShootable>(); }
                        if (fx != null) fx.Tracer(m, point);
                        if (target != null)
                        {
                            bool craft = target is HoverCraft; target.OnShot(craft ? GunDmg * 0.4f : GunDmg, point, dir, 5f);
                            if (fx != null) fx.Burst(point, craft ? "#ffe9a8" : "#c9a7ff", 4, 3f, 0.35f);
                        }
                        else if (dist < GunRange && fx != null) fx.Burst(point, "#f3e3c3", 2, 2f, 0.25f);
                        Sfx.Shot("rifle", 0.55f);
                        heat += GunHeat; if (heat >= 1f) { heat = 1f; overheated = GunLockout; }
                    }
                    if (wantRocket)
                    {
                        rocketCd = RocketReload; m = Muzzle(v, 0); dir = (aim - m).normalized; if (Vector3.Dot(dir, fwd) < 0.15f) dir = fwd;
                        Spawn(m, dir * RocketSpeed + v.vel, v); Sfx.Shot("shotgun", 0.8f);
                    }
                }
            }
            UpdateRockets(dt);
        }

        void Spawn(Vector3 pos, Vector3 vel, HoverCraft owner)
        {
            var g = new GameObject("Rocket"); g.transform.position = pos;
            var b = new GameObject("body"); b.transform.SetParent(g.transform, false); b.transform.localRotation = Quaternion.Euler(90, 0, 0); b.transform.localScale = new Vector3(0.22f, 0.4f, 0.22f);
            b.AddComponent<MeshFilter>().sharedMesh = bodyMesh; var r = b.AddComponent<MeshRenderer>(); r.sharedMaterial = bodyMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var gl = new GameObject("glow"); gl.transform.SetParent(g.transform, false); gl.transform.localPosition = new Vector3(0, 0, -0.42f); gl.transform.localScale = Vector3.one * 0.3f;
            gl.AddComponent<MeshFilter>().sharedMesh = glowMesh; var gr = gl.AddComponent<MeshRenderer>(); gr.sharedMaterial = glowMat; gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.transform.rotation = Quaternion.LookRotation(vel.normalized);
            rockets.Add(new Rocket { go = g, vel = vel, owner = owner });
        }

        void UpdateRockets(float dt)
        {
            var fx = Effects.Instance;
            for (int i = rockets.Count - 1; i >= 0; i--)
            {
                var r = rockets[i]; r.life -= dt; r.vel.y -= 2f * dt; float step = r.vel.magnitude * dt; Vector3 dir = r.vel.normalized, p = r.go.transform.position;
                bool hit = false; Vector3 hp = p;
                var hits = Physics.RaycastAll(p, dir, step + 0.3f, ~(1 << PlayerController.PlayerLayer), QueryTriggerInteraction.Ignore); float best = float.MaxValue;
                foreach (var h in hits) { if (h.collider.GetComponentInParent<HoverCraft>() == r.owner && r.owner != null) continue; if (h.distance < best) { best = h.distance; hp = h.point; hit = true; } }
                r.smokeT -= dt; if (r.smokeT <= 0 && fx != null) { r.smokeT = 0.03f; fx.Burst(p, "#d8d2e2", 1, 0.6f, 0.5f); }
                if (hit || r.life <= 0) { Destroy(r.go); rockets.RemoveAt(i); Explode(hit ? hp : p, r.owner); continue; }
                r.go.transform.position = p + r.vel * dt; r.go.transform.rotation = Quaternion.LookRotation(r.vel.normalized);
            }
        }

        // big blast, splash damage falls off with distance
        void Explode(Vector3 p, HoverCraft owner)
        {
            var fx = Effects.Instance;
            if (fx != null) { fx.Burst(p, "#fff1a8", 16, 10f, 0.7f); fx.Burst(p, "#ff9f6b", 14, 8f, 0.8f); fx.Burst(p, "#57505f", 8, 4f, 1.1f); }
            var cam = Camera.main; float d = cam != null ? Vector3.Distance(p, cam.transform.position) : 999f;
            if (d < 160) Sfx.Crash(Mathf.Max(0.15f, 1 - d / 160f));
            if (d < 25) garage.Shake(0.8f * (1 - d / 25f));
            world.doors.DamageNear(p, 5f, 400f, i => { });
            if (critters != null)
                foreach (var c in new List<Critter>(critters.list.Values))
                {
                    if (c == null) continue; float dd = Vector3.Distance(c.transform.position, p); if (dd >= RocketSplash) continue;
                    Vector3 away = c.transform.position - p; critters.Hit(c, RocketDmg * (1 - dd / RocketSplash), away, 14f * (1 - dd / RocketSplash) + 4f, c.transform.position + Vector3.up);
                }
            foreach (var car in garage.list)
            {
                float dd = Vector3.Distance(car.pos, p); if (dd >= RocketSplash + car.Radius) continue;
                float k = 1 - Mathf.Max(0, dd - car.Radius) / RocketSplash; Vector3 away = car.pos - p; away.y = 0; float l = Mathf.Max(0.01f, away.magnitude);
                car.vel += away / l * 9f * k; car.Damage(RocketDmg * 0.45f * k * (car == owner ? 0.5f : 1f));
            }
            if (player.vehicle == null && !player.dead)
            {
                float dd = Vector3.Distance(player.transform.position, p);
                if (dd < RocketSplash) { player.Damage(60f * (1 - dd / RocketSplash)); player.Launch(player.Velocity + Vector3.up * 6f * (1 - dd / RocketSplash)); }
            }
        }

        void OnGUI()
        {
            var v = player.vehicle; if (v == null) return; float W = Screen.width, H = Screen.height;
            var r = new Rect(W - 230, H - 190, 200, 12); GUI.color = new Color(0, 0, 0, 0.5f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = overheated > 0 ? new Color(1, 0.25f, 0.2f) : Color.Lerp(new Color(0.5f, 0.9f, 1f), new Color(1f, 0.6f, 0.2f), heat); GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, (r.width - 4) * heat, r.height - 4), Texture2D.whiteTexture);
            r.y += 22; GUI.color = new Color(0, 0, 0, 0.5f); GUI.DrawTexture(r, Texture2D.whiteTexture); float rk = Mathf.Max(0, 1 - rocketCd / RocketReload);
            GUI.color = rk >= 1 ? new Color(1f, 0.7f, 0.3f) : new Color(0.6f, 0.5f, 0.4f); GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, (r.width - 4) * rk, r.height - 4), Texture2D.whiteTexture); GUI.color = Color.white;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleRight }; st.normal.textColor = Color.white;
            GUI.Label(new Rect(r.x - 130, r.y - 22, 126, 18), overheated > 0 ? "CANNONS HOT" : "Cannons · LMB", st); GUI.Label(new Rect(r.x - 130, r.y, 126, 18), rk >= 1 ? "Rocket ready · RMB" : "Reloading…", st);
        }
    }
}
