using System.Collections.Generic;
using UnityEngine;

namespace Hearthvale
{
    public sealed class WeaponStats
    {
        public string kind, name, color, sound = null;
        public float dmg, rate, spread, range, reload, zoom, force, kick = -1f;
        public int mag, pellets, start; public bool auto, scopeable;
    }

    /// <summary>Port of the WEAPONS table (weapons.js). Same numbers.</summary>
    public static class WeaponData
    {
        public static readonly string[] SlotOrder = { "pistol", "rifle", "shotgun", "dmr", "sniper", "bolt" };
        public static readonly int[] ScopeLevels = { 2, 3, 4, 6 };
        public const float BaseFov = 75f;
        public static readonly Dictionary<string, WeaponStats> W = new Dictionary<string, WeaponStats>
        {
            { "pistol",  new WeaponStats { kind = "pistol",  name = "Peach Pistol",   dmg = 26,  rate = 0.22f,  mag = 12, auto = false, spread = 0.012f, pellets = 1, range = 160, reload = 1.1f, zoom = 55, color = "#ffb38a", start = 48, force = 6 } },
            { "rifle",   new WeaponStats { kind = "rifle",   name = "Mint Rifle",     dmg = 17,  rate = 0.095f, mag = 30, auto = true,  spread = 0.022f, pellets = 1, range = 220, reload = 1.8f, zoom = 45, color = "#8fe3c1", start = 90, force = 5, scopeable = true } },
            { "shotgun", new WeaponStats { kind = "shotgun", name = "Berry Blaster",  dmg = 11,  rate = 0.75f,  mag = 6,  auto = false, spread = 0.075f, pellets = 9, range = 45,  reload = 2.2f, zoom = 60, color = "#ff8fb1", start = 24, force = 11 } },
            { "dmr",     new WeaponStats { kind = "dmr",     name = "Thistle Marksman", dmg = 52, rate = 0.3f,   mag = 10, auto = false, spread = 0.004f, pellets = 1, range = 420, reload = 2.3f, zoom = 58, color = "#d9c27a", start = 30, force = 9, sound = "sniper", scopeable = true, kick = 0.6f } },
            { "sniper",  new WeaponStats { kind = "sniper",  name = "Lavender Longshot", dmg = 120, rate = 1.2f, mag = 5,  auto = false, spread = 0.0f,   pellets = 1, range = 500, reload = 2.4f, zoom = 58, color = "#b9a5ff", start = 15, force = 15, scopeable = true, kick = 1.4f } },
            { "bolt",    new WeaponStats { kind = "bolt",    name = "Kestrel Bolt-Action", dmg = 170, rate = 1.5f, mag = 5, auto = false, spread = 0.0f,   pellets = 1, range = 650, reload = 2.9f, zoom = 58, color = "#8fb6d9", start = 12, force = 18, sound = "sniper", scopeable = true, kick = 1.6f } },
        };
        public static float FovForZoom(float n) { return Mathf.Rad2Deg * 2f * Mathf.Atan(Mathf.Tan(Mathf.Deg2Rad * BaseFov / 2f) / n); }
    }

    /// <summary>Anything a bullet can hurt (doors, animals, ghosts...). dir is the bullet direction.</summary>
    public interface IShootable
    {
        void OnShot(float damage, Vector3 hitPoint, Vector3 dir, float force);
    }
}
