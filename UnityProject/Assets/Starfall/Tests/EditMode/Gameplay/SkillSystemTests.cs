using System;
using System.Linq;
using NUnit.Framework;
using Starfall.Content;
using Starfall.Domain;
using Starfall.Simulation;

namespace Starfall.Tests.EditMode.Gameplay
{
    public sealed class SkillSystemTests
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
        public void Catalog_ExposesEverySkillDefinition()
        {
            CollectionAssert.AreEquivalent(SkillIds.All, catalog.Skills.Keys);
            Assert.That(catalog.Skills.Values.All(skill => skill.Rank >= 1), Is.True);
        }

        [Test]
        public void NewPilot_StartsWithSpaceshipCommandOneAndEmptyQueue()
        {
            var session = CreateSession();

            Assert.That(session.State.Player.SkillLevels[SkillIds.SpaceshipCommand], Is.EqualTo(1));
            Assert.That(session.State.Player.SkillQueue, Is.Empty);
            Assert.That(session.State.Player.SkillLevels.TryGetValue(SkillIds.Gunnery, out _), Is.False,
                "Combat skills start untrained; the first level must be an early goal.");
        }

        [Test]
        public void Training_AdvancesLevelsOnTheDoubledPointCurve()
        {
            var session = CreateSession();
            Execute(session, new GameCommand(GameCommandType.TrainSkill, SkillIds.Gunnery));

            // The Execute frame itself trains 0.05 s; the offline credit adds 399 s.
            session.ApplyOfflineTraining(399d);
            Assert.That(session.State.Player.SkillLevels.TryGetValue(SkillIds.Gunnery, out _), Is.False,
                "400 points are required for level 1.");
            Assert.That(session.State.Player.SkillPoints[SkillIds.Gunnery], Is.InRange(399d, 400d));

            session.ApplyOfflineTraining(1d);
            Assert.That(session.State.Player.SkillLevels[SkillIds.Gunnery], Is.EqualTo(1));
            Assert.That(session.State.Player.SkillPoints[SkillIds.Gunnery], Is.LessThan(0.1d),
                "Only the sub-second remainder may carry past a level-up.");

            session.ApplyOfflineTraining(800d);
            Assert.That(session.State.Player.SkillLevels[SkillIds.Gunnery], Is.EqualTo(2),
                "Level 2 costs twice level 1.");
        }

        [Test]
        public void Training_RunsWhileDockedAtSimRate()
        {
            var session = CreateSession();
            Execute(session, new GameCommand(GameCommandType.TrainSkill, SkillIds.Gunnery));

            // The Execute frame above already ran one step; 99 more make 100 total.
            for (var i = 0; i < 99; i++) session.AdvanceFrame(GameSession.FixedStepSeconds);

            Assert.That(session.State.SimulationTime, Is.EqualTo(5d).Within(1e-9d));
            Assert.That(session.State.Player.SkillPoints[SkillIds.Gunnery], Is.EqualTo(5d).Within(1e-9d));
        }

        [Test]
        public void Queue_TrainsInOrder_PopsMaxedSkills_AndToggles()
        {
            var session = CreateSession();
            var player = session.State.Player;
            player.SkillLevels[SkillIds.Gunnery] = SkillRules.MaxLevel;
            Execute(session, new GameCommand(GameCommandType.TrainSkill, SkillIds.Gunnery),
                new GameCommand(GameCommandType.TrainSkill, SkillIds.Mining),
                new GameCommand(GameCommandType.TrainSkill, SkillIds.Navigation));

            var batch = session.ApplyOfflineTraining(100d);
            Assert.That(player.SkillQueue.First(), Is.EqualTo(SkillIds.Mining),
                "A maxed skill must be skipped immediately.");
            Assert.That(player.SkillPoints[SkillIds.Mining], Is.InRange(100d, 101d));
            Assert.That(batch.Any(value => value.Type == SimulationEventType.SkillTrained), Is.False);

            Execute(session, new GameCommand(GameCommandType.TrainSkill, SkillIds.Mining));
            Assert.That(player.SkillQueue, Is.EqualTo(new[] { SkillIds.Navigation }),
                "Tapping a queued skill removes it from the queue.");
        }

        [Test]
        public void BuyShip_BlockedUntilSpaceshipCommandMeetsTheClassRequirement()
        {
            var session = CreateSession();
            var player = session.State.Player;
            player.Credits = 20_000_000L;

            var rejected = Execute(session, new GameCommand(GameCommandType.Buy, ShipIds.Templar));
            Assert.That(player.Ships.Any(ship => ship.ShipId == ShipIds.Templar), Is.False);
            Assert.That(rejected.Any(value => value.Type == SimulationEventType.Log &&
                value.Message.Contains("Spaceship Command")), Is.True);

            player.SkillLevels[SkillIds.SpaceshipCommand] = 2;
            Execute(session, new GameCommand(GameCommandType.Buy, ShipIds.Templar));
            Assert.That(player.Ships.Any(ship => ship.ShipId == ShipIds.Templar), Is.True,
                "Destroyers unlock at Spaceship Command II.");

            Execute(session, new GameCommand(GameCommandType.Buy, ShipIds.Seraph));
            Assert.That(player.Ships.Any(ship => ship.ShipId == ShipIds.Seraph), Is.False,
                "Battleships stay gated at Spaceship Command IV.");
        }

