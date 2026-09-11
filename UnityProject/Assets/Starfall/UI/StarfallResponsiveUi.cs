using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starfall.UI
{
    /// <summary>
    /// USS has no media queries, so screen breakpoints are applied from C#:
    /// the document root gets the <see cref="CompactClass"/> class whenever the
    /// layout drops below the 1920x1080 design reference, which covers foldable
    /// inner (near-square) and outer (ultra-wide) screens.
    /// </summary>
    public static class StarfallResponsiveUi
    {
        public const string CompactClass = "hud-compact";
        public const float DefaultOverviewRowHeight = 30f;
        public const float CompactOverviewRowHeight = 26f;
        public const float MinUiScale = 0.75f;
        public const float MaxUiScale = 1.25f;

        // Thresholds in design-reference units, so they are independent of the
        // player's UI scale preference and cannot jitter while the slider moves.
        private const float CompactReferenceWidth = 1550f;
        private const float CompactReferenceHeight = 1000f;

        public static IDisposable Attach(UIDocument document, ListView overviewList = null)
        {
            return new Binding(document, overviewList);
        }

        /// <param name="panelWidth">Root width from GeometryChangedEvent (panel space).</param>
        /// <param name="panelHeight">Root height from GeometryChangedEvent (panel space).</param>
        public static bool Evaluate(float panelWidth, float panelHeight, PanelSettings settings)
        {
            if (settings == null || panelWidth <= 0f || panelHeight <= 0f) return false;
            // Panel-space size times the user scale factor yields design-reference
            // units: the screen-size scale (ScaleWithScreenSize) is already baked
            // into the layout coordinates.
            return Evaluate(panelWidth * settings.scale, panelHeight * settings.scale);
        }

        public static bool Evaluate(float referenceWidth, float referenceHeight)
        {
            if (referenceWidth <= 0f || referenceHeight <= 0f) return false;
            return referenceWidth < CompactReferenceWidth || referenceHeight < CompactReferenceHeight;
        }

        private sealed class Binding : IDisposable
        {
            private readonly UIDocument document;
            private readonly ListView overviewList;
            private readonly VisualElement root;
            private bool compact;
            private bool applied;

            public Binding(UIDocument document, ListView overviewList)
            {
                this.document = document;
                this.overviewList = overviewList;
                root = document.rootVisualElement;
                if (root == null) return;
                root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged, TrickleDown.TrickleDown);
                Apply(root.resolvedStyle.width, root.resolvedStyle.height);
            }

            public void Dispose()
            {
                if (root == null) return;
                root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged, TrickleDown.TrickleDown);
                if (applied) root.RemoveFromClassList(CompactClass);
            }

            private void OnGeometryChanged(GeometryChangedEvent evt)
            {
                Apply(evt.newRect.width, evt.newRect.height);
            }

            private void Apply(float width, float height)
            {
                var nextCompact = Evaluate(width, height, document.panelSettings);
                if (applied && nextCompact == compact) return;
                compact = nextCompact;
                applied = true;
                root.EnableInClassList(CompactClass, compact);
                if (overviewList == null) return;
                overviewList.fixedItemHeight = compact ? CompactOverviewRowHeight : DefaultOverviewRowHeight;
                overviewList.RefreshItems();
            }
        }
    }
}
