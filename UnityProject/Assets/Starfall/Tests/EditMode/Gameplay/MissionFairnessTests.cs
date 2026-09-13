using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Starfall.Content;
using Starfall.Domain;
using Starfall.Simulation;

namespace Starfall.Tests.EditMode.Gameplay
{
    /// <summary>
    /// Second-round mission fairness: rookie destinations stay patrolled,
    /// navy-stolen kills still count, storylines scale with their trigger
    /// level, and non-empire LP is spendable where it is earned.
    /// </summary>
    public sealed class MissionFairnessTests
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
        public void LevelOneMissions_StayInPatrolledSpace()
        {
            var session = CreateSession();
            var station = universe.Systems[session.State.Player.CurrentSystemId].Stations[0];
            for (var i = 0; i < 4; i++)
                station.Agents.Add(new AgentDefinition($"agent_d{i}", $"Courier Agent {i}", "distribution", 1, station.Id));
            for (var i = 0; i < 4; i++)
                station.Agents.Add(new AgentDefinition($"agent_s{i}", $"Security Agent {i}", "security", 1, station.Id));

            foreach (var agent in station.Agents)
            {
                Execute(session, GameCommandType.TalkToAgent, agent.Id);
                var mission = session.State.Player.Missions.Single(value => value.AgentId == agent.Id);
                var destinationId = mission.Type == MissionType.Distribution
                    ? mission.DestinationSystemId
                    : mission.TargetSystemId;
                var destination = universe.Systems[destinationId];
                Assert.That(destination.Security, Is.GreaterThanOrEqualTo(0.5d),
                    $"L1 {mission.Type} must not route a rookie frigate into {destination.Id} (security {destination.Security}).");
            }
        }

        [Test]
        public void MissionTargets_KilledByNpcForces_StillAdvanceTheObjective()
        {
            var session = CreateSession();
            Undock(session);
            var mission = new MissionState
            {
                Id = "mis_steal",
                Type = MissionType.Security,
                Status = MissionStatus.Active,
                Title = "Steal test",
                FactionId = FactionIds.Aurelian,
                TargetSystemId = session.State.Player.CurrentSystemId,
                TargetFactionId = FactionIds.BloodReavers,
                KillsRequired = 1,
                OfferedAt = session.State.SimulationTime,
            };
            session.State.Player.Missions.Add(mission);

            var cast = session.State.Entities.Where(value => value.Kind == EntityKind.Npc).ToList();
            var navy = cast[0];
            var target = cast[1];
            foreach (var entity in session.State.Entities)
            {
                if (entity.Kind != EntityKind.Npc || entity == navy || entity == target) continue;
                entity.AggroRange = 0d;
                entity.LockedTargetId = string.Empty;
                entity.Position = new SimVec2(-9000d, -9000d);
                for (var i = 0; i < entity.Modules.Count; i++) entity.Modules[i].Active = false;
            }
            ParkPlayer(session, new SimVec2(5000d, 5000d));
            navy.FactionId = FactionIds.Aurelian;
            navy.AiBehavior = "navy";
            navy.AggroRange = 0d;
            navy.LockedTargetId = target.Id;
            navy.Position = new SimVec2(2000d, 0d);
            target.FactionId = FactionIds.BloodReavers;
            target.AiBehavior = "pirate";
            target.AggroRange = 0d;
            target.LockedTargetId = string.Empty;
            target.Position = new SimVec2(2010d, 0d);
            target.Shield = 0d;
            target.Armor = 0d;
            target.Hull = 1d;
            target.MissionId = mission.Id;

            for (var i = 0; i < 400 && !target.Dead; i++)
                session.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(target.Dead, Is.True, "The navy patrol must win the 1 HP fight.");
            Assert.That(mission.Kills, Is.EqualTo(1),
                "A mission objective stolen by NPC forces must still advance for the player.");
            Assert.That(mission.Status, Is.EqualTo(MissionStatus.ObjectivesMet));
        }

