using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>
    /// Realistic four-legged animals built with the organic sculpting system: hippo (barrel body, huge muzzle,
    /// wet grey hide), crocodile (long armoured body, snout, scaled hide), gorilla (massive chest, long knuckle-walking
    /// arms, dark coat) and capybara (stocky barrel, blunt head, ginger coat). One seamless skin each, skinned to a
    /// 15-bone skeleton: body, chest, neck, head, three tail bones, and a thigh + shin bone for each leg.
    /// They face +z; feet are at y = 0.
    /// </summary>
    public static class Quadruped
    {
        public const int Body = 0, Chest = 1, Neck = 2, HeadB = 3, Tail0 = 4, Tail1 = 5, Tail2 = 6, FL = 7, FR = 9, BL = 11, BR = 13; // leg bones: upper, lower = +1

        sealed class Spec
        {
            public float L, R, legLen, legW, legR1, legR2, tailLen, tailR, bodyFlat = 1f; public bool croc, gorilla;
            public string skin, belly, detail, sss; public float detailScale, detailStr, smooth, sssAmt, wet; public Color tint = Color.white;
        }
        static readonly Dictionary<int, Spec> specs = new Dictionary<int, Spec>
        {
            { 0, new Spec { L = 1.95f, R = 0.62f, legLen = 0.46f, legW = 0.34f, legR1 = 0.2f, legR2 = 0.14f, tailLen = 0.22f, tailR = 0.07f, skin = "#7b706b", belly = "#c8a79b", detail = "hide_n", detailScale = 7, detailStr = 0.9f, smooth = 0.62f, sssAmt = 0.55f, wet = 0.5f, sss = "#d9726b" } },
            { 1, new Spec { L = 2.1f, R = 0.27f, legLen = 0.22f, legW = 0.3f, legR1 = 0.1f, legR2 = 0.06f, tailLen = 1.45f, tailR = 0.2f, bodyFlat = 0.8f, croc = true, skin = "#5a6b3d", belly = "#d6cf99", detail = "scales_n", detailScale = 22, detailStr = 1.3f, smooth = 0.35f, sssAmt = 0.15f, wet = 0.2f, sss = "#9ab04a" } },
            { 2, new Spec { L = 1.15f, R = 0.52f, legLen = 0.5f, legW = 0.36f, legR1 = 0.17f, legR2 = 0.13f, tailLen = 0.05f, tailR = 0.05f, gorilla = true, skin = "#2b2a30", belly = "#4a444a", detail = "hide_n", detailScale = 40, detailStr = 1.3f, smooth = 0.28f, sssAmt = 0.1f, wet = 0f, sss = "#7a5a50" } },
            { 3, new Spec { L = 1.0f, R = 0.31f, legLen = 0.26f, legW = 0.19f, legR1 = 0.085f, legR2 = 0.06f, tailLen = 0.04f, tailR = 0.05f, skin = "#a8703f", belly = "#d2a572", detail = "hide_n", detailScale = 55, detailStr = 1.2f, smooth = 0.2f, sssAmt = 0.15f, wet = 0f, sss = "#d98a55" } },
        };

        static readonly Dictionary<int, Mesh> meshCache = new Dictionary<int, Mesh>(); static readonly Dictionary<int, BoneDef[]> defCache = new Dictionary<int, BoneDef[]>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { meshCache.Clear(); defCache.Clear(); }

        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

        // y of the body centre, z of the shoulders and the hips
        static void Layout(Spec s, out float by, out float cz, out float hz) { by = s.legLen + s.R * (s.croc ? 0.9f : 0.78f); cz = s.L * 0.30f; hz = -s.L * 0.30f; }

        static BoneDef[] Defs(int type)
        {
            var s = specs[type]; float by, cz, hz; Layout(s, out by, out cz, out hz); var d = new List<BoneDef>();
            float headZ = s.L * 0.5f + (s.croc ? 0.2f : s.gorilla ? 0.22f : 0.25f), headY = by + (s.croc ? 0.04f : s.gorilla ? 0.28f : 0.1f);
            d.Add(new BoneDef("body", -1, V(0, by, 0), V(0, by, hz * 0.7f), V(0, by, cz * 0.7f), 0.25f));
            d.Add(new BoneDef("chest", Body, V(0, by, cz * 0.8f), V(0, by, cz * 0.5f), V(0, by + 0.02f, cz + 0.1f), 0.2f));
            d.Add(new BoneDef("neck", Chest, V(0, by + 0.04f, cz + 0.12f), V(0, by + 0.04f, cz + 0.1f), V(0, headY, headZ - 0.2f), 0.15f));
            d.Add(new BoneDef("head", Neck, V(0, headY, headZ - 0.18f), V(0, headY, headZ - 0.18f), V(0, headY - 0.02f, headZ + s.L * 0.3f), 0.2f));
            float t = s.tailLen; Vector3 t0 = V(0, by, hz - s.R * 0.5f), t1 = t0 + V(0, -0.02f, -t * 0.35f), t2 = t1 + V(0, -0.03f, -t * 0.35f), t3 = t2 + V(0, -0.03f, -t * 0.3f);
            d.Add(new BoneDef("tail0", Body, t0, t0, t1, 0.15f)); d.Add(new BoneDef("tail1", Tail0, t1, t1, t2, 0.15f)); d.Add(new BoneDef("tail2", Tail1, t2, t2, t3, 0.15f));
            foreach (var leg in new[] { new Vector3(1, 0, 1), new Vector3(-1, 0, 1), new Vector3(1, 0, 0), new Vector3(-1, 0, 0) })
            {
                bool front = leg.z > 0.5f; float x = leg.x * s.legW, z = front ? cz : hz; float ll = s.legLen + (s.gorilla && front ? 0.45f : 0f);
                float topY = by - s.R * 0.25f; Vector3 top = V(x, topY, z), knee = V(x, topY - (topY) * 0.5f, z + (front ? 0.03f : -0.04f)), foot = V(x, 0.03f, z + (front ? 0.07f : 0.03f));
                if (s.croc) { top = V(leg.x * (s.R * 0.8f), by - 0.04f, z); knee = V(leg.x * (s.legW + 0.08f), s.legLen * 0.55f, z + 0.03f); foot = V(leg.x * (s.legW + 0.1f), 0.02f, z + 0.09f); }
                d.Add(new BoneDef("up", front ? Chest : Body, top, top, knee, 0.09f)); d.Add(new BoneDef("lo", d.Count - 1, knee, knee, foot, 0.07f));
            }
            return d.ToArray();
        }

        static void AddShape(Sculpt c, int type)
        {
            var s = specs[type]; float by, cz, hz; Layout(s, out by, out cz, out hz); float R = s.R, L = s.L, f = s.bodyFlat;
            // torso: shoulders, belly, hips
            c.Ellipsoid(V(0, by, cz * 0.8f), V(R * 0.95f, R * f * 1.02f, L * 0.26f), 0.12f).Ellipsoid(V(0, by - 0.02f, 0), V(R, R * f * 0.97f, L * 0.3f), 0.12f).Ellipsoid(V(0, by, hz * 0.8f), V(R * 0.93f, R * f * 0.97f, L * 0.26f), 0.12f);
            float headZ = L * 0.5f + (s.croc ? 0.2f : s.gorilla ? 0.22f : 0.25f), headY = by + (s.croc ? 0.04f : s.gorilla ? 0.28f : 0.1f);
            // neck
            c.Cone(V(0, by + 0.02f, cz + 0.05f), V(0, headY, headZ - 0.12f), R * (s.croc ? 0.42f : 0.62f), R * (s.croc ? 0.32f : 0.45f), 0.1f);
            // head
            if (type == 0)
            { // hippo: huge square muzzle, nostril and eye bumps, small ears
                c.Ellipsoid(V(0, headY + 0.02f, headZ), V(0.3f, 0.27f, 0.33f), 0.08f).Ellipsoid(V(0, headY - 0.09f, headZ + 0.38f), V(0.34f, 0.2f, 0.32f), 0.1f).Ellipsoid(V(0, headY - 0.17f, headZ + 0.34f), V(0.3f, 0.12f, 0.3f), 0.06f);
                foreach (int sd in new[] { 1, -1 })
                {
                    c.Ellipsoid(V(sd * 0.1f, headY + 0.05f, headZ + 0.66f), V(0.055f, 0.04f, 0.045f), 0.03f).Ellipsoid(V(sd * 0.2f, headY + 0.25f, headZ + 0.1f), V(0.085f, 0.07f, 0.08f), 0.05f);
                    c.Ellipsoid(V(sd * 0.24f, headY + 0.27f, headZ - 0.1f), V(0.04f, 0.075f, 0.05f), 0.03f, V(0, 0, sd * 25f));
                    c.Cone(V(sd * 0.2f, headY - 0.17f, headZ + 0.58f), V(sd * 0.21f, headY - 0.07f, headZ + 0.6f), 0.025f, 0.012f, 0.01f); // tusks
                }
                c.SubEllipsoid(V(0, headY - 0.145f, headZ + 0.45f), V(0.3f, 0.0045f, 0.2f), 0.005f);
            }
            else if (type == 1)
            { // crocodile: long flat snout, brow bumps
                c.Ellipsoid(V(0, headY, headZ - 0.05f), V(0.15f, 0.1f, 0.2f), 0.06f).Ellipsoid(V(0, headY - 0.02f, headZ + 0.38f), V(0.115f, 0.062f, 0.45f), 0.06f).Ellipsoid(V(0, headY - 0.065f, headZ + 0.35f), V(0.11f, 0.035f, 0.4f), 0.04f);
                foreach (int sd in new[] { 1, -1 }) { c.Ellipsoid(V(sd * 0.09f, headY + 0.085f, headZ + 0.02f), V(0.05f, 0.045f, 0.06f), 0.03f); c.Ellipsoid(V(sd * 0.04f, headY + 0.045f, headZ + 0.8f), V(0.022f, 0.02f, 0.03f), 0.01f); }
                c.SubEllipsoid(V(0, headY - 0.05f, headZ + 0.45f), V(0.12f, 0.0035f, 0.42f), 0.004f);
                for (int i = 0; i < 9; i++) { float z = hz + 0.1f + i * (L * 0.62f / 9f); c.Ellipsoid(V(0, by + R * f * 0.95f, z), V(0.05f, 0.04f, 0.08f), 0.02f); } // armoured back ridge
            }
            else if (type == 2)
            { // gorilla: massive chest, small head, heavy brow, big shoulders
                c.Ellipsoid(V(0, headY, headZ), V(0.19f, 0.21f, 0.21f), 0.07f).Ellipsoid(V(0, headY - 0.07f, headZ + 0.17f), V(0.14f, 0.1f, 0.12f), 0.06f).Ellipsoid(V(0, headY + 0.1f, headZ + 0.12f), V(0.17f, 0.05f, 0.07f), 0.04f).Ellipsoid(V(0, headY + 0.28f, headZ - 0.03f), V(0.1f, 0.12f, 0.12f), 0.05f);
                c.Ellipsoid(V(0, by + 0.12f, cz * 0.7f), V(0.5f, 0.3f, 0.35f), 0.15f);
                foreach (int sd in new[] { 1, -1 }) { c.Ellipsoid(V(sd * 0.04f, headY - 0.06f, headZ + 0.28f), V(0.03f, 0.022f, 0.03f), 0.02f); c.Ellipsoid(V(sd * 0.2f, headY, headZ - 0.03f), V(0.03f, 0.065f, 0.04f), 0.03f); c.Ellipsoid(V(sd * 0.4f, by + 0.2f, cz), V(0.25f, 0.28f, 0.28f), 0.2f); }
                c.SubEllipsoid(V(0, headY - 0.1f, headZ + 0.26f), V(0.11f, 0.0035f, 0.1f), 0.004f);
            }
            else
            { // capybara: blunt barrel head, small ears, split upper lip
                c.Ellipsoid(V(0, headY, headZ), V(0.15f, 0.15f, 0.2f), 0.08f).Ellipsoid(V(0, headY - 0.03f, headZ + 0.25f), V(0.115f, 0.115f, 0.15f), 0.08f);
                foreach (int sd in new[] { 1, -1 }) { c.Ellipsoid(V(sd * 0.1f, headY + 0.14f, headZ - 0.08f), V(0.035f, 0.05f, 0.025f), 0.02f); c.Ellipsoid(V(sd * 0.085f, headY + 0.07f, headZ + 0.12f), V(0.04f, 0.035f, 0.045f), 0.03f); }
                c.Ellipsoid(V(0, headY - 0.01f, headZ + 0.4f), V(0.06f, 0.05f, 0.04f), 0.03f);
                c.SubEllipsoid(V(0, headY - 0.1f, headZ + 0.34f), V(0.1f, 0.004f, 0.1f), 0.004f);
            }
            // tail: a chain of cones
            Vector3 t0 = V(0, by, hz - R * 0.5f), t1 = t0 + V(0, -0.02f, -s.tailLen * 0.35f), t2 = t1 + V(0, -0.03f, -s.tailLen * 0.35f), t3 = t2 + V(0, -0.03f, -s.tailLen * 0.3f);
            c.Cone(t0, t1, s.tailR, s.tailR * 0.8f, 0.05f).Cone(t1, t2, s.tailR * 0.8f, s.tailR * 0.5f, 0.05f).Cone(t2, t3, s.tailR * 0.5f, s.tailR * 0.15f, 0.03f);
            // legs
            foreach (var leg in new[] { new Vector3(1, 0, 1), new Vector3(-1, 0, 1), new Vector3(1, 0, 0), new Vector3(-1, 0, 0) })
            {
                bool front = leg.z > 0.5f; float x = leg.x * s.legW, z = front ? cz : hz; float topY = by - R * 0.25f;
                Vector3 top = V(x, topY, z), knee = V(x, topY - topY * 0.5f, z + (front ? 0.03f : -0.04f)), foot = V(x, 0.03f, z + (front ? 0.07f : 0.03f));
                if (s.croc) { top = V(leg.x * (R * 0.8f), by - 0.04f, z); knee = V(leg.x * (s.legW + 0.08f), s.legLen * 0.55f, z + 0.03f); foot = V(leg.x * (s.legW + 0.1f), 0.02f, z + 0.09f); }
                float r1 = s.legR1 * (s.gorilla && front ? 1.25f : 1f), r2 = s.legR2;
                c.Cone(top, knee, r1, (r1 + r2) * 0.5f, 0.08f).Cone(knee, foot + V(0, 0.03f, 0), (r1 + r2) * 0.5f, r2, 0.05f);
                float fl = type == 0 ? 0.14f : type == 1 ? 0.12f : type == 2 ? 0.14f : 0.07f;
                c.Ellipsoid(foot + V(0, 0.005f, 0.04f), V(r2 * 1.2f, 0.035f, fl), 0.03f);
                if (type == 2 && front) c.Ellipsoid(foot + V(0, 0.04f, 0.0f), V(0.1f, 0.07f, 0.1f), 0.04f); // knuckles
            }
        }

        static Mesh GetMesh(int type)
        {
            Mesh m; if (meshCache.TryGetValue(type, out m) && m != null) return m;
            var s = specs[type]; var defs = defCache[type] = Defs(type); var sc = new Sculpt(); AddShape(sc, type);
            float by, cz, hz; Layout(s, out by, out cz, out hz); float ex = s.L * 0.5f + 1.0f;
            Color sk, bl; ColorUtility.TryParseHtmlString(s.skin, out sk); ColorUtility.TryParseHtmlString(s.belly, out bl);
            float vox = type == 3 ? 0.02f : type == 2 ? 0.026f : 0.03f;
            var r = sc.Mesh(V(-1.1f, -0.03f, -ex - s.tailLen - 0.2f), V(1.1f, 2.0f, ex + 0.3f), vox);
            m = SkinBuilder.Build(r, defs, sc, "creature" + type, (p, n) =>
            {   // lighter belly and under-jaw, darker back, slight blotchiness
                float under = Mathf.Clamp01(-n.y * 1.4f + 0.1f); Color c = Color.Lerp(Color.white, new Color(bl.r / sk.r, bl.g / sk.g, bl.b / sk.b), under * 0.85f);
                float blot = (float)Hv.Fbm(p.x * 6 + type * 9, p.z * 6 + p.y * 4, 3); c *= Mathf.Lerp(0.82f, 1.08f, blot); c.a = 1; return c;
            }, 0.9f);
            meshCache[type] = m; return m;
        }

        public static AnimalRig Build(int type, CritterType ct)
        {
            var s = specs[type]; var mesh = GetMesh(type); var defs = defCache[type];
            var rig = new AnimalRig { type = type }; var root = new GameObject("Stinker_" + ct.name); rig.root = root;
            var bones = SkinBuilder.MakeBones(root.transform, defs);
            var go = new GameObject("skin"); go.transform.SetParent(root.transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>(); smr.sharedMesh = mesh; smr.bones = bones; smr.rootBone = bones[0]; smr.updateWhenOffscreen = true; smr.shadowCastingMode = ShadowCastingMode.On;
            Color sk; ColorUtility.TryParseHtmlString(s.skin, out sk);
            var mat = new Material(Shader.Find("Hearthvale/Skin")) { name = "creature" };
            mat.SetColor("_BaseColor", sk); mat.SetFloat("_Smoothness", s.smooth); mat.SetFloat("_Wet", s.wet); mat.SetFloat("_SssAmount", s.sssAmt); Color sc2; ColorUtility.TryParseHtmlString(s.sss, out sc2); mat.SetColor("_SssColor", sc2);
            var dt = Resources.Load<Texture2D>("Textures/" + s.detail); if (dt != null) mat.SetTexture("_DetailMap", dt); mat.SetFloat("_DetailScale", s.detailScale); mat.SetFloat("_DetailStrength", s.detailStr); mat.SetFloat("_MapScale", 0f);
            mat.SetFloat("_Rim", type == 3 || type == 2 ? 0.45f : 0.2f); mat.SetFloat("_Wrap", 0.5f);
            smr.sharedMaterial = mat; rig.mats.Add(mat);
            // eyes: dark, glossy, with a bright catchlight
            float by, cz, hz; Layout(s, out by, out cz, out hz); float headZ = s.L * 0.5f + (s.croc ? 0.2f : s.gorilla ? 0.22f : 0.25f), headY = by + (s.croc ? 0.04f : s.gorilla ? 0.28f : 0.1f);
            Vector3[] eyeAt = { V(0.2f, headY + 0.25f, headZ + 0.16f), V(0.09f, headY + 0.1f, headZ + 0.02f), V(0.185f, headY + 0.045f, headZ + 0.15f), V(0.1f, headY + 0.075f, headZ + 0.16f) };
            Vector3 e = eyeAt[type]; float er = type == 0 ? 0.04f : type == 1 ? 0.028f : type == 2 ? 0.026f : 0.022f;
            var eyeMat = new Material(Shader.Find("Hearthvale/Skin")) { name = "eye" }; eyeMat.SetColor("_BaseColor", type == 1 ? new Color(0.9f, 0.75f, 0.2f) : new Color(0.07f, 0.04f, 0.03f)); eyeMat.SetFloat("_Smoothness", 0.95f); eyeMat.SetFloat("_SssAmount", 0f); eyeMat.SetFloat("_MapScale", 0f); eyeMat.SetFloat("_DetailStrength", 0f); eyeMat.SetFloat("_Rim", 0.8f);
            var pupMat = new Material(eyeMat) { name = "pupil" }; pupMat.SetColor("_BaseColor", Color.black);
            var sph = GameObject.CreatePrimitive(PrimitiveType.Sphere); var sm = sph.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(sph);
            foreach (int sd in new[] { 1, -1 })
            {
                var eg = new GameObject("eye"); eg.transform.SetParent(bones[HeadB], false); eg.transform.position = root.transform.TransformPoint(new Vector3(sd * e.x, e.y, e.z)); eg.transform.localScale = Vector3.one * er * 2;
                eg.AddComponent<MeshFilter>().sharedMesh = sm; var er2 = eg.AddComponent<MeshRenderer>(); er2.sharedMaterial = eyeMat; er2.shadowCastingMode = ShadowCastingMode.Off;
                if (type == 1) { var pg = new GameObject("pupil"); pg.transform.SetParent(eg.transform, false); pg.transform.localPosition = new Vector3(0, 0, 0.42f); pg.transform.localScale = new Vector3(0.18f, 0.7f, 0.3f); pg.AddComponent<MeshFilter>().sharedMesh = sm; var pr = pg.AddComponent<MeshRenderer>(); pr.sharedMaterial = pupMat; }
            }
            rig.body = bones[Body]; rig.head = bones[HeadB]; rig.tail = bones[Tail0]; rig.eyes = bones[HeadB];
            for (int i = 0; i < 4; i++) rig.legs.Add(bones[FL + i * 2]);
            rig.quad = true; rig.bones = bones; rig.restPos = bones[Body].localPosition;
            root.transform.localScale = Vector3.one * ct.scale * (type == 0 ? 0.9f : 1f);
            return rig;
        }
    }
}
