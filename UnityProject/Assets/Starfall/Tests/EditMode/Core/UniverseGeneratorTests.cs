using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Starfall.Domain;
using Starfall.Simulation;

namespace Starfall.Tests.EditMode.Core
{
    public sealed class UniverseGeneratorTests
    {
        private GeneratedUniverse _universe;

        [SetUp]
        public void SetUp() => _universe = new UniverseGenerator().Generate(12345);

        [Test]
        public void Seed12345_MatchesLegacyGoldenTopology()
        {
            Assert.That(_universe.Seed, Is.EqualTo(12345));
            Assert.That(_universe.GeneratorVersion, Is.EqualTo(1));
            Assert.That(_universe.Systems.Count, Is.EqualTo(48));
            Assert.That(_universe.OrderedSystems.Count(x => x.Region == SystemRegion.Empire), Is.EqualTo(28));
            Assert.That(_universe.OrderedSystems.Count(x => x.Region == SystemRegion.LowSecurity), Is.EqualTo(10));
            Assert.That(_universe.OrderedSystems.Count(x => x.Region == SystemRegion.NullSecurity), Is.EqualTo(8));
            Assert.That(_universe.OrderedSystems.Count(x => x.Region == SystemRegion.Sisters), Is.EqualTo(2));
            Assert.That(_universe.OrderedSystems.Sum(x => x.Stations.Count), Is.EqualTo(68));
            Assert.That(_universe.OrderedSystems.Sum(x => x.Stations.Sum(y => y.Agents.Count)), Is.EqualTo(142));
            Assert.That(_universe.OrderedSystems.Sum(x => x.Planets.Count), Is.EqualTo(216));
            Assert.That(_universe.OrderedSystems.Sum(x => x.Planets.Sum(y => y.Moons.Count)), Is.EqualTo(221));
            Assert.That(_universe.OrderedSystems.Sum(x => x.Belts.Count), Is.EqualTo(107));
            Assert.That(_universe.Adjacency.Sum(x => x.Value.Count) / 2, Is.EqualTo(83));
        }

        [Test]
        public void Seed12345_MatchesLegacySystemOrderAndNames()
        {
            var expected = new[]
            {
                "Ostmir", "Yorir", "Ostune", "Torir IV", "Belys", "Yorara", "Peroth II",
                "Elara Major", "Kaur", "Elon Major", "Wynur Major", "Vexys", "Ostax IV", "Moros",
                "Rynys II", "Wynax VI", "Rynune", "Zelir", "Nymmir V", "Ulara", "Ulos",
                "Ulax Prime", "Rynion", "Cynur", "Elion", "Kaea", "Corys Prime", "Nymea Minor",
                "Belael II", "Lyros", "Torys", "Helos", "Vexeth Minor", "Cyneth", "Zelion",
                "Rynos Minor", "Lyrur", "Belis Prime", "Ulael", "Zelara", "Belon Prime", "Jorara",
                "Jorys", "Wynea Major", "Ashos", "Yorael", "Fenos", "Wyneth",
            };
            CollectionAssert.AreEqual(expected, _universe.OrderedSystems.Select(x => x.Name).ToArray());
            Assert.That(_universe.StartSystems[FactionIds.Aurelian], Is.EqualTo("sys_0"));
            Assert.That(_universe.StartSystems[FactionIds.Kaldari], Is.EqualTo("sys_7"));
            Assert.That(_universe.StartSystems[FactionIds.Meridian], Is.EqualTo("sys_14"));
            Assert.That(_universe.StartSystems[FactionIds.Varkhald], Is.EqualTo("sys_21"));
        }

        [Test]
        public void Seed12345_FirstSystemContentMatchesLegacyGolden()
        {
            var system = _universe.Systems["sys_0"];
            Assert.That(system.Name, Is.EqualTo("Ostmir"));
            Assert.That(system.MapPosition.X, Is.EqualTo(354.7293496178463));
            Assert.That(system.MapPosition.Z, Is.EqualTo(179.75558876991272));
            Assert.That(system.Security, Is.EqualTo(1));
            Assert.That(system.IsCapital, Is.True);
            Assert.That(system.Star.SpectralType, Is.EqualTo("A"));
            Assert.That(system.Planets.Count, Is.EqualTo(4));
            Assert.That(system.Belts.Count, Is.EqualTo(3));
            Assert.That(system.Stations.Count, Is.EqualTo(4));
            Assert.That(system.Gates.Count, Is.EqualTo(3));
            Assert.That(system.Planets[0].PlanetType, Is.EqualTo("lava"));
            Assert.That(system.Planets[0].Moons.Count, Is.EqualTo(2));
            Assert.That(system.Belts[0].OreId, Is.EqualTo(ItemIds.Ferrite));
            Assert.That(system.Belts[0].AsteroidCount, Is.EqualTo(19));
            Assert.That(system.Stations[0].Name, Is.EqualTo("Ostmir AUR Station 1"));
            Assert.That(system.Stations[0].Agents[0].Name, Is.EqualTo("Petra Haldane"));
            Assert.That(system.Gates[0].DestinationSystemId, Is.EqualTo("sys_4"));
            Assert.That(system.Gates[2].DestinationSystemId, Is.EqualTo("sys_35"));
        }

        [Test]
        public void Graph_IsConnectedSymmetricAndRepresentedByBidirectionalGates()
        {
            var reached = new HashSet<string> { "sys_0" };
            var queue = new Queue<string>();
            queue.Enqueue("sys_0");
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in _universe.Adjacency[current])
                {
                    Assert.That(_universe.Adjacency[neighbor], Does.Contain(current));
                    Assert.That(_universe.Systems[current].Gates.Any(x => x.DestinationSystemId == neighbor), Is.True);
                    Assert.That(_universe.Systems[neighbor].Gates.Any(x => x.DestinationSystemId == current), Is.True);
                    if (reached.Add(neighbor)) queue.Enqueue(neighbor);
                }
            }
            Assert.That(reached.Count, Is.EqualTo(48));
            CollectionAssert.AreEqual(new[] { "sys_0", "sys_35", "sys_47", "sys_28", "sys_29", "sys_21" },
                UniverseRoutes.FindRoute(_universe, "sys_0", "sys_21"));
        }

        [Test]
        public void Generator_IsRepeatableWithoutSharedMutableRngState()
        {
            var second = new UniverseGenerator().Generate(12345);
            CollectionAssert.AreEqual(_universe.OrderedSystems.Select(x => x.Name), second.OrderedSystems.Select(x => x.Name));
            CollectionAssert.AreEqual(_universe.Adjacency["sys_0"], second.Adjacency["sys_0"]);
            Assert.That(second.Systems["sys_0"].Planets[0].Position, Is.EqualTo(_universe.Systems["sys_0"].Planets[0].Position));
        }
    }
}
