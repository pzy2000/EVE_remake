using System;
using System.Collections.Generic;
using NUnit.Framework;
using Starfall.UI;

namespace Starfall.Tests.EditMode.UI
{
    public sealed class OverviewModelsTests
    {
        [TestCase(OverviewPresetId.General, OverviewKind.Ship, true)]
        [TestCase(OverviewPresetId.General, OverviewKind.Asteroid, false)]
        [TestCase(OverviewPresetId.General, OverviewKind.Planet, false)]
        [TestCase(OverviewPresetId.Combat, OverviewKind.Ship, true)]
        [TestCase(OverviewPresetId.Combat, OverviewKind.Station, false)]
        [TestCase(OverviewPresetId.Mining, OverviewKind.Ship, true)]
        [TestCase(OverviewPresetId.Mining, OverviewKind.AsteroidBelt, true)]
        [TestCase(OverviewPresetId.Mining, OverviewKind.Asteroid, true)]
        [TestCase(OverviewPresetId.Mining, OverviewKind.Station, false)]
        [TestCase(OverviewPresetId.Travel, OverviewKind.Station, true)]
        [TestCase(OverviewPresetId.Travel, OverviewKind.Stargate, true)]
        [TestCase(OverviewPresetId.Travel, OverviewKind.Star, true)]
        [TestCase(OverviewPresetId.Travel, OverviewKind.Asteroid, false)]
        [TestCase(OverviewPresetId.All, OverviewKind.Asteroid, true)]
        public void PresetKindMatrix_UsesHardTypeGate(
            OverviewPresetId preset, OverviewKind kind, bool expected)
        {
            Assert.That(OverviewRules.AllowsKind(preset, kind), Is.EqualTo(expected));
        }

        [Test]
        public void TypeGate_CannotBeOverriddenByCriticalState()
        {
            var hostileMissionAsteroid = Contact(
                "ore", OverviewKind.Asteroid, OverviewDisposition.Hostile,
                OverviewStateFlags.TargetingPlayer | OverviewStateFlags.MissionObjective);

            Assert.That(
                OverviewRules.EvaluateVisibility(OverviewPresetId.Combat, hostileMissionAsteroid),
                Is.EqualTo(OverviewVisibility.FilterOut));
        }

        [TestCase(OverviewPresetId.Combat)]
        [TestCase(OverviewPresetId.Mining)]
        [TestCase(OverviewPresetId.Travel)]
        public void SafetyPresets_FilterOrdinaryShipsButAlwaysShowThreatsAndExplicitTargets(
            OverviewPresetId preset)
        {
            var friendly = Contact("friendly", OverviewKind.Ship, OverviewDisposition.Friendly);
            var neutral = Contact("neutral", OverviewKind.Ship, OverviewDisposition.Neutral);
            var hostile = Contact("hostile", OverviewKind.Ship, OverviewDisposition.Hostile);
            var attacking = Contact(
                "attacking", OverviewKind.Ship, OverviewDisposition.Friendly,
                OverviewStateFlags.TargetingPlayer);
            var mission = Contact(
                "mission", OverviewKind.Ship, OverviewDisposition.Neutral,
                OverviewStateFlags.MissionObjective);
            var locked = Contact(
                "locked", OverviewKind.Ship, OverviewDisposition.Friendly,
                OverviewStateFlags.LockedByPlayer);

            Assert.That(OverviewRules.IsVisible(preset, friendly), Is.False);
            Assert.That(OverviewRules.IsVisible(preset, neutral), Is.False);
            Assert.That(OverviewRules.EvaluateVisibility(preset, hostile), Is.EqualTo(OverviewVisibility.AlwaysShow));
            Assert.That(OverviewRules.EvaluateVisibility(preset, attacking), Is.EqualTo(OverviewVisibility.AlwaysShow));
            Assert.That(OverviewRules.EvaluateVisibility(preset, mission), Is.EqualTo(OverviewVisibility.AlwaysShow));
            Assert.That(OverviewRules.EvaluateVisibility(preset, locked), Is.EqualTo(OverviewVisibility.AlwaysShow));
        }

        [Test]
        public void GeneralAndAll_ShowShipsWithoutDispositionFiltering()
        {
            var contacts = new[]
            {
                Contact("none", OverviewKind.Ship, OverviewDisposition.None),
                Contact("hostile", OverviewKind.Ship, OverviewDisposition.Hostile),
                Contact("friendly", OverviewKind.Ship, OverviewDisposition.Friendly),
                Contact("neutral", OverviewKind.Ship, OverviewDisposition.Neutral)
            };

            foreach (var contact in contacts)
            {
                Assert.That(OverviewRules.IsVisible(OverviewPresetId.General, contact), Is.True);
                Assert.That(OverviewRules.IsVisible(OverviewPresetId.All, contact), Is.True);
            }
        }

