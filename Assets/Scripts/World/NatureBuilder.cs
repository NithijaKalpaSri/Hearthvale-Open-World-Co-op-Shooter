using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>
    /// Port of World.buildNature (world.js): where every tree, bush, flower and rock stands, and the
    /// loot / hover-craft spots in the wild. Placement uses the SAME random-number sequence as the
    /// JS game, so the forest grows in the same places. The looks are new: realistic branching
    /// trees with leaf cards, needle-card conifers, rounded bushes, real flowers, noise-carved rocks.
    /// </summary>
    public sealed class NatureBuilder
    {
        public readonly List<InstancedField> fields = new List<InstancedField>();
        public readonly List<Vector2> bushSpots = new List<Vector2>();
        readonly GfxQuality quality; bool lowEnd { get { return quality == GfxQuality.LowEnd; } }

        public NatureBuilder(GfxQuality q) { quality = q; }

        // ---------------------------------------------------------------- colour helpers (three.js HSL in linear space)
        static void GetHSL(Color c, out float h, out float s, out float l)
        {
            float max = Mathf.Max(c.r, c.g, c.b), min = Mathf.Min(c.r, c.g, c.b); l = (min + max) / 2; h = 0; s = 0;
            if (max != min)
            {
                float d = max - min; s = l <= 0.5f ? d / (max + min) : d / (2 - max - min);
                if (max == c.r) h = (c.g - c.b) / d + (c.g < c.b ? 6 : 0); else if (max == c.g) h = (c.b - c.r) / d + 2; else h = (c.r - c.g) / d + 4;
                h /= 6;
            }
        }
        static float Hue(float p, float q, float t) { if (t < 0) t += 1; if (t > 1) t -= 1; if (t < 1f / 6) return p + (q - p) * 6 * t; if (t < 0.5f) return q; if (t < 2f / 3) return p + (q - p) * 6 * (2f / 3 - t); return p; }
        static Color SetHSL(float h, float s, float l)
        {
            if (s == 0) return new Color(l, l, l);
            float p = l <= 0.5f ? l * (1 + s) : l + s - l * s, q = 2 * l - p;
            return new Color(Hue(q, p, h + 1f / 3), Hue(q, p, h), Hue(q, p, h - 1f / 3));
        }
        static Color OffsetL(Color c, float dl) { float h, s, l; GetHSL(c, out h, out s, out l); return SetHSL(h, s, Mathf.Clamp01(l + dl)); }
        static Vector4 V4(Color c) { return new Vector4(c.r, c.g, c.b, 1f); }

        // ---------------------------------------------------------------- materials
        static Texture2D Tex(string n) { return Resources.Load<Texture2D>("Textures/" + n); }
        public static Material PlantMat(string name, string tex, string normal, Color baseColor, float smooth, float wind, float trans, float normalUp, bool cutout, bool twoSided, float tintAmount, float uvScale = 1f)
        {
            var m = new Material(Shader.Find("Hearthvale/Plant")) { name = name };
            var t = tex != null ? Tex(tex) : null; m.SetTexture("_BaseMap", t != null ? (Texture)t : Texture2D.whiteTexture);
            if (normal != null && Tex(normal) != null) { m.SetTexture("_BumpMap", Tex(normal)); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_UseNormal", 1); }
            m.SetColor("_BaseColor", baseColor); m.SetFloat("_Smoothness", smooth); m.SetFloat("_WindAmp", wind); m.SetFloat("_Trans", trans);
            m.SetFloat("_NormalUp", normalUp); m.SetFloat("_TintAmount", tintAmount); m.SetFloat("_UvScale", uvScale); m.SetFloat("_Cull", twoSided ? 0 : 2);
            if (cutout) { m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_AlphaClip", 1); }
            m.enableInstancing = true;
            return m;
        }

        // ---------------------------------------------------------------- placement
        sealed class Tree { public float x, z, h, scale, rot; public Color color; }

        public void Build(Mulberry32 rng, TownBuilder town, Transform root)
        {
            const float TownH = WorldData.TownHeight; int TownR = WorldData.TownR;
            Func<float, float, bool> inArena = (x, z) => WorldData.ArenaDist(x, z) < 22;
            Func<float, float, float, bool> inArenaPad = (x, z, pad) => WorldData.ArenaDist(x, z) < pad;

            // ---- tree variants (4 broadleaf shapes + 2 conifers), shared leaf/needle materials
            bool hi = quality >= GfxQuality.High;
            var barkMat = PlantMat("bark", "bark", "bark_n", new Color(1.3f, 1.2f, 1.1f), 0.08f, 0.05f, 0, 0, false, false, 0f, 1f);
            var birchMat = PlantMat("birchbark", "birchbark", "birchbark_n", new Color(1f, 1f, 1f), 0.1f, 0.05f, 0, 0, false, false, 0f, 1f);
            var pineBarkMat = PlantMat("pinebark", "bark", "bark_n", new Color(1.15f, 1.05f, 1.0f), 0.05f, 0.04f, 0, 0, false, false, 0f, 1f);
            var leafMat = PlantMat("leaf", "leafcluster", null, new Color(1.9f, 1.9f, 1.9f), 0.18f, 0.2f, 0.9f, 0.2f, true, true, 1f);
            var needleMat = PlantMat("needle", "pinebranch", null, new Color(1.9f, 1.9f, 1.9f), 0.12f, 0.14f, 0.35f, 0.2f, true, true, 1f);
            var decid = new PlantMeshes[4]; int[] dSeed = { 11, 22, 33, 44 };
            for (int i = 0; i < 4; i++) decid[i] = PlantGeo.Broadleaf(dSeed[i], i);
            var pines = new PlantMeshes[2]; int[] pSeed = { 55, 66 };
            for (int i = 0; i < 2; i++) pines[i] = PlantGeo.Conifer(pSeed[i], i);

            string[] leafColors = { "#4f7f31", "#5e8f38", "#6c9a3f", "#7ea548", "#5a8a3a", "#d89aa9", "#c8782f", "#d8a43f" };
            float[] leafWeights = { 18, 18, 16, 12, 14, 4, 3, 3 };
            Func<string> pickLeaf = () => { double x = rng.NextD() * 88; for (int i = 0; i < leafWeights.Length; i++) { x -= leafWeights[i]; if (x <= 0) return leafColors[i]; } return leafColors[0]; };
            string[] pineColors = { "#2f5a33", "#3a6a3d", "#2a4f30" };

            var dList = new List<Tree>[4]; for (int i = 0; i < 4; i++) dList[i] = new List<Tree>();
            var pList = new List<Tree>[2]; for (int i = 0; i < 2; i++) pList[i] = new List<Tree>();
            var treeCircles = new List<CircleCol>();
            int tries = 0, count = 0;
            while (count < 1100 && tries < 20000)
            {
                tries++;
                float x = (float)((rng.NextD() - 0.5) * 780), z = (float)((rng.NextD() - 0.5) * 780);
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < TownR + 45 || inArena(x, z)) continue;
                float h = WorldData.HeightAt(x, z);
                if (h < 2.2f || h > 60) continue;
                if (Hv.Fbm(x * 0.012 + 7, z * 0.012 + 3, 2) < 0.45 && rng.NextD() < 0.75) continue;
                float scale = (float)(0.8 + rng.NextD() * 0.8);
                bool pine = h > 30 || rng.NextD() < 0.28;
                var list = pine ? pList[(int)Math.Floor(rng.NextD() * 2)] : dList[(int)Math.Floor(rng.NextD() * 4)];
                float rot = (float)(rng.NextD() * Math.PI * 2);
                string colHex = pine ? pineColors[(int)Math.Floor(rng.NextD() * 3)] : pickLeaf();
                list.Add(new Tree { x = x, z = z, h = h, scale = scale, rot = rot, color = M.Hex(colHex) });
                treeCircles.Add(new CircleCol { x = x, z = z, r = 0.45f * scale, h = 3.5f });
                count++;
            }
            foreach (var sp in town.townTreeSpots)
            {
                var list = dList[(int)Math.Floor(rng.NextD() * 4)];
                float sc = (float)(0.85 + rng.NextD() * 0.3), rot = (float)(rng.NextD() * 6.28);
                list.Add(new Tree { x = sp.x, z = sp.y, h = TownH, scale = sc, rot = rot, color = M.Hex(pickLeaf()) });
                treeCircles.Add(new CircleCol { x = sp.x, z = sp.y, r = 0.4f, h = 3.5f });
            }
            town.circles.AddRange(treeCircles);

            Action<PlantMeshes, List<Tree>, Material, Material> plant = (v, list, trunkMat, crownMat) =>
            {
                if (list.Count == 0) return;
                var ms = new List<Matrix4x4>(); var cs = new List<Vector4>(); var bs = new List<Vector4>();
                foreach (var t in list)
                {
                    Matrix4x4 mtx = Matrix4x4.TRS(new Vector3(t.x, t.h - 0.15f, -t.z), Quaternion.Euler(0, -t.rot * Mathf.Rad2Deg, 0), Vector3.one * t.scale);
                    Color col = OffsetL(t.color, (float)((rng.NextD() - 0.5) * 0.06));
                    ms.Add(mtx); cs.Add(V4(col));
                    bs.Add(new Vector4(t.x, t.h + v.height * 0.5f * t.scale, -t.z, v.height * 0.65f * t.scale));
                }
                var parts = new[]
                {
                    new InstPart { mesh = v.trunk, mat = trunkMat, castShadow = true, lod = 0 },
                    new InstPart { mesh = v.leaves, mat = crownMat, castShadow = true, lod = 0 },
                    new InstPart { mesh = v.trunkFar, mat = trunkMat, castShadow = false, lod = 1 },
                    new InstPart { mesh = v.leavesFar, mat = crownMat, castShadow = false, lod = 1 },
                };
                fields.Add(lowEnd ? new InstancedField(parts, ms, cs, bs, 45, 320, 0) : new InstancedField(parts, ms, cs, bs, 85, float.PositiveInfinity, 75));
            };
            for (int i = 0; i < 4; i++) plant(decid[i], dList[i], i == 3 ? birchMat : barkMat, leafMat);
            for (int i = 0; i < 2; i++) plant(pines[i], pList[i], pineBarkMat, needleMat);

            // ---- bushes (gardens + forest edges)
            for (int i = 0; i < 500; i++)
            {
                float x = (float)((rng.NextD() - 0.5) * 760), z = (float)((rng.NextD() - 0.5) * 760);
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < TownR + 45 || inArena(x, z)) continue;
                float h = WorldData.HeightAt(x, z);
                if (h > 2.4f && h < 40 && Hv.Fbm(x * 0.012 + 7, z * 0.012 + 3, 2) > 0.42) bushSpots.Add(new Vector2(x, z));
            }
            string[] bushColors = { "#4d7a32", "#5b8a3a", "#6a9446", "#577f3c" };
            var bm = new List<Matrix4x4>(); var bc = new List<Vector4>(); var bb = new List<Vector4>();
            foreach (var s in bushSpots)
            {
                float sc = (float)(0.6 + rng.NextD() * 0.8);
                float h = Mathf.Max(WorldData.HeightAt(s.x, s.y), Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y)) < TownR + 6 ? WorldData.TownHeight : -99);
                float rot = (float)(rng.NextD() * 6);
                Color col = M.Hex(bushColors[(int)Math.Floor(rng.NextD() * 4)]);
                bm.Add(Matrix4x4.TRS(new Vector3(s.x, h + 0.02f, -s.y), Quaternion.Euler(0, -rot * Mathf.Rad2Deg, 0), Vector3.one * sc));
                bc.Add(V4(col)); bb.Add(new Vector4(s.x, h + sc * 0.45f, -s.y, sc * 1.3f));
            }
            var bushMat = PlantMat("bushleaf", "leafcluster", null, new Color(1.8f, 1.8f, 1.8f), 0.15f, 0.12f, 0.8f, 0.3f, true, true, 1f);
            var twigMat = PlantMat("bushtwig", "bark", null, new Color(0.7f, 0.55f, 0.4f), 0.05f, 0.05f, 0, 0, false, false, 0f);
            if (bm.Count > 0)
            {
                var bushNear = PlantGeo.Bush(7, 64, 0.62f); var bushFar = PlantGeo.Bush(8, 20, 1.0f);
                fields.Add(new InstancedField(new[] {
                    new InstPart { mesh = bushNear, mat = bushMat, castShadow = true, lod = 0 },
                    new InstPart { mesh = bushFar, mat = bushMat, castShadow = false, lod = 1 } }, bm, bc, bb, 70, lowEnd ? 140 : 260, lowEnd ? 0 : 60));
            }

            // ---- wildflowers (too small to see beyond ~160 m)
            string[] fColors = { "#f2f0e6", "#f5d24b", "#d9739a", "#9a86d9", "#f29a5c" };
            var fm = new List<Matrix4x4>(); var fc = new List<Vector4>(); var fb = new List<Vector4>();
            tries = 0;
            while (fm.Count < 5000 && tries < 40000)
            {
                tries++;
                float x = (float)((rng.NextD() - 0.5) * 760), z = (float)((rng.NextD() - 0.5) * 760);
                float h = WorldData.HeightAt(x, z);
                if (h < 2.4f || h > 36 || Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < TownR + 60 || inArena(x, z)) continue;
                if (Hv.Fbm(x * 0.02 - 30, z * 0.02 + 11, 2) < 0.55) continue;
                Color col = M.Hex(fColors[(int)Math.Floor(rng.NextD() * fColors.Length)]);
                // a little extra variety the JS game did not have: random turn and size
                float yaw = (x * 12.9898f + z * 78.233f) % 6.28f; float sc = 0.8f + Mathf.Abs(Mathf.Sin(x * 3.7f + z * 1.9f)) * 0.6f;
                fm.Add(Matrix4x4.TRS(new Vector3(x, h - 0.02f, -z), Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0), Vector3.one * sc));
                fc.Add(V4(col)); fb.Add(new Vector4(x, h + 0.3f, -z, 0.3f));
            }
            if (fm.Count > 0)
            {
                Mesh petals, stem; PlantGeo.Flower(5, out petals, out stem);
                var petalMat = PlantMat("petal", null, null, new Color(1.4f, 1.4f, 1.4f), 0.35f, 0.5f, 0.8f, 0.3f, false, true, 1f);
                var stemMat = PlantMat("stem", null, null, new Color(1f, 1f, 1f), 0.1f, 0.5f, 0.3f, 0.3f, false, true, 0f);
                fields.Add(new InstancedField(new[] { new InstPart { mesh = petals, mat = petalMat, lod = 2 }, new InstPart { mesh = stem, mat = stemMat, lod = 2 } }, fm, fc, fb, 100, lowEnd ? 50 : 160, 0));
            }

            // ---- rocks
            var rm = new List<Matrix4x4>(); var rc = new List<Vector4>(); var rb = new List<Vector4>();
            var S = Matrix4x4.Scale(new Vector3(1, 1, -1));
            tries = 0;
            while (rm.Count < 260 && tries < 6000)
            {
                tries++;
                float x = (float)((rng.NextD() - 0.5) * 760), z = (float)((rng.NextD() - 0.5) * 760);
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < TownR + 45 || inArena(x, z)) continue;
                float h = WorldData.HeightAt(x, z);
                if (h < 0.5f) continue;
                float sc = (float)(0.5 + rng.NextD() * 2.4);
                float ex = (float)rng.NextD(), ey = (float)(rng.NextD() * 6), ez = (float)rng.NextD();
                Matrix4x4 R = S * (M.RotX(ex) * M.RotY(ey) * M.RotZ(ez)) * S;
                Matrix4x4 mtx = Matrix4x4.Translate(new Vector3(x, h + sc * 0.05f, -z)) * R * Matrix4x4.Scale(new Vector3(sc, sc * 0.65f, sc));
                Color col = SetHSL(0.08f, 0.05f, (float)(0.62 + rng.NextD() * 0.2));
                rm.Add(mtx); rc.Add(V4(col)); rb.Add(new Vector4(x, h, -z, sc * 1.2f));
                if (sc > 1.2f) town.circles.Add(new CircleCol { x = x, z = z, r = sc * 0.8f, h = sc * 0.9f });
            }
            if (rm.Count > 0)
            {
                var rockMat = PlantMat("rock", "rock", "rock_n", new Color(1.5f, 1.5f, 1.5f), 0.22f, 0f, 0, 0, false, false, 1f);
                var rock = PlantGeo.Rock(3, 1.15f);
                fields.Add(new InstancedField(new[] { new InstPart { mesh = rock, mat = rockMat, castShadow = true, lod = 2 } }, rm, rc, rb, 140, 320, 75));
            }

            // ---- loot spots in the wild (random: x,z each try, up to 50 tries)
            for (int i = 0; i < 40; i++)
            {
                float x = 0, z = 0, h = 0; int k = 0;
                do { x = (float)((rng.NextD() - 0.5) * 700); z = (float)((rng.NextD() - 0.5) * 700); h = WorldData.HeightAt(x, z); k++; }
                while ((h < 3 || h > 45 || Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < TownR + 25 || inArenaPad(x, z, 10)) && k < 50);
                town.pickupSpots.Add(new PickupSpot { p = new Vector3(x, h + 1, z) });
            }
            for (int i = 0; i < 4; i++)
            {
                float a = (float)(i / 4.0 * Math.PI * 2 + 0.4);
                float x = Mathf.Cos(a) * 235, z = Mathf.Sin(a) * 235;
                if (WorldData.HeightAt(x, z) > 2) town.vehicleSpots.Add(new VehicleSpot { x = x, z = z, ry = -a });
            }
        }

        // ---------------------------------------------------------------- the six palms in the gate yards
        public void BuildPalms(TownBuilder town, Transform root)
        {
            var trunkMat = PlantMat("palmbark", "bark", "bark_n", new Color(1.4f, 1.3f, 1.15f), 0.08f, 0.04f, 0, 0, false, false, 0f, 1f);
            var frondMat = PlantMat("palmfrond", "palmfrond", null, new Color(1.05f, 1.0f, 0.95f), 0.2f, 0.3f, 0.7f, 0.15f, true, true, 0f);
            var parent = new GameObject("Palms").transform; parent.SetParent(root, false);
            int seed = 301;
            foreach (var p in town.palms)
            {
                float gy = WorldData.TownHeight;
                Vector3 b = new Vector3(p.x, gy, -p.z), top = new Vector3(p.top.x, p.top.y, -p.top.z);
                var pm = PlantGeo.Palm(b, top, p.ry, p.droop, seed++);
                foreach (var pair in new[] { new KeyValuePair<Mesh, Material>(pm.trunk, trunkMat), new KeyValuePair<Mesh, Material>(pm.leaves, frondMat) })
                {
                    var go = new GameObject("Palm"); go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = pair.Key; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = pair.Value;
                    mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
                }
            }
        }

        public void Render(Camera cam, Vector3 focus) { foreach (var f in fields) f.Render(cam, focus); }
    }
}
