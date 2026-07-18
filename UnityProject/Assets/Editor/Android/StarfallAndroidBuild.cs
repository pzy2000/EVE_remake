#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starfall.Editor
{
    /// <summary>
    /// Deterministic Android build entry used by GameCI.
    ///
    /// GameCI deliberately skips its built-in AndroidSettings and VersionApplicator
    /// when a custom build method is selected, so every setting used by the build is
    /// applied here and restored before the Editor exits.
    /// </summary>
    public static class StarfallAndroidBuild
    {
        private const string ExpectedUnityVersion = "6000.1.10f1";
        private const string PackageName = "com.pzy.starfallodyssey";
        private const int MinimumSdk = 26;
        private const int TargetSdk = 36;
        private const int MaximumPlayVersionCode = 2_100_000_000;
        private const string SmokeFlavor = "ci-smoke";
        private const string ReleaseExportFlavor = "release-export";
        private const string IconAssetPath =
            "Assets/Starfall/Art/Generated/Thumbnails/acolyte.png";
        private const string GradlePropertiesTemplatePath =
            "Assets/Plugins/Android/gradleTemplate.properties";

        private static readonly Color AndroidBrandBackground =
            new Color32(6, 15, 35, 255);

        private static readonly HashSet<string> SecretArguments = new(StringComparer.Ordinal)
        {
            "androidKeystorePass",
            "androidKeyaliasName",
            "androidKeyaliasPass",
        };

        public static void PerformBuild()
        {
            var arguments = ParseCommandLine();
            var flavor = Require(arguments, "starfallBuildFlavor");
            if (flavor != SmokeFlavor && flavor != ReleaseExportFlavor)
            {
                throw new ArgumentException(
                    $"Unsupported -starfallBuildFlavor '{flavor}'. Expected '{SmokeFlavor}' or '{ReleaseExportFlavor}'.");
            }

            if (!string.Equals(Application.unityVersion, ExpectedUnityVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Android builds require Unity {ExpectedUnityVersion}; running {Application.unityVersion}.");
            }

            ValidateGeneratedAssetsForCi();

            var outputPath = Path.GetFullPath(Require(arguments, "customBuildPath"));
            var versionName = Require(arguments, "buildVersion");
            var versionCode = ParseVersionCode(Require(arguments, "androidVersionCode"));
            var targetSdk = ParseTargetSdk(Require(arguments, "androidTargetSdkVersion"));
            var expectedExportType = flavor == SmokeFlavor ? "androidPackage" : "androidStudioProject";
            var exportType = Require(arguments, "androidExportType");
            if (!string.Equals(exportType, expectedExportType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Flavor '{flavor}' requires -androidExportType {expectedExportType}, received {exportType}.");
            }
            var architecture = flavor == SmokeFlavor
                ? ParseSmokeArchitecture(arguments)
                : "arm64-v8a";

            ValidateOutputPath(flavor, outputPath);
            var enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (enabledScenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes were found in EditorBuildSettings.");
            }

            var snapshot = SettingsSnapshot.Capture();
            var debugSymbols = DebugSymbolsSnapshot.Capture();
            var icons = AndroidIconSnapshot.Capture();
            BuildReport report = null;
            try
            {
                ApplyCommonSettings(versionName, versionCode, targetSdk);
                var options = ConfigureFlavor(
                    flavor,
                    architecture,
                    outputPath,
                    enabledScenes,
                    debugSymbols);
                Debug.Log(
                    $"[Starfall Android] Building flavor={flavor}, version={versionName} ({versionCode}), " +
                    $"targetSdk={TargetSdk}, output={outputPath}");

                report = BuildPipeline.BuildPlayer(options);
                WriteBuildReport(flavor, architecture, outputPath, versionName, versionCode, report);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Android {flavor} build failed: {report.summary.totalErrors} errors, " +
                        $"{report.summary.totalWarnings} warnings.");
                }
            }
            finally
            {
                debugSymbols.Restore();
                icons.Restore();
                snapshot.Restore();
                AssetDatabase.SaveAssets();
            }

            Debug.Log(
                $"[Starfall Android] Build succeeded: flavor={flavor}, " +
                $"bytes={report.summary.totalSize}, duration={report.summary.totalTime}.");
        }

        private static void ValidateGeneratedAssetsForCi()
        {
            const string typeName =
                "Starfall.Presentation.Editor.VisualCatalogBuilder, Starfall.Presentation.Editor";
            var validatorType = Type.GetType(typeName, false);
            var validator = validatorType?.GetMethod(
                "ValidateGeneratedAssetsForCi",
                BindingFlags.Public | BindingFlags.Static);
            if (validator == null)
            {
                throw new MissingMethodException(
                    "Could not resolve VisualCatalogBuilder.ValidateGeneratedAssetsForCi().");
            }

            try
            {
                validator.Invoke(null, null);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw new InvalidOperationException(
                    "Generated visual assets are stale or invalid. Rebuild and commit them before Android CI.",
                    exception.InnerException);
            }
        }

        private static BuildPlayerOptions ConfigureFlavor(
            string flavor,
            string architecture,
            string outputPath,
            string[] enabledScenes,
            DebugSymbolsSnapshot debugSymbols)
        {
            var isSmoke = flavor == SmokeFlavor;
            PlayerSettings.Android.targetArchitectures =
                isSmoke && architecture == "x86_64"
                    ? AndroidArchitecture.X86_64
                    : AndroidArchitecture.ARM64;
            PlayerSettings.Android.useCustomKeystore = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.androidBuildType =
                isSmoke ? AndroidBuildType.Development : AndroidBuildType.Release;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = !isSmoke;
            EditorUserBuildSettings.buildAppBundle = false;
            debugSymbols.Set(
                isSmoke ? "None" : "SymbolTable",
                "Zip, LegacyExtensions");

            var buildOptions = BuildOptions.DetailedBuildReport;
            if (isSmoke)
            {
                buildOptions |= BuildOptions.Development | BuildOptions.AllowDebugging;
            }
            else
            {
                buildOptions |= BuildOptions.AcceptExternalModificationsToPlayer;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException(
                $"Could not determine output directory for {outputPath}."));

            return new BuildPlayerOptions
            {
                scenes = enabledScenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = buildOptions,
                extraScriptingDefines = isSmoke ? new[] { "STARFALL_ANDROID_CI" } : Array.Empty<string>(),
            };
        }

        private static void ApplyCommonSettings(
            string versionName,
            int versionCode,
            AndroidSdkVersions targetSdk)
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.bundleVersion = versionName;
            PlayerSettings.Android.bundleVersionCode = versionCode;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = targetSdk;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.Android.buildApkPerCpuArchitecture = false;
            PlayerSettings.Android.resizeableActivity = true;
            PlayerSettings.Android.predictiveBackSupport = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.fullscreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.Android.minAspectRatio = 1f;
            PlayerSettings.Android.maxAspectRatio = 2.4f;
            PlayerSettings.Android.minifyDebug = false;
            PlayerSettings.Android.minifyRelease = false;
            ValidateGradlePropertiesTemplate();
            ConfigureAndroidBranding();

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
        }

        private static void ConfigureAndroidBranding()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(IconAssetPath);
            var splashSprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconAssetPath);
            if (texture == null)
            {
                throw new InvalidOperationException(
                    $"Android icon source is missing or is not imported as Texture2D: {IconAssetPath}");
            }
            if (splashSprite == null)
            {
                throw new InvalidOperationException(
                    $"Android splash source is missing or is not imported as Sprite: {IconAssetPath}");
            }

            var kinds = PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android);
            if (kinds == null || kinds.Length < 3)
            {
                throw new InvalidOperationException(
                    "Android legacy, round, and adaptive icon kinds are required, but Unity returned fewer than three kinds.");
            }

            foreach (var kind in kinds)
            {
                var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                if (slots == null || slots.Length == 0)
                {
                    throw new InvalidOperationException($"Android icon kind '{kind}' exposes no icon slots.");
                }

                foreach (var slot in slots)
                {
                    if (slot.maxLayerCount <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Android icon slot {slot.width}x{slot.height} for '{kind}' has no texture layers.");
                    }

                    // Adaptive icons expose foreground/background layers. Unity requires
                    // both to be project Texture2D assets, so the checked-in branded art
                    // is assigned to every required layer. The Android 12 launch surface
                    // and Unity splash use the fixed deep-blue brand background below.
                    var layers = Enumerable.Repeat(texture, slot.maxLayerCount).ToArray();
                    slot.SetTextures(layers);
                }

                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
            }

            PlayerSettings.SplashScreen.background = splashSprite;
            PlayerSettings.SplashScreen.backgroundColor = AndroidBrandBackground;
        }

        private static void ValidateGradlePropertiesTemplate()
        {
            if (!File.Exists(GradlePropertiesTemplatePath))
            {
                throw new FileNotFoundException(
                    "The checked-in Android Gradle properties template is missing.",
                    GradlePropertiesTemplatePath);
            }
            var template = File.ReadAllText(GradlePropertiesTemplatePath);
            if (!template.Contains("**ADDITIONAL_PROPERTIES**", StringComparison.Ordinal) ||
                !template.Contains("android.useAndroidX=true", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Android Gradle properties template must preserve Unity additions and enable AndroidX.");
            }
        }

        private static Dictionary<string, string> ParseCommandLine()
        {
            var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
            var args = Environment.GetCommandLineArgs();
            for (var index = 0; index < args.Length; index++)
            {
                if (!args[index].StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }

                var key = args[index].TrimStart('-');
                var value = index + 1 < args.Length && !args[index + 1].StartsWith("-", StringComparison.Ordinal)
                    ? args[++index]
                    : string.Empty;
                parsed[key] = value;

                if (!SecretArguments.Contains(key))
                {
                    Debug.Log($"[Starfall Android] Argument {key}={value}");
                }
            }

            return parsed;
        }

        private static string Require(IReadOnlyDictionary<string, string> arguments, string key)
        {
            if (!arguments.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"Missing required command-line argument -{key}.");
            }

            return value.Trim();
        }

        private static int ParseVersionCode(string value)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var versionCode) ||
                versionCode <= 0 || versionCode >= MaximumPlayVersionCode)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"Android versionCode must be between 1 and {MaximumPlayVersionCode - 1}.");
            }

            return versionCode;
        }

        private static AndroidSdkVersions ParseTargetSdk(string value)
        {
            if (!Enum.TryParse(value, false, out AndroidSdkVersions parsed) || (int)parsed != TargetSdk)
            {
                throw new ArgumentException(
                    $"Android target SDK must be AndroidApiLevel{TargetSdk}; received '{value}'.");
            }

            return parsed;
        }

        private static string ParseSmokeArchitecture(IReadOnlyDictionary<string, string> arguments)
        {
            if (!arguments.TryGetValue("starfallSmokeArchitecture", out var architecture) ||
                string.IsNullOrWhiteSpace(architecture))
            {
                return "x86_64";
            }

            architecture = architecture.Trim();
            if (architecture != "x86_64" && architecture != "arm64-v8a")
            {
                throw new ArgumentException(
                    $"Unsupported -starfallSmokeArchitecture '{architecture}'. " +
                    "Expected x86_64 or arm64-v8a.");
            }

            return architecture;
        }

        private static void ValidateOutputPath(string flavor, string outputPath)
        {
            if (flavor == SmokeFlavor && !outputPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"ci-smoke output must be an .apk file: {outputPath}");
            }

            if (flavor == ReleaseExportFlavor && Path.HasExtension(outputPath))
            {
                throw new InvalidOperationException(
                    $"release-export output must be a Gradle project directory without an extension: {outputPath}");
            }

            if (File.Exists(outputPath) || Directory.Exists(outputPath))
            {
                throw new IOException(
                    $"Build output already exists. CI must provide a clean output path: {outputPath}");
            }
        }

        private static void WriteBuildReport(
            string flavor,
            string architecture,
            string outputPath,
            string versionName,
            int versionCode,
            BuildReport report)
        {
            var reportPath = Path.Combine(
                Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException(
                    $"Could not determine report directory for {outputPath}."),
                Path.GetFileNameWithoutExtension(outputPath) + ".build-report.json");
            var payload = new BuildReportPayload
            {
                unityVersion = Application.unityVersion,
                flavor = flavor,
                packageName = PackageName,
                versionName = versionName,
                versionCode = versionCode,
                minSdk = MinimumSdk,
                targetSdk = TargetSdk,
                architecture = architecture,
                graphicsApis = "Vulkan,OpenGLES3",
                gradlePropertiesTemplate = GradlePropertiesTemplatePath,
                androidLibraryManifest =
                    "Assets/Plugins/Android/StarfallMobile.androidlib/src/main/AndroidManifest.xml",
                outputPath = outputPath,
                result = report.summary.result.ToString(),
                totalBytes = report.summary.totalSize.ToString(CultureInfo.InvariantCulture),
                totalSeconds = report.summary.totalTime.TotalSeconds,
                totalErrors = report.summary.totalErrors,
                totalWarnings = report.summary.totalWarnings,
            };
            File.WriteAllText(reportPath, JsonUtility.ToJson(payload, true));
        }

        [Serializable]
        private sealed class BuildReportPayload
        {
            public string unityVersion;
            public string flavor;
            public string packageName;
            public string versionName;
            public int versionCode;
            public int minSdk;
            public int targetSdk;
            public string architecture;
            public string graphicsApis;
            public string gradlePropertiesTemplate;
            public string androidLibraryManifest;
            public string outputPath;
            public string result;
            public string totalBytes;
            public double totalSeconds;
            public int totalErrors;
            public int totalWarnings;
        }

        private sealed class DebugSymbolsSnapshot
        {
            private readonly PropertyInfo levelProperty;
            private readonly PropertyInfo formatProperty;
            private readonly object originalLevel;
            private readonly object originalFormat;

            private DebugSymbolsSnapshot(
                PropertyInfo levelProperty,
                PropertyInfo formatProperty,
                object originalLevel,
                object originalFormat)
            {
                this.levelProperty = levelProperty;
                this.formatProperty = formatProperty;
                this.originalLevel = originalLevel;
                this.originalFormat = originalFormat;
            }

            public static DebugSymbolsSnapshot Capture()
            {
                var debugSymbolsType = Type.GetType(
                    "UnityEditor.Android.UserBuildSettings+DebugSymbols, UnityEditor.Android.Extensions");
                var level = debugSymbolsType?.GetProperty(
                    "level", BindingFlags.Public | BindingFlags.Static);
                var format = debugSymbolsType?.GetProperty(
                    "format", BindingFlags.Public | BindingFlags.Static);
                if (level == null || format == null || !level.CanRead || !level.CanWrite ||
                    !format.CanRead || !format.CanWrite || !level.PropertyType.IsEnum ||
                    !format.PropertyType.IsEnum)
                {
                    throw new MissingMemberException(
                        "Unity Android debug-symbol level/format APIs are unavailable; " +
                        "refusing to produce a release without deterministic symbols.");
                }

                return new DebugSymbolsSnapshot(
                    level,
                    format,
                    level.GetValue(null),
                    format.GetValue(null));
            }

            public void Set(string levelName, string formatNames)
            {
                levelProperty.SetValue(null, ParseEnum(levelProperty, levelName));
                formatProperty.SetValue(null, ParseEnum(formatProperty, formatNames));
            }

            public void Restore()
            {
                levelProperty.SetValue(null, originalLevel);
                formatProperty.SetValue(null, originalFormat);
            }

            private static object ParseEnum(PropertyInfo property, string names)
            {
                try
                {
                    return Enum.Parse(property.PropertyType, names, false);
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidOperationException(
                        $"Could not set Android debug-symbol {property.Name} to '{names}'.",
                        exception);
                }
            }
        }

        private sealed class AndroidIconSnapshot
        {
            private readonly List<IconKindSnapshot> kinds;

            private AndroidIconSnapshot(List<IconKindSnapshot> kinds)
            {
                this.kinds = kinds;
            }

            public static AndroidIconSnapshot Capture()
            {
                var captured = new List<IconKindSnapshot>();
                var supportedKinds = PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android);
                if (supportedKinds == null)
                {
                    return new AndroidIconSnapshot(captured);
                }

                foreach (var kind in supportedKinds)
                {
                    var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                    var textures = slots
                        .Select(slot => Enumerable.Range(0, slot.maxLayerCount)
                            .Select(slot.GetTexture)
                            .ToArray())
                        .ToArray();
                    captured.Add(new IconKindSnapshot(kind, textures));
                }

                return new AndroidIconSnapshot(captured);
            }

            public void Restore()
            {
                foreach (var capturedKind in kinds)
                {
                    var slots = PlayerSettings.GetPlatformIcons(
                        NamedBuildTarget.Android,
                        capturedKind.Kind);
                    if (slots.Length != capturedKind.Textures.Length)
                    {
                        Debug.LogWarning(
                            $"[Starfall Android] Cannot restore icon kind '{capturedKind.Kind}': " +
                            "Unity returned a different slot count.");
                        continue;
                    }

                    for (var index = 0; index < slots.Length; index++)
                    {
                        slots[index].SetTextures(capturedKind.Textures[index]);
                    }

                    PlayerSettings.SetPlatformIcons(
                        NamedBuildTarget.Android,
                        capturedKind.Kind,
                        slots);
                }
            }

            private sealed class IconKindSnapshot
            {
                public IconKindSnapshot(PlatformIconKind kind, Texture2D[][] textures)
                {
                    Kind = kind;
                    Textures = textures;
                }

                public PlatformIconKind Kind { get; }
                public Texture2D[][] Textures { get; }
            }
        }

        private sealed class SettingsSnapshot
        {
            private readonly string applicationIdentifier;
            private readonly ScriptingImplementation scriptingBackend;
            private readonly string bundleVersion;
            private readonly int bundleVersionCode;
            private readonly AndroidSdkVersions minSdkVersion;
            private readonly AndroidSdkVersions targetSdkVersion;
            private readonly AndroidArchitecture targetArchitectures;
            private readonly AndroidApplicationEntry applicationEntry;
            private readonly bool buildApkPerCpuArchitecture;
            private readonly bool resizeableActivity;
            private readonly bool predictiveBackSupport;
            private readonly bool renderOutsideSafeArea;
            private readonly bool startInFullscreen;
            private readonly FullScreenMode fullscreenMode;
            private readonly float minAspectRatio;
            private readonly float maxAspectRatio;
            private readonly bool minifyDebug;
            private readonly bool minifyRelease;
            private readonly bool useCustomKeystore;
            private readonly UIOrientation interfaceOrientation;
            private readonly bool portrait;
            private readonly bool portraitUpsideDown;
            private readonly bool landscapeLeft;
            private readonly bool landscapeRight;
            private readonly bool useDefaultGraphicsApis;
            private readonly GraphicsDeviceType[] graphicsApis;
            private readonly AndroidBuildSystem androidBuildSystem;
            private readonly AndroidBuildType androidBuildType;
            private readonly bool exportAsGoogleAndroidProject;
            private readonly bool buildAppBundle;
            private readonly Sprite splashBackground;
            private readonly Color splashBackgroundColor;

            private SettingsSnapshot()
            {
                applicationIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
                scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
                bundleVersion = PlayerSettings.bundleVersion;
                bundleVersionCode = PlayerSettings.Android.bundleVersionCode;
                minSdkVersion = PlayerSettings.Android.minSdkVersion;
                targetSdkVersion = PlayerSettings.Android.targetSdkVersion;
                targetArchitectures = PlayerSettings.Android.targetArchitectures;
                applicationEntry = PlayerSettings.Android.applicationEntry;
                buildApkPerCpuArchitecture = PlayerSettings.Android.buildApkPerCpuArchitecture;
                resizeableActivity = PlayerSettings.Android.resizeableActivity;
                predictiveBackSupport = PlayerSettings.Android.predictiveBackSupport;
                renderOutsideSafeArea = PlayerSettings.Android.renderOutsideSafeArea;
                startInFullscreen = PlayerSettings.Android.startInFullscreen;
                fullscreenMode = PlayerSettings.Android.fullscreenMode;
                minAspectRatio = PlayerSettings.Android.minAspectRatio;
                maxAspectRatio = PlayerSettings.Android.maxAspectRatio;
                minifyDebug = PlayerSettings.Android.minifyDebug;
                minifyRelease = PlayerSettings.Android.minifyRelease;
                useCustomKeystore = PlayerSettings.Android.useCustomKeystore;
                interfaceOrientation = PlayerSettings.defaultInterfaceOrientation;
                portrait = PlayerSettings.allowedAutorotateToPortrait;
                portraitUpsideDown = PlayerSettings.allowedAutorotateToPortraitUpsideDown;
                landscapeLeft = PlayerSettings.allowedAutorotateToLandscapeLeft;
                landscapeRight = PlayerSettings.allowedAutorotateToLandscapeRight;
                useDefaultGraphicsApis = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android);
                graphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
                androidBuildSystem = EditorUserBuildSettings.androidBuildSystem;
                androidBuildType = EditorUserBuildSettings.androidBuildType;
                exportAsGoogleAndroidProject = EditorUserBuildSettings.exportAsGoogleAndroidProject;
                buildAppBundle = EditorUserBuildSettings.buildAppBundle;
                splashBackground = PlayerSettings.SplashScreen.background;
                splashBackgroundColor = PlayerSettings.SplashScreen.backgroundColor;
            }

            public static SettingsSnapshot Capture() => new();

            public void Restore()
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, applicationIdentifier);
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, scriptingBackend);
                PlayerSettings.bundleVersion = bundleVersion;
                PlayerSettings.Android.bundleVersionCode = bundleVersionCode;
                PlayerSettings.Android.minSdkVersion = minSdkVersion;
                PlayerSettings.Android.targetSdkVersion = targetSdkVersion;
                PlayerSettings.Android.targetArchitectures = targetArchitectures;
                PlayerSettings.Android.applicationEntry = applicationEntry;
                PlayerSettings.Android.buildApkPerCpuArchitecture = buildApkPerCpuArchitecture;
                PlayerSettings.Android.resizeableActivity = resizeableActivity;
                PlayerSettings.Android.predictiveBackSupport = predictiveBackSupport;
                PlayerSettings.Android.renderOutsideSafeArea = renderOutsideSafeArea;
                PlayerSettings.Android.startInFullscreen = startInFullscreen;
                PlayerSettings.Android.fullscreenMode = fullscreenMode;
                PlayerSettings.Android.minAspectRatio = minAspectRatio;
                PlayerSettings.Android.maxAspectRatio = maxAspectRatio;
                PlayerSettings.Android.minifyDebug = minifyDebug;
                PlayerSettings.Android.minifyRelease = minifyRelease;
                PlayerSettings.Android.useCustomKeystore = useCustomKeystore;
                PlayerSettings.defaultInterfaceOrientation = interfaceOrientation;
                PlayerSettings.allowedAutorotateToPortrait = portrait;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = portraitUpsideDown;
                PlayerSettings.allowedAutorotateToLandscapeLeft = landscapeLeft;
                PlayerSettings.allowedAutorotateToLandscapeRight = landscapeRight;
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, useDefaultGraphicsApis);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, graphicsApis);
                EditorUserBuildSettings.androidBuildSystem = androidBuildSystem;
                EditorUserBuildSettings.androidBuildType = androidBuildType;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = exportAsGoogleAndroidProject;
                EditorUserBuildSettings.buildAppBundle = buildAppBundle;
                PlayerSettings.SplashScreen.background = splashBackground;
                PlayerSettings.SplashScreen.backgroundColor = splashBackgroundColor;
            }
        }
    }
}
#endif
