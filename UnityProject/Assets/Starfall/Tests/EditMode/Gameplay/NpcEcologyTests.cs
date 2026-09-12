using System;
using System.Linq;
using NUnit.Framework;
using Starfall.Content;
using Starfall.Domain;
using Starfall.Simulation;

namespace Starfall.Tests.EditMode.Gameplay
{
    public sealed class NpcEcologyTests
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
        public void Pirates_EngageNavyWhileThePlayerIsFarAway()
        {
            var session = CreateSession();
            Undock(session);
            var pirate = Npc(session, 0);
            var navy = Npc(session, 1);
            ParkPlayer(session, new SimVec2(5000d, 5000d));
            Isolate(session, pirate, navy);
            pirate.FactionId = FactionIds.BloodReavers;
            pirate.AiBehavior = "pirate";
            pirate.Position = new SimVec2(2000d, 0d);
            navy.FactionId = FactionIds.Aurelian;
            navy.AiBehavior = "navy";
            navy.Position = new SimVec2(2050d, 0d);

            for (var i = 0; i < 100; i++) session.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(pirate.LockedTargetId, Is.EqualTo(navy.Id),
                "Pirates must raid lawful traffic without the player involved.");
            Assert.That(navy.LockedTargetId, Is.EqualTo(pirate.Id),
                "Navies fight back against their home pirates (standing -8).");
        }

        [Test]
        public void NpcCombat_LeavesThePlayersWalletAndStandingsAlone()
        {
            var session = CreateSession();
            Undock(session);
            var pirate = Npc(session, 0);
            var navy = Npc(session, 1);
            ParkPlayer(session, new SimVec2(5000d, 5000d));
            Isolate(session, pirate, navy);
            pirate.FactionId = FactionIds.BloodReavers;
            pirate.AiBehavior = "pirate";
            pirate.Position = new SimVec2(2000d, 0d);
            navy.FactionId = FactionIds.Aurelian;
            navy.AiBehavior = "navy";
            navy.Position = new SimVec2(2010d, 0d);
            navy.Shield = 0d;
            navy.Armor = 0d;
            navy.Hull = 1d;
            var creditsBefore = session.State.Player.Credits;
            var killsBefore = session.State.Player.Stats.Kills;
            var standingsBefore = string.Join(";", session.State.Player.Standings.OrderBy(pair => pair.Key));

            var navyId = navy.Id;
            for (var i = 0; i < 400 && session.State.FindEntity(navyId) != null; i++)
                session.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(session.State.FindEntity(navyId), Is.Null, "The pirate squad must win the 1 HP fight.");
            Assert.That(session.State.Player.Credits, Is.EqualTo(creditsBefore),
                "NPC kills must not pay the player a bounty.");
            Assert.That(session.State.Player.Stats.Kills, Is.EqualTo(killsBefore));
            Assert.That(string.Join(";", session.State.Player.Standings.OrderBy(pair => pair.Key)),
                Is.EqualTo(standingsBefore));
        }

        [Test]
        public void Traders_FlyUnarmed_NeverAcquireTargets_AndFleeUnderFire()
        {
            var session = CreateSession();
            Undock(session);
            var trader = session.State.Entities.FirstOrDefault(value =>
                value.Kind == EntityKind.Npc && value.AiBehavior == "trader");
            Assert.That(trader, Is.Not.Null, "Empire traffic must include haulers.");
            Assert.That(trader.Modules, Is.Empty, "Haulers fly without doctrine guns.");
            var pirate = Npc(session, 0);
            ParkPlayer(session, new SimVec2(5000d, 5000d));
            Isolate(session, pirate, trader);
            pirate.FactionId = FactionIds.BloodReavers;
            pirate.AiBehavior = "pirate";
            pirate.Position = trader.Position + new SimVec2(60d, 0d);

            for (var i = 0; i < 20; i++) session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(trader.LockedTargetId, Is.Empty, "A hauler never locks anyone.");

            trader.LastDamageAt = session.State.SimulationTime;
            trader.LastAttackerId = pirate.Id;
            session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(trader.Movement, Is.EqualTo(MovementMode.Flee),
                "Under fire the hauler turns and runs from its attacker.");
        }

