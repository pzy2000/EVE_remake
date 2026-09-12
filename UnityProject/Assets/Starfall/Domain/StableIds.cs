namespace Starfall.Domain
{
    public static class FactionIds
    {
        public const string Aurelian = "aurelian";
        public const string Kaldari = "kaldari";
        public const string Meridian = "meridian";
        public const string Varkhald = "varkhald";
        public const string BloodReavers = "blood_reavers";
        public const string Nathari = "nathari";
        public const string CrimsonHand = "crimson_hand";
        public const string Ashfang = "ashfang";
        public const string Sisters = "sisters";
        public const string Directorate = "directorate";

        public static readonly string[] Empires = { Aurelian, Kaldari, Meridian, Varkhald };
        public static readonly string[] Pirates = { BloodReavers, Nathari, CrimsonHand, Ashfang };
        public static readonly string[] All =
        {
            Aurelian, Kaldari, Meridian, Varkhald,
            BloodReavers, Nathari, CrimsonHand, Ashfang,
            Sisters, Directorate,
        };
    }

    public static class ShipIds
    {
        public const string Acolyte = "acolyte";
        public const string Templar = "templar";
        public const string Dawnbringer = "dawnbringer";
        public const string Justicar = "justicar";
        public const string Seraph = "seraph";
        public const string Shrike = "shrike";
        public const string Heron = "heron";
        public const string Rook = "rook";
        public const string Warden = "warden";
        public const string Onyx = "onyx";
        public const string Wasp = "wasp";
        public const string Anvil = "anvil";
        public const string Mantis = "mantis";
        public const string Bulwark = "bulwark";
        public const string Colossus = "colossus";
        public const string Fang = "fang";
        public const string Maul = "maul";
        public const string Broadsword = "broadsword";
        public const string Warhound = "warhound";
        public const string Stormcaller = "stormcaller";
        public const string Pilgrim = "pilgrim";
        public const string Enforcer = "enforcer";

        public static readonly string[] All =
        {
            Acolyte, Templar, Dawnbringer, Justicar, Seraph,
            Shrike, Heron, Rook, Warden, Onyx,
            Wasp, Anvil, Mantis, Bulwark, Colossus,
            Fang, Maul, Broadsword, Warhound, Stormcaller,
            Pilgrim, Enforcer,
        };
    }

    public static class ModuleIds
    {
        public const string PulseLaser = "pulse_laser";
        public const string HeavyLaser = "heavy_laser";
        public const string Railgun = "railgun";
        public const string MissileLauncher = "missile_launcher";
        public const string Blaster = "blaster";
        public const string Autocannon = "autocannon";
        public const string MiningLaser = "mining_laser";
        public const string ShieldBooster = "shield_booster";
        public const string Afterburner = "afterburner";
        public const string ArmorRepairer = "armor_repairer";
        public const string ShieldExtender = "shield_extender";
        public const string ArmorPlate = "armor_plate";
        public const string DamageAmp = "damage_amp";
        public const string CargoExpander = "cargo_expander";

        public static readonly string[] All =
        {
            PulseLaser, HeavyLaser, Railgun, MissileLauncher, Blaster, Autocannon, MiningLaser,
            ShieldBooster, Afterburner, ArmorRepairer, ShieldExtender, ArmorPlate, DamageAmp, CargoExpander,
        };
    }

    public static class ItemIds
    {
        public const string Ferrite = "ferrite";
        public const string Novacite = "novacite";
        public const string Crystalline = "crystalline";
        public const string SealedCargo = "sealed_cargo";

        public static readonly string[] All = { Ferrite, Novacite, Crystalline, SealedCargo };
    }
}
