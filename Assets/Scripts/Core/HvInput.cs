using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hearthvale
{
    /// <summary>
    /// One place that reads keyboard + mouse by name ("W", "Shift", "Space", "R", "1".."6", "LMB", "RMB").
    /// Automated tests can inject keys through testKeys / testHits (the Input System ignores injected
    /// events while the editor window is unfocused).
    /// </summary>
    public static class HvInput
    {
        public static readonly HashSet<string> testKeys = new HashSet<string>();
        public static readonly HashSet<string> testHits = new HashSet<string>();
        public static float testWheel;
        /// <summary>False while a menu / free cursor is up: nothing should react to the mouse then.</summary>
        public static bool Active = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { testKeys.Clear(); testHits.Clear(); testWheel = 0; Active = true; }

        static bool TryKey(string n, out Key k)
        {
            if (n == "Shift") { k = Key.LeftShift; return true; }
            if (n.Length == 1 && n[0] >= '0' && n[0] <= '9') { k = (Key)Enum.Parse(typeof(Key), "Digit" + n); return true; }
            return Enum.TryParse(n, out k);
        }

        public static bool Down(string n)
        {
            if (testKeys.Contains(n)) return true;
            var ms = Mouse.current; var kb = Keyboard.current;
            if (n == "LMB") return ms != null && ms.leftButton.isPressed;
            if (n == "RMB") return ms != null && ms.rightButton.isPressed;
            Key k; if (kb != null && TryKey(n, out k)) return kb[k].isPressed;
            return false;
        }

        public static bool Hit(string n)
        {
            if (testHits.Remove(n)) return true;
            var ms = Mouse.current; var kb = Keyboard.current;
            if (n == "LMB") return ms != null && ms.leftButton.wasPressedThisFrame;
            if (n == "RMB") return ms != null && ms.rightButton.wasPressedThisFrame;
            Key k; if (kb != null && TryKey(n, out k)) return kb[k].wasPressedThisFrame;
            return false;
        }

        /// <summary>+1 / -1 for one wheel notch (down = +1, like the browser), 0 if none.</summary>
        public static int WheelDir()
        {
            if (testWheel != 0) { int d = testWheel > 0 ? 1 : -1; testWheel = 0; return d; }
            var ms = Mouse.current; if (ms == null) return 0;
            float y = ms.scroll.ReadValue().y;
            return y > 0.5f ? -1 : y < -0.5f ? 1 : 0;
        }
    }
}