        [Test]
        public void FitModule_BlockedUntilGunneryMeetsTheRequirement()
        {
            var session = CreateSession();
            var player = session.State.Player;
            var cruiser = OwnCruiser(session);
            player.Hangar[ModuleIds.HeavyLaser] = 1;

            var rejected = Execute(session,
                new GameCommand(GameCommandType.Fit, cruiser.InstanceId + "|high|0|" + ModuleIds.HeavyLaser));
            Assert.That(cruiser.Fitting.High[0], Is.Null);
            Assert.That(rejected.Any(value => value.Type == SimulationEventType.Log &&
                value.Message.Contains("Gunnery")), Is.True);

            player.SkillLevels[SkillIds.Gunnery] = 3;
            Execute(session,
                new GameCommand(GameCommandType.Fit, cruiser.InstanceId + "|high|0|" + ModuleIds.HeavyLaser));
            Assert.That(cruiser.Fitting.High[0], Is.EqualTo(ModuleIds.HeavyLaser));
        }

        [Test]
        public void MiningSkill_ScalesYieldPerLevel()
        {
            var session = CreateSession();
            session.State.Player.SkillLevels[SkillIds.Mining] = 1;
            Undock(session);
            var playerEntity = session.State.PlayerEntity();
            var asteroid = session.State.Asteroids.First();
            asteroid.Position = playerEntity.Position;
            var amountBefore = asteroid.Amount;
            var miningIndex = playerEntity.Modules.FindIndex(value => value.ModuleId == ModuleIds.MiningLaser);

            Execute(session, new GameCommand(GameCommandType.Select, asteroid.Id),
                new GameCommand(GameCommandType.ActivateModule, index: miningIndex));

            Assert.That(asteroid.Amount, Is.EqualTo(amountBefore - 10.5d).Within(1e-6d),
                "+5% per level turns a 10 unit cycle into 10.5 (capped by the rock).");
            Assert.That(session.State.Player.Cargo[asteroid.OreId], Is.EqualTo(10.5d).Within(1e-6d));
        }

        [Test]
        public void NavigationSkill_IncreasesPlayerMaxSpeedOnly()
        {
            var session = CreateSession();
            session.State.Player.SkillLevels[SkillIds.Navigation] = 2;
            Undock(session);

            var playerEntity = session.State.PlayerEntity();
            Assert.That(playerEntity.MaxSpeed, Is.EqualTo(150d * 1.10d).Within(1e-9d));

            var npc = session.State.Entities.First(value => value.Kind == EntityKind.Npc);
            Assert.That(npc.MaxSpeed, Is.EqualTo(catalog.Ships[npc.ShipId].Speed).Within(1e-9d),
                "NPC hulls must not inherit pilot skills.");
        }

        [Test]
        public void GunnerySkill_ScalesPlayerWeaponDamage()
        {
            var session = CreateSession();
            session.State.Player.SkillLevels[SkillIds.Gunnery] = 5;
            Undock(session);
            var playerEntity = session.State.PlayerEntity();
            var target = session.State.Entities.First(value => value.Kind == EntityKind.Npc);
            target.Position = playerEntity.Position + new SimVec2(5d, 0d);
            target.AggroRange = 0d;
            playerEntity.LockedTargetId = target.Id;
            playerEntity.Modules[0].Active = true;

            var batch = session.AdvanceFrame(GameSession.FixedStepSeconds);
            var shot = batch.Single(value => value.Type == SimulationEventType.Weapon && value.TargetId == target.Id);
            // Base roll is 0.85..1.15 of module damage; Gunnery V adds 20%.
            var factor = shot.Value / catalog.Modules[ModuleIds.PulseLaser].Damage;
            Assert.That(factor, Is.InRange(0.85d * 1.20d, 1.15d * 1.20d));
        }

        [Test]
        public void RepairSkills_ScaleBoosterAndRepairerOutput()
        {
            var session = CreateSession();
            var player = session.State.Player;
            player.SkillLevels[SkillIds.ShieldOperation] = 2;
            player.SkillLevels[SkillIds.Mechanics] = 1;
            var ship = player.ActiveShip();
            ship.Fitting.Mid[0] = ModuleIds.ShieldBooster;
            ship.Fitting.Low[0] = ModuleIds.ArmorRepairer;
            Undock(session);
            var playerEntity = session.State.PlayerEntity();
            playerEntity.Shield = 0d;
            playerEntity.Armor = 0d;
            // Recent damage suppresses the 2%/s passive regen for this frame.
            playerEntity.LastDamageAt = session.State.SimulationTime;
            var boosterIndex = playerEntity.Modules.FindIndex(value => value.ModuleId == ModuleIds.ShieldBooster);
            var repairIndex = playerEntity.Modules.FindIndex(value => value.ModuleId == ModuleIds.ArmorRepairer);

            Execute(session, new GameCommand(GameCommandType.ActivateModule, index: boosterIndex),
                new GameCommand(GameCommandType.ActivateModule, index: repairIndex));

            Assert.That(playerEntity.Shield, Is.EqualTo(90d * 1.10d).Within(1e-9d));
            Assert.That(playerEntity.Armor, Is.EqualTo(70d * 1.05d).Within(1e-9d));
        }

