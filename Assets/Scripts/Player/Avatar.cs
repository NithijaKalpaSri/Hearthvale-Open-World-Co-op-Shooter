using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>
    /// A realistic human (1.78 m): one seamless sculpted skin surface with a face (brow, nose, lips, ears, eye sockets,
    /// real eyeballs), hands with fingers, calf and thigh muscle, plus separate clothing shells (shirt, open jacket,
    /// jeans, shoes) and hair, all skinned to a 17-bone skeleton so every joint bends smoothly.
    /// Same character as before (pink shirt, darker jacket, blue jeans, dark shoes with white soles).
    /// </summary>
    public sealed class Avatar
    {
        static readonly string[] HairCols = { "#3b2a20", "#6b4a2f", "#1f1b1a", "#a87a48", "#c9a26b", "#5a3a2a" };
        static readonly string[] SkinCols = { "#f2cba8", "#e0b08a", "#c68e64", "#9b6a45", "#f6d6bd" };
        const int Pelvis = 0, Spine = 1, Chest = 2, Neck = 3, Head = 4; // then per side: shoulder, elbow, wrist, hip, knee, ankle
        static int B(int side, int part) { return 5 + (side > 0 ? 0 : 6) + part; } // part: 0 shoulder 1 elbow 2 wrist 3 hip 4 knee 5 ankle

        public GameObject root; public Transform[] bones; public Transform gunMount;
        public SkinnedMeshRenderer rSkin, rHead, rShirt, rJacket, rPants, rShoes, rHair; public readonly List<Renderer> eyeRenderers = new List<Renderer>();
        public static int BoneIndex(int side, int part) { return B(side, part); }
        public const int BPelvis = 0, BSpine = 1, BChest = 2, BNeck = 3, BHead = 4;
        public readonly List<Renderer> renderers = new List<Renderer>();
        public float phase, blend, idleT, air; GameObject gun; string gunKind;

        // ---- shared (cached) geometry
        static BoneDef[] defs; static Mesh mHead, mSkin, mShirt, mJacket, mPants, mShoes, mHair; static bool built;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { defs = null; mHead = mSkin = mShirt = mJacket = mPants = mShoes = mHair = null; built = false; caps.Clear(); sph = null; }
        static readonly Dictionary<string, Mesh> caps = new Dictionary<string, Mesh>();

        /// <summary>A plain capsule mesh (axis Y, total height len + 2r): used by simple props such as ghost limbs.</summary>
        public static Mesh CapsuleMesh(float r, float len)
        {
            string k = r + "_" + len; Mesh m; if (caps.TryGetValue(k, out m) && m != null) return m;
            var sc = new Sculpt().Cone(new Vector3(0, -len / 2, 0), new Vector3(0, len / 2, 0), r, r, 0f);
            var res = sc.Mesh(new Vector3(-r - 0.03f, -len / 2 - r - 0.03f, -r - 0.03f), new Vector3(r + 0.03f, len / 2 + r + 0.03f, r + 0.03f), Mathf.Max(0.008f, r / 5f));
            m = new Mesh { name = "cap" }; m.SetVertices(res.v); m.SetNormals(res.n); m.SetTriangles(res.t, 0); m.RecalculateBounds(); caps[k] = m; return m;
        }

        static BoneDef[] Defs()
        {
            var d = new List<BoneDef>();
            Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
            d.Add(new BoneDef("pelvis", -1, V(0, 0.96f, 0), V(0, 0.9f, 0), V(0, 1.0f, 0), 0.08f));
            d.Add(new BoneDef("spine", Pelvis, V(0, 1.08f, 0), V(0, 1.06f, 0), V(0, 1.2f, 0), 0.07f));
            d.Add(new BoneDef("chest", Spine, V(0, 1.26f, 0), V(0, 1.26f, 0), V(0, 1.42f, 0), 0.07f));
            d.Add(new BoneDef("neck", Chest, V(0, 1.50f, 0), V(0, 1.50f, 0), V(0, 1.57f, 0), 0.04f));
            d.Add(new BoneDef("head", Neck, V(0, 1.60f, 0), V(0, 1.62f, 0), V(0, 1.78f, 0), 0.05f));
            foreach (int s in new[] { 1, -1 })
            {
                Vector3 sh = V(s * 0.2f, 1.45f, 0), el = V(s * 0.265f, 1.18f, 0), wr = V(s * 0.31f, 0.94f, 0.04f), hp = V(s * 0.095f, 0.93f, 0), kn = V(s * 0.10f, 0.51f, 0.01f), an = V(s * 0.10f, 0.09f, -0.01f);
                d.Add(new BoneDef("shoulder" + s, Chest, sh, sh, el, 0.06f)); d.Add(new BoneDef("elbow" + s, d.Count - 1, el, el, wr, 0.05f)); d.Add(new BoneDef("wrist" + s, d.Count - 1, wr, wr, wr + V(s * 0.02f, -0.12f, 0.01f), 0.04f));
                d.Add(new BoneDef("hip" + s, Pelvis, hp, hp, kn, 0.07f)); d.Add(new BoneDef("knee" + s, d.Count - 1, kn, kn, an, 0.06f)); d.Add(new BoneDef("ankle" + s, d.Count - 1, an, an, V(s * 0.10f, 0.04f, 0.18f), 0.04f));
            }
            return d.ToArray();
        }

        static void AddBody(Sculpt s, bool detail)
        {
            Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
            s.Ellipsoid(V(0, 0.97f, -0.005f), V(0.155f, 0.10f, 0.10f), 0.05f).Ellipsoid(V(0, 1.12f, 0), V(0.135f, 0.13f, 0.09f), 0.05f).Ellipsoid(V(0, 1.32f, 0), V(0.165f, 0.17f, 0.105f), 0.05f);
            s.Cone(V(-0.17f, 1.45f, -0.01f), V(0.17f, 1.45f, -0.01f), 0.052f, 0.052f, 0.05f);
            s.Ellipsoid(V(0.075f, 1.35f, 0.065f), V(0.075f, 0.06f, 0.04f), 0.03f).Ellipsoid(V(-0.075f, 1.35f, 0.065f), V(0.075f, 0.06f, 0.04f), 0.03f);
            s.Ellipsoid(V(0.08f, 0.95f, -0.06f), V(0.08f, 0.08f, 0.07f), 0.04f).Ellipsoid(V(-0.08f, 0.95f, -0.06f), V(0.08f, 0.08f, 0.07f), 0.04f);
            s.Cone(V(0, 1.49f, -0.005f), V(0, 1.60f, 0.01f), 0.05f, 0.046f, 0.03f);
            foreach (int sd in new[] { 1, -1 })
            {
                s.Cone(V(sd * 0.2f, 1.45f, 0), V(sd * 0.265f, 1.18f, 0), 0.049f, 0.038f, 0.04f); s.Cone(V(sd * 0.265f, 1.18f, 0), V(sd * 0.31f, 0.94f, 0.04f), 0.038f, 0.028f, 0.03f);
                s.Cone(V(sd * 0.095f, 0.93f, 0), V(sd * 0.10f, 0.51f, 0.01f), 0.086f, 0.058f, 0.04f); s.Cone(V(sd * 0.10f, 0.51f, 0.01f), V(sd * 0.10f, 0.09f, -0.01f), 0.058f, 0.036f, 0.03f);
                s.Ellipsoid(V(sd * 0.10f, 0.37f, -0.03f), V(0.046f, 0.095f, 0.05f), 0.03f);               // calf
                s.Ellipsoid(V(sd * 0.10f, 0.055f, 0.06f), V(0.045f, 0.04f, 0.12f), 0.02f);                // foot
                if (detail)
                {   // hand: palm, four fingers, thumb
                    Vector3 w = V(sd * 0.31f, 0.94f, 0.04f);
                    s.Ellipsoid(w + V(sd * 0.012f, -0.06f, 0.005f), V(0.032f, 0.062f, 0.017f), 0.015f, V(0, 0, sd * -4f));
                    for (int f = 0; f < 4; f++) { float fx = (f - 1.5f) * 0.0125f; float len = 0.06f + (f == 1 || f == 2 ? 0.01f : 0f) - (f == 3 ? 0.012f : 0f); Vector3 a = w + V(sd * 0.012f + fx, -0.105f, 0.006f); s.Cone(a, a + V(0, -len, 0.008f), 0.0095f, 0.0075f, 0.01f); }
                    s.Cone(w + V(-sd * 0.012f, -0.06f, 0.012f), w + V(-sd * 0.03f, -0.115f, 0.026f), 0.012f, 0.0085f, 0.015f);
                }
            }
            // head: skull, face, jaw, chin, brow, cheekbones, nose, lips, ears; eye sockets and the mouth line are carved in
            s.Ellipsoid(V(0, 1.70f, -0.005f), V(0.088f, 0.115f, 0.105f), 0.04f).Ellipsoid(V(0, 1.665f, 0.03f), V(0.074f, 0.095f, 0.08f), 0.04f).Ellipsoid(V(0, 1.615f, 0.03f), V(0.066f, 0.055f, 0.074f), 0.04f);
            s.Ellipsoid(V(0, 1.585f, 0.062f), V(0.032f, 0.03f, 0.03f), 0.03f).Ellipsoid(V(0, 1.706f, 0.086f), V(0.07f, 0.014f, 0.026f), 0.03f);
            s.Ellipsoid(V(0.05f, 1.672f, 0.078f), V(0.03f, 0.02f, 0.025f), 0.03f).Ellipsoid(V(-0.05f, 1.672f, 0.078f), V(0.03f, 0.02f, 0.025f), 0.03f);
            s.Cone(V(0, 1.688f, 0.092f), V(0, 1.648f, 0.124f), 0.0095f, 0.0145f, 0.025f).Ellipsoid(V(0.016f, 1.644f, 0.108f), V(0.011f, 0.009f, 0.012f), 0.008f).Ellipsoid(V(-0.016f, 1.644f, 0.108f), V(0.011f, 0.009f, 0.012f), 0.008f);
            s.Ellipsoid(V(0, 1.611f, 0.094f), V(0.028f, 0.008f, 0.013f), 0.008f).Ellipsoid(V(0, 1.598f, 0.09f), V(0.025f, 0.009f, 0.014f), 0.008f);
            s.Ellipsoid(V(0.09f, 1.69f, -0.005f), V(0.011f, 0.03f, 0.02f), 0.01f).Ellipsoid(V(-0.09f, 1.69f, -0.005f), V(0.011f, 0.03f, 0.02f), 0.01f);
            s.SubEllipsoid(V(0.034f, 1.70f, 0.09f), V(0.021f, 0.015f, 0.02f), 0.012f).SubEllipsoid(V(-0.034f, 1.70f, 0.09f), V(0.021f, 0.015f, 0.02f), 0.012f);
            s.SubEllipsoid(V(0, 1.6055f, 0.1f), V(0.026f, 0.0018f, 0.012f), 0.002f);
        }

        static void BuildShared()
        {
            if (built && mSkin != null && mHead != null) return; built = true; defs = Defs();
            Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
            float vox = 0.0145f; Vector3 mn = V(-0.46f, -0.02f, -0.22f), mx = V(0.46f, 1.82f, 0.30f);
            // skin: everything
            var skin = new Sculpt(); AddBody(skin, true); skin.clipYMax = 1.585f;
            mSkin = SkinBuilder.Build(skin.Mesh(mn, mx, vox * 0.85f), defs, skin, "human_skin", (p, n) => { Color c = Color.white; // blush on the lips
                if (p.y > 1.57f && p.y < 1.72f) { float l = Mathf.Clamp01(1f - Mathf.Abs(p.y - 1.608f) / 0.012f) * Mathf.Clamp01(p.z * 14f); c = Color.Lerp(c, new Color(0.85f, 0.45f, 0.42f), l * 0.8f); }
                return c; }, 1.2f);
            // head: its own, much finer mesh so the nose, lips and eyelids hold their shape
            var headS = new Sculpt(); AddBody(headS, false); headS.clipYMin = 1.55f;
            mHead = SkinBuilder.Build(headS.Mesh(V(-0.125f, 1.52f, -0.14f), V(0.125f, 1.83f, 0.17f), 0.0052f), defs, headS, "human_head", (p, n) => { Color c = Color.white;
                float l = Mathf.Clamp01(1f - Mathf.Abs(p.y - 1.605f) / 0.011f) * Mathf.Clamp01(p.z * 14f); c = Color.Lerp(c, new Color(0.85f, 0.45f, 0.42f), l * 0.8f); return c; }, 1.2f);
            // shirt: torso only, snug
            var shirt = new Sculpt(); shirt.Ellipsoid(V(0, 0.97f, -0.005f), V(0.155f, 0.10f, 0.10f), 0.05f).Ellipsoid(V(0, 1.12f, 0), V(0.135f, 0.13f, 0.09f), 0.05f).Ellipsoid(V(0, 1.32f, 0), V(0.165f, 0.17f, 0.105f), 0.05f).Cone(V(-0.17f, 1.45f, -0.01f), V(0.17f, 1.45f, -0.01f), 0.052f, 0.052f, 0.05f);
            shirt.Ellipsoid(V(0, 1.49f, -0.012f), V(0.058f, 0.035f, 0.058f), 0.03f); shirt.Ellipsoid(V(0.075f, 1.35f, 0.065f), V(0.075f, 0.06f, 0.04f), 0.03f).Ellipsoid(V(-0.075f, 1.35f, 0.065f), V(0.075f, 0.06f, 0.04f), 0.03f); shirt.inflate = 0.009f; shirt.clipYMin = 1.0f; shirt.clipYMax = 1.545f;
            mShirt = SkinBuilder.Build(shirt.Mesh(V(-0.3f, 0.95f, -0.2f), V(0.3f, 1.58f, 0.22f), vox * 0.9f), defs, shirt, "human_shirt", null, 1.2f);
            // jacket: torso + sleeves, open at the front, hem at the hips, raised collar
            var jacket = new Sculpt(); jacket.Ellipsoid(V(0, 1.12f, 0), V(0.135f, 0.13f, 0.09f), 0.05f).Ellipsoid(V(0, 1.32f, 0), V(0.165f, 0.17f, 0.105f), 0.05f).Cone(V(-0.17f, 1.45f, -0.01f), V(0.17f, 1.45f, -0.01f), 0.052f, 0.052f, 0.05f);
            jacket.Ellipsoid(V(0, 0.99f, -0.005f), V(0.158f, 0.07f, 0.104f), 0.05f);
            foreach (int sd in new[] { 1, -1 }) { jacket.Cone(V(sd * 0.2f, 1.45f, 0), V(sd * 0.265f, 1.18f, 0), 0.049f, 0.038f, 0.04f); jacket.Cone(V(sd * 0.265f, 1.18f, 0), V(sd * 0.305f, 0.99f, 0.035f), 0.038f, 0.031f, 0.03f); }
            jacket.Ellipsoid(V(0, 1.505f, -0.02f), V(0.075f, 0.035f, 0.075f), 0.03f);
            jacket.SubBox(V(0, 1.22f, 0.15f), V(0.016f, 0.34f, 0.07f), 0.01f); jacket.SubEllipsoid(V(0, 1.50f, 0.11f), V(0.05f, 0.06f, 0.06f), 0.02f);
            jacket.inflate = 0.017f; jacket.clipYMin = 0.97f;
            mJacket = SkinBuilder.Build(jacket.Mesh(V(-0.46f, 0.92f, -0.2f), V(0.46f, 1.58f, 0.24f), vox * 0.9f), defs, jacket, "human_jacket", null, 1.2f);
            // jeans
            var pants = new Sculpt(); pants.Ellipsoid(V(0, 0.97f, -0.005f), V(0.155f, 0.10f, 0.10f), 0.05f).Ellipsoid(V(0.08f, 0.95f, -0.06f), V(0.08f, 0.08f, 0.07f), 0.04f).Ellipsoid(V(-0.08f, 0.95f, -0.06f), V(0.08f, 0.08f, 0.07f), 0.04f);
            foreach (int sd in new[] { 1, -1 }) { pants.Cone(V(sd * 0.095f, 0.93f, 0), V(sd * 0.10f, 0.51f, 0.01f), 0.086f, 0.058f, 0.04f); pants.Cone(V(sd * 0.10f, 0.51f, 0.01f), V(sd * 0.10f, 0.1f, -0.01f), 0.058f, 0.044f, 0.03f); pants.Ellipsoid(V(sd * 0.10f, 0.37f, -0.03f), V(0.046f, 0.095f, 0.05f), 0.03f); }
            pants.inflate = 0.012f; pants.clipYMax = 1.045f; pants.clipYMin = 0.07f;
            mPants = SkinBuilder.Build(pants.Mesh(V(-0.26f, 0.05f, -0.2f), V(0.26f, 1.06f, 0.2f), vox * 0.9f), defs, pants, "human_pants", (p, n) => (p.y > 1.0f ? new Color(0.35f, 0.33f, 0.33f) : Color.white), 1.2f);
            // shoes: foot + ankle, white sole by vertex colour
            var shoes = new Sculpt(); foreach (int sd in new[] { 1, -1 }) { shoes.Ellipsoid(V(sd * 0.10f, 0.055f, 0.06f), V(0.045f, 0.04f, 0.12f), 0.02f); shoes.Cone(V(sd * 0.10f, 0.14f, -0.01f), V(sd * 0.10f, 0.07f, -0.01f), 0.04f, 0.045f, 0.02f); }
            shoes.inflate = 0.014f; shoes.clipYMax = 0.13f; shoes.clipYMin = 0.0f;
            mShoes = SkinBuilder.Build(shoes.Mesh(V(-0.2f, -0.02f, -0.1f), V(0.2f, 0.16f, 0.26f), vox * 0.7f), defs, shoes, "human_shoes", (p, n) => (p.y < 0.032f ? new Color(0.95f, 0.93f, 0.9f) : new Color(0.55f, 0.52f, 0.58f)), 1.2f);
            // hair: short, swept forward over the brow
            var hair = new Sculpt(); hair.Ellipsoid(V(0, 1.708f, -0.012f), V(0.093f, 0.119f, 0.109f), 0.02f).Ellipsoid(V(0, 1.742f, 0.062f), V(0.082f, 0.026f, 0.05f), 0.02f);
            hair.SubEllipsoid(V(0, 1.665f, 0.07f), V(0.075f, 0.092f, 0.07f), 0.03f); hair.SubEllipsoid(V(0.07f, 1.69f, 0.03f), V(0.03f, 0.04f, 0.05f), 0.03f); hair.SubEllipsoid(V(-0.07f, 1.69f, 0.03f), V(0.03f, 0.04f, 0.05f), 0.03f); hair.inflate = 0.006f; hair.clipYMin = 1.66f;
            mHair = SkinBuilder.Build(hair.Mesh(V(-0.14f, 1.64f, -0.16f), V(0.14f, 1.85f, 0.16f), vox * 0.6f), defs, hair, "human_hair", null, 2f);
        }

        // ------------------------------------------------------------------ materials
        static Texture2D Tex(string n) { return Resources.Load<Texture2D>("Textures/" + n); }
        static Material SkinMat(string hex, float smooth, float sss, string detail, float scale, float strength, float wrap = 0.5f, float rim = 0.2f, string sssHex = "#e0553c")
        {
            var m = new Material(Shader.Find("Hearthvale/Skin")) { name = "living" };
            Color bc, sc; ColorUtility.TryParseHtmlString(hex, out bc); ColorUtility.TryParseHtmlString(sssHex, out sc); m.SetColor("_BaseColor", bc); m.SetFloat("_Smoothness", smooth); m.SetFloat("_SssAmount", sss); m.SetColor("_SssColor", sc); m.SetFloat("_Wrap", wrap); m.SetFloat("_Rim", rim);
            var t = Tex(detail); if (t != null) m.SetTexture("_DetailMap", t); m.SetFloat("_DetailScale", scale); m.SetFloat("_DetailStrength", strength); m.SetFloat("_MapScale", 0f);
            return m;
        }

        public Material SkinMaterial(string hex, float smooth, float sss, string detail, float scale, float strength, float wrap = 0.5f, float rim = 0.2f, string sssHex = "#e0553c") { return SkinMat(hex, smooth, sss, detail, scale, strength, wrap, rim, sssHex); }
        public static Avatar Build(string shirtColor, int seed = 0) { BuildShared(); var a = new Avatar(); a.Create(shirtColor, seed); return a; }

        SkinnedMeshRenderer Add(Mesh mesh, Material mat, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(root.transform, false);
            var r = go.AddComponent<SkinnedMeshRenderer>(); r.sharedMesh = mesh; r.bones = bones; r.rootBone = bones[0]; r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; r.updateWhenOffscreen = true;
            renderers.Add(r); return r;
        }

        void Create(string shirtColor, int seed)
        {
            root = new GameObject("Avatar"); bones = SkinBuilder.MakeBones(root.transform, defs);
            string skinHex = SkinCols[(seed * 7 + 1) % SkinCols.Length], hairHex = HairCols[(seed * 7 + 3) % HairCols.Length];
            Color jc = M.Hex(shirtColor).gamma * 0.7f; string jacketHex = "#" + ColorUtility.ToHtmlStringRGB(jc);
            var skinMat = SkinMat(skinHex, 0.42f, 0.85f, "skin_n", 26f, 0.55f); rSkin = Add(mSkin, skinMat, "skin"); rHead = Add(mHead, skinMat, "head");
            rShirt = Add(mShirt, SkinMat(shirtColor, 0.12f, 0.08f, "cloth_n", 38f, 0.9f, 0.3f, 0.05f), "shirt");
            rJacket = Add(mJacket, SkinMat(jacketHex, 0.15f, 0.06f, "cloth_n", 30f, 1.0f, 0.3f, 0.06f), "jacket");
            rPants = Add(mPants, SkinMat("#384463", 0.12f, 0.05f, "cloth_n", 34f, 1.1f, 0.3f, 0.04f), "jeans");
            rShoes = Add(mShoes, SkinMat("#ffffff", 0.35f, 0.02f, "hide_n", 20f, 0.5f, 0.2f, 0.1f), "shoes");
            rHair = Add(mHair, SkinMat(hairHex, 0.4f, 0.1f, "hide_n", 140f, 1.8f, 0.4f, 0.3f, hairHex), "hair");
            // eyes: white ball, brown iris, black pupil, glossy
            Material white = SkinMat("#f2efe8", 0.85f, 0.1f, "skin_n", 8f, 0.1f, 0.5f, 0.1f), iris = SkinMat("#4a2f1c", 0.9f, 0.1f, "skin_n", 8f, 0.1f), pupil = SkinMat("#050505", 0.95f, 0f, "skin_n", 8f, 0f);
            foreach (int sd in new[] { 1, -1 })
            {
                Vector3 c = new Vector3(sd * 0.034f, 1.701f, 0.088f);
                Eye(c, 0.0118f, 1f, white); Eye(c + new Vector3(0, 0, 0.0092f), 0.0066f, 0.35f, iris); Eye(c + new Vector3(0, 0, 0.0111f), 0.0033f, 0.3f, pupil);
            }
            gunMount = new GameObject("gunMount").transform; gunMount.SetParent(root.transform, false);
            Pose("idle");
        }
        static Mesh sph;
        void Eye(Vector3 pos, float r, float zScale, Material m)
        {
            if (sph == null) { var s = GameObject.CreatePrimitive(PrimitiveType.Sphere); sph = s.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(s); }
            var go = new GameObject("eye"); go.transform.SetParent(bones[Head], false); go.transform.position = root.transform.TransformPoint(pos); go.transform.rotation = root.transform.rotation; go.transform.localScale = new Vector3(2 * r, 2 * r, 2 * r * zScale);
            go.AddComponent<MeshFilter>().sharedMesh = sph; var rr = go.AddComponent<MeshRenderer>(); rr.sharedMaterial = m; rr.shadowCastingMode = ShadowCastingMode.Off; renderers.Add(rr); eyeRenderers.Add(rr);
        }

        /// <summary>The gun held out in front of the chest (rebuilt when you change weapon).</summary>
        public void SetGun(string kind)
        {
            if (kind == gunKind) return; gunKind = kind;
            if (gun != null) Object.Destroy(gun);
            if (kind == null) return;
            gun = GunModels.Build(kind, true); gun.transform.SetParent(gunMount, false); gun.transform.localPosition = Vector3.zero; gun.transform.localScale = Vector3.one * 0.7f;
            foreach (var r in gun.GetComponentsInChildren<Renderer>()) { renderers.Add(r); if (!vis) r.enabled = false; else r.shadowCastingMode = ShadowCastingMode.On; }  // new gun obeys current visibility: no 2nd gun in first person
            renderers.RemoveAll(r => r == null);
        }

        /// <summary>pose: "walk" (amount 0..1 = speed), "sit", "idle". armed lifts the arms to hold the gun. Gait: stride length follows speed (no foot skating),
        /// opposite arm/leg swing, pelvis bob/sway/rotation with counter-rotating shoulders, knee flex + heel strike, forward lean when running, breathing/weight shift when standing, jump tuck.</summary>
        public void Pose(string name, float dt = 0, float amount = 0, float lookPitch = 0, bool armed = false, bool airborne = false)
        {
            float R = Mathf.Rad2Deg;
            if (name == "sit")
            {
                bones[Head].localRotation = Quaternion.Euler(-lookPitch * 0.5f * R, 0, 0); bones[Neck].localRotation = Quaternion.Euler(-lookPitch * 0.2f * R, 0, 0);
                bones[Pelvis].localPosition = defs[Pelvis].pos;
                foreach (int s in new[] { 1, -1 }) { bones[B(s, 3)].localRotation = Quaternion.Euler(-85f, 0, 0); bones[B(s, 4)].localRotation = Quaternion.Euler(88f, 0, 0); bones[B(s, 0)].localRotation = Quaternion.Euler(-50f, 0, 0); bones[B(s, 1)].localRotation = Quaternion.Euler(-35f, 0, 0); }
                bones[Spine].localRotation = Quaternion.Euler(4f, 0, 0); return;
            }
            if (dt > 0) { blend = Mathf.Lerp(blend, amount, 1f - Mathf.Exp(-dt * 9f)); idleT += dt; air = Mathf.MoveTowards(air, airborne ? 1f : 0f, dt * 6f); }
            else { blend = amount; air = airborne ? 1f : 0f; }
            float k = Mathf.Clamp01(blend), kg = Mathf.Clamp01(k * 5f), idle = 1f - kg;
            float stride = Mathf.Lerp(1.45f, 3.0f, k);                       // metres per full cycle (two steps)
            phase += dt * blend * 9.5f * Mathf.PI * 2f / stride * (1f - air * 0.9f);
            float sinP = Mathf.Sin(phase), cosP = Mathf.Cos(phase);
            float breath = Mathf.Sin(idleT * 1.7f), shift = Mathf.Sin(idleT * 0.35f);

            // pelvis: bob (highest as the legs cross), sink with speed, sway over the stance leg, yaw + drop on the swing side
            bones[Pelvis].localPosition = defs[Pelvis].pos + new Vector3(-cosP * 0.015f * kg + shift * 0.012f * idle, Mathf.Cos(2f * phase) * 0.016f * kg - 0.02f * k, 0);
            bones[Pelvis].localRotation = Quaternion.Euler(0, -sinP * 5f * kg, -cosP * 2.5f * kg + shift * 1.8f * idle);
            float lean = 2f + 9f * k * k;
            bones[Spine].localRotation = Quaternion.Euler(lean * 0.6f + 3f * k, sinP * 3f * kg, cosP * 1.5f * kg - shift * 1.2f * idle);
            bones[Chest].localRotation = Quaternion.Euler(lean * 0.4f + breath * 0.9f * idle, sinP * 5f * kg, Mathf.Sin(phase * 2) * 0.8f * kg);
            // head/neck stay level against the body motion and drift a little when idle
            bones[Neck].localRotation = Quaternion.Euler(-lookPitch * 0.2f * R - lean * 0.35f, Mathf.Sin(idleT * 0.23f) * 4f * idle - sinP * 3f * kg, 0);
            bones[Head].localRotation = Quaternion.Euler(-lookPitch * 0.5f * R - lean * 0.3f + breath * 0.5f * idle, Mathf.Sin(idleT * 0.41f + 1f) * 3f * idle, 0);

            foreach (int s in new[] { 1, -1 })
            {
                bool right = s > 0; float ph = phase + (right ? 0f : Mathf.PI), sn = Mathf.Sin(ph), cs = Mathf.Cos(ph);   // leg phase: forward when sn>0 (heel strike at pi/2)
                float hip = -(14f + 32f * k) * sn * kg;
                float swing = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(ph + 0.35f)), 1.5f), load = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(ph - Mathf.PI * 0.5f - 0.5f)), 4f);
                float knee = 3f + (30f + 45f * k) * swing * kg + 12f * load * kg + Mathf.Max(0f, right ? shift : -shift) * 5f * idle;
                float arm = (8f + 28f * k) * sn * kg + (Mathf.Sin(idleT * 0.9f + (right ? 0 : 1.7f)) * 1.5f) * idle;      // opposite to the same-side leg
                float elbow = 12f + 30f * k + 22f * Mathf.Max(0f, -sn) * kg;
                if (armed)
                {   // armUp 0 = gun lowered at the side (muzzle down), 1 = raised and pointing where you look
                    if (right) { arm = Mathf.Lerp(arm * 0.6f - 8f, -75f + lookPitch * R * 0.7f + sn * 2f * kg, armUp); elbow = Mathf.Lerp(30f, 12f, armUp); }
                    else { arm = Mathf.Lerp(arm, arm * 0.35f - 18f, armUp); elbow = Mathf.Lerp(elbow, 28f, armUp); }
                }
                if (air > 0) { hip = Mathf.Lerp(hip, right ? -38f : 8f, air); knee = Mathf.Lerp(knee, right ? 62f : 28f, air); if (!armed || !right) { arm = Mathf.Lerp(arm, -35f, air); elbow = Mathf.Lerp(elbow, 30f, air); } }
                bones[B(s, 3)].localRotation = Quaternion.Euler(hip, 0, 0);
                bones[B(s, 4)].localRotation = Quaternion.Euler(knee, 0, 0);
                bones[B(s, 5)].localRotation = Quaternion.Euler(-(hip + knee) * 0.5f, 0, 0);                             // keep the foot closer to flat
                bones[B(s, 0)].localRotation = Quaternion.Euler(arm, 0, 0); bones[B(s, 1)].localRotation = Quaternion.Euler(-elbow, 0, 0);
            }
            // the gun sits in the right hand and points where the character looks
            gunMount.position = bones[B(1, 2)].position + root.transform.TransformDirection(new Vector3(-0.012f, 0.045f, -0.11f)); gunMount.rotation = root.transform.rotation * Quaternion.Euler(-lookPitch * Mathf.Rad2Deg * armUp + 50f * (1f - armUp), 0, 0);
        }
        public float armUp = 1f;

        bool vis = true;
        public void SetVisible(bool visible)
        {
            vis = visible;
            var mode = visible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            foreach (var r in renderers) if (r != null) { if (r is SkinnedMeshRenderer) r.shadowCastingMode = mode; else r.enabled = visible; }
        }
    }

    /// <summary>Drives the player's own avatar: shown in third person, holds your current gun, walks when you walk.</summary>
    public sealed class PlayerAvatar : MonoBehaviour
    {
        public PlayerController player; public Arsenal arsenal; public Garage garage; Avatar av; bool shown = true, wasInV; float raiseHold, armUpT;
        void Start() { av = Avatar.Build("#ff9eb5", 3); av.root.transform.SetParent(player.transform, false); arsenal.OnChange += Sync; Sync(); }
        void Sync() { if (av != null && arsenal.owned.ContainsKey(arsenal.current)) av.SetGun(arsenal.current); }
        void LateUpdate()
        {
            if (av == null) return;
            bool inV = player.vehicle != null || player.pillion;
            bool third = (player.pillion ? true : inV ? !(garage != null && garage.FirstPerson) : player.ThirdPerson) && !player.dead;
            if (third != shown) { shown = third; av.SetVisible(third); }
            if (inV != wasInV) { wasInV = inV; if (inV) av.SetGun(null); else Sync(); av.SetVisible(third); shown = third; }
            if (inV) { av.SetGun(null); av.armUp = 0f; armUpT = 0f; raiseHold = 0f; av.root.transform.localRotation = Quaternion.identity; av.Pose("sit", Time.deltaTime, 0, 0, false); return; }
            av.root.transform.localRotation = Quaternion.Euler(0, player.Yaw * Mathf.Rad2Deg - player.transform.eulerAngles.y, 0);
            // gun is only raised while aiming (RMB) or shooting (and briefly after); otherwise it hangs lowered
            if (arsenal.aim > 0.1f || HvInput.Down("LMB") || arsenal.kick > 0.02f) raiseHold = 1.2f; else raiseHold -= Time.deltaTime;
            armUpT = Mathf.MoveTowards(armUpT, raiseHold > 0 && !player.swimming ? 1f : 0f, Time.deltaTime * 5f); av.armUp = Mathf.SmoothStep(0, 1, armUpT);
            var v = player.Velocity; float speed = new Vector2(v.x, v.z).magnitude;
            float amt = Mathf.Clamp01(speed / 9.5f);
            av.Pose(player.dead ? "sit" : speed > 0.3f ? "walk" : "idle", Time.deltaTime, player.swimming ? 0.3f : amt, player.Pitch, !player.swimming, !player.OnGround && !player.swimming);
        }
    }
}
