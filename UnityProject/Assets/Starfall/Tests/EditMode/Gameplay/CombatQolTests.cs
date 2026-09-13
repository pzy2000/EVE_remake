using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Starfall.Content;
using Starfall.Domain;
using Starfall.Simulation;

namespace Starfall.Tests.EditMode.Gameplay
{
    /// <summary>
    /// Station services and active tanking: repairs must target
    /// fitting-adjusted ceilings, defensive modules must cycle like weapons,
    /// and selling a hull must not eat its fitted modules.
    /// </summary>
    public sealed class CombatQolTests
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
        public void Repair_TargetsFittingAdjustedCeilings_NotTheBareHull()
        {
            var session = CreateSession();
            var ship = session.State.Player.ActiveShip();
            session.State.Player.Credits = 1_000_000L;
            var definition = catalog.Ships[ship.ShipId];
            FitFirstFree(ship.Fitting.Low, ModuleIds.ArmorPlate);
            FitFirstFree(ship.Fitting.Mid, ModuleIds.ShieldExtender);

            ship.Armor = definition.HitPoints.Armor * 0.5d;
            ship.Shield = definition.HitPoints.Shield;
            ship.Hull = definition.HitPoints.Hull;
            var creditsBefore = session.State.Player.Credits;

            Execute(session, GameCommandType.Repair);

            var expectedArmor = definition.HitPoints.Armor +
                FittingRules.StackedAdditive(1, catalog.Modules[ModuleIds.ArmorPlate].ArmorBonus);
            var expectedShield = definition.HitPoints.Shield +
                FittingRules.StackedAdditive(1, catalog.Modules[ModuleIds.ShieldExtender].ShieldBonus);
            Assert.That(ship.Armor, Is.EqualTo(expectedArmor).Within(1e-9d),
                "Armor must repair up to the plate-adjusted ceiling, not the bare hull value.");
            Assert.That(ship.Shield, Is.EqualTo(expectedShield).Within(1e-9d),
                "Shield must top out at the extender-adjusted ceiling, never be 'repaired' below it.");
            Assert.That(session.State.Player.Credits,
                Is.EqualTo(creditsBefore - (long)JsMath.Round(definition.Price * 0.04d)));
        }

        [Test]
        public void Repair_ShortOnCash_StillFillsShieldAndSkipsUnaffordableLayers()
        {
            var session = CreateSession();
            var ship = session.State.Player.ActiveShip();
            var definition = catalog.Ships[ship.ShipId];
            session.State.Player.Credits = 1L;
            ship.Armor = definition.HitPoints.Armor * 0.5d;
            ship.Hull = definition.HitPoints.Hull * 0.5d;
            ship.Shield = 1d;

            Execute(session, GameCommandType.Repair);

            Assert.That(ship.Shield, Is.EqualTo(definition.HitPoints.Shield).Within(1e-9d),
                "Shield recharge is free and must never be blocked by armor debt.");
            Assert.That(ship.Armor, Is.EqualTo(definition.HitPoints.Armor * 0.5d).Within(1e-9d),
                "An unaffordable armor layer must not block or be confused with the hull layer.");
            Assert.That(ship.Hull, Is.EqualTo(definition.HitPoints.Hull * 0.5d).Within(1e-9d));
            Assert.That(session.State.Player.Credits, Is.EqualTo(1L));
        }

        [Test]
        public void ArmorRepairer_CyclesAutomaticallyUntilToggledOff()
        {
            var session = CreateSession();
            var ship = session.State.Player.ActiveShip();
            FitFirstFree(ship.Fitting.Low, ModuleIds.ArmorRepairer);
            Undock(session);
            NeutralizeNpcs(session);
            var entity = session.State.PlayerEntity();
            var index = entity.Modules.ToList().FindIndex(m => m.ModuleId == ModuleIds.ArmorRepairer);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            var repairPerCycle = catalog.Modules[ModuleIds.ArmorRepairer].RepairAmount;

            entity.Armor = 10d;
            Execute(session, GameCommandType.ActivateModule, index: index);
            Assert.That(entity.Modules[index].Active, Is.True,
                "Tap once to run: the booster must latch on instead of one-shotting.");

            session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(entity.Armor, Is.EqualTo(10d + repairPerCycle).Within(1e-9d),
                "The active repairer pulses on its own without re-tapping.");

            // Cycle time is 10s/70hp: topping 80 -> 280 needs three more pulses.
            for (var i = 0; i < 600; i++) session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(entity.Armor, Is.EqualTo(entity.MaxArmor).Within(1e-9d),
                "Left running, the repairer tops the layer off and then idles for free.");

            Execute(session, GameCommandType.ActivateModule, index: index);
            Assert.That(entity.Modules[index].Active, Is.False);
            entity.Armor = 10d;
            for (var i = 0; i < 100; i++) session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(entity.Armor, Is.EqualTo(10d).Within(1e-9d),
                "Toggled off the repairer stays quiet (armor has no passive regen).");
        }

        [Test]
        public void SellShip_StripsFittedModulesIntoTheHangar()
        {
            var session = CreateSession();
            var second = new ShipInstanceState
            {
                InstanceId = "ship_test_second",
                ShipId = ShipIds.Acolyte,
                Name = "Acolyte",
                Fitting = new FittingState
                {
                    High = new List<string> { ModuleIds.Blaster, null },
                    Low = new List<string> { ModuleIds.ArmorPlate, ModuleIds.ArmorPlate },
                },
            };
            session.State.Player.Ships.Add(second);
            var creditsBefore = session.State.Player.Credits;

            Execute(session, GameCommandType.SellShip, argument: second.InstanceId);

            Assert.That(session.State.Player.Ships, Has.Count.EqualTo(1));
            Assert.That(session.State.Player.Credits, Is.GreaterThan(creditsBefore));
            Assert.That(session.State.Player.Hangar.GetValueOrDefault(ModuleIds.Blaster), Is.EqualTo(1),
                "Fitted guns must come back to the hangar, not evaporate with the hull.");
            Assert.That(session.State.Player.Hangar.GetValueOrDefault(ModuleIds.ArmorPlate), Is.EqualTo(2));
        }

        private GameSession CreateSession()
        {
            return new GameSession(universe, catalog, "Combat QoL Pilot", FactionIds.Aurelian);
        }

        private static void FitFirstFree(List<string> slots, string moduleId)
        {
            var index = slots.IndexOf(null);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "The hull must have a free slot for the module.");
            slots[index] = moduleId;
        }

        private static void Execute(GameSession session, GameCommandType type, string argument = null, int index = 0)
        {
            session.Enqueue(new GameCommand(type, argument, index: index));
            session.AdvanceFrame(GameSession.FixedStepSeconds);
        }

        private static void Undock(GameSession session)
        {
            session.Enqueue(new GameCommand(GameCommandType.Undock));
            session.AdvanceFrame(GameSession.FixedStepSeconds);
            Assert.That(session.State.PlayerEntity(), Is.Not.Null);
        }

        /// <summary>Keeps stray NPC aggression from bleeding into tanking assertions.</summary>
        private static void NeutralizeNpcs(GameSession session)
        {
            foreach (var entity in session.State.Entities)
            {
                if (entity.Kind != EntityKind.Npc) continue;
                entity.AggroRange = 0d;
                entity.LockedTargetId = string.Empty;
                entity.Position = new SimVec2(-9000d, -9000d);
                for (var i = 0; i < entity.Modules.Count; i++) entity.Modules[i].Active = false;
            }
        }
    }
}
