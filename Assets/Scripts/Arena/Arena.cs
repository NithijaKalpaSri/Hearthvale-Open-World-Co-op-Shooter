using System.Collections.Generic;
using UnityEngine;

namespace Hearthvale
{
    /// <summary>
    /// The Arena (port of concert.js): an open-air concert stadium just outside the north-west edge of town.
    ///  16:00-20:00  LIVE: a band plays on the stage. The star is the stage drummer, who wears his drum on his stomach and beats both skins
    ///               (the concert-drummer sound, looping). The sound is 3D and short-ranged: you only hear it near the Arena, never across the city.
    ///               A crowd dances, lights sweep. Near the show the night horror is kept away.
    ///  20:00-06:00  The band is gone and the Arena goes dark. Lost fans stand in the stands... and glide toward you whenever you are NOT looking.
    /// Everything runs on each player's own machine. Animals never go into the Arena.
    /// </summary>
    [DefaultExecutionOrder(90)]
    public sealed class Arena : MonoBehaviour
    {
        public const int ShowStart = 16, ShowEnd = 20, HauntEnd = 6;
        const float StageH = 1.6f, WallH = 7f, WallT = 1.2f, TierD = 1.6f, TierH = 0.42f; const int Tiers = 4;
        const float StageMinX = -64, StageMaxX = -26, StageMinZ = -144.8f, StageMaxZ = -134;      // JS space (z mirrored when built)
        const float GateSMin = -51, GateSMax = -39, GateEMinZ = -118, GateEMaxZ = -110;
        public const float HearRange = 80f;   // the drum fades out completely this far from the stage

        public HvWorld world; public PlayerController player; public Ghosts ghosts;
        public string mode = "idle"; public float cheer, nearness; public bool playerInside;
        float Y { get { return WorldData.TownHeight; } }
        public AudioSource DrumSource { get { return drum; } }
        float t; GameObject root; Transform bandRoot; Material signMat;

