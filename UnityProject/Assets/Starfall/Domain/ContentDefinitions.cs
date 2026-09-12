using System.Collections.Generic;

namespace Starfall.Domain
{
    public enum FactionKind { Empire, Pirate, Sisters, Police }
    public enum ShipClass { Frigate, Destroyer, Cruiser, Battlecruiser, Battleship }
    public enum SlotType { High, Mid, Low }
    public enum ModuleKind { Weapon, Mining, ShieldBoost, ArmorRepair, Propulsion, Passive }
    /// <summary>Weapon/module tier: small hulls cannot mount heavier hardware.</summary>
    public enum ModuleSize { Small, Medium, Large }

    public sealed class FactionDefinition
    {
        public FactionDefinition(string id, string name, string abbreviation, FactionKind kind, string color,
            string description, string homePirateId = null)
        {
            Id = id;
            Name = name;
            Abbreviation = abbreviation;
            Kind = kind;
            Color = color;
            Description = description;
            HomePirateId = homePirateId;
        }

        public string Id { get; }
        public string Name { get; }
        public string Abbreviation { get; }
        public FactionKind Kind { get; }
        public string Color { get; }
        public string Description { get; }
        public string HomePirateId { get; }
    }

    public readonly struct HitPoints
    {
        public HitPoints(double shield, double armor, double hull)
        {
            Shield = shield;
            Armor = armor;
            Hull = hull;
        }

        public double Shield { get; }
        public double Armor { get; }
        public double Hull { get; }
    }

    public readonly struct SlotLayout
    {
        public SlotLayout(int high, int mid, int low)
        {
            High = high;
            Mid = mid;
            Low = low;
        }

        public int High { get; }
        public int Mid { get; }
        public int Low { get; }
    }

    public sealed class ShipDefinition
    {
        public ShipDefinition(string id, string name, string factionId, ShipClass shipClass, double speed,
            double warpSpeed, HitPoints hitPoints, SlotLayout slots, double cargoCapacity, double lockRange,
            long price, string description, bool npcOnly = false, double powerGrid = 0d, double cpu = 0d)
        {
            Id = id;
            Name = name;
            FactionId = factionId;
            Class = shipClass;
            Speed = speed;
            WarpSpeed = warpSpeed;
            HitPoints = hitPoints;
            Slots = slots;
            CargoCapacity = cargoCapacity;
            LockRange = lockRange;
            Price = price;
            Description = description;
            NpcOnly = npcOnly;
            PowerGrid = powerGrid;
            Cpu = cpu;
        }

        public string Id { get; }
        public string Name { get; }
        public string FactionId { get; }
        public ShipClass Class { get; }
        public double Speed { get; }
        public double WarpSpeed { get; }
        public HitPoints HitPoints { get; }
        public SlotLayout Slots { get; }
        public double CargoCapacity { get; }
        public double LockRange { get; }
        public long Price { get; }
        public string Description { get; }
        public bool NpcOnly { get; }
        /// <summary>Total power grid available for fitted modules.</summary>
        public double PowerGrid { get; }
        /// <summary>Total CPU available for fitted modules.</summary>
        public double Cpu { get; }
    }

    public sealed class ModuleDefinition
    {
        public ModuleDefinition(string id, string name, SlotType slot, ModuleKind kind, long price, string description,
            double damage = 0d, double cycleTime = 0d, double range = 0d, string beamColor = null,
            bool projectile = false, double miningYield = 0d, double repairAmount = 0d,
            double speedMultiplier = 0d, double shieldBonus = 0d, double armorBonus = 0d,
            double damageMultiplier = 0d, double cargoBonus = 0d,
            ModuleSize size = ModuleSize.Small, double powerGrid = 0d, double cpu = 0d)
        {
            Id = id;
            Name = name;
            Slot = slot;
            Kind = kind;
            Price = price;
            Description = description;
            Damage = damage;
            CycleTime = cycleTime;
            Range = range;
            BeamColor = beamColor;
            Projectile = projectile;
            MiningYield = miningYield;
            RepairAmount = repairAmount;
            SpeedMultiplier = speedMultiplier;
            ShieldBonus = shieldBonus;
            ArmorBonus = armorBonus;
            DamageMultiplier = damageMultiplier;
            CargoBonus = cargoBonus;
            Size = size;
            PowerGrid = powerGrid;
            Cpu = cpu;
        }

        public string Id { get; }
        public string Name { get; }
        public SlotType Slot { get; }
        public ModuleKind Kind { get; }
        public long Price { get; }
        public string Description { get; }
        public double Damage { get; }
        public double CycleTime { get; }
        public double Range { get; }
        public string BeamColor { get; }
        public bool Projectile { get; }
        public double MiningYield { get; }
        public double RepairAmount { get; }
        public double SpeedMultiplier { get; }
        public double ShieldBonus { get; }
        public double ArmorBonus { get; }
        public double DamageMultiplier { get; }
        public double CargoBonus { get; }
        public ModuleSize Size { get; }
        public double PowerGrid { get; }
        public double Cpu { get; }
    }

    public sealed class ItemDefinition
    {
        public ItemDefinition(string id, string name, double volume, long basePrice, string description, bool noMarket = false)
        {
            Id = id;
            Name = name;
            Volume = volume;
            BasePrice = basePrice;
            Description = description;
            NoMarket = noMarket;
        }

        public string Id { get; }
        public string Name { get; }
        public double Volume { get; }
        public long BasePrice { get; }
        public string Description { get; }
        public bool NoMarket { get; }
    }

    public interface IContentCatalog
    {
        IReadOnlyDictionary<string, FactionDefinition> Factions { get; }
        IReadOnlyDictionary<string, ShipDefinition> Ships { get; }
        IReadOnlyDictionary<string, ModuleDefinition> Modules { get; }
        IReadOnlyDictionary<string, ItemDefinition> Items { get; }
        IReadOnlyDictionary<string, SkillDefinition> Skills { get; }
        IReadOnlyDictionary<ShipClass, double> ClassMultipliers { get; }
        double FactionRelation(string firstFactionId, string secondFactionId);
    }
}
