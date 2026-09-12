using System.Collections.Generic;
using System.Collections.ObjectModel;
using Starfall.Domain;

namespace Starfall.Content
{
    /// <summary>
    /// Immutable code representation of js/data/factions.js and js/data/ships.js.
    /// Stable IDs and numeric values are compatibility contracts for saves and tests.
    /// </summary>
    public sealed class GameContentCatalog : IContentCatalog
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> _standingMatrix;

        public GameContentCatalog()
        {
            Factions = ReadOnly(BuildFactions());
            Ships = ReadOnly(BuildShips());
            Modules = ReadOnly(BuildModules());
            Items = ReadOnly(BuildItems());
            Skills = ReadOnly(BuildSkills());
            ClassMultipliers = new ReadOnlyDictionary<ShipClass, double>(new Dictionary<ShipClass, double>
            {
                { ShipClass.Frigate, 1d },
                { ShipClass.Destroyer, 2.2d },
                { ShipClass.Cruiser, 5d },
                { ShipClass.Battlecruiser, 7.5d },
                { ShipClass.Battleship, 12d },
            });
            _standingMatrix = BuildStandingMatrix();
        }

        public static GameContentCatalog Default { get; } = new GameContentCatalog();

        public IReadOnlyDictionary<string, FactionDefinition> Factions { get; }
        public IReadOnlyDictionary<string, ShipDefinition> Ships { get; }
        public IReadOnlyDictionary<string, ModuleDefinition> Modules { get; }
        public IReadOnlyDictionary<string, ItemDefinition> Items { get; }
        public IReadOnlyDictionary<string, SkillDefinition> Skills { get; }
        public IReadOnlyDictionary<ShipClass, double> ClassMultipliers { get; }

        public double FactionRelation(string firstFactionId, string secondFactionId)
        {
            return _standingMatrix.TryGetValue(firstFactionId, out var row) && row.TryGetValue(secondFactionId, out var value)
                ? value
                : 0d;
        }

        private static IReadOnlyDictionary<TKey, TValue> ReadOnly<TKey, TValue>(Dictionary<TKey, TValue> source)
        {
            return new ReadOnlyDictionary<TKey, TValue>(source);
        }

        private static Dictionary<string, FactionDefinition> BuildFactions()
        {
            return new Dictionary<string, FactionDefinition>
            {
                { FactionIds.Aurelian, new FactionDefinition(FactionIds.Aurelian, "Aurelian Empire", "AUR", FactionKind.Empire, "#d4af37", "A theocratic golden empire, master of laser weaponry.", FactionIds.BloodReavers) },
                { FactionIds.Kaldari, new FactionDefinition(FactionIds.Kaldari, "Kaldari State", "KAL", FactionKind.Empire, "#4a9edd", "A corporate state built on missiles and railguns.", FactionIds.Nathari) },
                { FactionIds.Meridian, new FactionDefinition(FactionIds.Meridian, "Meridian Federation", "MER", FactionKind.Empire, "#3ec6b8", "A free federation favoring blasters and drones.", FactionIds.CrimsonHand) },
                { FactionIds.Varkhald, new FactionDefinition(FactionIds.Varkhald, "Varkhald Republic", "VAR", FactionKind.Empire, "#c96a3b", "A rugged republic of projectile-weapon clans.", FactionIds.Ashfang) },
                { FactionIds.BloodReavers, new FactionDefinition(FactionIds.BloodReavers, "Blood Reavers", "BLD", FactionKind.Pirate, "#a01830", "Fanatical raiders bleeding the Aurelian frontier.") },
                { FactionIds.Nathari, new FactionDefinition(FactionIds.Nathari, "Nathari Hive", "NAT", FactionKind.Pirate, "#7a3ec9", "A cybernetic hive mind infesting Kaldari space.") },
                { FactionIds.CrimsonHand, new FactionDefinition(FactionIds.CrimsonHand, "Crimson Hand", "CRI", FactionKind.Pirate, "#d13050", "Smugglers and cartel enforcers of the Federation.") },
                { FactionIds.Ashfang, new FactionDefinition(FactionIds.Ashfang, "Ashfang Cartel", "ASH", FactionKind.Pirate, "#8a8f3a", "Nomad cartel preying on the Varkhald Republic.") },
                { FactionIds.Sisters, new FactionDefinition(FactionIds.Sisters, "Sisters of the Veil", "SIS", FactionKind.Sisters, "#e8e4d8", "A humanitarian order devoted to exploration and mercy.") },
                { FactionIds.Directorate, new FactionDefinition(FactionIds.Directorate, "The Directorate", "DIR", FactionKind.Police, "#e8b400", "The unified stellar authority policing all of known space.") },
            };
        }

