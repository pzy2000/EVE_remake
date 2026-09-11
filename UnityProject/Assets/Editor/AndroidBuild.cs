using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Command-line entry point for headless Android builds.
// Usage (from build_android.sh):
//   Unity -batchmode -quit -nographics -projectPath <proj>
//     -executeMethod AndroidBuild.Build -apkPath <out.apk> -logFile <log>
public static class AndroidBuild
{
    public static void Build()
    {
        string apkPath = GetArg("-apkPath") ?? Path.Combine("build", "android", "app.apk");
        apkPath = Path.GetFullPath(apkPath);
        Directory.CreateDirectory(Path.GetDirectoryName(apkPath));

        ConfigureExternalTools();
        EnsureMainScene();

        var scenes = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled) scenes.Add(s.path);

        Debug.Log($"APPLICATION_ID={Application.identifier}");
        Debug.Log($"UNITY_BUILD_START target=Android scenes=[{string.Join(",", scenes)}] output={apkPath}");

        BuildPlayerOptions opts = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = apkPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"UNITY_BUILD_OK size={report.summary.totalSize} output={report.summary.outputPath}");
        }
        else
        {
            Debug.LogError($"UNITY_BUILD_FAILED result={report.summary.result} errors={report.summary.totalErrors}");
            EditorApplication.Exit(1);
        }
    }

    // This pipeline does NOT install Unity's embedded SDK/NDK/JDK modules.
    // Point Unity at the standalone toolchain instead (keys mirror the
    // Preferences > External Tools checkboxes; see build_android.sh for paths).
    static void ConfigureExternalTools()
    {
        string sdk = GetArg("-sdkRoot") ?? Environment.GetEnvironmentVariable("ANDROID_HOME");
        string ndk = GetArg("-ndkRoot") ?? Environment.GetEnvironmentVariable("ANDROID_NDK_HOME");
        string jdk = GetArg("-jdkRoot") ?? Environment.GetEnvironmentVariable("JAVA_HOME");

        if (!string.IsNullOrEmpty(sdk) && Directory.Exists(sdk))
        {
            EditorPrefs.SetString("AndroidSdkRoot", sdk);
            EditorPrefs.SetInt("AndroidSdkUseEmbedded", 0);
            UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath = sdk;
            Debug.Log($"External SDK: {sdk}");
        }
        if (!string.IsNullOrEmpty(ndk) && Directory.Exists(ndk))
        {
            EditorPrefs.SetString("AndroidNdkRoot", ndk);
            EditorPrefs.SetInt("AndroidNdkUseEmbedded", 0);
            UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath = ndk;
            Debug.Log($"External NDK: {ndk}");
        }
        if (!string.IsNullOrEmpty(jdk) && Directory.Exists(jdk))
        {
            EditorPrefs.SetString("JdkPath", jdk);
            EditorPrefs.SetInt("JdkUseEmbedded", 0);
            UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath = jdk;
            Debug.Log($"External JDK: {jdk}");
        }
    }

    // A brand-new project has no scenes, and BuildPlayer requires at least one.
    // Creates a minimal "Hello from Unity" scene so the pipeline is verifiable end to end.
    static void EnsureMainScene()
    {
        if (EditorBuildSettings.scenes.Length > 0) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var textGo = new GameObject("HelloText");
        var tm = textGo.AddComponent<TextMesh>();
        tm.text = "Hello from Unity 6!";
        tm.fontSize = 64;
        tm.characterSize = 0.25f;
        tm.anchor = TextAnchor.MiddleCenter;
        textGo.transform.position = new Vector3(0f, 1f, 0f);

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
        Debug.Log("Created default scene Assets/Scenes/Main.unity");
    }

    static string GetArg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
