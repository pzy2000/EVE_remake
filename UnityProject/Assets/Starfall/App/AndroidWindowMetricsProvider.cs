using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Starfall.UI;
using UnityEngine;

namespace Starfall.App
{
    public sealed class AndroidWindowMetricsProvider : IWindowMetricsProvider, IDisposable
    {
        private IReadOnlyList<FoldingFeatureInfo> foldingFeatures = Array.Empty<FoldingFeatureInfo>();
        private int nativeWidth;
        private int nativeHeight;
        private float nativeDensityDpi;
        private RectInt? nativeSafeArea;

        public AndroidWindowMetricsProvider()
        {
            AndroidPlatformBridge.WindowLayoutInfoReceived += OnWindowLayoutInfo;
        }

        public void Dispose()
        {
            AndroidPlatformBridge.WindowLayoutInfoReceived -= OnWindowLayoutInfo;
        }

        public MobileWindowMetrics GetMetrics()
        {
            var width = nativeWidth > 0 ? nativeWidth : Mathf.Max(1, Screen.width);
            var height = nativeHeight > 0 ? nativeHeight : Mathf.Max(1, Screen.height);
            var safe = nativeSafeArea ?? ScreenSafeArea(width, height);
            return new MobileWindowMetrics(width, height,
                nativeDensityDpi > 0f ? nativeDensityDpi : Screen.dpi,
                safe, foldingFeatures);
        }

