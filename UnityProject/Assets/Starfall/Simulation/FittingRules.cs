using System;
using System.Collections.Generic;
using Starfall.Content;
using Starfall.Domain;

namespace Starfall.Simulation
{
    /// <summary>
    /// Shared fitting validation for GameSession.Fit, UI previews and tests:
    /// slot type, module size, skill requirement, and power grid / CPU budgets.
    /// Pure functions only — no session state is touched.
    /// </summary>
    public static class FittingRules
    {
        /// <summary>
        /// EVE-style stacking penalty per duplicate passive module: the 2nd module
        /// contributes 86.9%, the 3rd 57.1%, the 4th 23.3%, beyond that ~11%.
        /// </summary>
        public static readonly double[] StackingPenalties = { 1d, 0.869d, 0.571d, 0.233d, 0.11d };

        public static double StackingFactor(int duplicateIndex)
        {
            return duplicateIndex < StackingPenalties.Length
                ? StackingPenalties[duplicateIndex]
                : StackingPenalties[StackingPenalties.Length - 1];
        }

        /// <summary>Additive stacking: sum of bonus × penalty across duplicates (shield/armor/cargo).</summary>
        public static double StackedAdditive(int count, double bonus)
        {
            var total = 0d;
            for (var i = 0; i < count; i++) total += bonus * StackingFactor(i);
            return total;
        }

        /// <summary>Multiplicative stacking: product of (1 + (m-1) × penalty) across duplicates (damage amps).</summary>
        public static double StackedMultiplicative(int count, double multiplier)
        {
            var result = 1d;
            for (var i = 0; i < count; i++) result *= 1d + (multiplier - 1d) * StackingFactor(i);
            return result;
        }

        /// <summary>Medium modules need a cruiser-class hull; large modules a battleship.</summary>
        public static bool SizeAllowedForClass(ModuleSize size, ShipClass shipClass)
        {
            switch (size)
            {
                case ModuleSize.Small:
                    return true;
                case ModuleSize.Medium:
                    return shipClass == ShipClass.Cruiser || shipClass == ShipClass.Battlecruiser ||
                           shipClass == ShipClass.Battleship;
                case ModuleSize.Large:
                    return shipClass == ShipClass.Battleship;
                default:
                    return false;
            }
        }

        public static void FittingUsage(IContentCatalog catalog, ShipInstanceState ship, out double powerGrid, out double cpu)
        {
            powerGrid = 0d;
            cpu = 0d;
            if (ship == null) return;
            foreach (var moduleId in ship.Fitting.All())
            {
                if (!catalog.Modules.TryGetValue(moduleId, out var module)) continue;
                powerGrid += module.PowerGrid;
                cpu += module.Cpu;
            }
        }

        /// <summary>
        /// Full check for fitting <paramref name="moduleId"/> into slot <paramref name="index"/> of
        /// <paramref name="slotName"/> on <paramref name="ship"/>. The skill lookup is supplied by the
        /// caller (session or preview); null treats every skill as trained. On failure
        /// <paramref name="reason"/> carries an untranslated format template plus its args so callers
        /// can localize with a single Tr call.
        /// </summary>
        public static bool CanFitModule(IContentCatalog catalog, ShipInstanceState ship, string slotName, int index,
            string moduleId, Func<string, int> skillLevel, out string reason, out object[] reasonArgs)
        {
            reason = null;
            reasonArgs = null;
            if (ship == null || !catalog.Ships.TryGetValue(ship.ShipId, out var hull) ||
                !catalog.Modules.TryGetValue(moduleId, out var module))
            {
                reason = "No compatible free slot.";
                return false;
            }
            var slots = SlotList(ship.Fitting, slotName);
            if (slots == null || index < 0 || index >= slots.Count || !string.IsNullOrEmpty(slots[index]) ||
                !SlotMatches(module.Slot, slotName))
            {
                reason = "No compatible free slot.";
                return false;
            }
            if (!SizeAllowedForClass(module.Size, hull.Class))
            {
                reason = "{0} modules do not fit a {1} hull.";
                reasonArgs = new object[] { module.Size.ToString(), hull.Class.ToString() };
                return false;
            }
            if (SkillRules.ModuleRequirement(moduleId, out var skillId, out var requiredLevel))
            {
                var level = skillLevel != null ? skillLevel(skillId) : int.MaxValue;
                if (level < requiredLevel)
                {
                    reason = "{0} requires {1} {2}.";
                    reasonArgs = new object[] { module.Name, catalog.Skills[skillId].Name, requiredLevel };
                    return false;
                }
            }
            FittingUsage(catalog, ship, out var usedGrid, out var usedCpu);
            if (usedGrid + module.PowerGrid > hull.PowerGrid + 1e-9d)
            {
                reason = "Not enough power grid.";
                return false;
            }
            if (usedCpu + module.Cpu > hull.Cpu + 1e-9d)
            {
                reason = "Not enough CPU.";
                return false;
            }
            return true;
        }

        private static List<string> SlotList(FittingState fitting, string slot)
        {
            if (slot == "high") return fitting.High;
            if (slot == "mid") return fitting.Mid;
            if (slot == "low") return fitting.Low;
            return null;
        }

        private static bool SlotMatches(SlotType type, string slot)
        {
            return (type == SlotType.High && slot == "high") ||
                   (type == SlotType.Mid && slot == "mid") ||
                   (type == SlotType.Low && slot == "low");
        }
    }
}
