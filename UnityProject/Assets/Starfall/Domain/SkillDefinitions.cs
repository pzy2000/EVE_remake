using System;

namespace Starfall.Domain
{
    public sealed class SkillDefinition
    {
        public SkillDefinition(string id, string name, string description, int rank, double bonusPerLevel)
        {
            Id = id;
            Name = name;
            Description = description;
            Rank = rank;
            BonusPerLevel = bonusPerLevel;
        }

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        /// <summary>Training time multiplier; higher rank trains slower.</summary>
        public int Rank { get; }
        /// <summary>Fractional bonus granted per trained level (0 = gating skill).</summary>
        public double BonusPerLevel { get; }
    }

    public static class SkillIds
    {
        public const string SpaceshipCommand = "spaceship_command";
        public const string Gunnery = "gunnery";
        public const string Missiles = "missiles";
        public const string Mining = "mining";
        public const string ShieldOperation = "shield_operation";
        public const string Mechanics = "mechanics";
        public const string Navigation = "navigation";

        public static readonly string[] All =
        {
            SpaceshipCommand, Gunnery, Missiles, Mining, ShieldOperation, Mechanics, Navigation,
        };
    }

    /// <summary>
    /// Pure skill rules shared by GameSession, UI previews and tests. Kept free of
    /// side effects so the same thresholds decide training, gating and display.
    /// </summary>
    public static class SkillRules
    {
        public const int MaxLevel = 5;
        /// <summary>Training speed in skill points per simulated second.</summary>
        public const double PointsPerSecond = 1d;
        public const int MaxQueueLength = 10;
        /// <summary>Wall-clock offline catch-up ceiling applied by the app layer.</summary>
        public const double MaxOfflineSeconds = 14d * 24d * 3600d;

        /// <summary>Skill points required to advance from <paramref name="currentLevel"/> to the next.</summary>
        public static double PointsToNextLevel(int rank, int currentLevel)
        {
            if (currentLevel < 0) currentLevel = 0;
            if (currentLevel >= MaxLevel) return 0d;
            return 400d * Math.Max(1, rank) * Math.Pow(2d, currentLevel);
        }

        /// <summary>Spaceship Command level required to pilot a ship class.</summary>
        public static int RequiredForShipClass(ShipClass shipClass)
        {
            switch (shipClass)
            {
                case ShipClass.Frigate: return 1;
                case ShipClass.Destroyer: return 2;
                case ShipClass.Cruiser: return 3;
                case ShipClass.Battlecruiser: return 4;
                case ShipClass.Battleship: return 5;
                default: return 1;
            }
        }

        /// <summary>Skill level required to fit a module; false when anyone may fit it.</summary>
        public static bool ModuleRequirement(string moduleId, out string skillId, out int level)
        {
            if (string.Equals(moduleId, ModuleIds.HeavyLaser, StringComparison.Ordinal))
            {
                skillId = SkillIds.Gunnery;
                level = 3;
                return true;
            }
            skillId = null;
            level = 0;
            return false;
        }

        /// <summary>The weapon skill that scales a module (turrets train Gunnery, launchers Missiles).</summary>
        public static string WeaponSkill(string moduleId)
        {
            return string.Equals(moduleId, ModuleIds.MissileLauncher, StringComparison.Ordinal)
                ? SkillIds.Missiles
                : SkillIds.Gunnery;
        }
    }
}
