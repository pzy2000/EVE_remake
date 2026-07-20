using System;
#if UNITY_EDITOR || STARFALL_ANDROID_CI
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
#endif
using UnityEngine;
using UnityEngine.UIElements;

namespace Starfall.UI
{
    public enum MobileScreenKind
    {
        MainMenu = 0,
        Station = 1,
        Space = 2,
    }

    public sealed class MobileUiCoordinator : IDisposable
    {
        public const float MinimumTouchTargetDp = 48f;
        public const float MobileOverviewRowHeightDp = 48f;
        private const string AndroidPanelSettingsResource = "StarfallAndroidPanelSettings";
        private const long MetricsPollIntervalMs = 250;

        private readonly UIDocument document;
        private readonly PanelSettings originalPanelSettings;
        private readonly VisualElement documentRoot;
        private readonly VisualElement contentRoot;
        private readonly MobileScreenKind screenKind;
        private readonly IWindowMetricsProvider metricsProvider;
        private readonly IVisualElementScheduledItem pollItem;
#if UNITY_ANDROID && !UNITY_EDITOR
        private readonly AndroidUiRenderTextureCompositor androidCompositor;
#endif
#if STARFALL_ANDROID_CI
        private readonly VisualElement ciRenderProbe;
#endif
        private MobileWindowMetrics lastMetrics;
        private string lastCiLayoutEvidence = string.Empty;
        private bool hasMetrics;
        private bool disposed;

        private MobileUiCoordinator(
            UIDocument document,
            MobileScreenKind screenKind,
            IWindowMetricsProvider metricsProvider)
        {
            this.document = document;
            this.screenKind = screenKind;
            this.metricsProvider = metricsProvider;
            originalPanelSettings = document.panelSettings;

            var androidPanelSettings = Resources.Load<PanelSettings>(AndroidPanelSettingsResource);
            if (androidPanelSettings == null)
                Debug.LogError($"Missing Resources/{AndroidPanelSettingsResource}.asset; mobile UI will use desktop scaling.");
            else if (document.panelSettings != androidPanelSettings)
                document.panelSettings = androidPanelSettings;

            documentRoot = document.rootVisualElement;
            contentRoot = FindContentRoot(documentRoot, screenKind);
#if UNITY_ANDROID && !UNITY_EDITOR
            androidCompositor = AndroidUiRenderTextureCompositor.Attach(document);
#endif
#if STARFALL_ANDROID_CI
            ciRenderProbe = CreateCiRenderProbe(documentRoot);
#endif
            documentRoot.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            Refresh(true);
            pollItem = documentRoot.schedule.Execute(() => Refresh(false)).Every(MetricsPollIntervalMs);
        }

        public MobileLayout CurrentLayout { get; private set; }

        /// <summary>Reapplies the current geometry after controllers add runtime overlays.</summary>
        public void ReapplyLayout()
        {
            if (disposed) return;
            if (hasMetrics) ApplyLayout(CurrentLayout);
            else Refresh(true);
        }

        public static MobileUiCoordinator Attach(UIDocument document, MobileScreenKind screenKind)
        {
            if (document == null || !MobileWindowing.ShouldUseMobileLayout) return null;
            return new MobileUiCoordinator(document, screenKind, MobileWindowing.CreateProvider());
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            pollItem?.Pause();
            documentRoot?.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
#if STARFALL_ANDROID_CI
            ciRenderProbe?.RemoveFromHierarchy();
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            androidCompositor?.Dispose();
#endif
            ResetHingeOverrides();
            if (contentRoot != null)
            {
                contentRoot.RemoveFromClassList("mobile");
                contentRoot.RemoveFromClassList("compact-landscape");
                contentRoot.RemoveFromClassList("square-expanded");
                contentRoot.RemoveFromClassList("hinge-vertical");
                contentRoot.RemoveFromClassList("hinge-horizontal");
                contentRoot.style.left = StyleKeyword.Null;
                contentRoot.style.top = StyleKeyword.Null;
                contentRoot.style.right = StyleKeyword.Null;
                contentRoot.style.bottom = StyleKeyword.Null;
            }
            ResetAbsoluteRect(documentRoot);
            if (documentRoot != null)
            {
                documentRoot.style.flexGrow = StyleKeyword.Null;
                documentRoot.style.flexShrink = StyleKeyword.Null;
            }
            if (document != null) document.panelSettings = originalPanelSettings;
        }

