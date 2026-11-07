using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hearthvale
{
    /// <summary>Entry point: put this on one empty GameObject. Builds the world, the player and the post-processing.</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public GfxQuality quality = GfxQuality.High;
        public HvWorld world; public PlayerController player; [System.NonSerialized] public Arsenal arsenal; [System.NonSerialized] public Pickups pickups; [System.NonSerialized] public CritterSystem critters; [System.NonSerialized] public Ghosts ghosts; [System.NonSerialized] public Traps traps; [System.NonSerialized] public Garage garage; [System.NonSerialized] public Arena arena; [System.NonSerialized] public VehicleWeapons vweap;

        void Awake()
        {
            Camera cam = Camera.main;
            if (cam == null) { var cg = new GameObject("Main Camera"); cg.tag = "MainCamera"; cam = cg.AddComponent<Camera>(); cg.AddComponent<AudioListener>(); }
            cam.fieldOfView = 75; cam.nearClipPlane = 0.1f; cam.farClipPlane = 3000;
            cam.clearFlags = CameraClearFlags.Skybox;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = quality >= GfxQuality.High;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = quality != GfxQuality.LowEnd;

            // reuse the scene's directional light as the sun
            Light sun = null;
            foreach (var l in FindObjectsByType<Light>()) if (l.type == LightType.Directional) { sun = l; break; }

            var wgo = new GameObject("Hearthvale"); world = wgo.AddComponent<HvWorld>();
            world.quality = quality; world.sun = sun;
            world.Build();

            var pgo = new GameObject("Player"); pgo.AddComponent<CharacterController>();
            player = pgo.AddComponent<PlayerController>();
            player.world = world; player.cam = cam;
            world.focus = pgo.transform;
            // spawn in the South Gate yard, facing north into town (JS (0, 72) -> Unity z = -72 mirrored)
            var sp = world.town.spawnPoint;
            float y = Mathf.Max(WorldData.HeightAt(sp.x, sp.z), WorldData.TownHeight) + 0.1f;
            player.Teleport(new Vector3(sp.x, y, -sp.z));
            player.SetYaw(0);

            // guns, loot, effects and HUD
            new GameObject("Effects").AddComponent<Effects>();
            var arsenal = pgo.AddComponent<Arsenal>(); arsenal.Init(cam, player);
            var pickups = new GameObject("Pickups").AddComponent<Pickups>(); pickups.arsenal = arsenal; pickups.player = player;
            pickups.Build(world.town, world.transform);
            var hud = pgo.AddComponent<WeaponHud>(); hud.arsenal = arsenal; hud.player = player;
            this.arsenal = arsenal; this.pickups = pickups;
            var pav = pgo.AddComponent<PlayerAvatar>(); pav.player = player; pav.arsenal = arsenal;
            var ghosts = new GameObject("Ghosts").AddComponent<Ghosts>(); ghosts.world = world; ghosts.player = player; ghosts.arsenal = arsenal; player.ghosts = ghosts; this.ghosts = ghosts;
            var traps = new GameObject("Traps").AddComponent<Traps>(); traps.player = player; traps.ghosts = ghosts; traps.world = world; this.traps = traps;
            var garage = new GameObject("Garage").AddComponent<Garage>(); garage.world = world; garage.player = player; garage.Build(); garage.avatar = pav; pav.garage = garage; this.garage = garage;
            var vw = new GameObject("VehicleWeapons").AddComponent<VehicleWeapons>(); vw.garage = garage; vw.player = player; vw.world = world; this.vweap = vw;
            var arenaGo = new GameObject("ArenaShow").AddComponent<Arena>(); arenaGo.world = world; arenaGo.player = player; arenaGo.ghosts = ghosts; this.arena = arenaGo;
            var gasGo = new GameObject("Gas"); var gas = gasGo.AddComponent<GasClouds>();
            var crit = new GameObject("Critters").AddComponent<CritterSystem>(); crit.world = world; crit.player = player; crit.gas = gas; crit.arsenal = arsenal; this.critters = crit; vw.critters = crit; traps.critters = crit;

            var weere = new GameObject("Weere").AddComponent<Weere>(); weere.world = world; weere.player = player;
            var map = new GameObject("Map").AddComponent<MapUI>(); map.player = player; map.world = world; map.pickups = pickups; map.critters = crit; map.garage = garage;

            if (quality >= GfxQuality.High) BuildPostFx();
        }

        // ACES tone mapping, bloom, colour grade and vignette (same look as graphics.js)
        void BuildPostFx()
        {
            var go = new GameObject("PostFx"); go.transform.SetParent(transform, false);
            var vol = go.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 1;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>(); vol.profile = prof;
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var bl = prof.Add<Bloom>(true); bl.intensity.Override(0.35f); bl.threshold.Override(1.0f); bl.scatter.Override(0.5f);
            var ca = prof.Add<ColorAdjustments>(true); ca.saturation.Override(10f); ca.contrast.Override(7f); ca.postExposure.Override(0.35f);
            var vg = prof.Add<Vignette>(true); vg.intensity.Override(0.28f); vg.smoothness.Override(0.5f);
            var lgg = prof.Add<LiftGammaGain>(true); lgg.lift.Override(new Vector4(1.02f, 1.0f, 0.99f, 0.0f));
        }
    }
}