        [Test]
        public void FilterAndSort_MiningReturnsResourcesAndOnlyImportantShips()
        {
            var source = new List<UiOverviewContact>
            {
                Contact("friendly", OverviewKind.Ship, OverviewDisposition.Friendly, distance: 5d),
                Contact("asteroid-far", OverviewKind.Asteroid, distance: 300d),
                Contact("hostile", OverviewKind.Ship, OverviewDisposition.Hostile, distance: 200d),
                Contact("station", OverviewKind.Station, distance: 2d),
                Contact("belt", OverviewKind.AsteroidBelt, distance: 100d),
                Contact("asteroid-near", OverviewKind.Asteroid, distance: 50d)
            };
            var result = new List<UiOverviewContact>();

            var count = OverviewFilter.FilterAndSort(
                source, result, OverviewPresetId.Mining,
                OverviewSortColumn.Distance, OverviewSortDirection.Ascending);

            Assert.That(count, Is.EqualTo(4));
            Assert.That(result.ConvertAll(value => value.Id), Is.EqualTo(new[]
            {
                "asteroid-near", "belt", "hostile", "asteroid-far"
            }));
        }

        [Test]
        public void Comparer_AllColumnsHonorDirectionAndStableIdFinalTieBreak()
        {
            foreach (OverviewSortColumn column in Enum.GetValues(typeof(OverviewSortColumn)))
            {
                var alpha = Contact(
                    "alpha", OverviewKind.Ship, OverviewDisposition.Hostile,
                    OverviewStateFlags.None, 100d, "Same", "Same", 25d);
                var beta = Contact(
                    "beta", OverviewKind.Ship, OverviewDisposition.Hostile,
                    OverviewStateFlags.None, 100d, "Same", "Same", 25d);

                Assert.That(
                    OverviewContactComparer.Get(column, OverviewSortDirection.Ascending).Compare(alpha, beta),
                    Is.LessThan(0), column + " did not use stable ID as the final tie-break.");
                Assert.That(
                    OverviewContactComparer.Get(column, OverviewSortDirection.Descending).Compare(alpha, beta),
                    Is.LessThan(0), column + " made its stable tie-break direction-dependent.");
            }
        }

        [Test]
        public void DistanceSort_ChangesPrimaryDirectionButKeepsInvalidTelemetryLast()
        {
            var contacts = new List<UiOverviewContact>
            {
                Contact("missing", OverviewKind.Station, distance: double.NaN),
                Contact("near", OverviewKind.Station, distance: 20d),
                Contact("far", OverviewKind.Station, distance: 80d)
            };

            OverviewFilter.Sort(contacts, OverviewSortColumn.Distance, OverviewSortDirection.Descending);

            Assert.That(contacts.ConvertAll(value => value.Id), Is.EqualTo(new[] { "far", "near", "missing" }));
        }

        [Test]
        public void ThreatSort_UsesDangerPriorityThenDistance()
        {
            var contacts = new List<UiOverviewContact>
            {
                Contact("neutral", OverviewKind.Ship, OverviewDisposition.Neutral, distance: 1d),
                Contact("hostile-far", OverviewKind.Ship, OverviewDisposition.Hostile, distance: 50d),
                Contact("mission", OverviewKind.Ship, OverviewDisposition.Neutral,
                    OverviewStateFlags.MissionObjective, 100d),
                Contact("attacking", OverviewKind.Ship, OverviewDisposition.Hostile,
                    OverviewStateFlags.TargetingPlayer, 200d),
                Contact("hostile-near", OverviewKind.Ship, OverviewDisposition.Hostile, distance: 10d)
            };

            OverviewFilter.Sort(contacts, OverviewSortColumn.Threat, OverviewSortDirection.Ascending);

            Assert.That(contacts.ConvertAll(value => value.Id), Is.EqualTo(new[]
            {
                "attacking", "mission", "hostile-near", "hostile-far", "neutral"
            }));
        }

        [Test]
        public void Filter_RejectsInPlaceMutationToKeepSourceStable()
        {
            var source = new List<UiOverviewContact>();
            Assert.Throws<ArgumentException>(() =>
                OverviewFilter.Filter(source, source, OverviewPresetId.All));
        }

        [TestCase(OverviewKind.Ship, OverviewCategory.Ship)]
        [TestCase(OverviewKind.Station, OverviewCategory.Structure)]
        [TestCase(OverviewKind.Stargate, OverviewCategory.Structure)]
        [TestCase(OverviewKind.AsteroidBelt, OverviewCategory.Resource)]
        [TestCase(OverviewKind.Asteroid, OverviewCategory.Resource)]
        [TestCase(OverviewKind.Planet, OverviewCategory.Celestial)]
        [TestCase(OverviewKind.Moon, OverviewCategory.Celestial)]
        [TestCase(OverviewKind.Star, OverviewCategory.Celestial)]
        public void CategoryFor_MapsEverySupportedKind(OverviewKind kind, OverviewCategory expected)
        {
            Assert.That(OverviewRules.CategoryFor(kind), Is.EqualTo(expected));
        }

        private static UiOverviewContact Contact(
            string id,
            OverviewKind kind,
            OverviewDisposition disposition = OverviewDisposition.None,
            OverviewStateFlags states = OverviewStateFlags.None,
            double distance = 0d,
            string name = null,
            string type = null,
            double velocity = 0d)
        {
            return new UiOverviewContact
            {
                Id = id,
                Category = OverviewRules.CategoryFor(kind),
                Kind = kind,
                Disposition = disposition,
                States = states,
                Actions = OverviewActionFlags.Select,
                Name = name ?? id,
                Type = type ?? kind.ToString(),
                DistanceMeters = distance,
                VelocityMetersPerSecond = velocity
            };
        }
    }
}