        private static Dictionary<string, ShipDefinition> BuildShips()
        {
            return new Dictionary<string, ShipDefinition>
            {
                { ShipIds.Acolyte, Ship(ShipIds.Acolyte, "Acolyte", FactionIds.Aurelian, ShipClass.Frigate, 150, 700, 320, 280, 220, 2, 2, 2, 160, 320, 40000, "Fast Aurelian laser frigate.", powerGrid: 35, cpu: 110) },
                { ShipIds.Templar, Ship(ShipIds.Templar, "Templar", FactionIds.Aurelian, ShipClass.Destroyer, 125, 650, 550, 600, 450, 3, 2, 3, 320, 380, 220000, "Aurelian destroyer with heavy pulse lasers.", powerGrid: 60, cpu: 160) },
                { ShipIds.Dawnbringer, Ship(ShipIds.Dawnbringer, "Dawnbringer", FactionIds.Aurelian, ShipClass.Cruiser, 100, 600, 1100, 1500, 1000, 4, 3, 4, 600, 450, 1200000, "Aurelian cruiser, a floating battery of light.", powerGrid: 130, cpu: 260) },
                { ShipIds.Justicar, Ship(ShipIds.Justicar, "Justicar", FactionIds.Aurelian, ShipClass.Battlecruiser, 88, 550, 1700, 2600, 1800, 5, 3, 5, 700, 750, 3500000, "Aurelian battlecruiser, judgment in a hull.", powerGrid: 210, cpu: 360) },
                { ShipIds.Seraph, Ship(ShipIds.Seraph, "Seraph", FactionIds.Aurelian, ShipClass.Battleship, 75, 500, 2600, 4200, 2800, 6, 4, 6, 900, 550, 9000000, "Golden Aurelian battleship of judgment.", powerGrid: 320, cpu: 480) },

                { ShipIds.Shrike, Ship(ShipIds.Shrike, "Shrike", FactionIds.Kaldari, ShipClass.Frigate, 145, 700, 420, 220, 200, 2, 3, 1, 160, 340, 40000, "Kaldari missile frigate, long reach.", powerGrid: 35, cpu: 120) },
                { ShipIds.Heron, Ship(ShipIds.Heron, "Heron", FactionIds.Kaldari, ShipClass.Destroyer, 120, 650, 800, 380, 400, 3, 3, 2, 320, 400, 220000, "Kaldari railgun destroyer.", powerGrid: 60, cpu: 175) },
                { ShipIds.Rook, Ship(ShipIds.Rook, "Rook", FactionIds.Kaldari, ShipClass.Cruiser, 95, 600, 1900, 800, 900, 4, 4, 3, 600, 480, 1200000, "Kaldari missile cruiser with deep shields.", powerGrid: 130, cpu: 280) },
                { ShipIds.Warden, Ship(ShipIds.Warden, "Warden", FactionIds.Kaldari, ShipClass.Battlecruiser, 82, 550, 3300, 1500, 1600, 5, 4, 4, 700, 780, 3500000, "Kaldari battlecruiser, a mobile shield fortress.", powerGrid: 210, cpu: 380) },
                { ShipIds.Onyx, Ship(ShipIds.Onyx, "Onyx", FactionIds.Kaldari, ShipClass.Battleship, 70, 500, 5200, 2200, 2400, 6, 5, 4, 900, 600, 9000000, "Kaldari battleship, a fortress of shields.", powerGrid: 320, cpu: 540) },

                { ShipIds.Wasp, Ship(ShipIds.Wasp, "Wasp", FactionIds.Meridian, ShipClass.Frigate, 160, 700, 300, 320, 240, 2, 2, 2, 170, 300, 40000, "Meridian blaster frigate, fast and mean.", powerGrid: 35, cpu: 110) },
                { ShipIds.Anvil, Ship(ShipIds.Anvil, "Anvil", FactionIds.Meridian, ShipClass.Destroyer, 130, 650, 520, 650, 480, 3, 2, 3, 340, 360, 220000, "Meridian destroyer built for brawls.", powerGrid: 60, cpu: 160) },
                { ShipIds.Mantis, Ship(ShipIds.Mantis, "Mantis", FactionIds.Meridian, ShipClass.Cruiser, 105, 600, 1000, 1700, 1100, 4, 3, 4, 640, 430, 1200000, "Meridian cruiser with crushing close-range damage.", powerGrid: 130, cpu: 260) },
                { ShipIds.Bulwark, Ship(ShipIds.Bulwark, "Bulwark", FactionIds.Meridian, ShipClass.Battlecruiser, 90, 560, 1600, 2900, 2000, 5, 3, 5, 750, 720, 3500000, "Meridian battlecruiser that anchors the line.", powerGrid: 210, cpu: 360) },
                { ShipIds.Colossus, Ship(ShipIds.Colossus, "Colossus", FactionIds.Meridian, ShipClass.Battleship, 78, 500, 2400, 4600, 3000, 6, 4, 6, 950, 520, 9000000, "Meridian battleship, an armored giant.", powerGrid: 320, cpu: 480) },

                { ShipIds.Fang, Ship(ShipIds.Fang, "Fang", FactionIds.Varkhald, ShipClass.Frigate, 170, 720, 300, 280, 260, 2, 2, 2, 150, 310, 40000, "Varkhald autocannon frigate, fastest hull afloat.", powerGrid: 35, cpu: 105) },
                { ShipIds.Maul, Ship(ShipIds.Maul, "Maul", FactionIds.Varkhald, ShipClass.Destroyer, 135, 660, 540, 580, 520, 3, 2, 3, 330, 370, 220000, "Varkhald destroyer with relentless barrage.", powerGrid: 60, cpu: 155) },
                { ShipIds.Broadsword, Ship(ShipIds.Broadsword, "Broadsword", FactionIds.Varkhald, ShipClass.Cruiser, 110, 600, 1150, 1500, 1200, 4, 3, 4, 620, 440, 1200000, "Varkhald cruiser, balanced and brutal.", powerGrid: 130, cpu: 260) },
                { ShipIds.Warhound, Ship(ShipIds.Warhound, "Warhound", FactionIds.Varkhald, ShipClass.Battlecruiser, 95, 570, 1800, 2500, 2100, 5, 3, 4, 720, 730, 3500000, "Varkhald battlecruiser that hunts in packs.", powerGrid: 210, cpu: 360) },
                { ShipIds.Stormcaller, Ship(ShipIds.Stormcaller, "Stormcaller", FactionIds.Varkhald, ShipClass.Battleship, 82, 510, 2800, 4000, 3200, 6, 4, 5, 900, 540, 9000000, "Varkhald battleship that brings the storm.", powerGrid: 320, cpu: 470) },

                { ShipIds.Pilgrim, Ship(ShipIds.Pilgrim, "Pilgrim", FactionIds.Sisters, ShipClass.Frigate, 165, 800, 380, 340, 260, 2, 3, 2, 220, 380, 350000, "Sisters of the Veil exploration frigate.", powerGrid: 40, cpu: 130) },
                { ShipIds.Enforcer, Ship(ShipIds.Enforcer, "Enforcer", FactionIds.Directorate, ShipClass.Cruiser, 130, 900, 3000, 3000, 2000, 5, 4, 4, 400, 600, 0, "Directorate response cruiser. Not for sale.", true, 150, 280) },
            };
        }

