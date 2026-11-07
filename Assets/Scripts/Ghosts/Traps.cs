using System.Collections.Generic;
using UnityEngine;

namespace Hearthvale
{
    /// <summary>
    /// Ghost traps (port of traps.js).
    ///  Hold G: a dotted arc shows where the trap will land (ring turns red where it can't be placed, e.g. water). Release G: thrown,
    ///  tumbles, bounces off walls, lands, digs in and arms after 2.5 s. Step on an armed trap: the ground swallows you, you drop into a
    ///  cave far below, a ghost at the far end (with sound A) comes for you. Reach the crack of light to climb out; if it touches you:
    ///  jump scare + damage. Animals that walk onto a trap are swallowed too. Up to 5 of yours at a time, each crumbles after 3 minutes.
    /// </summary>
    public sealed class Traps : MonoBehaviour
    {
        const float Grav = 18f, ThrowSpeed = 15f, ArmTime = 2.5f, Life = 180f, TriggerR = 1.15f; const int MaxOwn = 5;
        public static readonly Vector3 CavePos = new Vector3(0, -90f, 0); const float CaveRx = 23f, CaveRz = 7.2f;

        public PlayerController player; public Ghosts ghosts; public CritterSystem critters; public HvWorld world;

        sealed class Trap { public int id; public Vector3 pos; public GameObject go; public Renderer rune; public float arm = ArmTime, life = Life; public float seed; }
        sealed class Flying { public GameObject go; public List<Vector3> pts; public float t; public Vector3 land, spin; public bool hasLand; }
        readonly List<Trap> traps = new List<Trap>(); readonly List<Flying> flying = new List<Flying>(); int seq;
        LineRenderer arc, ring; Material arcMat, ringMat; bool aiming; Sim pending; float fade;
        static Mesh mDisc, mIron, mRune; static Material matDisc, matIron, matRune;
        int Mask { get { return ~((1 << PlayerController.PlayerLayer) | (1 << CritterSystem.CritterLayer)); } }

        struct Sim { public List<Vector3> pts; public bool hasLand, ok; public Vector3 land; }

        // test hooks
        public bool InCave { get { return cave != null; } }
        public int Count { get { return traps.Count; } }
        public string CavePhase { get { return cave == null ? "" : cave.phase; } }
        public void TestPlaceAt(Vector3 p, float arm = 0f) { var t = Place(p); t.arm = arm; }
        public Vector3 TrapPos(int i) { return traps[i].pos; }

        void Start()
        {
            var dash = new Texture2D(8, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < 8; i++) dash.SetPixel(i, 0, new Color(1, 1, 1, i < 5 ? 1f : 0f)); dash.Apply();
            arcMat = new Material(Shader.Find("Sprites/Default")); arcMat.mainTexture = dash;
            ringMat = new Material(Shader.Find("Sprites/Default"));
            var ag = new GameObject("TrapArc"); ag.transform.SetParent(transform, false); arc = ag.AddComponent<LineRenderer>(); arc.material = arcMat; arc.widthMultiplier = 0.04f; arc.textureMode = LineTextureMode.Tile; arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; arc.enabled = false;
            var rg = new GameObject("TrapRing"); rg.transform.SetParent(transform, false); ring = rg.AddComponent<LineRenderer>(); ring.material = ringMat; ring.widthMultiplier = 0.08f; ring.loop = true; ring.useWorldSpace = false; ring.positionCount = 40; ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; ring.enabled = false;
            for (int i = 0; i < 40; i++) { float a = i / 40f * Mathf.PI * 2; ring.SetPosition(i, new Vector3(Mathf.Cos(a) * TriggerR, 0, Mathf.Sin(a) * TriggerR)); }
        }

