using System.Collections.Generic;

namespace Starfall.Domain
{
    public enum SystemRegion
    {
        Empire,
        LowSecurity,
        NullSecurity,
        Sisters,
    }

    public sealed class StarDefinition
    {
        public StarDefinition(string spectralType, string color, double radius)
        {
            SpectralType = spectralType;
            Color = color;
            Radius = radius;
        }

        public string SpectralType { get; }
        public string Color { get; }
        public double Radius { get; }
    }

    public sealed class MoonDefinition
    {
        public MoonDefinition(string id, string name, SimVec2 position, double radius)
        {
            Id = id;
            Name = name;
            Position = position;
            Radius = radius;
        }

        public string Id { get; }
        public string Name { get; }
        public SimVec2 Position { get; }
        public double Radius { get; }
    }

    public sealed class PlanetDefinition
    {
        public PlanetDefinition(string id, string name, string planetType, string color, SimVec2 position, double radius)
        {
            Id = id;
            Name = name;
            PlanetType = planetType;
            Color = color;
            Position = position;
            Radius = radius;
            Moons = new List<MoonDefinition>();
        }

        public string Id { get; }
        public string Name { get; }
        public string PlanetType { get; }
        public string Color { get; }
        public SimVec2 Position { get; }
        public double Radius { get; }
        public List<MoonDefinition> Moons { get; }
    }

    public sealed class AsteroidBeltDefinition
    {
        public AsteroidBeltDefinition(string id, string name, SimVec2 position, string oreId, int asteroidCount)
        {
            Id = id;
            Name = name;
            Position = position;
            OreId = oreId;
            AsteroidCount = asteroidCount;
        }

        public string Id { get; }
        public string Name { get; }
        public SimVec2 Position { get; }
        public string OreId { get; }
        public int AsteroidCount { get; }
    }

    public sealed class AgentDefinition
    {
        public AgentDefinition(string id, string name, string division, int level, string stationId)
        {
            Id = id;
            Name = name;
            Division = division;
            Level = level;
            StationId = stationId;
        }

        public string Id { get; }
        public string Name { get; }
        public string Division { get; }
        public int Level { get; set; }
        public string StationId { get; }
    }

    public sealed class StationDefinition
    {
        public StationDefinition(string id, string name, string factionId, SimVec2 position)
        {
            Id = id;
            Name = name;
            FactionId = factionId;
            Position = position;
            Agents = new List<AgentDefinition>();
        }

        public string Id { get; }
        public string Name { get; }
        public string FactionId { get; }
        public SimVec2 Position { get; }
        public List<AgentDefinition> Agents { get; }
    }

    public sealed class GateDefinition
    {
        public GateDefinition(string id, string name, string destinationSystemId, SimVec2 position)
        {
            Id = id;
            Name = name;
            DestinationSystemId = destinationSystemId;
            Position = position;
        }

        public string Id { get; }
        public string Name { get; }
        public string DestinationSystemId { get; }
        public SimVec2 Position { get; }
    }

    public sealed class StarSystemDefinition
    {
        public StarSystemDefinition(string id, string name, SimVec2 mapPosition, double security,
            string factionId, SystemRegion region, StarDefinition star)
        {
            Id = id;
            Name = name;
            MapPosition = mapPosition;
            Security = security;
            FactionId = factionId;
            Region = region;
            Star = star;
            Planets = new List<PlanetDefinition>();
            Belts = new List<AsteroidBeltDefinition>();
            Stations = new List<StationDefinition>();
            Gates = new List<GateDefinition>();
        }

        public string Id { get; }
        public string Name { get; }
        public SimVec2 MapPosition { get; }
        public double Security { get; }
        public string FactionId { get; }
        public SystemRegion Region { get; }
        public StarDefinition Star { get; }
        public bool IsCapital { get; set; }
        public List<PlanetDefinition> Planets { get; }
        public List<AsteroidBeltDefinition> Belts { get; }
        public List<StationDefinition> Stations { get; }
        public List<GateDefinition> Gates { get; }
    }

    public sealed class GeneratedUniverse
    {
        public GeneratedUniverse(uint seed, IReadOnlyList<StarSystemDefinition> orderedSystems,
            IReadOnlyDictionary<string, StarSystemDefinition> systems,
            IReadOnlyDictionary<string, IReadOnlyList<string>> adjacency,
            IReadOnlyDictionary<string, string> startSystems)
        {
            Seed = seed;
            OrderedSystems = orderedSystems;
            Systems = systems;
            Adjacency = adjacency;
            StartSystems = startSystems;
        }

        public uint Seed { get; }
        public int GeneratorVersion => 1;
        public IReadOnlyList<StarSystemDefinition> OrderedSystems { get; }
        public IReadOnlyDictionary<string, StarSystemDefinition> Systems { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Adjacency { get; }
        public IReadOnlyDictionary<string, string> StartSystems { get; }
    }

    public interface IUniverseGenerator
    {
        GeneratedUniverse Generate(uint seed);
    }
}