        private static ShipDefinition Ship(string id, string name, string factionId, ShipClass shipClass,
            double speed, double warpSpeed, double shield, double armor, double hull,
            int high, int mid, int low, double cargo, double lockRange, long price, string description, bool npcOnly = false,
            double powerGrid = 30d, double cpu = 100d)
        {
            return new ShipDefinition(id, name, factionId, shipClass, speed, warpSpeed,
                new HitPoints(shield, armor, hull), new SlotLayout(high, mid, low), cargo, lockRange, price, description, npcOnly,
                powerGrid, cpu);
        }

        private static Dictionary<string, ModuleDefinition> BuildModules()
        {
            return new Dictionary<string, ModuleDefinition>
            {
                { ModuleIds.PulseLaser, new ModuleDefinition(ModuleIds.PulseLaser, "Pulse Laser", SlotType.High, ModuleKind.Weapon, 15000, "Aurelian energy turret.", damage: 16, cycleTime: 2, range: 75, beamColor: "#ffd76a", powerGrid: 6, cpu: 18) },
                { ModuleIds.HeavyLaser, new ModuleDefinition(ModuleIds.HeavyLaser, "Heavy Beam Laser", SlotType.High, ModuleKind.Weapon, 90000, "Capital-grade beam, cruiser+ punch.", damage: 34, cycleTime: 3.5, range: 100, beamColor: "#ffbf40", size: ModuleSize.Medium, powerGrid: 30, cpu: 45) },
                { ModuleIds.Railgun, new ModuleDefinition(ModuleIds.Railgun, "Railgun", SlotType.High, ModuleKind.Weapon, 18000, "Kaldari long-range hybrid turret.", damage: 20, cycleTime: 3, range: 150, beamColor: "#7ac2ff", powerGrid: 8, cpu: 22) },
                { ModuleIds.MissileLauncher, new ModuleDefinition(ModuleIds.MissileLauncher, "Missile Launcher", SlotType.High, ModuleKind.Weapon, 20000, "Launches seeker missiles.", damage: 30, cycleTime: 4, range: 170, projectile: true, powerGrid: 7, cpu: 20) },
                { ModuleIds.Blaster, new ModuleDefinition(ModuleIds.Blaster, "Ion Blaster", SlotType.High, ModuleKind.Weapon, 16000, "Meridian close-range hybrid turret.", damage: 26, cycleTime: 2.2, range: 50, beamColor: "#6affd8", powerGrid: 6, cpu: 16) },
                { ModuleIds.Autocannon, new ModuleDefinition(ModuleIds.Autocannon, "Autocannon", SlotType.High, ModuleKind.Weapon, 14000, "Varkhald rapid projectile turret.", damage: 18, cycleTime: 1.5, range: 65, beamColor: "#ff9a5a", powerGrid: 6, cpu: 15) },
                { ModuleIds.MiningLaser, new ModuleDefinition(ModuleIds.MiningLaser, "Mining Laser", SlotType.High, ModuleKind.Mining, 12000, "Extracts ore from asteroids.", cycleTime: 4, range: 70, beamColor: "#8aff8a", miningYield: 10, powerGrid: 5, cpu: 12) },
                { ModuleIds.ShieldBooster, new ModuleDefinition(ModuleIds.ShieldBooster, "Shield Booster", SlotType.Mid, ModuleKind.ShieldBoost, 25000, "Active shield restoration burst.", cycleTime: 8, repairAmount: 90, powerGrid: 10, cpu: 28) },
                { ModuleIds.Afterburner, new ModuleDefinition(ModuleIds.Afterburner, "Afterburner", SlotType.Mid, ModuleKind.Propulsion, 20000, "Toggle: +80% sublight speed.", speedMultiplier: 1.8, powerGrid: 12, cpu: 22) },
                { ModuleIds.ArmorRepairer, new ModuleDefinition(ModuleIds.ArmorRepairer, "Armor Repairer", SlotType.Low, ModuleKind.ArmorRepair, 25000, "Active armor restoration.", cycleTime: 10, repairAmount: 70, powerGrid: 10, cpu: 20) },
                { ModuleIds.ShieldExtender, new ModuleDefinition(ModuleIds.ShieldExtender, "Shield Extender", SlotType.Low, ModuleKind.Passive, 22000, "Passive: +200 max shield.", shieldBonus: 200, powerGrid: 10, cpu: 15) },
                { ModuleIds.ArmorPlate, new ModuleDefinition(ModuleIds.ArmorPlate, "Armor Plate", SlotType.Low, ModuleKind.Passive, 22000, "Passive: +250 max armor.", armorBonus: 250, powerGrid: 12, cpu: 10) },
                { ModuleIds.DamageAmp, new ModuleDefinition(ModuleIds.DamageAmp, "Weapon Amplifier", SlotType.Low, ModuleKind.Passive, 45000, "Passive: +18% weapon damage.", damageMultiplier: 1.18, powerGrid: 5, cpu: 25) },
                { ModuleIds.CargoExpander, new ModuleDefinition(ModuleIds.CargoExpander, "Cargo Expander", SlotType.Low, ModuleKind.Passive, 15000, "Passive: +250 m3 cargo.", cargoBonus: 250, powerGrid: 0, cpu: 20) },
            };
        }