        [Test]
        public void Storylines_ScaleWithTheTriggeringMissionLevel()
        {
            var session = CreateSession();
            session.State.Player.MissionCounts[FactionIds.Aurelian] = 4;
            var regular = new MissionState
            {
                Id = "mis_l1", Type = MissionType.Mining, Status = MissionStatus.ObjectivesMet,
                Level = 1, Title = "Regular L1", FactionId = FactionIds.Aurelian,
                RewardCredits = 1L, RewardLoyaltyPoints = 1, RewardStanding = 0.1d,
            };
            session.State.Player.Missions.Add(regular);

            Execute(session, GameCommandType.CompleteMission, regular.Id);

            var storyline = session.State.Player.Missions.Single(value =>
                value.Type == MissionType.StorylineKill || value.Type == MissionType.StorylineHaul);
            Assert.That(storyline.Level, Is.EqualTo(1),
                "A storyline following L1 work must not field an elite battlecruiser squad.");
            Assert.That(storyline.RewardCredits, Is.InRange(170_000L, 200_000L));
            if (storyline.Type == MissionType.StorylineKill)
                Assert.That(storyline.KillsRequired, Is.EqualTo(3));
        }

        [Test]
        public void LoyaltyStore_SpendsTheDockedStationsFactionPoints()
        {
            var session = CreateSession();
            var capital = universe.Systems[session.State.Player.CurrentSystemId];
            var bureau = capital.Stations.First(value => value.FactionId == FactionIds.Directorate);
            Assert.That(bureau, Is.Not.Null, "Capitals must have a Directorate Bureau station.");

            session.State.Player.DockedAtStationId = bureau.Id;
            session.State.Player.LoyaltyPoints[FactionIds.Aurelian] = 0;
            session.State.Player.LoyaltyPoints[FactionIds.Directorate] = 100;

            Execute(session, GameCommandType.ExchangeLoyalty, ModuleIds.DamageAmp);

            Assert.That(session.State.Player.Hangar.GetValueOrDefault(ModuleIds.DamageAmp), Is.EqualTo(1),
                "Directorate LP must be spendable at the Directorate Bureau.");
            Assert.That(session.State.Player.LoyaltyPoints[FactionIds.Directorate], Is.Zero);
            Assert.That(session.State.Player.LoyaltyPoints[FactionIds.Aurelian], Is.Zero,
                "The store must not touch the empire balance while docked elsewhere.");
        }

        [Test]
        public void UnacceptedOffers_ExpireAfterAWeekOfSimTime()
        {
            var session = CreateSession();
            var station = universe.Systems[session.State.Player.CurrentSystemId].Stations[0];
            var agent = new AgentDefinition("agent_expire", "Expiry Agent", "mining", 1, station.Id);
            station.Agents.Add(agent);
            Execute(session, GameCommandType.TalkToAgent, agent.Id);
            var offer = session.State.Player.Missions.Single(value => value.AgentId == agent.Id);
            Assert.That(offer.Status, Is.EqualTo(MissionStatus.Offered));

            // Simulate a week passing with nobody touching the journal.
            var steps = (int)(7.1d * 86400d / GameSession.FixedStepSeconds);
            for (var i = 0; i < steps; i++) session.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(session.State.Player.Missions, Has.None.Matches<MissionState>(
                value => value.Id == offer.Id), "Stale offers must leave the journal.");
        }

        private GameSession CreateSession()
        {
            return new GameSession(universe, catalog, "Fair Missions Pilot", FactionIds.Aurelian);
        }

        private static void Execute(GameSession session, GameCommandType type, string argument = null)
        {
            session.Enqueue(new GameCommand(type, argument));
            session.AdvanceFrame(GameSession.FixedStepSeconds);
        }

        private static void ParkPlayer(GameSession session, SimVec2 position)
        {
            var player = session.State.PlayerEntity();
            player.Position = position;
            player.Movement = MovementMode.Idle;
            player.MoveTargetId = string.Empty;
        }

        private static void Undock(GameSession session)
        {
            session.Enqueue(new GameCommand(GameCommandType.Undock));
            session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(session.State.PlayerEntity(), Is.Not.Null);
        }
    }
}