        private static VisualElement FindContentRoot(VisualElement root, MobileScreenKind kind)
        {
            var name = kind switch
            {
                MobileScreenKind.MainMenu => "main-menu",
                MobileScreenKind.Station => "station-ui",
                MobileScreenKind.Space => "space-hud",
                _ => string.Empty,
            };
            return string.IsNullOrEmpty(name) ? root : root.Q<VisualElement>(name) ?? root;
        }

        private void OnGeometryChanged(GeometryChangedEvent _) => Refresh(false);

        private void Refresh(bool force)
        {
            if (disposed || metricsProvider == null || contentRoot == null) return;
            var metrics = metricsProvider.GetMetrics();
            if (!force && hasMetrics && metrics.Equals(lastMetrics))
            {
#if STARFALL_ANDROID_CI
                // Overlay visibility changes without changing window metrics. Refresh the
                // evidence file on the polling cadence so ADB can capture each UI state.
                WriteCiLayoutEvidence(CurrentLayout);
#endif
                return;
            }

            lastMetrics = metrics;
            hasMetrics = true;
            CurrentLayout = MobileLayoutPolicy.Calculate(metrics);
            ApplyLayout(CurrentLayout);
        }

        private void ApplyLayout(MobileLayout layout)
        {
            // UIDocument normally inherits its panel size. During an Android
            // single-scene transition the new document can attach while the
            // shared PanelSettings target texture is being replaced, leaving
            // its Yoga root at NaN even though the panel already has valid dp
            // bounds. Anchor it explicitly so the first Space layout is usable.
            SetAbsoluteRect(documentRoot, layout.ScreenBoundsDp);
            documentRoot.style.flexGrow = 0f;
            documentRoot.style.flexShrink = 0f;

            contentRoot.AddToClassList("mobile");
            contentRoot.EnableInClassList("compact-landscape",
                layout.Mode == MobileLayoutMode.CompactLandscape);
            contentRoot.EnableInClassList("square-expanded",
                layout.Mode == MobileLayoutMode.SquareExpanded);
            contentRoot.EnableInClassList("hinge-vertical",
                layout.HasSeparatingFeature &&
                layout.FoldingOrientation == FoldingFeatureOrientation.Vertical);
            contentRoot.EnableInClassList("hinge-horizontal",
                layout.HasSeparatingFeature &&
                layout.FoldingOrientation == FoldingFeatureOrientation.Horizontal);

            contentRoot.style.left = layout.SafeInsetsDp.Left;
            contentRoot.style.top = layout.SafeInsetsDp.Top;
            contentRoot.style.right = layout.SafeInsetsDp.Right;
            contentRoot.style.bottom = layout.SafeInsetsDp.Bottom;

            ConfigureModuleRack(layout.Mode);
            ResetHingeOverrides();
            if (layout.HasSeparatingFeature) ApplyHingeLayout(layout);
#if STARFALL_ANDROID_CI
            contentRoot.schedule.Execute(() => WriteCiLayoutEvidence(layout));
#endif
        }

