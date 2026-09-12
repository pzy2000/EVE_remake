using System;
using System.Linq;
using NUnit.Framework;
using Starfall.Content;
using Starfall.Domain;
using Starfall.Simulation;

namespace Starfall.Tests.EditMode.Gameplay
{
    public sealed class FittingConstraintTests
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
        public void Hulls_And_Modules_CarryGridAndCpuBudgets()
        {
            Assert.That(catalog.Ships[ShipIds.Acolyte].PowerGrid, Is.EqualTo(35d));
            Assert.That(catalog.Ships[ShipIds.Acolyte].Cpu, Is.EqualTo(110d));
            Assert.That(catalog.Ships[ShipIds.Seraph].PowerGrid, Is.EqualTo(320d));
            Assert.That(catalog.Modules[ModuleIds.HeavyLaser].Size, Is.EqualTo(ModuleSize.Medium));
            Assert.That(catalog.Modules.Values.All(module => module.PowerGrid >= 0d && module.Cpu >= 0d), Is.True);
        }

        [Test]
        public void MediumWeapon_RejectedOnFrigate_BySize()
        {
            var session = CreateSession();
            var player = session.State.Player;
            player.SkillLevels[SkillIds.Gunnery] = SkillRules.MaxLevel;
            player.Hangar[ModuleIds.HeavyLaser] = 1;
            var ship = player.ActiveShip();
            Execute(session, new GameCommand(GameCommandType.Unfit, ship.InstanceId + "|high|1"));

            var rejected = Execute(session,
                new GameCommand(GameCommandType.Fit, ship.InstanceId + "|high|1|" + ModuleIds.HeavyLaser));

            Assert.That(ship.Fitting.High[1], Is.Null);
            Assert.That(rejected.Any(value => value.Type == SimulationEventType.Log &&
                value.Message.Contains("Medium")), Is.True,
                "The size rejection must name the module tier.");
        }

        [Test]
        public void HeavyLaser_FitsACruiser_OnceSkillsAllow()
        {
            var session = CreateSession();
            var player = session.State.Player;
            player.SkillLevels[SkillIds.SpaceshipCommand] = 3;
            player.SkillLevels[SkillIds.Gunnery] = 3;
            var cruiser = OwnCruiser(session);
            player.Hangar[ModuleIds.HeavyLaser] = 1;

            Execute(session,
                new GameCommand(GameCommandType.Fit, cruiser.InstanceId + "|high|0|" + ModuleIds.HeavyLaser));

            Assert.That(cruiser.Fitting.High[0], Is.EqualTo(ModuleIds.HeavyLaser),
                "Grid 30/130 and CPU 45/260 leave plenty of room on a cruiser.");
        }

        [Test]
        public void CpuBudget_BlocksTheModuleThatWouldOverflow()
        {
            var session = CreateSession();
            var player = session.State.Player;
            var cruiser = OwnCruiser(session);
            // 4 railguns (88) + 3 boosters (84) + 3 amps (75) = 247 of 260 CPU with
            // only 82 of 130 grid used, so CPU is the constraint that trips first.
            for (var i = 0; i < 4; i++) cruiser.Fitting.High[i] = ModuleIds.Railgun;
            for (var i = 0; i < 3; i++) cruiser.Fitting.Mid[i] = ModuleIds.ShieldBooster;
            for (var i = 0; i < 3; i++) cruiser.Fitting.Low[i] = ModuleIds.DamageAmp;

            var fits = FittingRules.CanFitModule(catalog, cruiser, "low", 3, ModuleIds.DamageAmp,
                null, out var reason, out _);
            Assert.That(fits, Is.False);
            Assert.That(reason, Is.EqualTo("Not enough CPU."));

            // Unfitting an amp drops usage to 222; an expander (20 CPU) now fits.
            cruiser.Fitting.Low[2] = null;
            var afterUnfitting = FittingRules.CanFitModule(catalog, cruiser, "low", 2, ModuleIds.CargoExpander,
                null, out reason, out _);
            Assert.That(afterUnfitting, Is.True, reason);
        }

