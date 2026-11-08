using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>
    /// Loot (port of class Pickups, weapons.js): guns, scopes, ammo pouches, picnic baskets and ghost traps at the
    /// spots the world generated. What each spot holds comes from the same seeded draw as the JS game
    /// (indoor spots roll the better table), and loot respawns 90 s after it is taken.
    /// </summary>
    public sealed class Pickups : MonoBehaviour
    {
        static readonly string[] OutdoorTypes = { "rifle", "shotgun", "sniper", "ammo", "heal", "ammo", "heal", "rifle", "shotgun", "trap", "trap" };
        static readonly string[] IndoorTypes = { "trap", "trap", "scope2", "scope2", "scope3", "scope3", "scope4", "scope4", "scope6", "dmr", "dmr", "bolt", "sniper", "rifle", "shotgun", "ammo", "ammo", "heal" };

        public sealed class Item { public int i; public string type; public GameObject group; public Transform model; public bool active = true; public float respawn; public string fitted; public Vector3 pos; }
        public readonly List<Item> items = new List<Item>();
        public Arsenal arsenal; public PlayerController player;

        static int ScopeOf(string t) { return t.StartsWith("scope") ? int.Parse(t.Substring(5)) : 0; }

        public void Build(TownBuilder town, Transform parent)
        {
            var rng = new Mulberry32(Hv.WorldSeed + 7);
            Mesh ring = new Geo().Append(Geo.Torus(0.8f, 0.06f, 28, 6), Matrix4x4.identity).ToUnityMesh("pickupRing");
            Material ringBase = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "ring" };
            Material ammoM = HvMaterials.Make("ammoBox", null, null, 0.2f, 0, "#7fc8a9", false), basketM = HvMaterials.Make("basket", null, null, 0.1f, 0, "#d9a066", false);
            Material clothM = HvMaterials.Make("pcloth", null, null, 0.1f, 0, "#ff8fa3", false), handleM = HvMaterials.Make("phandle", null, null, 0.1f, 0, "#a8743f", false);
            Mesh box1 = Geo.Box(0.6f, 0.4f, 0.4f).ToUnityMesh("ammoBoxMesh"), basketMesh = Geo.Box(0.7f, 0.4f, 0.5f).ToUnityMesh("basketMesh"), clothMesh = Geo.Box(0.72f, 0.06f, 0.52f).ToUnityMesh("clothMesh");
            Mesh handleMesh = new Geo().Append(Geo.Tube(new Vector3(-0.25f, 0, 0), new Vector3(0, 0.5f, 0), new Vector3(0.25f, 0, 0), 10, 0.04f, 6), Matrix4x4.identity).ToUnityMesh("handleMesh");
            int idx = 0;
            foreach (var spot in town.pickupSpots)
            {
                var pool = spot.fixedY ? IndoorTypes : OutdoorTypes;
                string type = pool[(int)Math.Floor(rng.NextD() * pool.Length)];
                var g = new GameObject("Pickup_" + idx + "_" + type); g.transform.SetParent(parent, false);
                Vector3 pos = WorldSpace.ToUnity(spot.p); g.transform.position = pos;
                var modelT = new GameObject("model").transform; modelT.SetParent(g.transform, false);
                if (WeaponData.W.ContainsKey(type))
                {
                    var gun = GunModels.Build(type); gun.transform.SetParent(modelT, false); gun.transform.localScale = Vector3.one * 1.6f; gun.transform.localRotation = Quaternion.Euler(0, 90f, 0);
                }
                else if (ScopeOf(type) > 0) { var sc = GunModels.BuildScope(ScopeOf(type)); sc.transform.SetParent(modelT, false); sc.transform.localScale = Vector3.one * 2.4f; }
                else if (type == "trap") Part(modelT, box1, HvMaterials.Make("trapMat", null, null, 0.3f, 0.5f, "#3a3548", false), Vector3.zero, new Vector3(0.9f, 0.5f, 0.9f));
                else if (type == "ammo") Part(modelT, box1, ammoM, Vector3.zero, Vector3.one);
                else
                {
                    Part(modelT, basketMesh, basketM, Vector3.zero, Vector3.one); Part(modelT, clothMesh, clothM, new Vector3(0, 0.22f, 0), Vector3.one);
                    Part(modelT, handleMesh, handleM, new Vector3(0, 0.2f, 0), Vector3.one);
                }
                string rc = type == "heal" ? "#ff9fb5" : type == "ammo" ? "#8fe3c1" : ScopeOf(type) > 0 ? GunModels.ScopeTint[ScopeOf(type)] : "#ffe38a";
                var ringGo = new GameObject("ring"); ringGo.transform.SetParent(g.transform, false); ringGo.transform.localPosition = new Vector3(0, -0.6f, 0);
                ringGo.AddComponent<MeshFilter>().sharedMesh = ring; var rr = ringGo.AddComponent<MeshRenderer>();
                var rm = new Material(ringBase); rm.SetColor("_BaseColor", M.Hex(rc).gamma * 1.4f); rr.sharedMaterial = rm; rr.shadowCastingMode = ShadowCastingMode.Off; rr.receiveShadows = false;
                items.Add(new Item { i = idx, type = type, group = g, model = modelT, pos = pos });
                idx++;
            }
        }

        static void Part(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject("part"); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        void Update()
        {
            if (player == null) return;
            float dt = Time.deltaTime; float t = Time.time; Vector3 pp = player.transform.position + Vector3.up * 0.9f;
            bool canPick = !player.dead;
            foreach (var it in items)
            {
                if (!it.active) { it.respawn -= dt; if (it.respawn <= 0) { it.active = true; it.group.SetActive(true); } continue; }
                float d2 = (it.pos - pp).sqrMagnitude;
                bool vis = d2 < 90f * 90f; if (it.group.activeSelf != vis) it.group.SetActive(vis);
                if (!vis) continue;
                bool near = d2 < 28f * 28f; if (it.model.gameObject.activeSelf != near) it.model.gameObject.SetActive(near); // far away: only the glowing ring (cheap)
                if (!near) continue;
                it.model.Rotate(0, dt * 1.2f * Mathf.Rad2Deg, 0, Space.Self);
                it.model.localPosition = new Vector3(0, Mathf.Sin(t * 2 + it.i) * 0.12f, 0);
                if (canPick && d2 < 2.2f * 2.2f) Pick(it);
            }
        }

        public void Take(int index) { var it = items[index]; if (it == null || !it.active) return; it.active = false; it.group.SetActive(false); it.respawn = 90f; }

        void Pick(Item it)
        {
            Take(it.i);
            string type = it.type;
            if (type == "trap") player.traps += 2;
            else if (ScopeOf(type) > 0) { var r = arsenal.GiveScope(ScopeOf(type)); it.fitted = r.Value; }
            else if (WeaponData.W.ContainsKey(type)) arsenal.Give(type);
            else if (type == "ammo") arsenal.AddAmmo();
            else player.hp = Mathf.Min(100, player.hp + 50);
            if (!(WeaponData.W.ContainsKey(type) && Sfx.Voice("when_get_a_weapon_sound", 1f))) Sfx.Pickup();
            string tail = it.fitted != null ? " → fitted to " + WeaponData.W[it.fitted].name + " (T to swap)" : type.StartsWith("scope") ? " · pick up a rifle or sniper to use it" : "";
            arsenal.Toast("Found: " + Label(type) + tail);
            it.fitted = null;
        }

        public static string Label(string type)
        {
            if (ScopeOf(type) > 0) return ScopeOf(type) + "x Scope";
            if (type == "trap") return "Ghost Traps ×2 (hold G to throw)";
            if (WeaponData.W.ContainsKey(type)) return WeaponData.W[type].name;
            return type == "ammo" ? "Ammo Pouch" : "Picnic Basket (+50 HP)";
        }
    }
}
