using System;
using Starfall.Domain;

namespace Starfall.Simulation
{
    public enum EntityDisposition
    {
        Neutral,
        Friendly,
        Hostile,
    }

    /// <summary>
    /// Single source of truth for NPC disposition. Simulation AI and presentation
    /// code consume this policy so the overview never disagrees with aggro rules.
    /// </summary>
    public static class EntityDispositionPolicy
    {
        public static EntityDisposition Evaluate(EntityState entity, PlayerState player,
            IContentCatalog catalog, string playerEntityId = "player")
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            if (!string.IsNullOrEmpty(entity.MissionId)) return EntityDisposition.Hostile;

            var factionId = entity.FactionId ?? string.Empty;
            if (!catalog.Factions.TryGetValue(factionId, out var faction))
                return EntityDisposition.Neutral;

            // Match the legacy browser disposition thresholds exactly. Directorate
            // contacts are a law-enforcement state rather than a standing gradient.
            if (faction.Kind == FactionKind.Police)
                return player.CriminalTimer > 0d
                    ? EntityDisposition.Hostile
                    : EntityDisposition.Neutral;

            var effectiveStanding = EffectiveStanding(player, catalog, factionId);
            if (faction.Kind == FactionKind.Pirate)
            {
                if (effectiveStanding >= 5d) return EntityDisposition.Friendly;
                if (effectiveStanding > 0d) return EntityDisposition.Neutral;
                return EntityDisposition.Hostile;
            }

            // Criminal status turns empire navies hostile, but Sisters retain their
            // normal standing-based disposition just as in js/systems/standings.js.
            if (faction.Kind == FactionKind.Empire && player.CriminalTimer > 0d)
                return EntityDisposition.Hostile;
            if (effectiveStanding < -5d) return EntityDisposition.Hostile;
            if (effectiveStanding >= 5d) return EntityDisposition.Friendly;
            return EntityDisposition.Neutral;
        }

        public static double EffectiveStanding(PlayerState player, IContentCatalog catalog, string factionId)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            factionId = factionId ?? string.Empty;
            player.Standings.TryGetValue(factionId, out var standing);
            foreach (var pair in player.Standings)
            {
                if (pair.Key == factionId || pair.Value == 0d) continue;
                standing += pair.Value * catalog.FactionRelation(pair.Key, factionId) * 0.04d;
            }
            return Math.Max(-10d, Math.Min(10d, standing));
        }
    }
}
