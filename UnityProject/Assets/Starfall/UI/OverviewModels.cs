using System;
using System.Collections.Generic;

namespace Starfall.UI
{
    public enum OverviewCategory
    {
        Ship,
        Structure,
        Resource,
        Celestial
    }

    public enum OverviewKind
    {
        Ship,
        Station,
        Stargate,
        AsteroidBelt,
        Asteroid,
        Planet,
        Moon,
        Star
    }

    public enum OverviewDisposition
    {
        None,
        Hostile,
        Friendly,
        Neutral
    }

    [Flags]
    public enum OverviewStateFlags
    {
        None = 0,
        TargetingPlayer = 1 << 0,
        LockedByPlayer = 1 << 1,
        MissionObjective = 1 << 2,
        Selected = 1 << 3,
        Elite = 1 << 4,
        LawEnforcement = 1 << 5
    }

    [Flags]
    public enum OverviewActionFlags
    {
        None = 0,
        Select = 1 << 0,
        Approach = 1 << 1,
        Orbit = 1 << 2,
        Warp = 1 << 3,
        Lock = 1 << 4,
        Dock = 1 << 5,
        Jump = 1 << 6,
        Mine = 1 << 7
    }

    public enum OverviewPresetId
    {
        General,
        Combat,
        Mining,
        Travel,
        All
    }

    public enum OverviewSortColumn
    {
        Threat,
        Distance,
        Name,
        Type,
        Velocity
    }

    public enum OverviewSortDirection
    {
        Ascending,
        Descending
    }

    public enum OverviewVisibility
    {
        FilterOut,
        ShowByDefault,
        AlwaysShow
    }

    /// <summary>
    /// Mutable presentation data for a single Overview row. Instances are intentionally reusable so
    /// telemetry can update distance, velocity and state without rebuilding the UI collection.
    /// </summary>
    [Serializable]
    public sealed class UiOverviewContact
    {
        public string Id = string.Empty;
        public OverviewCategory Category;
        public OverviewKind Kind;
        public OverviewDisposition Disposition;
        public OverviewStateFlags States;
        public OverviewActionFlags Actions;
        public string Name = string.Empty;
        public string Type = string.Empty;
        public string Detail = string.Empty;
        public string Accent = "#4edbff";
        public double DistanceMeters;
        public double VelocityMetersPerSecond;
        public float SizeMeters;

        public void CopyTelemetryFrom(UiOverviewContact source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Disposition = source.Disposition;
            States = source.States;
            DistanceMeters = source.DistanceMeters;
            VelocityMetersPerSecond = source.VelocityMetersPerSecond;
            SizeMeters = source.SizeMeters;
            Detail = source.Detail;
            Accent = source.Accent;
        }
    }

    /// <summary>
    /// Fixed EVE-inspired presets. Type admission is a hard gate: state rules never resurrect a kind
    /// excluded by a preset. Combat, Mining and Travel keep threats, mission objectives and player
    /// locks visible while filtering ordinary friendly and neutral ships.
    /// </summary>
    public static class OverviewRules
    {
        private const OverviewStateFlags AlwaysShowShipStates =
            OverviewStateFlags.TargetingPlayer |
            OverviewStateFlags.LockedByPlayer |
            OverviewStateFlags.MissionObjective;

        public static OverviewSortColumn DefaultSortColumn(OverviewPresetId preset) =>
            OverviewSortColumn.Distance;

        public static OverviewSortDirection DefaultSortDirection(OverviewPresetId preset) =>
            OverviewSortDirection.Ascending;

