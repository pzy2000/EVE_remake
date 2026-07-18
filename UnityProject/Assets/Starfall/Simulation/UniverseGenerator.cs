using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Starfall.Domain;

namespace Starfall.Simulation
{
    /// <summary>Compatibility port of js/data/universe.js.</summary>
    public sealed class UniverseGenerator : IUniverseGenerator
    {
        private static readonly string[] SyllableA =
        {
            "Al", "Bel", "Cor", "Dur", "El", "Fen", "Gal", "Hel", "Ith", "Jor", "Ka", "Lyr", "Mor", "Nym",
            "Ost", "Per", "Qua", "Ryn", "Sel", "Tor", "Ul", "Vex", "Wyn", "Xan", "Yor", "Zel", "Ash", "Bra", "Cyn", "Dra",
        };

        private static readonly string[] SyllableB =
        {
            "ara", "eth", "ion", "os", "une", "ax", "ir", "on", "ea", "ys", "oth", "ael", "mir", "is", "ur",
        };

        private static readonly string[] FirstNames =
        {
            "Aren", "Bela", "Corin", "Dara", "Elias", "Freya", "Goran", "Hana", "Ivan", "Jora", "Kell", "Lena", "Marek",
            "Nadia", "Orin", "Petra", "Quill", "Rosa", "Sten", "Talia", "Ulric", "Vera", "Wren", "Xavier", "Yara", "Zane",
        };

        private static readonly string[] LastNames =
        {
            "Voss", "Kaine", "Ardath", "Belmore", "Castellan", "Draven", "Erland", "Falk", "Greer", "Haldane", "Ivar", "Jorund",
            "Korr", "Lindqvist", "Moreau", "Nyx", "Okafor", "Pryce", "Quade", "Reyes", "Sorren", "Thane", "Umar", "Valen", "Ward", "Yilmaz",
        };

        private static readonly string[] NameSuffixes = { "II", "III", "IV", "V", "VI", "Prime", "Major", "Minor" };
        private static readonly string[] RomanPlanets = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };
        private static readonly string[] BeltSuffixes = { "Alpha", "Beta", "Gamma", "Delta" };
        private static readonly string[] AgentDivisions = { "security", "distribution", "mining" };

        private static readonly StarDefinition[] StarTypes =
        {
            new StarDefinition("O", "#9db4ff", 90), new StarDefinition("B", "#aabfff", 80),
            new StarDefinition("A", "#cad8ff", 70), new StarDefinition("F", "#f8f7ff", 62),
            new StarDefinition("G", "#fff4e0", 58), new StarDefinition("K", "#ffd2a1", 52),
            new StarDefinition("M", "#ffab8a", 44),
        };

        private static readonly PlanetType[] PlanetTypes =
        {
            new PlanetType("temperate", "#4a90d9"), new PlanetType("barren", "#a08a6a"),
            new PlanetType("gas giant", "#d9b06a"), new PlanetType("ice", "#bfe8ff"),
            new PlanetType("lava", "#ff6a3a"), new PlanetType("oceanic", "#3a6ad9"),
            new PlanetType("toxic", "#8ad93a"),
        };

