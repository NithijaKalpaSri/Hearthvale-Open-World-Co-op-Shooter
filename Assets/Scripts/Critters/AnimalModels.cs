using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    public sealed class CritterType
    {
        public string name, sound; public float hp, speed, scale, fartRange, fartCd, gasR, gasDps, gasLife; public int petals;
    }

    public sealed class AnimalRig
    {
        public GameObject root; public Transform body, head, eyes, tail; public bool quad; public Transform[] bones; public Vector3 restPos;
        public readonly List<Transform> legs = new List<Transform>(), arms = new List<Transform>();
        public readonly List<Material> mats = new List<Material>();
        public int type;
    }

    /// <summary>
    /// The four Stinkers (buildAnimal in critters.js): hippo, crocodile, gorilla, capybara - chunky, glossy
    /// "vinyl toy" animals with googly eyes. Same proportions and colours as the JS game.
    /// Authored in JS space (they face -z, the gas comes out of +z) and mirrored here: front = +z, rear = -z.
    /// </summary>
    public static class AnimalModels
    {
        public static readonly CritterType[] Types =
        {
            new CritterType { name = "Hippo",     hp = 170, speed = 2.9f, scale = 1.25f, petals = 5, fartRange = 7f, fartCd = 4.2f, gasR = 5.0f, gasDps = 12, gasLife = 8, sound = "deep" },
            new CritterType { name = "Crocodile", hp = 85,  speed = 4.3f, scale = 1.1f,  petals = 3, fartRange = 6f, fartCd = 3.2f, gasR = 3.6f, gasDps = 10, gasLife = 6, sound = "ripple" },
            new CritterType { name = "Gorilla",   hp = 120, speed = 5.0f, scale = 1.15f, petals = 4, fartRange = 6f, fartCd = 3.6f, gasR = 4.0f, gasDps = 11, gasLife = 7, sound = "wet" },
            new CritterType { name = "Capybara",  hp = 45,  speed = 5.6f, scale = 0.9f,  petals = 2, fartRange = 5f, fartCd = 2.4f, gasR = 2.8f, gasDps = 7,  gasLife = 5, sound = "squeak" },
        };

        static Mesh sphere, capsule, cone;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { sphere = capsule = cone = null; }
        enum K { Sph, Cap, Cone }

        static void Meshes()
        {
            if (sphere != null && capsule != null && cone != null) return;
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere = s.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(s);
            var c = GameObject.CreatePrimitive(PrimitiveType.Capsule); capsule = c.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(c);
            cone = new Geo().Append(Geo.Cylinder(0f, 1f, 1f, 12), Matrix4x4.identity).ToUnityMesh("cone");
        }

        public static AnimalRig Build(int type)
        {
            return Quadruped.Build(type, Types[type]);
        }

        public static AnimalRig BuildCartoon(int type)
        {
            Meshes();
            var rig = new AnimalRig { type = type };
            var g = new GameObject("Stinker_" + Types[type].name); rig.root = g.gameObject;
            var body = new GameObject("body").transform; body.SetParent(g.transform, false); rig.body = body;
            // soft glossy "vinyl toy" look; every animal gets its own materials so it can flash when hit
            Material eyeWhite = Mat(rig, "#ffffff", 0.9f), pupil = Mat(rig, "#141418", 0.9f), tooth = Mat(rig, "#fbf6e6", 0.55f);
            Material Cm(string hex) { return Mat(rig, hex, 0.62f); }

            Transform Add(K kind, Material m, float x, float y, float z, float sx, float sy, float sz, Transform parent, float rotX = 0, float rotZ = 0)
            {
                var o = new GameObject(kind.ToString()); o.transform.SetParent(parent, false);
                o.transform.localPosition = new Vector3(x, y, -z); // JS z is mirrored
                o.transform.localRotation = Quaternion.Euler(-rotX * Mathf.Rad2Deg, 0, rotZ * Mathf.Rad2Deg);
                Mesh mesh; Vector3 sc;
                if (kind == K.Sph) { mesh = sphere; sc = new Vector3(2 * sx, 2 * sy, 2 * sz); }
                else if (kind == K.Cap) { mesh = capsule; sc = new Vector3(2 * sx, 1.5f * sy, 2 * sz); }
                else { mesh = cone; sc = new Vector3(sx, sy, sz); }
                o.transform.localScale = sc;
                o.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = o.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
                return o.transform;
            }
            void Leg(Material m, float x, float len, float r, Material foot = null)
            {
                var p = new GameObject("leg").transform; p.SetParent(body, false); p.localPosition = new Vector3(x, len, 0);
                Add(K.Cap, m, 0, -len / 2, 0, r, len / 2, r, p); Add(K.Sph, foot ?? m, 0, -len + 0.02f, -0.06f, r * 1.25f, r * 0.6f, r * 1.6f, p); rig.legs.Add(p);
            }
            void Arm(Material m, float x, float y, float len, float r, Material hand = null)
            {
                var p = new GameObject("arm").transform; p.SetParent(body, false); p.localPosition = new Vector3(x, y, 0); p.localRotation = Quaternion.Euler(0, 0, Mathf.Sign(x) * 0.25f * Mathf.Rad2Deg);
                Add(K.Cap, m, 0, -len / 2, 0, r, len / 2, r, p); Add(K.Sph, hand ?? m, 0, -len - r * 0.4f, 0, r * 1.25f, r * 1.1f, r * 1.25f, p); rig.arms.Add(p);
            }
            var head = new GameObject("head").transform; head.SetParent(body, false); rig.head = head;
            var eyes = new GameObject("eyes").transform; eyes.SetParent(body, false); rig.eyes = eyes;
            var tail = new GameObject("tail").transform; rig.tail = tail; bool tailSet = false;
            void Googly(float x, float y, float z, float s)
            {
                Add(K.Sph, eyeWhite, x, y, z, s, s, s * 0.9f, eyes);
                Add(K.Sph, pupil, x - Mathf.Sign(x) * s * 0.18f, y - s * 0.12f, z - s * 0.72f, s * 0.45f, s * 0.45f, s * 0.35f, eyes); // dopey, slightly cross-eyed
            }
            void HeadAt(float x, float y, float z) { head.localPosition = new Vector3(x, y, -z); eyes.localPosition = new Vector3(x, y, -z); }

            if (type == 0)
            { // Hippo: wide round slate-blue body, huge muzzle, tiny ears, two stubby teeth
                Material skin = Cm("#7d8db8"), belly = Cm("#c2cbe4"), muzzle = Cm("#9fb0d6"), pink = Cm("#e7a7b4");
                Add(K.Sph, skin, 0, 0.95f, 0, 0.62f, 0.6f, 0.52f, body); Add(K.Sph, belly, 0, 0.88f, -0.2f, 0.46f, 0.44f, 0.36f, body);
                HeadAt(0, 1.6f, -0.12f);
                Add(K.Sph, skin, 0, 0, 0, 0.42f, 0.38f, 0.4f, head); Add(K.Sph, muzzle, 0, -0.1f, -0.34f, 0.4f, 0.27f, 0.3f, head);
                foreach (int s in new[] { -1, 1 })
                {
                    Add(K.Sph, pupil, s * 0.13f, 0.08f, -0.6f, 0.05f, 0.03f, 0.03f, head); Add(K.Sph, skin, s * 0.26f, 0.34f, 0.06f, 0.08f, 0.08f, 0.05f, head);
                    Add(K.Sph, pink, s * 0.26f, 0.34f, 0.03f, 0.05f, 0.05f, 0.03f, head); Add(K.Cone, tooth, s * 0.16f, -0.24f, -0.56f, 0.04f, 0.09f, 0.04f, head, Mathf.PI);
                }
                Googly(-0.17f, 0.3f, -0.24f, 0.12f); Googly(0.17f, 0.3f, -0.24f, 0.12f);
                Leg(skin, -0.3f, 0.42f, 0.15f); Leg(skin, 0.3f, 0.42f, 0.15f); Arm(skin, -0.62f, 1.15f, 0.4f, 0.11f); Arm(skin, 0.62f, 1.15f, 0.4f, 0.11f);
                tail.SetParent(body, false); tail.localPosition = new Vector3(0, 0.8f, -0.5f); tailSet = true; Add(K.Cone, skin, 0, 0, 0.06f, 0.06f, 0.18f, 0.06f, tail, Mathf.PI / 2);
            }
            else if (type == 1)
            { // Crocodile: green, long toothy snout, bumpy back ridge, thick tail
                Material skin = Cm("#5f9e48"), belly = Cm("#e3df9a"), dark = Cm("#447a35");
                Add(K.Sph, skin, 0, 0.95f, 0, 0.48f, 0.62f, 0.44f, body); Add(K.Sph, belly, 0, 0.9f, -0.18f, 0.36f, 0.48f, 0.3f, body);
                HeadAt(0, 1.62f, -0.05f);
                Add(K.Sph, skin, 0, 0, 0, 0.36f, 0.3f, 0.36f, head); Add(K.Sph, skin, 0, -0.08f, -0.5f, 0.26f, 0.15f, 0.42f, head);
                for (int i = 0; i < 6; i++) foreach (int s in new[] { -1, 1 }) Add(K.Cone, tooth, s * 0.2f, -0.16f, -0.25f - i * 0.1f, 0.025f, 0.07f, 0.025f, head, Mathf.PI);
                foreach (int s in new[] { -1, 1 }) Add(K.Sph, dark, s * 0.06f, -0.01f, -0.86f, 0.03f, 0.03f, 0.03f, head);
                foreach (int s in new[] { -1, 1 }) Add(K.Sph, skin, s * 0.15f, 0.22f, -0.12f, 0.13f, 0.12f, 0.13f, eyes);
                Googly(-0.15f, 0.27f, -0.2f, 0.1f); Googly(0.15f, 0.27f, -0.2f, 0.1f);
                for (int i = 0; i < 4; i++) Add(K.Cone, dark, 0, 1.3f - i * 0.2f, 0.38f + i * 0.03f, 0.07f, 0.14f, 0.07f, body, -0.9f);
                Leg(skin, -0.24f, 0.42f, 0.13f); Leg(skin, 0.24f, 0.42f, 0.13f); Arm(skin, -0.5f, 1.2f, 0.36f, 0.09f); Arm(skin, 0.5f, 1.2f, 0.36f, 0.09f);
                tail.SetParent(body, false); tail.localPosition = new Vector3(0, 0.6f, -0.38f); tailSet = true; Add(K.Cone, skin, 0, 0, 0.42f, 0.2f, 0.9f, 0.16f, tail, Mathf.PI / 2 + 0.25f);
            }
            else if (type == 2)
            { // Gorilla: charcoal fur, big chest, huge long arms, grumpy brow
                Material fur = Cm("#38383f"), face = Cm("#8b7d76"), chest = Cm("#5a5560");
                Add(K.Sph, fur, 0, 1.0f, 0, 0.6f, 0.62f, 0.5f, body); Add(K.Sph, chest, 0, 1.05f, -0.24f, 0.44f, 0.4f, 0.3f, body);
                HeadAt(0, 1.68f, -0.1f);
                Add(K.Sph, fur, 0, 0.04f, 0.02f, 0.34f, 0.36f, 0.34f, head); Add(K.Sph, face, 0, -0.04f, -0.2f, 0.27f, 0.27f, 0.2f, head); Add(K.Sph, face, 0, -0.14f, -0.33f, 0.18f, 0.12f, 0.1f, head);
                Add(K.Cap, fur, 0, 0.16f, -0.24f, 0.05f, 0.2f, 0.05f, head, 0, Mathf.PI / 2);
                foreach (int s in new[] { -1, 1 }) { Add(K.Sph, pupil, s * 0.05f, -0.1f, -0.42f, 0.025f, 0.02f, 0.02f, head); Add(K.Sph, face, s * 0.31f, 0.02f, -0.02f, 0.06f, 0.08f, 0.04f, head); }
                Googly(-0.11f, 0.06f, -0.28f, 0.09f); Googly(0.11f, 0.06f, -0.28f, 0.09f);
                Leg(fur, -0.28f, 0.38f, 0.14f, face); Leg(fur, 0.28f, 0.38f, 0.14f, face); Arm(fur, -0.66f, 1.35f, 0.75f, 0.14f, face); Arm(fur, 0.66f, 1.35f, 0.75f, 0.14f, face);
            }
            else
            { // Capybara: ginger barrel with a blunt boxy snout, tiny ears, totally unbothered
                Material fur = Cm("#c98a52"), dark = Cm("#9a6539"), nose = Cm("#4a3328");
                Add(K.Sph, fur, 0, 0.85f, 0, 0.5f, 0.52f, 0.46f, body);
                HeadAt(0, 1.42f, -0.12f);
                Add(K.Cap, fur, 0, 0, -0.1f, 0.27f, 0.22f, 0.3f, head, Mathf.PI / 2); Add(K.Sph, dark, 0, -0.04f, -0.42f, 0.22f, 0.18f, 0.14f, head); Add(K.Sph, nose, 0, 0.04f, -0.54f, 0.09f, 0.05f, 0.04f, head);
                foreach (int s in new[] { -1, 1 }) Add(K.Sph, dark, s * 0.2f, 0.22f, 0.08f, 0.06f, 0.07f, 0.04f, head);
                Googly(-0.17f, 0.12f, -0.15f, 0.08f); Googly(0.17f, 0.12f, -0.15f, 0.08f);
                Leg(fur, -0.24f, 0.36f, 0.12f, dark); Leg(fur, 0.24f, 0.36f, 0.12f, dark); Arm(fur, -0.5f, 1.05f, 0.32f, 0.09f, dark); Arm(fur, 0.5f, 1.05f, 0.32f, 0.09f, dark);
            }
            if (!tailSet) { tail.SetParent(body, false); }
            g.transform.localScale = Vector3.one * Types[type].scale;
            return rig;
        }

        static Material Mat(AnimalRig rig, string hex, float smooth)
        {
            var m = HvMaterials.Make("animal", null, null, smooth, 0f, hex, false);
            rig.mats.Add(m); return m;
        }

        public static void SetFlash(AnimalRig rig, float k)
        {
            Color e = Color.white * k; foreach (var m in rig.mats) m.SetColor("_EmissionColor", e);
        }
    }
}