        // ------------------------------------------------------------------ the trap model: dark disc, iron ring + spikes, glowing runes
        static Mesh Torus(float R, float r, int seg, int tube)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++) { float a = i / (float)seg * Mathf.PI * 2; Vector3 c = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int j = 0; j < tube; j++) { float b = j / (float)tube * Mathf.PI * 2; v.Add(c * (R + Mathf.Cos(b) * r) + Vector3.up * Mathf.Sin(b) * r); } }
            for (int i = 0; i < seg; i++) for (int j = 0; j < tube; j++) { int a = i * tube + j, b = i * tube + (j + 1) % tube, c = (i + 1) * tube + j, d = (i + 1) * tube + (j + 1) % tube; t.AddRange(new[] { a, c, b, b, c, d }); }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
        }
        static Mesh Cone(float r, float h, int seg, bool down = false)
        {
            var v = new List<Vector3>(); var t = new List<int>(); v.Add(new Vector3(0, h, 0)); v.Add(Vector3.zero);
            for (int i = 0; i < seg; i++) { float a = i / (float)seg * Mathf.PI * 2; v.Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r)); }
            for (int i = 0; i < seg; i++) { int a = 2 + i, b = 2 + (i + 1) % seg; t.AddRange(new[] { 0, b, a }); t.AddRange(new[] { 1, a, b }); }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
        }
        static Mesh Merge(List<CombineInstance> ci) { var m = new Mesh(); m.CombineMeshes(ci.ToArray(), true, true); return m; }
        static CombineInstance CI(Mesh m, Vector3 p, Quaternion r, Vector3 s) { return new CombineInstance { mesh = m, transform = Matrix4x4.TRS(p, r, s) }; }

        static void BuildShared()
        {
            if (mDisc != null && matIron != null) return;
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder); var cm = cyl.GetComponent<MeshFilter>().sharedMesh; Destroy(cyl);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); var bm = cube.GetComponent<MeshFilter>().sharedMesh; Destroy(cube);
            mDisc = Merge(new List<CombineInstance> { CI(cm, new Vector3(0, 0.03f, 0), Quaternion.identity, new Vector3(0.88f, 0.03f, 0.88f)) });
            var iron = new List<CombineInstance> { CI(Torus(0.45f, 0.04f, 32, 8), new Vector3(0, 0.06f, 0), Quaternion.identity, Vector3.one) };
            var rune = new List<CombineInstance> { CI(Torus(0.3f, 0.018f, 32, 6), new Vector3(0, 0.07f, 0), Quaternion.identity, Vector3.one) };
            var spike = Cone(0.03f, 0.14f, 6);
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2;
                rune.Add(CI(bm, new Vector3(Mathf.Cos(a) * 0.17f, 0.07f, Mathf.Sin(a) * 0.17f), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), new Vector3(0.14f, 0.012f, 0.05f)));
                iron.Add(CI(spike, new Vector3(Mathf.Cos(a + 0.5f) * 0.45f, 0.05f, Mathf.Sin(a + 0.5f) * 0.45f), Quaternion.identity, Vector3.one));
            }
            mIron = Merge(iron); mRune = Merge(rune);
            matDisc = HvMaterials.Make("trapDisc", null, null, 0.1f, 0, "#121016", false);
            matIron = HvMaterials.Make("trapIron", null, null, 0.55f, 0.8f, "#2a2a30", false);
            matRune = HvMaterials.Make("trapRune", null, null, 0.3f, 0, "#b98cff", false); matRune.SetColor("_EmissionColor", M.Hex("#9a5cff") * 2.4f);
        }

        public static GameObject BuildModel(out Renderer rune)
        {
            BuildShared(); var g = new GameObject("GhostTrap");
            Renderer Add(string n, Mesh m, Material mat) { var c = new GameObject(n); c.transform.SetParent(g.transform, false); c.AddComponent<MeshFilter>().sharedMesh = m; var r = c.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return r; }
            Add("disc", mDisc, matDisc); Add("iron", mIron, matIron); rune = Add("rune", mRune, matRune);
            return g;
        }

        // ------------------------------------------------------------------ throwing
        float GroundY(Vector3 p) { RaycastHit h; return Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out h, 80f, Mask, QueryTriggerInteraction.Ignore) ? h.point.y : WorldData.HeightAtU(p.x, p.z); }

        // the preview and the real throw use the same path, so it lands exactly where the ring shows
        Sim Simulate(Vector3 origin, Vector3 vel)
        {
            var s = new Sim { pts = new List<Vector3> { origin } }; Vector3 p = origin, v = vel; float dt = 1f / 60f; int bounces = 0;
            for (int i = 0; i < 240; i++)
            {
                v.y -= Grav * dt; float step = v.magnitude * dt; Vector3 d = v.normalized; RaycastHit h;
                if (bounces < 3 && Physics.Raycast(p, d, out h, step + 0.1f, Mask, QueryTriggerInteraction.Ignore) && h.normal.y < 0.6f)
                { p += d * Mathf.Max(0, h.distance - 0.12f); v.x *= -0.35f; v.z *= -0.35f; v.y *= 0.6f; bounces++; }
                else p += v * dt;
                float floor = GroundY(p);
                if (p.y <= floor) { p.y = floor; s.hasLand = true; s.land = p; s.pts.Add(p); break; }
                if (i % 2 == 0) s.pts.Add(p);
            }
            s.ok = s.hasLand && s.land.y > WorldData.WaterLevel + 0.2f; return s;
        }

        void ThrowParams(out Vector3 origin, out Vector3 vel)
        {
            var cam = player.cam.transform; Vector3 d = cam.forward;
            origin = cam.position + d * 0.6f + Vector3.down * 0.25f;
            if (player.ThirdPerson) origin = player.Muzzle(d);
            vel = d * ThrowSpeed; vel.y += 3.5f;
        }

        bool CanThrow { get { return !player.dead && player.InputActive && !player.frozen && !player.inCave && cave == null; } }

        void UpdateAim()
        {
            bool holding = CanThrow && HvInput.Down("G") && player.traps > 0;
            if (holding)
            {
                aiming = true; Vector3 o, v; ThrowParams(out o, out v); var sim = Simulate(o, v); pending = sim;
                int n = Mathf.Min(sim.pts.Count, 160); arc.positionCount = n; for (int i = 0; i < n; i++) arc.SetPosition(i, sim.pts[i]);
                arcMat.mainTextureScale = new Vector2(Mathf.Max(1f, n * 0.45f / 0.43f * 0.25f), 1);
                var col = sim.ok ? new Color(0.79f, 0.65f, 1f, 0.9f) : new Color(1f, 0.48f, 0.48f, 0.9f); arc.startColor = arc.endColor = col; arc.enabled = true;
                ring.enabled = sim.hasLand; if (sim.hasLand) { ring.transform.position = sim.land + Vector3.up * 0.1f; ring.transform.localScale = Vector3.one * (1 + Mathf.Sin(Time.time * 6f) * 0.05f); }
                ring.startColor = ring.endColor = sim.ok ? new Color(0.73f, 0.55f, 1f, 0.85f) : new Color(1f, 0.29f, 0.29f, 0.85f);
                return;
            }
            if (aiming)
            {
                aiming = false; arc.enabled = false; ring.enabled = false;
                if (CanThrow && pending.pts != null && pending.ok && player.traps > 0) { player.traps--; Throw(pending); }
                else if (pending.pts != null && !pending.ok) Sfx.Empty();
                pending = default(Sim);
            }
        }

        void Throw(Sim sim)
        {
            Renderer r; var go = BuildModel(out r); go.transform.localScale = Vector3.one * 0.8f; go.transform.position = sim.pts[0];
            flying.Add(new Flying { go = go, pts = sim.pts, land = sim.land, hasLand = sim.hasLand, spin = new Vector3(Random.Range(6f, 12f), Random.Range(3f, 8f), Random.Range(4f, 9f)) });
            Sfx.Hatch();
        }

        Trap Place(Vector3 p)
        {
            Renderer r; var go = BuildModel(out r); go.transform.position = p + Vector3.up * 0.01f; go.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            var t = new Trap { id = seq++, pos = p, go = go, rune = r, seed = Random.value * 6f }; traps.Add(t);
            if (traps.Count > MaxOwn) Remove(traps[0]);
            return t;
        }
        void Remove(Trap t) { traps.Remove(t); if (t.go != null) Destroy(t.go); }

        public void Rumble() { Sfx.Crash(0.8f); StartCoroutine(Late()); }
        System.Collections.IEnumerator Late() { yield return new WaitForSeconds(0.18f); Sfx.Crash(0.5f); }

        Trap TrapAt(Vector3 p)
        {
            foreach (var t in traps) { if (t.arm > 0) continue; if (Mathf.Sqrt((p.x - t.pos.x) * (p.x - t.pos.x) + (p.z - t.pos.z) * (p.z - t.pos.z)) < TriggerR && Mathf.Abs(p.y - t.pos.y) < 1.2f) return t; }
            return null;
        }

        // ------------------------------------------------------------------ per frame
        MaterialPropertyBlock mpb;
        void Update()
        {
            if (player == null || world == null) return; float dt = Mathf.Min(Time.deltaTime, 0.05f);
            UpdateAim();
            var fx = Effects.Instance;
            for (int k = flying.Count - 1; k >= 0; k--)
            {
                var f = flying[k]; f.t += dt * 30f; int i = (int)f.t;
                if (i >= f.pts.Count - 1)
                {
                    Destroy(f.go); flying.RemoveAt(k);
                    if (f.hasLand)
                    {
                        Place(f.land);
                        if (fx != null) { fx.Burst(f.land + Vector3.up * 0.2f, "#8a7a6a", 10, 3f, 0.5f); fx.Burst(f.land + Vector3.up * 0.2f, "#b98cff", 6, 2f, 0.6f); }
                        Sfx.Crash(0.25f);
                    }
                    continue;
                }
                f.go.transform.position = Vector3.Lerp(f.pts[i], f.pts[i + 1], f.t - i); f.go.transform.Rotate(f.spin * dt * Mathf.Rad2Deg, Space.Self);
            }
            if (mpb == null) mpb = new MaterialPropertyBlock();
            for (int k = traps.Count - 1; k >= 0; k--)
            {
                var t = traps[k]; t.life -= dt; if (t.life <= 0) { Remove(t); continue; }
                bool was = t.arm > 0; t.arm -= dt; float e;
                if (t.arm > 0) { e = 2.4f + Mathf.Sin(Time.time * 20f) * 1.2f; t.go.transform.position = t.pos + Vector3.up * (0.01f - (1 - t.arm / ArmTime) * 0.04f); }
                else { if (was) Sfx.Static(0.08f); e = 0.25f + Mathf.Max(0, Mathf.Sin(Time.time * 1.3f + t.seed)) * 0.5f; }
                mpb.SetColor("_EmissionColor", M.Hex("#9a5cff") * e); t.rune.SetPropertyBlock(mpb);
            }
            if (!player.dead && !player.frozen && cave == null && !player.inCave) { var hit = TrapAt(player.transform.position); if (hit != null) TriggerFall(hit); }
            if (critters != null && traps.Count > 0)
                foreach (var c in new List<Critter>(critters.list.Values))
                {
                    if (c == null) continue; var t = TrapAt(c.transform.position); if (t == null) continue;
                    Vector3 cp = c.transform.position + Vector3.up * 0.3f;
                    if (fx != null) { fx.Burst(cp, "#7a6a58", 14, 4f, 0.6f); fx.Burst(cp, "#b98cff", 10, 3f, 0.7f); }
                    Rumble(); Remove(t); critters.Hit(c, 9999f, Vector3.zero, 1f, cp); player.Toast("👻 Your ghost trap swallowed an animal!");
                }
            UpdateCave(dt);
            // the cave camera fade eases itself back out when nothing is driving it
            if (cave == null) fade = Mathf.Max(0, fade - dt * 2f);
        }

        // ------------------------------------------------------------------ swallowed by the ground
        sealed class CaveRun { public string phase = "sink"; public float t; public Vector3 trap; public int kind; public GhostModel m; public Vector3 gpos; public bool flashWas; }
        CaveRun cave; CaveObj caveObj; Light caveLight;

        void TriggerFall(Trap trap)
        {
            if (cave != null) return;
            cave = new CaveRun { trap = trap.pos, kind = Random.Range(0, 3), flashWas = player.flashOn }; Remove(trap);
            player.Toast("👻 The ground swallows you…"); player.frozen = true; player.inCave = false;
            if (ghosts != null) ghosts.suspended = true;
            Sfx.Crackle(0.6f); Sfx.Stinger(0.5f); Rumble();
        }

        public static AudioClip VoiceA;
        void UpdateCave(float dt)
        {
            var c = cave; if (c == null) return; c.t += dt; var fx = Effects.Instance;
            if (player.dead) { Leave(true); return; }
            if (c.phase == "sink")
            {
                player.Teleport(new Vector3(c.trap.x, c.trap.y - c.t * 2.2f, c.trap.z));
                if (fx != null && Random.value < dt * 20f) fx.Burst(new Vector3(c.trap.x + Random.Range(-0.6f, 0.6f), c.trap.y + 0.2f, c.trap.z + Random.Range(-0.6f, 0.6f)), "#7a6a58", 3, 3f, 0.4f);
                fade = Mathf.Min(1, c.t / 0.8f); if (c.t > 0.9f) { c.phase = "black"; c.t = 0; }
            }
            else if (c.phase == "black")
            {
                fade = 1; if (c.t > 0.6f)
                {
                    if (caveObj == null) caveObj = BuildCave(transform);
                    caveObj.root.SetActive(true); world.caveMode = true; player.inCave = true; player.flashOn = true;
                    player.frozen = false; player.Teleport(CavePos + new Vector3(-14f, 4f, 0)); player.SetYaw(Mathf.PI / 2f); player.SetPitch(0.1f);
                    if (ghosts != null) ghosts.End(false);
                    c.phase = "fall"; c.t = 0;
                }
            }
            else if (c.phase == "fall")
            {
                fade = Mathf.Max(0, 1 - c.t / 0.5f);
                if (player.OnGround && c.t > 0.3f)
                {
                    Sfx.Crash(0.9f); if (fx != null) fx.Burst(player.transform.position + Vector3.up * 0.2f, "#6a5a4a", 14, 4f, 0.6f);
                    c.m = ghosts != null ? ghosts.Model(c.kind) : null; c.gpos = CavePos + new Vector3(8f, 0, Random.Range(-2f, 2f));
                    if (c.m != null) { c.m.root.transform.position = c.gpos; c.m.root.SetActive(true); }
                    if (VoiceA == null) VoiceA = Resources.Load<AudioClip>("Voices/ghost_trap_A"); if (VoiceA != null) Sfx.Play(VoiceA, 1f);
                    Sfx.Stinger(0.7f); c.phase = "hunt"; c.t = 0;
                }
            }
            else if (c.phase == "hunt")
            {
                var m = c.m; Vector3 pp = player.transform.position, cam = player.cam.transform.position;
                Vector3 toP = new Vector3(pp.x - c.gpos.x, 0, pp.z - c.gpos.z); float dist = toP.magnitude;
                float speed = c.t < 1.5f ? 0 : 2.6f + Mathf.Min(2.2f, c.t * 0.18f);   // glides toward you, faster as it closes in
                if (dist > 0.01f) c.gpos += toP.normalized * speed * dt;
                if (m != null)
                {
                    m.root.transform.position = new Vector3(c.gpos.x + (Random.value < 0.05f ? Random.Range(-0.2f, 0.2f) : 0), CavePos.y, c.gpos.z);
                    m.root.transform.rotation = Quaternion.LookRotation(new Vector3(cam.x - c.gpos.x, 0, cam.z - c.gpos.z) + Vector3.forward * 0.0001f);
                    if (c.kind == 1) m.Crawl(c.t); else if (Random.value < 0.04f) m.head.localRotation = Quaternion.Euler(0, 0, Random.Range(-28f, 28f)); else m.head.localRotation = Quaternion.Slerp(m.head.localRotation, Quaternion.identity, 0.05f);
                    m.root.SetActive(Random.value > 0.03f);   // flickers
                }
                if (caveLight == null) { var lg = new GameObject("CaveGhostLight"); lg.transform.SetParent(transform, false); caveLight = lg.AddComponent<Light>(); caveLight.type = LightType.Point; caveLight.range = 12; caveLight.shadows = LightShadows.None; }
                caveLight.color = (c.kind == 1 ? M.Hex("#ff5a1a") : M.Hex("#8fa6bf")).gamma; caveLight.transform.position = new Vector3(c.gpos.x, CavePos.y + 2, c.gpos.z); caveLight.intensity = 12f;
                if (ghosts != null) ghosts.nearness = Mathf.Max(0, 1 - dist / 20f);
                if (Random.value < dt * 0.6f) Sfx.Whisper(0.3f + (ghosts != null ? ghosts.nearness : 0) * 0.4f);
                caveObj.beamMat.SetColor("_BaseColor", new Color(0.74f, 0.82f, 1f, 0.6f + Mathf.Sin(c.t * 3f) * 0.2f));
                // made it to the crack of light: climb out
                if (Mathf.Sqrt((pp.x - caveObj.exit.x) * (pp.x - caveObj.exit.x) + (pp.z - caveObj.exit.z) * (pp.z - caveObj.exit.z)) < 1.8f) { Leave(false); player.Toast("😮‍💨 You clawed your way out of the cave!"); return; }
                // it got you
                if (dist < 1.3f || c.t > 25f) { int kind = c.kind; Leave(false); if (ghosts != null) ghosts.Jumpscare(kind, 35f); }
            }
        }

        void Leave(bool dead)
        {
            var c = cave; if (c == null) return;
            if (c.m != null) c.m.root.SetActive(false);
            if (caveLight != null) caveLight.intensity = 0;
            if (ghosts != null) { ghosts.nearness = 0; ghosts.suspended = false; }
            if (caveObj != null) caveObj.root.SetActive(false);
            world.caveMode = false; player.inCave = false; player.frozen = false; player.flashOn = c.flashWas;
            if (!dead)
            {
                float a = Random.value * 6.28f; float x = c.trap.x + Mathf.Cos(a) * 1.8f, z = c.trap.z + Mathf.Sin(a) * 1.8f;
                player.Teleport(new Vector3(x, GroundY(new Vector3(x, c.trap.y + 2, z)) + 0.05f, z));
            }
            fade = Mathf.Max(fade, 0.8f); cave = null;
        }

        // ------------------------------------------------------------------ the cave
        sealed class CaveObj { public GameObject root; public Vector3 exit; public Material beamMat; }

        static CaveObj BuildCave(Transform parent)
        {
            var root = new GameObject("GhostCave"); root.transform.SetParent(parent, false); root.transform.position = CavePos;
            var rock = HvMaterials.Make("caveRock", "rock", "rock_n", 0.15f, 0, "#7a6e64", false, false, true);
            var floorM = HvMaterials.Make("caveFloor", "rock", "rock_n", 0.1f, 0, "#6a5e54", false, false, true);
            // rough rock shell around a long chamber (triangles wound inward: you see and collide with the inside)
            int W = 64, H = 24; var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= H; j++) for (int i = 0; i <= W; i++)
            {
                float th = j / (float)H * Mathf.PI, ph = i / (float)W * Mathf.PI * 2; Vector3 s = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                float n = 1 + ((float)Hv.Fbm(s.x * 3 + 5, s.z * 3 + s.y * 2, 3) - 0.5f) * 0.35f;
                v.Add(new Vector3(s.x * (CaveRx + 2) * n, Mathf.Max(-0.15f, s.y) * 8f * n + 1.2f, s.z * (CaveRz + 2.5f) * n)); uv.Add(new Vector2(i / (float)W * 14f, j / (float)H * 5f));
            }
            for (int j = 0; j < H; j++) for (int i = 0; i < W; i++) { int a = j * (W + 1) + i, b = a + 1, c = a + W + 1, d = c + 1; t.AddRange(new[] { a, b, c, b, d, c }); }
            var sm = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; sm.SetVertices(v); sm.SetUVs(0, uv); sm.SetTriangles(t, 0); sm.RecalculateNormals(); sm.RecalculateBounds();
            // make sure the shell faces inward whatever the sphere's winding turned out to be
            Vector3 mid = new Vector3(0, 3f, 0); var nn = sm.normals; int bad = 0; for (int i = 0; i < nn.Length; i += 37) if (Vector3.Dot(nn[i], mid - v[i]) < 0) bad++;
            if (bad > nn.Length / 37 / 2) { for (int i = 0; i < t.Count; i += 3) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; } sm.SetTriangles(t, 0); sm.RecalculateNormals(); }
            var shell = new GameObject("shell"); shell.transform.SetParent(root.transform, false); shell.AddComponent<MeshFilter>().sharedMesh = sm; var sr = shell.AddComponent<MeshRenderer>(); sr.sharedMaterial = rock; sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shell.AddComponent<MeshCollider>().sharedMesh = sm;
            // uneven floor (visual) on a flat collider
            int fw = 60, fh = 20; var fv = new List<Vector3>(); var fu = new List<Vector2>(); var ft = new List<int>(); float fx0 = (CaveRx + 3) * 2, fz0 = (CaveRz + 3) * 2;
            for (int j = 0; j <= fh; j++) for (int i = 0; i <= fw; i++) { float x = (i / (float)fw - 0.5f) * fx0, z = (j / (float)fh - 0.5f) * fz0; fv.Add(new Vector3(x, ((float)Hv.Fbm(x * 0.3, z * 0.3, 3) - 0.5f) * 0.25f, z)); fu.Add(new Vector2(x * 0.25f, z * 0.25f)); }
            for (int j = 0; j < fh; j++) for (int i = 0; i < fw; i++) { int a = j * (fw + 1) + i, b = a + 1, c = a + fw + 1, d = c + 1; ft.AddRange(new[] { a, c, b, b, c, d }); }
            var fm = new Mesh(); fm.SetVertices(fv); fm.SetUVs(0, fu); fm.SetTriangles(ft, 0); fm.RecalculateNormals();
            var floor = new GameObject("floor"); floor.transform.SetParent(root.transform, false); floor.AddComponent<MeshFilter>().sharedMesh = fm; var fr = floor.AddComponent<MeshRenderer>(); fr.sharedMaterial = floorM; fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var fc = floor.AddComponent<BoxCollider>(); fc.center = new Vector3(0, -0.5f, 0); fc.size = new Vector3(fx0, 1f, fz0);
            // stalactites hang from the ceiling; stalagmites on the floor block your way
            var coneM = HvMaterials.Make("caveCone", "rock", null, 0.1f, 0, "#9a9088", false);
            var rs = new System.Random(77); float R(float a, float b) { return a + (float)rs.NextDouble() * (b - a); }
            for (int i = 0; i < 26; i++) { float x = R(-CaveRx + 3, CaveRx - 3), z = R(-CaveRz + 1.2f, CaveRz - 1.2f), h = R(1.2f, 3.6f); AddMesh(root, "stalactite", Cone(R(0.15f, 0.4f), h, 7), coneM, new Vector3(x, 8.4f, z), Quaternion.Euler(180, 0, 0)); }
            for (int i = 0; i < 9; i++)
            {
                float x = R(-CaveRx + 7, CaveRx - 6), z = R(-CaveRz + 1.5f, CaveRz - 1.5f), h = R(0.8f, 2.2f), r = R(0.25f, 0.5f);
                if (Mathf.Abs(x + 14) < 3 || Mathf.Abs(x - 21) < 3) continue;
                var g = AddMesh(root, "stalagmite", Cone(r, h, 7), coneM, new Vector3(x, 0, z), Quaternion.identity); var cc = g.AddComponent<CapsuleCollider>(); cc.radius = r * 0.7f; cc.height = h; cc.center = new Vector3(0, h / 2, 0);
            }
            // glowing crystals along the walls: the only light besides your torch
            var crystalM = HvMaterials.Make("caveCrystal", null, null, 0.6f, 0, "#4fd8c8", false); crystalM.SetColor("_EmissionColor", M.Hex("#2fb8a8") * 2f);
            for (int i = 0; i < 14; i++) { int side = rs.NextDouble() < 0.5 ? -1 : 1; var g = AddMesh(root, "crystal", Cone(0.06f, R(0.2f, 0.5f), 5), crystalM, new Vector3(R(-CaveRx + 2, CaveRx - 2), R(0.1f, 0.4f), side * R(CaveRz - 1.2f, CaveRz - 0.4f)), Quaternion.Euler(0, 0, side * -23f)); }
            // scattered bones of those who didn't make it
            var boneM = HvMaterials.Make("caveBone", null, null, 0.4f, 0, "#cfc6b0", false);
            var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule); var capM = cap.GetComponent<MeshFilter>().sharedMesh; Destroy(cap);
            var sph = GameObject.CreatePrimitive(PrimitiveType.Sphere); var sphM = sph.GetComponent<MeshFilter>().sharedMesh; Destroy(sph);
            for (int i = 0; i < 10; i++) { var b = AddMesh(root, "bone", capM, boneM, new Vector3(R(-CaveRx + 3, CaveRx - 3), 0.05f, R(-CaveRz + 1, CaveRz - 1)), Quaternion.Euler(90, R(0, 360), 0)); b.transform.localScale = new Vector3(0.07f, R(0.12f, 0.25f), 0.07f); }
            var sk = AddMesh(root, "skull", sphM, boneM, new Vector3(6, 0.12f, 3), Quaternion.identity); sk.transform.localScale = Vector3.one * 0.24f;
            // a few dim lights so the chamber can be navigated even without a torch
            for (int i = 0; i < 4; i++) { var lg = new GameObject("caveLight"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(-16 + i * 12.5f, 5.5f, 0); var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 18; l.intensity = 5f; l.color = M.Hex("#9fa8b8").gamma; l.shadows = LightShadows.None; }
            // the way out: a jagged crack of pale light in the far wall
            float exitX = CaveRx - 1.2f; var beamM = new Material(Shader.Find("Sprites/Default")); beamM.SetColor("_BaseColor", Color.white);
            var crackM = new Material(Shader.Find("Universal Render Pipeline/Unlit")); crackM.SetColor("_BaseColor", M.Hex("#dfe8ff") * 3f);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); var qm = quad.GetComponent<MeshFilter>().sharedMesh; Destroy(quad);
            var crack = AddMesh(root, "crack", qm, crackM, new Vector3(exitX + 0.6f, 1.6f, 0), Quaternion.Euler(0, 90, 0)); crack.transform.localScale = new Vector3(0.9f, 3.2f, 1);
            var lgx = new GameObject("exitLight"); lgx.transform.SetParent(root.transform, false); lgx.transform.localPosition = new Vector3(exitX - 1.5f, 1.8f, 0); var le = lgx.AddComponent<Light>(); le.type = LightType.Point; le.range = 14; le.intensity = 14f; le.color = M.Hex("#bcd0ff").gamma; le.shadows = LightShadows.None;
            var beam = AddMesh(root, "beam", qm, beamM, new Vector3(exitX - 0.5f, 1.6f, 0), Quaternion.Euler(0, 90, 0)); beam.transform.localScale = new Vector3(4, 6, 1); Destroy(beam.GetComponent<Collider>());
            var bmat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); bmat.SetFloat("_Surface", 1); bmat.SetFloat("_Blend", 1); bmat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One); bmat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One); bmat.SetFloat("_ZWrite", 0); bmat.renderQueue = 3000; bmat.SetColor("_BaseColor", new Color(0.74f, 0.82f, 1f, 0.7f)); bmat.SetTexture("_BaseMap", Resources.Load<Texture2D>("Textures/glow"));
            beam.GetComponent<MeshRenderer>().sharedMaterial = bmat; beam.GetComponent<MeshRenderer>().enabled = false;   // (the soft beam sprite looked like a solid box; the crack + its light are enough)
            root.SetActive(false);
            return new CaveObj { root = root, exit = CavePos + new Vector3(exitX, 0, 0), beamMat = bmat };
        }

        static GameObject AddMesh(GameObject parent, string name, Mesh m, Material mat, Vector3 pos, Quaternion rot)
        {
            var g = new GameObject(name); g.transform.SetParent(parent.transform, false); g.transform.localPosition = pos; g.transform.localRotation = rot;
            g.AddComponent<MeshFilter>().sharedMesh = m; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return g;
        }

        // ------------------------------------------------------------------ HUD
        void OnGUI()
        {
            if (player == null) return;
            if (!player.dead && !player.inCave) { var st = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft }; st.normal.textColor = new Color(0.85f, 0.72f, 1f); GUI.Label(new Rect(14, Screen.height - 76, 360, 26), "👻 Ghost traps ×" + player.traps + " · hold G", st); }
            if (fade > 0.01f) { GUI.color = new Color(0, 0, 0, Mathf.Clamp01(fade)); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = Color.white; }
        }
    }
}