        [Test]
        public void OfflineTraining_CapsAtTheCeilingAndLogsTheSummary()
        {
            var session = CreateSession();
            Execute(session, new GameCommand(GameCommandType.TrainSkill, SkillIds.Gunnery));

            var batch = session.ApplyOfflineTraining(30d * 24d * 3600d);

            Assert.That(session.State.Player.SkillLevels[SkillIds.Gunnery], Is.EqualTo(SkillRules.MaxLevel),
                "A maxed skill stops consuming queue time.");
            var summary = batch.Single(value => value.Type == SimulationEventType.Log &&
                value.Message.Contains("Offline training"));
            Assert.That(summary.Message, Does.Contain((SkillRules.MaxOfflineSeconds / 3600d).ToString("0.0")),
                "The credited interval must be capped at SkillRules.MaxOfflineSeconds.");
        }

        [Test]
        public void LoadCtor_GrandfathersSkillsForPreSkillSaves()
        {
            var player = new PlayerState
            {
                EmpireId = FactionIds.Aurelian,
                CurrentSystemId = universe.StartSystems[FactionIds.Aurelian],
                DockedAtStationId = universe.Systems[universe.StartSystems[FactionIds.Aurelian]].Stations[0].Id,
                ActiveShipInstanceId = "ship_legacy",
            };
            player.Ships.Add(new ShipInstanceState
            {
                InstanceId = "ship_legacy",
                ShipId = ShipIds.Dawnbringer,
                Name = "Legacy Cruiser",
                Fitting = new FittingState { High = { ModuleIds.HeavyLaser }, Mid = { null }, Low = { null } },
            });

            var session = new GameSession(universe, catalog, player, 0d, 1u, 100u);

            Assert.That(session.State.Player.SkillLevels[SkillIds.SpaceshipCommand], Is.EqualTo(3),
                "An old save flying a cruiser keeps flying it.");
            Assert.That(session.State.Player.SkillLevels[SkillIds.Gunnery], Is.EqualTo(3),
                "Fitted hardware requirements are granted with the hull.");
        }

        [Test]
        public void LoadCtor_NormalizesStaleSkillPayload()
        {
            var player = new PlayerState
            {
                EmpireId = FactionIds.Aurelian,
                CurrentSystemId = universe.StartSystems[FactionIds.Aurelian],
                DockedAtStationId = universe.Systems[universe.StartSystems[FactionIds.Aurelian]].Stations[0].Id,
                ActiveShipInstanceId = "ship_legacy",
                SkillLevels = { [SkillIds.Gunnery] = 99 },
            };
            player.Ships.Add(new ShipInstanceState
            {
                InstanceId = "ship_legacy",
                ShipId = ShipIds.Acolyte,
                Name = "Legacy",
                Fitting = new FittingState { High = { null, null }, Mid = { null, null }, Low = { null, null } },
            });
            player.SkillQueue.Add("removed_skill");
            player.SkillQueue.Add(SkillIds.Mining);

            var session = new GameSession(universe, catalog, player, 0d, 1u, 100u);

            Assert.That(session.State.Player.SkillLevels[SkillIds.Gunnery], Is.EqualTo(SkillRules.MaxLevel));
            Assert.That(session.State.Player.SkillQueue, Is.EqualTo(new[] { SkillIds.Mining }),
                "Queue entries the catalog no longer knows must be dropped.");
        }

        private GameSession CreateSession()
        {
            return new GameSession(universe, catalog, "Skill Pilot", FactionIds.Aurelian);
        }

        private ShipInstanceState OwnCruiser(GameSession session)
        {
            var player = session.State.Player;
            var cruiser = new ShipInstanceState
            {
                InstanceId = "ship_cruiser",
                ShipId = ShipIds.Dawnbringer,
                Name = "Test Cruiser",
                Fitting = new FittingState { High = { null, null, null, null }, Mid = { null, null, null }, Low = { null, null, null, null } },
                Shield = catalog.Ships[ShipIds.Dawnbringer].HitPoints.Shield,
                Armor = catalog.Ships[ShipIds.Dawnbringer].HitPoints.Armor,
                Hull = catalog.Ships[ShipIds.Dawnbringer].HitPoints.Hull,
            };
            player.Ships.Add(cruiser);
            player.ActiveShipInstanceId = cruiser.InstanceId;
            return cruiser;
        }

        private static SimulationEventBatch Execute(GameSession session, params GameCommand[] commands)
        {
            foreach (var command in commands) session.Enqueue(command);
            return session.AdvanceFrame(GameSession.FixedStepSeconds);
        }

        private static void Undock(GameSession session)
        {
            Execute(session, new GameCommand(GameCommandType.Undock));
            Assert.That(session.State.PlayerEntity(), Is.Not.Null);
        }
    }
}
