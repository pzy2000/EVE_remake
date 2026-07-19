#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Starfall.Editor
{
    /// <summary>
    /// Fast Android player-script compilation used before the full Unity suite.
    /// It deliberately uses the same target and CI-only define as the x86_64
    /// smoke player so player-only compiler errors cannot hide behind green
    /// EditMode and PlayMode runs.
    /// </summary>
    public static class StarfallAndroidPreflight
    {
        private const string ExpectedUnityVersion = "6000.1.10f1";
        private const string CiDefine = "STARFALL_ANDROID_CI";
        private const string OutputArgument = "starfallPreflightOutput";

        public static void Run()
        {
            var stopwatch = Stopwatch.StartNew();
            var outputDirectory = ResolveOutputDirectory();
            var assemblyDirectory = Path.Combine(outputDirectory, "PlayerScripts");
            var reportPath = Path.Combine(outputDirectory, "preflight-report.json");
            Directory.CreateDirectory(outputDirectory);
            if (Directory.Exists(assemblyDirectory)) Directory.Delete(assemblyDirectory, true);
            Directory.CreateDirectory(assemblyDirectory);

            Exception failure = null;
            var assemblyCount = 0;
            try
            {
                if (!string.Equals(Application.unityVersion, ExpectedUnityVersion,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Android preflight requires Unity {ExpectedUnityVersion}; " +
                        $"running {Application.unityVersion}.");
                }

                var settings = new ScriptCompilationSettings
                {
                    target = BuildTarget.Android,
                    group = BuildTargetGroup.Android,
                    options = ScriptCompilationOptions.DevelopmentBuild,
                    extraScriptingDefines = new[] { CiDefine },
                };
                var result = PlayerBuildInterface.CompilePlayerScripts(settings, assemblyDirectory);
                assemblyCount = result.assemblies?.Count ?? 0;
                if (assemblyCount == 0)
                    throw new InvalidOperationException("Android preflight produced no player assemblies.");
                Debug.Log($"[Starfall Android] Preflight compiled {assemblyCount} player assemblies " +
                          $"with {CiDefine}.");
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                File.WriteAllText(reportPath, BuildReportJson(
                    failure == null, assemblyCount, stopwatch.ElapsedMilliseconds, failure));
                Debug.Log($"[Starfall Android] Preflight report: {reportPath}");
            }
        }

        private static string ResolveOutputDirectory()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(arguments[index].TrimStart('-'), OutputArgument,
                        StringComparison.OrdinalIgnoreCase)) continue;
                return Path.GetFullPath(arguments[index + 1]);
            }
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
                "artifacts", "android-preflight"));
        }

        private static string BuildReportJson(
            bool succeeded,
            int assemblyCount,
            long durationMilliseconds,
            Exception failure)
        {
            var builder = new StringBuilder(512);
            builder.Append('{')
                .Append("\"schemaVersion\":1,")
                .Append("\"unityVersion\":\"").Append(Escape(Application.unityVersion)).Append("\",")
                .Append("\"target\":\"Android\",")
                .Append("\"defines\":[\"").Append(CiDefine).Append("\"],")
                .Append("\"developmentBuild\":true,")
                .Append("\"succeeded\":").Append(succeeded ? "true" : "false").Append(',')
                .Append("\"assemblyCount\":").Append(assemblyCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append("\"durationMs\":").Append(durationMilliseconds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append("\"errorType\":");
            AppendNullableString(builder, failure?.GetType().FullName);
            builder.Append(',').Append("\"errorMessage\":");
            AppendNullableString(builder, failure?.Message);
            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendNullableString(StringBuilder builder, string value)
        {
            if (value == null) builder.Append("null");
            else builder.Append('"').Append(Escape(value)).Append('"');
        }

        private static string Escape(string value) => (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }
}
#endif
