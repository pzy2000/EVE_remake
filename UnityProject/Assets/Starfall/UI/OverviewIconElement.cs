using UnityEngine;
using UnityEngine.UIElements;

namespace Starfall.UI
{
    /// <summary>
    /// Small code-drawn tactical glyph used by the Overview. The geometry is deliberately original
    /// and does not depend on imported textures, so it remains crisp at every panel scale.
    /// </summary>
    public sealed class OverviewIconElement : VisualElement
    {
        private OverviewKind kind;
        private OverviewDisposition disposition;
        private OverviewStateFlags states;

        public OverviewIconElement()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("overview-object-icon");
            generateVisualContent += Draw;
        }

        public void Bind(UiOverviewContact contact)
        {
            var nextKind = contact?.Kind ?? OverviewKind.Ship;
            var nextDisposition = contact?.Disposition ?? OverviewDisposition.None;
            var nextStates = contact?.States ?? OverviewStateFlags.None;
            if (kind == nextKind && disposition == nextDisposition && states == nextStates) return;

            kind = nextKind;
            disposition = nextDisposition;
            states = nextStates;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width <= 1f || rect.height <= 1f) return;

            var painter = context.painter2D;
            var center = rect.center;
            var radius = Mathf.Max(3f, Mathf.Min(rect.width, rect.height) * 0.34f);
            var color = DispositionColor(disposition);
            painter.lineWidth = 1.35f;
            painter.strokeColor = color;
            painter.fillColor = new Color(color.r, color.g, color.b, 0.18f);

            switch (kind)
            {
                case OverviewKind.Ship:
                    DrawShip(painter, center, radius);
                    break;
                case OverviewKind.Station:
                    DrawStation(painter, center, radius);
                    break;
                case OverviewKind.Stargate:
                    DrawGate(painter, center, radius);
                    break;
                case OverviewKind.AsteroidBelt:
                    DrawBelt(painter, center, radius);
                    break;
                case OverviewKind.Asteroid:
                    DrawAsteroid(painter, center, radius);
                    break;
                case OverviewKind.Planet:
                    DrawPlanet(painter, center, radius);
                    break;
                case OverviewKind.Moon:
                    DrawMoon(painter, center, radius);
                    break;
                case OverviewKind.Star:
                    DrawStar(painter, center, radius);
                    break;
            }

