#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Starfall.Editor
{
    /// <summary>
    /// Binds Android scenes to their density-aware panel before UIDocument is
    /// enabled in the player. Swapping PanelSettings from a controller's
    /// OnEnable can leave UI geometry alive on a detached panel: layout evidence
    /// still looks valid while neither rendering nor hit-testing reaches it.
    /// </summary>
    public sealed class StarfallAndroidSceneProcessor : IProcessSceneWithReport
    {
        internal const string AndroidPanelSettingsPath =
            "Assets/Resources/StarfallAndroidPanelSettings.asset";

        public int callbackOrder => -1000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || report.summary.platform != BuildTarget.Android) return;

            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(AndroidPanelSettingsPath);
            if (panelSettings == null)
            {
                throw new BuildFailedException(
                    $"Android PanelSettings are missing at {AndroidPanelSettingsPath}.");
            }

            var documents = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<UIDocument>(true))
                .ToArray();
            foreach (var document in documents) document.panelSettings = panelSettings;

            if (documents.Length > 0)
            {
                Debug.Log($"[Starfall Android] Bound {documents.Length} UIDocument(s) in " +
                          $"scene '{scene.name}' before player serialization.");
            }
        }
    }
}
#endif
