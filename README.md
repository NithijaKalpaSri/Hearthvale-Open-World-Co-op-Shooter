<div align="center">

# 🌸 Hearthvale

### Open-World Co-op Shooter · Unity Edition

**Explore a sun-baked island town · fly alien hover-craft · survive haunted nights · gas the Stinkers**

![Engine](https://img.shields.io/badge/Unity-6000.6.8f1-black?logo=unity)
![Pipeline](https://img.shields.io/badge/render%20pipeline-URP-blue)
![Language](https://img.shields.io/badge/language-C%23-239120)
![Status](https://img.shields.io/badge/status-work%20in%20progress-orange)

[🎮 Controls](#-controls) · [🚀 Open the project](#-open-the-project) · [🗺 World](#-the-world) · [📁 Structure](#-project-structure) · [🗓 Roadmap](#-roadmap)

<img src="docs/screenshots/town-south-gate.jpg" alt="Hearthvale: the South Gate of the Old Quarter" width="90%">

</div>

---

## ✨ About

Hearthvale is a cozy open-world shooter set on a seeded island with a dense, enterable town. This repository is the **Unity (URP) rebuild** of the original Three.js game, with more realistic characters, better physics and good performance on low-end and integrated graphics.

The world is generated in code from a fixed seed (identical to the original game), so the island, the town and all loot spawn in the same places every time.

---

## 📸 Gallery

<table>
  <tr>
    <td width="50%"><img src="docs/screenshots/site-a.jpg" alt="Site A plaza"><br><sub><b>Site A:</b> an open plaza with cover and loot</sub></td>
    <td width="50%"><img src="docs/screenshots/third-person.jpg" alt="Third-person view"><br><sub><b>Third person:</b> your character in the Old Quarter</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/screenshots/night.jpg" alt="Night in Hearthvale"><br><sub><b>Night:</b> fog, flickering lamps and a flashlight</sub></td>
    <td width="50%"><img src="docs/screenshots/arena-band.jpg" alt="Arena concert"><br><sub><b>The Arena:</b> a live concert from 4 PM to 8 PM</sub></td>
  </tr>
</table>

### 🛸 Hover-craft

<table>
  <tr>
    <td width="33%"><img src="docs/screenshots/hovercraft-pod.jpg" alt="Pod"><br><sub><b>Pod</b></sub></td>
    <td width="33%"><img src="docs/screenshots/hovercraft-manta.jpg" alt="Manta"><br><sub><b>Manta</b></sub></td>
    <td width="33%"><img src="docs/screenshots/hovercraft-beetle.jpg" alt="Beetle"><br><sub><b>Beetle</b></sub></td>
  </tr>
</table>

### 👻 The night ghosts

<table>
  <tr>
    <td width="33%"><img src="docs/screenshots/ghost-hollow.jpg" alt="Hollow Man"><br><sub><b>The Hollow Man:</b> stare and he vanishes, then he is behind you</sub></td>
    <td width="33%"><img src="docs/screenshots/ghost-cinder.jpg" alt="Cinder Woman"><br><sub><b>The Cinder Woman:</b> crawls closer whenever you look away</sub></td>
    <td width="33%"><img src="docs/screenshots/ghost-patient.jpg" alt="The Patient"><br><sub><b>The Patient:</b> closer every time the lights flicker</sub></td>
  </tr>
</table>

### 🦛 The Stinkers

<img src="docs/screenshots/stinkers.jpg" alt="Hippo, crocodile, gorilla and capybara" width="80%">

> Chunky animals (hippos, crocodiles, gorillas, capybaras) that waddle up, turn around and gas you with toxic green clouds.

---

## ✅ What's in the Unity build

| Area | Status |
|---|---|
| 🏝 Island terrain, water, sky, day/night cycle | ✅ Done |
| 🏘 Old Quarter: 26 enterable buildings, real physics doors, props, lamps | ✅ Done |
| 🌳 Trees, bushes, flowers, rocks, grass, fireflies | ✅ Done |
| 🧍 First and third person player with a sculpted, skinned human avatar | ✅ Done |
| 🔫 6 guns, scopes 2x–6x, ammo, reload, recoil, loot | ✅ Done |
| 🦛 Stinkers with gas clouds and player health and respawn | ✅ Done |
| 👻 3 ghosts, horror night, flashlight, ghost traps and the cave | ✅ Done |
| 🛸 3 hover-craft with crash physics, cannons and rockets | ✅ Done |
| 🎸 Arena with a live band, dancing crowd and haunted night | ✅ Done |
| 🌦 Weather, 🛵 Scooty Boy, 🚛 traffic, menus and map | 🔜 Planned |
| 🤝 Co-op networking | 🔜 Planned |

---

## 🚀 Open the project

**Requirements**
- **Unity 6000.6.8f1** (Unity 6) with the **Universal Render Pipeline**
- A GPU with Vulkan or DX11 support. Runs on integrated graphics.

**Steps**

```bash
git clone https://github.com/NithijaKalpaSri/Game.git
```

1. Open **Unity Hub → Add → Add project from disk** and select the cloned folder.
2. Open the scene `Assets/Scenes/SampleScene`.
3. Press **Play**. A `GameBootstrap` object builds the whole world at runtime.

> ⏳ The first start sculpts the characters and creatures in code, which takes about **15–40 seconds**.
> ⚠️ After editing any script, **stop Play mode and start it again**. The runtime-built scene can break otherwise.

---

## 🎮 Controls

<table>
<tr><td>

**On foot**

| Key | Action |
|---|---|
| `W A S D` | Move |
| `Shift` | Sprint |
| `Space` | Jump |
| Mouse | Look |
| `LMB` / `RMB` | Shoot / aim down sights |
| `R` | Reload |
| `1`–`6`, wheel | Switch weapon |
| `T` | Swap scope |
| `G` (hold) | Aim ghost trap, release to throw |
| `L` | Flashlight |
| `F` | Open doors, board craft |
| `V` | Change camera view |

</td><td>

**In a hover-craft**

| Key | Action |
|---|---|
| `W A S D` | Drive |
| `Shift` | Boost |
| `Space` | Brake |
| `LMB` | Plasma cannons |
| `RMB` | Rocket |
| `V` | Cycle chase cameras |
| `F` | Leave craft |

</td></tr>
</table>

---

## 🗺 The world

<details>
<summary><b>🏘 The Old Quarter</b></summary>

<br>

You spawn in the **South Gate** yard of a dense stone district built for tactical firefights: **Mid**, **Long** (to Site A), **West Alley** and the covered **Tunnel** (to Site B), plus short routes, two market rows and small courtyards with loot.

26 buildings can be entered through real doors. About half are **locked**: shoot or rocket them until they burst apart.

</details>

<details>
<summary><b>🌗 Day and night</b></summary>

<br>

The game clock runs all the time. At night the game turns to horror: near-black fog, flickering lamps and a flashlight. Three ghosts haunt the streets. Bullets pass through them, but a **ghost trap** drags whoever steps on it into a cave. Find the crack of light at the far end to escape.

</details>

<details>
<summary><b>🎸 The Arena concert</b></summary>

<br>

- **4 PM to 8 PM:** a band plays (singer, guitarist, bassist, keys and a stage drummer) while the crowd dances.
- **8 PM to 6 AM:** the band is gone and pale lost fans glide toward you whenever you are not looking.

The drum track is a 3D audio source: loud in the Arena, fading to silence by 80 m, and muffled outside.

</details>

---

## 📁 Project structure

| Path | Contents |
|---|---|
| `Assets/Scripts/Core` | Math, seeded RNG and noise (identical to the original game) |
| `Assets/Scripts/World` | Island data, town builder, terrain, water, doors, nature, grass |
| `Assets/Scripts/Player` | Player controller and the human avatar |
| `Assets/Scripts/Weapons` | Guns, ammo, loot, effects |
| `Assets/Scripts/Critters` | Stinkers and gas clouds |
| `Assets/Scripts/Ghosts` | Ghosts, horror night, ghost traps |
| `Assets/Scripts/Vehicles` | Hover-craft, garage, vehicle weapons |
| `Assets/Scripts/Arena` | Stadium, band, crowd |
| `Assets/Scripts/Organic` | SDF sculpting and skinned mesh generation |
| `Assets/Shaders` | Custom URP shaders: Lit, Plant, Sky, Skin |
| `Assets/Editor` | Editor tools (texture generation, watch layout) |
| `docs/screenshots` | Images used in this README |

---

## 🗓 Roadmap

- [x] World, nature and day/night
- [x] Weapons, animals, ghosts and traps
- [x] Hover-craft and vehicle weapons
- [x] Arena concert
- [ ] Weather system
- [ ] Scooty Boy and traffic
- [ ] Menus, map and settings
- [ ] Audio pass and remaining voice lines
- [ ] Co-op networking

---

## 🧱 Tech

Unity 6 · URP · C# · Unity Input System · procedural meshes and textures generated in code

## 📄 License

Add a `LICENSE` file (for example MIT). Only upload audio, images and voice recordings you have the rights to use.

<div align="center">

⭐ **If you like Hearthvale, star the repo!** ⭐

</div>
