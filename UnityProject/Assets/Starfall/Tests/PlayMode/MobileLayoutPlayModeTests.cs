using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Starfall.App;
using Starfall.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Starfall.Tests.PlayMode
{
    // Full-scene UI Toolkit geometry settles substantially more slowly on
    // GitHub's software-rendered Unity runner than on the local editor.
    // Preserve the complete scene matrix while allowing for runner variance.
    [Timeout(600000)]
    public sealed class MobileLayoutPlayModeTests
    {
        private const float Dpi = 420f;
        private const float GeometryTimeoutSeconds = 5f;
        private const float Epsilon = 0.75f;
        private const string LongLegacyImportError =
            "Legacy import failed: checksum mismatch in the selected Legacy V1 JSON. " +
            "The source file was not modified; choose a valid save and try again.";
        private Func<IWindowMetricsProvider> originalProviderFactory;
        private bool originalForceMobileLayout;
        private readonly List<PanelSettings> ownedPanelSettings = new();
        private readonly List<RenderTexture> ownedPanelTextures = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            originalProviderFactory = MobileWindowing.ProviderFactory;
            originalForceMobileLayout = MobileWindowing.ForceMobileLayoutForTests;
            yield return DestroyExistingAppRoots();
            MobileWindowing.ForceMobileLayoutForTests = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return DestroyExistingAppRoots();
            foreach (var panelSettings in ownedPanelSettings)
                if (panelSettings) Object.Destroy(panelSettings);
            foreach (var panelTexture in ownedPanelTextures)
            {
                if (!panelTexture) continue;
                panelTexture.Release();
                Object.Destroy(panelTexture);
            }
            ownedPanelSettings.Clear();
            ownedPanelTextures.Clear();
            yield return null;
            MobileWindowing.ProviderFactory = originalProviderFactory;
            MobileWindowing.ForceMobileLayoutForTests = originalForceMobileLayout;
        }

        [UnityTest]
        public IEnumerator UltraWide2748x1172_UsesCompactLayoutAcrossAllScenes()
        {
            yield return RunProfile(Profile.FullScreen(
                "2748x1172", 2748, 1172, MobileLayoutMode.CompactLandscape));
        }

        [UnityTest]
        public IEnumerator Square2480x2200_UsesExpandedLayoutAcrossAllScenes()
        {
            yield return RunProfile(Profile.FullScreen(
                "2480x2200", 2480, 2200, MobileLayoutMode.SquareExpanded));
        }

        [UnityTest]
        public IEnumerator Square2480x2200_With84PixelVerticalHinge_UsesSeparatedPanes()
        {
            const int hingeWidth = 84;
            yield return RunProfile(new Profile(
                "2480x2200-vertical-hinge",
                new MobileWindowMetrics(
                    2480,
                    2200,
                    Dpi,
                    new RectInt(0, 0, 2480, 2200),
                    new[]
                    {
                        new FoldingFeatureInfo(
                            new RectInt((2480 - hingeWidth) / 2, 0, hingeWidth, 2200),
                            FoldingFeatureOrientation.Vertical,
                            FoldingFeatureState.Flat,
                            FoldingFeatureOcclusion.Full,
                            true),
                    }),
                MobileLayoutMode.SquareExpanded,
                FoldingFeatureOrientation.Vertical));
        }

        [UnityTest]
        public IEnumerator Square2480x2200_With80PixelHorizontalHalfOpenedHinge_UsesSeparatedPanes()
        {
            const int hingeHeight = 80;
            yield return RunProfile(new Profile(
                "2480x2200-horizontal-hinge",
                new MobileWindowMetrics(
                    2480,
                    2200,
                    Dpi,
                    new RectInt(0, 0, 2480, 2200),
                    new[]
                    {
                        new FoldingFeatureInfo(
                            new RectInt(0, (2200 - hingeHeight) / 2, 2480, hingeHeight),
                            FoldingFeatureOrientation.Horizontal,
                            FoldingFeatureState.HalfOpened,
                            FoldingFeatureOcclusion.Full,
                            true),
                    }),
                MobileLayoutMode.SquareExpanded,
                FoldingFeatureOrientation.Horizontal));
        }

        private IEnumerator RunProfile(Profile profile)
        {
            var provider = new FixedWindowMetricsProvider(profile.Metrics);
            var layout = MobileLayoutPolicy.Calculate(profile.Metrics);
            var overlayFailures = new List<string>();
            AssertPureGeometry(profile, layout);

            foreach (var scene in new[] { "MainMenu", "Station", "Space" })
            {
                // AppRoot owns the production factory and may replace it during Awake. Reset immediately
                // before every scene so each UIDocument captures this deterministic provider in OnEnable.
                MobileWindowing.ProviderFactory = () => provider;
                var load = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
                Assert.That(load, Is.Not.Null, $"{profile.Name}: could not start loading {scene}.");
                while (!load.isDone) yield return null;

                var document = FindDocument(scene);
                Assert.That(document, Is.Not.Null, $"{profile.Name}: {scene} has no UIDocument.");
                AttachDeterministicPanel(document, profile);
                yield return null;
                var content = ContentRoot(document, scene);
                Assert.That(content, Is.Not.Null, $"{profile.Name}: {scene} has no mobile content root.");
                if (scene == "MainMenu") ShowLegacyImportError(content);

                ApplyDeterministicPanelGeometry(document, layout);
                yield return WaitForFinalGeometry(content, layout.SafeInsetsDp.Left);
                AssertDocumentMode(profile, document, content, layout, scene);
                AssertInteractiveControlsAvoidFolding(profile, content, layout, scene);
                if (layout.HasSeparatingFeature && scene == "Station")
                    yield return AssertOffscreenScrollControlIsExcluded(profile, content, layout);
                AssertCriticalTouchTargets(profile, content, scene);
                AssertCardsAndScrolling(profile, content, layout, scene);
                AssertVisibleTextMinimum(profile, content, $"{scene}/Base");
                if (scene == "MainMenu") AssertLegacyImportError(profile, content, layout);
                if (scene == "Space")
                    yield return AssertModuleRackAccessibility(profile, content, layout);
                yield return AssertSettingsOverlay(profile, content, layout, scene, overlayFailures);
                if (scene == "Space")
                    yield return AssertSpaceOverlays(profile, content, layout, overlayFailures);
            }

            Assert.That(overlayFailures, Is.Empty,
                $"{profile.Name}: mobile overlay geometry failures:\n" + string.Join("\n", overlayFailures));
        }

        private static void ShowLegacyImportError(VisualElement root)
        {
            var status = root.Q<Label>("legacy-import-status");
            Assert.That(status, Is.Not.Null, "MainMenu must expose the legacy import status label.");
            status.text = LongLegacyImportError;
            status.style.display = DisplayStyle.Flex;
            status.EnableInClassList("danger", true);
        }

        private static void AssertLegacyImportError(
            Profile profile,
            VisualElement root,
            MobileLayout layout)
        {
            var status = root.Q<Label>("legacy-import-status");
            var card = root.Q<VisualElement>("menu-card");
            Assert.That(status, Is.Not.Null, $"{profile.Name}/MainMenu: missing legacy status.");
            Assert.That(card, Is.Not.Null, $"{profile.Name}/MainMenu: missing menu card.");
            Assert.That(status.text, Is.EqualTo(LongLegacyImportError),
                $"{profile.Name}/MainMenu: the specific import reason was lost.");
            Assert.That(status.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex),
                $"{profile.Name}/MainMenu: legacy status is hidden.");
            Assert.That(status.resolvedStyle.fontSize + Epsilon, Is.GreaterThanOrEqualTo(14f),
                $"{profile.Name}/MainMenu: legacy status is below 14dp.");
            Assert.That(status.worldBound.width, Is.GreaterThan(0f),
                $"{profile.Name}/MainMenu: legacy status has no width.");
            Assert.That(status.worldBound.height, Is.GreaterThanOrEqualTo(status.resolvedStyle.fontSize),
                $"{profile.Name}/MainMenu: legacy status has no rendered text height.");
            Assert.That(RectContains(layout.SafeBoundsDp, status.worldBound), Is.True,
                $"{profile.Name}/MainMenu: legacy status leaves the safe area: {status.worldBound}.");
            Assert.That(RectContains(card.worldBound, status.worldBound), Is.True,
                $"{profile.Name}/MainMenu: legacy status is clipped by menu card {card.worldBound}.");
            Assert.That(TryGetVisibleBoundsInContent(status, root, out var visibleBounds), Is.True,
                $"{profile.Name}/MainMenu: legacy status is not visible.");
            AssertRectApproximately(visibleBounds, status.worldBound,
                $"{profile.Name}/MainMenu: legacy status is partially clipped");

            if (!layout.HasSeparatingFeature) return;
            Assert.That(status.worldBound.Overlaps(layout.FoldingBoundsDp), Is.False,
                $"{profile.Name}/MainMenu: legacy status {status.worldBound} crosses fold " +
                $"{layout.FoldingBoundsDp}.");
            Assert.That(
                RectContains(layout.PrimaryPaneDp, status.worldBound) ||
                RectContains(layout.SecondaryPaneDp, status.worldBound),
                Is.True,
                $"{profile.Name}/MainMenu: legacy status is not wholly contained by one pane.");
        }

        private void AttachDeterministicPanel(UIDocument document, Profile profile)
        {
            Assert.That(document.panelSettings, Is.Not.Null,
                $"{profile.Name}: cannot create a deterministic panel without production PanelSettings.");
            var settings = Object.Instantiate(document.panelSettings);
            // Preserve the production resource identity asserted below while isolating the target texture
            // and panel geometry from every other PlayMode test.
            settings.name = document.panelSettings.name;
            var texture = new RenderTexture(
                profile.Metrics.PixelWidth,
                profile.Metrics.PixelHeight,
                0,
                RenderTextureFormat.ARGB32)
            {
                name = $"MobileLayout-{profile.Name}",
            };
            settings.targetTexture = texture;
            ownedPanelSettings.Add(settings);
            ownedPanelTextures.Add(texture);
            document.panelSettings = settings;
        }

        private static void ApplyDeterministicPanelGeometry(
            UIDocument document,
            MobileLayout layout)
        {
            var documentRoot = document.rootVisualElement;
            var panelRoot = documentRoot.parent;
            Assert.That(panelRoot, Is.Not.Null,
                "UIDocument must be attached to a real panel before mobile geometry is fixed.");

            FixRect(panelRoot, layout.ScreenBoundsDp);
            // Production MobileUiCoordinator owns document/content geometry.
            // Do not mask a detached or NaN UIDocument root by fixing it here.
        }

        private static void FixRect(VisualElement element, Rect rect)
        {
            element.style.position = Position.Absolute;
            element.style.left = rect.x;
            element.style.top = rect.y;
            element.style.right = StyleKeyword.Auto;
            element.style.bottom = StyleKeyword.Auto;
            element.style.width = rect.width;
            element.style.height = rect.height;
            element.style.flexGrow = 0f;
            element.style.flexShrink = 0f;
        }

        private static void AssertPureGeometry(Profile profile, MobileLayout layout)
        {
            Assert.That(layout.Mode, Is.EqualTo(profile.ExpectedMode), profile.Name);
            Assert.That(layout.SafeBoundsDp.width,
                Is.EqualTo(profile.Metrics.SafeAreaPixels.width / layout.Density).Within(Epsilon), profile.Name);
            Assert.That(layout.SafeBoundsDp.height,
                Is.EqualTo(profile.Metrics.SafeAreaPixels.height / layout.Density).Within(Epsilon), profile.Name);

            if (profile.ExpectedFold == FoldingFeatureOrientation.Unknown)
            {
                Assert.That(layout.HasSeparatingFeature, Is.False, profile.Name);
                Assert.That(layout.PrimaryPaneDp, Is.EqualTo(layout.SafeBoundsDp), profile.Name);
                return;
            }

            Assert.That(layout.HasSeparatingFeature, Is.True, profile.Name);
            Assert.That(layout.FoldingOrientation, Is.EqualTo(profile.ExpectedFold), profile.Name);
            Assert.That(layout.PrimaryPaneDp.Overlaps(layout.FoldingBoundsDp), Is.False, profile.Name);
            Assert.That(layout.SecondaryPaneDp.Overlaps(layout.FoldingBoundsDp), Is.False, profile.Name);
            Assert.That(layout.PrimaryPaneDp.Overlaps(layout.SecondaryPaneDp), Is.False, profile.Name);
            if (profile.ExpectedFold == FoldingFeatureOrientation.Vertical)
            {
                Assert.That(layout.PrimaryPaneDp.xMax,
                    Is.EqualTo(layout.FoldingBoundsDp.xMin).Within(Epsilon), profile.Name);
                Assert.That(layout.SecondaryPaneDp.xMin,
                    Is.EqualTo(layout.FoldingBoundsDp.xMax).Within(Epsilon), profile.Name);
            }
            else
            {
                Assert.That(layout.PrimaryPaneDp.yMax,
                    Is.EqualTo(layout.FoldingBoundsDp.yMin).Within(Epsilon), profile.Name);
                Assert.That(layout.SecondaryPaneDp.yMin,
                    Is.EqualTo(layout.FoldingBoundsDp.yMax).Within(Epsilon), profile.Name);
            }
        }

        private static void AssertDocumentMode(
            Profile profile,
            UIDocument document,
            VisualElement content,
            MobileLayout layout,
            string scene)
        {
            Assert.That(document.panelSettings, Is.Not.Null, $"{profile.Name}/{scene}: missing PanelSettings.");
            Assert.That(document.panelSettings.name, Is.EqualTo("StarfallAndroidPanelSettings"),
                $"{profile.Name}/{scene}: mobile PanelSettings were not selected.");
            Assert.That(document.panelSettings.scaleMode, Is.EqualTo(PanelScaleMode.ConstantPhysicalSize),
                $"{profile.Name}/{scene}: mobile CSS pixels must resolve as density-independent units.");
            Assert.That(content.ClassListContains("mobile"), Is.True, $"{profile.Name}/{scene}");
            Assert.That(content.ClassListContains("compact-landscape"),
                Is.EqualTo(layout.Mode == MobileLayoutMode.CompactLandscape), $"{profile.Name}/{scene}");
            Assert.That(content.ClassListContains("square-expanded"),
                Is.EqualTo(layout.Mode == MobileLayoutMode.SquareExpanded), $"{profile.Name}/{scene}");
            Assert.That(content.ClassListContains("hinge-vertical"),
                Is.EqualTo(layout.HasSeparatingFeature &&
                           layout.FoldingOrientation == FoldingFeatureOrientation.Vertical),
                $"{profile.Name}/{scene}");
            Assert.That(content.ClassListContains("hinge-horizontal"),
                Is.EqualTo(layout.HasSeparatingFeature &&
                           layout.FoldingOrientation == FoldingFeatureOrientation.Horizontal),
                $"{profile.Name}/{scene}");
            Assert.That(content.resolvedStyle.left,
                Is.EqualTo(layout.SafeInsetsDp.Left).Within(Epsilon), $"{profile.Name}/{scene}: safe left");
            Assert.That(content.resolvedStyle.top,
                Is.EqualTo(layout.SafeInsetsDp.Top).Within(Epsilon), $"{profile.Name}/{scene}: safe top");
            AssertPanelCoordinateMapping(profile, document, content, layout, scene);
        }

        private static void AssertPanelCoordinateMapping(
            Profile profile,
            UIDocument document,
            VisualElement content,
            MobileLayout layout,
            string scene)
        {
            var documentRoot = document.rootVisualElement;
            var panelRoot = documentRoot.parent;
            Assert.That(panelRoot, Is.Not.Null, $"{profile.Name}/{scene}: document is detached from panel.");
            Assert.That(documentRoot.panel, Is.SameAs(panelRoot.panel),
                $"{profile.Name}/{scene}: document and panel roots use different panels.");
            Assert.That(content.panel, Is.SameAs(panelRoot.panel),
                $"{profile.Name}/{scene}: content root is detached from the tested panel.");
            Assert.That(documentRoot.resolvedStyle.position, Is.EqualTo(Position.Absolute),
                $"{profile.Name}/{scene}: mobile document root is not explicitly anchored.");

            AssertRectApproximately(panelRoot.worldBound, layout.ScreenBoundsDp,
                $"{profile.Name}/{scene}: real panel root does not match target dp bounds.");
            AssertRectApproximately(documentRoot.worldBound, panelRoot.worldBound,
                $"{profile.Name}/{scene}: document root does not cover the real panel root.");

            var contentMinInPanel = panelRoot.WorldToLocal(
                new Vector2(content.worldBound.xMin, content.worldBound.yMin));
            var contentMaxInPanel = panelRoot.WorldToLocal(
                new Vector2(content.worldBound.xMax, content.worldBound.yMax));
            var contentInPanel = Rect.MinMaxRect(
                contentMinInPanel.x,
                contentMinInPanel.y,
                contentMaxInPanel.x,
                contentMaxInPanel.y);
            AssertRectApproximately(contentInPanel, layout.SafeBoundsDp,
                $"{profile.Name}/{scene}: panel-to-content coordinate transform is inconsistent.");
            Assert.That(RectContains(panelRoot.worldBound, content.worldBound), Is.True,
                $"{profile.Name}/{scene}: safe content is a virtual box outside the rendered panel.");

            var targetTexture = document.panelSettings.targetTexture;
            Assert.That(targetTexture, Is.Not.Null,
                $"{profile.Name}/{scene}: deterministic panel has no real pixel render target.");
            Assert.That(targetTexture.width, Is.EqualTo(profile.Metrics.PixelWidth),
                $"{profile.Name}/{scene}: panel pixel width does not match target device.");
            Assert.That(targetTexture.height, Is.EqualTo(profile.Metrics.PixelHeight),
                $"{profile.Name}/{scene}: panel pixel height does not match target device.");
            Assert.That(targetTexture.width / panelRoot.worldBound.width,
                Is.EqualTo(layout.Density).Within(0.01f),
                $"{profile.Name}/{scene}: horizontal pixel-to-dp mapping is inconsistent.");
            Assert.That(targetTexture.height / panelRoot.worldBound.height,
                Is.EqualTo(layout.Density).Within(0.01f),
                $"{profile.Name}/{scene}: vertical pixel-to-dp mapping is inconsistent.");
        }

        private static void AssertRectApproximately(Rect actual, Rect expected, string message)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon), message + " x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon), message + " y");
            Assert.That(actual.width, Is.EqualTo(expected.width).Within(Epsilon), message + " width");
            Assert.That(actual.height, Is.EqualTo(expected.height).Within(Epsilon), message + " height");
        }

        private static void AssertCriticalTouchTargets(Profile profile, VisualElement root, string scene)
        {
            string[] names;
            switch (scene)
            {
                case "MainMenu":
                    names = new[] { "launch", "continue", "import", "settings" };
                    break;
                case "Station":
                    names = new[] { "settings", "repair", "save", "undock", "tab-agents" };
                    break;
                default:
                    names = root.ClassListContains("compact-landscape")
                        ? new[] { "map", "journal", "save", "settings", "mobile-overview-toggle" }
                        : new[] { "map", "journal", "save", "settings", "approach", "warp" };
                    break;
            }

            foreach (var name in names)
            {
                var button = root.Q<Button>(name);
                Assert.That(button, Is.Not.Null, $"{profile.Name}/{scene}: missing button {name}.");
                Assert.That(button.resolvedStyle.display, Is.Not.EqualTo(DisplayStyle.None),
                    $"{profile.Name}/{scene}: key button {name} is hidden.");
                Assert.That(
                    button.resolvedStyle.minHeight.value + Epsilon >= MobileUiCoordinator.MinimumTouchTargetDp,
                    Is.True,
                    $"{profile.Name}/{scene}: {name} is below the 48dp touch minimum.");
                Assert.That(button.worldBound.width, Is.GreaterThan(0f),
                    $"{profile.Name}/{scene}: {name} has no rendered geometry.");
                Assert.That(
                    button.worldBound.height + Epsilon >= MobileUiCoordinator.MinimumTouchTargetDp,
                    Is.True,
                    $"{profile.Name}/{scene}: {name} rendered below 48dp.");
                Assert.That(RectContains(root.worldBound, button.worldBound), Is.True,
                    $"{profile.Name}/{scene}: {name} is clipped by the mobile safe root; " +
                    $"button={button.worldBound}, root={root.worldBound}.");
            }
        }

        private static void AssertInteractiveControlsAvoidFolding(
            Profile profile,
            VisualElement root,
            MobileLayout layout,
            string scene)
        {
            if (!layout.HasSeparatingFeature) return;

            foreach (var button in root.Query<Button>().ToList())
                AssertVisibleElementAvoidsFolding(profile, root, button, layout.FoldingBoundsDp, scene);
            foreach (var textField in root.Query<TextField>().ToList())
                AssertVisibleElementAvoidsFolding(profile, root, textField, layout.FoldingBoundsDp, scene);
            foreach (var slider in root.Query<Slider>().ToList())
                AssertVisibleElementAvoidsFolding(profile, root, slider, layout.FoldingBoundsDp, scene);
            foreach (var toggle in root.Query<Toggle>().ToList())
                AssertVisibleElementAvoidsFolding(profile, root, toggle, layout.FoldingBoundsDp, scene);
            foreach (var text in root.Query<TextElement>().ToList())
                AssertVisibleElementAvoidsFolding(profile, root, text, layout.FoldingBoundsDp, scene);

            if (scene == "Station" &&
                layout.FoldingOrientation == FoldingFeatureOrientation.Vertical)
            {
                var footer = root.Q<Label>("station-footer");
                Assert.That(footer, Is.Not.Null,
                    $"{profile.Name}/Station: missing named station footer.");
                Assert.That(TryGetVisibleBoundsInContent(footer, root, out var footerBounds), Is.True,
                    $"{profile.Name}/Station: station footer is not visible.");
                Assert.That(footerBounds.Overlaps(layout.FoldingBoundsDp), Is.False,
                    $"{profile.Name}/Station: footer {footerBounds} crosses fold {layout.FoldingBoundsDp}.");
                Assert.That(RectContains(layout.PrimaryPaneDp, footerBounds), Is.True,
                    $"{profile.Name}/Station: footer {footerBounds} is not contained by the primary pane.");
            }
        }

        private static void AssertVisibleElementAvoidsFolding(
            Profile profile,
            VisualElement root,
            VisualElement control,
            Rect foldingBoundsDp,
            string scene)
        {
            if (!TryGetVisibleBoundsInContent(control, root, out var visibleBounds))
                return;

            Assert.That(visibleBounds.Overlaps(foldingBoundsDp), Is.False,
                $"{profile.Name}/{scene}: visible {control.GetType().Name} '{control.name}' " +
                $"raw={control.worldBound}, visible={visibleBounds} intersects folding bounds " +
                $"{foldingBoundsDp}.");
        }

        private static bool TryGetVisibleBoundsInContent(
            VisualElement element,
            VisualElement root,
            out Rect visibleBounds)
        {
            visibleBounds = default;
            if (!IsVisibleInHierarchy(element, root) ||
                !TryIntersect(element.worldBound, root.worldBound, out visibleBounds))
                return false;

            foreach (var scrollView in root.Query<ScrollView>().ToList())
            {
                if (!scrollView.contentContainer.Contains(element)) continue;
                if (!TryIntersect(visibleBounds, scrollView.contentViewport.worldBound,
                        out visibleBounds))
                    return false;
            }

            return true;
        }

        private static bool TryIntersect(Rect first, Rect second, out Rect intersection)
        {
            var xMin = Mathf.Max(first.xMin, second.xMin);
            var yMin = Mathf.Max(first.yMin, second.yMin);
            var xMax = Mathf.Min(first.xMax, second.xMax);
            var yMax = Mathf.Min(first.yMax, second.yMax);
            if (xMax <= xMin || yMax <= yMin)
            {
                intersection = default;
                return false;
            }

            intersection = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return true;
        }

        private static IEnumerator AssertOffscreenScrollControlIsExcluded(
            Profile profile,
            VisualElement root,
            MobileLayout layout)
        {
            var scrollView = root.Q<ScrollView>("agents-list");
            Assert.That(scrollView, Is.Not.Null,
                $"{profile.Name}/Station: missing ScrollView for offscreen visibility regression.");
            var viewport = scrollView.contentViewport.worldBound;
            var container = scrollView.contentContainer.worldBound;
            var fold = layout.FoldingBoundsDp;
            var vertical = layout.FoldingOrientation == FoldingFeatureOrientation.Vertical;
            var desired = vertical
                ? new Rect(fold.xMin + 1f, viewport.yMin + 16f,
                    Mathf.Max(1f, fold.width - 2f), MobileUiCoordinator.MinimumTouchTargetDp)
                : new Rect(viewport.xMin + 16f, fold.yMin + 1f,
                    MobileUiCoordinator.MinimumTouchTargetDp, Mathf.Max(1f, fold.height - 2f));

            var offscreen = new Button { name = "offscreen-raw-cross-fold", text = "OFFSCREEN" };
            offscreen.style.position = Position.Absolute;
            offscreen.style.left = desired.xMin - container.xMin;
            offscreen.style.top = desired.yMin - container.yMin;
            offscreen.style.width = desired.width;
            offscreen.style.height = desired.height;
            offscreen.style.minWidth = 0f;
            offscreen.style.minHeight = 0f;
            offscreen.style.flexShrink = 0f;

            var changed = false;
            EventCallback<GeometryChangedEvent> callback = _ => changed = true;
            offscreen.RegisterCallback(callback);
            scrollView.contentContainer.Add(offscreen);
            var deadline = Time.realtimeSinceStartup + GeometryTimeoutSeconds;
            while ((!changed || offscreen.worldBound.width <= 0f || offscreen.worldBound.height <= 0f) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            offscreen.UnregisterCallback(callback);

            Assert.That(changed, Is.True,
                $"{profile.Name}/Station: synthetic offscreen control received no geometry.");
            Assert.That(offscreen.worldBound.Overlaps(fold), Is.True,
                $"{profile.Name}/Station: regression fixture raw bounds must cross fold; " +
                $"raw={offscreen.worldBound}, fold={fold}.");
            Assert.That(TryGetVisibleBoundsInContent(offscreen, root, out _), Is.False,
                $"{profile.Name}/Station: control fully clipped by its ScrollView viewport was considered visible.");
            AssertVisibleControlAvoidsFolding(profile, root, offscreen, fold, "Station");

            offscreen.RemoveFromHierarchy();
            yield return null;
        }

        private static IEnumerator AssertModuleRackAccessibility(
            Profile profile,
            VisualElement root,
            MobileLayout layout)
        {
            var rack = root.Q<ScrollView>("module-rack");
            Assert.That(rack, Is.Not.Null, $"{profile.Name}/Space: module rack must be a ScrollView.");
            Assert.That(rack.mode,
                Is.EqualTo(layout.Mode == MobileLayoutMode.CompactLandscape
                    ? ScrollViewMode.Horizontal
                    : ScrollViewMode.Vertical),
                $"{profile.Name}/Space: module rack scroll axis does not match the layout mode.");

            var buttons = new List<Button>();
            for (var i = 1; i <= 9; i++)
            {
                var button = root.Q<Button>($"module-{i}");
                Assert.That(button, Is.Not.Null, $"{profile.Name}/Space: missing module-{i}.");
                buttons.Add(button);
            }

            yield return null;
            yield return null;
            var viewport = rack.contentViewport.worldBound;
            Assert.That(viewport.width, Is.GreaterThan(0f), $"{profile.Name}/Space: empty module viewport.");
            Assert.That(viewport.height, Is.GreaterThan(0f), $"{profile.Name}/Space: empty module viewport.");

            var horizontalOverflow = rack.contentContainer.worldBound.width > viewport.width + Epsilon;
            var verticalOverflow = rack.contentContainer.worldBound.height > viewport.height + Epsilon;
            if (layout.Mode == MobileLayoutMode.CompactLandscape && horizontalOverflow)
                Assert.That(rack.horizontalScroller.highValue, Is.GreaterThan(rack.horizontalScroller.lowValue),
                    $"{profile.Name}/Space: overflowing compact rack has no horizontal scroll range.");
            if (layout.Mode == MobileLayoutMode.SquareExpanded && verticalOverflow)
                Assert.That(rack.verticalScroller.highValue, Is.GreaterThan(rack.verticalScroller.lowValue),
                    $"{profile.Name}/Space: overflowing square rack has no vertical scroll range.");

            foreach (var button in buttons)
            {
                rack.ScrollTo(button);
                yield return null;
                yield return null;
                Assert.That(button.worldBound.width + Epsilon,
                    Is.GreaterThanOrEqualTo(MobileUiCoordinator.MinimumTouchTargetDp),
                    $"{profile.Name}/Space: {button.name} raw width is below 48dp.");
                Assert.That(button.worldBound.height + Epsilon,
                    Is.GreaterThanOrEqualTo(MobileUiCoordinator.MinimumTouchTargetDp),
                    $"{profile.Name}/Space: {button.name} raw height is below 48dp.");
                Assert.That(RectContains(rack.contentViewport.worldBound, button.worldBound), Is.True,
                    $"{profile.Name}/Space: {button.name} cannot be fully revealed by scrolling; " +
                    $"button={button.worldBound}, viewport={rack.contentViewport.worldBound}.");
                Assert.That(IsVisibleInHierarchy(button, root), Is.True,
                    $"{profile.Name}/Space: {button.name} has a hidden hierarchy ancestor.");
                Assert.That(TryIntersect(button.worldBound, root.worldBound, out _), Is.True,
                    $"{profile.Name}/Space: {button.name} does not intersect content root {root.worldBound}.");
                foreach (var scrollView in root.Query<ScrollView>().ToList())
                {
                    if (!scrollView.contentContainer.Contains(button)) continue;
                    Assert.That(TryIntersect(button.worldBound, scrollView.contentViewport.worldBound, out _),
                        Is.True,
                        $"{profile.Name}/Space: {button.name} is outside containing ScrollView " +
                        $"'{scrollView.name}' viewport {scrollView.contentViewport.worldBound}.");
                }
                Assert.That(TryGetVisibleBoundsInContent(button, root, out var visibleBounds), Is.True,
                    $"{profile.Name}/Space: {button.name} remains inaccessible after ScrollTo.");
                Assert.That(RectContains(layout.SafeBoundsDp, visibleBounds), Is.True,
                    $"{profile.Name}/Space: {button.name} visible bounds leave the safe area.");
                if (layout.HasSeparatingFeature)
                    Assert.That(visibleBounds.Overlaps(layout.FoldingBoundsDp), Is.False,
                        $"{profile.Name}/Space: {button.name} visible bounds cross the fold.");
            }

            rack.scrollOffset = Vector2.zero;
            yield return null;
        }

        private static void AssertVisibleTextMinimum(Profile profile, VisualElement root, string context)
        {
            foreach (var textElement in root.Query<TextElement>().ToList())
            {
                if (!ContainsReadableText(textElement.text) ||
                    !TryGetVisibleBoundsInContent(textElement, root, out _))
                    continue;
                Assert.That(textElement.resolvedStyle.fontSize + Epsilon,
                    Is.GreaterThanOrEqualTo(14f),
                    $"{profile.Name}/{context}: visible text '{textElement.name}' " +
                    $"uses {textElement.resolvedStyle.fontSize:0.##}px, below the 14px mobile minimum.");
            }
        }

        private static bool ContainsReadableText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (var character in value)
                if (char.IsLetterOrDigit(character)) return true;
            return false;
        }

        private static bool IsVisibleInHierarchy(VisualElement element, VisualElement root)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (current.resolvedStyle.display == DisplayStyle.None ||
                    current.resolvedStyle.visibility == Visibility.Hidden)
                    return false;
                if (current == root) return true;
            }

            return false;
        }

        private static IEnumerator AssertSettingsOverlay(
            Profile profile,
            VisualElement root,
            MobileLayout layout,
            string scene,
            List<string> failures)
        {
            var open = root.Q<Button>("settings");
            var overlay = root.Q<VisualElement>("settings-overlay");
            var card = root.Q<VisualElement>("settings-card");
            Assert.That(open, Is.Not.Null, $"{profile.Name}/{scene}: missing settings opener.");
            Assert.That(overlay, Is.Not.Null, $"{profile.Name}/{scene}: missing settings overlay.");
            Assert.That(card, Is.Not.Null, $"{profile.Name}/{scene}: missing settings card.");

            yield return ShowAndWaitForGeometry(
                overlay,
                () => Submit(open),
                $"{profile.Name}/{scene}/Settings",
                failures);
            AssertVisibleTextMinimum(profile, root, $"{scene}/Settings");
            RecordCardGeometry(profile, layout, card, $"{scene}/Settings", failures);
            RecordTouchTarget(profile, layout, overlay, card,
                root.Q<Button>("settings-close"), $"{scene}/Settings close", failures);
            RecordTouchTarget(profile, layout, overlay, card,
                root.Q<Button>("quality-cycle"), $"{scene}/Settings quality", failures);
            RecordTouchTarget(profile, layout, overlay, card,
                root.Q<Toggle>("music-muted"), $"{scene}/Settings mute", failures);
            RecordTouchTarget(profile, layout, overlay, card,
                root.Q<Slider>("music-volume"), $"{scene}/Settings volume", failures);

            var close = root.Q<Button>("settings-close");
            yield return CloseAndWait(
                overlay,
                () => Submit(close),
                $"{profile.Name}/{scene}/Settings",
                failures);
        }

        private static IEnumerator AssertSpaceOverlays(
            Profile profile,
            VisualElement root,
            MobileLayout layout,
            List<string> failures)
        {
            yield return AssertScrollableOverlay(
                profile, root, layout, "map-overlay", "starmap-list", "map-close", "Starmap", failures);
            yield return AssertScrollableOverlay(
                profile, root, layout, "journal-overlay", "journal-list", "journal-close", "Journal", failures);
            yield return AssertCombatLog(profile, root, layout, failures);
            yield return AssertDeathOverlay(profile, root, layout, failures);
        }

        private static IEnumerator AssertScrollableOverlay(
            Profile profile,
            VisualElement root,
            MobileLayout layout,
            string overlayName,
            string scrollName,
            string closeName,
            string label,
            List<string> failures)
        {
            var overlay = root.Q<VisualElement>(overlayName);
            var card = overlay?.Q<VisualElement>(className: "modal-card");
            var scroll = overlay?.Q<ScrollView>(scrollName);
            Assert.That(overlay, Is.Not.Null, $"{profile.Name}/Space/{label}: missing overlay.");
            Assert.That(card, Is.Not.Null, $"{profile.Name}/Space/{label}: missing card.");
            Assert.That(scroll, Is.Not.Null, $"{profile.Name}/Space/{label}: missing ScrollView.");

            var fillers = new List<VisualElement>();
            for (var i = 0; i < 24; i++)
            {
                var row = new Label($"{label.ToUpperInvariant()} GEOMETRY ROW {i + 1:00}");
                row.style.minHeight = MobileUiCoordinator.MinimumTouchTargetDp;
                row.style.flexShrink = 0f;
                scroll.contentContainer.Add(row);
                fillers.Add(row);
            }

            yield return ShowAndWaitForGeometry(
                overlay,
                () => overlay.style.display = DisplayStyle.Flex,
                $"{profile.Name}/Space/{label}",
                failures);
            yield return null;
            yield return null;
            AssertVisibleTextMinimum(profile, root, $"Space/{label}");

            RecordCardGeometry(profile, layout, card, $"Space/{label}", failures);
            RecordTouchTarget(profile, layout, overlay, card,
                root.Q<Button>(closeName), $"Space/{label} close", failures);

            var viewport = scroll.contentViewport;
            Record(viewport.worldBound.width > 0f && viewport.worldBound.height > 0f, failures,
                $"{profile.Name}/Space/{label}: ScrollView viewport has no rendered geometry.");
            var overflow = scroll.contentContainer.worldBound.height > viewport.worldBound.height + Epsilon;
            Record(overflow, failures,
                $"{profile.Name}/Space/{label}: test content does not overflow the ScrollView viewport.");
            Record(scroll.verticalScroller.highValue > scroll.verticalScroller.lowValue + Epsilon, failures,
                $"{profile.Name}/Space/{label}: vertical scroller has no usable range.");
            if (overflow)
            {
                scroll.scrollOffset = new Vector2(0f,
                    scroll.contentContainer.worldBound.height - viewport.worldBound.height);
                yield return null;
                Record(scroll.scrollOffset.y > Epsilon, failures,
                    $"{profile.Name}/Space/{label}: ScrollView refused a non-zero scroll offset.");
                scroll.scrollOffset = Vector2.zero;
            }

            yield return CloseAndWait(
                overlay,
                () => overlay.style.display = DisplayStyle.None,
                $"{profile.Name}/Space/{label}",
                failures);
            foreach (var filler in fillers) filler.RemoveFromHierarchy();
        }

        private static IEnumerator AssertCombatLog(
            Profile profile,
            VisualElement root,
            MobileLayout layout,
            List<string> failures)
        {
            var log = root.Q<Label>("combat-log");
            var scroll = root.Q<ScrollView>("combat-log-scroll");
            var surface = (VisualElement)scroll ?? log;
            Assert.That(log, Is.Not.Null, $"{profile.Name}/Space/Combat Log: missing log text.");
            Record(scroll != null, failures,
                $"{profile.Name}/Space/Combat Log: missing scroll container 'combat-log-scroll'.");

            surface.style.display = DisplayStyle.None;
            yield return null;
            yield return ShowAndWaitForGeometry(
                surface,
                () =>
                {
                    surface.style.display = StyleKeyword.Null;
                    if (root.ClassListContains("compact-landscape"))
                    {
                        Submit(root.Q<Button>("mobile-log-toggle"));
                        // Keep the geometry fixture independent from host state while still exercising
                        // the real button route above.
                        root.RemoveFromClassList("mobile-panel-overview");
                        root.RemoveFromClassList("mobile-panel-target");
                        root.AddToClassList("mobile-panel-log");
                    }
                },
                $"{profile.Name}/Space/Combat Log",
                failures);
            AssertVisibleTextMinimum(profile, root, "Space/Combat Log");

            RecordCardGeometry(profile, layout, surface, "Space/Combat Log", failures);
            if (scroll != null)
            {
                // The live controller refreshes the real log label every frame. Add deterministic
                // rows to the real content container so the fixture tests scrolling, not host timing.
                var fillers = new List<VisualElement>();
                for (var i = 0; i < 64; i++)
                {
                    var row = new Label($"COMBAT EVENT {i + 1:00}");
                    row.style.minHeight = 24f;
                    row.style.flexShrink = 0f;
                    scroll.contentContainer.Add(row);
                    fillers.Add(row);
                }
                yield return null;
                yield return null;
                RecordCardGeometry(profile, layout, surface,
                    "Space/Combat Log after long content", failures);
                var overflow = scroll.contentContainer.worldBound.height >
                               scroll.contentViewport.worldBound.height + Epsilon;
                Record(overflow,
                    failures, $"{profile.Name}/Space/Combat Log: long log does not overflow its viewport " +
                              $"(content={scroll.contentContainer.worldBound}, " +
                              $"viewport={scroll.contentViewport.worldBound}).");
                Record(scroll.verticalScroller.highValue > scroll.verticalScroller.lowValue + Epsilon,
                    failures, $"{profile.Name}/Space/Combat Log: vertical scroller has no usable range.");
                if (overflow)
                {
                    scroll.scrollOffset = new Vector2(0f,
                        scroll.contentContainer.worldBound.height - scroll.contentViewport.worldBound.height);
                    yield return null;
                    Record(scroll.scrollOffset.y > Epsilon, failures,
                        $"{profile.Name}/Space/Combat Log: ScrollView refused a non-zero scroll offset.");
                    scroll.scrollOffset = Vector2.zero;
                }
                foreach (var filler in fillers) filler.RemoveFromHierarchy();
            }

            surface.style.display = DisplayStyle.None;
            if (root.ClassListContains("compact-landscape"))
            {
                root.RemoveFromClassList("mobile-panel-log");
                root.AddToClassList("mobile-panel-overview");
            }
            yield return null;
        }

        private static IEnumerator AssertDeathOverlay(
            Profile profile,
            VisualElement root,
            MobileLayout layout,
            List<string> failures)
        {
            var overlay = root.Q<VisualElement>("death-overlay");
            var card = overlay?.Q<VisualElement>(className: "death-card");
            Assert.That(overlay, Is.Not.Null, $"{profile.Name}/Space/Death: missing overlay.");
            Assert.That(card, Is.Not.Null, $"{profile.Name}/Space/Death: missing card.");

            yield return ShowAndWaitForGeometry(
                overlay,
                () => overlay.style.display = DisplayStyle.Flex,
                $"{profile.Name}/Space/Death",
                failures);
            AssertVisibleTextMinimum(profile, root, "Space/Death");
            RecordCardGeometry(profile, layout, card, "Space/Death", failures);
            RecordTouchTarget(profile, layout, overlay, card,
                root.Q<Button>("respawn"), "Space/Death respawn", failures);
            yield return CloseAndWait(
                overlay,
                () => overlay.style.display = DisplayStyle.None,
                $"{profile.Name}/Space/Death",
                failures);
        }

        private static IEnumerator ShowAndWaitForGeometry(
            VisualElement element,
            Action show,
            string label,
            List<string> failures)
        {
            var changed = false;
            EventCallback<GeometryChangedEvent> callback = _ => changed = true;
            element.RegisterCallback(callback);
            show();

            var deadline = Time.realtimeSinceStartup + GeometryTimeoutSeconds;
            while ((!changed || element.resolvedStyle.display == DisplayStyle.None ||
                    element.worldBound.width <= 0f || element.worldBound.height <= 0f) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            element.UnregisterCallback(callback);

            Record(changed, failures, $"{label}: no GeometryChangedEvent while opening.");
            Record(element.resolvedStyle.display != DisplayStyle.None, failures,
                $"{label}: did not become visible.");
            Record(element.worldBound.width > 0f && element.worldBound.height > 0f, failures,
                $"{label}: visible element has no rendered geometry.");
        }

        private static IEnumerator CloseAndWait(
            VisualElement element,
            Action close,
            string label,
            List<string> failures)
        {
            close();
            var deadline = Time.realtimeSinceStartup + GeometryTimeoutSeconds;
            while (element.resolvedStyle.display != DisplayStyle.None &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Record(element.resolvedStyle.display == DisplayStyle.None, failures,
                $"{label}: did not close before the next overlay.");
            if (element.resolvedStyle.display != DisplayStyle.None)
                element.style.display = DisplayStyle.None;
        }

        private static void Submit(VisualElement element)
        {
            if (element == null) return;
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = element;
            element.SendEvent(submit);
        }

        private static void RecordCardGeometry(
            Profile profile,
            MobileLayout layout,
            VisualElement card,
            string label,
            List<string> failures)
        {
            if (card == null) return;
            var bounds = card.worldBound;
            Record(bounds.width > 0f && bounds.height > 0f, failures,
                $"{profile.Name}/{label}: card has no rendered geometry.");
            Record(RectContains(layout.SafeBoundsDp, bounds), failures,
                $"{profile.Name}/{label}: card {bounds} is clipped outside safe bounds {layout.SafeBoundsDp}.");
            if (layout.HasSeparatingFeature)
                Record(!bounds.Overlaps(layout.FoldingBoundsDp), failures,
                    $"{profile.Name}/{label}: card {bounds} intersects folding bounds {layout.FoldingBoundsDp}.");
        }

        private static void RecordTouchTarget(
            Profile profile,
            MobileLayout layout,
            VisualElement visibleRoot,
            VisualElement card,
            VisualElement control,
            string label,
            List<string> failures)
        {
            if (control == null)
            {
                failures.Add($"{profile.Name}/{label}: missing key control.");
                return;
            }

            var bounds = control.worldBound;
            Record(IsVisibleInHierarchy(control, visibleRoot), failures,
                $"{profile.Name}/{label}: key control is not visible.");
            Record(bounds.width + Epsilon >= MobileUiCoordinator.MinimumTouchTargetDp &&
                   bounds.height + Epsilon >= MobileUiCoordinator.MinimumTouchTargetDp,
                failures, $"{profile.Name}/{label}: key control {bounds} is below 48dp.");
            Record(RectContains(layout.SafeBoundsDp, bounds), failures,
                $"{profile.Name}/{label}: key control {bounds} is clipped outside safe bounds.");
            if (card != null)
                Record(RectContains(card.worldBound, bounds), failures,
                    $"{profile.Name}/{label}: key control {bounds} is clipped by card {card.worldBound}.");
            if (layout.HasSeparatingFeature)
                Record(!bounds.Overlaps(layout.FoldingBoundsDp), failures,
                    $"{profile.Name}/{label}: key control {bounds} intersects folding bounds {layout.FoldingBoundsDp}.");
        }

        private static bool RectContains(Rect outer, Rect inner) =>
            inner.xMin >= outer.xMin - Epsilon && inner.yMin >= outer.yMin - Epsilon &&
            inner.xMax <= outer.xMax + Epsilon && inner.yMax <= outer.yMax + Epsilon;

        private static void Record(bool condition, List<string> failures, string message)
        {
            if (!condition) failures.Add(message);
        }

        private static void AssertCardsAndScrolling(
            Profile profile,
            VisualElement root,
            MobileLayout layout,
            string scene)
        {
            if (scene == "MainMenu")
            {
                var card = root.Q<VisualElement>("menu-card");
                Assert.That(card, Is.Not.Null, $"{profile.Name}: missing menu card.");
                Assert.That(card.resolvedStyle.width, Is.LessThanOrEqualTo(layout.SafeBoundsDp.width + Epsilon),
                    $"{profile.Name}: menu card exceeds the safe width.");
                Assert.That(card.resolvedStyle.height, Is.LessThanOrEqualTo(layout.SafeBoundsDp.height + Epsilon),
                    $"{profile.Name}: menu card exceeds the safe height and has no ScrollView.");
                return;
            }

            if (scene == "Station")
            {
                var services = root.Q<VisualElement>("station-services");
                Assert.That(services, Is.Not.Null, $"{profile.Name}: missing Station services card.");
                Assert.That(services.Q<ScrollView>(), Is.Not.Null,
                    $"{profile.Name}: Station services must remain scrollable.");
                return;
            }

            var mapOverlay = root.Q<VisualElement>("map-overlay");
            var mapCard = mapOverlay?.Q<VisualElement>(className: "modal-card");
            Assert.That(mapOverlay, Is.Not.Null, $"{profile.Name}: missing map overlay.");
            Assert.That(mapCard, Is.Not.Null, $"{profile.Name}: missing map card.");
            Assert.That(mapCard.Q<ScrollView>("starmap-list"), Is.Not.Null,
                $"{profile.Name}: map overlay must remain scrollable.");
            Assert.That(root.Q<VisualElement>("death-overlay")?.Q<VisualElement>(className: "death-card"),
                Is.Not.Null, $"{profile.Name}: death card must remain available inside the safe root.");
        }

        private static IEnumerator WaitForFinalGeometry(VisualElement element, float finalLeft)
        {
            var changed = false;
            EventCallback<GeometryChangedEvent> callback = _ => changed = true;
            element.RegisterCallback(callback);
            element.style.left = finalLeft + 1f;

            var deadline = Time.realtimeSinceStartup + GeometryTimeoutSeconds;
            while (!changed && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(changed, Is.True, "Timed out waiting for the first GeometryChangedEvent.");

            changed = false;
            element.style.left = finalLeft;
            deadline = Time.realtimeSinceStartup + GeometryTimeoutSeconds;
            while (!changed && Time.realtimeSinceStartup < deadline) yield return null;
            element.UnregisterCallback(callback);
            Assert.That(changed, Is.True, "Timed out waiting for final mobile GeometryChangedEvent.");
        }

        private static UIDocument FindDocument(string scene)
        {
            Component controller = scene switch
            {
                "MainMenu" => Object.FindFirstObjectByType<MainMenuUiController>(),
                "Station" => Object.FindFirstObjectByType<StationUiController>(),
                "Space" => Object.FindFirstObjectByType<SpaceHudController>(),
                _ => null,
            };
            return controller ? controller.GetComponent<UIDocument>() : null;
        }

        private static VisualElement ContentRoot(UIDocument document, string scene)
        {
            var name = scene switch
            {
                "MainMenu" => "main-menu",
                "Station" => "station-ui",
                "Space" => "space-hud",
                _ => string.Empty,
            };
            return document.rootVisualElement.Q<VisualElement>(name);
        }

        private static IEnumerator DestroyExistingAppRoots()
        {
            var roots = Object.FindObjectsByType<AppRoot>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (var root in roots)
                if (root) Object.Destroy(root.gameObject);
            if (roots.Length > 0) yield return null;
        }

        private sealed class FixedWindowMetricsProvider : IWindowMetricsProvider
        {
            private readonly MobileWindowMetrics metrics;

            public FixedWindowMetricsProvider(MobileWindowMetrics metrics)
            {
                this.metrics = metrics;
            }

            public MobileWindowMetrics GetMetrics() => metrics;
        }

        private readonly struct Profile
        {
            public Profile(
                string name,
                MobileWindowMetrics metrics,
                MobileLayoutMode expectedMode,
                FoldingFeatureOrientation expectedFold)
            {
                Name = name;
                Metrics = metrics;
                ExpectedMode = expectedMode;
                ExpectedFold = expectedFold;
            }

            public string Name { get; }
            public MobileWindowMetrics Metrics { get; }
            public MobileLayoutMode ExpectedMode { get; }
            public FoldingFeatureOrientation ExpectedFold { get; }

            public static Profile FullScreen(
                string name,
                int width,
                int height,
                MobileLayoutMode mode) => new Profile(
                name,
                new MobileWindowMetrics(width, height, Dpi, new RectInt(0, 0, width, height)),
                mode,
                FoldingFeatureOrientation.Unknown);
        }
    }
}
