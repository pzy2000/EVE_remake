using System;
using System.Collections.Generic;
using Starfall.Domain;
using Starfall.Simulation;
using Starfall.UI;

namespace Starfall.App
{
    /// <summary>
    /// Builds the full, unfiltered set of contacts for the current system. The UI applies the
    /// active EVE-style preset and sort order. Structural rebuilds allocate contacts; the 10 Hz
    /// telemetry path updates the existing objects in place.
    /// </summary>
    internal static class OverviewContactBuilder
    {
        private const string ResourceAccent = "#b99262";
        private const string MoonAccent = "#8494ad";

        public static void Rebuild(List<UiOverviewContact> contacts, GameSession session, IContentCatalog catalog)
        {
            if (contacts == null) throw new ArgumentNullException(nameof(contacts));
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var state = session.State;
            var system = CurrentSystem(state);
            contacts.Clear();

            for (var i = 0; i < system.Stations.Count; i++)
            {
                var station = system.Stations[i];
                contacts.Add(Create(station.Id, OverviewKind.Station, station.Name, "Station",
                    "Dock within 40 m", FactionAccent(catalog, station.FactionId),
                    OverviewActionFlags.Select | OverviewActionFlags.Approach | OverviewActionFlags.Warp |
                    OverviewActionFlags.Dock));
            }

            for (var i = 0; i < system.Gates.Count; i++)
            {
                var gate = system.Gates[i];
                var destinationName = state.Universe.Systems.TryGetValue(gate.DestinationSystemId, out var destination)
                    ? destination.Name
                    : gate.DestinationSystemId;
                contacts.Add(Create(gate.Id, OverviewKind.Stargate, gate.Name, "Stargate",
                    "Jump to " + destinationName, FactionAccent(catalog, system.FactionId),
                    OverviewActionFlags.Select | OverviewActionFlags.Approach | OverviewActionFlags.Warp |
                    OverviewActionFlags.Jump));
            }

            for (var i = 0; i < system.Belts.Count; i++)
            {
                var belt = system.Belts[i];
                var oreName = catalog.Items.TryGetValue(belt.OreId, out var ore) ? ore.Name : belt.OreId;
                contacts.Add(Create(belt.Id, OverviewKind.AsteroidBelt, belt.Name, "Asteroid Belt",
                    oreName, ResourceAccent,
                    OverviewActionFlags.Select | OverviewActionFlags.Approach | OverviewActionFlags.Warp));
            }

            for (var i = 0; i < state.Entities.Count; i++)
            {
                var entity = state.Entities[i];
                if (entity.Kind != EntityKind.Npc || entity.Dead) continue;
                catalog.Ships.TryGetValue(entity.ShipId, out var ship);
                var type = ship != null ? ship.Name : entity.ShipId;
                var factionName = catalog.Factions.TryGetValue(entity.FactionId, out var faction)
                    ? faction.Name
                    : entity.FactionId;
                var detail = ship != null
                    ? ship.Class + " · " + factionName
                    : factionName;
                contacts.Add(Create(entity.Id, OverviewKind.Ship, entity.Name, type, detail,
                    FactionAccent(catalog, entity.FactionId),
                    OverviewActionFlags.Select | OverviewActionFlags.Approach | OverviewActionFlags.Orbit |
                    OverviewActionFlags.Warp | OverviewActionFlags.Lock));
            }

            for (var i = 0; i < state.Asteroids.Count; i++)
            {
                var asteroid = state.Asteroids[i];
                if (asteroid.Amount <= 0d) continue;
                var oreName = catalog.Items.TryGetValue(asteroid.OreId, out var ore)
                    ? ore.Name
                    : asteroid.OreId;
                var contact = Create(asteroid.Id, OverviewKind.Asteroid, oreName, oreName,
                    asteroid.Amount.ToString("0") + " units remaining", ResourceAccent,
                    OverviewActionFlags.Select | OverviewActionFlags.Approach | OverviewActionFlags.Orbit |
                    OverviewActionFlags.Warp | OverviewActionFlags.Mine);
                contact.SizeMeters = (float)(asteroid.Radius * 2d);
                contacts.Add(contact);
            }

            var star = Create(system.Id + "_star", OverviewKind.Star, system.Name + " Star",
                system.Star.SpectralType + "-class Star", "System primary", system.Star.Color,
                OverviewActionFlags.Select | OverviewActionFlags.Approach | OverviewActionFlags.Warp);
            star.SizeMeters = (float)(system.Star.Radius * 2d);
            contacts.Add(star);

            for (var i = 0; i < system.Planets.Count; i++)
            {
                var planet = system.Planets[i];
                var planetContact = Create(planet.Id, OverviewKind.Planet, planet.Name,
                    TitleCase(planet.PlanetType), "Planet", planet.Color,
                    OverviewActionFlags.Select | OverviewActionFlags.Approach | OverviewActionFlags.Warp);
                planetContact.SizeMeters = (float)(planet.Radius * 2d);
                contacts.Add(planetContact);

                for (var moonIndex = 0; moonIndex < planet.Moons.Count; moonIndex++)
                {
                    var moon = planet.Moons[moonIndex];
                    var moonContact = Create(moon.Id, OverviewKind.Moon, moon.Name, "Moon",
                        "Natural satellite", MoonAccent,
                        OverviewActionFlags.Select | OverviewActionFlags.Approach | OverviewActionFlags.Warp);
                    moonContact.SizeMeters = (float)(moon.Radius * 2d);
                    contacts.Add(moonContact);
                }
            }

            RefreshTelemetry(contacts, session, catalog);
        }

