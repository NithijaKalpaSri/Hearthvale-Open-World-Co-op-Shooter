#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Hearthvale.EditorTools
{
    /// <summary>
    /// Lets you watch the work live: Hierarchy + Inspector + Console stay open, the Scene view
    /// follows the player from behind/above while the game runs, and a second floating Game view
    /// shows exactly what the player sees. Menu: Hearthvale / Watch Layout.
    /// </summary>
    [InitializeOnLoad]
    public static class LiveWatch
    {
        const string FollowKey = "Hearthvale.FollowScene";
        static PlayerController player;
        static bool Follow { get { return EditorPrefs.GetBool(FollowKey, true); } set { EditorPrefs.SetBool(FollowKey, value); } }

        static LiveWatch()
        {
            EditorApplication.update += Tick;
            // keep the game running when you click another window (otherwise the editor pauses the game loop)
            PlayerSettings.runInBackground = true;
            // every time the game starts, put the watch layout up automatically
            EditorApplication.playModeStateChanged += st =>
            {
                if (st == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += () => { player = null; Layout(); };
            };
        }

        static void Tick()
        {
            if (!Application.isPlaying || !Follow) return;
            if (player == null) player = Object.FindAnyObjectByType<PlayerController>();
            var sv = SceneView.lastActiveSceneView;
            if (player == null || sv == null) return;
            Quaternion rot = Quaternion.Euler(24f, player.Yaw * Mathf.Rad2Deg, 0f);
            sv.LookAt(player.transform.position + Vector3.up * 1.4f, rot, 9f, false, true);
        }

        [MenuItem("Hearthvale/Toggle Scene Follow")]
        static void ToggleFollow() { Follow = !Follow; Debug.Log("[Watch] Scene view follow: " + Follow); }

        [MenuItem("Hearthvale/Watch Layout")]
        public static void Layout()
        {
            foreach (var m in new[] { "Window/General/Hierarchy", "Window/General/Inspector", "Window/General/Console", "Window/General/Scene" })
                EditorApplication.ExecuteMenuItem(m);
            if (!EditorWindow.HasOpenInstances<CodeWatch>()) CodeWatch.Open();
            // second, floating Game view so Scene and Game are visible at the same time
            var gvType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            foreach (var w in Resources.FindObjectsOfTypeAll(gvType)) if (w.name == "Game (live)") { var ew = (EditorWindow)w; var mw = EditorGUIUtility.GetMainWindowPosition(); ew.position = new Rect(mw.x + mw.width * 0.42f, mw.y + 70, mw.width * 0.57f, mw.height * 0.60f); EditorWindow.GetWindow<SceneView>().Show(); return; }
            var gv = (EditorWindow)ScriptableObject.CreateInstance(gvType);
            gv.titleContent = new GUIContent("Game (live)");
            gv.name = "Game (live)";
            gv.Show();
            // sit on the right of the Unity window so the Hierarchy, Scene view and Console stay visible
            var main = EditorGUIUtility.GetMainWindowPosition();
            gv.position = new Rect(main.x + main.width * 0.42f, main.y + 70, main.width * 0.57f, main.height * 0.60f);
            EditorWindow.GetWindow<SceneView>().Show(); // make the Scene tab (follow-cam) the visible one
        }
    }
}
#endif
