using System;
using System.Linq;
using NUnit.Framework;
using Starfall.Content;
using Starfall.Domain;
using Starfall.Simulation;

namespace Starfall.Tests.EditMode.Gameplay
{
    public sealed class ContentCeilingTests
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
        public void AgentLevels_ReachDeeperIntoLawlessSpace()
        {
            foreach (var system in universe.OrderedSystems)
                foreach (var station in system.Stations)
                    foreach (var agent in station.Agents)
                    {
                        if (system.Region == SystemRegion.Empire)
                            Assert.That(agent.Level, Is.InRange(1, 2), system.Id + " empire agent");
                        else if (system.Region == SystemRegion.LowSecurity)
                            Assert.That(agent.Level, Is.InRange(3, 4), system.Id + " lowsec agent");
                        else if (system.Region == SystemRegion.NullSecurity)
                            Assert.That(agent.Level, Is.EqualTo(4), system.Id + " nullsec agent");
                    }

            var maxLevel = universe.OrderedSystems.SelectMany(value => value.Stations)
                .SelectMany(value => value.Agents).Max(value => value.Level);
            Assert.That(maxLevel, Is.GreaterThanOrEqualTo(4),
                "Progression must not dead-end at level 3 agents.");
        }

        [Test]
        public void UniverseLayout_IsUnchangedByTheLevelRemap()
        {
            // The remap reuses the legacy RNG draws; gates and names must be stable
            // for pre-existing saves on the same seed.
            var reference = new UniverseGenerator().Generate(12345);
            for (var i = 0; i < universe.OrderedSystems.Count; i++)
            {
                Assert.That(universe.OrderedSystems[i].Name, Is.EqualTo(reference.OrderedSystems[i].Name));
                CollectionAssert.AreEqual(
                    universe.OrderedSystems[i].Gates.Select(value => value.DestinationSystemId).OrderBy(id => id),
                    reference.OrderedSystems[i].Gates.Select(value => value.DestinationSystemId).OrderBy(id => id));
            }
        }

        [Test]
        public void Battlecruisers_BridgeTheCruiserToBattleshipPriceAndSkillGap()
        {
            var battlecruisers = catalog.Ships.Values.Where(value => value.Class == ShipClass.Battlecruiser).ToList();
            Assert.That(battlecruisers.Count, Is.EqualTo(4), "Every empire fields one battlecruiser.");
            foreach (var hull in battlecruisers)
            {
                Assert.That(hull.Price, Is.EqualTo(3_500_000L));
                Assert.That(hull.PowerGrid, Is.GreaterThan(catalog.Ships[ShipIds.Dawnbringer].PowerGrid));
                Assert.That(hull.PowerGrid, Is.LessThan(catalog.Ships[ShipIds.Seraph].PowerGrid));
                Assert.That(SkillRules.RequiredForShipClass(hull.Class), Is.EqualTo(4));
            }
            Assert.That(SkillRules.RequiredForShipClass(ShipClass.Battleship), Is.EqualTo(5));
            Assert.That(SkillRules.RequiredForShipClass(ShipClass.Frigate), Is.EqualTo(1));
        }

        [Test]
        public void Battlecruiser_UnlocksAtCommandFour_BattleshipNeedsFive()
        {
            var session = CreateSession();
            var player = session.State.Player;
            player.Credits = 50_000_000L;
            player.SkillLevels[SkillIds.SpaceshipCommand] = 3;

            Execute(session, new GameCommand(GameCommandType.Buy, ShipIds.Justicar));
            Assert.That(player.Ships.Any(value => value.ShipId == ShipIds.Justicar), Is.False,
                "A cruiser pilot cannot yet field a battlecruiser.");

            player.SkillLevels[SkillIds.SpaceshipCommand] = 4;
            Execute(session, new GameCommand(GameCommandType.Buy, ShipIds.Justicar));
            Assert.That(player.Ships.Any(value => value.ShipId == ShipIds.Justicar), Is.True);

            Execute(session, new GameCommand(GameCommandType.Buy, ShipIds.Seraph));
            Assert.That(player.Ships.Any(value => value.ShipId == ShipIds.Seraph), Is.False,
                "Battleships now require the full Spaceship Command V train.");
        }