        // ---------------------------------------------------------- tiny box builder (JS coordinates in, Unity out)
        readonly Dictionary<Material, List<CombineInstance>> batches = new Dictionary<Material, List<CombineInstance>>(); Mesh cube; int colliderCount;
        void Box(float minX, float maxX, float minZ, float maxZ, float y0, float h, Material mat, bool solid = true)
        {
            Vector3 c = new Vector3((minX + maxX) / 2, y0 + h / 2, -(minZ + maxZ) / 2), s = new Vector3(maxX - minX, h, maxZ - minZ);
            List<CombineInstance> l; if (!batches.TryGetValue(mat, out l)) batches[mat] = l = new List<CombineInstance>();
            l.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(c, Quaternion.identity, s) });
            if (solid) { var g = new GameObject("col"); g.transform.SetParent(root.transform, false); g.transform.position = c; var bc = g.AddComponent<BoxCollider>(); bc.size = s; colliderCount++; }
        }
        void BoxC(float w, float h, float d, float x, float y0, float z, Material mat, bool solid = true) { Box(x - w / 2, x + w / 2, z - d / 2, z + d / 2, y0, h, mat, solid); }
        void Flush()
        {
            foreach (var kv in batches)
            {
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; m.CombineMeshes(kv.Value.ToArray(), true, true); m.RecalculateNormals(); m.RecalculateBounds();   // scaled cubes: rebuild exact flat normals
                var g = new GameObject("arena_" + kv.Key.name); g.transform.SetParent(root.transform, false); g.AddComponent<MeshFilter>().sharedMesh = m; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = kv.Key;
            }
            batches.Clear();
        }
        static Material Lit(string n, string hex, float smooth = 0.2f, float metal = 0f) { return HvMaterials.Make(n, null, null, smooth, metal, hex, false); }

        // ---------------------------------------------------------- lifecycle
        Light[] stageLamps = new Light[2]; AudioSource drum; AudioLowPassFilter lowpass; AudioClip drumClip; bool toldStart;

        void Start()
        {
            root = new GameObject("Arena"); root.transform.SetParent(transform, false);
            var cb = GameObject.CreatePrimitive(PrimitiveType.Cube); cube = cb.GetComponent<MeshFilter>().sharedMesh; Destroy(cb);
            BuildStadium(); BuildStage(); Flush(); BuildBand(); BuildCrowd(); BuildLostFans();
            player.SetStepOffset(0.45f);   // the stands and the stage steps are 0.4 m high
            drumClip = Resources.Load<AudioClip>("Voices/concert_drummer_B");
            var dg = new GameObject("DrumSound"); dg.transform.SetParent(transform, false); drum = dg.AddComponent<AudioSource>(); drum.clip = drumClip; drum.loop = true; drum.playOnAwake = false;
            drum.spatialBlend = 1f; drum.rolloffMode = AudioRolloffMode.Linear; drum.minDistance = 22f; drum.maxDistance = HearRange; drum.dopplerLevel = 0; drum.spread = 60;
            lowpass = dg.AddComponent<AudioLowPassFilter>(); lowpass.cutoffFrequency = 22000;
            for (int i = 0; i < 2; i++)
            {
                var lg = new GameObject("StageLamp" + i); lg.transform.SetParent(root.transform, false); var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 34; l.intensity = 0; l.shadows = LightShadows.None;
                lg.transform.position = new Vector3((StageMinX + StageMaxX) / 2 + (i == 0 ? -9 : 9), Y + StageH + 7f, -(StageMaxZ - 3)); stageLamps[i] = l;
            }
        }

        // ---------------------------------------------------------- the stadium
        void BuildStadium()
        {
            var A = WorldData.Arena; var concrete = Lit("arenaWall", "#d6cec2", 0.1f); var band = Lit("arenaBand", "#6d32c8", 0.4f); var cap = Lit("arenaCap", "#b8afa2", 0.1f);
            var seatA = Lit("seatA", "#4a74c8", 0.3f); var seatB = Lit("seatB", "#e05050", 0.3f); var turf = Lit("arenaTurf", "#99ba68", 0.05f);   // brighter: the old colours rendered almost black inside the walls
            BoxC(A.maxX - A.minX, 0.06f, A.maxZ - A.minZ, WorldData.ArenaC.x, Y - 0.02f, WorldData.ArenaC.y, turf, false);
            System.Action<float, float, float, float> wall = (x0, x1, z0, z1) => { Box(x0, x1, z0, z1, Y, WallH, concrete); Box(x0 - 0.03f, x1 + 0.03f, z0 - 0.03f, z1 + 0.03f, Y + WallH - 1.4f, 0.6f, band, false); Box(x0 - 0.1f, x1 + 0.1f, z0 - 0.1f, z1 + 0.1f, Y + WallH, 0.25f, cap, false); };
            wall(A.minX, A.maxX, A.minZ, A.minZ + WallT); wall(A.minX, A.minX + WallT, A.minZ, A.maxZ);
            wall(A.maxX - WallT, A.maxX, A.minZ, GateEMinZ); wall(A.maxX - WallT, A.maxX, GateEMaxZ, A.maxZ);
            wall(A.minX, GateSMin, A.maxZ - WallT, A.maxZ); wall(GateSMax, A.maxX, A.maxZ - WallT, A.maxZ);
            System.Action<float, float, float, float, char> stand = (x0, x1, z0, z1, side) =>
            {
                for (int i = 0; i < Tiers; i++)
                {
                    // each tier is its own strip (not nested boxes): nested boxes shared their end faces, which z-fought (red/blue flicker) at the gates
                    float inset = i * TierD, top = (i + 1) * TierH; float a = x0, b = x1, c = z0, d = z1;
                    if (side == 'w') { b = x1 - inset; a = b - TierD; } else if (side == 'e') { a = x0 + inset; b = a + TierD; } else { c = z0 + inset; d = c + TierD; }
                    Box(a, b, c, d, Y, top, i % 2 == 1 ? seatB : seatA);
                }
            };
            float D = Tiers * TierD, inW = A.minX + WallT, inE = A.maxX - WallT, inS = A.maxZ - WallT, standN = StageMaxZ + 2;
            stand(inW, inW + D, standN, inS, 'w'); stand(inE - D, inE, standN, GateEMinZ, 'e'); stand(inE - D, inE, GateEMaxZ, inS, 'e');
            stand(inW + D, GateSMin, inS - D, inS, 's'); stand(GateSMax, inE - D, inS - D, inS, 's');
            // gate arch over the main entrance with the sign
            foreach (float x in new[] { GateSMin - 0.6f, GateSMax + 0.6f }) BoxC(1.2f, WallH + 3, WallT + 0.4f, x, Y, A.maxZ - WallT / 2, concrete);
            Box(GateSMin - 1.2f, GateSMax + 1.2f, A.maxZ - WallT / 2 - (WallT + 0.4f) / 2, A.maxZ - WallT / 2 + (WallT + 0.4f) / 2, Y + WallH + 2.2f, 0.8f, concrete, false);
            signMat = Lit("arenaSignBack", "#3a1366", 0.3f); signMat.EnableKeyword("_EMISSION"); signMat.SetColor("_EmissionColor", M.Hex("#5a1a6e") * 0.6f);
            foreach (int side in new[] { 1, -1 })
            {
                float zJs = side > 0 ? A.maxZ + 0.25f : A.maxZ - WallT - 0.25f; var sp = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(sp.GetComponent<Collider>()); sp.transform.SetParent(root.transform, false);
                sp.transform.position = new Vector3(WorldData.ArenaC.x, Y + WallH + 1.0f, -zJs); sp.transform.localScale = new Vector3(13, 3.25f, 0.1f); sp.GetComponent<MeshRenderer>().sharedMaterial = signMat;
                var tm = new GameObject("signText"); tm.transform.SetParent(sp.transform.parent, false); tm.transform.position = sp.transform.position + new Vector3(0, 0, side > 0 ? 0.08f : -0.08f); tm.transform.rotation = Quaternion.Euler(0, side > 0 ? 180 : 0, 0);
                var t1 = tm.AddComponent<TextMesh>(); t1.text = "HEARTHVALE ARENA\n<size=40>LIVE MUSIC · 4 PM – 8 PM</size>"; t1.richText = true; t1.fontSize = 90; t1.characterSize = 0.1f; t1.anchor = TextAnchor.MiddleCenter; t1.alignment = TextAlignment.Center; t1.color = new Color(1, 0.92f, 1); t1.fontStyle = FontStyle.Bold;
                tm.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = t1.font.material.mainTexture };   // depth-tested text (the default font shader draws through walls)
            }
        }

        Texture2D screenTex; Color32[] screenPx; const int SW = 128, SH = 56; float screenT; Renderer[] lampLens; Transform[] lampFx; Material[] beamMats; GameObject[] beams; Light[] unused;
        void BuildStage()
        {
            float cx = (StageMinX + StageMaxX) / 2; var deck = Lit("stageDeck", "#1b1b20", 0.45f); var skirt = Lit("stageSkirt", "#0c0c0f", 0.2f); var metal = Lit("stageMetal", "#8d939b", 0.35f, 0.8f); var spk = Lit("speaker", "#141418", 0.3f);
            Box(StageMinX, StageMaxX, StageMinZ, StageMaxZ, Y, StageH, deck);
            foreach (var xs in new[] { new Vector2(StageMaxX - 3, StageMaxX), new Vector2(StageMinX, StageMinX + 3) }) for (int k = 0; k < 3; k++) Box(xs.x, xs.y, StageMaxZ + k, StageMaxZ + k + 1, Y, StageH - (k + 1) * 0.4f, deck);
            float TOP = Y + 11, tz0 = StageMinZ + 1.2f, tz1 = StageMaxZ - 0.4f;
            foreach (float x in new[] { StageMinX + 0.4f, StageMaxX - 0.4f }) foreach (float z in new[] { tz0, tz1 }) BoxC(0.45f, TOP - Y - StageH, 0.45f, x, Y + StageH, z, metal, false);
            BoxC(StageMaxX - StageMinX, 0.5f, 0.5f, cx, TOP, tz1, metal, false); BoxC(StageMaxX - StageMinX, 0.5f, 0.5f, cx, TOP, tz0, metal, false);
            foreach (float x in new[] { StageMinX + 0.4f, StageMaxX - 0.4f }) BoxC(0.5f, 0.5f, tz1 - tz0, x, TOP, (tz0 + tz1) / 2, metal, false);
            BoxC(22.6f, 10.2f, 0.4f, cx, Y + StageH + 0.6f, StageMinZ + 0.45f, skirt, false);
            foreach (float x in new[] { StageMinX - 1.6f, StageMaxX + 1.6f }) BoxC(2.2f, 4.4f, 2, x, Y, StageMaxZ - 1, spk);
            foreach (float x in new[] { cx - 10, cx + 10 }) BoxC(1.2f, 1.0f, 0.7f, x, Y + StageH, StageMinZ + 3.4f, spk, false);
            // big LED screen behind the band (a small texture redrawn a few times a second)
            screenTex = new Texture2D(SW, SH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp }; screenPx = new Color32[SW * SH];
            var scr = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(scr.GetComponent<Collider>()); scr.transform.SetParent(root.transform, false); scr.transform.position = new Vector3(cx, Y + StageH + 0.9f + 4.8f, -(StageMinZ + 0.75f)); scr.transform.localScale = new Vector3(22, 9.6f, 1);
            scr.transform.rotation = Quaternion.Euler(0, 180, 0);   // faces the crowd (toward -z)
            var sm = CraftModel.Unlit(Color.white); sm.SetTexture("_BaseMap", screenTex); scr.GetComponent<MeshRenderer>().sharedMaterial = sm;
            // stage lights on the front truss: a fixture and a sweeping translucent beam each
            int N = 8; lampLens = new Renderer[N]; lampFx = new Transform[N]; beamMats = new Material[N]; beams = new GameObject[N];
            var cone = new Mesh(); { var v = new List<Vector3> { Vector3.zero }; var tr = new List<int>(); for (int i = 0; i < 18; i++) { float a = i / 18f * Mathf.PI * 2; v.Add(new Vector3(Mathf.Cos(a) * 2.6f, -15f, Mathf.Sin(a) * 2.6f)); } for (int i = 0; i < 18; i++) { tr.AddRange(new[] { 0, 1 + (i + 1) % 18, 1 + i }); } cone.SetVertices(v); cone.SetTriangles(tr, 0); cone.RecalculateNormals(); }
            var cylM = GameObject.CreatePrimitive(PrimitiveType.Cylinder); var cylMesh = cylM.GetComponent<MeshFilter>().sharedMesh; Destroy(cylM);
            for (int i = 0; i < N; i++)
            {
                float x = StageMinX + 3 + i / (float)(N - 1) * (StageMaxX - StageMinX - 6);
                var fx = new GameObject("StageLight" + i); fx.transform.SetParent(root.transform, false); fx.transform.position = new Vector3(x, TOP - 0.25f, -(tz1 + 0.1f)); lampFx[i] = fx.transform;
                var can = new GameObject("can"); can.transform.SetParent(fx.transform, false); can.transform.localScale = new Vector3(0.5f, 0.25f, 0.5f); can.AddComponent<MeshFilter>().sharedMesh = cylMesh; can.AddComponent<MeshRenderer>().sharedMaterial = Lit("lampCan", "#111111", 0.3f);
                var lens = new GameObject("lens"); lens.transform.SetParent(fx.transform, false); lens.transform.localPosition = new Vector3(0, -0.28f, 0); lens.transform.localScale = new Vector3(0.45f, 0.03f, 0.45f); lens.AddComponent<MeshFilter>().sharedMesh = cylMesh;
                var lr = lens.AddComponent<MeshRenderer>(); lr.sharedMaterial = CraftModel.Unlit(Color.black); lampLens[i] = lr;
                var bm = new Material(Shader.Find("Sprites/Default")) { name = "beam" }; bm.color = new Color(1, 1, 1, 0.1f); beamMats[i] = bm;
                var bg = new GameObject("beam"); bg.transform.SetParent(fx.transform, false); bg.AddComponent<MeshFilter>().sharedMesh = cone; var br = bg.AddComponent<MeshRenderer>(); br.sharedMaterial = bm; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; beams[i] = bg;
            }
        }

        // ---------------------------------------------------------- the band: realistic sculpted people
        sealed class Member { public Avatar av; public string kind; public Vector3 baseU; public float baseYaw; public Transform mallet0, mallet1; }
        readonly List<Member> band = new List<Member>(); Member drummer;
        static Mesh capsM, sphM, cubM, cylM2;
        static void Prims()
        {
            if (sphM != null) return;
            var a = GameObject.CreatePrimitive(PrimitiveType.Capsule); capsM = a.GetComponent<MeshFilter>().sharedMesh; Destroy(a);
            var b = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphM = b.GetComponent<MeshFilter>().sharedMesh; Destroy(b);
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube); cubM = c.GetComponent<MeshFilter>().sharedMesh; Destroy(c);
            var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cylM2 = d.GetComponent<MeshFilter>().sharedMesh; Destroy(d);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { capsM = sphM = cubM = cylM2 = null; }
        static GameObject Prop(Mesh m, Material mat, Transform parent, Vector3 worldPos, Quaternion worldRot, Vector3 scale)
        {
            var g = new GameObject("prop"); g.transform.SetParent(parent, true); g.transform.position = worldPos; g.transform.rotation = worldRot; g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = m; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; return g;
        }
        static Vector3 AvP(Avatar av, float x, float y, float z) { return av.root.transform.TransformPoint(new Vector3(x, y, z)); }

        Member MakeMember(string kind, string shirt, int seed, float xJs, float zJs, float yawJs)
        {
            Prims(); var av = Avatar.Build(shirt, seed); var m = new Member { av = av, kind = kind };
            m.baseU = new Vector3(xJs, Y + StageH, -zJs); m.baseYaw = 180f - yawJs * Mathf.Rad2Deg; av.root.transform.SetParent(bandRoot, false); av.root.transform.position = m.baseU; av.root.transform.rotation = Quaternion.Euler(0, m.baseYaw, 0);
            Transform head = av.bones[Avatar.BHead], spine = av.bones[Avatar.BChest];
            var black = Lit("bandBlack", "#0d0d10", 0.55f); var chrome = Lit("bandChrome", "#e4e7ec", 0.85f, 1f);
            if (kind == "drummer")
            {   // modern look: black tee, snapback cap, shades, a glossy red marching drum strapped across the stomach
                av.rShirt.sharedMaterial = av.SkinMaterial("#151518", 0.1f, 0.05f, "cloth_n", 38f, 0.9f, 0.3f, 0.05f); av.rJacket.enabled = false; av.rPants.sharedMaterial = av.SkinMaterial("#26262b", 0.1f, 0.05f, "cloth_n", 30f, 1.0f, 0.3f, 0.05f);
                av.rHair.enabled = false;
                var dome = Prop(sphM, black, head, AvP(av, 0, 1.745f, -0.005f), av.root.transform.rotation, new Vector3(0.205f, 0.14f, 0.215f)); // cap
                Prop(cubM, black, head, AvP(av, 0, 1.775f, -0.115f), av.root.transform.rotation, new Vector3(0.17f, 0.012f, 0.11f));   // brim, worn backwards
                Prop(cubM, Lit("shades", "#050507", 0.95f, 0.6f), head, AvP(av, 0, 1.704f, 0.097f), av.root.transform.rotation, new Vector3(0.165f, 0.036f, 0.022f));
                var red = Lit("drumRed", "#c4142a", 0.8f, 0.35f); var skinHead = Lit("drumSkin", "#f4f1ea", 0.4f);
                Vector3 dc = AvP(av, 0, 1.0f, 0.3f); var rot = av.root.transform.rotation * Quaternion.Euler(0, 0, 90);
                Prop(cylM2, red, spine, dc, rot, new Vector3(0.4f, 0.25f, 0.4f));
                foreach (int s in new[] { -1, 1 })
                {
                    Prop(cylM2, skinHead, spine, AvP(av, s * 0.245f, 1.0f, 0.3f), rot, new Vector3(0.39f, 0.01f, 0.39f));
                    Prop(cylM2, chrome, spine, AvP(av, s * 0.255f, 1.0f, 0.3f), rot, new Vector3(0.42f, 0.008f, 0.42f));
                }
                for (int k = 0; k < 10; k++) { float a = k / 10f * Mathf.PI * 2; Prop(cylM2, chrome, spine, AvP(av, 0, 1.0f + Mathf.Sin(a) * 0.205f, 0.3f + Mathf.Cos(a) * 0.205f), rot, new Vector3(0.012f, 0.25f, 0.012f)); }
                foreach (int s in new[] { -1, 1 }) Prop(cubM, black, spine, AvP(av, s * 0.12f, 1.28f, 0.1f), av.root.transform.rotation * Quaternion.Euler(10, 0, 0), new Vector3(0.06f, 0.5f, 0.02f)); // harness straps
                for (int s = 0; s < 2; s++)
                {   // mallets in both hands
                    Transform wrist = av.bones[Avatar.BoneIndex(s == 0 ? 1 : -1, 2)]; Vector3 w = wrist.position;
                    var stick = Prop(cylM2, Lit("stick", "#d8c49a", 0.3f), wrist, w + Vector3.down * 0.17f, Quaternion.identity, new Vector3(0.018f, 0.16f, 0.018f)); Prop(sphM, Lit("mallet", "#f2f2f2", 0.4f), wrist, w + Vector3.down * 0.34f, Quaternion.identity, Vector3.one * 0.07f);
                    if (s == 0) m.mallet0 = stick.transform; else m.mallet1 = stick.transform;
                }
            }
            else if (kind == "guitar" || kind == "bass")
            {
                bool bass = kind == "bass"; var body = Lit(bass ? "bassBody" : "gtrBody", bass ? "#1f4fbf" : "#b3121f", 0.85f, 0.2f); var wood = Lit("gtrNeck", "#3a2414", 0.4f);
                av.rShirt.sharedMaterial = av.SkinMaterial(shirt, 0.1f, 0.05f, "cloth_n", 38f, 0.9f, 0.3f, 0.05f);
                Quaternion tilt = av.root.transform.rotation * Quaternion.Euler(0, 0, 28);
                Vector3 bc = AvP(av, 0.08f, 1.0f, 0.2f); Prop(sphM, body, spine, bc, tilt, new Vector3(0.36f, 0.42f, 0.07f)); Prop(sphM, body, spine, bc + tilt * new Vector3(0, 0.2f, 0), tilt, new Vector3(0.26f, 0.3f, 0.065f));
                float nl = bass ? 0.95f : 0.75f; Quaternion nr = av.root.transform.rotation * Quaternion.Euler(0, -8, 62);   // neck points up and out to the player's left
                Vector3 nb = bc + av.root.transform.rotation * new Vector3(-0.17f, 0.12f, 0.02f); Prop(cubM, wood, spine, nb + nr * new Vector3(0, nl / 2, 0), nr, new Vector3(0.05f, nl, 0.025f)); Prop(cubM, black, spine, nb + nr * new Vector3(0, nl + 0.07f, 0), nr, new Vector3(0.07f, 0.14f, 0.03f));
                Prop(cubM, black, spine, AvP(av, 0.09f, 1.3f, 0.02f), av.root.transform.rotation * Quaternion.Euler(0, 0, -55), new Vector3(0.05f, 0.62f, 0.015f));   // strap
            }
            else if (kind == "singer")
            {
                av.rShirt.sharedMaterial = av.SkinMaterial("#d43c7a", 0.1f, 0.05f, "cloth_n", 38f, 0.9f, 0.3f, 0.05f); av.rJacket.sharedMaterial = av.SkinMaterial("#f2f2f2", 0.15f, 0.05f, "cloth_n", 30f, 1.0f, 0.3f, 0.06f); av.rShirt.enabled = false;
                Transform wrist = av.bones[Avatar.BoneIndex(1, 2)]; Prop(cylM2, black, wrist, wrist.position + Vector3.down * 0.08f, Quaternion.identity, new Vector3(0.028f, 0.08f, 0.028f)); Prop(sphM, Lit("micBall", "#9aa0a6", 0.6f, 0.8f), wrist, wrist.position + Vector3.down * 0.17f, Quaternion.identity, Vector3.one * 0.07f);
                var stand = Prop(cylM2, black, bandRoot, new Vector3(xJs - 0.5f, Y + StageH + 0.75f, -(zJs + 0.45f)), Quaternion.identity, new Vector3(0.03f, 0.75f, 0.03f));
            }
            else
            {   // keys player behind a keyboard
                av.rShirt.sharedMaterial = av.SkinMaterial("#e3b23c", 0.1f, 0.05f, "cloth_n", 38f, 0.9f, 0.3f, 0.05f);
                Vector3 kp = AvP(av, 0, 0.95f, 0.55f); Prop(cubM, black, bandRoot, kp, av.root.transform.rotation, new Vector3(1.2f, 0.08f, 0.36f)); Prop(cubM, Lit("ivory", "#f4f4f4", 0.5f), bandRoot, kp + Vector3.up * 0.045f + av.root.transform.rotation * new Vector3(0, 0, -0.07f), av.root.transform.rotation, new Vector3(1.1f, 0.02f, 0.16f));
                foreach (int s in new[] { -1, 1 }) Prop(cubM, black, bandRoot, kp + av.root.transform.rotation * new Vector3(s * 0.5f, -0.47f, 0), av.root.transform.rotation * Quaternion.Euler(0, 0, s * -17), new Vector3(0.05f, 0.95f, 0.05f));
            }
            band.Add(m); return m;
        }

        void BuildBand()
        {
            bandRoot = new GameObject("Band").transform; bandRoot.SetParent(root.transform, false); float cx = (StageMinX + StageMaxX) / 2;
            drummer = MakeMember("drummer", "#151518", 12, cx, StageMaxZ - 2.6f, 0);
            MakeMember("singer", "#d43c7a", 21, cx - 5, StageMaxZ - 3.2f, -0.15f);
            MakeMember("guitar", "#2b2b2b", 33, cx - 11, StageMaxZ - 4.6f, -0.3f);
            MakeMember("bass", "#3a6ea8", 44, cx + 10, StageMaxZ - 4.6f, 0.3f);
            MakeMember("keys", "#e3b23c", 55, cx + 4.5f, StageMinZ + 4.4f, 0.1f);
            bandRoot.gameObject.SetActive(false);
        }

        // set arm bones directly: negative x lifts the arm forward
        static void Arm(Avatar av, int side, float shoulderX, float shoulderZ, float elbowX, float shoulderY = 0) { av.bones[Avatar.BoneIndex(side, 0)].localRotation = Quaternion.Euler(shoulderX, shoulderY, shoulderZ * side); av.bones[Avatar.BoneIndex(side, 1)].localRotation = Quaternion.Euler(elbowX, 0, 0); }

        void AnimateBand(float beat, float dt)
        {
            foreach (var m in band)
            {
                var av = m.av; float k = Mathf.Sin(beat * Mathf.PI); av.Pose("idle", dt, 0f, 0f, false);
                av.bones[Avatar.BHead].localRotation = Quaternion.Euler(Mathf.Max(0, k) * 12f - 3f, 0, 0);
                if (m.kind == "drummer")
                {   // both mallets beat the drum skins, alternating; he struts a little left and right
                    foreach (int s in new[] { 1, -1 })
                    {
                        float hit = Mathf.Max(0, Mathf.Sin(beat * Mathf.PI + (s > 0 ? Mathf.PI / 2 : 0)));
                        Arm(av, s, -48 - hit * 24f, 14f, -88f - hit * 30f);
                        av.bones[Avatar.BoneIndex(s, 2)].localRotation = Quaternion.Euler(-hit * 25f, 0, 0);
                    }
                    var p = m.baseU; p.x += Mathf.Sin(t * 0.6f) * 1.6f; av.root.transform.position = p; av.root.transform.rotation = Quaternion.Euler(0, m.baseYaw + Mathf.Sin(t * 0.6f + 1.2f) * 14f, 0);
                    foreach (int s in new[] { 1, -1 }) { av.bones[Avatar.BoneIndex(s, 3)].localRotation = Quaternion.Euler(Mathf.Sin(beat * Mathf.PI * 0.5f + s) * 7f, 0, 0); }
                }
                else if (m.kind == "guitar" || m.kind == "bass")
                {
                    float strum = Mathf.Sin(beat * Mathf.PI * 2);
                    Arm(av, -1, -62f, 24f, -55f + Mathf.Sin(t * 1.3f) * 5f, 38f);                // fretting hand up the neck
                    Arm(av, 1, -38f, 8f, -80f - strum * 14f);                                       // strumming hand
                    av.bones[Avatar.BSpine].localRotation = Quaternion.Euler(6f + Mathf.Max(0, k) * 7f, 0, 0);
                }
                else if (m.kind == "singer")
                {
                    Arm(av, 1, -118f, 12f, -125f);                                                  // mic to the mouth
                    Arm(av, -1, -138f + Mathf.Sin(beat * Mathf.PI * 0.5f) * 25f, 20f, -18f);        // pumps the crowd
                    av.root.transform.rotation = Quaternion.Euler(0, m.baseYaw + Mathf.Sin(t * 0.8f) * 17f, 0);
                }
                else foreach (int s in new[] { 1, -1 }) Arm(av, s, -55f, 4f, -42f - Mathf.Max(0, Mathf.Sin(beat * Mathf.PI * 2 + s * 1.3f)) * 12f);
            }
        }

        // ---------------------------------------------------------- crowd + lost fans
        sealed class Fan { public Avatar av; public Vector3 baseU; public float off; public int style; }
        readonly List<Fan> crowd = new List<Fan>();
        void BuildCrowd()
        {
            string[] cols = { "#ff9eb5", "#ffb38a", "#ffd37a", "#8fe3c1", "#8fd3ff", "#b9a5ff", "#f25f5c", "#ffffff", "#3a3a3a", "#6fd16f" };
            int n = world.quality == GfxQuality.LowEnd ? 4 : 9; var rs = new System.Random(5);
            for (int i = 0; i < n; i++)
            {
                var av = Avatar.Build(cols[i % cols.Length], 200 + i * 3); float x = Mathf.Lerp(StageMinX + 5, StageMaxX - 5, (float)rs.NextDouble()), z = Mathf.Lerp(StageMaxZ + 6, StageMaxZ + 18, (float)rs.NextDouble());
                av.root.transform.SetParent(root.transform, false); var f = new Fan { av = av, baseU = new Vector3(x, Y, -z), off = (float)rs.NextDouble() * 6, style = i % 3 };
                av.root.transform.position = f.baseU; av.root.transform.rotation = Quaternion.Euler(0, (float)(rs.NextDouble() - 0.5) * 30f, 0); av.rJacket.enabled = i % 2 == 0; av.SetGun(null); av.root.SetActive(false); crowd.Add(f);   // they face the stage (+z)
            }
        }
        void AnimateCrowd(float beat)
        {
            foreach (var c in crowd)
            {
                var av = c.av; float ph = beat * Mathf.PI + c.off; av.Pose("idle", 0f, 0f, 0f, false);
                if (c.style == 0) { av.root.transform.position = c.baseU + Vector3.up * Mathf.Max(0, Mathf.Sin(ph)) * 0.28f; foreach (int s in new[] { 1, -1 }) Arm(av, s, -148f + Mathf.Sin(ph) * 10f, 18f, -8f); }
                else if (c.style == 1) { av.root.transform.position = c.baseU; foreach (int s in new[] { 1, -1 }) Arm(av, s, -132f, 20f + Mathf.Sin(ph * 0.5f) * 20f, -15f); }
                else { av.root.transform.position = c.baseU + Vector3.up * Mathf.Abs(Mathf.Sin(ph)) * 0.05f; av.bones[Avatar.BHead].localRotation = Quaternion.Euler(Mathf.Max(0, Mathf.Sin(ph)) * 15f, 0, 0); Arm(av, 1, -125f - Mathf.Max(0, Mathf.Sin(ph)) * 30f, 12f, -35f); Arm(av, -1, -6f, 4f, -14f); }
            }
        }

        sealed class Lost { public Avatar av; public Vector3 home, pos; public float gone; public bool stung; public Material skin; }
        readonly List<Lost> lost = new List<Lost>(); bool hauntNoticed;
        void BuildLostFans()
        {
            var A = WorldData.Arena; var homes = new[] { new Vector2(A.minX + 3.2f, -128), new Vector2(A.minX + 2.4f, -110), new Vector2(A.maxX - 3.2f, -124), new Vector2(A.maxX - 2.4f, -102), new Vector2(WorldData.ArenaC.x - 14, A.maxZ - 3), new Vector2(WorldData.ArenaC.x, -138) };
            foreach (var h in homes)
            {   // pale, washed-out, faintly glowing people with black eyes
                var av = Avatar.Build("#cfd8e0", 400 + lost.Count); var skin = av.SkinMaterial("#cfd8e0", 0.3f, 0.2f, "skin_n", 26f, 0.8f, 0.4f, 0.6f, "#b0c8e0"); skin.SetColor("_EmissionColor", M.Hex("#6f98b8") * 0.9f);
                av.rSkin.sharedMaterial = skin; av.rHead.sharedMaterial = skin; var cloth = av.SkinMaterial("#aab4bc", 0.1f, 0.05f, "cloth_n", 30f, 1.0f, 0.3f, 0.05f); cloth.SetColor("_EmissionColor", M.Hex("#5f8aa8") * 0.7f);
                av.rShirt.sharedMaterial = cloth; av.rJacket.sharedMaterial = cloth; av.rPants.sharedMaterial = cloth; av.rShoes.sharedMaterial = cloth; av.rHair.enabled = false; av.SetGun(null);
                var dark = av.SkinMaterial("#020202", 0.9f, 0f, "skin_n", 8f, 0f, 0.3f, 0.6f, "#000000"); foreach (var r in av.eyeRenderers) r.sharedMaterial = dark;
                Vector3 home = new Vector3(h.x, WorldData.HeightAtU(h.x, -h.y), -h.y);
                RaycastHit hit; if (Physics.Raycast(new Vector3(h.x, Y + 6, -h.y), Vector3.down, out hit, 20f, ~((1 << PlayerController.PlayerLayer) | (1 << CritterSystem.CritterLayer)), QueryTriggerInteraction.Ignore)) home.y = hit.point.y;
                av.root.transform.SetParent(root.transform, false); av.root.transform.position = home; av.root.SetActive(false);
                av.bones[Avatar.BHead].localRotation = Quaternion.Euler(0, 0, Random.Range(-22f, 22f));
                lost.Add(new Lost { av = av, home = home, pos = home, skin = skin });
            }
        }

        // ---------------------------------------------------------- per frame
        string ModeAt(float hour) { if (hour >= ShowStart && hour < ShowEnd) return "show"; if (hour >= ShowEnd || hour < HauntEnd) return "haunt"; return "idle"; }
        float beatT, heartT;

        void Update()
        {
            if (world == null || player == null || bandRoot == null) return; float dt = Mathf.Min(Time.deltaTime, 0.05f); t += dt; var cam = Camera.main; if (cam == null) return;
            if (!toldStart && t > 4f) { toldStart = true; player.Toast("🎸 Live concert at the Arena (north-west of town) every day " + ShowStart + ":00–" + ShowEnd + ":00"); }
            string md = ModeAt(world.Hour);
            if (md != mode)
            {
                mode = md; if (md != "show") StopDrum();
                if (md == "haunt") { hauntNoticed = false; foreach (var g in lost) { g.pos = g.home; g.gone = 0; } }
                if (md == "show") player.Toast("🎸 The concert at the Arena has started! Follow the drums north-west of town (until 8 PM)");
                if (md == "haunt") player.Toast("🌑 The concert is over and the Arena goes dark… something stayed behind.");
            }
            Vector3 cp = cam.transform.position; var A = WorldData.Arena; float cx = cp.x, czJs = -cp.z;
            float dx = Mathf.Max(Mathf.Max(A.minX - cx, 0), cx - A.maxX), dz = Mathf.Max(Mathf.Max(A.minZ - czJs, 0), czJs - A.maxZ), distOut = Mathf.Sqrt(dx * dx + dz * dz); bool near = distOut < 170f;
            Vector3 pp = player.transform.position; playerInside = !player.dead && WorldData.ArenaDist(pp.x, -pp.z) < -0.5f;
            bool show = mode == "show", haunt = mode == "haunt";
            bandRoot.gameObject.SetActive(show && near); foreach (var c in crowd) c.av.root.SetActive(show && near);
            cheer += ((show && distOut < 90 ? 1 : 0) - cheer) * Mathf.Min(1, dt * 0.8f); world.horrorSuppress = cheer;
            if (ghosts != null) { ghosts.suppress = cheer; ghosts.blocked = playerInside; }
            signMat.SetColor("_EmissionColor", M.Hex("#7a2aa0") * (show ? 1.2f + Mathf.Sin(t * 6) * 0.3f : haunt ? (Random.value < 0.04f ? 0.6f : 0.03f) : 0.35f));
            float beat = t * 4.2f;
            if (show && near) { AnimateBand(beat, dt); AnimateCrowd(beat); }
            UpdateLights(show, haunt, near, beat);
            for (int i = 0; i < 2; i++) { stageLamps[i].intensity = show && near ? 16f : 0; stageLamps[i].color = Color.HSVToRGB((t * 0.08f + i * 0.5f) % 1f, 0.8f, 1f).gamma; }
            screenT -= dt; if (near && screenT <= 0) { screenT = 0.1f; DrawScreen(show, haunt); }
            // ---- the drum: 3D sound, short range. Full volume inside the Arena, fading out to nothing ~80 m from the stage.
            if (show && distOut < HearRange + 10f && drumClip != null)
            {
                if (!drum.isPlaying) drum.Play();
                drum.transform.position = drummer.av.root.transform.position + Vector3.up * 1.2f;
                bool inside = distOut <= 0.01f;
                drum.volume = inside ? 1f : 0.9f;                                   // the distance curve does the fading
                lowpass.cutoffFrequency = Mathf.Lerp(lowpass.cutoffFrequency, inside ? 22000f : 1400f, 1 - Mathf.Exp(-dt * 6));   // muffled through the walls
            }
            else if (drum.isPlaying) StopDrum();
            UpdateLost(dt, haunt, near);
        }
        void StopDrum() { if (drum != null && drum.isPlaying) drum.Stop(); }

        void UpdateLights(bool show, bool haunt, bool near, float beat)
        {
            for (int i = 0; i < lampFx.Length; i++)
            {
                bool on = show && near; beams[i].SetActive(on); var lens = lampLens[i].sharedMaterial;
                if (on)
                {
                    float hue = (t * 0.08f + i * 0.125f) % 1f; Color c = Color.HSVToRGB(hue, 1f, 1f);
                    beamMats[i].color = new Color(c.r, c.g, c.b, 0.04f + Mathf.Max(0, Mathf.Sin(beat * Mathf.PI + i)) * 0.04f); lens.SetColor("_BaseColor", c * 3f);
                    lampFx[i].localRotation = Quaternion.Euler(-(0.55f + Mathf.Sin(t * 0.9f + i * 0.8f) * 0.35f) * Mathf.Rad2Deg + 0f, 0, Mathf.Sin(t * 0.7f + i * 1.7f) * 0.5f * Mathf.Rad2Deg) * Quaternion.Euler(0, 180, 0);
                }
                else if (haunt) { bool flick = i == 3 && Random.value < 0.5f; lens.SetColor("_BaseColor", flick ? M.Hex("#b8d4ff") * 2f : Color.black); }
                else lens.SetColor("_BaseColor", M.Hex("#222222"));
            }
        }

        void DrawScreen(bool show, bool haunt)
        {
            int W = SW, H = SH;
            if (show)
            {
                float hue = (t * 20f) % 360f / 360f; Color c0 = Color.HSVToRGB(hue, 0.8f, 0.35f), c1 = Color.HSVToRGB((hue + 0.22f) % 1f, 0.8f, 0.2f);
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) screenPx[y * W + x] = Color.Lerp(c0, c1, (x / (float)W + y / (float)H) * 0.5f);
                int n = 20; float beat = Mathf.Max(0, Mathf.Sin(t * 4.2f * Mathf.PI)), bw = (W - 8f) / n;
                for (int i = 0; i < n; i++)
                {
                    float h = (0.25f + 0.6f * Mathf.Abs(Mathf.Sin(t * (2 + i * 0.37f) + i)) * (0.6f + beat * 0.4f)) * H * 0.7f; Color bc = Color.HSVToRGB(((hue * 360f + i * 9f) % 360f) / 360f, 1f, 1f);
                    for (int y = 4; y < 4 + (int)h; y++) for (int x = 4 + (int)(i * bw); x < 4 + (int)((i + 1) * bw) - 1; x++) screenPx[y * W + x] = bc;
                }
            }
            else if (haunt) { for (int i = 0; i < screenPx.Length; i++) { byte v = (byte)(Random.value * 40); screenPx[i] = new Color32(v, v, (byte)(v * 1.1f), 255); } if (Random.value < 0.08f) for (int y = H / 2 - 4; y < H / 2 + 4; y++) for (int x = W / 2 - 24; x < W / 2 + 24; x++) screenPx[y * W + x] = new Color32(179, 18, 31, 255); }
            else for (int i = 0; i < screenPx.Length; i++) screenPx[i] = new Color32(7, 7, 10, 255);
            screenTex.SetPixels32(screenPx); screenTex.Apply(false);
        }

        void UpdateLost(float dt, bool haunt, bool near)
        {
            nearness = 0; bool active = haunt && near; foreach (var g in lost) g.av.root.SetActive(active && g.gone <= 0); if (!active) return;
            bool inside = playerInside && !player.dead && player.vehicle == null && !player.inCave && !player.frozen;
            if (inside && !hauntNoticed) { hauntNoticed = true; Sfx.Stinger(0.7f); player.Toast("👻 The Arena is not empty…"); }
            if (!inside) hauntNoticed = hauntNoticed && WorldData.ArenaDist(player.transform.position.x, -player.transform.position.z) < 25;
            Vector3 cam = Camera.main.transform.position, pp = player.transform.position;
            foreach (var g in lost)
            {
                var r = g.av.root;
                if (g.gone > 0) { g.gone -= dt; if (g.gone <= 0) g.pos = g.home; continue; }
                g.skin.SetColor("_EmissionColor", M.Hex("#6f98b8") * (0.6f + Random.value * 0.7f)); r.SetActive(Random.value > 0.04f);
                r.transform.rotation = Quaternion.LookRotation(new Vector3(cam.x - g.pos.x, 0, cam.z - g.pos.z) + Vector3.forward * 0.0001f);
                float dx = pp.x - g.pos.x, dz = pp.z - g.pos.z, dist = Mathf.Max(0.01f, Mathf.Sqrt(dx * dx + dz * dz)); var av = g.av;
                av.Pose("idle", dt, 0f, 0f, false);
                if (inside)
                {
                    nearness = Mathf.Max(nearness, Mathf.Max(0, 1 - dist / 22f));
                    bool seen = ghosts != null && ghosts.Seen(g.pos, 1.8f, 0.8f);
                    if (!seen)
                    {   // glide toward you while you are not looking (they float over the seats)
                        float sp = 2.2f * dt; g.pos.x += dx / dist * sp; g.pos.z += dz / dist * sp; g.pos.y += (GroundY(g.pos) - g.pos.y) * Mathf.Min(1, dt * 6); g.stung = false;
                        Arm(av, 1, -78f, 8f, -12f); Arm(av, -1, -78f, 8f, -12f);   // arms reaching out
                    }
                    else { if (!g.stung && dist < 14) { g.stung = true; Sfx.Stinger(0.45f); } if (Random.value < 0.04f) av.bones[Avatar.BHead].localRotation = Quaternion.Euler(0, 0, Random.Range(-34f, 34f)); }
                    if (dist < 1.3f) { g.gone = Random.Range(10f, 16f); r.SetActive(false); if (ghosts != null) ghosts.Jumpscare(3, 20f); continue; }
                    if (Random.value < dt * 0.15f) Sfx.Whisper(0.2f + nearness * 0.3f);
                }
                else
                {   // you left: they drift back to where they stood and wait
                    float hx = g.home.x - g.pos.x, hz = g.home.z - g.pos.z, hd = Mathf.Sqrt(hx * hx + hz * hz);
                    if (hd > 0.1f) { float sp = Mathf.Min(hd, 1.5f * dt); g.pos.x += hx / hd * sp; g.pos.z += hz / hd * sp; g.pos.y = GroundY(g.pos); }
                }
                r.transform.position = new Vector3(g.pos.x, g.pos.y + Mathf.Sin(t * 1.5f + g.home.x) * 0.06f, g.pos.z);
            }
            if (nearness > 0.15f) { heartT -= dt; if (heartT <= 0) { heartT = 1.1f - nearness * 0.7f; Sfx.Heartbeat(0.3f + nearness * 0.5f); } }
            if (ghosts != null) ghosts.nearness = Mathf.Max(ghosts.nearness, nearness);
        }
        float GroundY(Vector3 p) { RaycastHit h; return Physics.Raycast(new Vector3(p.x, p.y + 1.5f, p.z), Vector3.down, out h, 12f, ~((1 << PlayerController.PlayerLayer) | (1 << CritterSystem.CritterLayer) | (1 << Garage.VehicleLayer)), QueryTriggerInteraction.Ignore) ? h.point.y : p.y; }
    }
}