        [Test]
        public void PowerGridBudget_BlocksTheSecondArmorPlate()
        {
            var session = CreateSession();
            var player = session.State.Player;
            var ship = player.ActiveShip();
            // Starter fit uses 21 of 35 grid; one plate reaches 33, a second 45.
            player.Hangar[ModuleIds.ArmorPlate] = 2;
            Execute(session, new GameCommand(GameCommandType.Fit, ship.InstanceId + "|low|0|" + ModuleIds.ArmorPlate),
                new GameCommand(GameCommandType.Fit, ship.InstanceId + "|low|1|" + ModuleIds.ArmorPlate));

            Assert.That(ship.Fitting.Low.Count(fitted => fitted == ModuleIds.ArmorPlate), Is.EqualTo(1),
                "The second plate must exceed the frigate power grid.");
        }

        [Test]
        public void DamageAmps_StackWithDiminishingReturns()
        {
            var session = CreateSession();
            var player = session.State.Player;
            player.SkillLevels[SkillIds.SpaceshipCommand] = 3;
            var cruiser = OwnCruiser(session);
            player.Hangar[ModuleIds.DamageAmp] = 4;
            for (var i = 0; i < 4; i++)
                Execute(session, new GameCommand(GameCommandType.Fit, cruiser.InstanceId + "|low|" + i + "|" + ModuleIds.DamageAmp));
            SwitchAndUndock(session, cruiser);

            var playerEntity = session.State.PlayerEntity();
            var expected = FittingRules.StackedMultiplicative(4, 1.18d);
            Assert.That(playerEntity.DamageMultiplier, Is.EqualTo(expected).Within(1e-12d));
            Assert.That(playerEntity.DamageMultiplier, Is.LessThan(1.62d),
                "Four amps used to multiply to 1.18^4 = 1.94.");
        }

        [Test]
        public void ShieldExtenders_And_ArmorPlates_StackAdditivelyPenalized()
        {
            var session = CreateSession();
            var player = session.State.Player;
            player.SkillLevels[SkillIds.SpaceshipCommand] = 3;
            var cruiser = OwnCruiser(session);
            player.Hangar[ModuleIds.ShieldExtender] = 2;
            player.Hangar[ModuleIds.ArmorPlate] = 1;
            Execute(session, new GameCommand(GameCommandType.Fit, cruiser.InstanceId + "|low|0|" + ModuleIds.ShieldExtender),
                new GameCommand(GameCommandType.Fit, cruiser.InstanceId + "|low|1|" + ModuleIds.ShieldExtender),
                new GameCommand(GameCommandType.Fit, cruiser.InstanceId + "|low|2|" + ModuleIds.ArmorPlate));
            SwitchAndUndock(session, cruiser);

            var playerEntity = session.State.PlayerEntity();
            Assert.That(playerEntity.MaxShield, Is.EqualTo(1100d + FittingRules.StackedAdditive(2, 200d)).Within(1e-9d));
            Assert.That(playerEntity.MaxArmor, Is.EqualTo(1500d + 250d).Within(1e-9d));
        }

        [Test]
        public void SingleAfterburner_StaysACleanSpeedMultiplier()
        {
            var session = CreateSession();
            var player = session.State.Player;
            var ship = player.ActiveShip();
            player.Hangar[ModuleIds.Afterburner] = 1;
            Execute(session, new GameCommand(GameCommandType.Fit, ship.InstanceId + "|mid|1|" + ModuleIds.Afterburner));
            Undock(session);
            var playerEntity = session.State.PlayerEntity();

            Execute(session, new GameCommand(GameCommandType.ActivateModule,
                index: playerEntity.Modules.FindIndex(value => value.ModuleId == ModuleIds.Afterburner)));

            Assert.That(playerEntity.MaxSpeed, Is.EqualTo(150d * 1.8d).Within(1e-9d),
                "A single afterburner must remain a clean 1.8x (270).");
        }

