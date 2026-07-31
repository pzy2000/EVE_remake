using UnityEditor;
using UnityEngine;

namespace Starfall.Presentation.Editor
{
    [InitializeOnLoad]
    internal static class StarfallPlayModeEntry
    {
        private const string BootstrapScenePath = "Assets/Starfall/Scenes/Bootstrap.unity";

        static StarfallPlayModeEntry()
        {
            EditorApplication.delayCall += ConfigurePlayModeEntry;
        }

        [MenuItem("STARFALL/Open Bootstrap Scene")]
        private static void OpenBootstrapScene()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(BootstrapScenePath);
        }

        private static void ConfigurePlayModeEntry()
        {
            // The Test Runner owns its temporary PlayMode entry scene in batch mode.
            if (Application.isBatchMode) return;
            var bootstrap = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath);
            if (bootstrap == null)
            {
                Debug.LogError("STARFALL Bootstrap scene is missing: " + BootstrapScenePath);
                return;
            }

            if (UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene != bootstrap)
                UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = bootstrap;
        }
    }
}