        private void ConfigureModuleRack(MobileLayoutMode mode)
        {
            if (screenKind != MobileScreenKind.Space) return;
            var rack = contentRoot.Q<ScrollView>("module-rack");
            if (rack == null) return;

            var compact = mode == MobileLayoutMode.CompactLandscape;
            rack.mode = compact ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical;
            rack.horizontalScrollerVisibility = compact
                ? ScrollerVisibility.Auto
                : ScrollerVisibility.Hidden;
            rack.verticalScrollerVisibility = compact
                ? ScrollerVisibility.Hidden
                : ScrollerVisibility.Auto;
            rack.contentContainer.style.flexDirection = FlexDirection.Row;
            rack.contentContainer.style.flexWrap = compact ? Wrap.NoWrap : Wrap.Wrap;
            rack.contentContainer.style.alignItems = Align.FlexStart;
        }

#if UNITY_EDITOR || STARFALL_ANDROID_CI
#if STARFALL_ANDROID_CI
        private static VisualElement CreateCiRenderProbe(VisualElement root)
        {
            var probe = new VisualElement
            {
                name = "starfall-ci-render-probe",
                pickingMode = PickingMode.Ignore,
            };
            probe.style.position = Position.Absolute;
            probe.style.left = 4f;
            probe.style.top = 4f;
            probe.style.width = 20f;
            probe.style.height = 20f;
            probe.style.backgroundColor = new StyleColor(new Color(1f, 0f, 1f, 1f));
            root.Add(probe);
            probe.BringToFront();
            return probe;
        }
#endif

