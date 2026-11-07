using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace Hearthvale
{
    /// <summary>
    /// Weere, the Scooty Boy (port of rider.js): a playboy on an electric scooter who tears around the town roads far too fast to see.
    /// He does NOT ride all the time: he only turns up at random moments in daylight, rides for a while, then is gone again (never at night).
    /// Now and then he screeches to a stop next to you and raps, then zooms off. THE RULE: never kill him. If he dies the game is lost.
    /// </summary>
    public sealed class Weere : MonoBehaviour, IShootable
    {
        public HvWorld world; public PlayerController player;
        public const string RiderName = "Weere";

        const float FLASH = 52f, LANE = 1.5f, MAX_HP = 100f;
        static readonly Dictionary<string, Vector2> NODES = new Dictionary<string, Vector2>   // JS road junctions, z mirrored into Unity space
        {
            {"BW",new Vector2(-80,-62)},{"BSW",new Vector2(-58,-62)},{"BM",new Vector2(0,-62)},{"BSE",new Vector2(60,-62)},{"BE",new Vector2(80,-62)},
            {"LS",new Vector2(60,-80)},{"RW",new Vector2(-80,62)},{"RM",new Vector2(0,62)},{"RNE",new Vector2(60,62)},{"RE",new Vector2(80,62)},
            {"MN",new Vector2(0,80)},{"MS",new Vector2(0,-80)},{"WS",new Vector2(-58,-80)},
        };
        static readonly string[][] EDGES = { new[]{"BW","BSW"}, new[]{"BSW","BM"}, new[]{"BM","BSE"}, new[]{"BSE","BE"}, new[]{"BSE","LS"}, new[]{"BSE","RNE"},
            new[]{"RW","RM"}, new[]{"RM","RNE"}, new[]{"RNE","RE"}, new[]{"RM","MN"}, new[]{"BM","MS"}, new[]{"BSW","WS"} };
        static Dictionary<string, List<string>> ADJ;

        enum St { Away, Ride, Approach, Rap, Leave, Dead }
        St state = St.Away;
        string from, to; float seg;
        Vector3 pos; float yaw, speed;
        float hp = MAX_HP, hurtT, flashT, t;
        float awayT, visitT, meetCd, rapT; int met;
        Vector3 leaveFrom; string rFrom, rTo; float rSeg;
        bool gameOver; float gameOverT;
        int rapLine; float lineT;

        GameObject visual, trail; Avatar av; Transform[] wheels = new Transform[2]; CapsuleCollider col; Material trailMat;
        readonly List<Material> flashMats = new List<Material>();
        static readonly string[] Raps = {
            "Yo yo! I'm Weere — ride with no fear!", "Two wheels, no brakes, the whole town shakes!",
            "Hearthvale nights? I'm gone by sundown, flash!", "Don't shoot the kid, I'm the whole game's grid!", "Weere on the street, can't touch these feet!" };

        // ------------------------------------------------------------------ setup
        void Awake()
        {
            if (ADJ == null) { ADJ = new Dictionary<string, List<string>>(); foreach (var e in EDGES) { Add(e[0], e[1]); Add(e[1], e[0]); } }
        }
        static void Add(string a, string b) { if (!ADJ.ContainsKey(a)) ADJ[a] = new List<string>(); ADJ[a].Add(b); }

        void Start()
        {
            Build(); visual.SetActive(false); trail.SetActive(false);
            awayT = Random.Range(20f, 55f);     // the first appearance is soon after the game starts (if it is daytime)
        }

        static Material Unlit(Color c, float mul = 1f) { var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", c * mul); return m; }

        GameObject Prim(PrimitiveType pt, Transform parent, Vector3 lp, Vector3 ls, Material m, Vector3? euler = null, bool shadow = true)
        {
            var g = GameObject.CreatePrimitive(pt); Destroy(g.GetComponent<Collider>()); g.transform.SetParent(parent, false);
            g.transform.localPosition = lp; g.transform.localScale = ls; if (euler.HasValue) g.transform.localRotation = Quaternion.Euler(euler.Value);
            var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off; return g;
        }

        /// <summary>A UV sphere (radius 1) with the triangles removed where keep(direction) is false: used for the open-face helmet and the raised visor.</summary>
        static Mesh SpherePart(System.Func<Vector3, bool> keep, int lat = 22, int lon = 32)
        {
            var vs = new List<Vector3>(); var tris = new List<int>();
            for (int i = 0; i <= lat; i++) for (int j = 0; j <= lon; j++)
            { float th = Mathf.PI * i / lat, ph = 2 * Mathf.PI * j / lon; vs.Add(new Vector3(Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Cos(ph))); }
            for (int i = 0; i < lat; i++) for (int j = 0; j < lon; j++)
            {
                int a = i * (lon + 1) + j, b = a + lon + 1; Vector3 c = (vs[a] + vs[b + 1]).normalized;
                if (!keep(c)) continue; tris.Add(a); tris.Add(a + 1); tris.Add(b); tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
            }
            var m = new Mesh(); m.SetVertices(vs); m.SetNormals(vs); m.SetTriangles(tris, 0); m.RecalculateBounds(); return m;
        }

        void Build()
        {
            visual = new GameObject("WeereVisual"); visual.transform.SetParent(transform, false);
            var g = visual.transform;   // front = +z
            Material body = HvMaterials.Make("wBody", null, null, 0.6f, 0.3f, "#101014", false), trim = HvMaterials.Make("wTrim", null, null, 0.55f, 0.1f, "#f2f2f5", false),
                     rubber = HvMaterials.Make("wRubber", null, null, 0.1f, 0f, "#18181a", false), metal = HvMaterials.Make("wMetal", null, null, 0.7f, 0.85f, "#b8bcc4", false);
            trailMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.62f, 0.96f, 1f, 0.4f) };
            for (int i = 0; i < 2; i++)
            {
                var w = new GameObject("wheel").transform; w.SetParent(g, false); w.localPosition = new Vector3(0, 0.23f, i == 0 ? 0.58f : -0.52f); wheels[i] = w;
                Prim(PrimitiveType.Cylinder, w, Vector3.zero, new Vector3(0.46f, 0.06f, 0.46f), rubber, new Vector3(0, 0, 90));
                Prim(PrimitiveType.Cylinder, w, Vector3.zero, new Vector3(0.28f, 0.066f, 0.28f), metal, new Vector3(0, 0, 90));
                Prim(PrimitiveType.Cube, w, Vector3.zero, new Vector3(0.14f, 0.04f, 0.36f), metal, null);   // spoke bar so the spin reads
            }
            Prim(PrimitiveType.Cube, g, new Vector3(0, 0.33f, 0.05f), new Vector3(0.34f, 0.08f, 0.72f), body);                       // floorboard
            Prim(PrimitiveType.Capsule, g, new Vector3(0, 0.55f, -0.32f), new Vector3(0.4f, 0.36f, 0.34f), body, new Vector3(90, 0, 0)); // rear body
            Prim(PrimitiveType.Cube, g, new Vector3(0, 0.77f, -0.3f), new Vector3(0.3f, 0.07f, 0.56f), rubber);                       // seat
            Prim(PrimitiveType.Cube, g, new Vector3(0, 0.63f, 0.47f), new Vector3(0.38f, 0.62f, 0.07f), trim, new Vector3(-14, 0, 0)); // front shield
            Prim(PrimitiveType.Cylinder, g, new Vector3(0, 0.98f, 0.52f), new Vector3(0.05f, 0.31f, 0.05f), metal, new Vector3(-14, 0, 0));
            Prim(PrimitiveType.Cube, g, new Vector3(0, 1.27f, 0.45f), new Vector3(0.64f, 0.035f, 0.035f), metal);                      // handlebar
            Prim(PrimitiveType.Cube, g, new Vector3(0, 1.15f, 0.6f), new Vector3(0.14f, 0.08f, 0.04f), Unlit(M.Hex("#fff6dc"), 3f), null, false);   // headlamp
            Prim(PrimitiveType.Cube, g, new Vector3(0, 0.27f, 0f), new Vector3(0.3f, 0.02f, 0.8f), Unlit(M.Hex("#28f0ff"), 3f), null, false);       // underglow
            Prim(PrimitiveType.Cube, g, new Vector3(0, 0.62f, -0.62f), new Vector3(0.16f, 0.05f, 0.03f), Unlit(M.Hex("#ff2a3a"), 3f), null, false); // tail light

            // the rider: sitting, facing forward
            av = Avatar.Build("#f1f1ee", 7); av.root.transform.SetParent(g, false); av.root.transform.localPosition = new Vector3(0, -0.14f, -0.3f);
            av.Pose("sit");
            av.rHair.enabled = false;   // hair under the helmet
            var jacket = av.rJacket.material; jacket.SetColor("_BaseColor", M.Hex("#3f5f8f")); var jeans = av.rPants.material; jeans.SetColor("_BaseColor", M.Hex("#1f2a44"));
            foreach (var r in av.renderers) { if (r is SkinnedMeshRenderer) { var m = r.material; if (m.HasProperty("_EmissionColor")) flashMats.Add(m); } }
            SetArms(0f);
            // helmet (black, open face, smoked visor flipped up on the forehead), pink mirrored shades, gold chain
            Transform head = av.bones[Avatar.BHead];
            Vector3 hc = av.root.transform.TransformPoint(new Vector3(0, 1.725f, -0.012f));
            Material shell = HvMaterials.Make("wHelmet", null, null, 0.45f, 0f, "#070708", false, false, true), visorM = HvMaterials.Make("wVisor", null, null, 0.5f, 0f, "#101216", false, false, true);
            var helm = new GameObject("helmet"); helm.transform.SetParent(head, false); helm.transform.position = hc; helm.transform.rotation = av.root.transform.rotation; helm.transform.localScale = new Vector3(0.138f, 0.142f, 0.15f);
            helm.AddComponent<MeshFilter>().sharedMesh = SpherePart(d => !(d.z > 0.4f && d.y < 0.36f && d.y > -0.62f && Mathf.Abs(d.x) < 0.85f) && d.y > -0.97f);
            var neck = Prim(PrimitiveType.Cylinder, av.bones[Avatar.BNeck], Vector3.zero, new Vector3(0.1f, 0.06f, 0.1f), HvMaterials.Make("wNeck", null, null, 0.3f, 0f, "#8a5a3c", false), null, false);
            neck.transform.position = av.root.transform.TransformPoint(new Vector3(0, 1.56f, -0.01f)); neck.transform.rotation = av.root.transform.rotation; av.renderers.Add(neck.GetComponent<Renderer>());
            var hr = helm.AddComponent<MeshRenderer>(); hr.sharedMaterial = shell; av.renderers.Add(hr);
            var vz = new GameObject("visor"); vz.transform.SetParent(head, false); vz.transform.position = hc + av.root.transform.up * 0.004f; vz.transform.rotation = av.root.transform.rotation; vz.transform.localScale = new Vector3(0.1425f, 0.1465f, 0.1545f);
            vz.AddComponent<MeshFilter>().sharedMesh = SpherePart(d => d.z > 0.42f && d.y >= 0.36f && d.y < 0.78f && Mathf.Abs(d.x) < 0.8f, 10, 24);
            var vr = vz.AddComponent<MeshRenderer>(); vr.sharedMaterial = visorM; av.renderers.Add(vr);
            // his real face (the photo: eyes, pink mirrored shades, nose) sits in the helmet opening
            foreach (var er in av.eyeRenderers) er.enabled = false;
            av.rHead.enabled = false;   // the photo replaces the sculpted face (the helmet hides the rest of the head)
            var faceM = HvMaterials.Make("wFace", "weere_face", null, 0.25f, 0f, "#ffffff", false, false, true); faceM.SetColor("_EmissionColor", Color.white * 0.0f);
            var fq = Prim(PrimitiveType.Quad, head, Vector3.zero, new Vector3(0.196f, 0.151f, 1f), faceM, null, false);
            fq.name = "face"; fq.transform.position = av.root.transform.TransformPoint(new Vector3(0, 1.712f, 0.118f)); fq.transform.rotation = av.root.transform.rotation * Quaternion.Euler(0, 180f, 0); av.renderers.Add(fq.GetComponent<Renderer>());
            Material gold = HvMaterials.Make("wGold", null, null, 0.9f, 1f, "#e3b341", false);
            for (int i = 0; i < 14; i++)
            {
                float a = Mathf.Lerp(-1.1f, 1.1f, i / 13f); var s = Prim(PrimitiveType.Sphere, av.bones[Avatar.BChest], Vector3.zero, Vector3.one * 0.013f, gold, null, false);
                s.transform.position = av.root.transform.TransformPoint(new Vector3(Mathf.Sin(a) * 0.075f, 1.5f - Mathf.Abs(a) * 0.012f + (1 - Mathf.Cos(a)) * -0.01f, 0.07f * Mathf.Cos(a) + 0.015f)); av.renderers.Add(s.GetComponent<Renderer>());
            }

            // the flash streak he leaves behind
            trail = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(trail.GetComponent<Collider>()); trail.transform.SetParent(g, false); trail.name = "streak";
            trail.GetComponent<MeshRenderer>().sharedMaterial = trailMat; trail.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            col = gameObject.AddComponent<CapsuleCollider>(); col.height = 1.9f; col.radius = 0.55f; col.center = new Vector3(0, 0.95f, 0); gameObject.layer = CritterSystem.CritterLayer;
        }

        void SetArms(float rap)
        {
            var b = av.bones;
            foreach (int sd in new[] { 1, -1 })
            {
                bool right = sd > 0; float s0 = -62f, e0 = -30f;
                if (right && rap > 0) { float bt = Mathf.Sin(t * 2.3f * Mathf.PI); s0 = -100f - bt * 22f; e0 = -62f - Mathf.Max(0, bt) * 28f; }
                b[Avatar.BoneIndex(sd, 0)].localRotation = Quaternion.Euler(s0, 0, -sd * 8f); b[Avatar.BoneIndex(sd, 1)].localRotation = Quaternion.Euler(e0, 0, 0);
            }
        }

        // ------------------------------------------------------------------ roads
        Vector3 Lane(string a, string b, float f)
        {
            Vector2 A = NODES[a], B = NODES[b]; Vector2 d = B - A; float l = d.magnitude;
            Vector2 left = new Vector2(-d.y, d.x) / l * LANE;   // keeps to the left side of the road
            Vector2 p = A + d * f + left; return new Vector3(p.x, 0, p.y);
        }
        void NextRoad()
        {
            var opts = ADJ[to].FindAll(n => n != from); string nxt = opts.Count > 0 ? opts[Random.Range(0, opts.Count)] : from;
            from = to; to = nxt; seg = 0;
        }
        float GroundY(Vector3 p, float refY)
        {
            RaycastHit h; int mask = ~((1 << PlayerController.PlayerLayer) | (1 << CritterSystem.CritterLayer) | (1 << Garage.VehicleLayer));
            return Physics.Raycast(new Vector3(p.x, refY + 2.5f, p.z), Vector3.down, out h, 12f, mask, QueryTriggerInteraction.Ignore) ? h.point.y : Mathf.Max(WorldData.HeightAtU(p.x, p.z), refY - 0.5f);
        }

        bool Daytime() { return world.nightFactor < 0.04f && world.dayLight > 0.55f; }

        void Appear()
        {
            var keys = new List<string>(NODES.Keys); Vector3 pp = player.transform.position;
            for (int k = 0; k < 12; k++)
            {
                from = keys[Random.Range(0, keys.Count)]; to = ADJ[from][Random.Range(0, ADJ[from].Count)]; seg = Random.value;
                Vector3 p = Lane(from, to, seg); p.y = WorldData.TownHeight + 0.05f; if ((p - pp).magnitude > 50f || k == 11) { pos = p; break; }
            }
            pos.y = GroundY(pos, WorldData.TownHeight); state = St.Ride; speed = FLASH; visitT = Random.Range(45f, 100f); meetCd = Random.Range(10f, 28f);
            visual.SetActive(true); trail.SetActive(true); transform.position = pos;
        }
        void Vanish(float nextIn) { state = St.Away; awayT = nextIn; visual.SetActive(false); trail.SetActive(false); col.enabled = false; }

        // ------------------------------------------------------------------ damage
        public void OnShot(float damage, Vector3 hitPoint, Vector3 dir, float force) { Damage(damage); }
        public void Damage(float dmg)
        {
            if (state == St.Dead || state == St.Away) return;
            hp -= dmg; hurtT = 8f; flashT = 0.15f; Sfx.Hit();
            if (hp <= 0)
            {
                hp = 0; state = St.Dead; speed = 0; trail.SetActive(false); visual.SetActive(true);
                visual.transform.localRotation = Quaternion.Euler(0, 0, 80f); visual.transform.localPosition = new Vector3(0, 0.25f, 0);
                gameOver = true; gameOverT = 0; player.frozen = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; Sfx.Scream(0.6f);
                return;
            }
            player.Toast(string.Format("🚫 Don't shoot {0}! ({1}% left · if he dies, GAME OVER)", RiderName, Mathf.CeilToInt(hp)));
            if (state == St.Rap || state == St.Approach) Leave();
        }
        void Leave() { state = St.Leave; meetCd = Random.Range(70f, 140f); }

        // ------------------------------------------------------------------ behaviour
        void Update()
        {
            if (world == null || player == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f); t += dt;
            if (gameOver) { gameOverT += dt; if (gameOverT > 1.2f && (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame || Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)) Restart(); return; }
            bool day = Daytime();
            if (state == St.Away)
            {
                if (day && !player.dead) { awayT -= dt; if (awayT <= 0) Appear(); }
                return;
            }
            if (state == St.Dead) return;

            meetCd -= dt; visitT -= dt; hurtT -= dt; flashT -= dt;
            if (hurtT <= 0 && hp < MAX_HP) hp = Mathf.Min(MAX_HP, hp + 12f * dt);
            foreach (var m in flashMats) m.SetColor("_EmissionColor", flashT > 0 ? new Color(0.67f, 0.06f, 0.12f) : Color.black);
            Vector3 pp = player.transform.position; float dxp = pp.x - pos.x, dzp = pp.z - pos.z, distP = Mathf.Sqrt(dxp * dxp + dzp * dzp);
            bool canMeet = day && !player.dead && player.vehicle == null && !player.inCave && !player.frozen;
            float rate;

            if (state == St.Ride)
            {
                Vector3 a = Lane(from, to, 0), b = Lane(from, to, 1); float L = Vector3.Distance(a, b);
                seg += FLASH * dt / L;
                while (seg >= 1f) { float over = (seg - 1f) * L; NextRoad(); Vector3 c = Lane(from, to, 0), e = Lane(from, to, 1); seg = over / Vector3.Distance(c, e); }
                a = Lane(from, to, 0); b = Lane(from, to, 1); pos.x = Mathf.Lerp(a.x, b.x, seg); pos.z = Mathf.Lerp(a.z, b.z, seg);
                TurnTo(Mathf.Atan2(b.x - a.x, b.z - a.z), dt, 18f); speed = FLASH;
                if (!day || (visitT <= 0 && distP > 40f)) { Vanish(Random.Range(150f, 360f)); return; }   // dusk or the visit is over: gone (never seen at night)
                if (canMeet && meetCd <= 0 && distP < 45f && distP > 6f)
                {
                    Vector3 o = new Vector3(pos.x, pos.y + 1f, pos.z), d = new Vector3(dxp, pp.y + 0.6f - o.y, dzp); float dd = d.magnitude;
                    if (!Physics.Raycast(o, d / dd, dd - 1f, ~((1 << PlayerController.PlayerLayer) | (1 << CritterSystem.CritterLayer) | (1 << Garage.VehicleLayer)), QueryTriggerInteraction.Ignore))
                    { state = St.Approach; leaveFrom = pos; rFrom = from; rTo = to; rSeg = seg; }
                }
            }
            else if (state == St.Approach)
            {
                float stop = 2.6f, rem = distP - stop;
                speed = Mathf.Min(FLASH, Mathf.Sqrt(Mathf.Max(0f, 2f * 70f * rem)) + 2f);
                TurnTo(Mathf.Atan2(dxp, dzp), dt, 14f);
                float step = Mathf.Min(rem, speed * dt);
                if (distP > 0.01f) { pos.x += dxp / distP * step; pos.z += dzp / distP * step; }
                if (rem < 0.15f || !canMeet)
                {
                    if (!canMeet) Leave();
                    else
                    {
                        state = St.Rap; speed = 0; met++; rapT = 7f; rapLine = Random.Range(0, Raps.Length); lineT = 0;
                        Sfx.Crackle(0.35f); Sfx.Hatch();
                        if (met == 1) player.Toast("⚠️ " + RiderName + " the Scooty Boy! Never hurt him: if he dies, you lose the whole game.");
                    }
                }
            }
            else if (state == St.Rap)
            {
                speed = 0; TurnTo(Mathf.Atan2(dxp, dzp), dt, 5f); rapT -= dt; lineT += dt; if (lineT > 2.2f) { lineT = 0; rapLine = (rapLine + 1) % Raps.Length; }
                if (rapT <= 0 || distP > 18f || !canMeet) Leave();
            }
            else if (state == St.Leave)
            {
                float tx = leaveFrom.x - pos.x, tz = leaveFrom.z - pos.z, td = Mathf.Sqrt(tx * tx + tz * tz);
                speed = Mathf.Min(FLASH, speed + 60f * dt); TurnTo(Mathf.Atan2(tx, tz), dt, 14f);
                if (td < speed * dt + 0.3f) { pos = leaveFrom; from = rFrom; to = rTo; seg = rSeg; state = St.Ride; if (!day) { Vanish(Random.Range(150f, 360f)); return; } }
                else { pos.x += tx / td * speed * dt; pos.z += tz / td * speed * dt; }
            }
            rate = Mathf.Min(1f, dt * 12f); pos.y += (GroundY(pos, pos.y) - pos.y) * rate;
            Animate(dt, distP);
        }

        void TurnTo(float want, float dt, float rate)
        {
            float dy = Mathf.DeltaAngle(yaw * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad; yaw += dy * Mathf.Min(1f, dt * rate);
        }

        void Animate(float dt, float distP)
        {
            transform.position = pos; transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
            bool fast = speed > 15f;
            visual.SetActive(!fast || Random.value < 0.55f);   // a flash: you only catch flickers of him
            col.enabled = !(fast && distP < 4f);
            trail.SetActive(fast);
            if (fast)
            {
                float len = Mathf.Min(14f, speed * 0.22f); trail.transform.localScale = new Vector3(0.4f, 1.3f, len); trail.transform.localPosition = new Vector3(0, 0.9f, -len / 2f - 0.3f);
                trailMat.color = new Color(0.62f, 0.96f, 1f, 0.2f + Random.value * 0.25f);
            }
            foreach (var w in wheels) w.Rotate(speed / 0.23f * dt * Mathf.Rad2Deg, 0, 0, Space.Self);
            bool rap = state == St.Rap;
            av.bones[Avatar.BHead].localRotation = Quaternion.Euler(rap ? Mathf.Max(0, Mathf.Sin(t * 2.3f * Mathf.PI)) * 14f - 3f : -6f, 0, 0);
            av.bones[Avatar.BSpine].localRotation = Quaternion.Euler(rap ? -6f : fast ? 18f : 4f, 0, 0);
            SetArms(rap ? 1f : 0f);
        }

        void Restart() { SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); }

        // ------------------------------------------------------------------ test hooks / state
        public bool IsAway { get { return state == St.Away; } }
        public string StateName { get { return state.ToString(); } }
        public Vector3 Pos { get { return pos; } }
        public void ForceAppear() { if (state == St.Away) Appear(); }
        public void ForceMeet() { meetCd = 0; }

        // ------------------------------------------------------------------ GUI: rap bubble + game over
        GUIStyle ts, bs, big;
        void OnGUI()
        {
            if (ts == null)
            {
                ts = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true }; ts.normal.textColor = new Color(1f, 0.48f, 0.85f);
                bs = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true }; bs.normal.textColor = Color.white;
                big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 64, fontStyle = FontStyle.Bold }; big.normal.textColor = new Color(1f, 0.3f, 0.35f);
            }
            if (state == St.Rap && Camera.main != null)
            {
                Vector3 sp = Camera.main.WorldToScreenPoint(pos + Vector3.up * 2.45f);
                if (sp.z > 0)
                {
                    float x = sp.x, y = Screen.height - sp.y; GUI.color = new Color(0.12f, 0.04f, 0.16f, 0.8f); GUI.DrawTexture(new Rect(x - 130, y - 26, 260, 34), Texture2D.whiteTexture);
                    GUI.color = Color.white; GUI.Label(new Rect(x - 130, y - 26, 260, 34), "🛵 " + RiderName, ts);
                    GUI.Label(new Rect(x - 190, y + 10, 380, 60), "“" + Raps[rapLine] + "”", bs);
                }
            }
            if (gameOver)
            {
                GUI.color = new Color(0.1f, 0f, 0.02f, Mathf.Min(0.88f, gameOverT * 0.8f)); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = Color.white;
                GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 90), "GAME OVER", big);
                GUI.Label(new Rect(0, Screen.height * 0.3f + 90, Screen.width, 40), RiderName + " the Scooty Boy didn't make it. You lose the whole game.", bs);
                if (gameOverT > 1.2f) GUI.Label(new Rect(0, Screen.height * 0.3f + 140, Screen.width, 40), "Click or press R to start again", bs);
            }
        }
    }
}
