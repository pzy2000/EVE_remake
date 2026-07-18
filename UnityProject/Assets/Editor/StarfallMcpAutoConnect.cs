#if UNITY_EDITOR
using System;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace Starfall.Editor
{
    /// <summary>
    /// Restores the project-scoped Unity MCP bridge after the Editor starts or
    /// reloads scripts. The server lifecycle remains owned by MCP for Unity.
    /// </summary>
    [InitializeOnLoad]
    internal static class StarfallMcpAutoConnect
    {
        private static bool connectionAttempted;

        static StarfallMcpAutoConnect()
        {
            EditorApplication.delayCall += ConnectOnce;
        }

        private static async void ConnectOnce()
        {
            if (Application.isBatchMode || connectionAttempted)
            {
                return;
            }

            connectionAttempted = true;

            try
            {
                var bridge = MCPServiceLocator.Bridge;
                if (!bridge.IsRunning)
                {
                    bool connected = await bridge.StartAsync();
                    Debug.Log($"[Starfall] Unity MCP auto-connect: {(connected ? "connected" : "server unavailable")}");
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Starfall] Unity MCP auto-connect failed: {exception.Message}");
            }
        }
    }
}
#endif