        private void WriteCiLayoutEvidence(MobileLayout layout)
        {
            if (disposed || contentRoot == null) return;
            try
            {
                var json = new StringBuilder(4096);
                json.Append('{');
                AppendString(json, "screen", screenKind.ToString());
                json.Append(',');
                AppendString(json, "mode", layout.Mode.ToString());
                json.Append(",\"density\":").Append(Number(layout.Density));
                json.Append(",\"safeBoundsDp\":");
                AppendRect(json, layout.SafeBoundsDp);
                json.Append(",\"foldingBoundsDp\":");
                AppendRect(json, layout.FoldingBoundsDp);
#if STARFALL_ANDROID_CI
                json.Append(",\"renderProbe\":");
                AppendRect(json, ciRenderProbe != null ? ciRenderProbe.worldBound : default);
                json.Append(",\"renderProbeRgb\":\"ff00ff\"");
                // AppRoot draws this independent IMGUI probe in physical pixels.
                // It distinguishes a detached UI Toolkit render chain from an
                // Android screenshot/compositor failure without weakening the gate.
                json.Append(",\"frameProbePx\":");
                AppendRect(json, new Rect(80f, 8f, 40f, 40f));
                json.Append(",\"frameProbeRgb\":\"00ff00\"");
                json.Append(",\"panelDiagnostics\":{");
                json.Append("\"documentEnabled\":")
                    .Append(document != null && document.enabled ? "true" : "false");
                json.Append(",\"documentAttached\":")
                    .Append(documentRoot?.panel != null ? "true" : "false");
                json.Append(",\"contentAttached\":")
                    .Append(contentRoot?.panel != null ? "true" : "false");
                json.Append(",\"samePanel\":")
                    .Append(documentRoot?.panel != null &&
                            ReferenceEquals(documentRoot.panel, contentRoot?.panel)
                        ? "true"
                        : "false");
                json.Append(",\"targetTexture\":")
                    .Append(document?.panelSettings?.targetTexture != null ? "true" : "false");
                json.Append(',');
                AppendString(json, "panelSettings", document?.panelSettings?.name ?? string.Empty);
                json.Append(',');
                AppendString(json, "scaleMode",
                    document?.panelSettings?.scaleMode.ToString() ?? string.Empty);
                json.Append(",\"sortingOrder\":")
                    .Append(Number(document?.panelSettings?.sortingOrder ?? 0f));
                json.Append(",\"documentRootBoundsDp\":");
                AppendRect(json, documentRoot != null ? documentRoot.worldBound : default);
                json.Append(",\"panelRootBoundsDp\":");
                AppendRect(json, documentRoot?.parent != null
                    ? documentRoot.parent.worldBound
                    : default);
                json.Append(",\"documentOpacity\":")
                    .Append(Number(documentRoot?.resolvedStyle.opacity ?? 0f));
                json.Append(',');
                AppendString(json, "documentDisplay",
                    documentRoot?.resolvedStyle.display.ToString() ?? string.Empty);
                json.Append(',');
                AppendString(json, "documentVisibility",
                    documentRoot?.resolvedStyle.visibility.ToString() ?? string.Empty);
                json.Append('}');
#endif
                json.Append(",\"controls\":[");
                var first = true;
                var scrollViews = contentRoot.Query<ScrollView>().ToList();
                void AppendControl(VisualElement control)
                {
                    var rawBounds = control.worldBound;
                    var visible = TryGetVisibleBoundsInContent(control, scrollViews, out var visibleBounds);
                    if (!first) json.Append(',');
                    first = false;
                    json.Append('{');
                    AppendString(json, "name", control.name ?? string.Empty);
                    json.Append(",\"boundsDp\":");
                    AppendRect(json, rawBounds);
                    json.Append(",\"visibleBoundsDp\":");
                    AppendRect(json, visibleBounds);
                    json.Append(",\"visible\":").Append(visible ? "true" : "false");
                    json.Append(",\"fullyVisible\":")
                        .Append(visible && RectApproximately(rawBounds, visibleBounds) ? "true" : "false");
                    json.Append(",\"inScrollView\":")
                        .Append(IsInsideScrollView(control, scrollViews) ? "true" : "false");
                    json.Append(",\"enabled\":").Append(control.enabledInHierarchy ? "true" : "false");
                    json.Append('}');
                }
                contentRoot.Query<Button>().ForEach(AppendControl);
                contentRoot.Query<TextField>().ForEach(AppendControl);
                contentRoot.Query<Slider>().ForEach(AppendControl);
                contentRoot.Query<Toggle>().ForEach(AppendControl);
                json.Append("],\"surfaces\":[");
                first = true;
                foreach (var name in new[]
                         {
                             "menu-card", "station-services", "overview-panel", "target-panel",
                             "combat-log-scroll", "settings-card", "starmap-card", "journal-card",
                             "death-card", "confirmation-card"
                         })
                {
                    var surface = contentRoot.Q<VisualElement>(name);
                    if (surface == null) continue;
                    var visible = TryGetVisibleBoundsInContent(surface, scrollViews, out var visibleBounds);
                    if (!first) json.Append(',');
                    first = false;
                    json.Append('{');
                    AppendString(json, "name", name);
                    json.Append(",\"boundsDp\":");
                    AppendRect(json, surface.worldBound);
                    json.Append(",\"visibleBoundsDp\":");
                    AppendRect(json, visibleBounds);
                    json.Append(",\"visible\":").Append(visible ? "true" : "false");
                    json.Append(",\"fullyVisible\":")
                        .Append(visible && RectApproximately(surface.worldBound, visibleBounds)
                            ? "true"
                            : "false");
                    json.Append('}');
                }
                json.Append("],\"texts\":[");
                first = true;
                foreach (var textElement in contentRoot.Query<TextElement>().ToList())
                {
                    if (!ContainsReadableText(textElement.text)) continue;
                    var visible = TryGetVisibleBoundsInContent(
                        textElement, scrollViews, out var visibleBounds);
                    if (!first) json.Append(',');
                    first = false;
                    json.Append('{');
                    AppendString(json, "name", textElement.name ?? string.Empty);
                    json.Append(',');
                    AppendString(json, "text", textElement.text ?? string.Empty);
                    json.Append(",\"fontSizeDp\":").Append(Number(textElement.resolvedStyle.fontSize));
                    json.Append(",\"boundsDp\":");
                    AppendRect(json, textElement.worldBound);
                    json.Append(",\"visibleBoundsDp\":");
                    AppendRect(json, visibleBounds);
                    json.Append(",\"visible\":").Append(visible ? "true" : "false");
                    json.Append(",\"fullyVisible\":")
                        .Append(visible && RectApproximately(textElement.worldBound, visibleBounds)
                            ? "true"
                            : "false");
                    json.Append(",\"inScrollView\":")
                        .Append(IsInsideScrollView(textElement, scrollViews) ? "true" : "false");
                    json.Append('}');
                }
                json.Append("]}");
                var payload = json.ToString();
                if (string.Equals(payload, lastCiLayoutEvidence, StringComparison.Ordinal)) return;
                lastCiLayoutEvidence = payload;
                var path = Path.Combine(Application.persistentDataPath,
                    $"starfall-ci-layout-{screenKind}.json");
                File.WriteAllText(path, payload);
                Debug.Log($"STARFALL_ANDROID_CI_UI_LAYOUT={screenKind}:{path}");
            }
            catch (Exception exception)
            {
                Debug.LogError("Could not write Android CI UI layout evidence: " + exception.Message);
            }
        }

