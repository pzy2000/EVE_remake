using System;
using System.Collections.Generic;
using Starfall.Domain;

namespace Starfall.Simulation
{
    [Serializable]
    public sealed class FittingState
    {
        public List<string> High = new List<string>();
        public List<string> Mid = new List<string>();
        public List<string> Low = new List<string>();

        public IEnumerable<string> All()
        {
            foreach (var id in High) if (!string.IsNullOrEmpty(id)) yield return id;
            foreach (var id in Mid) if (!string.IsNullOrEmpty(id)) yield return id;
            foreach (var id in Low) if (!string.IsNullOrEmpty(id)) yield return id;
        }
    }

    [Serializable]
    public sealed class ShipInstanceState
    {
        public string InstanceId = string.Empty;
        public string ShipId = string.Empty;
        public string Name = string.Empty;
        public FittingState Fitting = new FittingState();
        public double Shield;
        public double Armor;
        public double Hull;
    }

    [Serializable]
    public sealed class PlayerStatsState
    {
        public int Kills;
        public int MissionsDone;
        public double OreMined;
        public int Jumps;
    }

    public enum MissionType
    {
        Security,
        Distribution,
        Mining,
        StorylineKill,
        StorylineHaul,
    }

    public enum MissionStatus
    {
        Offered,
        Active,
        ObjectivesMet,
        Done,
    }

    [Serializable]
    public sealed class MissionState
    {
        public string Id = string.Empty;
        public MissionType Type;
        public MissionStatus Status;
        public string Title = string.Empty;
        public string Description = string.Empty;
        public string FactionId = string.Empty;
        public string AgentId = string.Empty;
        public string AgentName = string.Empty;
        public string StationId = string.Empty;
        public string TargetSystemId = string.Empty;
        public string DestinationSystemId = string.Empty;
        public string DestinationStationId = string.Empty;
        public string TargetFactionId = string.Empty;
        public string OreId = string.Empty;
        public int Level = 1;
        public int Kills;
        public int KillsRequired;
        public double Quantity;
        public long RewardCredits;
        public int RewardLoyaltyPoints;
        public double RewardStanding;
        public bool AmbushSpawned;

        public string ProgressText()
        {
            switch (Type)
            {
                case MissionType.Security:
                case MissionType.StorylineKill:
                    return L10n.Tr("Hostiles destroyed: {0}/{1}", Kills, KillsRequired);
                case MissionType.Distribution:
                case MissionType.StorylineHaul:
                    return L10n.Tr("Deliver the sealed cargo to the destination station");
                case MissionType.Mining:
                    return L10n.Tr("Deliver {0} units of ore to the agent", Quantity.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                default:
                    return string.Empty;
            }
        }
    }

    [Serializable]
    public sealed class AsteroidVisitState
    {
        public string Id = string.Empty;
        public string OreId = string.Empty;
        public double X;
        public double Z;
        public double Radius;
        public double Amount;
    }

    /// <summary>
    /// Per-system world state that must survive docking, jumping and reloading.
    /// Without it every undock reset the belt to full ore and respawned all
    /// NPCs, which made mining and bounty farming infinitely AFK-able.
    /// </summary>
    [Serializable]
    public sealed class SystemVisitState
    {
        /// <summary>Simulation time before which NPC traffic will not repopulate.</summary>
        public double NpcRespawnReadyAt;
        public List<AsteroidVisitState> Asteroids;
    }

    [Serializable]
    public sealed class PlayerState
    {
        public string Name = "Pilot";
        public string EmpireId = FactionIds.Aurelian;
        public long Credits = 50000;
        public Dictionary<string, int> LoyaltyPoints = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, double> Standings = new Dictionary<string, double>(StringComparer.Ordinal);
        public List<ShipInstanceState> Ships = new List<ShipInstanceState>();
        public string ActiveShipInstanceId = string.Empty;
        public Dictionary<string, double> Cargo = new Dictionary<string, double>(StringComparer.Ordinal);
        public Dictionary<string, int> Hangar = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<MissionState> Missions = new List<MissionState>();
        public Dictionary<string, int> MissionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        public string CurrentSystemId = string.Empty;
        public string DockedAtStationId = string.Empty;
        public string HomeSystemId = string.Empty;
        public string HomeStationId = string.Empty;
        public double X;
        public double Z;
        public double CriminalTimer;
        public string DestinationSystemId = string.Empty;
        /// <summary>Last simulation time the player fired a weapon; gates docking for a while.</summary>
        public double LastWeaponFireAt = -999d;
        /// <summary>How many times systems have been populated; seeds per-visit variation.</summary>
        public int VisitCounter;
        public Dictionary<string, SystemVisitState> SystemVisits = new Dictionary<string, SystemVisitState>(StringComparer.Ordinal);
        public PlayerStatsState Stats = new PlayerStatsState();
        /// <summary>Trained level per skill id (0 = untrained).</summary>
        public Dictionary<string, int> SkillLevels = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Partial skill points toward the next level per skill id.</summary>
        public Dictionary<string, double> SkillPoints = new Dictionary<string, double>(StringComparer.Ordinal);
        /// <summary>Ordered training queue; index 0 is the skill currently training.</summary>
        public List<string> SkillQueue = new List<string>();