        private static Dictionary<string, SkillDefinition> BuildSkills()
        {
            return new Dictionary<string, SkillDefinition>
            {
                { SkillIds.SpaceshipCommand, new SkillDefinition(SkillIds.SpaceshipCommand, "Spaceship Command", "Gates access to larger ship classes.", 1, 0d) },
                { SkillIds.Gunnery, new SkillDefinition(SkillIds.Gunnery, "Gunnery", "+4% turret damage per level.", 1, 0.04d) },
                { SkillIds.Missiles, new SkillDefinition(SkillIds.Missiles, "Missile Operation", "+4% missile damage per level.", 1, 0.04d) },
                { SkillIds.Mining, new SkillDefinition(SkillIds.Mining, "Mining", "+5% mining yield per level.", 1, 0.05d) },
                { SkillIds.ShieldOperation, new SkillDefinition(SkillIds.ShieldOperation, "Shield Operation", "+5% shield restoration per level.", 1, 0.05d) },
                { SkillIds.Mechanics, new SkillDefinition(SkillIds.Mechanics, "Mechanics", "+5% armor repair amount per level.", 1, 0.05d) },
                { SkillIds.Navigation, new SkillDefinition(SkillIds.Navigation, "Navigation", "+5% sublight speed per level.", 1, 0.05d) },
            };
        }

