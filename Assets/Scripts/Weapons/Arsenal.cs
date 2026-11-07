using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hearthvale
{
    /// <summary>
    /// The player's guns (port of class Arsenal, weapons.js): 6 guns, ammo + reload, spread, recoil, aim down
    /// sights, scopes with real magnification and breathing sway, weapon swap by number keys / wheel.
    /// Bullets are real Physics raycasts, so they stop at walls, crates, trees and rocks and can hurt anything
    /// that implements IShootable (doors now; animals and ghosts later).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class Arsenal : MonoBehaviour
    {
        public sealed class Slot { public int ammo, reserve, scope; }

        public Camera cam; public PlayerController player;
        public readonly Dictionary<string, Slot> owned = new Dictionary<string, Slot>();
        public string current = "pistol";
        public float cooldown, reloading, kick, aim;
        public bool scoped;
        public readonly HashSet<int> scopes = new HashSet<int>();
        public event Action OnChange; public event Action<string> OnToast; public event Action<int> OnScope; public event Action OnHitMarker;

        Transform holder, flash; readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
        readonly Dictionary<int, GameObject> scopeModels = new Dictionary<int, GameObject>();
        float breath, holdT; int lastScope;

        public WeaponStats Stats { get { return WeaponData.W[current]; } }
        public Slot CurSlot { get { return owned[current]; } }
        public int Scope { get { Slot s; return owned.TryGetValue(current, out s) ? s.scope : 0; } }

        public void Init(Camera camera, PlayerController p)
        {
            cam = camera; player = p;
            owned["pistol"] = new Slot { ammo = 12, reserve = WeaponData.W["pistol"].start };
            holder = new GameObject("WeaponHolder").transform; holder.SetParent(cam.transform, false); holder.localScale = Vector3.one * 0.52f;
            // cosy knitted mitten holding the gun
            var mitten = GameObject.CreatePrimitive(PrimitiveType.Sphere); mitten.name = "mitten"; Destroy(mitten.GetComponent<Collider>());
            mitten.transform.SetParent(holder, false); mitten.transform.localPosition = new Vector3(0.01f, -0.12f, -0.04f); mitten.transform.localScale = new Vector3(0.15f, 0.165f, 0.21f);
            mitten.GetComponent<MeshRenderer>().sharedMaterial = HvMaterials.Make("mitten", null, null, 0.2f, 0, "#3b3a42", false);
            var cuff = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cuff.name = "cuff"; Destroy(cuff.GetComponent<Collider>());
            cuff.transform.SetParent(holder, false); cuff.transform.localPosition = new Vector3(0.01f, -0.15f, -0.16f); cuff.transform.localRotation = Quaternion.Euler(90f - 17f, 0, 0); cuff.transform.localScale = new Vector3(0.15f, 0.06f, 0.15f);
            cuff.GetComponent<MeshRenderer>().sharedMaterial = HvMaterials.Make("cuff", null, null, 0.1f, 0, "#7a5a4a", false);
            foreach (var m in new[] { mitten, cuff }) { var r = m.GetComponent<MeshRenderer>(); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
            foreach (var k in WeaponData.SlotOrder) { var g = GunModels.Build(k); g.transform.SetParent(holder, false); g.SetActive(false); models[k] = g; }
            models["pistol"].SetActive(true);
            var fl = GameObject.CreatePrimitive(PrimitiveType.Sphere); fl.name = "flash"; Destroy(fl.GetComponent<Collider>());
            fl.transform.SetParent(holder, false); fl.transform.localScale = Vector3.one * 0.14f; fl.SetActive(false); flash = fl.transform;
            var fm = HvMaterials.Make("flash", null, null, 0f, 0f, "#ffffff", false); fm.SetColor("_BaseColor", Color.black); fm.SetColor("_EmissionColor", M.Hex("#fff1a8") * 6f);
            var fr = fl.GetComponent<MeshRenderer>(); fr.sharedMaterial = fm; fr.shadowCastingMode = ShadowCastingMode.Off;
            foreach (int n in WeaponData.ScopeLevels) { var s = GunModels.BuildScope(n); s.transform.SetParent(holder, false); s.SetActive(false); scopeModels[n] = s; }
        }

        // ------------------------------------------------------------------ inventory
        /// <summary>A new scope: put it on the gun in your hands if it fits there, otherwise on a sniper.</summary>
        public KeyValuePair<bool, string> GiveScope(int n)
        {
            bool isNew = scopes.Add(n);
            Func<string, bool> fits = k => owned.ContainsKey(k) && WeaponData.W[k].scopeable;
            string target = null;
            if (fits(current) && owned[current].scope < n) target = current;
            if (target == null) foreach (var k in new[] { "bolt", "sniper", "dmr", "rifle" }) if (fits(k) && owned[k].scope < n) { target = k; break; }
            if (target != null) owned[target].scope = n;
            if (OnChange != null) OnChange();
            return new KeyValuePair<bool, string>(isNew, target);
        }
        /// <summary>T: swap the scope on this gun (none / every scope you have found).</summary>
        public int CycleScope()
        {
            if (!Stats.scopeable) return -1;
            var opts = new List<int> { 0 }; foreach (int n in WeaponData.ScopeLevels) if (scopes.Contains(n)) opts.Add(n);
            if (opts.Count < 2) return -1;
            int i = opts.IndexOf(Scope); CurSlot.scope = opts[(i + 1) % opts.Count];
            if (OnChange != null) OnChange(); return CurSlot.scope;
        }
        public void Give(string kind)
        {
            var st = WeaponData.W[kind];
            if (!owned.ContainsKey(kind))
            {
                owned[kind] = new Slot { ammo = st.mag, reserve = st.start };
                if (st.scopeable && scopes.Count > 0 && kind != "rifle") { int best = 0; foreach (int n in scopes) best = Mathf.Max(best, n); owned[kind].scope = best; }
                Select(kind);
            }
            else owned[kind].reserve += st.mag * 2;
            if (OnChange != null) OnChange();
        }
        public void AddAmmo() { foreach (var kv in owned) kv.Value.reserve += WeaponData.W[kv.Key].mag * 2; if (OnChange != null) OnChange(); }
        public void Select(string kind)
        {
            if (!owned.ContainsKey(kind) || kind == current) return;
            models[current].SetActive(false); current = kind; models[kind].SetActive(true);
            reloading = 0; cooldown = 0.25f; kick = 0.6f;
            if (OnChange != null) OnChange();
        }
        public void Cycle(int dir)
        {
            var list = new List<string>(); foreach (var k in WeaponData.SlotOrder) if (owned.ContainsKey(k)) list.Add(k);
            int i = list.IndexOf(current); Select(list[((i + dir) % list.Count + list.Count) % list.Count]);
        }
        public void Reload()
        {
            var s = CurSlot; var st = Stats;
            if (reloading > 0 || s.ammo >= st.mag || s.reserve <= 0) return;
            reloading = st.reload; Sfx.Reload();
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (player.vehicle != null) { holder.gameObject.SetActive(false); return; }
            if (cam == null || player == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            bool active = player.InputActive && !player.dead;
            holder.gameObject.SetActive(active && !player.ThirdPerson && !scoped);
            if (!active)
            {
                if (aim > 0 || scoped)
                {
                    aim = 0; if (scoped) { scoped = false; lastScope = 0; player.scopedView = false; if (OnScope != null) OnScope(0); }
                    cam.fieldOfView = WeaponData.BaseFov;
                }
                return;
            }
            cooldown -= dt;
            if (reloading > 0)
            {
                reloading -= dt;
                if (reloading <= 0) { var s = CurSlot; int take = Mathf.Min(Stats.mag - s.ammo, s.reserve); s.ammo += take; s.reserve -= take; if (OnChange != null) OnChange(); }
            }
            for (int i = 0; i < WeaponData.SlotOrder.Length; i++) if (HvInput.Hit((i + 1).ToString())) Select(WeaponData.SlotOrder[i]);
            int wheel = HvInput.WheelDir(); if (wheel != 0) Cycle(wheel);
            if (HvInput.Hit("R")) Reload();
            if (HvInput.Hit("T")) { int n = CycleScope(); if (n >= 0 && OnToast != null) OnToast(n > 0 ? "🔭 " + n + "x scope" : "🔭 Iron sights"); }

            var st = Stats;
            bool want = st.auto ? HvInput.Down("LMB") : HvInput.Hit("LMB");
            if (want && cooldown <= 0 && reloading <= 0)
            {
                if (CurSlot.ammo <= 0) { Sfx.Empty(); cooldown = 0.25f; Reload(); }
                else Fire();
            }

            // aim down sights + viewmodel sway (scopes take a moment longer to bring up)
            int sc = Scope;
            aim += ((HvInput.Down("RMB") && reloading <= 0 ? 1f : 0f) - aim) * Mathf.Min(1f, dt * (sc > 0 ? 9f : 12f));
            bool was = scoped; scoped = sc > 0 && aim > 0.82f;
            if (scoped != was || (scoped && sc != lastScope)) { player.scopedView = scoped; if (OnScope != null) OnScope(scoped ? sc : 0); }
            lastScope = sc;
            foreach (int n in WeaponData.ScopeLevels) scopeModels[n].SetActive(n == sc && !scoped);
            if (sc > 0) scopeModels[sc].transform.localPosition = new Vector3(0, 0.105f, current == "rifle" ? 0.12f : 0.2f);
            kick = Mathf.Max(0, kick - dt * 5f);
            float dip = reloading > 0 ? Mathf.Sin(Mathf.Min(1f, 1f - reloading / st.reload) * Mathf.PI) * 0.25f : 0f;
            float sway = Mathf.Sin(player.Bob) * 0.012f * (1 - aim);
            holder.localPosition = new Vector3(0.2f * (1 - aim) + sway,
                -0.19f + 0.09f * aim - dip * 0.6f - kick * 0.03f + Mathf.Abs(Mathf.Cos(player.Bob)) * 0.008f,
                0.42f - kick * 0.05f);
            // JS euler (x up, y left, z roll) -> Unity (mirrored z): x and y flip sign
            holder.localRotation = Quaternion.Euler(-(kick * 0.25f + dip) * Mathf.Rad2Deg, -0.08f * (1 - aim) * Mathf.Rad2Deg, dip * 0.6f * Mathf.Rad2Deg);
            float adsFov = sc > 0 ? WeaponData.FovForZoom(sc) : st.zoom;
            float target = WeaponData.BaseFov + (adsFov - WeaponData.BaseFov) * aim;
            if (Mathf.Abs(cam.fieldOfView - target) > 0.01f) cam.fieldOfView = target;
            foreach (var kv in models) kv.Value.SetActive(kv.Key == current);
            holder.gameObject.SetActive(!scoped && !player.ThirdPerson);
            breath += dt;
            if (!scoped) holdT = Mathf.Max(0, holdT - dt);
            flash.gameObject.SetActive(cooldown > st.rate - 0.04f && !scoped);
        }

        // camera breathing sway while scoped: after the player has placed the camera this frame
        void LateUpdate()
        {
            if (!scoped || cam == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f); int sc = Scope;
            bool hold = HvInput.Down("Shift") && holdT < 4f;
            holdT = hold ? holdT + dt : Mathf.Max(0, holdT - dt * 0.5f);
            float amp = (hold ? 0.12f : 1f) * (0.0008f + sc * 0.00055f) * (player.OnGround ? 1f : 3f);
            float px = Mathf.Sin(breath * 1.3f) * amp, py = Mathf.Sin(breath * 0.7f + 1f) * amp * 1.3f;
            cam.transform.rotation = cam.transform.rotation * Quaternion.Euler(-px * Mathf.Rad2Deg, -py * Mathf.Rad2Deg, 0);
        }

        void Fire()
        {
            var st = Stats;
            CurSlot.ammo--; cooldown = st.rate; kick = 1f;
            float k = st.kick >= 0 ? st.kick : (current == "shotgun" ? 1f : 0.35f);
            player.recoil = Mathf.Min(1.5f, player.recoil + k * (scoped ? 0.6f : 1f));
            Sfx.Shot(st.sound ?? current);
            Vector3 origin = cam.transform.position, fwd = cam.transform.forward;
            // third person: aim from the crosshair but start level with the character; tracers come from the hand
            if (player.ThirdPerson) origin += fwd * player.CamDist;
            Vector3 muzzle = player.ThirdPerson ? player.Muzzle(fwd) : holder.TransformPoint(new Vector3(0, 0, 0.75f));
            flash.localPosition = new Vector3(0, 0.02f, (current == "sniper" || current == "bolt") ? 1.1f : current == "pistol" ? 0.35f : 0.8f);
            float hip = st.scopeable && current != "rifle" ? 0.05f : 0f; // snipers are wildly inaccurate from the hip
            float spread = st.spread * (1 - aim * 0.7f) + hip * (1 - aim) + (player.OnGround ? 0f : 0.03f);
            int mask = ~(1 << PlayerController.PlayerLayer);
            for (int p = 0; p < st.pellets; p++)
            {
                Vector3 dir = fwd;
                dir.x += (UnityEngine.Random.value - 0.5f) * spread * 2; dir.y += (UnityEngine.Random.value - 0.5f) * spread * 2; dir.z += (UnityEngine.Random.value - 0.5f) * spread * 2;
                dir.Normalize();
                RaycastHit hit; Vector3 point = origin + dir * st.range; float dist = st.range; IShootable target = null;
                if (Physics.Raycast(origin, dir, out hit, st.range, mask, QueryTriggerInteraction.Ignore))
                {
                    point = hit.point; dist = hit.distance; target = hit.collider.GetComponentInParent<IShootable>();
                }
                if (Effects.Instance != null) Effects.Instance.Tracer(muzzle, point);
                float falloff = current == "shotgun" ? Mathf.Max(0.3f, 1f - dist / st.range) : 1f;
                if (target != null) { target.OnShot(st.dmg * falloff, point, dir, st.force * falloff); if (OnHitMarker != null) OnHitMarker(); }
                else if (dist < st.range && Effects.Instance != null) Effects.Instance.Burst(point, "#f3e3c3", 3, 2f, 0.25f);
            }
            if (OnChange != null) OnChange();
        }

        public void Toast(string t) { if (OnToast != null) OnToast(t); }
    }
}