        public bool TryApplyJson(string json, out string error)
        {
            error = string.Empty;
            try
            {
                var root = JObject.Parse(json ?? string.Empty);
                var parsedWidth = ReadInt(root, "widthPx", "width");
                var parsedHeight = ReadInt(root, "heightPx", "height");
                if (parsedWidth < 0 || parsedHeight < 0)
                    throw new InvalidDataException("Android window dimensions cannot be negative.");
                if ((parsedWidth > 0) != (parsedHeight > 0))
                    throw new InvalidDataException("Android window width and height must be supplied together.");

                var candidateWidth = parsedWidth > 0 ? parsedWidth : nativeWidth;
                var candidateHeight = parsedHeight > 0 ? parsedHeight : nativeHeight;
                var parsedDpi = ReadFloat(root, "densityDpi", "density");
                var candidateDpi = parsedDpi > 0f ? parsedDpi : nativeDensityDpi;
                var candidateSafeArea = nativeSafeArea;

                var safeToken = root["safeArea"] as JObject;
                if (parsedWidth > 0 && safeToken == null)
                    throw new InvalidDataException("Android window metrics must include a safe area.");
                if (safeToken != null && parsedWidth > 0)
                {
                    var safe = ReadRect(safeToken);
                    ValidateRectWithinWindow(safe, candidateWidth, candidateHeight, "safe area");
                    var origin = root.Value<string>("safeAreaOrigin") ??
                                 safeToken.Value<string>("origin") ??
                                 root.Value<string>("origin") ??
                                 "top-left";
                    if (string.Equals(origin, "top-left", StringComparison.OrdinalIgnoreCase))
                        safe.y = candidateHeight - safe.yMax;
                    else if (!string.Equals(origin, "bottom-left", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Unsupported Android safe-area origin: " + origin);
                    candidateSafeArea = safe;
                }

                var features = new List<FoldingFeatureInfo>();
                var array = root["foldingFeatures"] as JArray ?? root["folds"] as JArray;
                if (array != null)
                {
                    foreach (var token in array)
                    {
                        if (!(token is JObject feature)) continue;
                        var bounds = ReadRect(feature["bounds"] as JObject ?? feature);
                        var orientation = ParseOrientation(feature.Value<string>("orientation"));
                        ValidateFoldingExtent(bounds, orientation);
                        if (candidateWidth > 0 && candidateHeight > 0)
                            ValidateFoldingFeatureWithinWindow(bounds, candidateWidth, candidateHeight);
                        features.Add(new FoldingFeatureInfo(
                            bounds,
                            orientation,
                            ParseState(feature.Value<string>("state")),
                            ParseOcclusion(feature.Value<string>("occlusion")),
                            feature.Value<bool?>("separating") ?? feature.Value<bool?>("isSeparating") ?? false));
                    }
                }

                nativeWidth = candidateWidth;
                nativeHeight = candidateHeight;
                nativeDensityDpi = candidateDpi;
                nativeSafeArea = candidateSafeArea;
                foldingFeatures = features;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private void OnWindowLayoutInfo(string json)
        {
            if (!TryApplyJson(json, out var error))
                Debug.LogWarning("Ignored invalid Android window metrics: " + error);
#if STARFALL_ANDROID_CI
            else
                WriteCiLayoutEvidence(json);
#endif
        }

#if UNITY_EDITOR || STARFALL_ANDROID_CI
        private void WriteCiLayoutEvidence(string sourceJson)
        {
            try
            {
                var layout = MobileLayoutPolicy.Calculate(GetMetrics());
                var evidence = new JObject
                {
                    ["source"] = JObject.Parse(sourceJson),
                    ["mode"] = layout.Mode.ToString(),
                    ["density"] = layout.Density,
                    ["safeBoundsDp"] = RectJson(layout.SafeBoundsDp),
                    ["hasSeparatingFeature"] = layout.HasSeparatingFeature,
                    ["foldingOrientation"] = layout.FoldingOrientation.ToString(),
                    ["foldingBoundsDp"] = RectJson(layout.FoldingBoundsDp),
                    ["primaryPaneDp"] = RectJson(layout.PrimaryPaneDp),
                    ["secondaryPaneDp"] = RectJson(layout.SecondaryPaneDp),
                };
                var compact = evidence.ToString(Newtonsoft.Json.Formatting.None);
                var path = Path.Combine(Application.persistentDataPath, "starfall-ci-layout.json");
                File.WriteAllText(path, evidence.ToString(Newtonsoft.Json.Formatting.Indented));
                Debug.Log("STARFALL_ANDROID_CI_LAYOUT=" + compact);
            }
            catch (Exception exception)
            {
                Debug.LogError("Could not write Android CI layout evidence: " + exception.Message);
            }
        }

        private static JObject RectJson(Rect value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y,
                ["width"] = value.width,
                ["height"] = value.height,
            };
        }
#endif

        private static RectInt ScreenSafeArea(int width, int height)
        {
            var safe = Screen.safeArea;
            if (safe.width <= 0f || safe.height <= 0f) return new RectInt(0, 0, width, height);
            return new RectInt(Mathf.RoundToInt(safe.x), Mathf.RoundToInt(safe.y),
                Mathf.RoundToInt(safe.width), Mathf.RoundToInt(safe.height));
        }

        private static RectInt ReadRect(JObject value)
        {
            if (value == null) return default;
            var x = ReadInt(value, "x", "left");
            var y = ReadInt(value, "y", "top");
            var width = ReadInt(value, "width");
            var height = ReadInt(value, "height");
            if (width <= 0 && value["right"] != null) width = value.Value<int>("right") - x;
            if (height <= 0 && value["bottom"] != null) height = value.Value<int>("bottom") - y;
            return new RectInt(x, y, width, height);
        }

        private static void ValidateRectWithinWindow(RectInt value, int width, int height, string label)
        {
            var xMax = (long)value.x + value.width;
            var yMax = (long)value.y + value.height;
            if (value.x < 0 || value.y < 0 || value.width <= 0 || value.height <= 0 ||
                xMax > width || yMax > height)
                throw new InvalidDataException("Android " + label + " must remain inside the window.");
        }

        private static void ValidateFoldingExtent(
            RectInt value,
            FoldingFeatureOrientation orientation)
        {
            if (value.width < 0 || value.height < 0 || (value.width == 0 && value.height == 0) ||
                (orientation == FoldingFeatureOrientation.Vertical && value.height == 0) ||
                (orientation == FoldingFeatureOrientation.Horizontal && value.width == 0))
                throw new InvalidDataException(
                    "Android folding feature must have a positive span along its fold axis.");
        }

        private static void ValidateFoldingFeatureWithinWindow(RectInt value, int width, int height)
        {
            var xMax = (long)value.x + value.width;
            var yMax = (long)value.y + value.height;
            if (value.x < 0 || value.y < 0 || xMax > width || yMax > height)
                throw new InvalidDataException(
                    "Android folding feature must remain inside the window.");
        }

        private static int ReadInt(JObject value, params string[] names)
        {
            foreach (var name in names)
                if (value[name] != null && int.TryParse(value[name].ToString(), out var result)) return result;
            return 0;
        }

        private static float ReadFloat(JObject value, params string[] names)
        {
            foreach (var name in names)
                if (value[name] != null && float.TryParse(value[name].ToString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var result)) return result;
            return 0f;
        }

        private static FoldingFeatureOrientation ParseOrientation(string value)
        {
            if (Contains(value, "vertical")) return FoldingFeatureOrientation.Vertical;
            if (Contains(value, "horizontal")) return FoldingFeatureOrientation.Horizontal;
            return FoldingFeatureOrientation.Unknown;
        }

        private static FoldingFeatureState ParseState(string value)
        {
            if (Contains(value, "half")) return FoldingFeatureState.HalfOpened;
            if (Contains(value, "flat")) return FoldingFeatureState.Flat;
            return FoldingFeatureState.Unknown;
        }

        private static FoldingFeatureOcclusion ParseOcclusion(string value)
        {
            return Contains(value, "full") ? FoldingFeatureOcclusion.Full : FoldingFeatureOcclusion.None;
        }

        private static bool Contains(string value, string needle)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