        [Test]
        public void MediumWeapons_FitBattlecruisers()
        {
            var justicar = catalog.Ships[ShipIds.Justicar];
            var ship = new ShipInstanceState { ShipId = ShipIds.Justicar, Fitting = new FittingState() };
            ship.Fitting.High.Add(null);
            Assert.That(FittingRules.SizeAllowedForClass(ModuleSize.Medium, ShipClass.Battlecruiser), Is.True);
            Assert.That(FittingRules.SizeAllowedForClass(ModuleSize.Large, ShipClass.Battlecruiser), Is.False);
            Assert.That(FittingRules.CanFitModule(catalog, ship, "high", 0, ModuleIds.HeavyLaser, null,
                out var reason, out _), Is.True, reason + " on grid " + justicar.PowerGrid);
        }

        [Test]
        public void LoyaltyStore_StocksMoreThanOneOffer()
        {
            var offers = GameSession.LoyaltyOffers;
            Assert.That(offers.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(offers[0].ModuleId, Is.EqualTo(ModuleIds.DamageAmp),
                "The classic button keeps exchanging the default offer.");
            Assert.That(offers.Sum(offer => offer.Cost), Is.GreaterThan(0));

            var session = CreateSession();
            var player = session.State.Player;

            var heavy = offers.First(offer => offer.ModuleId == ModuleIds.HeavyLaser);
            player.LoyaltyPoints[player.EmpireId] = heavy.Cost - 1;
            var rejected = Execute(session, new GameCommand(GameCommandType.ExchangeLoyalty, ModuleIds.HeavyLaser));
            Assert.That(player.Hangar.ContainsKey(ModuleIds.HeavyLaser), Is.False);
            Assert.That(rejected.Any(value => value.Type == SimulationEventType.Log &&
                value.Message.Contains(heavy.Cost.ToString("N0"))), Is.True,
                "The rejection must name the actual LP price.");

            player.LoyaltyPoints[player.EmpireId] = heavy.Cost;
            Execute(session, new GameCommand(GameCommandType.ExchangeLoyalty, ModuleIds.HeavyLaser));
            Assert.That(player.Hangar[ModuleIds.HeavyLaser], Is.EqualTo(1));
        }

        [Test]
        public void StorylineRewards_NoLongerCliffAboveRegularMissions()
        {
            var session = CreateSession();
            session.State.Player.MissionCounts[FactionIds.Aurelian] = 4;
            var regular = new MissionState
            {
                Id = "mis_regular", Type = MissionType.Mining, Status = MissionStatus.ObjectivesMet,
                Title = "Regular", FactionId = FactionIds.Aurelian, RewardCredits = 1L,
                RewardLoyaltyPoints = 1, RewardStanding = 0.1d,
            };
            session.State.Player.Missions.Add(regular);

            Execute(session, new GameCommand(GameCommandType.CompleteMission, regular.Id));
            var storyline = session.State.Player.Missions.Single(value =>
                value.Type == MissionType.StorylineKill || value.Type == MissionType.StorylineHaul);

            Assert.That(storyline.RewardCredits, Is.LessThanOrEqualTo(600_000L),
                "Storylines stay a premium, not a 13x cliff.");
            Assert.That(storyline.RewardStanding, Is.EqualTo(1.0d));
        }

        [Test]
        public void LevelFourSecurityMissions_FieldBattlecruiserSquads()
        {
            var session = CreateSession();
            var player = session.State.Player;
            var station = universe.Systems[player.CurrentSystemId].Stations[0];
            var agent = new AgentDefinition("agent_l4", "Deep Space Agent", "security", 4, station.Id);
            station.Agents.Add(agent);

            Execute(session, new GameCommand(GameCommandType.TalkToAgent, agent.Id));
            var mission = player.Missions.Single(value => value.AgentId == agent.Id);
            Execute(session, new GameCommand(GameCommandType.AcceptMission, mission.Id));

            // Security missions send the player elsewhere; teleport acceptance is
            // enough to confirm the offer itself is level 4.
            Assert.That(mission.Level, Is.EqualTo(4));
            Assert.That(mission.KillsRequired, Is.GreaterThanOrEqualTo(5));
        }

        private GameSession CreateSession()
        {
            return new GameSession(universe, catalog, "Ceiling Pilot", FactionIds.Aurelian);
        }

        private static SimulationEventBatch Execute(GameSession session, params GameCommand[] commands)
        {
            foreach (var command in commands) session.Enqueue(command);
            return session.AdvanceFrame(GameSession.FixedStepSeconds);
        }
    }
}