        public static void RefreshTelemetry(List<UiOverviewContact> contacts, GameSession session,
            IContentCatalog catalog)
        {
            if (contacts == null || session == null || catalog == null) return;
            var state = session.State;
            var playerEntity = state.PlayerEntity();
            var playerPosition = playerEntity != null
                ? playerEntity.Position
                : new SimVec2(state.Player.X, state.Player.Z);
            var nextRouteGateId = NextRouteGateId(state);

            for (var i = 0; i < contacts.Count; i++)
            {
                var contact = contacts[i];
                var states = string.Equals(contact.Id, state.SelectedId, StringComparison.Ordinal)
                    ? OverviewStateFlags.Selected
                    : OverviewStateFlags.None;

                if (contact.Kind == OverviewKind.Stargate &&
                    string.Equals(contact.Id, nextRouteGateId, StringComparison.Ordinal))
                    states |= OverviewStateFlags.RouteNext;

                contact.VelocityMetersPerSecond = 0d;
                contact.Disposition = OverviewDisposition.None;

                if (contact.Kind == OverviewKind.Ship)
                {
                    var entity = state.FindEntity(contact.Id);
                    if (entity != null)
                    {
                        contact.VelocityMetersPerSecond = entity.Speed;
                        contact.Disposition = ToOverviewDisposition(
                            EntityDispositionPolicy.Evaluate(entity, state.Player, catalog,
                                playerEntity != null ? playerEntity.Id : "player"));
                        if (playerEntity != null &&
                            string.Equals(entity.LockedTargetId, playerEntity.Id, StringComparison.Ordinal))
                            states |= OverviewStateFlags.TargetingPlayer;
                        if (playerEntity != null &&
                            string.Equals(playerEntity.LockedTargetId, entity.Id, StringComparison.Ordinal))
                            states |= OverviewStateFlags.LockedByPlayer;
                        if (!string.IsNullOrEmpty(entity.MissionId))
                            states |= OverviewStateFlags.MissionObjective;
                        if (entity.Elite) states |= OverviewStateFlags.Elite;
                        if (catalog.Factions.TryGetValue(entity.FactionId, out var faction) &&
                            faction.Kind == FactionKind.Police)
                            states |= OverviewStateFlags.LawEnforcement;
                    }
                }

                contact.States = states;
                contact.DistanceMeters = TryResolvePosition(state, contact.Id, out var targetPosition)
                    ? SimVec2.Distance(playerPosition, targetPosition)
                    : -1d;
            }
        }

