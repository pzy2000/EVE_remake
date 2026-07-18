using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Starfall.Content;
using Starfall.Domain;
using Starfall.Simulation;

namespace Starfall.Tests.EditMode.Gameplay
{
    public sealed class GameSessionGameplayTests
    {
        private GeneratedUniverse universe;
        private GameContentCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            universe = new UniverseGenerator().Generate(12345);
            catalog = new GameContentCatalog();
        }

        [TestCase(FactionIds.Aurelian, ShipIds.Acolyte, ModuleIds.PulseLaser)]
        [TestCase(FactionIds.Kaldari, ShipIds.Shrike, ModuleIds.MissileLauncher)]
        [TestCase(FactionIds.Meridian, ShipIds.Wasp, ModuleIds.Blaster)]
        [TestCase(FactionIds.Varkhald, ShipIds.Fang, ModuleIds.Autocannon)]
        public void NewGame_FourEmpiresStartDockedAtTheirCapitalWithFactionFrigate(
            string empireId, string expectedShipId, string expectedWeaponId)
        {
            var session = CreateSession(empireId);
            var player = session.State.Player;
            var ship = player.ActiveShip();
            var startSystem = universe.Systems[universe.StartSystems[empireId]];

            Assert.That(player.EmpireId, Is.EqualTo(empireId));
            Assert.That(player.CurrentSystemId, Is.EqualTo(startSystem.Id));
            Assert.That(player.HomeSystemId, Is.EqualTo(startSystem.Id));
            Assert.That(player.DockedAtStationId, Is.EqualTo(startSystem.Stations[0].Id));
            Assert.That(player.HomeStationId, Is.EqualTo(startSystem.Stations[0].Id));
            Assert.That(ship, Is.Not.Null);
            Assert.That(ship.ShipId, Is.EqualTo(expectedShipId));
            Assert.That(ship.Fitting.High[0], Is.EqualTo(expectedWeaponId));
            Assert.That(player.Hangar[ModuleIds.MiningLaser], Is.EqualTo(1));
            Assert.That(session.State.Entities, Is.Empty);
        }

        [Test]
        public void AdvanceFrame_ProcessesCommandsOnlyAtTwentyHertzBoundary()
        {
            var session = CreateSession();
            session.Enqueue(new GameCommand(GameCommandType.Select, "target_alpha"));

            var beforeTick = session.AdvanceFrame(0.049d);
            Assert.That(session.State.SimulationTime, Is.Zero);
            Assert.That(session.State.SelectedId, Is.Empty);
            Assert.That(beforeTick, Is.Empty);

            var atTick = session.AdvanceFrame(0.001d);
            Assert.That(session.State.SimulationTime, Is.EqualTo(GameSession.FixedStepSeconds).Within(1e-12d));
            Assert.That(session.State.SelectedId, Is.EqualTo("target_alpha"));
            Assert.That(atTick.Any(value => value.Type == SimulationEventType.Selection && value.TargetId == "target_alpha"), Is.True);
        }

        [Test]
        public void Approach_UsesExplicitXZTargetAndMovesAtFixedStep()
        {
            var session = CreateSession();
            Undock(session);
            var player = session.State.PlayerEntity();
            var start = player.Position;
            var target = start + new SimVec2(500d, 0d);

            session.Enqueue(new GameCommand(GameCommandType.Approach, position: target));
            session.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(player.Movement, Is.EqualTo(MovementMode.Approach));
            Assert.That(player.Position.X, Is.GreaterThan(start.X));
            Assert.That(player.Position.Z, Is.EqualTo(start.Z).Within(1e-9d));
            Assert.That(SimVec2.Distance(player.Position, target), Is.LessThan(SimVec2.Distance(start, target)));
        }

        [Test]
        public void Warp_TransitionsToCruiseAndArrivesExactlyAtExplicitTarget()
        {
            var session = CreateSession();
            Undock(session);
            var player = session.State.PlayerEntity();
            var target = player.Position + new SimVec2(1800d, 420d);
            session.Enqueue(new GameCommand(GameCommandType.Warp, position: target));

            var firstBatch = session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(firstBatch.Any(value => value.Type == SimulationEventType.Warp && value.Detail == "start"), Is.True);

            var ended = false;
            for (var i = 0; i < 400 && !ended; i++)
            {
                var batch = session.AdvanceFrame(GameSession.FixedStepSeconds);
                ended = batch.Any(value => value.Type == SimulationEventType.Warp && value.Detail == "end");
            }

            Assert.That(ended, Is.True, "Warp did not finish within twenty simulated seconds.");
            Assert.That(player.Movement, Is.EqualTo(MovementMode.Idle));
            Assert.That(player.Position, Is.EqualTo(target));
            Assert.That(player.Speed, Is.Zero);
        }

