using UnityEngine;

namespace Hearthvale
{
    /// <summary>
    /// GTA-style "WASTED": the moment you die the game drops into slow motion, the picture goes grey and dark with a vignette,
    /// letter-box bars slide in and a big red WASTED stamps onto the screen. Everything clears when you respawn.
    /// </summary>
    public sealed class Wasted : MonoBehaviour
    {
        public PlayerController player; float t; bool was; GUIStyle big, small; Texture2D vig;

        void Update()
        {
            bool dead = player != null && player.dead;
            if (dead && !was) { t = 0f; Time.timeScale = 0.25f; }
            if (dead)
            {
                t += Time.unscaledDeltaTime;
                Time.timeScale = Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - 1.6f) / 1.2f)));   // slow motion first, then back to normal speed
            }
            else if (was) Time.timeScale = 1f;
            was = dead;
        }
        void OnDisable() { Time.timeScale = 1f; }

        void OnGUI()
        {
            if (t <= 0f || player == null || !player.dead) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }; small = new GUIStyle(big) { fontSize = 20, fontStyle = FontStyle.Normal };
                int n = 128; vig = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp }; var px = new Color32[n * n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1; float d = Mathf.Sqrt(dx * dx + dy * dy); px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1.35f, d)) * 255)); }
                vig.SetPixels32(px); vig.Apply();
            }
            float W = Screen.width, H = Screen.height, k = Mathf.Clamp01(t / 1.0f), tx = Mathf.Clamp01((t - 0.5f) / 0.7f);
            GUI.color = new Color(0.58f, 0.6f, 0.64f, 0.42f * k); GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);        // washed-out grey
            GUI.color = new Color(0.02f, 0.02f, 0.04f, 0.85f * k); GUI.DrawTexture(new Rect(0, 0, W, H), vig);                          // dark edges
            float bar = H * 0.1f * k; GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, W, bar), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(0, H - bar, W, bar), Texture2D.whiteTexture);
            big.fontSize = Mathf.RoundToInt(H * 0.17f * Mathf.Lerp(1.35f, 1f, Mathf.SmoothStep(0, 1, tx)));
            var r = new Rect(0, H * 0.5f - H * 0.14f, W, H * 0.28f);
            GUI.color = new Color(0, 0, 0, 0.7f * tx); GUI.Label(new Rect(r.x + 4, r.y + 5, r.width, r.height), "wasted", big);
            GUI.color = new Color(0.78f, 0.06f, 0.07f, tx); GUI.Label(r, "wasted", big);
            float rt = Mathf.Max(0f, 5f - Mathf.Max(0, t - 1.6f) * 1f);
            GUI.color = new Color(1, 1, 1, 0.7f * tx); GUI.Label(new Rect(0, H * 0.5f + H * 0.13f, W, 30), "respawning at the South Gate…", small);
            GUI.color = Color.white;
        }
    }
}