            if ((states & OverviewStateFlags.LockedByPlayer) != 0)
                DrawLockCorners(painter, center, radius + 2f);
            if ((states & OverviewStateFlags.MissionObjective) != 0)
                DrawMissionMarker(painter, rect);
            if ((states & OverviewStateFlags.TargetingPlayer) != 0)
                DrawThreatMarker(painter, rect);
        }

        private static Color DispositionColor(OverviewDisposition value)
        {
            switch (value)
            {
                case OverviewDisposition.Hostile:
                    return new Color32(255, 78, 86, 255);
                case OverviewDisposition.Friendly:
                    return new Color32(88, 228, 173, 255);
                case OverviewDisposition.Neutral:
                    return new Color32(174, 199, 210, 255);
                default:
                    return new Color32(89, 216, 249, 255);
            }
        }

        private static void DrawShip(Painter2D painter, Vector2 center, float radius)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x, center.y - radius));
            painter.LineTo(new Vector2(center.x + radius * 0.78f, center.y + radius));
            painter.LineTo(new Vector2(center.x, center.y + radius * 0.55f));
            painter.LineTo(new Vector2(center.x - radius * 0.78f, center.y + radius));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }

        private static void DrawStation(Painter2D painter, Vector2 center, float radius)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x - radius, center.y - radius));
            painter.LineTo(new Vector2(center.x + radius, center.y - radius));
            painter.LineTo(new Vector2(center.x + radius, center.y + radius));
            painter.LineTo(new Vector2(center.x - radius, center.y + radius));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x - radius * 0.55f, center.y));
            painter.LineTo(new Vector2(center.x + radius * 0.55f, center.y));
            painter.MoveTo(new Vector2(center.x, center.y - radius * 0.55f));
            painter.LineTo(new Vector2(center.x, center.y + radius * 0.55f));
            painter.Stroke();
        }

        private static void DrawGate(Painter2D painter, Vector2 center, float radius)
        {
            DrawCircle(painter, center, radius, false);
            DrawCircle(painter, center, radius * 0.48f, false);
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x - radius, center.y));
            painter.LineTo(new Vector2(center.x - radius * 0.48f, center.y));
            painter.MoveTo(new Vector2(center.x + radius * 0.48f, center.y));
            painter.LineTo(new Vector2(center.x + radius, center.y));
            painter.Stroke();
        }

        private static void DrawBelt(Painter2D painter, Vector2 center, float radius)
        {
            DrawCircle(painter, new Vector2(center.x - radius * 0.55f, center.y + radius * 0.18f), radius * 0.4f, true);
            DrawCircle(painter, new Vector2(center.x + radius * 0.1f, center.y - radius * 0.42f), radius * 0.34f, true);
            DrawCircle(painter, new Vector2(center.x + radius * 0.62f, center.y + radius * 0.3f), radius * 0.3f, true);
        }

        private static void DrawAsteroid(Painter2D painter, Vector2 center, float radius)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x - radius * 0.65f, center.y - radius));
            painter.LineTo(new Vector2(center.x + radius * 0.45f, center.y - radius * 0.78f));
            painter.LineTo(new Vector2(center.x + radius, center.y - radius * 0.05f));
            painter.LineTo(new Vector2(center.x + radius * 0.48f, center.y + radius));
            painter.LineTo(new Vector2(center.x - radius * 0.58f, center.y + radius * 0.76f));
            painter.LineTo(new Vector2(center.x - radius, center.y));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }

        private static void DrawPlanet(Painter2D painter, Vector2 center, float radius)
        {
            DrawCircle(painter, center, radius * 0.76f, true);
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x - radius, center.y + radius * 0.28f));
            painter.QuadraticCurveTo(new Vector2(center.x, center.y + radius * 0.78f), new Vector2(center.x + radius, center.y - radius * 0.2f));
            painter.Stroke();
        }

        private static void DrawMoon(Painter2D painter, Vector2 center, float radius)
        {
            DrawCircle(painter, center, radius * 0.72f, true);
            painter.BeginPath();
            painter.Arc(new Vector2(center.x + radius * 0.18f, center.y), radius * 0.52f,
                Angle.Degrees(80f), Angle.Degrees(280f));
            painter.Stroke();
        }

        private static void DrawStar(Painter2D painter, Vector2 center, float radius)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x, center.y - radius));
            painter.LineTo(new Vector2(center.x + radius * 0.28f, center.y - radius * 0.28f));
            painter.LineTo(new Vector2(center.x + radius, center.y));
            painter.LineTo(new Vector2(center.x + radius * 0.28f, center.y + radius * 0.28f));
            painter.LineTo(new Vector2(center.x, center.y + radius));
            painter.LineTo(new Vector2(center.x - radius * 0.28f, center.y + radius * 0.28f));
            painter.LineTo(new Vector2(center.x - radius, center.y));
            painter.LineTo(new Vector2(center.x - radius * 0.28f, center.y - radius * 0.28f));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }

        private static void DrawCircle(Painter2D painter, Vector2 center, float radius, bool fill)
        {
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            if (fill) painter.Fill();
            painter.Stroke();
        }

        private static void DrawLockCorners(Painter2D painter, Vector2 center, float radius)
        {
            painter.strokeColor = new Color32(92, 225, 255, 255);
            painter.lineWidth = 1f;
            var length = 3f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x - radius, center.y - radius + length));
            painter.LineTo(new Vector2(center.x - radius, center.y - radius));
            painter.LineTo(new Vector2(center.x - radius + length, center.y - radius));
            painter.MoveTo(new Vector2(center.x + radius - length, center.y - radius));
            painter.LineTo(new Vector2(center.x + radius, center.y - radius));
            painter.LineTo(new Vector2(center.x + radius, center.y - radius + length));
            painter.MoveTo(new Vector2(center.x - radius, center.y + radius - length));
            painter.LineTo(new Vector2(center.x - radius, center.y + radius));
            painter.LineTo(new Vector2(center.x - radius + length, center.y + radius));
            painter.MoveTo(new Vector2(center.x + radius - length, center.y + radius));
            painter.LineTo(new Vector2(center.x + radius, center.y + radius));
            painter.LineTo(new Vector2(center.x + radius, center.y + radius - length));
            painter.Stroke();
        }

        private static void DrawMissionMarker(Painter2D painter, Rect rect)
        {
            var center = new Vector2(rect.xMax - 3.3f, rect.yMax - 3.3f);
            painter.fillColor = new Color32(255, 184, 66, 255);
            painter.strokeColor = painter.fillColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(center.x, center.y - 2.1f));
            painter.LineTo(new Vector2(center.x + 2.1f, center.y));
            painter.LineTo(new Vector2(center.x, center.y + 2.1f));
            painter.LineTo(new Vector2(center.x - 2.1f, center.y));
            painter.ClosePath();
            painter.Fill();
        }

        private static void DrawThreatMarker(Painter2D painter, Rect rect)
        {
            painter.fillColor = new Color32(255, 55, 64, 255);
            painter.strokeColor = painter.fillColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMax - 5.5f, rect.yMin + 1.5f));
            painter.LineTo(new Vector2(rect.xMax - 1.2f, rect.yMin + 1.5f));
            painter.LineTo(new Vector2(rect.xMax - 1.2f, rect.yMin + 5.8f));
            painter.ClosePath();
            painter.Fill();
        }
    }
}