        private static UiOverviewContact Create(string id, OverviewKind kind, string name, string type,
            string detail, string accent, OverviewActionFlags actions)
        {
            return new UiOverviewContact
            {
                Id = id,
                Category = OverviewRules.CategoryFor(kind),
                Kind = kind,
                Name = name ?? string.Empty,
                Type = type ?? string.Empty,
                Detail = detail ?? string.Empty,
                Accent = string.IsNullOrEmpty(accent) ? "#4edbff" : accent,
                Actions = actions,
                DistanceMeters = -1d,
            };
        }

        private static OverviewDisposition ToOverviewDisposition(EntityDisposition disposition)
        {
            switch (disposition)
            {
                case EntityDisposition.Hostile: return OverviewDisposition.Hostile;
                case EntityDisposition.Friendly: return OverviewDisposition.Friendly;
                default: return OverviewDisposition.Neutral;
            }
        }

        private static StarSystemDefinition CurrentSystem(GameState state) =>
            state.Universe.Systems[state.Player.CurrentSystemId];

        private static string NextRouteGateId(GameState state)
        {
            if (string.IsNullOrEmpty(state.Player.DestinationSystemId) ||
                string.Equals(state.Player.CurrentSystemId, state.Player.DestinationSystemId, StringComparison.Ordinal))
                return string.Empty;
            var route = UniverseRoutes.FindRoute(state.Universe, state.Player.CurrentSystemId,
                state.Player.DestinationSystemId);
            if (route == null || route.Count < 2) return string.Empty;
            var gate = CurrentSystem(state).Gates.Find(value =>
                string.Equals(value.DestinationSystemId, route[1], StringComparison.Ordinal));
            return gate?.Id ?? string.Empty;
        }

        private static string FactionAccent(IContentCatalog catalog, string factionId) =>
            catalog.Factions.TryGetValue(factionId ?? string.Empty, out var faction)
                ? faction.Color
                : "#4edbff";

        private static string TitleCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Planet";
            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static bool TryResolvePosition(GameState state, string id, out SimVec2 position)
        {
            var entity = state.FindEntity(id);
            if (entity != null && !entity.Dead)
            {
                position = entity.Position;
                return true;
            }

            var asteroid = state.FindAsteroid(id);
            if (asteroid != null && asteroid.Amount > 0d)
            {
                position = asteroid.Position;
                return true;
            }

            var system = CurrentSystem(state);
            if (string.Equals(id, system.Id + "_star", StringComparison.Ordinal))
            {
                position = SimVec2.Zero;
                return true;
            }

            for (var i = 0; i < system.Stations.Count; i++)
                if (string.Equals(system.Stations[i].Id, id, StringComparison.Ordinal))
                {
                    position = system.Stations[i].Position;
                    return true;
                }

            for (var i = 0; i < system.Gates.Count; i++)
                if (string.Equals(system.Gates[i].Id, id, StringComparison.Ordinal))
                {
                    position = system.Gates[i].Position;
                    return true;
                }

            for (var i = 0; i < system.Belts.Count; i++)
                if (string.Equals(system.Belts[i].Id, id, StringComparison.Ordinal))
                {
                    position = system.Belts[i].Position;
                    return true;
                }

            for (var i = 0; i < system.Planets.Count; i++)
            {
                var planet = system.Planets[i];
                if (string.Equals(planet.Id, id, StringComparison.Ordinal))
                {
                    position = planet.Position;
                    return true;
                }

                for (var moonIndex = 0; moonIndex < planet.Moons.Count; moonIndex++)
                    if (string.Equals(planet.Moons[moonIndex].Id, id, StringComparison.Ordinal))
                    {
                        position = planet.Moons[moonIndex].Position;
                        return true;
                    }
            }

            position = SimVec2.Zero;
            return false;
        }
    }
}