        private static readonly IReadOnlyDictionary<string, string> FactionAbbreviations =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
            {
                { FactionIds.Aurelian, "AUR" }, { FactionIds.Kaldari, "KAL" },
                { FactionIds.Meridian, "MER" }, { FactionIds.Varkhald, "VAR" },
                { FactionIds.BloodReavers, "BLD" }, { FactionIds.Nathari, "NAT" },
                { FactionIds.CrimsonHand, "CRI" }, { FactionIds.Ashfang, "ASH" },
                { FactionIds.Sisters, "SIS" }, { FactionIds.Directorate, "DIR" },
            });

        public GeneratedUniverse Generate(uint seed)
        {
            var rng = new Mulberry32(seed);
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            var systems = new Dictionary<string, StarSystemDefinition>(StringComparer.Ordinal);
            var systemOrder = new List<StarSystemDefinition>();
            var systemCounter = 0;

            StarSystemDefinition NewSystem(string name, double mapX, double mapZ, double security,
                string factionId, SystemRegion region)
            {
                var id = "sys_" + systemCounter++;
                var pickedStar = rng.Pick(StarTypes);
                var system = new StarSystemDefinition(id, name, new SimVec2(mapX, mapZ), security,
                    factionId, region, new StarDefinition(pickedStar.SpectralType, pickedStar.Color, pickedStar.Radius));
                systems.Add(id, system);
                systemOrder.Add(system);
                return system;
            }

            var empireCenters = new Dictionary<string, SimVec2>
            {
                { FactionIds.Aurelian, new SimVec2(230, 230) },
                { FactionIds.Kaldari, new SimVec2(770, 230) },
                { FactionIds.Meridian, new SimVec2(230, 770) },
                { FactionIds.Varkhald, new SimVec2(770, 770) },
            };
            var clusters = new List<Cluster>();
            var startSystems = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var empireId in FactionIds.Empires)
            {
                var center = empireCenters[empireId];
                var ids = new List<string>();
                for (var i = 0; i < 7; i++)
                {
                    var mapX = 0d;
                    var mapZ = 0d;
                    var accepted = false;
                    for (var attempt = 0; attempt < 40 && !accepted; attempt++)
                    {
                        mapX = center.X + rng.Range(-130, 130);
                        mapZ = center.Z + rng.Range(-130, 130);
                        accepted = ids.All(id => SimVec2.Distance(systems[id].MapPosition, new SimVec2(mapX, mapZ)) > 55);
                    }

                    var isCapital = i == 0;
                    var rawSecurity = isCapital ? 1d : rng.Range(0.5, 0.9);
                    var system = NewSystem(MakeName(rng, usedNames), mapX, mapZ,
                        JsMath.Round(rawSecurity * 10d) / 10d, empireId, SystemRegion.Empire);
                    if (isCapital)
                    {
                        system.IsCapital = true;
                        startSystems.Add(empireId, system.Id);
                    }
                    ids.Add(system.Id);
                }
                clusters.Add(new Cluster(ids, ClusterKind.Empire));
            }

            var lowSecurityIds = new List<string>();
            for (var i = 0; i < 10; i++)
            {
                var angle = i / 10d * Math.PI * 2d + rng.Range(-0.2, 0.2);
                var radius = rng.Range(130, 195);
                var mapX = 500 + Math.Cos(angle) * radius;
                var mapZ = 500 + Math.Sin(angle) * radius;
                var owner = rng.Pick(FactionIds.Empires);
                var system = NewSystem(MakeName(rng, usedNames), mapX, mapZ,
                    JsMath.Round(rng.Range(0.1, 0.4) * 10d) / 10d, owner, SystemRegion.LowSecurity);
                lowSecurityIds.Add(system.Id);
            }
            clusters.Add(new Cluster(lowSecurityIds, ClusterKind.LowSecurity));

            var nullClusters = new[]
            {
                new NullCluster(FactionIds.BloodReavers, 90, 500),
                new NullCluster(FactionIds.Nathari, 500, 90),
                new NullCluster(FactionIds.CrimsonHand, 500, 910),
                new NullCluster(FactionIds.Ashfang, 910, 500),
            };
            foreach (var nullCluster in nullClusters)
            {
                var ids = new List<string>();
                for (var i = 0; i < 2; i++)
                {
                    var mapX = nullCluster.Center.X + rng.Range(-70, 70);
                    var mapZ = nullCluster.Center.Z + rng.Range(-70, 70);
                    ids.Add(NewSystem(MakeName(rng, usedNames), mapX, mapZ, 0d,
                        nullCluster.FactionId, SystemRegion.NullSecurity).Id);
                }
                clusters.Add(new Cluster(ids, ClusterKind.NullSecurity));
            }

            var sistersIds = new List<string>();
            for (var i = 0; i < 2; i++)
            {
                var mapX = 500 + rng.Range(-70, 70);
                var mapZ = 500 + rng.Range(-70, 70);
                sistersIds.Add(NewSystem(MakeName(rng, usedNames), mapX, mapZ,
                    0.5, FactionIds.Sisters, SystemRegion.Sisters).Id);
            }
            clusters.Add(new Cluster(sistersIds, ClusterKind.Sisters));

            var adjacency = systemOrder.ToDictionary(x => x.Id, _ => new OrderedSet<string>(), StringComparer.Ordinal);
            void Link(string first, string second)
            {
                if (first == second) return;
                adjacency[first].Add(second);
                adjacency[second].Add(first);
            }

            List<string> Nearest(string id, IEnumerable<string> pool, int count)
            {
                return pool.Where(candidate => candidate != id)
                    .OrderBy(candidate => SimVec2.Distance(systems[candidate].MapPosition, systems[id].MapPosition))
                    .Take(count)
                    .ToList();
            }

            foreach (var cluster in clusters)
            {
                foreach (var id in cluster.SystemIds)
                {
                    foreach (var nearby in Nearest(id, cluster.SystemIds, 2)) Link(id, nearby);
                }
            }

            var ring = lowSecurityIds
                .OrderBy(id => Math.Atan2(systems[id].MapPosition.Z - 500, systems[id].MapPosition.X - 500))
                .ToList();
            for (var i = 0; i < ring.Count; i++) Link(ring[i], ring[(i + 1) % ring.Count]);

            foreach (var cluster in clusters.Where(x => x.Kind == ClusterKind.Empire))
            {
                var border = cluster.SystemIds
                    .OrderBy(id => SimVec2.Distance(systems[id].MapPosition, new SimVec2(500, 500)))
                    .Take(3);
                foreach (var borderId in border)
                {
                    foreach (var lowId in Nearest(borderId, lowSecurityIds, 1)) Link(borderId, lowId);
                }
            }

            foreach (var cluster in clusters.Where(x => x.Kind == ClusterKind.NullSecurity || x.Kind == ClusterKind.Sisters))
            {
                foreach (var id in cluster.SystemIds)
                {
                    foreach (var lowId in Nearest(id, lowSecurityIds, 2)) Link(id, lowId);
                }
            }

            EnsureConnectivity(systemOrder, systems, adjacency, Link);
            GenerateSystemContent(seed, systemCounter, systemOrder, systems, adjacency);

            var adjacencyResult = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var system in systemOrder)
            {
                adjacencyResult.Add(system.Id, new ReadOnlyCollection<string>(adjacency[system.Id].Items));
            }

            return new GeneratedUniverse(seed,
                new ReadOnlyCollection<StarSystemDefinition>(systemOrder),
                new ReadOnlyDictionary<string, StarSystemDefinition>(systems),
                new ReadOnlyDictionary<string, IReadOnlyList<string>>(adjacencyResult),
                new ReadOnlyDictionary<string, string>(startSystems));
        }

        private static void GenerateSystemContent(uint seed, int systemCounter,
            IReadOnlyList<StarSystemDefinition> systemOrder,
            IReadOnlyDictionary<string, StarSystemDefinition> systems,
            IReadOnlyDictionary<string, OrderedSet<string>> adjacency)
        {
            foreach (var system in systemOrder)
            {
                var seedMix = unchecked(system.Id.Length * 7919 + systemCounter + system.Name.Length * 131 + system.Id[4] * 31337);
                var rng = new Mulberry32(seed ^ unchecked((uint)seedMix));

                var planetCount = rng.RangeInclusive(2, 7);
                for (var i = 0; i < planetCount; i++)
                {
                    var angle = rng.Range(0, Math.PI * 2d);
                    var radius = 700 + i * rng.Range(320, 420) + rng.Range(0, 150);
                    var type = rng.Pick(PlanetTypes);
                    var planet = new PlanetDefinition(
                        $"{system.Id}_p{i}", $"{system.Name} {RomanPlanets[i]}", type.Name, type.Color,
                        new SimVec2(Math.Cos(angle) * radius, Math.Sin(angle) * radius),
                        rng.Range(26, type.Name == "gas giant" ? 60 : 42));

                    var moonCount = rng.RangeInclusive(0, 2);
                    for (var moonIndex = 0; moonIndex < moonCount; moonIndex++)
                    {
                        var moonAngle = rng.Range(0, Math.PI * 2d);
                        planet.Moons.Add(new MoonDefinition(
                            $"{planet.Id}_m{moonIndex}", $"{planet.Name} - Moon {moonIndex + 1}",
                            new SimVec2(planet.Position.X + Math.Cos(moonAngle) * rng.Range(70, 110),
                                planet.Position.Z + Math.Sin(moonAngle) * rng.Range(70, 110)),
                            rng.Range(8, 14)));
                    }
                    system.Planets.Add(planet);
                }

                var beltCount = rng.RangeInclusive(1, 4);
                for (var i = 0; i < beltCount; i++)
                {
                    var angle = rng.Range(0, Math.PI * 2d);
                    var radius = rng.Range(900, 3200);
                    var oreId = ItemIds.Ferrite;
                    if (system.Security <= 0d) oreId = rng.Chance(0.6) ? ItemIds.Crystalline : ItemIds.Novacite;
                    else if (system.Security < 0.5) oreId = rng.Chance(0.7) ? ItemIds.Novacite : ItemIds.Ferrite;
                    else oreId = rng.Chance(0.8) ? ItemIds.Ferrite : ItemIds.Novacite;
                    system.Belts.Add(new AsteroidBeltDefinition(
                        $"{system.Id}_b{i}", $"{system.Name} Belt {BeltSuffixes[i]}",
                        new SimVec2(Math.Cos(angle) * radius, Math.Sin(angle) * radius), oreId,
                        rng.RangeInclusive(10, 20)));
                }

                StationDefinition MakeStation(string factionId, string label)
                {
                    var angle = rng.Range(0, Math.PI * 2d);
                    var radius = rng.Range(800, 2600);
                    var station = new StationDefinition(
                        $"{system.Id}_st{system.Stations.Count}", $"{system.Name} {label}", factionId,
                        new SimVec2(Math.Cos(angle) * radius, Math.Sin(angle) * radius));
                    var shuffledDivisions = rng.Shuffle(AgentDivisions);
                    var divisionCount = rng.RangeInclusive(1, 3);
                    var level = system.Security >= 0.5
                        ? rng.RangeInclusive(1, 2)
                        : system.Security > 0 ? rng.RangeInclusive(2, 3) : 3;
                    for (var i = 0; i < divisionCount; i++)
                    {
                        var division = shuffledDivisions[i];
                        station.Agents.Add(new AgentDefinition(
                            $"agent_{station.Id}_{division}", $"{rng.Pick(FirstNames)} {rng.Pick(LastNames)}",
                            division, level, station.Id));
                    }
                    if (system.IsCapital && station.Agents.Count > 0) station.Agents[0].Level = 1;
                    system.Stations.Add(station);
                    return station;
                }

                if (system.Region == SystemRegion.Empire)
                {
                    var count = system.IsCapital ? 3 : rng.RangeInclusive(1, 2);
                    for (var i = 0; i < count; i++)
                    {
                        MakeStation(system.FactionId, $"{FactionAbbreviations[system.FactionId]} Station {i + 1}");
                    }
                    if (system.IsCapital) MakeStation(FactionIds.Directorate, "Directorate Bureau");
                }
                else if (system.Region == SystemRegion.LowSecurity)
                {
                    if (rng.Chance(0.7)) MakeStation(system.FactionId, "Outpost");
                }
                else if (system.Region == SystemRegion.NullSecurity)
                {
                    MakeStation(system.FactionId, "Pirate Haven");
                }
                else
                {
                    MakeStation(FactionIds.Sisters, "Sanctuary");
                    if (rng.Chance(0.5)) MakeStation(FactionIds.Sisters, "Refuge");
                }

                for (var i = 0; i < adjacency[system.Id].Items.Count; i++)
                {
                    var destinationId = adjacency[system.Id].Items[i];
                    var destination = systems[destinationId];
                    var angle = Math.Atan2(destination.MapPosition.Z - system.MapPosition.Z,
                        destination.MapPosition.X - system.MapPosition.X) + rng.Range(-0.15, 0.15);
                    var radius = rng.Range(2400, 3400);
                    system.Gates.Add(new GateDefinition(
                        $"{system.Id}_g{i}", $"Stargate to {destination.Name}", destinationId,
                        new SimVec2(Math.Cos(angle) * radius, Math.Sin(angle) * radius)));
                }
            }
        }

        private static void EnsureConnectivity(IReadOnlyList<StarSystemDefinition> systemOrder,
            IReadOnlyDictionary<string, StarSystemDefinition> systems,
            IReadOnlyDictionary<string, OrderedSet<string>> adjacency,
            Action<string, string> link)
        {
            var parent = systemOrder.ToDictionary(x => x.Id, x => x.Id, StringComparer.Ordinal);
            string Find(string id)
            {
                if (parent[id] == id) return id;
                parent[id] = Find(parent[id]);
                return parent[id];
            }

            foreach (var system in systemOrder)
            {
                foreach (var neighbor in adjacency[system.Id].Items)
                {
                    parent[Find(system.Id)] = Find(neighbor);
                }
            }

            var roots = systemOrder.Select(x => Find(x.Id)).Distinct(StringComparer.Ordinal).ToList();
            while (roots.Count > 1)
            {
                string bestFirst = null;
                string bestSecond = null;
                var bestDistance = double.PositiveInfinity;
                for (var i = 0; i < roots.Count; i++)
                {
                    for (var j = i + 1; j < roots.Count; j++)
                    {
                        foreach (var first in systemOrder.Where(x => Find(x.Id) == roots[i]))
                        {
                            foreach (var second in systemOrder.Where(x => Find(x.Id) == roots[j]))
                            {
                                var distance = SimVec2.Distance(first.MapPosition, second.MapPosition);
                                if (distance < bestDistance)
                                {
                                    bestDistance = distance;
                                    bestFirst = first.Id;
                                    bestSecond = second.Id;
                                }
                            }
                        }
                    }
                }

                link(bestFirst, bestSecond);
                parent[Find(bestFirst)] = Find(bestSecond);
                roots = systemOrder.Select(x => Find(x.Id)).Distinct(StringComparer.Ordinal).ToList();
            }
        }

        private static string MakeName(Mulberry32 rng, ISet<string> usedNames)
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var name = rng.Pick(SyllableA) + rng.Pick(SyllableB);
                if (rng.Chance(0.35)) name += " " + rng.Pick(NameSuffixes);
                if (usedNames.Add(name)) return name;
            }

            var fallback = rng.Pick(SyllableA) + rng.Pick(SyllableB) + "-" + rng.RangeInclusive(10, 99);
            usedNames.Add(fallback);
            return fallback;
        }

        private sealed class PlanetType
        {
            public PlanetType(string name, string color)
            {
                Name = name;
                Color = color;
            }

            public string Name { get; }
            public string Color { get; }
        }

        private sealed class Cluster
        {
            public Cluster(List<string> systemIds, ClusterKind kind)
            {
                SystemIds = systemIds;
                Kind = kind;
            }

            public List<string> SystemIds { get; }
            public ClusterKind Kind { get; }
        }

        private enum ClusterKind { Empire, LowSecurity, NullSecurity, Sisters }

        private sealed class NullCluster
        {
            public NullCluster(string factionId, double centerX, double centerZ)
            {
                FactionId = factionId;
                Center = new SimVec2(centerX, centerZ);
            }

            public string FactionId { get; }
            public SimVec2 Center { get; }
        }

        private sealed class OrderedSet<T>
        {
            private readonly HashSet<T> _set = new HashSet<T>();
            public List<T> Items { get; } = new List<T>();

            public void Add(T value)
            {
                if (_set.Add(value)) Items.Add(value);
            }
        }
    }
}
