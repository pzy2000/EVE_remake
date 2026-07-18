using NUnit.Framework;
using Starfall.UI;
using UnityEngine;

namespace Starfall.Tests.EditMode.UI
{
    public sealed class MobileWindowingTests
    {
        private const float Dpi = 420f;
        private const float Epsilon = 0.01f;

        [Test]
        public void UltraWideTarget_IsCompactLandscape()
        {
            var layout = MobileLayoutPolicy.Calculate(Metrics(2748, 1172, Dpi));

            Assert.That(layout.Mode, Is.EqualTo(MobileLayoutMode.CompactLandscape));
            Assert.That(layout.Density, Is.EqualTo(2.625f).Within(Epsilon));
            Assert.That(layout.SafeBoundsDp.width, Is.EqualTo(2748f / 2.625f).Within(Epsilon));
            Assert.That(layout.SafeBoundsDp.height, Is.EqualTo(1172f / 2.625f).Within(Epsilon));
        }

        [Test]
        public void SquareFoldableTarget_IsSquareExpanded()
        {
            var layout = MobileLayoutPolicy.Calculate(Metrics(2480, 2200, Dpi));

            Assert.That(layout.Mode, Is.EqualTo(MobileLayoutMode.SquareExpanded));
            Assert.That(layout.SafeBoundsDp.width, Is.EqualTo(2480f / 2.625f).Within(Epsilon));
            Assert.That(layout.SafeBoundsDp.height, Is.EqualTo(2200f / 2.625f).Within(Epsilon));
            Assert.That(layout.HasSeparatingFeature, Is.False);
        }

        [Test]
        public void MissingDpi_Uses420DpiFallback()
        {
            var layout = MobileLayoutPolicy.Calculate(Metrics(2748, 1172, 0f));

            Assert.That(layout.Density, Is.EqualTo(MobileLayoutPolicy.DefaultDensityDpi / 160f)
                .Within(Epsilon));
            Assert.That(layout.Mode, Is.EqualTo(MobileLayoutMode.CompactLandscape));
        }

        [TestCase(320f)]
        [TestCase(560f)]
        public void DensityStressTargetsRemainFiniteAndClassified(float dpi)
        {
            var compact = MobileLayoutPolicy.Calculate(Metrics(2748, 1172, dpi));
            var square = MobileLayoutPolicy.Calculate(Metrics(2480, 2200, dpi));

            Assert.That(compact.Mode, Is.EqualTo(MobileLayoutMode.CompactLandscape));
            Assert.That(square.Mode, Is.EqualTo(MobileLayoutMode.SquareExpanded));
            Assert.That(float.IsNaN(compact.SafeBoundsDp.width), Is.False);
            Assert.That(float.IsInfinity(square.SafeBoundsDp.height), Is.False);
            Assert.That(compact.SafeBoundsDp.width, Is.GreaterThan(0f));
            Assert.That(square.SafeBoundsDp.height, Is.GreaterThan(0f));
        }

        [Test]
        public void SafeArea_ConvertsBottomLeftPixelsToTopLeftDpInsets()
        {
            var safeArea = new RectInt(105, 42, 2548, 1088);
            var layout = MobileLayoutPolicy.Calculate(
                new MobileWindowMetrics(2748, 1172, Dpi, safeArea));

            Assert.That(layout.SafeInsetsDp.Left, Is.EqualTo(105f / 2.625f).Within(Epsilon));
            Assert.That(layout.SafeInsetsDp.Right, Is.EqualTo(95f / 2.625f).Within(Epsilon));
            Assert.That(layout.SafeInsetsDp.Top, Is.EqualTo(42f / 2.625f).Within(Epsilon));
            Assert.That(layout.SafeInsetsDp.Bottom, Is.EqualTo(42f / 2.625f).Within(Epsilon));
            Assert.That(layout.SafeBoundsDp.y, Is.EqualTo(layout.SafeInsetsDp.Top).Within(Epsilon));
        }

        [Test]
        public void Vertical84PixelHinge_SplitsSquareTargetIntoLeftAndRightPanes()
        {
            const int hingeWidth = 84;
            var hinge = new FoldingFeatureInfo(
                new RectInt((2480 - hingeWidth) / 2, 0, hingeWidth, 2200),
                FoldingFeatureOrientation.Vertical,
                FoldingFeatureState.Flat,
                FoldingFeatureOcclusion.Full,
                true);

            var layout = MobileLayoutPolicy.Calculate(Metrics(2480, 2200, Dpi, hinge));

            Assert.That(layout.Mode, Is.EqualTo(MobileLayoutMode.SquareExpanded));
            Assert.That(layout.HasSeparatingFeature, Is.True);
            Assert.That(layout.FoldingOrientation, Is.EqualTo(FoldingFeatureOrientation.Vertical));
            Assert.That(layout.FoldingBoundsDp.width, Is.EqualTo(hingeWidth / 2.625f).Within(Epsilon));
            Assert.That(layout.PrimaryPaneDp.xMax, Is.EqualTo(layout.FoldingBoundsDp.xMin).Within(Epsilon));
            Assert.That(layout.SecondaryPaneDp.xMin, Is.EqualTo(layout.FoldingBoundsDp.xMax).Within(Epsilon));
            Assert.That(layout.PrimaryPaneDp.Overlaps(layout.FoldingBoundsDp), Is.False);
            Assert.That(layout.SecondaryPaneDp.Overlaps(layout.FoldingBoundsDp), Is.False);
            Assert.That(layout.PrimaryPaneDp.Overlaps(layout.SecondaryPaneDp), Is.False);
        }

