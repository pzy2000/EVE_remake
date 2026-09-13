using System;
using System.Linq;
using NUnit.Framework;
using Starfall.Content;
using Starfall.Domain;
using Starfall.Simulation;

namespace Starfall.Tests.EditMode.Gameplay
{
    /// <summary>
    /// Regression coverage for the second review round's economy/save fairness
    /// fixes: autosave flushing, the battlecruiser bounty tier and the pirate
    /// warp-off window.
    /// </summary>
    public sealed class EconomyFairnessTests
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
        public void PrepareForSave_FlushesLiveShipHpIntoThePersistedShip()
        {
            var session = CreateSession();
            Undock(session);
            var entity = session.State.PlayerEntity();
            entity.Shield = 123d;
            entity.Armor = 45d;
            entity.Hull = 67d;

            session.PrepareForSave();

            var ship = session.State.Player.ActiveShip();
            Assert.That(ship.Shield, Is.EqualTo(123d).Within(1e-9d),
                "A pause-kill autosave must record the battered ship, not the pristine one from the last dock.");
            Assert.That(ship.Armor, Is.EqualTo(45d).Within(1e-9d));
            Assert.That(ship.Hull, Is.EqualTo(67d).Within(1e-9d));
        }

        [Test]
        public void PrepareForSave_FlushesMinedDownAsteroidsIntoTheVisitState()
        {
            var session = CreateSession();
            Undock(session);
            var asteroid = session.State.Asteroids.First(value => value.Amount > 0d);
            asteroid.Amount = 1d;

            session.PrepareForSave();

            var visit = session.State.Player.SystemVisits[session.State.Player.CurrentSystemId];
            var persisted = visit.Asteroids.First(value => value.Id == asteroid.Id);
            Assert.That(persisted.Amount, Is.EqualTo(1d).Within(1e-9d),
                "Killing the app mid-mining must not roll the belt back to full.");
        }

        [Test]
        public void BattlecruiserBounty_SitsBetweenCruiserAndBattleship()
        {
            var session = CreateSession();
            Undock(session);
            var pirate = IsolatedPirate(session);
            pirate.ShipId = ShipIds.Warden;
            pirate.FactionId = FactionIds.BloodReavers;
            pirate.Position = session.State.PlayerEntity().Position + new SimVec2(40d, 0d);
            pirate.Shield = 0d;
            pirate.Armor = 0d;
            pirate.Hull = 1d;
            var creditsBefore = session.State.Player.Credits;

            var player = session.State.PlayerEntity();
            player.LockedTargetId = pirate.Id;
            var weaponIndex = Array.FindIndex(player.Modules.ToArray(),
                runtime => catalog.Modules[runtime.ModuleId].Kind == ModuleKind.Weapon);
            Assert.That(weaponIndex, Is.GreaterThanOrEqualTo(0));
            player.Modules[weaponIndex].Active = true;

            var pirateId = pirate.Id;
            for (var i = 0; i < 200 && session.State.FindEntity(pirateId) != null; i++)
                session.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(session.State.FindEntity(pirateId), Is.Null, "The player must win the 1 HP fight.");
            var security = universe.Systems[session.State.Player.CurrentSystemId].Security;
            var expected = (long)JsMath.Round(180000d * (1d + Math.Max(0d, 0.5d - security)));
            Assert.That(session.State.Player.Credits - creditsBefore, Is.EqualTo(expected),
                "Battlecruisers must pay their own tier, not the battleship bounty.");
        }

        [Test]
        public void WoundedTrafficPirates_TakeFourSecondsToWarpOff()
        {
            var session = CreateSession();
            Undock(session);
            var pirate = IsolatedPirate(session);
            ParkPlayer(session, new SimVec2(5000d, 5000d));
            // The warp-off branch only runs while the pirate is engaged, so it
            // keeps the player locked (guns disabled by IsolatedPirate).
            pirate.LockedTargetId = session.State.PlayerEntity().Id;

            // Age the pirate far past 4s: its total lifetime must NOT count as
            // escape time, which is what made wounded pirates vanish instantly.
            for (var i = 0; i < 120; i++) session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(session.State.FindEntity(pirate.Id), Is.Not.Null);

            pirate.Shield = 0d;
            pirate.Armor = 0d;
            pirate.Hull = pirate.MaxHull * 0.1d;

            for (var i = 0; i < 40; i++) session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(pirate.Movement, Is.EqualTo(MovementMode.Flee));
            Assert.That(pirate.FleeSince, Is.GreaterThan(0d));
            Assert.That(session.State.FindEntity(pirate.Id), Is.Not.Null,
                "A pirate that just broke off must still be killable for its bounty.");

            for (var i = 0; i < 60; i++) session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(session.State.FindEntity(pirate.Id), Is.Null,
                "After four seconds of fleeing the pirate warps off.");
        }

        private GameSession CreateSession()
        {
            return new GameSession(universe, catalog, "Fairness Pilot", FactionIds.Aurelian);
        }

        /// <summary>
        /// Picks the first NPC, converts it into a passive Blood Reavers pirate
        /// far from the player, and neutralises every other NPC.
        /// </summary>
        private EntityState IsolatedPirate(GameSession session)
        {
            var cast = session.State.Entities.First(value => value.Kind == EntityKind.Npc);
            foreach (var entity in session.State.Entities)
            {
                if (entity.Kind != EntityKind.Npc || entity == cast) continue;
                entity.AggroRange = 0d;
                entity.LockedTargetId = string.Empty;
                entity.Position = new SimVec2(-9000d, -9000d);
                for (var i = 0; i < entity.Modules.Count; i++) entity.Modules[i].Active = false;
            }
            cast.AggroRange = 0d;
            cast.LockedTargetId = string.Empty;
            cast.AiBehavior = "pirate";
            cast.FactionId = FactionIds.BloodReavers;
            cast.Position = new SimVec2(-500d, -500d);
            for (var i = 0; i < cast.Modules.Count; i++) cast.Modules[i].Active = false;
            return cast;
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