        [Test]
        public void WeaponDamage_SpillsThroughShieldArmorAndHull()
        {
            var session = CreateSession();
            Undock(session);
            var player = session.State.PlayerEntity();
            var target = session.State.Entities.First(value => value.Kind == EntityKind.Npc);
            target.Position = player.Position + new SimVec2(5d, 0d);
            target.Shield = 1d;
            target.Armor = 1d;
            target.Hull = 1000d;
            target.AggroRange = 0d;
            var hullBefore = target.Hull;

            session.Enqueue(new GameCommand(GameCommandType.Lock, target.Id));
            session.Enqueue(new GameCommand(GameCommandType.ActivateModule, index: 0));
            var batch = session.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(player.LockedTargetId, Is.EqualTo(target.Id));
            Assert.That(target.Shield, Is.Zero);
            Assert.That(target.Armor, Is.Zero);
            Assert.That(target.Hull, Is.LessThan(hullBefore));
            Assert.That(batch.Any(value => value.Type == SimulationEventType.Weapon && value.TargetId == target.Id), Is.True);
            Assert.That(batch.Any(value => value.Type == SimulationEventType.Damage && value.TargetId == target.Id && value.Detail.Split('|').Length == 3), Is.True);
        }

        [Test]
        public void MiningLaser_ExtractsOreIntoCargoAndUpdatesStatistics()
        {
            var session = CreateSession();
            var ship = session.State.Player.ActiveShip();
            Execute(session, new GameCommand(GameCommandType.Unfit, ship.InstanceId + "|high|1"));
            Execute(session, new GameCommand(GameCommandType.Fit,
                ship.InstanceId + "|high|1|" + ModuleIds.MiningLaser));
            Assert.That(ship.Fitting.High[1], Is.EqualTo(ModuleIds.MiningLaser));

            Undock(session);
            var player = session.State.PlayerEntity();
            var asteroid = session.State.Asteroids.First();
            asteroid.Position = player.Position;
            var amountBefore = asteroid.Amount;
            var miningIndex = player.Modules.FindIndex(value => value.ModuleId == ModuleIds.MiningLaser);

            session.Enqueue(new GameCommand(GameCommandType.Select, asteroid.Id));
            session.Enqueue(new GameCommand(GameCommandType.ActivateModule, index: miningIndex));
            var batch = session.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(miningIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(asteroid.Amount, Is.EqualTo(amountBefore - 10d).Within(1e-9d));
            Assert.That(session.State.Player.Cargo[asteroid.OreId], Is.EqualTo(10d));
            Assert.That(session.State.Player.Stats.OreMined, Is.EqualTo(10d));
            Assert.That(batch.Any(value => value.Type == SimulationEventType.Inventory && value.Detail == asteroid.OreId), Is.True);
        }

        [Test]
        public void DepletedAsteroid_ClearsSelectionStopsMinerAndEmitsDespawn()
        {
            var session = CreateSession();
            var ship = session.State.Player.ActiveShip();
            Execute(session, new GameCommand(GameCommandType.Unfit, ship.InstanceId + "|high|1"));
            Execute(session, new GameCommand(GameCommandType.Fit,
                ship.InstanceId + "|high|1|" + ModuleIds.MiningLaser));

            Undock(session);
            var player = session.State.PlayerEntity();
            var asteroid = session.State.Asteroids.First();
            asteroid.Position = player.Position;
            asteroid.Amount = 5d;
            var miningIndex = player.Modules.FindIndex(value => value.ModuleId == ModuleIds.MiningLaser);

            session.Enqueue(new GameCommand(GameCommandType.Select, asteroid.Id));
            session.Enqueue(new GameCommand(GameCommandType.ActivateModule, index: miningIndex));
            var batch = session.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(asteroid.Amount, Is.Zero);
            Assert.That(session.State.Asteroids.Any(value => ReferenceEquals(value, asteroid)), Is.False);
            Assert.That(session.State.SelectedId, Is.Empty);
            Assert.That(player.Modules[miningIndex].Active, Is.False);
            Assert.That(batch.Any(value => value.Type == SimulationEventType.Selection &&
                                           string.IsNullOrEmpty(value.TargetId)), Is.True);
            Assert.That(batch.Any(value => value.Type == SimulationEventType.Despawn &&
                                           value.SourceId == asteroid.Id), Is.True);
        }

        [Test]
        public void DeadTarget_ClearsSelectionLockAndMovementBeforeDespawn()
        {
            var session = CreateSession();
            Undock(session);
            var player = session.State.PlayerEntity();
            var target = session.State.Entities.First(value => value.Kind == EntityKind.Npc);
            Execute(session, new GameCommand(GameCommandType.Select, target.Id));
            player.LockedTargetId = target.Id;
            player.MoveTargetId = target.Id;
            player.Movement = MovementMode.Approach;
            player.Modules[0].Active = true;
            target.Dead = true;

            var batch = session.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(session.State.FindEntity(target.Id), Is.Null);
            Assert.That(session.State.SelectedId, Is.Empty);
            Assert.That(player.LockedTargetId, Is.Empty);
            Assert.That(player.MoveTargetId, Is.Empty);
            Assert.That(player.Movement, Is.EqualTo(MovementMode.Idle));
            Assert.That(player.Modules.All(value => !value.Active), Is.True,
                "Losing a locked target must not leave weapons armed for the next lock.");
            Assert.That(batch.Any(value => value.Type == SimulationEventType.Selection &&
                                           string.IsNullOrEmpty(value.TargetId)), Is.True);
            Assert.That(batch.Any(value => value.Type == SimulationEventType.Despawn &&
                                           value.SourceId == target.Id), Is.True);
        }

        [Test]
        public void CriminalAttack_AlertsDirectorateAndSpawnsTwoEnforcers()
        {
            var session = CreateSession();
            Undock(session);
            var player = session.State.PlayerEntity();
            var lawfulTarget = session.State.Entities.First(value =>
                value.Kind == EntityKind.Npc && catalog.Factions[value.FactionId].Kind == FactionKind.Empire);
            lawfulTarget.Position = player.Position + new SimVec2(5d, 0d);

            session.Enqueue(new GameCommand(GameCommandType.Lock, lawfulTarget.Id));
            session.Enqueue(new GameCommand(GameCommandType.ActivateModule, index: 0));
            var batch = session.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(session.State.Player.CriminalTimer, Is.EqualTo(120d).Within(1e-9d));
            Assert.That(session.State.Player.Standings[lawfulTarget.FactionId], Is.LessThan(1d));
            Assert.That(session.State.Player.Standings[FactionIds.Directorate], Is.LessThan(0d));
            Assert.That(session.State.Entities.Count(value => value.FactionId == FactionIds.Directorate && value.ShipId == ShipIds.Enforcer), Is.EqualTo(2));
            Assert.That(batch.Any(value => value.Type == SimulationEventType.Log && value.Message.Contains("Directorate response")), Is.True);
        }

        [Test]
        public void StationMarket_BuySellFitAndUnfitRoundTripInventory()
        {
            var session = CreateSession();
            var player = session.State.Player;
            var ship = player.ActiveShip();
            player.Credits = 1_000_000L;

            var beforeBuy = player.Credits;
            Execute(session, new GameCommand(GameCommandType.Buy, ModuleIds.Afterburner));
            Assert.That(player.Credits, Is.LessThan(beforeBuy));
            Assert.That(player.Hangar[ModuleIds.Afterburner], Is.EqualTo(1));

            Execute(session, new GameCommand(GameCommandType.Fit,
                ship.InstanceId + "|mid|1|" + ModuleIds.Afterburner));
            Assert.That(ship.Fitting.Mid[1], Is.EqualTo(ModuleIds.Afterburner));
            Assert.That(player.Hangar.ContainsKey(ModuleIds.Afterburner), Is.False);

            Execute(session, new GameCommand(GameCommandType.Unfit, ship.InstanceId + "|mid|1"));
            Assert.That(ship.Fitting.Mid[1], Is.Null);
            Assert.That(player.Hangar[ModuleIds.Afterburner], Is.EqualTo(1));

            var beforeModuleSale = player.Credits;
            Execute(session, new GameCommand(GameCommandType.Sell, ModuleIds.Afterburner));
            Assert.That(player.Credits, Is.GreaterThan(beforeModuleSale));
            Assert.That(player.Hangar.ContainsKey(ModuleIds.Afterburner), Is.False);

            player.Cargo[ItemIds.Ferrite] = 5d;
            var beforeOreSale = player.Credits;
            Execute(session, new GameCommand(GameCommandType.Sell, ItemIds.Ferrite));
            Assert.That(player.Credits, Is.GreaterThan(beforeOreSale));
            Assert.That(player.Cargo.ContainsKey(ItemIds.Ferrite), Is.False);
        }

        [Test]
        public void LoyaltyExchange_RequiresOneHundredLp_AndAwardsDamageAmpDeterministically()
        {
            var session = CreateSession();
            var player = session.State.Player;
            player.LoyaltyPoints[player.EmpireId] = 99;

            var rejected = Execute(session, new GameCommand(GameCommandType.ExchangeLoyalty));
            Assert.That(player.LoyaltyPoints[player.EmpireId], Is.EqualTo(99));
            Assert.That(player.Hangar.ContainsKey(ModuleIds.DamageAmp), Is.False);
            Assert.That(rejected.Any(value => value.Type == SimulationEventType.Log &&
                value.Message.Contains("requires 100 LP")), Is.True);

            player.LoyaltyPoints[player.EmpireId] = 100;
            var accepted = Execute(session, new GameCommand(GameCommandType.ExchangeLoyalty));
            Assert.That(player.LoyaltyPoints[player.EmpireId], Is.Zero);
            Assert.That(player.Hangar[ModuleIds.DamageAmp], Is.EqualTo(1));
            Assert.That(accepted.Any(value => value.Type == SimulationEventType.Inventory &&
                value.Detail == ModuleIds.DamageAmp), Is.True);
        }

        [TestCase("security", MissionType.Security)]
        [TestCase("distribution", MissionType.Distribution)]
        [TestCase("mining", MissionType.Mining)]
        public void AgentDivisions_GenerateAndAcceptAllThreeRegularMissionTypes(
            string division, MissionType expectedType)
        {
            var session = CreateSession();
            var station = universe.Systems[session.State.Player.CurrentSystemId].Stations[0];
            var agent = new AgentDefinition("agent_test_" + division, "Test Agent", division, 2, station.Id);
            station.Agents.Add(agent);

            var batch = Execute(session, new GameCommand(GameCommandType.TalkToAgent, agent.Id));
            var mission = session.State.Player.Missions.Single();

            Assert.That(mission.Id, Is.EqualTo("mis_1"));
            Assert.That(mission.AgentId, Is.EqualTo(agent.Id));
            Assert.That(mission.Type, Is.EqualTo(expectedType));
            Assert.That(mission.Status, Is.EqualTo(MissionStatus.Active));
            Assert.That(mission.RewardCredits, Is.GreaterThan(0));
            Assert.That(batch.Count(value => value.Type == SimulationEventType.Mission), Is.EqualTo(2));
            if (expectedType == MissionType.Distribution)
                Assert.That(session.State.Player.Cargo[ItemIds.SealedCargo], Is.EqualTo(mission.Quantity));
        }

        [Test]
        public void CompletingFifthRegularMission_OffersStorylineMission()
        {
            var session = CreateSession();
            session.State.Player.MissionCounts[FactionIds.Aurelian] = 4;
            var regular = new MissionState
            {
                Id = "mis_completed_regular",
                Type = MissionType.Mining,
                Status = MissionStatus.ObjectivesMet,
                Title = "Completed Regular Mission",
                FactionId = FactionIds.Aurelian,
                RewardCredits = 1234L,
                RewardLoyaltyPoints = 7,
                RewardStanding = 0.12d,
            };
            session.State.Player.Missions.Add(regular);

            var batch = Execute(session, new GameCommand(GameCommandType.CompleteMission, regular.Id));
            var storyline = session.State.Player.Missions.Single(value =>
                value.Type == MissionType.StorylineKill || value.Type == MissionType.StorylineHaul);

            Assert.That(regular.Status, Is.EqualTo(MissionStatus.Done));
            Assert.That(session.State.Player.MissionCounts[FactionIds.Aurelian], Is.EqualTo(5));
            Assert.That(storyline.Status, Is.EqualTo(MissionStatus.Offered));
            Assert.That(storyline.FactionId, Is.EqualTo(FactionIds.Aurelian));
            Assert.That(batch.Any(value => value.Type == SimulationEventType.Mission && value.Detail == "offered" && value.TargetId == storyline.Id), Is.True);
        }

        [Test]
        public void PlayerDeathAndRespawn_ReturnsRookieFrigateToHomeStation()
        {
            var session = CreateSession();
            Undock(session);
            var player = session.State.PlayerEntity();
            var attacker = session.State.Entities.First(value => value.Kind == EntityKind.Npc);
            attacker.AiBehavior = "police";
            attacker.AggroRange = 99999d;
            attacker.LockedTargetId = player.Id;
            attacker.Position = player.Position;
            player.Shield = 0d;
            player.Armor = 0d;
            player.Hull = 1d;
            player.LastDamageAt = session.State.SimulationTime;

            var deathBatch = session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(session.State.PlayerDead, Is.True);
            Assert.That(deathBatch.Any(value => value.Type == SimulationEventType.Death && value.TargetId == "player"), Is.True);

            var respawnBatch = Execute(session, new GameCommand(GameCommandType.Respawn));
            var replacement = session.State.Player.ActiveShip();
            Assert.That(session.State.PlayerDead, Is.False);
            Assert.That(session.State.Docked, Is.True);
            Assert.That(session.State.Player.CurrentSystemId, Is.EqualTo(session.State.Player.HomeSystemId));
            Assert.That(session.State.Player.DockedAtStationId, Is.EqualTo(session.State.Player.HomeStationId));
            Assert.That(replacement.ShipId, Is.EqualTo(ShipIds.Acolyte));
            Assert.That(replacement.InstanceId, Is.Not.EqualTo("ship_start"));
            Assert.That(respawnBatch.Any(value => value.Type == SimulationEventType.SaveRequested && value.Detail == "auto"), Is.True);
        }

        [Test]
        public void SameSeedAndCommands_ProduceStableEntityAndAsteroidIds()
        {
            var first = new GameSession(new UniverseGenerator().Generate(12345), new GameContentCatalog(), "Pilot", FactionIds.Aurelian);
            var second = new GameSession(new UniverseGenerator().Generate(12345), new GameContentCatalog(), "Pilot", FactionIds.Aurelian);
            Undock(first);
            Undock(second);

            CollectionAssert.AreEqual(
                first.State.Entities.Select(Signature).ToArray(),
                second.State.Entities.Select(Signature).ToArray());
            CollectionAssert.AreEqual(
                first.State.Asteroids.Select(value => value.Id + "|" + value.OreId).ToArray(),
                second.State.Asteroids.Select(value => value.Id + "|" + value.OreId).ToArray());
            Assert.That(first.State.NextEntityId, Is.EqualTo(second.State.NextEntityId));
            Assert.That(first.State.RngState, Is.EqualTo(second.State.RngState));
            Assert.That(ShipIds.All.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(18));
            Assert.That(FactionIds.All.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(10));
        }

        [Test]
        public void SimulationSource_UsesNoWallClockGuidOrUnityRandom()
        {
            var sourceRoot = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Starfall", "Simulation");
            Assert.That(Directory.Exists(sourceRoot), Is.True, "Could not locate Simulation source directory.");
            var source = string.Join("\n", Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .OrderBy(value => value, StringComparer.Ordinal)
                .Select(File.ReadAllText));

            StringAssert.DoesNotContain("UnityEngine.Random", source);
            StringAssert.DoesNotContain("System.DateTime", source);
            StringAssert.DoesNotContain("DateTime.Now", source);
            StringAssert.DoesNotContain("DateTime.UtcNow", source);
            StringAssert.DoesNotContain("System.Guid", source);
            StringAssert.DoesNotContain("Guid.NewGuid", source);
        }

        private GameSession CreateSession(string empireId = FactionIds.Aurelian)
        {
            return new GameSession(universe, catalog, "  Test Pilot  ", empireId);
        }

        private static SimulationEventBatch Execute(GameSession session, GameCommand command)
        {
            session.Enqueue(command);
            return session.AdvanceFrame(GameSession.FixedStepSeconds);
        }

        private static void Undock(GameSession session)
        {
            var batch = Execute(session, new GameCommand(GameCommandType.Undock));
            Assert.That(session.State.Docked, Is.False);
            Assert.That(session.State.PlayerEntity(), Is.Not.Null);
            Assert.That(batch.Any(value => value.Type == SimulationEventType.Dock && value.Detail == "undock"), Is.True);
            Assert.That(batch.Any(value => value.Type == SimulationEventType.SaveRequested && value.Detail == "auto"), Is.True);
        }

        private static string Signature(EntityState entity)
        {
            return entity.Id + "|" + entity.Kind + "|" + entity.ShipId + "|" + entity.FactionId + "|" +
                   entity.Position.X.ToString("R") + "|" + entity.Position.Z.ToString("R");
        }
    }
}
