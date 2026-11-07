using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    public sealed class GhostModel
    {
        public GameObject root; public Transform head; public List<Transform> limbs = new List<Transform>(); public float height; public Material emberMat; public Avatar av;
        public void Crawl(float life)
        {   // the Cinder Woman on all fours: torso pitched forward, arms reaching, legs folded, alternating strides
            if (av == null) return; float sw = Mathf.Sin(life * 14f);
            av.root.transform.localRotation = Quaternion.Euler(70f, 0, 0); av.root.transform.localPosition = new Vector3(0, 0.05f, -0.2f); var b = av.bones;
            b[Avatar.BHead].localRotation = Quaternion.Euler(-62f, Mathf.Sin(life * 2f) * 10f, 0); b[Avatar.BNeck].localRotation = Quaternion.Euler(-20f, 0, 0);
            foreach (int sd in new[] { 1, -1 })
            {
                float ph = sd > 0 ? sw : -sw;
                b[Avatar.BoneIndex(sd, 0)].localRotation = Quaternion.Euler(-95f + ph * 28f, 0, sd * 12f); b[Avatar.BoneIndex(sd, 1)].localRotation = Quaternion.Euler(-20f + Mathf.Max(0, ph) * -30f, 0, 0);
                b[Avatar.BoneIndex(sd, 3)].localRotation = Quaternion.Euler(-70f - ph * 24f, 0, sd * 14f); b[Avatar.BoneIndex(sd, 4)].localRotation = Quaternion.Euler(95f + Mathf.Max(0, -ph) * 25f, 0, 0);
            }
            b[Avatar.BSpine].localRotation = Quaternion.Euler(8f, sw * 6f, 0);
        }
    }

    /// <summary>
    /// Night horror (port of ghosts.js): three ghosts haunt the town after dark, one at a time, at random moments.
    /// Bullets pass straight through them. If one gets you: jump scare + damage.
    ///   0 The Hollow Man   - very tall and thin, watches from the end of a lane. Stare at him and he vanishes... then he is behind you.
    ///   1 The Cinder Woman - charred, glowing, crawls toward you whenever you are NOT looking at her.
    ///   2 The Patient      - every time the lights flicker she is closer.
    /// Ghosts are personal: each player has their own nightmares.
    /// </summary>
    public sealed class Ghosts : MonoBehaviour
    {
        public HvWorld world; public PlayerController player; public Arsenal arsenal;
        public Texture2D[] faces = new Texture2D[4];
        GhostModel[] models = new GhostModel[3]; Light glow;
        public sealed class Active { public int kind; public GhostModel m; public string phase = "appear"; public float t, seenT, hopT = 1.8f, life; public Vector3 pos; public int hops; public bool stung; }
        public Active active; float next = 18f, ambientT = 10f, beatT, scareT; int last = -1;
        public float nearness; public float lampFlicker; Texture2D scareFace2;
        Texture2D scareFace; int scareKind;
        static float Rnd(float a, float b) { return Random.Range(a, b); }

        Material baseMat, faceBase;

        void Start()
        {
            for (int i = 0; i < 4; i++) faces[i] = GhostArt.Face(i);
            models[0] = BuildHollow(); models[1] = BuildCinder(); models[2] = BuildPatient();
            foreach (var m in models) { m.root.SetActive(false); m.root.transform.SetParent(transform, false); }
            var lg = new GameObject("GhostGlow"); lg.transform.SetParent(transform, false); glow = lg.AddComponent<Light>(); glow.type = LightType.Point; glow.range = 9; glow.intensity = 0; glow.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------ bodies
        Material M_(string hex, float smooth = 0.1f, Color? emission = null)
        { var m = HvMaterials.Make("ghost", null, null, smooth, 0, hex, false); if (emission.HasValue) m.SetColor("_EmissionColor", emission.Value); return m; }
        Material RagMat(string hex)
        {
            var m = NatureBuilder.PlantMat("rag", null, null, Color.white, 0.05f, 0f, 0f, 0f, true, true, 0f);
            m.SetTexture("_BaseMap", GhostArt.Rag(hex)); return m;
        }
        static Transform Node(string n, Transform parent, float x, float y, float z) { var t = new GameObject(n).transform; t.SetParent(parent, false); t.localPosition = new Vector3(x, y, -z); return t; }
        static MeshRenderer Mesh(Mesh mesh, Material mat, Transform parent, float x, float y, float z, Vector3 scale, float rx = 0, float rz = 0)
        {
            var o = new GameObject("p"); o.transform.SetParent(parent, false); o.transform.localPosition = new Vector3(x, y, -z); o.transform.localRotation = Quaternion.Euler(-rx * Mathf.Rad2Deg, 0, rz * Mathf.Rad2Deg); o.transform.localScale = scale;
            o.AddComponent<MeshFilter>().sharedMesh = mesh; var r = o.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; return r;
        }
        static Mesh sph, cylM;
        static void Prims()
        {
            if (sph != null && cylM != null) return;
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere); sph = s.GetComponent<MeshFilter>().sharedMesh; Destroy(s);
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cylM = c.GetComponent<MeshFilter>().sharedMesh; Destroy(c);
        }
        static Transform Limb(Material mat, float len, float r, Transform parent, float x, float y, float z)
        { var p = Node("limb", parent, x, y, z); Mesh(Avatar.CapsuleMesh(r, len), mat, p, 0, -len / 2, 0, Vector3.one); return p; }
        static Transform Sph(Material mat, Transform parent, float x, float y, float z, float r, float sx = 1, float sy = 1, float sz = 1) { return Mesh(sph, mat, parent, x, y, z, new Vector3(2 * r * sx, 2 * r * sy, 2 * r * sz)).transform; }

        // faces ignore lighting: pale and glowing even in pitch dark
        Transform Face(Transform head, int kind, float w, float h, float z)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>()); q.transform.SetParent(head, false);
            q.transform.localPosition = new Vector3(0, 0, -z); q.transform.localRotation = Quaternion.Euler(0, 180, 0); q.transform.localScale = new Vector3(w, h, 1);
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "ghostFace" };
            m.SetTexture("_BaseMap", faces[kind]); m.SetColor("_BaseColor", Color.white); m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_SrcBlend", 5f); m.SetFloat("_DstBlend", 10f); m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000; m.SetFloat("_Cull", 0f);
            var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; return q.transform;
        }
        static void GlowEyes(Transform parent, float y, float z, float spread, Color col, float size)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", col * 4f);
            foreach (int s in new[] { -1, 1 }) Mesh(sph, m, parent, s * spread, y, z, Vector3.one * size);
        }

        // ---- the ghosts are built on the realistic human body: same sculpt and skeleton, reshaped and re-skinned
        static GameObject Wrap(Avatar av) { var w = new GameObject("ghost"); av.root.transform.SetParent(w.transform, false); return w; }
        static Material Dark(Avatar av, string hex, float smooth = 0.9f) { return av.SkinMaterial(hex, smooth, 0f, "skin_n", 8f, 0f, 0.3f, 0.6f, hex); }
        static void GlowDots(Avatar av, Color col, float size)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", col * 4f);
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere); var sm = s.GetComponent<MeshFilter>().sharedMesh; Destroy(s);
            foreach (int sd in new[] { 1, -1 }) { var g = new GameObject("glint"); g.transform.SetParent(av.bones[Avatar.BHead], false); g.transform.position = av.root.transform.TransformPoint(new Vector3(sd * 0.034f, 1.701f, 0.099f)); g.transform.localScale = Vector3.one * size; g.AddComponent<MeshFilter>().sharedMesh = sm; g.AddComponent<MeshRenderer>().sharedMaterial = m; }
        }
        static void EyesMat(Avatar av, Material m) { foreach (var r in av.eyeRenderers) r.sharedMaterial = m; }

        GhostModel BuildHollow()
        {
            var av = Avatar.Build("#050507", 11); var m = new GhostModel { av = av, root = Wrap(av), height = 3.0f, head = av.bones[Avatar.BHead] };
            var pale = av.SkinMaterial("#9a968c", 0.3f, 0.25f, "skin_n", 26f, 0.9f, 0.4f, 0.4f, "#c8c0b0"); av.rSkin.sharedMaterial = pale; av.rHead.sharedMaterial = pale;
            var coat = av.SkinMaterial("#07070a", 0.1f, 0f, "cloth_n", 30f, 1.2f, 0.2f, 0.05f, "#101018");
            av.rShirt.sharedMaterial = coat; av.rJacket.sharedMaterial = coat; av.rPants.sharedMaterial = coat; av.rShoes.sharedMaterial = coat; av.rHair.enabled = false;
            EyesMat(av, Dark(av, "#020202", 0.95f)); GlowDots(av, new Color(0.95f, 0.97f, 1f), 0.0045f);
            av.root.transform.localScale = new Vector3(0.84f, 1.64f, 0.84f);
            foreach (int sd in new[] { 1, -1 }) { av.bones[Avatar.BoneIndex(sd, 0)].localScale = new Vector3(1, 1.38f, 1); av.bones[Avatar.BoneIndex(sd, 2)].localScale = new Vector3(1.0f, 1.5f, 1.0f); av.bones[Avatar.BoneIndex(sd, 3)].localScale = new Vector3(1, 1.1f, 1); }
            av.bones[Avatar.BNeck].localScale = new Vector3(0.9f, 1.4f, 0.9f);
            return m;
        }

        GhostModel BuildCinder()
        {
            var av = Avatar.Build("#150d0a", 2); var m = new GhostModel { av = av, root = Wrap(av), height = 1.3f, head = av.bones[Avatar.BHead] };
            var chr = av.SkinMaterial("#17100c", 0.15f, 0f, "hide_n", 14f, 1.4f, 0.2f, 0.1f, "#ff5a1a"); chr.SetTexture("_EmissionMap", GhostArt.Ember()); chr.SetColor("_EmissionColor", Color.white * 1.8f);
            av.rSkin.sharedMaterial = chr; av.rHead.sharedMaterial = chr; m.emberMat = chr;
            av.rShirt.enabled = false; av.rJacket.enabled = false; av.rShoes.enabled = false;
            var rag = av.SkinMaterial("#0a0605", 0.05f, 0f, "cloth_n", 30f, 1.4f, 0.1f, 0.02f, "#200a05"); av.rPants.sharedMaterial = rag; av.rHair.sharedMaterial = av.SkinMaterial("#050304", 0.2f, 0f, "hide_n", 140f, 1.8f, 0.2f, 0.1f, "#050304");
            var ember = Dark(av, "#ff9a30", 0.5f); ember.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.12f) * 4f); EyesMat(av, ember);
            av.root.transform.localScale = Vector3.one * 1.1f; m.Crawl(0);
            return m;
        }

        GhostModel BuildPatient()
        {
            var av = Avatar.Build("#a9b0aa", 1); var m = new GhostModel { av = av, root = Wrap(av), height = 2.1f, head = av.bones[Avatar.BHead] };
            var gs = av.SkinMaterial("#6d7864", 0.25f, 0.4f, "skin_n", 26f, 1.0f, 0.4f, 0.3f, "#8aa07a"); av.rSkin.sharedMaterial = gs; av.rHead.sharedMaterial = gs;
            var gown = av.SkinMaterial("#7a807a", 0.08f, 0.05f, "cloth_n", 28f, 1.2f, 0.3f, 0.05f, "#aab0aa"); av.rShirt.sharedMaterial = gown; av.rJacket.sharedMaterial = gown; av.rPants.enabled = false; av.rShoes.enabled = false;
            av.rHair.sharedMaterial = av.SkinMaterial("#040303", 0.3f, 0f, "hide_n", 140f, 1.8f, 0.2f, 0.2f, "#040303");
            var white = av.SkinMaterial("#d9bdb0", 0.8f, 0f, "skin_n", 8f, 0.1f); av.eyeRenderers[0].sharedMaterial = white; av.eyeRenderers[3].sharedMaterial = white;
            av.root.transform.localScale = new Vector3(0.95f, 1.12f, 0.95f);
            // hunched, neck bent far too far, arms hanging long
            av.bones[Avatar.BSpine].localRotation = Quaternion.Euler(24f, 0, 0); av.bones[Avatar.BChest].localRotation = Quaternion.Euler(12f, 0, 0); av.bones[Avatar.BNeck].localRotation = Quaternion.Euler(8f, 0, 22f);
            foreach (int sd in new[] { 1, -1 }) { av.bones[Avatar.BoneIndex(sd, 0)].localRotation = Quaternion.Euler(-18f, 0, 0); av.bones[Avatar.BoneIndex(sd, 0)].localScale = new Vector3(1, 1.22f, 1); av.bones[Avatar.BoneIndex(sd, 2)].localScale = new Vector3(1, 1.4f, 1); }
            return m;
        }

        // ------------------------------------------------------------------ the haunting
        public float suppress; public bool blocked;   // set by the Arena: its show keeps the night horror away
        public bool Seen(Vector3 p, float height, float fovDot = 0.82f)
        {
            var cam = Camera.main; if (cam == null) return false;
            Vector3 v = new Vector3(p.x, p.y + height * 0.8f, p.z) - cam.transform.position; float d = v.magnitude;
            if (Vector3.Dot(v / d, cam.transform.forward) < fovDot) return false;
            return !Physics.Linecast(cam.transform.position, cam.transform.position + v - v.normalized * 0.5f, ~((1 << PlayerController.PlayerLayer) | (1 << CritterSystem.CritterLayer)), QueryTriggerInteraction.Ignore);
        }

        bool FindSpot(float ang, float dist, float spread, out Vector3 spot)
        {
            Vector3 pp = player.transform.position;
            for (int k = 0; k < 24; k++)
            {
                float a = ang + Rnd(-spread, spread), d = dist * Rnd(0.8f, 1.15f); float x = pp.x + Mathf.Sin(a) * d, z = pp.z + Mathf.Cos(a) * d;
                if (world.IsBlockedU(x, z) || Mathf.Sqrt(x * x + z * z) > 400) continue;
                RaycastHit h; float y = Physics.Raycast(new Vector3(x, pp.y + 6, z), Vector3.down, out h, 40f, ~((1 << PlayerController.PlayerLayer) | (1 << CritterSystem.CritterLayer)), QueryTriggerInteraction.Ignore) ? h.point.y : WorldData.HeightAtU(x, z);
                spot = new Vector3(x, y, z); return true;
            }
            spot = Vector3.zero; return false;
        }

        public bool suspended; public GhostModel Model(int k) { return models[k]; }
        public void Jumpscare(int k, float dmg) { scareFace = faces[k]; scareKind = k; scareT = 1.1f; Sfx.Scream(1f); player.Damage(dmg); player.recoil = 1.5f; }
        public bool freeze; public Vector3 ActivePos => active != null ? active.pos : Vector3.zero; // test hooks
        public void Force(int kind) { End(false); StartGhost(kind); }

        void StartGhost(int kind)
        {
            float yaw = player.Yaw; Vector3 spot;
            // camera looks along (sin yaw, cos yaw): "ahead" is yaw, "behind" is yaw + PI
            bool ok = kind == 0 ? FindSpot(yaw, Rnd(12, 16), 0.45f, out spot) : kind == 1 ? FindSpot(yaw + Mathf.PI, Rnd(18, 24), 0.9f, out spot) : FindSpot(yaw + Rnd(-0.9f, 0.9f), Rnd(14, 18), 0.4f, out spot);
            if (!ok) { next = 3; return; }
            var m = models[kind]; m.root.transform.position = spot; m.root.SetActive(true);
            active = new Active { kind = kind, m = m, pos = spot }; last = kind;
            if (kind == 0) Sfx.Static(0.25f);
            if (kind == 2) lampFlicker = 1;
        }

        public void End(bool fade = true)
        {
            if (active == null) return;
            active.m.root.SetActive(false); glow.intensity = 0; if (fade) Sfx.Static(0.15f); active = null; next = Rnd(25, 60); nearness = 0;
        }

        void Scare(float dmg)
        {
            int k = active.kind; End(false);
            scareFace = faces[k]; scareKind = k; scareT = 1.1f; Sfx.Scream(1f); player.Damage(dmg); player.recoil = 1.5f;
        }

        void Update()
        {
            if (world == null || player == null || suspended) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float nightK = world.nightFactor * (1 - suppress); bool can = !player.dead && !blocked;
            lampFlicker = Mathf.Max(0, lampFlicker - dt * 0.8f); world.lampFlicker = lampFlicker; if (scareT > 0) scareT -= dt;
            world.ghostK = Mathf.MoveTowards(world.ghostK, active != null ? 1f : 0f, dt * (active != null ? 0.6f : 0.35f));
            if (active == null)
            {
                if (!can || nightK < 0.6f) return; next -= dt; if (next > 0) return;
                int kind = Random.Range(0, 3); if (kind == last) kind = (kind + 1 + Random.Range(0, 2)) % 3; StartGhost(kind); return;
            }
            if (freeze) { glow.intensity = (active.kind == 0 ? 6f : 9f); glow.transform.position = active.pos + new Vector3(0, active.m.height * 0.75f, 2f); return; }
            var a = active; var g = a.m.root.transform; var cam = Camera.main;
            if (!can || nightK < 0.3f) { End(); return; }
            a.t += dt; a.life += dt;
            Vector3 pp = player.transform.position; Vector3 toP = new Vector3(pp.x - a.pos.x, 0, pp.z - a.pos.z); float dist = toP.magnitude;
            nearness = Mathf.Max(0, 1 - dist / 25f);
            Vector3 cp = cam.transform.position; g.rotation = Quaternion.LookRotation(new Vector3(cp.x - a.pos.x, 0, cp.z - a.pos.z) + Vector3.forward * 0.0001f);
            bool visible = Seen(a.pos, a.m.height);
            if (a.kind != 1)
            {
                float d = Mathf.Max(dist, 0.01f); glow.color = M.Hex("#8fa6bf").gamma;
                glow.transform.position = new Vector3(a.pos.x + (pp.x - a.pos.x) / d * 1.4f, a.pos.y + a.m.height * 0.75f, a.pos.z + (pp.z - a.pos.z) / d * 1.4f);
                glow.intensity = (a.kind == 0 ? 6f : 9f) * (g.gameObject.activeSelf ? 1 : 0) * (0.85f + Random.value * 0.15f);
            }
            else glow.color = new Color(1f, 0.35f, 0.1f);
            beatT -= dt; if (nearness > 0.15f && beatT <= 0) { beatT = 1.1f - nearness * 0.7f; Sfx.Heartbeat(0.3f + nearness * 0.5f); }

            if (a.kind == 0)
            { // The Hollow Man: stands and stares. Look at him or come close and he vanishes...
                a.m.head.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(a.t * 0.7f) * 3.5f);
                g.position = new Vector3(a.pos.x, a.pos.y + Mathf.Sin(a.t * 1.3f) * 0.02f, a.pos.z);
                if (a.phase == "appear")
                {
                    if (visible) { a.seenT += dt; if (a.seenT > 0.3f && !a.stung) { a.stung = true; Sfx.Stinger(0.7f); } }
                    if (a.seenT > 1.6f || dist < 12 || a.t > 25)
                    {
                        Sfx.Static(0.35f);
                        if (Random.value < 0.7f)
                        { // ...and then he's right behind you. Turn around.
                            a.phase = "behind"; a.t = 0; Vector3 back;
                            if (!FindSpot(player.Yaw + Mathf.PI, 2.2f, 0.15f, out back)) { End(); return; }
                            a.pos = back; Sfx.Whisper(0.6f);
                        }
                        else { End(); return; }
                    }
                }
                else
                {
                    g.position = a.pos;
                    if (Seen(a.pos, a.m.height, 0.55f)) { Scare(28); return; }
                    if (a.t > 6) { Sfx.Whisper(0.4f); End(); return; }
                }
            }
            else if (a.kind == 1)
            { // The Cinder Woman: crawls at you whenever you look away, freezes when watched
                a.m.emberMat.SetColor("_EmissionColor", Color.white * (1.2f + Random.value * 0.8f));
                glow.transform.position = a.pos + Vector3.up * 0.8f; glow.intensity = 3f * (0.6f + Random.value * 0.5f);
                if (!visible && a.t > 0.8f)
                {
                    Vector3 dir = toP.normalized; a.pos += dir * dt * 6.5f;
                    RaycastHit h; if (Physics.Raycast(a.pos + Vector3.up * 2, Vector3.down, out h, 8f, ~((1 << PlayerController.PlayerLayer) | (1 << CritterSystem.CritterLayer)))) a.pos.y = h.point.y;
                    a.m.Crawl(a.life);
                    if (Random.value < dt * 3) Sfx.Crackle(0.25f + nearness * 0.4f);
                }
                else if (visible && !a.stung) { a.stung = true; Sfx.Stinger(0.8f); }
                if (visible && Random.value < 0.05f) a.m.head.localRotation = Quaternion.Euler(-62f, Rnd(-35f, 35f), Rnd(-30f, 30f));
                g.position = a.pos;
                if (dist < 1.6f) { Scare(38); return; }
                if (a.life > 45) { End(); return; }
            }
            else
            { // The Patient: the lights stutter and every time they do she is closer
                a.hopT -= dt; g.gameObject.SetActive(lampFlicker < 0.75f || Random.value < 0.5f);
                a.m.head.localRotation = Quaternion.Euler(0, 0, 28f + Mathf.Sin(a.t * 9) * 1.2f);
                if (a.hopT <= 0)
                {
                    a.hops++; a.hopT = Rnd(1.4f, 2.2f); lampFlicker = 1;
                    if (dist > 30) { End(); return; } // you ran far enough: she lets you go... for now
                    Vector3 dir = toP.normalized; float step = Mathf.Min(dist - 1.2f, dist * 0.4f + 1.5f); a.pos += dir * step;
                    RaycastHit h; if (Physics.Raycast(a.pos + Vector3.up * 2, Vector3.down, out h, 8f, ~((1 << PlayerController.PlayerLayer) | (1 << CritterSystem.CritterLayer)))) a.pos.y = h.point.y;
                    Sfx.Static(0.2f + nearness * 0.3f); if (a.hops == 1) Sfx.Stinger(0.6f); if (Random.value < 0.5f) Sfx.Whisper(0.25f + nearness * 0.4f);
                }
                g.position = a.pos;
                if (dist < 1.8f && lampFlicker < 0.6f) { Scare(32); return; }
                if (a.life > 40) { End(); return; }
            }
        }

        // a ghost got you: its face fills the screen
        void OnGUI()
        {
            if (scareT > 0 && scareFace != null)
            {
                float a = Mathf.Clamp01(scareT / 0.5f); GUI.color = new Color(0, 0, 0, a * 0.9f); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                float s = Mathf.Min(Screen.width, Screen.height) * (1.0f + (1.1f - scareT) * 0.35f); GUI.color = new Color(1, 1, 1, a); GUI.DrawTexture(new Rect((Screen.width - s) / 2, (Screen.height - s) / 2, s, s), scareFace); GUI.color = Color.white;
            }
        }
    }
}
