using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hearthvale
{
    /// <summary>
    /// First / third person walking, sprinting, jumping and swimming (port of player.js).
    /// Same numbers as the JS game: walk 5.5, sprint 9.5, jump 8.5, gravity 24, eye 1.65 m.
    /// Unity's CharacterController + PhysX now handles collisions with real colliders.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        public const int PlayerLayer = 8;
        const float Eye = 1.65f;
        const float Radius = 0.45f;
        static readonly (string name, float dist, float side, float up)[] FootCams =
        {
            ("First person", 0, 0, 0),
            ("Third person · near", 2.0f, 0.55f, 0.15f),
            ("Third person · medium", 3.4f, 0.6f, 0.35f),
            ("Third person · far", 5.2f, 0.5f, 0.7f),
        };

        public HvWorld world;
        public Camera cam;
        public float sensitivity = 0.0022f;
        public bool invertY;
        public float hp = 100;
        public bool onGround, swimming, aiming;
        public int camMode;
        public string prompt = "";
        public string toast = ""; float toastT;

        CharacterController cc; float prevVy;
        Vector3 vel; float yaw, pitch, bob, camDist;
        public float recoil; public bool dead, scopedView, frozen, inCave; public HoverCraft vehicle; public int traps = 2;
        bool cursorFree;

        public Vector3 Pos { get { return transform.position; } }
        public float Yaw { get { return yaw; } }
        public bool pillion;   // riding on the back of Weere's scooter (Weere moves us and the camera)
        public void SetLook(float y, float p) { yaw = y; pitch = p; }
        public float Pitch { get { return pitch; } }
        public Vector3 Velocity { get { return vel; } }
        public bool InputActive { get { return !cursorFree; } }
        public bool ThirdPerson { get { return camMode > 0 && !scopedView; } }
        public float CamDist { get { return camDist; } }
        public float Bob { get { return bob; } }
        public bool OnGround { get { return onGround; } }
        /// <summary>Where third-person bullets and tracers start: the character's gun hand.</summary>
        public Vector3 Muzzle(Vector3 fwd)
        {
            Vector3 right = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
            return transform.position + right * 0.32f + Vector3.up * 1.4f + fwd * 0.5f;
        }

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            cc.radius = Radius; cc.height = 1.8f; cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.35f; cc.slopeLimit = 55; cc.skinWidth = 0.03f; cc.minMoveDistance = 0;
            gameObject.layer = PlayerLayer;
        }

        public void Teleport(Vector3 p) { cc.enabled = false; transform.position = p; cc.enabled = true; vel = Vector3.zero; }
        // automated tests inject keys here (the real keyboard keeps working as well)
        public HashSet<string> testKeys { get { return HvInput.testKeys; } }
        public HashSet<string> testHits { get { return HvInput.testHits; } }
        public bool mapOpen; public int mapClosedFrame = -1;   // the big map (M) owns the mouse and keyboard while open
        public void SetCursorFree(bool free) { LockCursor(!free); }
        bool Dn(string k, bool real) { return !mapOpen && (real || HvInput.testKeys.Contains(k)); }
        bool Hit(string k, bool real) { return !mapOpen && (real || HvInput.testHits.Remove(k)); }
        public bool freezeLook;   // used by automated tests so stray mouse movement cannot steer the camera
        public void SetStepOffset(float s) { cc.stepOffset = s; }
        public void SetYaw(float y) { yaw = y; }
        public void Launch(Vector3 v) { vel = v; }
        public void SetPitch(float p) { pitch = p; }

        void Start() { LockCursor(true); }

        void LockCursor(bool on)
        {
            cursorFree = !on;
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on; HvInput.Active = on;
        }

        public string CamName { get { return FootCams[camMode].name; } }

        void Update()
        {
            var kb = Keyboard.current; var ms = Mouse.current;
            if (kb == null || ms == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            hurtFlash = Mathf.Max(0, hurtFlash - dt * 1.5f);
            if (dead) { respawnTimer -= dt; if (respawnTimer <= 0) Respawn(); else return; }
            if (kb.leftAltKey.wasPressedThisFrame) LockCursor(cursorFree);
            if (kb.escapeKey.wasPressedThisFrame && !mapOpen && mapClosedFrame != Time.frameCount) LockCursor(false);
            if (cursorFree && !mapOpen && ms.leftButton.wasPressedThisFrame) LockCursor(true);

            if (!cursorFree && !freezeLook)
            {
                Vector2 d = ms.delta.ReadValue();
                yaw += d.x * sensitivity;
                pitch += d.y * sensitivity * (invertY ? -1 : 1);
                pitch = Mathf.Clamp(pitch, -1.5f, 1.5f);
            }
            if (vehicle != null) { vel = Vector3.zero; prompt = ""; UpdateFlashlight(dt); if (toastT > 0) { toastT -= dt; if (toastT <= 0) toast = ""; } return; }   // driving: the Garage moves everything
            if (Hit("V", kb.vKey.wasPressedThisFrame)) { camMode = (camMode + 1) % FootCams.Length; Toast("🎥 " + FootCams[camMode].name); }

            if (frozen) { vel = Vector3.zero; UpdateFlashlight(dt); return; }   // ground trap is dragging you under

            // wish direction from WASD
            float f = (Dn("W", kb.wKey.isPressed) ? 1 : 0) - (Dn("S", kb.sKey.isPressed) ? 1 : 0);
            float s = (Dn("D", kb.dKey.isPressed) ? 1 : 0) - (Dn("A", kb.aKey.isPressed) ? 1 : 0);
            float sin = Mathf.Sin(yaw), cos = Mathf.Cos(yaw);
            Vector3 wish = new Vector3(sin * f + cos * s, 0, cos * f - sin * s);
            if (wish.sqrMagnitude > 0) wish.Normalize();
            bool sprint = Dn("Shift", kb.leftShiftKey.isPressed) && f > 0;
            float speed = sprint ? 9.5f : 5.5f;
            if (swimming) speed = 3.5f;
            aiming = Dn("RMB", ms.rightButton.isPressed) && !cursorFree;
            if (aiming) speed *= 0.6f;

            float accel = onGround || swimming ? 14 : 3;
            float k = Mathf.Min(1, accel * dt);
            vel.x += (wish.x * speed - vel.x) * k;
            vel.z += (wish.z * speed - vel.z) * k;

            if (swimming) vel.y += ((Dn("Space", kb.spaceKey.isPressed) ? 3 : -0.5f) - vel.y) * dt * 3;
            else
            {
                if (onGround && Dn("Space", kb.spaceKey.isPressed)) { vel.y = 8.5f; onGround = false; }
                prevVy = vel.y; vel.y -= 24 * dt;
            }

            // keep inside the island-ish bounds
            Vector3 p0 = transform.position;
            var mv = vel; if (!swimming) mv.y = (prevVy + vel.y) * 0.5f; // average velocity over the frame
            var flags = cc.Move(mv * dt);
            onGround = cc.isGrounded;
            if (onGround && vel.y < 0 && !swimming) vel.y = -2f; // stay glued to slopes (not while swimming)
            if ((flags & CollisionFlags.Above) != 0 && vel.y > 0) vel.y = 0;

            Vector3 pos = transform.position;
            float r = Mathf.Sqrt(pos.x * pos.x + pos.z * pos.z);
            if (r > 440) { pos.x *= 440 / r; pos.z *= 440 / r; Teleport(pos); }

            // swimming (same rule as player.js): deep water under you and your feet below the surface
            float floor = WorldData.HeightAtU(pos.x, pos.z);
            bool wasSwimming = swimming;
            swimming = floor < WorldData.WaterLevel - 1.3f && transform.position.y < WorldData.WaterLevel - 0.6f;
            if (swimming)
            {
                pos = transform.position;
                float top = WorldData.WaterLevel - 1.1f;   // head just above the surface
                if (pos.y > top) { pos.y = top; vel.y = Mathf.Min(vel.y, 0f); }
                if (pos.y < floor) pos.y = floor;
                cc.enabled = false; transform.position = pos; cc.enabled = true;
                onGround = false;
            }

            float moving = new Vector2(vel.x, vel.z).magnitude;
            if (onGround && moving > 1) bob += dt * moving * 1.6f;
            recoil = Mathf.Max(0, recoil - dt * 6);

            UpdateFlashlight(dt);
            DoorInteraction(kb);
            if (toastT > 0) { toastT -= dt; if (toastT <= 0) toast = ""; }
        }

        // flashlight (L): a spot light on the camera; comes on by itself the first night
        Light flash; public bool flashOn; bool flashAuto; float flashFlicker = 1f; public Ghosts ghosts;
        void UpdateFlashlight(float dt)
        {
            if (flash == null)
            {
                var go = new GameObject("Flashlight"); go.transform.SetParent(cam.transform, false); go.transform.localPosition = new Vector3(0.0f, 0.1f, 0.6f);
                flash = go.AddComponent<Light>(); flash.type = LightType.Spot; flash.range = 42; flash.spotAngle = 46; flash.innerSpotAngle = 22; flash.intensity = 0; flash.color = M.Hex("#e4ecff").gamma; flash.shadows = LightShadows.None;
            }
            float horror = world.horrorK;
            if (!flashAuto && horror > 0.4f) { flashAuto = true; flashOn = true; Toast("🔦 Night falls. Something walks the streets… (L: flashlight)"); }
            if (Hit("L", Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)) { flashOn = !flashOn; flashAuto = true; Sfx.Hatch(); }
            flashFlicker = ghosts != null && ghosts.active != null && Random.value < 0.15f + ghosts.nearness * 0.4f ? Random.value * 0.3f : 1f;
            flash.intensity = flashOn && !dead ? 9f * flashFlicker : 0f;
            Sfx.SetDrone(ghosts != null && ghosts.active != null ? Mathf.Max(0.5f, ghosts.nearness) : 0f); // dread music only while a ghost is here
        }
        float respawnTimer; public float hurtFlash; Texture2D vig;
        /// <summary>Returns true if this hit knocked you out. gassed = from a fart cloud (no flash / grunt every tick).</summary>
        public bool Damage(float amount, bool gassed = false)
        {
            if (dead) return false;
            hp = Mathf.Max(0, hp - amount);
            if (!gassed) { Sfx.Hurt(); hurtFlash = 1f; }
            if (hp <= 0)
            {
                dead = true; respawnTimer = 5f; vel = Vector3.zero; Sfx.Voice("player_killed_gassed_wasted", 1f);
                Toast(gassed ? "🤢 You got gassed! Respawning at the South Gate…" : "You got knocked out! Respawning at the South Gate…");
                return true;
            }
            return false;
        }
        void Respawn()
        {
            dead = false; hp = 100; Sfx.Voice("when_raspawned_after_killed_here_we_go_again", 1f); var sp = world.town.spawnPoint;
            Teleport(new Vector3(sp.x + (Random.value - 0.5f) * 10, WorldData.TownHeight + 0.3f, -sp.z + (Random.value - 0.5f) * 6)); yaw = 0; pitch = 0;
        }
        public void Toast(string t) { toast = t; toastT = 2f; }

        void DoorInteraction(Keyboard kb)
        {
            prompt = "";
            if (world == null) return;
            var door = world.doors.Nearest(transform.position, 2.2f);
            if (door == null) return;
            if (door.state == DoorState.Closed && door.locked) { prompt = "🔒 Locked · shoot the door to break it in"; return; }
            prompt = door.state == DoorState.Open ? "Press F to close the door" : "Press F to open the door";
            if (Hit("F", kb.fKey.wasPressedThisFrame)) world.doors.Set(door.i, door.state == DoorState.Open ? DoorState.Closed : DoorState.Open);
        }

        void LateUpdate()
        {
            if (cam == null || vehicle != null || pillion) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            var c = FootCams[ThirdPerson ? camMode : 0];
            float eyeY = transform.position.y + Eye + (c.dist > 0 ? 0 : Mathf.Sin(bob) * 0.05f);
            Quaternion rot = Quaternion.Euler(-(pitch + recoil * 0.05f) * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, 0);
            cam.transform.rotation = rot;
            if (c.dist <= 0) { camDist = 0; cam.transform.position = new Vector3(transform.position.x, eyeY, transform.position.z); DeathCam(dt); return; }

            float zoom = aiming ? 0.55f : 1f;
            Vector3 right = rot * Vector3.right;
            Vector3 pivot = new Vector3(transform.position.x, eyeY + c.up * zoom, transform.position.z) + new Vector3(right.x, 0, right.z).normalized * c.side * zoom;
            float want = c.dist * zoom;
            Vector3 dir = rot * Vector3.back;
            float d = want;
            RaycastHit hit;
            int camMask = ~((1 << PlayerLayer) | (1 << 9));   // not the player or animals
            // the shoulder offset must not poke through a wall beside you (a cast that starts inside a collider finds nothing and the camera ends up inside the building)
            Vector3 head = new Vector3(transform.position.x, eyeY, transform.position.z), toPivot = pivot - head; float pl = toPivot.magnitude;
            if (pl > 0.01f && Physics.SphereCast(head, 0.2f, toPivot / pl, out hit, pl, camMask, QueryTriggerInteraction.Ignore)) pivot = head + toPivot / pl * Mathf.Max(0f, hit.distance - 0.02f);
            if (Physics.SphereCast(pivot, 0.25f, dir, out hit, want, camMask, QueryTriggerInteraction.Ignore)) d = Mathf.Max(0.05f, hit.distance - 0.02f);
            else if (Physics.CheckSphere(pivot + dir * want, 0.12f, camMask, QueryTriggerInteraction.Ignore)) d = Mathf.Max(0.05f, want * 0.5f);   // final spot is inside solid geometry: pull in
            // walls push the camera in instantly; it eases back out (no popping in narrow alleys)
            camDist = d < camDist ? d : camDist + (d - camDist) * Mathf.Min(1, dt * 6);
            Vector3 p = pivot + dir * camDist;
            float ground = Mathf.Max(WorldData.HeightAtU(p.x, p.z), WorldData.WaterLevel) + 0.25f;
            if (p.y < ground) p.y = ground;
            cam.transform.position = p;
            DeathCam(dt);
        }
        float deadCam;
        /// <summary>GTA-style: when you are wasted the camera sinks to the ground and the world tilts over.</summary>
        void DeathCam(float dt)
        {
            deadCam = dead ? deadCam + dt : 0f; if (deadCam <= 0) return;
            float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(deadCam / 1.2f)); var ct = cam.transform;
            Vector3 p = ct.position; float ground = Mathf.Max(WorldData.HeightAtU(p.x, p.z), WorldData.WaterLevel) + 0.35f; p.y = Mathf.Max(ground, p.y - k * 1.1f); ct.position = p;
            ct.rotation = ct.rotation * Quaternion.Euler(0, 0, 28f * k);
        }

        static Texture2D MakeVignette()
        {
            int n = 128; var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp }; var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1; float d = Mathf.Sqrt(dx * dx + dy * dy); float a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.5f, 1.35f, d)); px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255)); }
            t.SetPixels32(px); t.Apply(); return t;
        }

        // ---- minimal HUD for this phase (the real UI comes with the menu port)
        void OnGUI()
        {
            if (world == null) return;
            int h = (int)world.Hour, m = (int)((world.Hour - h) * 60);
            var st = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            st.normal.textColor = Color.white;
            GUI.Label(new Rect(14, 10, 500, 30), string.Format("Day {0} · {1:00}:{2:00}", world.day, h, m), st);
            GUI.Label(new Rect(Screen.width - 130, 10, 120, 30), Mathf.Min(999, Mathf.RoundToInt(1f / Mathf.Max(Time.smoothDeltaTime, 0.001f))) + " FPS", st);
            var ps = new GUIStyle(st) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, Screen.height * 0.62f, Screen.width, 30), prompt, ps);
            GUI.Label(new Rect(0, 50, Screen.width, 30), toast, ps);
            // underwater tint over the whole screen
            if (cam != null && !inCave && cam.transform.position.y < WorldData.WaterLevel)
            {
                GUI.color = new Color(0.02f, 0.30f, 0.40f, 0.62f); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = Color.white;
            }
            // health bar (bottom left) and the red vignette when hurt or low
            var hb = new Rect(14, Screen.height - 46, 260, 22);
            GUI.color = new Color(0, 0, 0, 0.5f); GUI.DrawTexture(hb, Texture2D.whiteTexture);
            GUI.color = hp > 35 ? new Color(0.45f, 0.85f, 0.4f) : new Color(1f, 0.35f, 0.3f); GUI.DrawTexture(new Rect(hb.x + 2, hb.y + 2, (hb.width - 4) * hp / 100f, hb.height - 4), Texture2D.whiteTexture); GUI.color = Color.white;
            var hs = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; hs.normal.textColor = Color.white;
            GUI.Label(hb, "❤ " + Mathf.CeilToInt(hp), hs);
            float va = Mathf.Max(hurtFlash, hp < 35 ? 0.5f : 0f) + (dead ? 0.4f : 0f);
            if (va > 0.01f)
            {
                if (vig == null) vig = MakeVignette();
                GUI.color = new Color(1, 0.15f, 0.12f, Mathf.Clamp01(va)); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vig); GUI.color = Color.white;
            }
            Watch.Draw();
        }
    }
}
