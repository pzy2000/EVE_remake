using System.Linq;
using NUnit.Framework;
using Starfall.Content;
using Starfall.Domain;
using Starfall.Simulation;

namespace Starfall.Tests.EditMode.Gameplay
{
    public sealed class OverviewSimulationPolicyTests
    {
        private GeneratedUniverse universe;
        private GameContentCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            universe = new UniverseGenerator().Generate(12345);
            catalog = new GameContentCatalog();
        }

        [Test]
        public void Police_DoesNotAggroLawfulPlayer_ButAggrosCriminalPlayer()
        {
            var lawfulSession = CreateUndockedSession();
            var lawfulPlayer = lawfulSession.State.PlayerEntity();
            var lawfulPolice = ConfigurePolice(lawfulSession, lawfulPlayer);

            lawfulSession.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(lawfulPolice.LockedTargetId, Is.Empty);
            Assert.That(EntityDispositionPolicy.Evaluate(lawfulPolice, lawfulSession.State.Player, catalog,
                lawfulPlayer.Id), Is.EqualTo(EntityDisposition.Neutral));

            var criminalSession = CreateUndockedSession();
            var criminalPlayer = criminalSession.State.PlayerEntity();
            var criminalPolice = ConfigurePolice(criminalSession, criminalPlayer);
            criminalSession.State.Player.CriminalTimer = 10d;

            criminalSession.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(criminalPolice.LockedTargetId, Is.EqualTo(criminalPlayer.Id));
            Assert.That(EntityDispositionPolicy.Evaluate(criminalPolice, criminalSession.State.Player, catalog,
                criminalPlayer.Id), Is.EqualTo(EntityDisposition.Hostile));

            criminalSession.State.Player.CriminalTimer = 0d;
            Assert.That(EntityDispositionPolicy.Evaluate(criminalPolice, criminalSession.State.Player, catalog,
                criminalPlayer.Id), Is.EqualTo(EntityDisposition.Neutral),
                "Directorate disposition must return to neutral when the criminal timer expires.");
        }

        [Test]
        public void MissionTargets_OverrideFactionStanding()
        {
            var player = new PlayerState { EmpireId = FactionIds.Aurelian };
            var npc = new EntityState
            {
                Kind = EntityKind.Npc,
                FactionId = FactionIds.Aurelian,
                AiBehavior = "navy",
                MissionId = "mis_test",
            };

            Assert.That(EntityDispositionPolicy.Evaluate(npc, player, catalog),
                Is.EqualTo(EntityDisposition.Hostile), "Mission targets override friendly faction standing.");
        }

        [TestCase(FactionIds.BloodReavers, -1d, false, EntityDisposition.Hostile)]
        [TestCase(FactionIds.BloodReavers, 0d, false, EntityDisposition.Hostile)]
        [TestCase(FactionIds.BloodReavers, 0.01d, false, EntityDisposition.Neutral)]
        [TestCase(FactionIds.BloodReavers, 4.99d, false, EntityDisposition.Neutral)]
        [TestCase(FactionIds.BloodReavers, 5d, false, EntityDisposition.Friendly)]
        [TestCase(FactionIds.Aurelian, -5.01d, false, EntityDisposition.Hostile)]
        [TestCase(FactionIds.Aurelian, -5d, false, EntityDisposition.Neutral)]
        [TestCase(FactionIds.Aurelian, 4.99d, false, EntityDisposition.Neutral)]
        [TestCase(FactionIds.Aurelian, 5d, false, EntityDisposition.Friendly)]
        [TestCase(FactionIds.Aurelian, 10d, true, EntityDisposition.Hostile)]
        [TestCase(FactionIds.Sisters, -5.01d, false, EntityDisposition.Hostile)]
        [TestCase(FactionIds.Sisters, -5d, false, EntityDisposition.Neutral)]
        [TestCase(FactionIds.Sisters, 4.99d, false, EntityDisposition.Neutral)]
        [TestCase(FactionIds.Sisters, 5d, false, EntityDisposition.Friendly)]
        [TestCase(FactionIds.Sisters, 5d, true, EntityDisposition.Friendly)]
        [TestCase(FactionIds.Directorate, -10d, false, EntityDisposition.Neutral)]
        [TestCase(FactionIds.Directorate, 10d, false, EntityDisposition.Neutral)]
        [TestCase(FactionIds.Directorate, 10d, true, EntityDisposition.Hostile)]
        public void FactionDisposition_MatchesLegacyStandingThresholds(string factionId, double standing,
            bool criminal, EntityDisposition expected)
        {
            var player = new PlayerState
            {
                EmpireId = FactionIds.Aurelian,
                CriminalTimer = criminal ? 10d : 0d,
            };
            player.Standings[factionId] = standing;
            var npc = new EntityState
            {
                Kind = EntityKind.Npc,
                FactionId = factionId,
            };

            Assert.That(EntityDispositionPolicy.Evaluate(npc, player, catalog),
                Is.EqualTo(expected));
        }

