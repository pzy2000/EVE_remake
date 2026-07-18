using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starfall.UI
{
    public enum MobileLayoutMode
    {
        CompactLandscape = 0,
        SquareExpanded = 1,
    }

    public enum FoldingFeatureOrientation
    {
        Unknown = 0,
        Vertical = 1,
        Horizontal = 2,
    }

    public enum FoldingFeatureState
    {
        Unknown = 0,
        Flat = 1,
        HalfOpened = 2,
    }

    public enum FoldingFeatureOcclusion
    {
        None = 0,
        Full = 1,
    }

    [Serializable]
    public readonly struct FoldingFeatureInfo : IEquatable<FoldingFeatureInfo>
    {
        public FoldingFeatureInfo(
            RectInt boundsPixelsTopLeft,
            FoldingFeatureOrientation orientation,
            FoldingFeatureState state,
            FoldingFeatureOcclusion occlusion,
            bool isSeparating)
        {
            BoundsPixelsTopLeft = boundsPixelsTopLeft;
            Orientation = orientation;
            State = state;
            Occlusion = occlusion;
            IsSeparating = isSeparating;
        }

        // Android WindowManager reports folding bounds from the top-left of the window.
        public RectInt BoundsPixelsTopLeft { get; }
        public FoldingFeatureOrientation Orientation { get; }
        public FoldingFeatureState State { get; }
        public FoldingFeatureOcclusion Occlusion { get; }
        public bool IsSeparating { get; }
        public bool SplitsWindow => IsSeparating || Occlusion == FoldingFeatureOcclusion.Full;

        public bool Equals(FoldingFeatureInfo other) =>
            BoundsPixelsTopLeft.Equals(other.BoundsPixelsTopLeft) &&
            Orientation == other.Orientation &&
            State == other.State &&
            Occlusion == other.Occlusion &&
            IsSeparating == other.IsSeparating;

        public override bool Equals(object obj) => obj is FoldingFeatureInfo other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            BoundsPixelsTopLeft, (int)Orientation, (int)State, (int)Occlusion, IsSeparating);
    }

    public readonly struct MobileWindowMetrics : IEquatable<MobileWindowMetrics>
    {
        private static readonly IReadOnlyList<FoldingFeatureInfo> NoFoldingFeatures =
            Array.Empty<FoldingFeatureInfo>();

        public MobileWindowMetrics(
            int pixelWidth,
            int pixelHeight,
            float densityDpi,
            RectInt safeAreaPixels,
            IReadOnlyList<FoldingFeatureInfo> foldingFeatures = null)
        {
            PixelWidth = Mathf.Max(1, pixelWidth);
            PixelHeight = Mathf.Max(1, pixelHeight);
            DensityDpi = densityDpi;
            SafeAreaPixels = NormalizeSafeArea(safeAreaPixels, PixelWidth, PixelHeight);
            FoldingFeatures = foldingFeatures ?? NoFoldingFeatures;
        }

        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public float DensityDpi { get; }
        public RectInt SafeAreaPixels { get; }
        public IReadOnlyList<FoldingFeatureInfo> FoldingFeatures { get; }

        public bool Equals(MobileWindowMetrics other)
        {
            if (PixelWidth != other.PixelWidth || PixelHeight != other.PixelHeight ||
                !Mathf.Approximately(DensityDpi, other.DensityDpi) ||
                !SafeAreaPixels.Equals(other.SafeAreaPixels) ||
                FoldingFeatures.Count != other.FoldingFeatures.Count)
                return false;

            for (var i = 0; i < FoldingFeatures.Count; i++)
                if (!FoldingFeatures[i].Equals(other.FoldingFeatures[i])) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is MobileWindowMetrics other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = HashCode.Combine(PixelWidth, PixelHeight, DensityDpi, SafeAreaPixels);
                for (var i = 0; i < FoldingFeatures.Count; i++)
                    hash = hash * 397 ^ FoldingFeatures[i].GetHashCode();
                return hash;
            }
        }

        private static RectInt NormalizeSafeArea(RectInt value, int width, int height)
        {
            if (value.width <= 0 || value.height <= 0) return new RectInt(0, 0, width, height);
            var xMin = Mathf.Clamp(value.xMin, 0, width);
            var xMax = Mathf.Clamp(value.xMax, xMin, width);
            var yMin = Mathf.Clamp(value.yMin, 0, height);
            var yMax = Mathf.Clamp(value.yMax, yMin, height);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }
    }

    public interface IWindowMetricsProvider
    {
        MobileWindowMetrics GetMetrics();
    }

    public readonly struct MobileInsets
    {
        public MobileInsets(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public float Left { get; }
        public float Top { get; }
        public float Right { get; }
        public float Bottom { get; }
    }

    public readonly struct MobileLayout
    {
        public MobileLayout(
            MobileLayoutMode mode,
            float density,
            Rect screenBoundsDp,
            Rect safeBoundsDp,
            MobileInsets safeInsetsDp,
            bool hasSeparatingFeature,
            FoldingFeatureOrientation foldingOrientation,
            Rect foldingBoundsDp,
            Rect primaryPaneDp,
            Rect secondaryPaneDp)
        {
            Mode = mode;
            Density = density;
            ScreenBoundsDp = screenBoundsDp;
            SafeBoundsDp = safeBoundsDp;
            SafeInsetsDp = safeInsetsDp;
            HasSeparatingFeature = hasSeparatingFeature;
            FoldingOrientation = foldingOrientation;
            FoldingBoundsDp = foldingBoundsDp;
            PrimaryPaneDp = primaryPaneDp;
            SecondaryPaneDp = secondaryPaneDp;
        }

        public MobileLayoutMode Mode { get; }
        public float Density { get; }
        public Rect ScreenBoundsDp { get; }
        public Rect SafeBoundsDp { get; }
        public MobileInsets SafeInsetsDp { get; }
        public bool HasSeparatingFeature { get; }
        public FoldingFeatureOrientation FoldingOrientation { get; }
        public Rect FoldingBoundsDp { get; }
        public Rect PrimaryPaneDp { get; }
        public Rect SecondaryPaneDp { get; }
    }

    public static class MobileLayoutPolicy
    {
        public const float DefaultDensityDpi = 420f;
        public const float CompactAspectRatio = 1.8f;
        public const float CompactHeightDp = 600f;

        public static MobileLayout Calculate(MobileWindowMetrics metrics)
        {
            var dpi = metrics.DensityDpi > 0f ? metrics.DensityDpi : DefaultDensityDpi;
            var density = Mathf.Max(1f, dpi / 160f);
            var screen = new Rect(0f, 0f, metrics.PixelWidth / density, metrics.PixelHeight / density);
            var safe = SafeAreaToPanelDp(metrics, density);
            var insets = new MobileInsets(
                safe.xMin,
                safe.yMin,
                Mathf.Max(0f, screen.xMax - safe.xMax),
                Mathf.Max(0f, screen.yMax - safe.yMax));
            var aspect = safe.height > Mathf.Epsilon ? safe.width / safe.height : float.PositiveInfinity;
            var mode = aspect >= CompactAspectRatio || safe.height < CompactHeightDp
                ? MobileLayoutMode.CompactLandscape
                : MobileLayoutMode.SquareExpanded;

            for (var i = 0; i < metrics.FoldingFeatures.Count; i++)
            {
                var feature = metrics.FoldingFeatures[i];
                if (!feature.SplitsWindow) continue;
                var fold = FoldingBoundsToPanelDp(feature.BoundsPixelsTopLeft, density);
                var orientation = feature.Orientation;
                if (orientation == FoldingFeatureOrientation.Unknown)
                    orientation = fold.height >= fold.width
                        ? FoldingFeatureOrientation.Vertical
                        : FoldingFeatureOrientation.Horizontal;
                if (!TryClipFoldingBounds(fold, safe, orientation, out fold)) continue;

                if (TrySplit(safe, fold, orientation, out var primary, out var secondary))
                    return new MobileLayout(mode, density, screen, safe, insets, true,
                        orientation, fold, primary, secondary);
            }

            return new MobileLayout(mode, density, screen, safe, insets, false,
                FoldingFeatureOrientation.Unknown, default, safe, default);
        }

        private static Rect SafeAreaToPanelDp(MobileWindowMetrics metrics, float density)
        {
            var safe = metrics.SafeAreaPixels;
            var top = metrics.PixelHeight - safe.yMax;
            return new Rect(safe.xMin / density, top / density, safe.width / density, safe.height / density);
        }

        private static Rect FoldingBoundsToPanelDp(RectInt bounds, float density) => new Rect(
            bounds.x / density,
            bounds.y / density,
            bounds.width / density,
            bounds.height / density);

        private static bool TrySplit(
            Rect safe,
            Rect fold,
            FoldingFeatureOrientation orientation,
            out Rect primary,
            out Rect secondary)
        {
            if (orientation == FoldingFeatureOrientation.Vertical)
            {
                primary = Rect.MinMaxRect(safe.xMin, safe.yMin, fold.xMin, safe.yMax);
                secondary = Rect.MinMaxRect(fold.xMax, safe.yMin, safe.xMax, safe.yMax);
            }
            else
            {
                primary = Rect.MinMaxRect(safe.xMin, safe.yMin, safe.xMax, fold.yMin);
                secondary = Rect.MinMaxRect(safe.xMin, fold.yMax, safe.xMax, safe.yMax);
            }

            return primary.width > 0f && primary.height > 0f &&
                   secondary.width > 0f && secondary.height > 0f;
        }

        private static bool TryClipFoldingBounds(
            Rect fold,
            Rect safe,
            FoldingFeatureOrientation orientation,
            out Rect clipped)
        {
            var xMin = Mathf.Max(fold.xMin, safe.xMin);
            var yMin = Mathf.Max(fold.yMin, safe.yMin);
            var xMax = Mathf.Min(fold.xMax, safe.xMax);
            var yMax = Mathf.Min(fold.yMax, safe.yMax);
            var valid = orientation == FoldingFeatureOrientation.Vertical
                ? xMax >= xMin && yMax > yMin
                : xMax > xMin && yMax >= yMin;
            clipped = valid ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : default;
            return valid;
        }
    }

    public static class MobileWindowing
    {
        // Native Android integration can replace this factory without coupling UI layout policy to JNI.
        public static Func<IWindowMetricsProvider> ProviderFactory { get; set; }

        // Device Simulator and PlayMode fixtures may opt into mobile layout while running in the Editor.
        public static bool ForceMobileLayoutForTests { get; set; }

        internal static bool ShouldUseMobileLayout =>
            Application.isMobilePlatform || ForceMobileLayoutForTests;

        internal static IWindowMetricsProvider CreateProvider() =>
            ProviderFactory?.Invoke() ?? new ScreenWindowMetricsProvider();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            ProviderFactory = null;
            ForceMobileLayoutForTests = false;
        }

        private sealed class ScreenWindowMetricsProvider : IWindowMetricsProvider
        {
            public MobileWindowMetrics GetMetrics()
            {
                var safe = Screen.safeArea;
                return new MobileWindowMetrics(
                    Screen.width,
                    Screen.height,
                    Screen.dpi,
                    new RectInt(
                        Mathf.RoundToInt(safe.x),
                        Mathf.RoundToInt(safe.y),
                        Mathf.RoundToInt(safe.width),
                        Mathf.RoundToInt(safe.height)));
            }
        }
    }
}