        [Test]
        public void Police_IgnoreTheNpcWar_AroundThem()
        {
            var session = CreateSession();
            Undock(session);
            var police = Npc(session, 0);
            var pirate = Npc(session, 1);
            var navy = Npc(session, 2);
            ParkPlayer(session, new SimVec2(5000d, 5000d));
            Isolate(session, police, pirate, navy);
            police.FactionId = FactionIds.Directorate;
            police.AiBehavior = "police";
            police.AggroRange = 99999d;
            police.Position = new SimVec2(4000d, 0d);
            pirate.FactionId = FactionIds.BloodReavers;
            pirate.AiBehavior = "pirate";
            pirate.Position = new SimVec2(2000d, 0d);
            navy.FactionId = FactionIds.Aurelian;
            navy.AiBehavior = "navy";
            navy.Position = new SimVec2(2050d, 0d);
            session.State.Player.CriminalTimer = 0d;

            for (var i = 0; i < 20; i++) session.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(pirate.LockedTargetId, Is.EqualTo(navy.Id));
            Assert.That(police.LockedTargetId, Is.Empty,
                "Directorate response units police the player, not the local war.");
        }

        [Test]
        public void LowSecurityTraffic_AlsoIncludesHaulers()
        {
            var session = CreateSession();
            Undock(session);
            var lowSystem = universe.OrderedSystems.First(value =>
                value.Region == SystemRegion.LowSecurity && value.Stations.Count > 0);
            JumpAlong(session, UniverseRoutes.FindRoute(universe,
                session.State.Player.CurrentSystemId, lowSystem.Id));

            var traders = session.State.Entities.Count(value =>
                value.Kind == EntityKind.Npc && value.AiBehavior == "trader");
            Assert.That(traders, Is.GreaterThanOrEqualTo(1));
            var fighters = session.State.Entities.Count(value =>
                value.Kind == EntityKind.Npc && value.AiBehavior != "trader");
            Assert.That(fighters, Is.GreaterThanOrEqualTo(2));
        }

        private GameSession CreateSession()
        {
            return new GameSession(universe, catalog, "Ecology Pilot", FactionIds.Aurelian);
        }

        private static EntityState Npc(GameSession session, int index)
        {
            var npcs = session.State.Entities.Where(value => value.Kind == EntityKind.Npc).ToList();
            return npcs[Math.Min(index, npcs.Count - 1)];
        }

        private static void ParkPlayer(GameSession session, SimVec2 position)
        {
            var player = session.State.PlayerEntity();
            player.Position = position;
            player.Movement = MovementMode.Idle;
            player.MoveTargetId = string.Empty;
        }

        /// <summary>
        /// Neutralises and relocates every NPC outside the scenario cast so stray
        /// fights or spawns cannot leak into the assertions.
        /// </summary>
        private static void Isolate(GameSession session, params EntityState[] cast)
        {
            foreach (var entity in session.State.Entities)
            {
                if (entity.Kind != EntityKind.Npc || cast.Contains(entity)) continue;
                entity.AggroRange = 0d;
                entity.LockedTargetId = string.Empty;
                entity.Position = new SimVec2(-9000d, -9000d);
                for (var i = 0; i < entity.Modules.Count; i++) entity.Modules[i].Active = false;
            }
        }

        /// <summary>Walks a route gate by gate, parking the player on each gate before jumping.</summary>
        private static void JumpAlong(GameSession session, System.Collections.Generic.IReadOnlyList<string> route)
        {
            Assert.That(route, Is.Not.Null, "A route must exist.");
            for (var hop = 1; hop < route.Count; hop++)
            {
                var state = session.State;
                var player = state.PlayerEntity();
                Assert.That(player, Is.Not.Null, "The player entity must survive every jump.");
                var gate = state.Universe.Systems[state.Player.CurrentSystemId].Gates
                    .First(value => value.DestinationSystemId == route[hop]);
                player.Position = gate.Position;
                session.Enqueue(new GameCommand(GameCommandType.DockOrJump, gate.Id));
                session.AdvanceFrame(GameSession.FixedStepSeconds);
            }
        }

        private static void Undock(GameSession session)
        {
            session.Enqueue(new GameCommand(GameCommandType.Undock));
            session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(session.State.PlayerEntity(), Is.Not.Null);
        }
    }
}
