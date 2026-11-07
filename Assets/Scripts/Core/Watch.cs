using UnityEngine;

namespace Hearthvale
{
    /// <summary>On-screen narration: shows what is being built/changed so you can follow along in the Game view.</summary>
    public static class Watch
    {
        public static string Message = "";
        public static float Until;
        public static void Say(string msg, float seconds = 6f)
        {
            Message = msg; Until = Time.realtimeSinceStartup + seconds;
            Debug.Log("[Watch] " + msg);
        }
        public static void Draw()
        {
            if (Time.realtimeSinceStartup > Until || string.IsNullOrEmpty(Message)) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            st.normal.textColor = new Color(1f, 0.93f, 0.6f);
            var r = new Rect(14, Screen.height - 56, Screen.width - 28, 40);
            GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 10, r.y, r.width - 20, r.height), "🔧 " + Message, st);
        }
    }
}