        private static Dictionary<string, ItemDefinition> BuildItems()
        {
            return new Dictionary<string, ItemDefinition>
            {
                { ItemIds.Ferrite, new ItemDefinition(ItemIds.Ferrite, "Ferrite Ore", 1, 12, "Common high-security ore.") },
                { ItemIds.Novacite, new ItemDefinition(ItemIds.Novacite, "Novacite Ore", 1.5, 45, "Uncommon low-security ore.") },
                { ItemIds.Crystalline, new ItemDefinition(ItemIds.Crystalline, "Crystalline Ore", 2, 140, "Rare null-security ore.") },
                { ItemIds.SealedCargo, new ItemDefinition(ItemIds.SealedCargo, "Sealed Cargo", 1, 0, "Mission cargo. Handle with care.", true) },
            };
        }

        private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> BuildStandingMatrix()
        {
            var result = new Dictionary<string, IReadOnlyDictionary<string, double>>
            {
                { FactionIds.Aurelian, Row(10, 5, -5, -2, -8, -5, -3, -5, 3, 8) },
                { FactionIds.Kaldari, Row(5, 10, -2, -5, -5, -8, -3, -5, 2, 8) },
                { FactionIds.Meridian, Row(-5, -2, 10, 5, -3, -5, -8, -5, 5, 8) },
                { FactionIds.Varkhald, Row(-2, -5, 5, 10, -5, -3, -5, -8, 3, 8) },
                { FactionIds.BloodReavers, Row(-8, -5, -3, -5, 10, 5, 2, 2, -5, -10) },
                { FactionIds.Nathari, Row(-5, -8, -5, -3, 5, 10, 2, 2, -5, -10) },
                { FactionIds.CrimsonHand, Row(-3, -3, -8, -5, 2, 2, 10, 5, -5, -10) },
                { FactionIds.Ashfang, Row(-5, -5, -5, -8, 2, 2, 5, 10, -5, -10) },
                { FactionIds.Sisters, Row(3, 2, 5, 3, -5, -5, -5, -5, 10, 5) },
                { FactionIds.Directorate, Row(8, 8, 8, 8, -10, -10, -10, -10, 5, 10) },
            };
            return new ReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>(result);
        }

        private static IReadOnlyDictionary<string, double> Row(params double[] values)
        {
            var row = new Dictionary<string, double>();
            for (var i = 0; i < FactionIds.All.Length; i++)
            {
                row.Add(FactionIds.All[i], values[i]);
            }

            return new ReadOnlyDictionary<string, double>(row);
        }
    }
}