        public static OverviewCategory CategoryFor(OverviewKind kind)
        {
            switch (kind)
            {
                case OverviewKind.Ship:
                    return OverviewCategory.Ship;
                case OverviewKind.Station:
                case OverviewKind.Stargate:
                    return OverviewCategory.Structure;
                case OverviewKind.AsteroidBelt:
                case OverviewKind.Asteroid:
                    return OverviewCategory.Resource;
                case OverviewKind.Planet:
                case OverviewKind.Moon:
                case OverviewKind.Star:
                    return OverviewCategory.Celestial;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public static bool AllowsKind(OverviewPresetId preset, OverviewKind kind)
        {
            switch (preset)
            {
                case OverviewPresetId.General:
                    return kind == OverviewKind.Ship || kind == OverviewKind.Station ||
                           kind == OverviewKind.Stargate || kind == OverviewKind.AsteroidBelt;
                case OverviewPresetId.Combat:
                    return kind == OverviewKind.Ship;
                case OverviewPresetId.Mining:
                    return kind == OverviewKind.Ship || kind == OverviewKind.AsteroidBelt ||
                           kind == OverviewKind.Asteroid;
                case OverviewPresetId.Travel:
                    return kind == OverviewKind.Ship || kind == OverviewKind.Station ||
                           kind == OverviewKind.Stargate || kind == OverviewKind.AsteroidBelt ||
                           kind == OverviewKind.Planet || kind == OverviewKind.Moon ||
                           kind == OverviewKind.Star;
                case OverviewPresetId.All:
                    return kind >= OverviewKind.Ship && kind <= OverviewKind.Star;
                default:
                    return false;
            }
        }

        public static OverviewVisibility EvaluateVisibility(OverviewPresetId preset, UiOverviewContact contact)
        {
            if (contact == null || !AllowsKind(preset, contact.Kind))
                return OverviewVisibility.FilterOut;

            if (preset == OverviewPresetId.All || preset == OverviewPresetId.General ||
                contact.Kind != OverviewKind.Ship)
                return OverviewVisibility.ShowByDefault;

            if ((contact.States & AlwaysShowShipStates) != 0 ||
                contact.Disposition == OverviewDisposition.Hostile)
                return OverviewVisibility.AlwaysShow;

            return OverviewVisibility.FilterOut;
        }

        public static bool IsVisible(OverviewPresetId preset, UiOverviewContact contact) =>
            EvaluateVisibility(preset, contact) != OverviewVisibility.FilterOut;

        /// <summary>
        /// Lower values are more urgent and therefore appear first in ascending Threat order.
        /// Selection and player locks are deliberately not danger states.
        /// </summary>
        public static int GetThreatPriority(UiOverviewContact contact)
        {
            if (contact == null) return int.MaxValue;
            if ((contact.States & OverviewStateFlags.TargetingPlayer) != 0) return 0;
            if ((contact.States & OverviewStateFlags.MissionObjective) != 0) return 1;
            if (contact.Disposition == OverviewDisposition.Hostile) return 2;
            if ((contact.States & OverviewStateFlags.LawEnforcement) != 0) return 3;
            if (contact.Disposition == OverviewDisposition.Friendly) return 4;
            if (contact.Disposition == OverviewDisposition.Neutral) return 5;
            return 6;
        }
    }

    /// <summary>
    /// Cached deterministic comparer. Only the requested primary column changes direction; equal
    /// primary values retain stable, human-friendly tie ordering ending in the stable contact ID.
    /// </summary>
    public sealed class OverviewContactComparer : IComparer<UiOverviewContact>
    {
        private const int SortColumnCount = (int)OverviewSortColumn.Velocity + 1;
        private static readonly OverviewContactComparer[] Comparers;

        private readonly OverviewSortColumn column;
        private readonly OverviewSortDirection direction;

        static OverviewContactComparer()
        {
            Comparers = new OverviewContactComparer[SortColumnCount * 2];
            for (var i = 0; i < SortColumnCount; i++)
            {
                Comparers[i * 2] = new OverviewContactComparer(
                    (OverviewSortColumn)i, OverviewSortDirection.Ascending);
                Comparers[i * 2 + 1] = new OverviewContactComparer(
                    (OverviewSortColumn)i, OverviewSortDirection.Descending);
            }
        }

        private OverviewContactComparer(OverviewSortColumn column, OverviewSortDirection direction)
        {
            this.column = column;
            this.direction = direction;
        }

        public static OverviewContactComparer Get(OverviewSortColumn column, OverviewSortDirection direction)
        {
            var columnIndex = (int)column;
            if (columnIndex < 0 || columnIndex >= SortColumnCount)
                throw new ArgumentOutOfRangeException(nameof(column), column, null);
            if (direction != OverviewSortDirection.Ascending && direction != OverviewSortDirection.Descending)
                throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            return Comparers[columnIndex * 2 + (direction == OverviewSortDirection.Descending ? 1 : 0)];
        }

        public int Compare(UiOverviewContact left, UiOverviewContact right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            var primary = ComparePrimary(left, right);
            if (primary != 0)
            {
                // Invalid telemetry is kept last in both directions so a missing value can never
                // displace an actionable contact at the top of the Overview.
                var hasInvalidMetric = IsMetricColumn(column) &&
                    (IsInvalidMetric(PrimaryMetric(left)) != IsInvalidMetric(PrimaryMetric(right)));
                return direction == OverviewSortDirection.Ascending || hasInvalidMetric ? primary : -primary;
            }

            var secondary = CompareSecondary(left, right);
            if (secondary != 0) return secondary;

            secondary = left.Kind.CompareTo(right.Kind);
            if (secondary != 0) return secondary;

            return StringComparer.Ordinal.Compare(left.Id ?? string.Empty, right.Id ?? string.Empty);
        }

        private int ComparePrimary(UiOverviewContact left, UiOverviewContact right)
        {
            switch (column)
            {
                case OverviewSortColumn.Threat:
                    return OverviewRules.GetThreatPriority(left).CompareTo(OverviewRules.GetThreatPriority(right));
                case OverviewSortColumn.Distance:
                    return CompareMetric(left.DistanceMeters, right.DistanceMeters);
                case OverviewSortColumn.Name:
                    return StringComparer.OrdinalIgnoreCase.Compare(left.Name ?? string.Empty, right.Name ?? string.Empty);
                case OverviewSortColumn.Type:
                    return StringComparer.OrdinalIgnoreCase.Compare(left.Type ?? string.Empty, right.Type ?? string.Empty);
                case OverviewSortColumn.Velocity:
                    return CompareMetric(left.VelocityMetersPerSecond, right.VelocityMetersPerSecond);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private int CompareSecondary(UiOverviewContact left, UiOverviewContact right)
        {
            switch (column)
            {
                case OverviewSortColumn.Threat:
                case OverviewSortColumn.Velocity:
                    var distance = CompareMetric(left.DistanceMeters, right.DistanceMeters);
                    return distance != 0 ? distance : CompareName(left, right);
                case OverviewSortColumn.Distance:
                    return CompareName(left, right);
                case OverviewSortColumn.Name:
                    var type = StringComparer.OrdinalIgnoreCase.Compare(left.Type ?? string.Empty, right.Type ?? string.Empty);
                    return type != 0 ? type : CompareMetric(left.DistanceMeters, right.DistanceMeters);
                case OverviewSortColumn.Type:
                    var name = CompareName(left, right);
                    return name != 0 ? name : CompareMetric(left.DistanceMeters, right.DistanceMeters);
                default:
                    return 0;
            }
        }

        private static int CompareName(UiOverviewContact left, UiOverviewContact right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Name ?? string.Empty, right.Name ?? string.Empty);

        private static int CompareMetric(double left, double right)
        {
            var leftInvalid = IsInvalidMetric(left);
            var rightInvalid = IsInvalidMetric(right);
            if (leftInvalid != rightInvalid) return leftInvalid ? 1 : -1;
            return left.CompareTo(right);
        }

        private double PrimaryMetric(UiOverviewContact contact) =>
            column == OverviewSortColumn.Distance ? contact.DistanceMeters : contact.VelocityMetersPerSecond;

        private static bool IsMetricColumn(OverviewSortColumn value) =>
            value == OverviewSortColumn.Distance || value == OverviewSortColumn.Velocity;

        private static bool IsInvalidMetric(double value) => double.IsNaN(value) || value < 0d;

    }

    public static class OverviewFilter
    {
        public static int Filter(
            IReadOnlyList<UiOverviewContact> source,
            List<UiOverviewContact> destination,
            OverviewPresetId preset)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (ReferenceEquals(source, destination))
                throw new ArgumentException("Source and destination must be different collections.", nameof(destination));

            destination.Clear();
            for (var i = 0; i < source.Count; i++)
            {
                var contact = source[i];
                if (OverviewRules.IsVisible(preset, contact)) destination.Add(contact);
            }
            return destination.Count;
        }

        public static void Sort(
            List<UiOverviewContact> contacts,
            OverviewSortColumn column,
            OverviewSortDirection direction)
        {
            if (contacts == null) throw new ArgumentNullException(nameof(contacts));
            contacts.Sort(OverviewContactComparer.Get(column, direction));
        }

        public static int FilterAndSort(
            IReadOnlyList<UiOverviewContact> source,
            List<UiOverviewContact> destination,
            OverviewPresetId preset,
            OverviewSortColumn column,
            OverviewSortDirection direction)
        {
            Filter(source, destination, preset);
            Sort(destination, column, direction);
            return destination.Count;
        }
    }
}