        [Test]
        public void HostileDisposition_StillRequiresNpcAggroRange()
        {
            var session = CreateUndockedSession();
            var player = session.State.PlayerEntity();
            var pirate = session.State.Entities.First(value => value.Kind == EntityKind.Npc);
            pirate.AiBehavior = "pirate";
            pirate.FactionId = FactionIds.BloodReavers;
            pirate.AggroRange = 10d;
            pirate.Position = player.Position + new SimVec2(11d, 0d);
            pirate.LockedTargetId = string.Empty;

            session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(pirate.LockedTargetId, Is.Empty);

            pirate.Position = player.Position;
            session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(pirate.LockedTargetId, Is.EqualTo(player.Id));
        }

        [TestCase("star")]
        [TestCase("planet")]
        [TestCase("moon")]
        public void CelestialCommands_SelectApproachAndWarp_UseSurfaceAwareStandOff(string kind)
        {
            var session = CreateUndockedSession();
            var player = session.State.PlayerEntity();
            var system = universe.Systems[session.State.Player.CurrentSystemId];
            var target = ResolveCelestial(system, kind);
            var start = player.Position;
            var expectedDistance = target.radius + 40d;
            var expected = StandOff(target.position, start, expectedDistance);

            Execute(session, new GameCommand(GameCommandType.Select, target.id));
            Assert.That(session.State.SelectedId, Is.EqualTo(target.id));

            Execute(session, new GameCommand(GameCommandType.Approach));
            Assert.That(player.MoveTargetId, Is.Empty,
                "A static celestial target must not overwrite its stand-off point during movement updates.");
            AssertVec(player.MoveTargetPosition, expected);
            Assert.That(SimVec2.Distance(player.MoveTargetPosition, target.position),
                Is.EqualTo(expectedDistance).Within(1e-9d));

            player.Position = start;
            player.Movement = MovementMode.Idle;
            Execute(session, new GameCommand(GameCommandType.Approach, target.id,
                position: target.position));
            Assert.That(player.MoveTargetId, Is.Empty,
                "An explicit double-click point must not bypass celestial stand-off handling.");
            AssertVec(player.MoveTargetPosition, expected);

            player.Position = start;
            player.Movement = MovementMode.Idle;
            Execute(session, new GameCommand(GameCommandType.Warp, target.id,
                position: target.position));
            Assert.That(player.Movement, Is.EqualTo(MovementMode.WarpAlign));
            AssertVec(player.WarpTarget, expected);
            Assert.That(SimVec2.Distance(player.WarpTarget, target.position),
                Is.EqualTo(expectedDistance).Within(1e-9d));
        }

        private GameSession CreateUndockedSession()
        {
            var session = new GameSession(universe, catalog, "Test Pilot", FactionIds.Aurelian);
            var batch = Execute(session, new GameCommand(GameCommandType.Undock));
            Assert.That(batch.Any(value => value.Type == SimulationEventType.Dock && value.Detail == "undock"), Is.True);
            return session;
        }

        private static EntityState ConfigurePolice(GameSession session, EntityState player)
        {
            var police = session.State.Entities.First(value => value.Kind == EntityKind.Npc);
            police.AiBehavior = "police";
            police.FactionId = FactionIds.Directorate;
            police.Position = player.Position;
            police.AggroRange = 99999d;
            police.LockedTargetId = string.Empty;
            return police;
        }

        private static (string id, SimVec2 position, double radius) ResolveCelestial(
            StarSystemDefinition system, string kind)
        {
            if (kind == "star") return (system.Id + "_star", SimVec2.Zero, system.Star.Radius);
            if (kind == "planet")
                return (system.Planets[0].Id, system.Planets[0].Position, system.Planets[0].Radius);

            var moon = system.Planets.SelectMany(value => value.Moons).FirstOrDefault();
            if (moon == null)
            {
                moon = new MoonDefinition(system.Planets[0].Id + "_test_moon", "Test Moon",
                    system.Planets[0].Position + new SimVec2(80d, 0d), 10d);
                system.Planets[0].Moons.Add(moon);
            }
            return (moon.Id, moon.Position, moon.Radius);
        }

        private static SimVec2 StandOff(SimVec2 center, SimVec2 actor, double distance)
        {
            var fromCenter = actor - center;
            var magnitude = fromCenter.Magnitude;
            var direction = magnitude > 1e-9d ? fromCenter * (1d / magnitude) : new SimVec2(1d, 0d);
            return center + direction * distance;
        }

        private static void AssertVec(SimVec2 actual, SimVec2 expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(1e-9d));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(1e-9d));
        }

        private static SimulationEventBatch Execute(GameSession session, GameCommand command)
        {
            session.Enqueue(command);
            return session.AdvanceFrame(GameSession.FixedStepSeconds);
        }
    }
}