        private static void AppendString(StringBuilder builder, string key, string value)
        {
            builder.Append('\"').Append(EscapeJson(key)).Append("\":\"")
                .Append(EscapeJson(value)).Append('\"');
        }

        private static void AppendRect(StringBuilder builder, Rect rect)
        {
            builder.Append("{\"x\":").Append(Number(rect.x))
                .Append(",\"y\":").Append(Number(rect.y))
                .Append(",\"width\":").Append(Number(rect.width))
                .Append(",\"height\":").Append(Number(rect.height)).Append('}');
        }

        private static string Number(float value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string EscapeJson(string value) => (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");

        private static bool ContainsReadableText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (var character in value)
                if (char.IsLetterOrDigit(character)) return true;
            return false;
        }

        private bool TryGetVisibleBoundsInContent(
            VisualElement element,
            IReadOnlyList<ScrollView> scrollViews,
            out Rect visibleBounds)
        {
            visibleBounds = default;
            for (var current = element; current != null; current = current.parent)
            {
                if (current.resolvedStyle.display == DisplayStyle.None ||
                    current.resolvedStyle.visibility == Visibility.Hidden)
                    return false;
                if (ReferenceEquals(current, contentRoot))
                {
                    if (!TryIntersect(element.worldBound, contentRoot.worldBound, out visibleBounds))
                        return false;
                    foreach (var scrollView in scrollViews)
                    {
                        if (!scrollView.contentContainer.Contains(element)) continue;
                        if (!TryIntersect(visibleBounds, scrollView.contentViewport.worldBound,
                                out visibleBounds))
                            return false;
                    }
                    return true;
                }
            }
            return false;
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

        private static bool IsInsideScrollView(
            VisualElement element,
            IReadOnlyList<ScrollView> scrollViews)
        {
            foreach (var scrollView in scrollViews)
                if (scrollView.contentContainer.Contains(element)) return true;
            return false;
        }

        private static bool RectApproximately(Rect first, Rect second)
        {
            const float toleranceDp = 0.05f;
            return Mathf.Abs(first.x - second.x) <= toleranceDp &&
                   Mathf.Abs(first.y - second.y) <= toleranceDp &&
                   Mathf.Abs(first.width - second.width) <= toleranceDp &&
                   Mathf.Abs(first.height - second.height) <= toleranceDp;
        }
#endif

        private void ApplyHingeLayout(MobileLayout layout)
        {
            var primary = ToContentLocal(layout.PrimaryPaneDp, layout.SafeBoundsDp);
            var secondary = ToContentLocal(layout.SecondaryPaneDp, layout.SafeBoundsDp);
            switch (screenKind)
            {
                case MobileScreenKind.MainMenu:
                    ApplyMainMenuHinge(primary, secondary);
                    break;
                case MobileScreenKind.Station:
                    ApplyStationHinge(layout, primary, secondary);
                    break;
                case MobileScreenKind.Space:
                    ApplySpaceHinge(layout, primary, secondary);
                    break;
            }
        }

        private void ApplyMainMenuHinge(Rect primary, Rect secondary)
        {
            var pane = Area(primary) >= Area(secondary) ? primary : secondary;
            var card = contentRoot.Q<VisualElement>("menu-card");
            if (card == null) return;
            var width = Mathf.Min(720f, Mathf.Max(0f, pane.width - 24f));
            // A horizontal tabletop pane is only about 404 dp tall at the 2480 x 2200
            // acceptance profile. Keep the 48 dp action row wholly above the fold.
            var topInset = pane.width > pane.height ? 4f : 12f;
            card.style.position = Position.Absolute;
            card.style.left = pane.xMin + Mathf.Max(12f, (pane.width - width) * 0.5f);
            card.style.top = pane.yMin + topInset;
            card.style.width = width;
            card.style.maxWidth = width;
            ConstrainOverlaysToPane(pane);
        }

        private void ApplyStationHinge(MobileLayout layout, Rect primary, Rect secondary)
        {
            var services = contentRoot.Q<VisualElement>("station-services");
            if (layout.FoldingOrientation == FoldingFeatureOrientation.Vertical)
            {
                SetAbsoluteRect(services, InsetTopBottom(secondary, 8f, 88f, 46f));
                SetAbsoluteRect(contentRoot.Q<VisualElement>("station-footer"),
                    new Rect(primary.xMin + 8f, primary.yMax - 46f,
                        Mathf.Max(0f, primary.width - 16f), 34f));
                var info = contentRoot.Q<VisualElement>("station-header-info");
                var actions = contentRoot.Q<VisualElement>("station-header-actions");
                var spacer = contentRoot.Q<VisualElement>("station-header-spacer");
                if (info != null) info.style.width = Mathf.Max(0f, primary.width - 28f);
                if (actions != null) actions.style.width = Mathf.Max(0f, secondary.width - 28f);
                if (spacer != null)
                {
                    spacer.style.flexGrow = 0f;
                    spacer.style.width = Mathf.Max(12f, secondary.xMin - primary.xMax + 28f);
                }
            }
            else
            {
                SetAbsoluteRect(services, Inset(secondary, 8f));
            }

            ConstrainOverlaysToPane(primary);
        }

        private void ApplySpaceHinge(MobileLayout layout, Rect primary, Rect secondary)
        {
            if (layout.FoldingOrientation == FoldingFeatureOrientation.Vertical)
            {
                var top = 70f;
                SetAbsoluteRect(contentRoot.Q<VisualElement>("overview-panel"),
                    InsetTopBottom(primary, 8f, top, 172f));
                SetAbsoluteRect(contentRoot.Q<VisualElement>("ship-status"),
                    new Rect(primary.xMin + 8f, primary.yMax - 164f,
                        Mathf.Min(220f, primary.width - 16f), 156f));
                SetAbsoluteRect(contentRoot.Q<VisualElement>("module-rack"),
                    new Rect(primary.xMin + Mathf.Min(236f, primary.width * 0.52f), primary.yMax - 126f,
                        Mathf.Max(0f, primary.width - Mathf.Min(244f, primary.width * 0.54f)), 120f));
                SetAbsoluteRect(contentRoot.Q<VisualElement>("target-panel"),
                    new Rect(secondary.xMin + 8f, secondary.yMin + top,
                        Mathf.Max(0f, secondary.width - 16f), Mathf.Min(180f, secondary.height - top - 8f)));
                SetAbsoluteRect(contentRoot.Q<VisualElement>("combat-log-scroll"),
                    new Rect(secondary.xMin + 8f, secondary.yMax - 128f,
                        Mathf.Max(0f, secondary.width - 16f), 120f));
                ApplyTopbarSplit(primary, secondary);
            }
            else
            {
                var overviewWidth = Mathf.Max(300f, primary.width * 0.48f);
                SetAbsoluteRect(contentRoot.Q<VisualElement>("overview-panel"),
                    new Rect(primary.xMin + 8f, primary.yMin + 70f,
                        Mathf.Min(overviewWidth, primary.width - 16f), Mathf.Max(0f, primary.height - 78f)));
                SetAbsoluteRect(contentRoot.Q<VisualElement>("target-panel"),
                    new Rect(primary.xMax - Mathf.Min(330f, primary.width * 0.42f) - 8f, primary.yMin + 70f,
                        Mathf.Min(330f, primary.width * 0.42f), Mathf.Max(0f, primary.height - 78f)));
                SetAbsoluteRect(contentRoot.Q<VisualElement>("ship-status"),
                    new Rect(secondary.xMin + 8f, secondary.yMax - 126f, 220f, 118f));
                SetAbsoluteRect(contentRoot.Q<VisualElement>("module-rack"),
                    new Rect(secondary.xMin + 238f, secondary.yMax - 126f,
                        Mathf.Max(0f, secondary.width - 586f), 120f));
                SetAbsoluteRect(contentRoot.Q<VisualElement>("combat-log-scroll"),
                    new Rect(secondary.xMax - 338f, secondary.yMax - 128f, 330f, 120f));
            }


            ConstrainOverlaysToPane(primary);
        }

        private void ConstrainOverlaysToPane(Rect pane)
        {
            foreach (var name in new[]
                     {
                         "settings-overlay", "mobile-confirmation", "map-overlay",
                         "journal-overlay", "death-overlay"
                     })
                SetAbsoluteRect(contentRoot.Q<VisualElement>(name), pane);
        }

        private void ApplyTopbarSplit(Rect primary, Rect secondary)
        {
            var left = contentRoot.Q<VisualElement>("topbar-left");
            var right = contentRoot.Q<VisualElement>("topbar-right");
            var gap = contentRoot.Q<VisualElement>("topbar-gap");
            if (left != null) left.style.width = Mathf.Max(0f, primary.width - 24f);
            if (right != null) right.style.width = Mathf.Max(0f, secondary.width - 24f);
            if (gap != null)
            {
                gap.style.flexGrow = 0f;
                gap.style.width = Mathf.Max(8f, secondary.xMin - primary.xMax + 16f);
            }
        }

        private void ResetHingeOverrides()
        {
            if (contentRoot == null) return;
            foreach (var name in new[]
                     {
                         "menu-card", "station-services", "overview-panel", "target-panel",
                         "station-footer", "ship-status", "module-rack", "combat-log-scroll", "settings-overlay",
                         "mobile-confirmation", "map-overlay", "journal-overlay", "death-overlay"
                     })
                ResetAbsoluteRect(contentRoot.Q<VisualElement>(name));

            foreach (var name in new[]
                     {
                         "station-header-info", "station-header-actions", "station-header-spacer",
                         "topbar-left", "topbar-right", "topbar-gap"
                     })
            {
                var element = contentRoot.Q<VisualElement>(name);
                if (element == null) continue;
                element.style.width = StyleKeyword.Null;
                element.style.flexGrow = StyleKeyword.Null;
            }
        }

        private static void SetAbsoluteRect(VisualElement element, Rect rect)
        {
            if (element == null) return;
            element.style.position = Position.Absolute;
            element.style.left = rect.x;
            element.style.top = rect.y;
            element.style.right = StyleKeyword.Auto;
            element.style.bottom = StyleKeyword.Auto;
            element.style.width = Mathf.Max(0f, rect.width);
            element.style.height = Mathf.Max(0f, rect.height);
        }

        private static void ResetAbsoluteRect(VisualElement element)
        {
            if (element == null) return;
            element.style.position = StyleKeyword.Null;
            element.style.left = StyleKeyword.Null;
            element.style.top = StyleKeyword.Null;
            element.style.right = StyleKeyword.Null;
            element.style.bottom = StyleKeyword.Null;
            element.style.width = StyleKeyword.Null;
            element.style.height = StyleKeyword.Null;
            element.style.maxWidth = StyleKeyword.Null;
        }

        private static Rect ToContentLocal(Rect pane, Rect safe) =>
            new Rect(pane.x - safe.x, pane.y - safe.y, pane.width, pane.height);

        private static Rect Inset(Rect rect, float amount) => new Rect(
            rect.x + amount,
            rect.y + amount,
            Mathf.Max(0f, rect.width - amount * 2f),
            Mathf.Max(0f, rect.height - amount * 2f));

        private static Rect InsetTopBottom(Rect rect, float horizontal, float top, float bottom) => new Rect(
            rect.x + horizontal,
            rect.y + top,
            Mathf.Max(0f, rect.width - horizontal * 2f),
            Mathf.Max(0f, rect.height - top - bottom));

        private static float Area(Rect rect) => rect.width * rect.height;
    }
}
