using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hearthvale
{
    public enum DoorState { Closed = 0, Open = 1, Broken = 2 }

    public sealed class Door
    {
        public int i; public DoorSpec spec; public DoorState state = DoorState.Closed;
        public Transform pivot, leaf; public BoxCollider col;
        public float anim, hp, brokenT, shake; public bool locked, wasLocked;
        public Vector3 centerU, hingeU;
    }

    /// <summary>
    /// Front doors (port of doors.js): F opens/closes, bullets and blasts smash them.
    /// Smashed doors burst into REAL rigid-body planks (Unity physics) and are replaced after 5 minutes.
    /// </summary>
    /// <summary>Put on a door's collider: bullets (and blasts) damage the door; shooting wood throws splinters.</summary>
    public sealed class DoorHitbox : MonoBehaviour, IShootable
    {
        public DoorSystem system; public int index;
        public void OnShot(float damage, Vector3 hitPoint, Vector3 dir, float force)
        {
            if (Effects.Instance != null) Effects.Instance.Burst(hitPoint, "#a8794f", 5, 3f, 0.4f);
            if (system.Damage(index, damage))
            {   // you break it from the street: the house-door crash; a far-off break is heard as "some guy" smashing in
                var cam = Camera.main; float dd = cam != null ? Vector3.Distance(cam.transform.position, hitPoint) : 0f;
                if (dd < 25f) { if (!Sfx.Voice("when_a_door_breaks_inside_house", 0.45f)) Sfx.Crash(0.7f); }
                else if (dd < 90f) { if (!Sfx.Voice("when_a_guy_break_a_door", 0.45f * (1f - dd / 90f))) Sfx.Crash(0.7f); }
            }
        }
    }

    public sealed class DoorSystem : MonoBehaviour
    {
        const float BrokenFor = 300f;
        public readonly List<Door> list = new List<Door>();
        Material woodM, lockedM, shutterM, metalM, lockM, hardwareM;
        Mesh cube;
        readonly List<GameObject> planks = new List<GameObject>();

        public static Mesh CubeMesh()
        {
            var g = Geo.Box(1, 1, 1); var m = new Mesh();
            var v = new List<Vector3>(); foreach (var p in g.v) v.Add(p);
            m.SetVertices(v); m.SetNormals(g.n); m.SetUVs(0, g.uv); m.SetTriangles(g.t, 0); return m; // symmetric: handedness does not matter
        }

        void Awake()
        {
            woodM = HvMaterials.Make("doorWood", "wood", "wood_n", 0.3f, 0, "#c99a68", false);
            lockedM = HvMaterials.Make("doorLocked", "wood", "wood_n", 0.3f, 0, "#9b6a48", false);
            shutterM = HvMaterials.Make("doorShutter", "siding", "siding_n", 0.45f, 0.45f, "#9aa3ad", false);
            metalM = HvMaterials.Make("doorMetal", null, null, 0.7f, 0.8f, "#c9a648", false);
            lockM = HvMaterials.Make("doorLock", null, null, 0.65f, 0.85f, "#3a3b40", false);
            hardwareM = HvMaterials.Make("doorHardware", null, null, 0.7f, 0.8f, "#ffffff", true);
            cube = CubeMesh();
        }

        struct Pc { public Vector3 pos, size; public Color col; public Pc(Vector3 p, Vector3 sz, Color c) { pos = p; size = sz; col = c; } }

        // merge boxes (positions given in JS-style local space: z mirrored) into one mesh with vertex colours
        Mesh Merge(List<Pc> list)
        {
            var V = new List<Vector3>(); var N = new List<Vector3>(); var UV = new List<Vector2>(); var C = new List<Color>(); var T = new List<int>();
            var src = cube; var sv = src.vertices; var sn = src.normals; var su = src.uv; var st = src.triangles;
            foreach (var pc in list)
            {
                int o = V.Count; Vector3 c0 = new Vector3(pc.pos.x, pc.pos.y, -pc.pos.z);
                for (int i = 0; i < sv.Length; i++) { V.Add(c0 + Vector3.Scale(sv[i], pc.size)); N.Add(sn[i]); UV.Add(su[i]); C.Add(pc.col); }
                foreach (int idx in st) T.Add(idx + o);
            }
            var m = new Mesh { name = "doorMesh" }; m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetColors(C); m.SetTriangles(T, 0); m.RecalculateBounds(); return m;
        }

        void MakePart(Transform parent, string name, Mesh mesh, Material mat, bool shadows)
        {
            if (mesh.vertexCount == 0) { Destroy(mesh); return; }
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        GameObject Piece(Transform parent, Material mat, Vector3 size, Vector3 localJs)
        {
            var go = new GameObject("p");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(localJs.x, localJs.y, -localJs.z);
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            return go;
        }

        public Door Add(DoorSpec spec)
        {
            var d = new Door { i = list.Count, spec = spec, locked = spec.locked, wasLocked = spec.locked, hp = spec.roller ? 220 : 90 };
            d.hingeU = WorldSpace.ToUnity(spec.hinge); d.centerU = WorldSpace.ToUnity(spec.center);
            var pivot = new GameObject("Door" + d.i).transform;
            pivot.SetParent(transform, false);
            pivot.position = d.hingeU; pivot.rotation = Quaternion.Euler(0, -spec.yaw * Mathf.Rad2Deg, 0);
            var leaf = new GameObject("leaf").transform; leaf.SetParent(pivot, false);
            float w = spec.width - 0.04f, h = spec.height - 0.02f;
            // every piece of the door is merged into two meshes (wood/shutter body + metal hardware): 2 draw calls per door instead of ~6
            var body = new List<Pc>(); var hw = new List<Pc>();
            Color gold = M.Hex("#c9a648"), dark = M.Hex("#3a3b40");
            if (spec.roller)
            {
                leaf.localPosition = new Vector3(spec.width / 2, spec.height, 0);
                body.Add(new Pc(new Vector3(0, -h / 2, 0), new Vector3(w, h, 0.06f), Color.white));
                if (spec.locked) hw.Add(new Pc(new Vector3(0, -h + 0.3f, 0.08f), new Vector3(0.16f, 0.2f, 0.1f), dark));
            }
            else
            {
                leaf.localPosition = new Vector3(spec.width / 2, spec.height / 2, 0);
                foreach (float py in new[] { -h * 0.22f, h * 0.24f }) body.Add(new Pc(new Vector3(0, py, 0), new Vector3(w * 0.72f, h * 0.34f, 0.1f), Color.white));
                body.Add(new Pc(Vector3.zero, new Vector3(w, h, 0.07f), Color.white));
                foreach (int sgn in new[] { -1, 1 }) hw.Add(new Pc(new Vector3(w / 2 - 0.15f, -0.1f, sgn * 0.06f), new Vector3(0.12f, 0.04f, 0.04f), gold));
                if (spec.locked)
                {
                    hw.Add(new Pc(new Vector3(0, 0, 0.055f), new Vector3(w + 0.1f, 0.1f, 0.05f), dark));
                    hw.Add(new Pc(new Vector3(w / 2 - 0.1f, -0.14f, 0.09f), new Vector3(0.14f, 0.18f, 0.08f), dark));
                }
            }
            var bodyMat = spec.roller ? shutterM : (spec.locked ? lockedM : woodM);
            MakePart(leaf, "body", Merge(body), bodyMat, true);
            MakePart(leaf, "hardware", Merge(hw), hardwareM, false);
            d.pivot = pivot; d.leaf = leaf;
            // collision box (its own object so it can be switched off when the door opens)
            var b = spec.box;
            var cgo = new GameObject("DoorCol" + d.i); cgo.transform.SetParent(transform, false);
            d.col = cgo.AddComponent<BoxCollider>();
            float y0 = b.bottom ?? (WorldData.TownHeight), y1 = b.top;
            cgo.transform.position = new Vector3((b.minX + b.maxX) / 2, (y0 + y1) / 2, -(b.minZ + b.maxZ) / 2);
            d.col.size = new Vector3(b.maxX - b.minX, y1 - y0, b.maxZ - b.minZ);
            cgo.layer = 0;
            var hb = cgo.AddComponent<DoorHitbox>(); hb.system = this; hb.index = d.i;
            list.Add(d);
            return d;
        }

        public Door Nearest(Vector3 posUnity, float r = 2.2f)
        {
            Door best = null; float bd = r * r;
            foreach (var d in list)
            {
                if (d.state == DoorState.Broken) continue;
                float dx = d.centerU.x - posUnity.x, dz = d.centerU.z - posUnity.z, dy = d.centerU.y - posUnity.y;
                float d2 = dx * dx + dz * dz;
                if (d2 < bd && Mathf.Abs(dy) < 2.5f) { bd = d2; best = d; }
            }
            return best;
        }

        public System.Action<Door> OnBroken;
        public bool Set(int i, DoorState st)
        {
            if (i < 0 || i >= list.Count) return false;
            var d = list[i];
            if (d.state == st) return false;
            if (st == DoorState.Broken) { Smash(d); if (OnBroken != null) OnBroken(d); }
            if (st == DoorState.Open) d.locked = false;
            d.state = st;
            d.col.enabled = st == DoorState.Closed;
            if (st == DoorState.Closed) { d.leaf.gameObject.SetActive(true); d.hp = d.spec.roller ? 220 : 90; }
            return true;
        }

        /// <summary>Bullets/blasts. Returns true when this hit broke it.</summary>
        public bool Damage(int i, float dmg)
        {
            var d = list[i];
            if (d.state != DoorState.Closed) return false;
            d.hp -= dmg; d.shake = 0.15f;
            if (d.hp > 0) return false;
            Set(i, DoorState.Broken); return true;
        }

        public void DamageNear(Vector3 pU, float r, float dmg, Action<int> onBroken)
        {
            foreach (var d in list)
            {
                if (d.state != DoorState.Closed) continue;
                float dist = Vector3.Distance(d.centerU, pU);
                if (dist < r && Damage(d.i, dmg * (1 - dist / r))) { if (onBroken != null) onBroken(d.i); }
            }
        }

        void Smash(Door d)
        {
            d.leaf.gameObject.SetActive(false);
            d.brokenT = BrokenFor;
            var mat = d.spec.roller ? shutterM : d.wasLocked ? lockedM : woodM;
            int n = d.spec.roller ? 8 : 6;
            Vector3 inward = d.pivot.rotation * Vector3.forward; // the room side (Unity z is mirrored, so outward is -z)
            for (int k = 0; k < n; k++)
            {
                var go = new GameObject("plank");
                go.transform.position = d.centerU + new Vector3((UnityEngine.Random.value - 0.5f) * d.spec.width, (UnityEngine.Random.value - 0.3f) * d.spec.height * 0.5f, (UnityEngine.Random.value - 0.5f) * 0.4f);
                go.transform.rotation = UnityEngine.Random.rotation;
                go.transform.localScale = new Vector3(d.spec.roller ? 0.54f : 0.18f, d.spec.height * (0.4f + UnityEngine.Random.value * 0.5f), 0.06f);
                go.AddComponent<MeshFilter>().sharedMesh = cube;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                var bc = go.AddComponent<BoxCollider>();
                var rb = go.AddComponent<Rigidbody>(); rb.mass = 1.5f; rb.linearDamping = 0.1f; rb.angularDamping = 0.5f;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                rb.linearVelocity = inward * (1 + UnityEngine.Random.value * 3) + new Vector3((UnityEngine.Random.value - 0.5f) * 3, 2 + UnityEngine.Random.value * 3, (UnityEngine.Random.value - 0.5f) * 3);
                rb.angularVelocity = new Vector3(UnityEngine.Random.value * 8, UnityEngine.Random.value * 8, UnityEngine.Random.value * 8);
                Destroy(go, 6f);
            }
        }

        public void Tick(float dt, Func<Vector3, float, bool> playersNear)
        {
            foreach (var d in list)
            {
                float target = d.state == DoorState.Open ? 1 : 0;
                d.anim += Mathf.Sign(target - d.anim) * Mathf.Min(Mathf.Abs(target - d.anim), dt * 2.6f);
                if (d.spec.roller) d.leaf.localScale = new Vector3(1, 1 - d.anim * 0.92f, 1);
                else d.pivot.rotation = Quaternion.Euler(0, -(d.spec.yaw + d.anim * 1.75f) * Mathf.Rad2Deg, 0);
                if (d.shake > 0) d.shake -= dt;
                var lp = d.leaf.localPosition; lp.x = d.spec.width / 2 + (d.shake > 0 ? (UnityEngine.Random.value - 0.5f) * 0.04f : 0); d.leaf.localPosition = lp;
                if (d.state == DoorState.Broken)
                {
                    d.brokenT -= dt;
                    if (d.brokenT <= 0 && !playersNear(d.centerU, 2.5f)) { d.locked = d.wasLocked; Set(d.i, DoorState.Closed); }
                }
            }
        }
    }
}
