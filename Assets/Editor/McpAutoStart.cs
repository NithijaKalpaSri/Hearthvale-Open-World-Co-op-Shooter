#if UNITY_EDITOR
using UnityEditor;

namespace Hearthvale.EditorTools
{
    /// <summary>
    /// Makes MCP for Unity reconnect by itself every time the editor starts or scripts reload,
    /// so Claude can reach Unity without anyone clicking "Start Session" in the MCP window.
    /// </summary>
    [InitializeOnLoad]
    public static class McpAutoStart
    {
        static McpAutoStart()
        {
            if (!EditorPrefs.GetBool("MCPForUnity.AutoStartOnLoad", false))
                EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", true);
        }
    }
}
#endif