        [Test]
        public void CargoExpanders_StackPenalized()
        {
            var session = CreateSession();
            var player = session.State.Player;
            var ship = player.ActiveShip();
            player.Hangar[ModuleIds.CargoExpander] = 2;
            Execute(session, new GameCommand(GameCommandType.Fit, ship.InstanceId + "|low|0|" + ModuleIds.CargoExpander),
                new GameCommand(GameCommandType.Fit, ship.InstanceId + "|low|1|" + ModuleIds.CargoExpander));

            // 160 base + 250 + 250×0.869 = 627.25 m3; observed through mining overflow.
            Undock(session);
            var playerEntity = session.State.PlayerEntity();
            var asteroid = session.State.Asteroids.First();
            asteroid.Position = playerEntity.Position;
            asteroid.Amount = 100000d;
            var miningIndex = playerEntity.Modules.FindIndex(value => value.ModuleId == ModuleIds.MiningLaser);

            Execute(session, new GameCommand(GameCommandType.Select, asteroid.Id),
                new GameCommand(GameCommandType.ActivateModule, index: miningIndex));
            var capacity = 160d + FittingRules.StackedAdditive(2, 250d);
            var volume = catalog.Items[asteroid.OreId].Volume;
            // 627 m3 at 10 units per 4 s cycle needs ~250 simulated seconds.
            for (var i = 0; i < 7000; i++) session.AdvanceFrame(GameSession.FixedStepSeconds);

            var used = session.State.Player.Cargo[asteroid.OreId];
            Assert.That(used * volume, Is.GreaterThanOrEqualTo(capacity - 25d),
                "Mining must run until the hold is within one cycle of full.");
            Assert.That(used * volume, Is.LessThanOrEqualTo(capacity + 1e-6d),
                "Mining must stop at the stack-penalized capacity, not 160+500.");
        }

        [Test]
        public void NpcDefaultFits_StayWithinTheirHullBudgets()
        {
            // CreateEntity fills every high slot with the faction weapon and adds
            // a booster plus amp to non-frigates; those presets must stay legal.
            foreach (var hullId in new[] { ShipIds.Templar, ShipIds.Dawnbringer, ShipIds.Seraph, ShipIds.Heron, ShipIds.Rook, ShipIds.Onyx })
            {
                var hull = catalog.Ships[hullId];
                var weaponId = hull.FactionId == FactionIds.Kaldari ? ModuleIds.MissileLauncher : ModuleIds.PulseLaser;
                var weapon = catalog.Modules[weaponId];
                var ship = new ShipInstanceState { ShipId = hullId, Fitting = new FittingState() };
                for (var i = 0; i < hull.Slots.High; i++) ship.Fitting.High.Add(weaponId);
                ship.Fitting.Mid.Add(ModuleIds.ShieldBooster);
                ship.Fitting.Low.Add(ModuleIds.DamageAmp);
                FittingRules.FittingUsage(catalog, ship, out var grid, out var cpu);
                Assert.That(grid, Is.LessThanOrEqualTo(hull.PowerGrid + 1e-9d), hullId + " grid");
                Assert.That(cpu, Is.LessThanOrEqualTo(hull.Cpu + 1e-9d), hullId + " cpu");
                Assert.That(weapon.Size, Is.EqualTo(ModuleSize.Small), hullId + " default weapon must be small");
            }
        }

        private GameSession CreateSession()
        {
            return new GameSession(universe, catalog, "Fitting Pilot", FactionIds.Aurelian);
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
            return cruiser;
        }

        private static void SwitchAndUndock(GameSession session, ShipInstanceState ship)
        {
            Execute(session, new GameCommand(GameCommandType.SwitchShip, ship.InstanceId));
            Undock(session);
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
