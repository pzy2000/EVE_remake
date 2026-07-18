using System;
using System.IO;
using NUnit.Framework;
using Starfall.App;
using Starfall.UI;
using UnityEngine;

namespace Starfall.Tests.EditMode.Mobile
{
    public sealed class MobileRuntimePolicyTests
    {
        private static readonly string[] QualityNames = { "Low", "Mobile", "High", "PC" };

        [Test]
        public void MobileQualityCycleNeverEntersPcPreset()
        {
            Assert.That(QualityLevelPolicy.Next(QualityNames, 2, true), Is.EqualTo(0));
            Assert.That(QualityLevelPolicy.Next(QualityNames, 3, true), Is.EqualTo(0));
            Assert.That(QualityLevelPolicy.Allowed(QualityNames, true), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void DesktopQualityPolicyPreservesExistingPcOnlyBehavior()
        {
            Assert.That(QualityLevelPolicy.Next(QualityNames, 1, false), Is.EqualTo(3));
            Assert.That(QualityLevelPolicy.Next(QualityNames, 3, false), Is.EqualTo(3));
            Assert.That(QualityLevelPolicy.Allowed(QualityNames, false), Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void InvalidPersistedMobileQualityFallsBackToAllowedLevel()
        {
            Assert.That(QualityLevelPolicy.ResolvePersisted(QualityNames, 3, 1, true), Is.EqualTo(1));
            Assert.That(QualityLevelPolicy.ResolvePersisted(QualityNames, 99, 3, true), Is.EqualTo(0));
        }

        [Test]
        public void PauseAndFocusStormSavesOnlyOnBackgroundEdges()
        {
            var coordinator = new LifecycleSaveCoordinator();
            var saves = 0;
            Assert.That(coordinator.SetPaused(true, () => saves++), Is.True);
            Assert.That(coordinator.SetFocused(false, () => saves++), Is.False);
            Assert.That(coordinator.SetPaused(false, () => saves++), Is.False);
            Assert.That(coordinator.SetFocused(true, () => saves++), Is.False);
            Assert.That(coordinator.SetFocused(false, () => saves++), Is.True);
            Assert.That(saves, Is.EqualTo(2));
        }

        [Test]
        public void SaveExceptionDoesNotEscapeLifecycleCallback()
        {
            var coordinator = new LifecycleSaveCoordinator();
            Exception captured = null;
            Assert.DoesNotThrow(() => coordinator.SetPaused(true,
                () => throw new InvalidOperationException("disk"), exception => captured = exception));
            Assert.That(captured, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void AndroidBridgeShutdownAlwaysClearsManagedWindowSubscribers()
        {
            var callbacks = 0;
            AndroidPlatformBridge.WindowLayoutInfoReceived += _ => callbacks++;

            AndroidPlatformBridge.Shutdown();
            AndroidPlatformBridge.PublishWindowLayoutInfo("{}");

            Assert.That(callbacks, Is.Zero);
        }

        [Test]
        public void AndroidWindowJsonFeedsSquareFoldLayout()
        {
            using var provider = new AndroidWindowMetricsProvider();
            const string json = "{\"widthPx\":2480,\"heightPx\":2200,\"densityDpi\":420," +
                                "\"safeAreaOrigin\":\"top-left\",\"safeArea\":{\"x\":0,\"y\":40,\"width\":2480,\"height\":2120}," +
                                "\"foldingFeatures\":[{\"bounds\":{\"x\":1198,\"y\":0,\"width\":84,\"height\":2200}," +
                                "\"orientation\":\"VERTICAL\",\"state\":\"FLAT\",\"occlusion\":\"FULL\",\"separating\":true}]}";
            Assert.That(provider.TryApplyJson(json, out var error), Is.True, error);
            var metrics = provider.GetMetrics();
            var layout = MobileLayoutPolicy.Calculate(metrics);
            Assert.That(layout.Mode, Is.EqualTo(MobileLayoutMode.SquareExpanded));
            Assert.That(layout.HasSeparatingFeature, Is.True);
            Assert.That(layout.PrimaryPaneDp.xMax, Is.LessThanOrEqualTo(layout.FoldingBoundsDp.xMin));
            Assert.That(layout.SecondaryPaneDp.xMin, Is.GreaterThanOrEqualTo(layout.FoldingBoundsDp.xMax));
        }

        [Test]
        public void AndroidWindowJsonPreservesZeroWidthSeparatingFold()
        {
            using var provider = new AndroidWindowMetricsProvider();
            const string json = "{\"widthPx\":2480,\"heightPx\":2200,\"densityDpi\":420," +
                                "\"safeAreaOrigin\":\"top-left\",\"safeArea\":{\"x\":0,\"y\":0,\"width\":2480,\"height\":2200}," +
                                "\"foldingFeatures\":[{\"bounds\":{\"x\":1240,\"y\":0,\"width\":0,\"height\":2200}," +
                                "\"orientation\":\"VERTICAL\",\"state\":\"FLAT\",\"occlusion\":\"NONE\",\"separating\":true}]}";

            Assert.That(provider.TryApplyJson(json, out var error), Is.True, error);
            var metrics = provider.GetMetrics();
            Assert.That(metrics.FoldingFeatures, Has.Count.EqualTo(1));
            Assert.That(metrics.FoldingFeatures[0].BoundsPixelsTopLeft.width, Is.Zero);
            Assert.That(MobileLayoutPolicy.Calculate(metrics).HasSeparatingFeature, Is.True);
        }

        [Test]
        public void AndroidTopLeftSafeAreaConvertsToUnityBottomLeftOnce()
        {
            using var provider = new AndroidWindowMetricsProvider();
            const string json = "{\"widthPx\":1000,\"heightPx\":600,\"densityDpi\":320," +
                                "\"safeAreaOrigin\":\"top-left\",\"safeArea\":{" +
                                "\"x\":10,\"y\":40,\"width\":970,\"height\":500}," +
                                "\"foldingFeatures\":[]}";

            Assert.That(provider.TryApplyJson(json, out var error), Is.True, error);
            Assert.That(provider.GetMetrics().SafeAreaPixels, Is.EqualTo(new RectInt(10, 60, 970, 500)));
        }

        [Test]
        public void InvalidWindowJsonDoesNotPartiallyReplaceLastGoodMetrics()
        {
            using var provider = new AndroidWindowMetricsProvider();
            const string initial = "{\"widthPx\":1000,\"heightPx\":600,\"densityDpi\":320," +
                                   "\"safeAreaOrigin\":\"top-left\",\"safeArea\":{" +
                                   "\"x\":0,\"y\":20,\"width\":1000,\"height\":540}," +
                                   "\"foldingFeatures\":[]}";
            const string invalid = "{\"widthPx\":2480,\"heightPx\":2200,\"densityDpi\":420," +
                                   "\"safeAreaOrigin\":\"diagonal\",\"safeArea\":{" +
                                   "\"x\":0,\"y\":40,\"width\":2480,\"height\":2120}," +
                                   "\"foldingFeatures\":[]}";

            Assert.That(provider.TryApplyJson(initial, out var initialError), Is.True, initialError);
            var before = provider.GetMetrics();
            Assert.That(provider.TryApplyJson(invalid, out var error), Is.False);
            Assert.That(error, Does.Contain("origin"));
            Assert.That(provider.GetMetrics(), Is.EqualTo(before));
        }

        [Test]
        public void RejectedLegacyCachePathIsNeverDeleted()
        {
            var cacheRoot = CreateTemporaryDirectory();
            var rejectedPath = Path.Combine(cacheRoot, "legacy-v1-outside-import-root.json");
            File.WriteAllText(rejectedPath, "{}");
            try
            {
                Assert.Throws<InvalidDataException>(() =>
                    AppRoot.ReadAndDeleteLegacyImportCache(rejectedPath, cacheRoot, out _));
                Assert.That(File.Exists(rejectedPath), Is.True);
            }
            finally
            {
                Directory.Delete(cacheRoot, true);
            }
        }

        [Test]
        public void OversizedValidatedLegacyCacheFileIsRejectedAndCleaned()
        {
            var cacheRoot = CreateTemporaryDirectory();
            var importRoot = Path.Combine(cacheRoot, "legacy-import");
            Directory.CreateDirectory(importRoot);
            var importPath = Path.Combine(importRoot, "legacy-v1-oversized.json");
            using (var stream = File.Create(importPath))
                stream.SetLength(5L * 1024L * 1024L + 1L);
            try
            {
                var exception = Assert.Throws<InvalidDataException>(() =>
                    AppRoot.ReadAndDeleteLegacyImportCache(importPath, cacheRoot, out _));
                Assert.That(exception.Message, Does.Contain("5 MiB"));
                Assert.That(File.Exists(importPath), Is.False);
            }
            finally
            {
                Directory.Delete(cacheRoot, true);
            }
        }

        [Test]
        public void NestedLegacyCacheDirectoryIsRejectedWithoutDeletingTheFile()
        {
            var cacheRoot = CreateTemporaryDirectory();
            var nestedRoot = Path.Combine(cacheRoot, "legacy-import", "nested");
            Directory.CreateDirectory(nestedRoot);
            var nestedPath = Path.Combine(nestedRoot, "legacy-v1-nested.json");
            File.WriteAllText(nestedPath, "{}");
            try
            {
                Assert.Throws<InvalidDataException>(() =>
                    AppRoot.ReadAndDeleteLegacyImportCache(nestedPath, cacheRoot, out _));
                Assert.That(File.Exists(nestedPath), Is.True);
            }
            finally
            {
                Directory.Delete(cacheRoot, true);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "starfall-mobile-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