        public ShipInstanceState ActiveShip()
        {
            return Ships.Find(ship => ship.InstanceId == ActiveShipInstanceId);
        }
    }

    public enum EntityKind
    {
        Player,
        Npc,
    }

    public enum MovementMode
    {
        Idle,
        Approach,
        Orbit,
        Patrol,
        Flee,
        WarpAlign,
        WarpCruise,
        WarpDecelerate,
    }

    [Serializable]
    public sealed class RuntimeModuleState
    {
        public string ModuleId = string.Empty;
        public double Cooldown;
        public bool Active;
    }

    [Serializable]
    public sealed class EntityState
    {
        public string Id = string.Empty;
        public EntityKind Kind;
        public string ShipId = string.Empty;
        public string FactionId = string.Empty;
        public string Name = string.Empty;
        public SimVec2 Position;
        public double HeadingRadians;
        public double Speed;
        public double Shield;
        public double Armor;
        public double Hull;
        public double MaxShield;
        public double MaxArmor;
        public double MaxHull;
        public double MaxSpeed;
        public double WarpSpeed;
        public double LockRange;
        public double DamageMultiplier = 1d;
        public MovementMode Movement;
        public string MoveTargetId = string.Empty;
        public SimVec2 MoveTargetPosition;
        public double DesiredDistance = 5d;
        public string LockedTargetId = string.Empty;
        public SimVec2 WarpTarget;
        public double WarpPhaseTime;
        public double LastDamageAt = -999d;
        public string LastAttackerId = string.Empty;
        public bool Dead;
        public bool AfterburnerOn;
        public string AiBehavior = string.Empty;
        public string MissionId = string.Empty;
        public double AggroRange = 350d;
        public bool Elite;
        public double AiTime;
        public readonly List<SimVec2> Waypoints = new List<SimVec2>();
        public int WaypointIndex;
        public readonly List<RuntimeModuleState> Modules = new List<RuntimeModuleState>();
    }

    [Serializable]
    public sealed class AsteroidState
    {
        public string Id = string.Empty;
        public string BeltId = string.Empty;
        public string OreId = string.Empty;
        public SimVec2 Position;
        public double Radius;
        public double Amount;
    }

    public sealed class GameState
    {
        public const int GeneratorVersion = 1;
        public uint Seed { get; internal set; }
        public double SimulationTime { get; internal set; }
        public uint RngState { get; internal set; }
        public ulong NextEntityId { get; internal set; }
        public GeneratedUniverse Universe { get; internal set; }
        public PlayerState Player { get; internal set; }
        public string SelectedId { get; internal set; } = string.Empty;
        public bool Docked => !string.IsNullOrEmpty(Player.DockedAtStationId);
        public bool PlayerDead { get; internal set; }
        public IReadOnlyList<EntityState> Entities => entities;
        public IReadOnlyList<AsteroidState> Asteroids => asteroids;

        internal readonly List<EntityState> entities = new List<EntityState>();
        internal readonly List<AsteroidState> asteroids = new List<AsteroidState>();

        public EntityState PlayerEntity()
        {
            return entities.Find(entity => entity.Kind == EntityKind.Player && !entity.Dead);
        }

        public EntityState FindEntity(string id)
        {
            return string.IsNullOrEmpty(id) ? null : entities.Find(entity => entity.Id == id && !entity.Dead);
        }

        public AsteroidState FindAsteroid(string id)
        {
            return string.IsNullOrEmpty(id) ? null : asteroids.Find(asteroid => asteroid.Id == id && asteroid.Amount > 0d);
        }
    }
}
