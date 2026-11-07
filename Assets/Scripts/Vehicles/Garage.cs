using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hearthvale
{
    public sealed class CraftCfg { public float radius, maxSpeed, accel, turn, hover, mass, hp; }

    /// <summary>One hover-craft (port of class Vehicle). Unity space: nose toward +z, yaw like the player (forward = (sin yaw, cos yaw)).</summary>
    public sealed class HoverCraft : MonoBehaviour, IShootable
    {
        public static readonly Dictionary<string, CraftCfg> Cfg = new Dictionary<string, CraftCfg>
        {
            { "pod",    new CraftCfg { radius = 1.9f, maxSpeed = 34, accel = 17, turn = 2.0f, hover = 0.95f, mass = 1.0f, hp = 100 } },
            { "manta",  new CraftCfg { radius = 2.2f, maxSpeed = 42, accel = 20, turn = 1.7f, hover = 0.85f, mass = 1.3f, hp = 90 } },
            { "beetle", new CraftCfg { radius = 2.4f, maxSpeed = 27, accel = 13, turn = 1.6f, hover = 1.05f, mass = 2.0f, hp = 160 } },
        };
        public static readonly Dictionary<string, string> Names = new Dictionary<string, string> { { "pod", "🛸 Bubble Pod" }, { "manta", "🪽 Sky Manta" }, { "beetle", "🐞 Moon Beetle" } };

        public int id; public string style; public CraftCfg cfg; public Garage garage;
        public Vector3 pos, vel; public float yaw, yawVel, pitch, roll, speed, steer, hp, brokenT, hatchT, smokeT, flicker;
        public bool broken, driven; public Vector3 spotPos; public float spotYaw;
        CraftModel.Parts parts; Collider hit;
        public Transform Seat { get { return parts.seat; } }
        public Transform HeadAnchor { get { return parts.headAnchor; } }
        public float Radius { get { return cfg.radius; } }
        public Vector3 Forward { get { return new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)); } }

        public void Init(Garage g, int i, string st, Vector3 spot, float ry)
        {
            garage = g; id = i; style = st; cfg = Cfg[st]; spotPos = spot; spotYaw = ry;
            parts = CraftModel.Build(st, CraftModel.Paints[i % CraftModel.Paints.Length], CraftModel.Glows[(i * 3) % CraftModel.Glows.Length]);
            parts.root.transform.SetParent(transform, false); hit = parts.root.GetComponent<Collider>(); SetLayer(parts.root, Garage.VehicleLayer);
            Reset();
        }
        static void SetLayer(GameObject g, int l) { g.layer = l; foreach (Transform t in g.transform) SetLayer(t.gameObject, l); }

        public void Reset()
        {
            pos = new Vector3(spotPos.x, 0, spotPos.z); pos.y = GroundY(pos.x, pos.z, 100f) + cfg.hover; vel = Vector3.zero; yaw = spotYaw; yawVel = pitch = roll = speed = steer = 0;
            hp = cfg.hp; broken = false; brokenT = 0; hatchT = 0; smokeT = 0; flicker = 0; driven = false;
            foreach (var d in parts.detachable) if (d != null) d.SetActive(true);
            parts.paint.SetColor("_BaseColor", parts.paintColor); Apply(0, 0);
        }

        public float GroundY(float x, float z, float fromY)
        {
            RaycastHit h; float floor = Physics.Raycast(new Vector3(x, fromY + 1.5f, z), Vector3.down, out h, 120f, garage.GroundMask, QueryTriggerInteraction.Ignore) ? h.point.y : WorldData.HeightAtU(x, z);
            return broken ? Mathf.Max(floor, WorldData.WaterLevel - 2.5f) : Mathf.Max(floor, WorldData.WaterLevel);   // hovers over water!
        }

        void Settle(float dt)
        {
            Vector3 f = Forward, r = new Vector3(f.z, 0, -f.x); float L = cfg.radius * 0.85f;
            float hf = GroundY(pos.x + f.x * L, pos.z + f.z * L, pos.y), hb = GroundY(pos.x - f.x * L, pos.z - f.z * L, pos.y);
            float hr = GroundY(pos.x + r.x, pos.z + r.z, pos.y), hl = GroundY(pos.x - r.x, pos.z - r.z, pos.y);
            float ground = (hf + hb + hr + hl) / 4f, targetY = ground + (broken ? 0.45f : cfg.hover), k = Mathf.Min(1, dt * (broken ? 4 : 10));
            pos.y = Mathf.Lerp(pos.y, targetY, k);
            pitch = Mathf.Lerp(pitch, Mathf.Atan2(hf - hb, L * 2) * 0.8f, k);
            float bank = broken ? 0.25f : yawVel * 0.22f * Mathf.Clamp01(Mathf.Abs(speed) / 15f);   // leans into turns
            roll = Mathf.Lerp(roll, Mathf.Atan2(hr - hl, 2) * 0.8f + bank, k);
        }

        public void Drive(float dt)
        {
            if (broken) { Coast(dt); return; }
            float throttle = (HvInput.Down("W") ? 1 : 0) - (HvInput.Down("S") ? 1 : 0), steerIn = (HvInput.Down("A") ? -1 : 0) + (HvInput.Down("D") ? 1 : 0);
            float boost = HvInput.Down("Shift") ? 1.3f : 1f; Vector3 f = Forward; float fwd = Vector3.Dot(vel, f); Vector3 lat = vel - f * fwd;
            float nf = fwd + throttle * cfg.accel * (throttle < 0 ? 0.6f : boost) * dt;
            if (throttle == 0) nf *= Mathf.Pow(0.55f, dt);
            if (HvInput.Down("Space")) nf *= Mathf.Pow(0.03f, dt);
            nf = Mathf.Clamp(nf, -12f, cfg.maxSpeed * boost);
            lat *= Mathf.Pow(0.12f, dt);   // hovercraft drift, then grip
            vel = f * nf + lat; speed = nf;
            steer = Mathf.Lerp(steer, steerIn, Mathf.Min(1, dt * 6));
            float turnScale = Mathf.Clamp(Mathf.Abs(nf) / 6f, 0.35f, 1f) * (nf < -0.5f ? -1 : 1);
            yawVel = Mathf.Lerp(yawVel, steer * cfg.turn * turnScale, Mathf.Min(1, dt * 5)); yaw += yawVel * dt;
            Move(vel * dt);
            float r = Mathf.Sqrt(pos.x * pos.x + pos.z * pos.z); if (r > 440) { pos.x *= 440 / r; pos.z *= 440 / r; vel *= 0.5f; }
            Settle(dt);
        }

        public void Coast(float dt)
        {
            vel *= Mathf.Pow(broken ? 0.15f : 0.35f, dt); yawVel *= Mathf.Pow(0.2f, dt); yaw += yawVel * dt;
            if (vel.sqrMagnitude > 0.01f) Move(vel * dt);
            speed = Vector3.Dot(vel, Forward); Settle(dt);
        }

        /// <summary>Horizontal sweep with a sphere: slide along walls, bounce a little, report the impact.</summary>
        void Move(Vector3 d)
        {
            d.y = 0; float dist = d.magnitude; if (dist < 1e-5f) return; Vector3 dir = d / dist;
            float rad = cfg.radius * 0.62f; Vector3 c = pos + Vector3.up * 0.9f; RaycastHit h;
            if (Physics.SphereCast(c, rad, dir, out h, dist + 0.05f, garage.WallMask, QueryTriggerInteraction.Ignore) && h.normal.y < 0.6f && h.distance > 0.001f)
            {
                pos += dir * Mathf.Max(0, h.distance - 0.05f); Vector3 n = h.normal; n.y = 0; n.Normalize();
                float vn = Vector3.Dot(vel, n);
                if (vn < 0) { vel -= n * vn * 1.35f; float impact = -vn; if (impact > 6) garage.Crash(this, null, (impact - 6) * 1.8f, 0, n, impact, h.point); }
                else pos += d;
            }
            else pos += d;
            // pushed into something by a turn or a neighbour? step back out
            Collider[] cs = Physics.OverlapSphere(pos + Vector3.up * 0.9f, rad * 0.95f, garage.WallMask, QueryTriggerInteraction.Ignore);
            foreach (var col in cs)
            {
                Vector3 push; float pd; if (Physics.ComputePenetration(garage.probe, pos + Vector3.up * 0.9f, Quaternion.identity, col, col.transform.position, col.transform.rotation, out push, out pd)) { push.y = 0; pos += push * pd; }
            }
        }

        public void OnShot(float damage, Vector3 point, Vector3 dir, float force)
        {
            if (broken) return; vel += new Vector3(dir.x, 0, dir.z).normalized * 0.6f * 0.5f; Damage(damage);
        }

        public void Damage(float amount)
        {
            if (broken || amount <= 0) return; float before = hp; hp = Mathf.Max(0, hp - amount); flicker = 0.3f; UpdateParts(before);
            if (hp <= 0) BreakDown();
        }

        void UpdateParts(float before)
        {
            float frac = hp / cfg.hp, prev = before / cfg.hp; int n = parts.detachable.Count;
            for (int i = 0; i < n; i++) { float th = 0.8f - (float)i / n * 0.6f; if (prev > th && frac <= th && parts.detachable[i].activeSelf) garage.Detach(this, parts.detachable[i]); }
        }

        void BreakDown()
        {
            if (broken) return; broken = true; brokenT = 40; hp = 0;
            foreach (var d in parts.detachable) if (d.activeSelf) garage.Detach(this, d);
            parts.paint.SetColor("_BaseColor", Color.Lerp(parts.paintColor, M.Hex("#3a3346"), 0.75f)); parts.glow.SetColor("_BaseColor", M.Hex("#2c2838"));
            garage.OnBreak(this);
        }

        public void OpenHatch() { hatchT = 1.5f; }

        public void Apply(float t, float dt)
        {
            float bob = broken ? 0 : Mathf.Sin(t * 2.2f + id) * 0.07f;
            transform.position = new Vector3(pos.x, pos.y + bob, pos.z); transform.rotation = Quaternion.Euler(-pitch * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, roll * Mathf.Rad2Deg);
            if (hatchT > 0) hatchT = Mathf.Max(0, hatchT - dt);
            float h = hatchT, open = h > 1.1f ? (1.5f - h) / 0.4f : h > 0.4f ? 1 : h / 0.4f, ease = open * open * (3 - 2 * open);
            foreach (var hp2 in parts.hatches) hp2.Key.localRotation = Quaternion.Euler(0, 0, hp2.Value * ease * 1.9f * Mathf.Rad2Deg);
            if (!broken)
            {   // thruster glow pulses with speed
                float p = 0.75f + Mathf.Sin(t * 12 + id) * 0.08f + Mathf.Min(0.4f, Mathf.Abs(speed) / 60f);
                float k = 2.4f * (flicker > 0 && Random.value < 0.5f ? 0.25f : p * (hp / cfg.hp < 0.25f && Random.value < 0.08f ? 0.2f : 1f));
                parts.glow.SetColor("_BaseColor", parts.glowColor * k);
            }
            flicker = Mathf.Max(0, flicker - dt);
        }
        public void SetVisible(bool v) { if (parts.root.activeSelf != v) parts.root.SetActive(v); }
    }

    /// <summary>All hover-craft: boarding, driving, crash physics, debris, smoke and the chase camera (port of Garage + ChaseCam).</summary>
    [DefaultExecutionOrder(100)]
    public sealed class Garage : MonoBehaviour
    {
        public const int VehicleLayer = 11;
        public HvWorld world; public PlayerController player; public PlayerAvatar avatar;
        public readonly List<HoverCraft> list = new List<HoverCraft>();
        public int GroundMask { get { return ~((1 << PlayerController.PlayerLayer) | (1 << CritterSystem.CritterLayer) | (1 << VehicleLayer)); } }
        public int WallMask { get { return GroundMask; } }
        public Collider probe;
        sealed class Debris { public Transform t; public Vector3 vel, spin; public float life = 12; }
        readonly List<Debris> debris = new List<Debris>();
        Light headlight, underLight; HoverCraft current;

        static readonly (string name, float dist, float height, float look, bool fp)[] Cams = { ("Close chase", 5.0f, 1.5f, 1.25f, false), ("Medium chase", 7.6f, 2.1f, 1.35f, false), ("Far chase", 11.5f, 3.2f, 1.5f, false), ("First person", 0, 0, 0, true) };
        int camMode = 1; float camYaw, lookYaw, lookPitch = 0.08f, idle, camDist, shake; bool camInit;

        public void Build()
        {
            var pg = new GameObject("Probe"); pg.transform.SetParent(transform, false); pg.transform.position = new Vector3(0, -500, 0); pg.layer = 2; var sc = pg.AddComponent<SphereCollider>(); sc.radius = 1f; probe = sc;
            var spots = world.town.vehicleSpots; int i = 0;
            foreach (var sp in spots)
            {
                var go = new GameObject("Craft" + i); go.transform.SetParent(transform, false); var c = go.AddComponent<HoverCraft>();
                c.Init(this, i, CraftModel.StyleList[i % 3], new Vector3(sp.x, 0, -sp.z), -sp.ry); list.Add(c); i++;
            }
            var hl = new GameObject("CraftHeadlight"); hl.transform.SetParent(transform, false); headlight = hl.AddComponent<Light>(); headlight.type = LightType.Spot; headlight.range = 48; headlight.spotAngle = 62; headlight.innerSpotAngle = 28; headlight.intensity = 0; headlight.shadows = LightShadows.None; headlight.color = M.Hex("#f4f8ff").gamma;
            var ul = new GameObject("CraftUnderglow"); ul.transform.SetParent(transform, false); underLight = ul.AddComponent<Light>(); underLight.type = LightType.Point; underLight.range = 7; underLight.intensity = 0; underLight.shadows = LightShadows.None;
        }

        public HoverCraft Nearest(Vector3 p, float max)
        {
            HoverCraft best = null; float bd = max * max;
            foreach (var c in list) { if (c.driven || c.broken) continue; float d = (c.pos - p).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
            return best;
        }

        // test hooks
        public void TestBoard(int i) { Enter(list[i]); }
        public void TestExit() { Exit(); }

        void Enter(HoverCraft c)
        {
            current = c; c.driven = true; c.vel = Vector3.zero; player.vehicle = c; c.OpenHatch(); Sfx.Hatch(); camInit = false; lookYaw = 0; lookPitch = 0.08f; camDist = 0;
            headlight.transform.SetParent(c.HeadAnchor, false); headlight.transform.localPosition = Vector3.zero; headlight.transform.localRotation = Quaternion.Euler(8f, 0, 0); headlight.intensity = 14f;
            underLight.transform.SetParent(c.transform, false); underLight.transform.localPosition = new Vector3(0, -0.6f, 0); underLight.intensity = 3f; underLight.color = M.Hex(CraftModel.Glows[(c.id * 3) % CraftModel.Glows.Length]).gamma;
            player.Toast("W/S fly · A/D steer · Shift boost · Space brake · V camera · F exit");
        }

        void Exit()
        {
            var c = current; if (c == null) return; c.driven = false; c.OpenHatch(); Sfx.Hatch(); player.vehicle = null; current = null;
            headlight.intensity = 0; underLight.intensity = 0; headlight.transform.SetParent(transform, false); underLight.transform.SetParent(transform, false);
            Vector3 right = new Vector3(Mathf.Cos(c.yaw), 0, -Mathf.Sin(c.yaw)); Vector3 p = c.pos + right * (c.Radius + 1f);
            RaycastHit h; float gy = Physics.Raycast(new Vector3(p.x, c.pos.y + 3, p.z), Vector3.down, out h, 40f, GroundMask, QueryTriggerInteraction.Ignore) ? h.point.y : WorldData.HeightAtU(p.x, p.z);
            player.Teleport(new Vector3(p.x, gy + 0.1f, p.z)); player.SetYaw(c.yaw); player.SetPitch(0); player.cam.fieldOfView = 75;
        }

        public void OnBreak(HoverCraft c)
        {
            var fx = Effects.Instance; Vector3 p = c.pos + Vector3.up * 0.8f;
            if (fx != null) { fx.Burst(p, "#fff1a8", 18, 9f, 0.8f); fx.Burst(p, "#ff9f6b", 14, 7f, 0.9f); fx.Burst(p, "#9fe8ff", 10, 6f, 0.7f); }
            Sfx.Crash(0.9f);
            var cam = Camera.main; float d = cam != null ? Vector3.Distance(p, cam.transform.position) : 0;
            if (player.vehicle == c) { shake = 1.5f; Exit(); player.Launch(new Vector3((Random.value - 0.5f) * 8, 9, (Random.value - 0.5f) * 8)); player.Damage(25); player.Toast("💥 Your hover-craft broke down! It will be repaired soon."); }
            else if (d < 8 && !player.dead) player.Damage(15);
        }

        readonly Dictionary<string, float> crashCd = new Dictionary<string, float>();
        public void Crash(HoverCraft v, HoverCraft o, float dmgSelf, float dmgOther, Vector3 n, float impulse, Vector3 point)
        {
            string key = v.id + ":" + (o != null ? o.id : -1); float now = Time.time; float cd; if (crashCd.TryGetValue(key, out cd) && cd > now) return; crashCd[key] = now + 0.3f;
            Sfx.Crash(Mathf.Min(1f, dmgSelf / 25f)); var fx = Effects.Instance; if (fx != null) { fx.Burst(point, "#ffe9a8", 10, 7f, 0.45f); fx.Burst(point, "#d8d2e2", 6, 3f, 0.6f); }
            if (v == current) shake = Mathf.Min(1.2f, dmgSelf / 18f);
            v.Damage(dmgSelf); if (o != null) { o.Damage(dmgOther); }
        }

        public void Detach(HoverCraft v, GameObject part)
        {
            part.SetActive(false);
            var piece = Instantiate(part, part.transform.position, part.transform.rotation); piece.SetActive(true); piece.transform.localScale = part.transform.lossyScale;
            foreach (var col in piece.GetComponentsInChildren<Collider>()) Destroy(col);
            Vector3 vel = v.vel * 0.6f + new Vector3((Random.value - 0.5f) * 6, 4 + Random.value * 4, (Random.value - 0.5f) * 6);
            debris.Add(new Debris { t = piece.transform, vel = vel, spin = new Vector3(Random.value * 10 - 5, Random.value * 10 - 5, Random.value * 10 - 5) * Mathf.Rad2Deg });
            if (Effects.Instance != null) Effects.Instance.Burst(piece.transform.position, "#ffe9a8", 6, 6f, 0.4f);
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (world == null || player == null) return; float dt = Mathf.Min(Time.deltaTime, 0.05f), t = Time.time; var cam = Camera.main;
            if (current == null)
            {
                if (!player.dead && string.IsNullOrEmpty(player.prompt))
                {
                    var n = Nearest(player.transform.position, 4.8f);
                    if (n != null) { player.prompt = "Press F to board " + HoverCraft.Names[n.style]; if (HvInput.Hit("F")) Enter(n); }
                }
            }
            else
            {
                player.prompt = "";
                if (player.dead) Exit();
                else if (HvInput.Hit("F")) Exit();
                else if (HvInput.Hit("V")) { camMode = (camMode + 1) % Cams.Length; camDist = 0; player.Toast("🎥 " + Cams[camMode].name); }
            }
            foreach (var c in list)
            {
                if (c == current) c.Drive(dt); else c.Coast(dt);
                if (!c.driven && c.vel.sqrMagnitude > 0.5f) CraftCollide(c);
                if (c == current) CraftCollide(c);
                c.SetVisible(c.driven || cam == null || (c.pos - cam.transform.position).sqrMagnitude < 170f * 170f);
                c.Apply(t, dt);
                float frac = c.hp / c.cfg.hp;
                if (c.broken || frac < 0.5f)
                {
                    c.smokeT -= dt;
                    if (c.smokeT <= 0 && Effects.Instance != null) { c.smokeT = c.broken ? 0.16f : 0.15f + frac * 0.5f; Effects.Instance.Burst(c.pos + Vector3.up * 0.8f, c.broken || frac < 0.25f ? "#57505f" : "#c9c2cf", 2, 1.5f, 1.2f); if (frac < 0.25f && !c.broken && Random.value < 0.2f) Effects.Instance.Burst(c.pos + Vector3.up * 0.6f, "#ffd27a", 3, 4f, 0.3f); }
                }
                if (c.broken) { c.brokenT -= dt; if (c.brokenT <= 0 && !c.driven) { c.Reset(); if (Effects.Instance != null) Effects.Instance.Burst(c.pos, "#bff7ff", 12, 5f, 0.8f); } }
            }
            if (current != null) SeatPlayer(current);
            for (int i = debris.Count - 1; i >= 0; i--)
            {
                var d = debris[i]; if (d.t == null) { debris.RemoveAt(i); continue; } d.life -= dt; d.vel.y -= 18 * dt; d.t.position += d.vel * dt; d.t.Rotate(d.spin * dt, Space.World);
                float floor = Mathf.Max(WorldData.HeightAtU(d.t.position.x, d.t.position.z), WorldData.WaterLevel - 3) + 0.1f;
                if (d.t.position.y < floor) { var p = d.t.position; p.y = floor; d.t.position = p; d.vel.y = Mathf.Abs(d.vel.y) > 2 ? -d.vel.y * 0.35f : 0; d.vel.x *= 0.6f; d.vel.z *= 0.6f; d.spin *= 0.6f; }
                if (d.life < 1) d.t.localScale *= Mathf.Pow(0.05f, dt);
                if (d.life <= 0) { Destroy(d.t.gameObject); debris.RemoveAt(i); }
            }
        }

        void CraftCollide(HoverCraft v)
        {
            foreach (var o in list)
            {
                if (o == v) continue; float dx = v.pos.x - o.pos.x, dz = v.pos.z - o.pos.z, rr = v.Radius + o.Radius, d2 = dx * dx + dz * dz; if (d2 >= rr * rr || d2 < 1e-6f) continue;
                float d = Mathf.Sqrt(d2), nx = dx / d, nz = dz / d, overlap = rr - d, rv = (v.vel.x - o.vel.x) * nx + (v.vel.z - o.vel.z) * nz;
                float mv = v.cfg.mass, mo = o.cfg.mass, shareV = mo / (mv + mo), shareO = mv / (mv + mo);
                v.pos.x += nx * overlap * shareV; v.pos.z += nz * overlap * shareV; o.pos.x -= nx * overlap * shareO; o.pos.z -= nz * overlap * shareO; float j = 0;
                if (rv < 0) { j = -(1 + 0.35f) * rv / (1 / mv + 1 / mo); v.vel.x += nx * j / mv; v.vel.z += nz * j / mv; o.vel.x -= nx * j / mo; o.vel.z -= nz * j / mo; v.yawVel += (Random.value - 0.5f) * Mathf.Min(3, -rv * 0.15f); o.yawVel += (Random.value - 0.5f) * Mathf.Min(3, -rv * 0.2f); }
                float impact = -rv; if (impact > 5) { float dmg = (impact - 5) * 1.5f; Crash(v, o, dmg * (o.cfg.mass / v.cfg.mass), dmg * (v.cfg.mass / o.cfg.mass), new Vector3(nx, 0, nz), j, (v.pos + o.pos) / 2 + Vector3.up * 0.5f); }
            }
        }

        // the player sits in the cockpit
        void SeatPlayer(HoverCraft c)
        {
            Vector3 sp = c.Seat.position + Vector3.down * 0.88f; player.Teleport(sp); player.SetYaw(c.yaw);
        }

        void LateUpdate()
        {
            var c = current; if (c == null || player.cam == null) return; float dt = Mathf.Min(Time.deltaTime, 0.05f); var cam = player.cam; var cm = Cams[camMode];
            Vector2 md = player.InputActive && Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            if (md.sqrMagnitude > 0) idle = 0; else idle += dt;
            lookYaw += md.x * 0.0028f; lookPitch = Mathf.Clamp(lookPitch - md.y * 0.002f, cm.fp ? -0.7f : -0.25f, cm.fp ? 0.7f : 0.9f);
            if (idle > 1f && Mathf.Abs(c.speed) > 1.5f) { float k = 1 - Mathf.Exp(-dt * 2.5f); lookYaw = Mathf.LerpAngle(lookYaw * Mathf.Rad2Deg, 0, k) * Mathf.Deg2Rad; lookPitch = Mathf.Lerp(lookPitch, 0.08f, k); }
            if (!camInit) { camYaw = c.yaw; camInit = true; }
            camYaw = Mathf.LerpAngle(camYaw * Mathf.Rad2Deg, c.yaw * Mathf.Rad2Deg, 1 - Mathf.Exp(-dt * (cm.fp ? 30f : 4.5f))) * Mathf.Deg2Rad;
            float yaw = camYaw + lookYaw + (HvInput.Down("C") ? Mathf.PI : 0); float speed = Mathf.Abs(c.speed);
            if (cm.fp)
            {
                cam.transform.position = c.Seat.position + Vector3.up * 0.9f;
                cam.transform.rotation = Quaternion.Euler(-(c.pitch * 0.7f) * Mathf.Rad2Deg + (lookPitch - 0.08f) * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, -c.roll * 0.5f * Mathf.Rad2Deg);
            }
            else
            {
                float p = lookPitch, dist = cm.dist + c.Radius * 0.6f + Mathf.Min(speed / 40f, 0.3f) * cm.dist;
                Vector3 pv = new Vector3(c.pos.x, c.pos.y + 1.2f, c.pos.z), horiz = new Vector3(-Mathf.Sin(yaw), 0, -Mathf.Cos(yaw)) * (dist * Mathf.Cos(p));
                Vector3 want = pv + horiz + Vector3.up * (cm.height - 1.2f + dist * Mathf.Sin(p));
                Vector3 dir = want - pv; float wd = dir.magnitude; dir /= wd; RaycastHit h;
                if (Physics.SphereCast(pv, 0.35f, dir, out h, wd, GroundMask, QueryTriggerInteraction.Ignore)) wd = Mathf.Max(0.5f, h.distance);
                camDist = camDist == 0 || wd < camDist ? wd : camDist + (wd - camDist) * Mathf.Min(1, dt * 3f);   // walls pull the camera in at once; it eases back out
                cam.transform.position = pv + dir * camDist; cam.transform.LookAt(new Vector3(c.pos.x, c.pos.y + cm.look, c.pos.z));
            }
            if (shake > 0) { cam.transform.position += new Vector3(Random.value - 0.5f, Random.value - 0.5f, 0) * shake * 0.6f; shake = Mathf.Max(0, shake - dt * 2f); }
            float fov = (cm.fp ? 78f : 70f) + speed * 0.3f; cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, 1 - Mathf.Exp(-dt * 6f));
        }
        public void Shake(float k) { shake = Mathf.Max(shake, k); }
        public bool FirstPerson { get { return current != null && Cams[camMode].fp; } }

        void OnGUI()
        {
            if (current == null) return; var c = current;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; st.normal.textColor = Color.white;
            GUI.Label(new Rect(0, 12, Screen.width, 34), HoverCraft.Names[c.style], st);
            st.fontSize = 18; GUI.Label(new Rect(0, Screen.height - 84, Screen.width, 28), Mathf.RoundToInt(Mathf.Abs(c.speed) * 3.6f) + " km/h", st);
            var r = new Rect(Screen.width / 2 - 100, Screen.height - 50, 200, 14); GUI.color = new Color(0, 0, 0, 0.5f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            float f = c.hp / c.cfg.hp; GUI.color = f > 0.35f ? new Color(0.5f, 0.9f, 1f) : new Color(1f, 0.4f, 0.3f); GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, (r.width - 4) * f, r.height - 4), Texture2D.whiteTexture); GUI.color = Color.white;
        }
    }
}