        [Test]
        public void Horizontal80PixelHalfOpenedHinge_SplitsSquareTargetIntoTopAndBottomPanes()
        {
            const int hingeHeight = 80;
            var hinge = new FoldingFeatureInfo(
                new RectInt(0, (2200 - hingeHeight) / 2, 2480, hingeHeight),
                FoldingFeatureOrientation.Horizontal,
                FoldingFeatureState.HalfOpened,
                FoldingFeatureOcclusion.Full,
                true);

            var layout = MobileLayoutPolicy.Calculate(Metrics(2480, 2200, Dpi, hinge));

            Assert.That(layout.Mode, Is.EqualTo(MobileLayoutMode.SquareExpanded));
            Assert.That(layout.HasSeparatingFeature, Is.True);
            Assert.That(layout.FoldingOrientation, Is.EqualTo(FoldingFeatureOrientation.Horizontal));
            Assert.That(layout.FoldingBoundsDp.height, Is.EqualTo(hingeHeight / 2.625f).Within(Epsilon));
            Assert.That(layout.PrimaryPaneDp.yMax, Is.EqualTo(layout.FoldingBoundsDp.yMin).Within(Epsilon));
            Assert.That(layout.SecondaryPaneDp.yMin, Is.EqualTo(layout.FoldingBoundsDp.yMax).Within(Epsilon));
            Assert.That(layout.PrimaryPaneDp.Overlaps(layout.FoldingBoundsDp), Is.False);
            Assert.That(layout.SecondaryPaneDp.Overlaps(layout.FoldingBoundsDp), Is.False);
            Assert.That(layout.PrimaryPaneDp.Overlaps(layout.SecondaryPaneDp), Is.False);
        }

        [Test]
        public void ZeroWidthSeparatingFold_SplitsSquareTargetAtVerticalFoldLine()
        {
            var fold = new FoldingFeatureInfo(
                new RectInt(1240, 0, 0, 2200),
                FoldingFeatureOrientation.Vertical,
                FoldingFeatureState.Flat,
                FoldingFeatureOcclusion.None,
                true);

            var layout = MobileLayoutPolicy.Calculate(Metrics(2480, 2200, Dpi, fold));

            Assert.That(layout.HasSeparatingFeature, Is.True);
            Assert.That(layout.FoldingBoundsDp.width, Is.Zero.Within(Epsilon));
            Assert.That(layout.PrimaryPaneDp.xMax, Is.EqualTo(1240f / 2.625f).Within(Epsilon));
            Assert.That(layout.SecondaryPaneDp.xMin, Is.EqualTo(1240f / 2.625f).Within(Epsilon));
            Assert.That(layout.PrimaryPaneDp.width, Is.GreaterThan(0f));
            Assert.That(layout.SecondaryPaneDp.width, Is.GreaterThan(0f));
        }

        [Test]
        public void ZeroHeightHalfOpenedFold_SplitsSquareTargetAtHorizontalFoldLine()
        {
            var fold = new FoldingFeatureInfo(
                new RectInt(0, 1100, 2480, 0),
                FoldingFeatureOrientation.Horizontal,
                FoldingFeatureState.HalfOpened,
                FoldingFeatureOcclusion.None,
                true);

            var layout = MobileLayoutPolicy.Calculate(Metrics(2480, 2200, Dpi, fold));

            Assert.That(layout.HasSeparatingFeature, Is.True);
            Assert.That(layout.FoldingBoundsDp.height, Is.Zero.Within(Epsilon));
            Assert.That(layout.PrimaryPaneDp.yMax, Is.EqualTo(1100f / 2.625f).Within(Epsilon));
            Assert.That(layout.SecondaryPaneDp.yMin, Is.EqualTo(1100f / 2.625f).Within(Epsilon));
            Assert.That(layout.PrimaryPaneDp.height, Is.GreaterThan(0f));
            Assert.That(layout.SecondaryPaneDp.height, Is.GreaterThan(0f));
        }

        [Test]
        public void FlatNonSeparatingFeature_DoesNotSplitWindow()
        {
            var feature = new FoldingFeatureInfo(
                new RectInt(1238, 0, 4, 2200),
                FoldingFeatureOrientation.Vertical,
                FoldingFeatureState.Flat,
                FoldingFeatureOcclusion.None,
                false);

            var layout = MobileLayoutPolicy.Calculate(Metrics(2480, 2200, Dpi, feature));

            Assert.That(layout.HasSeparatingFeature, Is.False);
            Assert.That(layout.PrimaryPaneDp, Is.EqualTo(layout.SafeBoundsDp));
        }

        private static MobileWindowMetrics Metrics(
            int width,
            int height,
            float dpi,
            params FoldingFeatureInfo[] features) =>
            new MobileWindowMetrics(width, height, dpi, new RectInt(0, 0, width, height), features);
    }
}
