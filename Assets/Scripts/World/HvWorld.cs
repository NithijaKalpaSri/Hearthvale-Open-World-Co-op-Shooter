using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hearthvale
{
    public enum GfxQuality { LowEnd, Low, High, Ultra }

    /// <summary>
    /// Builds and runs the island: terrain, water, the Old Quarter, doors, street lamps and the
    /// day/night cycle. Port of the World class in world.js.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class HvWorld : MonoBehaviour
    {
        public GfxQuality quality = GfxQuality.High;
        public Light sun;
        public Transform focus;                 // what the shadows follow (the player)
        public DoorSystem doors;
        [System.NonSerialized] public TownBuilder town;
        public Material waterMat;

        // game clock (in-game day length: 24 real minutes by default)
        [Range(0, 1)] public float time = 0.3f;
        public int day = 1;
        public float dayLength = 1440f;
        public float nightFactor, dayLight;
        public float windSpeed = 3f;
        public float ghostK; // 0..1, eased up while a ghost is haunting (set by Ghosts)
        public float horrorK, lampFlicker, horrorSuppress; // night horror strength 0..1, ghost-driven lamp flicker 0..1
        public float darkness;                  // storm / horror darkness (set by weather later)
        public Vector3 sunDirJs;

        public float Hour { get { return (time * 24f) % 24f; } }
        public void SetHour(float h) { time = (((h / 24f) % 1f) + 1f) % 1f; }

        // sky colours: [sky, horizon]
        static readonly Color[] SkyNight = { M.Hex("#0b1026"), M.Hex("#1c2440") };
        static readonly Color[] SkyDawn = { M.Hex("#7fa6d9"), M.Hex("#f2b597") };
        static readonly Color[] SkyDay = { M.Hex("#6ea4d6"), M.Hex("#ddd5c4") };
        static readonly Color[] SkyDusk = { M.Hex("#5d5f9c"), M.Hex("#f09a72") };

        Material skyMat, terrainMat;
        [System.NonSerialized] public NatureBuilder nature; [System.NonSerialized] GrassField grass; [System.NonSerialized] Fireflies flies;
        MeshRenderer[] detailMeshes = new MeshRenderer[0];
        float detailT;
        Transform root;
        readonly List<Light> lampLights = new List<Light>();
        public class Pole { public Transform t; public Vector3 bulb; public bool broken; }
        public readonly List<Pole> poles = new List<Pole>();

        bool built;
        public void Build()
        {
            Application.targetFrameRate = quality == GfxQuality.LowEnd ? 30 : 60;
            ApplyQuality();
            root = new GameObject("World").transform; root.SetParent(transform, false);
            doors = new GameObject("Doors").AddComponent<DoorSystem>(); doors.transform.SetParent(transform, false);
            BuildLights();
            BuildTerrain();
            BuildWater();
            BuildTown();
            BuildNature();
            BuildColliders();
            built = true;
        }

        // ------------------------------------------------------------ quality presets (same four as the JS options)
        void ApplyQuality()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                switch (quality)
                {
                    case GfxQuality.LowEnd: urp.renderScale = 0.75f; urp.shadowDistance = 0; urp.supportsHDR = false; urp.msaaSampleCount = 1; urp.shadowCascadeCount = 1; break;
                    case GfxQuality.Low: urp.renderScale = 1f; urp.shadowDistance = 60; urp.supportsHDR = false; urp.msaaSampleCount = 1; urp.shadowCascadeCount = 1; urp.mainLightShadowmapResolution = 1024; break;
                    case GfxQuality.High: urp.renderScale = 1f; urp.shadowDistance = 90; urp.supportsHDR = true; urp.msaaSampleCount = 2; urp.shadowCascadeCount = 2; urp.mainLightShadowmapResolution = 2048; break;
                    default: urp.renderScale = 1f; urp.shadowDistance = 140; urp.supportsHDR = true; urp.msaaSampleCount = 4; urp.shadowCascadeCount = 4; urp.mainLightShadowmapResolution = 4096; break;
                }
            }
            QualitySettings.anisotropicFiltering = quality <= GfxQuality.Low ? AnisotropicFiltering.Disable : AnisotropicFiltering.Enable;
            QualitySettings.vSyncCount = 0;
        }

        // ------------------------------------------------------------ lights
        void BuildLights()
        {
            if (sun == null)
            {
                var go = new GameObject("Sun"); go.transform.SetParent(transform, false);
                sun = go.AddComponent<Light>(); sun.type = LightType.Directional;
            }
            sun.color = M.Hex("#fff1d6");
            sun.shadows = quality == GfxQuality.LowEnd ? LightShadows.None : LightShadows.Soft;
            sun.shadowBias = 0.05f; sun.shadowNormalBias = 0.4f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 160; RenderSettings.fogEndDistance = 900;
            RenderSettings.fogColor = M.Hex("#c9dcef");
            skyMat = new Material(Shader.Find("Hearthvale/Sky"));
            skyMat.SetFloat("_CloudOctaves", quality == GfxQuality.LowEnd ? 2 : 4);
            RenderSettings.skybox = skyMat;
            // street lights: real point lights only on non-low-end; they hop to the nearest lamps at night
            for (int i = 0; i < (quality == GfxQuality.LowEnd ? 0 : 8); i++)
            {
                var go = new GameObject("LampLight" + i); go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 26; l.color = M.Hex("#ffc98a"); l.intensity = 0; l.shadows = LightShadows.None;
                l.enabled = false; lampLights.Add(l);
            }
        }

        // ------------------------------------------------------------ terrain
        void BuildTerrain()
        {
            int seg = WorldData.GridSegments, hw = seg + 1; float size = WorldData.WorldSize, cell = size / seg;
            var grid = new float[hw * hw];
            var V = new Vector3[hw * hw]; var C = new Color[hw * hw]; var UV = new Vector2[hw * hw];
            Color grassA = M.Hex("#5d8a37"), grassB = M.Hex("#7da346"), dry = M.Hex("#a3a352"), forest = M.Hex("#4a6e2c");
            Color sand = M.Hex("#d8c497"), wetSand = M.Hex("#b49e72"), rock = M.Hex("#857d72"), snow = M.Hex("#f2f3f6"), seabed = M.Hex("#a8956a");
            Color path = M.Hex("#9c8361"), aridA = M.Hex("#bfa477"), aridB = M.Hex("#a8915f");
            for (int iz = 0; iz < hw; iz++) for (int ix = 0; ix < hw; ix++)
                {
                    int i = iz * hw + ix;
                    float x = -size / 2 + ix * cell, z = -size / 2 + iz * cell;
                    float h = (float)WorldData.HeightExact(x, z);
                    grid[i] = h;
                    V[i] = new Vector3(x, h, -z); UV[i] = new Vector2(x / 6f, z / 6f);
                    Color col = Color.Lerp(grassA, grassB, (float)Hv.Fbm(x * 0.03, z * 0.03, 2));
                    if (Hv.Fbm(x * 0.008 + 99, z * 0.008, 2) > 0.6) col = Color.Lerp(col, dry, 0.5f);
                    if (Hv.Fbm(x * 0.012 + 7, z * 0.012 + 3, 2) > 0.5) col = Color.Lerp(col, forest, 0.45f);
                    float ring = Mathf.Abs(Mathf.Sqrt(x * x + z * z) - 235);
                    if (ring < 4 && h > 1.5f) col = Color.Lerp(col, path, 0.9f - ring * 0.15f);
                    float arid = 1 - (float)Hv.Smoothstep(WorldData.TownR + 15, WorldData.TownR + 120, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)));
                    if (arid > 0) col = Color.Lerp(col, Hv.Fbm(x * 0.05, z * 0.05, 2) > 0.5 ? aridA : aridB, arid * 0.9f);
                    if (h < 2.2f) col = Color.Lerp(col, sand, (float)Hv.Smoothstep(2.2, 1.4, h));
                    if (h < 0.4f) col = wetSand;
                    if (h < -1.5f) col = seabed;
                    if (h > 38) col = Color.Lerp(col, rock, (float)Hv.Smoothstep(38, 52, h));
                    if (h > 66) col = Color.Lerp(col, snow, (float)Hv.Smoothstep(66, 76, h));
                    C[i] = col;
                }
            WorldData.SetGrid(grid);
            // The terrain is cut into 6 x 6 chunks (50 cells each) so the GPU can skip chunks that are off screen
            // or outside the shadow range. Normals come straight from the height grid, so chunk borders have no seams.
            terrainMat = HvMaterials.Make("terrain", "ground", "ground_n", 0.02f, 0, "#ffffff", true, false, false, 0.9f);
            terrainMat.SetFloat("_Mix", 0.45f);
            var tparent = new GameObject("Terrain").transform; tparent.SetParent(root, false);
            const int CH = 50; int nc = seg / CH;
            Func<int, int, float> H = (ix, iz) => grid[Mathf.Clamp(iz, 0, seg) * hw + Mathf.Clamp(ix, 0, seg)];
            for (int cz = 0; cz < nc; cz++) for (int cx = 0; cx < nc; cx++)
                {
                    int vw = CH + 1; var cv = new Vector3[vw * vw]; var cn = new Vector3[vw * vw]; var cuv = new Vector2[vw * vw]; var cc = new Color[vw * vw];
                    for (int j = 0; j < vw; j++) for (int i = 0; i < vw; i++)
                        {
                            int gx = cx * CH + i, gz = cz * CH + j, gi = gz * hw + gx, ci = j * vw + i;
                            cv[ci] = V[gi]; cuv[ci] = UV[gi]; cc[ci] = C[gi];
                            float dhx = (H(gx + 1, gz) - H(gx - 1, gz)) / (2 * cell), dhz = (H(gx, gz + 1) - H(gx, gz - 1)) / (2 * cell);
                            cn[ci] = new Vector3(-dhx, 1f, dhz).normalized; // JS normal (-dhdx, 1, -dhdz) with z mirrored
                        }
                    var ct = new int[CH * CH * 6]; int k = 0;
                    for (int j = 0; j < CH; j++) for (int i = 0; i < CH; i++)
                        {
                            int a2 = j * vw + i, b2 = (j + 1) * vw + i, c2 = (j + 1) * vw + i + 1, d2 = j * vw + i + 1;
                            ct[k++] = a2; ct[k++] = d2; ct[k++] = b2; ct[k++] = b2; ct[k++] = d2; ct[k++] = c2;
                        }
                    var m = new Mesh { name = "Terrain_" + cx + "_" + cz };
                    m.SetVertices(cv); m.SetNormals(cn); m.SetUVs(0, cuv); m.SetColors(cc); m.SetTriangles(ct, 0); m.RecalculateBounds();
                    var go = new GameObject("TerrainChunk_" + cx + "_" + cz); go.transform.SetParent(tparent, false); go.layer = 10; go.isStatic = true;
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = terrainMat; mr.receiveShadows = true;
                    go.AddComponent<MeshCollider>().sharedMesh = m;
                }
        }

        void BuildWater()
        {
            bool low = quality == GfxQuality.LowEnd;
            waterMat = HvMaterials.Make("water", null, "water_n", 0.95f, 0, "#1c6a85", false, !low, true, 0.45f);
            waterMat.SetFloat("_Alpha", low ? 1f : 0.93f);
            waterMat.SetColor("_EmissionColor", M.Hex("#1f8fb0") * 1.3f); // keeps the sea teal-blue instead of reflecting grey
            waterMat.SetTextureScale("_BaseMap", new Vector2(220, 220));
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "Water";
            go.transform.SetParent(root, false);
            go.transform.rotation = Quaternion.Euler(90, 0, 0); go.transform.localScale = new Vector3(5000, 5000, 1);
            go.transform.position = new Vector3(0, WorldData.WaterLevel, 0);
            Destroy(go.GetComponent<MeshCollider>());
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = waterMat; mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------ town
        void BuildTown()
        {
            var rng = new Mulberry32(Hv.WorldSeed);
            town = new TownBuilder(rng);
            town.Build();
            foreach (var d in town.doors) doors.Add(d);
            var noShadow = new HashSet<string> { "cobble", "dirt", "pave", "glass", "glassDark", "ceiling", "decalA", "decalB", "fountainWater", "porch" };
            var res = town.B.Build(root, HvMaterials.Get, noShadow);
            detailMeshes = res.details.ToArray();
            BuildLampPosts();
        }

        void BuildLampPosts()
        {
            const float Y = WorldData.TownHeight;
            Func<float, float, float, float, int, Geo> cyl = (r1, r2, h, yy, seg) => new Geo().Append(Geo.Cylinder(r1, r2, h, seg), M.T(0, yy, 0));
            var metal = new Geo();
            metal.Append(cyl(0.22f, 0.28f, 0.5f, 0.25f, 12), Matrix4x4.identity);
            metal.Append(cyl(0.09f, 0.13f, 5.6f, 3.3f, 12), Matrix4x4.identity);
            metal.Append(Geo.Tube(new Vector3(0, 5.9f, 0), new Vector3(0, 6.5f, 0.7f), new Vector3(0, 6.1f, 1.45f), 12, 0.06f, 8), Matrix4x4.identity);
            metal.Append(cyl(0.12f, 0.34f, 0.28f, 5.95f, 16), M.T(0, 0, 1.5f));
            var glass = new Geo().Append(Geo.Cylinder(0.3f, 0.26f, 0.12f, 16), M.T(0, 5.76f, 1.5f));
            var metalMesh = metal.ToUnityMesh("lampMetal"); var glassMesh = glass.ToUnityMesh("lampGlass");
            var poleMat = HvMaterials.Make("pole", null, null, 0.4f, 0.85f, "#2f3238", false);
            var bulbMat = HvMaterials.BulbMat();
            var parent = new GameObject("LampPosts").transform; parent.SetParent(root, false);
            poleMat.enableInstancing = true; bulbMat.enableInstancing = true;
            poleMesh = metalMesh; bulbMesh = glassMesh; poleMaterial = poleMat; bulbMaterial = bulbMat;
            var list = new List<Matrix4x4>();
            foreach (var s in town.poleSpots)
            {
                var pos = new Vector3(s.x, Y, -s.y); var rot = Quaternion.Euler(0, -s.z * Mathf.Rad2Deg, 0);
                list.Add(Matrix4x4.TRS(pos, rot, Vector3.one));
                // physics only: an invisible capsule per pole (the visible poles are drawn instanced, see LateUpdate)
                var go = new GameObject("LampCollider"); go.transform.SetParent(parent, false); go.transform.position = pos; go.transform.rotation = rot;
                var cap = go.AddComponent<CapsuleCollider>(); cap.radius = 0.3f; cap.height = 6f; cap.center = new Vector3(0, 3, 0);
                poles.Add(new Pole { t = go.transform, bulb = go.transform.TransformPoint(new Vector3(0, 5.6f, -1.5f)) });
            }
            poleMatrices = list.ToArray();
        }

        Mesh poleMesh, bulbMesh; Material poleMaterial, bulbMaterial; Matrix4x4[] poleMatrices = new Matrix4x4[0];

        // ------------------------------------------------------------ physics: real colliders for walls, crates, props
        void BuildColliders()
        {
            const float Y = WorldData.TownHeight;
            var chunks = new Dictionary<long, GameObject>();
            foreach (var b in town.boxes)
            {
                if (b.door >= 0) continue;
                float cx = (b.minX + b.maxX) / 2, cz = (b.minZ + b.maxZ) / 2;
                long k = (long)Mathf.Floor(cx / 80f) * 4096 + (long)Mathf.Floor(cz / 80f);
                GameObject host;
                if (!chunks.TryGetValue(k, out host)) { host = new GameObject("Colliders_" + k); host.transform.SetParent(root, false); host.isStatic = true; chunks[k] = host; }
                float y1 = b.top, y0 = b.bottom ?? Mathf.Max(b.top - 60f, Y - 3f);
                var bc = host.AddComponent<BoxCollider>();
                bc.center = new Vector3(cx, (y0 + y1) / 2, -cz);
                bc.size = new Vector3(b.maxX - b.minX, y1 - y0, b.maxZ - b.minZ);
            }
            var cHosts = new Dictionary<long, GameObject>();
            foreach (var c in town.circles)
            {
                long k = (long)Mathf.Floor(c.x / 80f) * 4096 + (long)Mathf.Floor(c.z / 80f);
                GameObject host;
                if (!cHosts.TryGetValue(k, out host)) { host = new GameObject("PropColliders_" + k); host.transform.SetParent(root, false); host.isStatic = true; cHosts[k] = host; }
                float gy = c.h > 3f && c.r < 0.6f ? WorldData.HeightAt(c.x, c.z) : Mathf.Max(WorldData.HeightAt(c.x, c.z), WorldData.TownHeight - 0.2f);
                var cc = host.AddComponent<CapsuleCollider>(); cc.radius = c.r; cc.height = Mathf.Max(c.h, c.r * 2);
                cc.center = new Vector3(c.x, gy + cc.height / 2f - 0.15f, -c.z);
            }
        }

        // ------------------------------------------------------------ nature
        void BuildNature()
        {
            nature = new NatureBuilder(quality);
            nature.Build(town.rng, town, root);
            nature.BuildPalms(town, root);
            for (int i = 0; i < town.pickupSpots.Count; i++)
            {
                var p = town.pickupSpots[i];
                if (!p.fixedY) { p.p.y = WorldData.HeightAt(p.p.x, p.p.z) + 1; town.pickupSpots[i] = p; }
            }
            if (quality >= GfxQuality.High) grass = quality == GfxQuality.Ultra ? new GrassField(55, 0.72f) : new GrassField(40, 0.85f);
            flies = new Fireflies();
        }

        /// <summary>true inside the solid stone blocks of the district (nothing should spawn there). Unity coordinates.</summary>
        public bool IsBlockedU(float x, float zUnity)
        {
            float z = -zUnity; int R = WorldData.TownR;
            if (WorldData.ArenaDist(x, z) < 5f) return true;   // animals (and anything else that asks) keep out of the concert Arena
            if (town == null || town.districtOpen == null || Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) >= R) return false;
            int i = Mathf.FloorToInt(x + R), j = Mathf.FloorToInt(z + R);
            return town.districtOpen[j * R * 2 + i] == 0;
        }

        // ------------------------------------------------------------ per frame
        void Update()
        {
            if (!built) return;
            float dt = Time.deltaTime;
            time += dt / dayLength;
            if (time >= 1) { time -= 1; day++; }
            cloudTime += dt * windSpeed; 
            UpdateSky();
            Shader.SetGlobalVector("_HvWind", new Vector4(0.94f, 0.34f, windSpeed, Time.time));
            doors.Tick(dt, (p, r) => focus != null && (focus.position - p).sqrMagnitude < r * r);

            // small facade details (shutters, bars, cables, awnings) only near the camera
            detailT -= dt;
            var cam = Camera.main;
            if (detailT <= 0 && cam != null)
            {
                detailT = 0.25f;
                float R = quality == GfxQuality.LowEnd ? 55 : 110; Vector3 cp = cam.transform.position;
                foreach (var m in detailMeshes)
                {
                    var b = m.bounds;
                    m.enabled = Vector3.Distance(b.center, cp) - b.extents.magnitude < R;
                }
            }
            UpdateLampLights(dt, cam);
            // water ripples drift
            waterOff += new Vector2(0.004f, 0.0025f) * dt; waterMat.SetTextureOffset("_BaseMap", waterOff);
        }

        public float overcast;            // 0..1, set by the weather system
        float cloudTime, envTimer;
        bool wasUnder; float fogStart0 = 160, fogEnd0 = 900;
        Vector2 waterOff;
        public bool caveMode; bool wasCave; float cfs0, cfe0;
        const float ambientBoost = 3.0f;
        void LateUpdate()
        {
            if (!built) return;
            var cam = Camera.main; if (cam == null) return;
            Vector3 f = focus != null ? focus.position : cam.transform.position;
            if (nature != null) nature.Render(cam, f);
            if (grass != null) { grass.Update(f); grass.Render(); }
            if (poleMatrices.Length > 0)
            {
                Graphics.DrawMeshInstanced(poleMesh, 0, poleMaterial, poleMatrices, poleMatrices.Length, null, ShadowCastingMode.On, true, 0);
                Graphics.DrawMeshInstanced(bulbMesh, 0, bulbMaterial, poleMatrices, poleMatrices.Length, null, ShadowCastingMode.Off, false, 0);
            }
            if (flies != null) flies.Update(f, nightFactor);
            // underwater: thick teal fog instead of the normal haze
            if (caveMode != wasCave)
            {   // down in the ghost-trap cave: no sun, dark thick air
                wasCave = caveMode;
                if (caveMode) { cfs0 = RenderSettings.fogStartDistance; cfe0 = RenderSettings.fogEndDistance; RenderSettings.fogStartDistance = 3; RenderSettings.fogEndDistance = 60; }
                else { RenderSettings.fogStartDistance = cfs0; RenderSettings.fogEndDistance = cfe0; }
            }
            if (caveMode)
            {
                RenderSettings.fogColor = M.Hex("#0b0908"); sun.intensity = 0;
                RenderSettings.ambientSkyColor = RenderSettings.ambientEquatorColor = RenderSettings.ambientGroundColor = M.Hex("#3a3633") * 1.0f;
            }
            bool under = !caveMode && cam.transform.position.y < WorldData.WaterLevel;
            if (under != wasUnder)
            {
                wasUnder = under;
                if (under) { fogStart0 = RenderSettings.fogStartDistance; fogEnd0 = RenderSettings.fogEndDistance; RenderSettings.fogStartDistance = 0; RenderSettings.fogEndDistance = 45; }
                else { RenderSettings.fogStartDistance = fogStart0; RenderSettings.fogEndDistance = fogEnd0; }
            }
            if (under) RenderSettings.fogColor = Color.Lerp(M.Hex("#0b4a5c"), M.Hex("#03161c"), nightFactor);
        }

        float lampTimer;
        void UpdateLampLights(float dt, Camera cam)
        {
            if (lampLights.Count == 0 || cam == null) return;
            bool on = darkness > 0.02f || nightFactor > 0.3f;
            lampTimer -= dt;
            if (!on) { foreach (var l in lampLights) { l.enabled = false; } return; }
            if (lampTimer > 0) return;
            lampTimer = 0.4f;
            Vector3 cp = cam.transform.position;
            var near = new List<Pole>(poles); near.RemoveAll(p => p.broken);
            near.Sort((a, b) => (a.bulb - cp).sqrMagnitude.CompareTo((b.bulb - cp).sqrMagnitude));
            for (int i = 0; i < lampLights.Count; i++)
            {
                var l = lampLights[i];
                bool ok = i < near.Count && (near[i].bulb - cp).sqrMagnitude < 160f * 160f;
                l.enabled = ok;
                if (ok) { l.transform.position = near[i].bulb + Vector3.down * 0.4f; l.intensity = Mathf.Max(darkness, nightFactor) * 2.2f * (1 - 0.55f * horrorK) * (lampFlicker > 0.05f ? (UnityEngine.Random.value < lampFlicker * 0.7f ? 0.02f : 1f) : (Mathf.Sin(Time.time * (7 + i * 3.1f)) > 0.93f ? 0.15f : 1f)); l.color = M.Hex(horrorK > 0.5f ? "#cfe2c4" : "#ffc98a").gamma; }
            }
        }

        void UpdateSky()
        {
            float ang = (time - 0.25f) * Mathf.PI * 2;
            Vector3 sd = new Vector3(Mathf.Cos(ang) * 0.8f, Mathf.Sin(ang), 0.45f).normalized; // JS space
            sunDirJs = sd;
            float elev = sd.y;
            Color top, hor;
            Color[] edge = time < 0.5f ? SkyDawn : SkyDusk;
            if (elev > 0.25f) { top = SkyDay[0]; hor = SkyDay[1]; }
            else if (elev > 0) { float t = elev / 0.25f; top = Color.Lerp(edge[0], SkyDay[0], t); hor = Color.Lerp(edge[1], SkyDay[1], t); }
            else { float t = Mathf.Clamp01(-elev / 0.2f); top = Color.Lerp(edge[0], SkyNight[0], t); hor = Color.Lerp(edge[1], SkyNight[1], t); }
            nightFactor = (float)Hv.Smoothstep(0.05, -0.15, elev);
            dayLight = (float)Hv.Smoothstep(-0.12, 0.3, elev);
            if (!wasUnder) RenderSettings.fogColor = hor;

            // sun by day, cool moonlight by night
            Vector3 toLightJs = elev > -0.05f ? sd : -sd;
            Vector3 toLightU = WorldSpace.ToUnity(toLightJs);
            if (elev <= -0.05f) { toLightU.y = Mathf.Max(toLightU.y, 0.7f); toLightU.Normalize(); } // the moon always shines from high enough to light the streets
            sun.transform.rotation = Quaternion.LookRotation(-toLightU);
            float sunI = elev > -0.05f ? Mathf.Lerp(0.4f, 3.2f, (float)Hv.Smoothstep(-0.05, 0.35, elev)) : 0.35f;
            sun.intensity = (elev > -0.05f ? sunI / Mathf.PI : 0.55f) * (quality == GfxQuality.LowEnd ? 1.15f : 1f); // moonlight is brighter than the JS value: Unity has no env-map fill
            sun.color = M.Hex(elev > 0.25f ? "#fff2dc" : elev > -0.05f ? "#ffb27e" : "#9fb4ff");
            // ambient (hemisphere light): sky colour above, warm ground bounce below
            float hemi = Mathf.Lerp(0.35f, 0.2f, nightFactor);
            Color sky = Color.Lerp(Color.Lerp(top, Color.white, 0.5f), M.Hex("#4a5f9a"), nightFactor * 0.7f);
            RenderSettings.ambientSkyColor = sky * (hemi * ambientBoost);
            RenderSettings.ambientEquatorColor = Color.Lerp(sky, M.Hex("#a39580"), 0.5f) * (hemi * ambientBoost);
            RenderSettings.ambientGroundColor = M.Hex("#a39580") * (hemi * ambientBoost);

            if (skyMat != null)
            {
                skyMat.SetColor("_TopColor", top);
                skyMat.SetColor("_HorizonColor", hor);
                skyMat.SetVector("_SunDir", WorldSpace.ToUnity(sd));
                skyMat.SetColor("_SunColor", M.Hex(elev > 0.25f ? "#fff2dc" : "#ffb27e"));
                skyMat.SetFloat("_Night", nightFactor);
                // cloud lighting follows the sun: white by day, peach at sunset, blue-grey at night
                Color dayC = Color.white, duskC = M.Hex("#ffc4a8"), nightC = M.Hex("#2e3552");
                Color cc = elev > 0.25f ? dayC : elev > 0 ? Color.Lerp(duskC, dayC, elev / 0.25f) : Color.Lerp(duskC, nightC, Mathf.Min(1, -elev / 0.15f));
                skyMat.SetColor("_CloudColor", cc * Mathf.Lerp(1.15f, 0.5f, nightFactor));
                skyMat.SetFloat("_Overcast", overcast);
                skyMat.SetColor("_OvercastColor", Color.Lerp(M.Hex("#9aa0aa") * 0.8f, M.Hex("#9aa0aa") * 0.08f, 1 - dayLight));
                skyMat.SetFloat("_CloudTime", cloudTime);
                envTimer -= Time.deltaTime;
                if (envTimer <= 0 && quality != GfxQuality.LowEnd) { envTimer = 2f; DynamicGI.UpdateEnvironment(); } // refresh sky reflections
            }
            // ---- night horror: near-black fog, dim moon, dark windows, flickering lamps
            horrorK = (float)Hv.Smoothstep(0.15, 0.85, nightFactor) * (1 - horrorSuppress) * ghostK; // the dread only closes in while a ghost is about
            if (horrorK > 0 && !wasUnder)
            {
                RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, M.Hex("#020304"), horrorK * 0.96f);
                RenderSettings.fogStartDistance = Mathf.Lerp(fogStart0, 1f, horrorK); RenderSettings.fogEndDistance = Mathf.Lerp(fogEnd0, 42f, horrorK);
                sun.intensity *= 1 - 0.8f * horrorK;
                RenderSettings.ambientSkyColor *= 1 - 0.7f * horrorK; RenderSettings.ambientEquatorColor *= 1 - 0.7f * horrorK; RenderSettings.ambientGroundColor *= 1 - 0.7f * horrorK;
            }
            else if (!wasUnder) { RenderSettings.fogStartDistance = fogStart0; RenderSettings.fogEndDistance = fogEnd0; }
            // gentle moonlight floor so night is never pitch dark (applied after the horror dimming)
            if (!wasUnder && nightFactor > 0)
            {
                float calm = 1 - horrorK; // full moonlight unless a ghost is dimming things
                sun.intensity += 1.1f * nightFactor * calm;
                Color moonAmb = new Color(0.20f, 0.26f, 0.42f) * nightFactor * (0.35f + 0.65f * calm);
                RenderSettings.ambientSkyColor += moonAmb; RenderSettings.ambientEquatorColor += moonAmb * 0.9f; RenderSettings.ambientGroundColor += moonAmb * 0.7f;
                RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, M.Hex("#18264a"), 0.6f * nightFactor * calm);
                RenderSettings.fogEndDistance = Mathf.Max(RenderSettings.fogEndDistance, Mathf.Lerp(fogEnd0, 260f * calm + 42f * (1 - calm), nightFactor));
            }
            float flick = lampFlicker > 0.05f && UnityEngine.Random.value < lampFlicker * 0.7f ? 0.05f : 1f;
            HvMaterials.SetNightGlow(nightFactor * (1 - 0.85f * horrorK) * (horrorK > 0 ? flick : 1f));
        }
    }
}
