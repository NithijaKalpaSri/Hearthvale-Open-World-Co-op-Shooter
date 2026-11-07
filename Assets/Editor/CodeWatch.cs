#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Hearthvale.EditorTools
{
    /// <summary>
    /// "Code Watch": a window inside Unity that shows the C# script that was changed most recently,
    /// with line numbers, and refreshes by itself whenever a script is saved. Lets you read the code
    /// as it is written. Menu: Hearthvale / Code Watch. Pick any script from the list to pin it.
    /// </summary>
    public sealed class CodeWatch : EditorWindow
    {
        string[] files = new string[0];
        string current, text = "", pinned;
        string[] lines = new string[0];
        Vector2 scroll, listScroll;
        double nextScan;
        DateTime shownStamp;
        GUIStyle code, num, header;
        bool follow = true;

        [MenuItem("Hearthvale/Code Watch")]
        public static void Open()
        {
            var w = GetWindow<CodeWatch>("Code Watch");
            w.minSize = new Vector2(420, 300);
            var main = EditorGUIUtility.GetMainWindowPosition();
            w.position = new Rect(main.x + main.width * 0.02f, main.y + main.height * 0.50f, main.width * 0.40f, main.height * 0.46f);
            w.Show(); w.Scan(true);
        }

        void OnEnable() { EditorApplication.update += Tick; Scan(true); }
        void OnDisable() { EditorApplication.update -= Tick; }

        void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextScan) return;
            nextScan = EditorApplication.timeSinceStartup + 0.7;
            Scan(false);
        }

        void Scan(bool force)
        {
            string root = Path.Combine(Application.dataPath, "Scripts");
            string ed = Path.Combine(Application.dataPath, "Editor");
            var all = new List<string>();
            if (Directory.Exists(root)) all.AddRange(Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories));
            if (Directory.Exists(ed)) all.AddRange(Directory.GetFiles(ed, "*.cs", SearchOption.AllDirectories));
            var sh = Path.Combine(Application.dataPath, "Shaders");
            if (Directory.Exists(sh)) all.AddRange(Directory.GetFiles(sh, "*.shader"));
            files = all.OrderByDescending(f => File.GetLastWriteTime(f)).ToArray();
            string pick = !string.IsNullOrEmpty(pinned) ? pinned : (follow && files.Length > 0 ? files[0] : current);
            if (string.IsNullOrEmpty(pick) || !File.Exists(pick)) return;
            var stamp = File.GetLastWriteTime(pick);
            if (force || pick != current || stamp != shownStamp)
            {
                current = pick; shownStamp = stamp;
                try { text = File.ReadAllText(pick); } catch { text = "(cannot read)"; }
                lines = text.Replace("\r", "").Split('\n');
                if (pick != current || force) scroll = Vector2.zero;
                Repaint();
            }
        }

        void OnGUI()
        {
            if (code == null)
            {
                code = new GUIStyle(EditorStyles.label) { font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New" }, 12), fontSize = 12, richText = false, wordWrap = false };
                code.normal.textColor = new Color(0.86f, 0.9f, 0.86f);
                num = new GUIStyle(code) { alignment = TextAnchor.UpperRight };
                num.normal.textColor = new Color(0.5f, 0.55f, 0.6f);
                header = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                follow = GUILayout.Toggle(follow && string.IsNullOrEmpty(pinned), "Follow newest", EditorStyles.toolbarButton, GUILayout.Width(100));
                if (follow) pinned = null;
                GUILayout.Label(string.IsNullOrEmpty(current) ? "" : Path.GetFileName(current) + "   (" + lines.Length + " lines, saved " + shownStamp.ToString("HH:mm:ss") + ")", header);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open in editor", EditorStyles.toolbarButton, GUILayout.Width(100)) && !string.IsNullOrEmpty(current))
                    UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(current, 1);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (var ls = new EditorGUILayout.ScrollViewScope(listScroll, GUILayout.Width(190)))
                {
                    listScroll = ls.scrollPosition;
                    foreach (var f in files.Take(60))
                    {
                        bool sel = f == current;
                        var rect = GUILayoutUtility.GetRect(180, 18);
                        if (sel) EditorGUI.DrawRect(rect, new Color(0.25f, 0.45f, 0.75f, 0.45f));
                        var age = (DateTime.Now - File.GetLastWriteTime(f)).TotalSeconds;
                        var st = new GUIStyle(EditorStyles.label) { fontSize = 11 };
                        if (age < 120) st.normal.textColor = new Color(1f, 0.9f, 0.45f);
                        if (GUI.Button(rect, Path.GetFileName(f), st)) { pinned = f; follow = false; Scan(true); }
                    }
                }
                using (var sv = new EditorGUILayout.ScrollViewScope(scroll))
                {
                    scroll = sv.scrollPosition;
                    var sb = new System.Text.StringBuilder(); var nb = new System.Text.StringBuilder();
                    int max = Mathf.Min(lines.Length, 1500);
                    for (int i = 0; i < max; i++) { nb.Append(i + 1).Append('\n'); sb.Append(lines[i]).Append('\n'); }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(nb.ToString(), num, GUILayout.Width(42));
                        GUILayout.Label(sb.ToString(), code);
                    }
                }
            }
        }
    }
}
#endif
