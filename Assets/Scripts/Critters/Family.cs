using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hearthvale
{
    /// <summary>
    /// Break a house's front door and you find the family at the table (breakfast / lunch / dinner / late supper by the clock).
    /// They freeze mid-bite; the father roars (sound A), grabs a huge stick and chases you; the mother, sons and sisters panic,
    /// stand up, run to the far corners of the room and cower. Everybody can be shot (headshots hurt more) and stays dead until the
    /// door is replaced. The six people are built once at start-up and moved into whichever house's door was smashed.
    /// </summary>
    public sealed class Family : MonoBehaviour
    {
        public HvWorld world; public PlayerController player;
        static readonly float[] Scale = { 1.07f, 0.97f, 0.82f, 0.88f, 0.9f, 0.78f };
        static readonly float[] MaxHp = { 160f, 70f, 55f, 60f, 50f, 45f };
        static readonly string[] Shirt = { "#6b4a2a", "#b04a68", "#3f7fbf", "#4a9f5f", "#d9a03a", "#a06ad0" };
        static readonly Vector2[] Seat = { new Vector2(3.55f, 2.2f), new Vector2(1.65f, 2.2f), new Vector2(2.15f, 1.5f), new Vector2(3.05f, 1.5f), new Vector2(2.15f, 2.9f), new Vector2(3.05f, 2.9f) };
        static readonly Vector2[] Corner = { Vector2.zero, new Vector2(4.2f, 0.2f), new Vector2(-4.0f, 0.3f), new Vector2(4.2f, 1.1f), new Vector2(-3.0f, 0.3f), new Vector2(-4.0f, 1.2f) };   // where each cowers (house local x/z)
        static readonly Vector2 Table = new Vector2(2.6f, 2.2f);   // the wooden table in every house's front room (TownBuilder, local x/z)

        enum S { Eat, Stand, Flee, Cower, Chase, Back, Dead }
        sealed class Mem
        {
            public GameObject holder; public Avatar av; public CharacterController cc; public Vector3 seatPos, corner; public Quaternion seatRot;
            public GameObject chair, plate, cup, blood; public S st; public float hp, stagger, standT, deadT, phase, cowerT; public bool seated = true; public Vector3 vel, fallDir;
        }
        readonly List<Mem> m = new List<Mem>(); GameObject stickPivot; Material wood, plateM, foodM, clothM, potM, breadM, bloodM, cupM;
        GameObject tableDress; bool ready, active, alarmed; Door door; float t, cool, lostT, swingT, hrTime;
        AudioSource voice; Light candle; Vector3 tableC;

        void Start() { StartCoroutine(BuildAll()); }

        IEnumerator BuildAll()
        {
            yield return new WaitForSeconds(4f);   // after the town and the characters
            wood = HvMaterials.Make("famWood", null, null, 0.3f, 0, "#7a5a42", false);
            plateM = HvMaterials.Make("famPlate", null, null, 0.7f, 0, "#f2efe8", false);
            foodM = HvMaterials.Make("famFood", null, null, 0.3f, 0, "#c9733a", false);
            clothM = HvMaterials.Make("famCloth", null, null, 0.2f, 0, "#c8423c", false);
            potM = HvMaterials.Make("famPot", null, null, 0.6f, 0.7f, "#8d939b", false);
            breadM = HvMaterials.Make("famBread", null, null, 0.2f, 0, "#d9a05a", false);
            bloodM = HvMaterials.Make("famBlood", null, null, 0.8f, 0, "#5a0a0c", false);
            cupM = HvMaterials.Make("famCup", null, null, 0.7f, 0, "#4f86c6", false);
            for (int i = 0; i < Seat.Length; i++)
            {
                var mem = new Mem(); mem.av = Avatar.Build(Shirt[i], 700 + i);
                mem.holder = new GameObject("FamilyMember" + i); mem.holder.transform.localScale = Vector3.one * Scale[i]; mem.holder.layer = CritterSystem.CritterLayer;
                mem.av.root.transform.SetParent(mem.holder.transform, false);
                mem.cc = mem.holder.AddComponent<CharacterController>(); mem.cc.stepOffset = 0.25f; mem.cc.enabled = false;
                var fh = mem.holder.AddComponent<FamilyHit>(); fh.owner = this; fh.index = i;
                mem.chair = Chair(); mem.plate = Plate(); mem.cup = Cup(); mem.phase = i * 1.7f;
                mem.blood = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(mem.blood.GetComponent<Collider>()); mem.blood.GetComponent<Renderer>().sharedMaterial = bloodM; mem.blood.SetActive(false);
                mem.holder.SetActive(false); mem.chair.SetActive(false); mem.plate.SetActive(false); mem.cup.SetActive(false); m.Add(mem);
                yield return null;
            }
            var f = m[0];
            voice = f.holder.AddComponent<AudioSource>(); voice.spatialBlend = 1f; voice.rolloffMode = AudioRolloffMode.Linear; voice.minDistance = 6f; voice.maxDistance = 70f; voice.playOnAwake = false; voice.dopplerLevel = 0;
            stickPivot = new GameObject("StickPivot"); stickPivot.transform.SetParent(f.av.gunMount, false);
            var stick = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(stick.GetComponent<Collider>()); stick.transform.SetParent(stickPivot.transform, false);
            stick.transform.localPosition = new Vector3(0, 0, 0.55f); stick.transform.localScale = new Vector3(0.1f, 0.1f, 1.5f); stick.GetComponent<Renderer>().sharedMaterial = wood;
            var knob = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(knob.GetComponent<Collider>()); knob.transform.SetParent(stickPivot.transform, false);
            knob.transform.localPosition = new Vector3(0, 0, 1.2f); knob.transform.localScale = new Vector3(0.22f, 0.22f, 0.5f); knob.GetComponent<Renderer>().sharedMaterial = wood;
            stickPivot.SetActive(false);
            tableDress = MakeTableDress(); tableDress.SetActive(false);
            var lg = new GameObject("Candle"); lg.transform.SetParent(transform, false); candle = lg.AddComponent<Light>(); candle.type = LightType.Point; candle.range = 7; candle.color = new Color(1f, 0.75f, 0.45f); candle.intensity = 0; candle.shadows = LightShadows.None;
            world.doors.OnBroken += OnBroken; ready = true;
        }

        // ------------------------------------------------------------------ props
        GameObject Prim(Transform parent, PrimitiveType type, Vector3 p, Vector3 s, Material mat)
        {
            var c = GameObject.CreatePrimitive(type); Destroy(c.GetComponent<Collider>()); c.transform.SetParent(parent, false); c.transform.localPosition = p; c.transform.localScale = s; c.GetComponent<Renderer>().sharedMaterial = mat; return c;
        }
        GameObject Chair()
        {
            var g = new GameObject("Chair");
            Prim(g.transform, PrimitiveType.Cube, new Vector3(0, 0.45f, 0), new Vector3(0.42f, 0.04f, 0.42f), wood); Prim(g.transform, PrimitiveType.Cube, new Vector3(0, 0.72f, -0.2f), new Vector3(0.42f, 0.5f, 0.04f), wood);
            foreach (float sx in new[] { -0.18f, 0.18f }) foreach (float sz in new[] { -0.18f, 0.18f }) Prim(g.transform, PrimitiveType.Cube, new Vector3(sx, 0.22f, sz), new Vector3(0.04f, 0.44f, 0.04f), wood);
            return g;
        }
        GameObject Plate()
        {
            var g = new GameObject("Plate");
            Prim(g.transform, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.26f, 0.012f, 0.26f), plateM);
            Prim(g.transform, PrimitiveType.Sphere, new Vector3(0, 0.04f, 0), new Vector3(0.15f, 0.07f, 0.15f), foodM);
            Prim(g.transform, PrimitiveType.Sphere, new Vector3(0.06f, 0.035f, 0.04f), new Vector3(0.08f, 0.05f, 0.08f), breadM);
            return g;
        }
        GameObject Cup() { var g = new GameObject("Cup"); Prim(g.transform, PrimitiveType.Cylinder, new Vector3(0, 0.05f, 0), new Vector3(0.07f, 0.05f, 0.07f), cupM); return g; }
        GameObject MakeTableDress()
        {   // tablecloth, a steaming pot and bread in the middle (placed on the existing table of the house)
            var g = new GameObject("TableDress");
            Prim(g.transform, PrimitiveType.Cube, new Vector3(0, 0f, 0), new Vector3(1.55f, 0.012f, 1.05f), clothM);
            Prim(g.transform, PrimitiveType.Cylinder, new Vector3(0, 0.1f, 0), new Vector3(0.28f, 0.1f, 0.28f), potM); Prim(g.transform, PrimitiveType.Cylinder, new Vector3(0, 0.215f, 0), new Vector3(0.3f, 0.015f, 0.3f), potM);
            Prim(g.transform, PrimitiveType.Sphere, new Vector3(-0.35f, 0.05f, 0.1f), new Vector3(0.3f, 0.1f, 0.14f), breadM);
            return g;
        }

        // ------------------------------------------------------------------ trigger
        void OnBroken(Door d)
        {
            if (!ready || active || !d.spec.house || player == null || player.dead) return;
            if (Vector3.Distance(player.transform.position, d.centerU) > 40f) return;
            Activate(d);
        }

        string Meal(float h) { return h >= 5 && h < 11 ? "breakfast" : h >= 11 && h < 16.5f ? "lunch" : h >= 17 && h < 22 ? "dinner" : "a late supper"; }

        void SetBody(Mem x, bool seated)
        {   // collider + avatar height for sitting / standing
            x.seated = seated; bool en = x.cc.enabled; x.cc.enabled = false;
            if (seated) { x.cc.radius = 0.2f; x.cc.height = 1.35f; x.cc.center = new Vector3(0, 0.62f, 0); } else { x.cc.radius = 0.3f; x.cc.height = 1.8f; x.cc.center = new Vector3(0, 0.95f, 0); }
            x.cc.enabled = en;
        }

        void Activate(Door d)
        {
            door = d; active = true; alarmed = false; t = 0; cool = 0; lostT = 0; swingT = 0; var tw = d.spec.toWorld; float floor = d.spec.hinge.y;
            tableC = WorldSpace.ToUnity(tw(Table.x, 0, Table.y)); tableC.y = floor;
            for (int i = 0; i < m.Count; i++)
            {
                var x = m[i]; Vector3 sp = WorldSpace.ToUnity(tw(Seat[i].x, 0, Seat[i].y)); sp.y = floor;
                Vector3 toT = tableC - sp; toT.y = 0; Quaternion rot = Quaternion.LookRotation(toT.normalized, Vector3.up);
                x.seatPos = sp; x.seatRot = rot; x.hp = MaxHp[i]; x.st = i == 0 ? S.Eat : S.Eat; x.stagger = 0; x.standT = 0; x.deadT = 0; x.cowerT = 0; x.vel = Vector3.zero;
                if (i > 0) { Vector3 c = WorldSpace.ToUnity(tw(Corner[i].x, 0, Corner[i].y)); c.y = floor; x.corner = c; }
                x.cc.enabled = false; x.holder.transform.SetPositionAndRotation(sp, rot); x.av.root.transform.localPosition = new Vector3(0, -0.45f, 0); SetBody(x, true);
                x.holder.SetActive(true); x.cc.enabled = true;
                x.chair.transform.SetPositionAndRotation(sp - toT.normalized * 0.12f, rot); x.chair.SetActive(true);
                Vector3 pp = tableC + (sp - tableC) * 0.5f; x.plate.transform.position = new Vector3(pp.x, floor + 0.97f, pp.z); x.plate.SetActive(true);
                Vector3 cp = tableC + (sp - tableC) * 0.62f + rot * new Vector3(0.22f, 0, 0.1f); x.cup.transform.position = new Vector3(cp.x, floor + 0.97f, cp.z); x.cup.SetActive(true);
                x.blood.SetActive(false); x.av.SetGun(null); x.av.armUp = 0f; x.av.Pose("sit", 0, 0, 0, false);
            }
            tableDress.transform.position = new Vector3(tableC.x, floor + 0.96f, tableC.z); tableDress.transform.rotation = m[0].seatRot * Quaternion.Euler(0, 90f, 0); tableDress.SetActive(true);
            stickPivot.SetActive(false);
            candle.transform.position = tableC + Vector3.up * 1.4f; hrTime = world.Hour; candle.intensity = (hrTime >= 17 || hrTime < 6) ? 1.5f : 0.5f;
            player.Toast("🍽️ A family is having " + Meal(hrTime) + "…");
        }

        void Deactivate()
        {
            active = false; foreach (var x in m) { x.cc.enabled = false; x.holder.SetActive(false); x.chair.SetActive(false); x.plate.SetActive(false); x.cup.SetActive(false); x.blood.SetActive(false); }
            stickPivot.SetActive(false); tableDress.SetActive(false); candle.intensity = 0f;
        }

        // ------------------------------------------------------------------ getting shot
        public void Shot(int i, float dmg, Vector3 point, Vector3 dir)
        {
            if (!active) return; var x = m[i]; if (x.st == S.Dead) return;
            float top = x.holder.transform.position.y + (x.seated ? 1.15f : 1.55f) * Scale[i]; bool head = point.y > top - 0.02f; if (head) dmg *= 2.5f;
            x.hp -= dmg; x.stagger = 0.3f; Sfx.Hit(); if (Effects.Instance != null) Effects.Instance.Burst(point, "#a01414", head ? 12 : 7, 3.5f, 0.55f);
            Alarm();
            if (x.hp <= 0) Kill(i, dir);
        }

        void Kill(int i, Vector3 dir)
        {
            var x = m[i]; x.st = S.Dead; x.deadT = 0; dir.y = 0; x.fallDir = dir.sqrMagnitude > 0.01f ? dir.normalized : -x.holder.transform.forward;
            x.cc.enabled = false; if (i == 0) stickPivot.SetActive(false);
            x.blood.transform.position = x.holder.transform.position + x.fallDir * 0.5f + Vector3.up * 0.015f; x.blood.transform.localScale = new Vector3(0.05f, 0.004f, 0.05f); x.blood.SetActive(true);
            if (voice != null && i == 0) voice.Stop();
            int alive = 0; foreach (var y in m) if (y.st != S.Dead) alive++;
            player.Toast(alive == 0 ? "💀 The whole family is dead…" : "💀 Family member down");
        }

        /// <summary>Something violent happened: everyone but the father stands up and panics.</summary>
        void Alarm()
        {
            if (alarmed) return; alarmed = true;
            for (int i = 1; i < m.Count; i++) if (m[i].st == S.Eat) { m[i].st = S.Stand; m[i].standT = 0.15f * i; }
            var f = m[0]; if (f.st == S.Eat) { f.st = S.Chase; f.standT = 0; f.seated = true; }
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (!ready || !active) return; float dt = Mathf.Min(Time.deltaTime, 0.05f); t += dt;
            if (door.state != DoorState.Broken) { Deactivate(); return; }   // the door was replaced: the family is back to normal (and out of sight)
            Vector3 pp = player.transform.position;
            for (int i = 0; i < m.Count; i++) { var x = m[i]; x.stagger -= dt; if (i == 0) Father(x, dt, pp); else Relative(i, x, dt, pp); Flinch(x); }
            if (!alarmed && t > 1.0f)   // the door crash has just played: now the father roars (sound A)
            {
                var a = Sfx.File("ghost_trap_A"); if (a != null && m[0].st != S.Dead) { voice.clip = a; voice.volume = Mathf.Clamp01(0.5f + 0.5f * Sfx.Master * Sfx.Effects); voice.Play(); }
                Alarm();
            }
        }

        void Flinch(Mem x)
        {   // when hit the body jolts
            if (x.stagger <= 0 || x.st == S.Dead) return; float k = x.stagger / 0.3f;
            x.av.bones[Avatar.BChest].localRotation *= Quaternion.Euler(-18f * k, 0, Mathf.Sin(Time.time * 40f) * 6f * k); x.av.bones[Avatar.BHead].localRotation *= Quaternion.Euler(-14f * k, 0, 0);
        }

        // ---- eating / talking at the table
        void SeatedAnim(Mem x, int i, float dt, Vector3 pp, bool eating)
        {
            x.av.Pose("sit", dt, 0, 0, false); var b = x.av.bones; float tt = Time.time + x.phase;
            float yaw = LookYaw(x.holder.transform, pp);
            if (!eating) { b[Avatar.BHead].localRotation = Quaternion.Euler(0, yaw, 0); return; }
            float cyc = (tt * 0.42f) % 1f;                                   // one forkful every ~2.4 s
            float lift = Mathf.SmoothStep(0, 1, Mathf.Clamp01(cyc * 3f)) * (1f - Mathf.SmoothStep(0, 1, Mathf.Clamp01((cyc - 0.4f) * 3f)));
            float sc = Mathf.Sin(tt * 0.9f) > 0.2f ? 1f : 0f;             // sometimes a scoop, sometimes a chat
            b[Avatar.BoneIndex(1, 0)].localRotation = Quaternion.Euler(-50f - 30f * lift * sc, 0, 0); b[Avatar.BoneIndex(1, 1)].localRotation = Quaternion.Euler(-35f - 85f * lift * sc, 0, 0);
            b[Avatar.BoneIndex(-1, 0)].localRotation = Quaternion.Euler(-48f, 0, 0); b[Avatar.BoneIndex(-1, 1)].localRotation = Quaternion.Euler(-38f - 8f * Mathf.Sin(tt * 0.5f), 0, 0);
            float chat = Mathf.Sin(tt * 0.33f) * 28f;
            b[Avatar.BHead].localRotation = Quaternion.Euler(10f * (1f - lift * sc) - 4f * lift * sc + Mathf.Sin(tt * 3.1f) * 2f * (1 - sc), chat, Mathf.Sin(tt * 0.7f) * 3f);
            b[Avatar.BSpine].localRotation = Quaternion.Euler(8f + 6f * (1f - lift * sc), 0, 0);
        }

        void Relative(int i, Mem x, float dt, Vector3 pp)
        {
            var tr = x.holder.transform; var rt = x.av.root.transform; var b = x.av.bones;
            switch (x.st)
            {
                case S.Eat: SeatedAnim(x, i, dt, pp, t < 0.35f); break;                       // they freeze, staring at the broken door
                case S.Stand:
                    x.standT -= dt; if (x.standT > 0) { SeatedAnim(x, i, dt, pp, false); break; }
                    { float k = Mathf.Clamp01(-x.standT / 0.5f); rt.localPosition = new Vector3(0, Mathf.Lerp(-0.45f, 0, k), 0); x.av.Pose("walk", dt, 0.1f, 0, false); PanicArms(x, 0.5f); FaceBody(tr, pp, dt, 10f);
                      if (k >= 1f) { SetBody(x, false); x.st = S.Flee; } }
                    break;
                case S.Flee:
                    {
                        Vector3 to = x.corner - tr.position; to.y = 0; float d = to.magnitude; FaceBody(tr, x.corner, dt, 9f);
                        Move(x, d > 0.3f ? to.normalized * 3.6f : Vector3.zero, dt); x.av.Pose("walk", dt, d > 0.3f ? 0.7f : 0f, 0, false); PanicArms(x, 1f);
                        if (d < 0.35f) { x.st = S.Cower; x.cowerT = 0; }
                    }
                    break;
                case S.Cower:
                    {
                        x.cowerT += dt; FaceBody(tr, pp, dt, 4f); Move(x, Vector3.zero, dt); x.av.Pose("sit", dt, 0, 0, false);
                        rt.localPosition = Vector3.Lerp(rt.localPosition, new Vector3(0, -0.5f, 0), dt * 6f);
                        b[Avatar.BoneIndex(1, 0)].localRotation = Quaternion.Euler(-150f, 0, -15f); b[Avatar.BoneIndex(1, 1)].localRotation = Quaternion.Euler(-110f, 0, 0);
                        b[Avatar.BoneIndex(-1, 0)].localRotation = Quaternion.Euler(-150f, 0, 15f); b[Avatar.BoneIndex(-1, 1)].localRotation = Quaternion.Euler(-110f, 0, 0);
                        b[Avatar.BSpine].localRotation = Quaternion.Euler(32f + Mathf.Sin(Time.time * 14f + x.phase) * 2f, 0, 0); b[Avatar.BHead].localRotation = Quaternion.Euler(30f, 0, Mathf.Sin(Time.time * 11f + x.phase) * 3f);   // trembling
                    }
                    break;
                case S.Dead: DeadAnim(x, dt); break;
            }
        }

        void PanicArms(Mem x, float k)
        {   // hands up by the face, shaking
            var b = x.av.bones; float sh = Mathf.Sin(Time.time * 18f + x.phase) * 6f * k;
            b[Avatar.BoneIndex(1, 0)].localRotation = Quaternion.Euler(-110f * k + sh, 0, -10f); b[Avatar.BoneIndex(1, 1)].localRotation = Quaternion.Euler(-95f * k, 0, 0);
            b[Avatar.BoneIndex(-1, 0)].localRotation = Quaternion.Euler(-110f * k - sh, 0, 10f); b[Avatar.BoneIndex(-1, 1)].localRotation = Quaternion.Euler(-95f * k, 0, 0);
            b[Avatar.BHead].localRotation = Quaternion.Euler(-6f, Mathf.Sin(Time.time * 5f + x.phase) * 20f, 0);
        }

        void DeadAnim(Mem x, float dt)
        {
            x.deadT += dt; var tr = x.holder.transform; float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(x.deadT / 0.8f));
            Quaternion lie = Quaternion.LookRotation(Vector3.up, -x.fallDir) * Quaternion.Euler(0, 0, 0);   // lying on the back, head away from the shooter... approximated by a pitch fall
            Quaternion yawQ = Quaternion.Euler(0, Mathf.Atan2(x.fallDir.x, x.fallDir.z) * Mathf.Rad2Deg + 180f, 0);
            tr.rotation = Quaternion.Slerp(tr.rotation, yawQ * Quaternion.Euler(-86f, 0, 0), Mathf.Min(1f, dt * 7f));
            x.av.root.transform.localPosition = Vector3.Lerp(x.av.root.transform.localPosition, new Vector3(0, 0.12f, 0.0f), Mathf.Min(1f, dt * 6f));
            var b = x.av.bones; b[Avatar.BoneIndex(1, 0)].localRotation = Quaternion.Euler(-30f, 0, -50f * k); b[Avatar.BoneIndex(1, 1)].localRotation = Quaternion.Euler(-10f, 0, 0);
            b[Avatar.BoneIndex(-1, 0)].localRotation = Quaternion.Euler(-20f, 0, 60f * k); b[Avatar.BoneIndex(-1, 1)].localRotation = Quaternion.Euler(-25f, 0, 0);
            b[Avatar.BHead].localRotation = Quaternion.Euler(0, 20f, 10f); b[Avatar.BoneIndex(1, 3)].localRotation = Quaternion.Euler(-10f, 0, -8f); b[Avatar.BoneIndex(-1, 3)].localRotation = Quaternion.Euler(0, 0, 12f);
            float r = Mathf.Min(0.55f, 0.06f + x.deadT * 0.14f); x.blood.transform.localScale = new Vector3(r * 2f, 0.004f, r * 1.6f);
        }

        // ---- the father
        void Father(Mem f, float dt, Vector3 pp)
        {
            var ft = f.holder.transform;
            if (f.st == S.Dead) { DeadAnim(f, dt); return; }
            if (f.st == S.Eat) { SeatedAnim(f, 0, dt, pp, t < 0.35f); return; }
            if (f.st == S.Chase && f.seated)
            {   // jumps up from the chair with the stick
                stickPivot.SetActive(true); f.av.armUp = 1f; f.standT += dt; float k = Mathf.Clamp01(f.standT / 0.45f);
                f.av.root.transform.localPosition = new Vector3(0, Mathf.Lerp(-0.45f, 0f, k), 0); f.av.Pose("walk", dt, 0.2f, 0, true); FaceBody(ft, pp, dt, 12f);
                stickPivot.transform.localRotation = Quaternion.Euler(-70f, 0, 0);
                if (k >= 1f) { SetBody(f, false); lostT = 0; }
                return;
            }
            Vector3 flat = pp - ft.position; flat.y = 0; float dist = flat.magnitude; cool -= dt;
            if (f.st == S.Chase)
            {
                if (dist > 28f || player.dead) lostT += dt; else lostT = 0;
                if (lostT > 4f) f.st = S.Back;
                FaceBody(ft, pp, dt, 10f);
                if (f.stagger > 0) { Move(f, Vector3.zero, dt); f.av.Pose("walk", dt, 0f, 0, true); return; }
                if (swingT > 0)
                {   // wind-up then strike
                    swingT -= dt; float u = 1f - swingT / 0.7f; float ang = u < 0.6f ? Mathf.Lerp(-70f, -120f, u / 0.6f) : Mathf.Lerp(-120f, 55f, (u - 0.6f) / 0.4f);
                    stickPivot.transform.localRotation = Quaternion.Euler(ang, 0, 0);
                    if (swingT <= 0.28f && swingT + dt > 0.28f && !player.dead && dist < 2.6f)
                    { player.Damage(22f); player.Launch(player.Velocity + flat.normalized * 7f + Vector3.up * 3f); Sfx.Hit(); }
                    Move(f, Vector3.zero, dt); f.av.Pose("walk", dt, 0f, 0, true); return;
                }
                stickPivot.transform.localRotation = Quaternion.Euler(-70f + Mathf.Sin(t * 8f) * 6f, 0, 0);
                if (dist < 1.9f && cool <= 0 && !player.dead) { swingT = 0.7f; cool = 1.4f; Move(f, Vector3.zero, dt); return; }
                Move(f, flat.sqrMagnitude > 0.01f ? flat.normalized * 4.3f : Vector3.zero, dt); f.av.Pose("walk", dt, 0.65f, 0, true);
            }
            else if (f.st == S.Back)
            {
                Vector3 back = f.seatPos - ft.position; back.y = 0; float bd = back.magnitude; FaceBody(ft, f.seatPos, dt, 8f);
                Move(f, bd > 0.25f ? back.normalized * 2.4f : Vector3.zero, dt); f.av.Pose("walk", dt, 0.4f, 0, true);
                if (dist < 18f && !player.dead) { f.st = S.Chase; lostT = 0; }
                else if (bd < 0.3f)
                {   // sits back down to finish his meal
                    f.cc.enabled = false; ft.SetPositionAndRotation(f.seatPos, f.seatRot); f.av.root.transform.localPosition = new Vector3(0, -0.45f, 0); SetBody(f, true); f.cc.enabled = true;
                    stickPivot.SetActive(false); f.av.armUp = 0f; f.st = S.Eat; t = -3f; alarmed = false; f.standT = 0;
                }
            }
        }

        static float LookYaw(Transform body, Vector3 target)
        {
            Vector3 to = target - body.position; to.y = 0; if (to.sqrMagnitude < 0.01f) return 0f;
            return Mathf.Clamp(Mathf.DeltaAngle(body.eulerAngles.y, Quaternion.LookRotation(to).eulerAngles.y), -70f, 70f);
        }
        void FaceBody(Transform tr, Vector3 target, float dt, float rate)
        {
            Vector3 to = target - tr.position; to.y = 0; if (to.sqrMagnitude < 0.01f) return;
            tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.LookRotation(to.normalized), Mathf.Min(1f, dt * rate));
        }
        void Move(Mem x, Vector3 v, float dt)
        {
            if (!x.cc.enabled) return;
            x.vel.y = x.cc.isGrounded ? -1f : x.vel.y - 20f * dt; x.vel.x = v.x; x.vel.z = v.z; x.cc.Move(x.vel * dt);
        }
    }

    /// <summary>Bullets hit a family member (collider on the holder object).</summary>
    public sealed class FamilyHit : MonoBehaviour, IShootable
    {
        public Family owner; public int index;
        public void OnShot(float damage, Vector3 hitPoint, Vector3 dir, float force) { if (owner != null) owner.Shot(index, damage, hitPoint, dir); }
    }
}
