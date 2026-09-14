using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Starfall.App;
using Starfall.Content;
using Starfall.Domain;
using Starfall.Simulation;
using Starfall.UI;

namespace Starfall.Tests.EditMode.UI
{
    /// <summary>
    /// Spawn/Despawn/Death stay on the light UI refresh path (M6: no
    /// market/skill/starmap rebuild during NPC fights), so the overview ship
    /// rows must reconcile structurally or the list ghosts the dead and misses
    /// warp-ins such as the Directorate response.
    /// </summary>
    public sealed class OverviewContactBuilderTests
    {
        private GeneratedUniverse universe;
        private GameContentCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            universe = new UniverseGenerator().Generate(12345);
            catalog = new GameContentCatalog();
        }

        [Test]
        public void ReconcileShips_AddsWarpIns_AndDropsDeadNpcs()
        {
            var session = CreateUndockedSession();
            var contacts = new List<UiOverviewContact>();
            OverviewContactBuilder.Rebuild(contacts, session, catalog);

            // A warp-in while the overview sits on the light path: the player is
            // flagged criminal and the Directorate response arrives next tick.
            var npcsBefore = session.State.Entities.Count(value =>
                value.Kind == EntityKind.Npc && !value.Dead);
            session.State.Player.CriminalTimer = 10d;
            session.AdvanceFrame(GameSession.FixedStepSeconds);
            var npcsAfter = session.State.Entities.Count(value =>
                value.Kind == EntityKind.Npc && !value.Dead);
            Assert.That(npcsAfter, Is.GreaterThan(npcsBefore),
                "Fixture: the criminal flag must spawn Directorate responders.");

            // A kill whose entity is still in the list: FindEntity treats "dead"
            // and "removed" identically, so this exercises the ghost-row drop.
            var dead = session.State.Entities.First(value => value.Kind == EntityKind.Npc);
            dead.Dead = true;

            OverviewContactBuilder.ReconcileShips(contacts, session.State, catalog);

            var shipIds = contacts.Where(value => value.Kind == OverviewKind.Ship)
                .Select(value => value.Id).ToList();
            Assert.That(shipIds, Is.EquivalentTo(session.State.Entities
                    .Where(value => value.Kind == EntityKind.Npc && !value.Dead)
                    .Select(value => value.Id)),
                "Ship rows must exactly mirror the live NPC set after a light-path reconcile.");
        }

        [Test]
        public void ReconcileShips_WithoutChanges_KeepsRowsStable()
        {
            var session = CreateUndockedSession();
            var contacts = new List<UiOverviewContact>();
            OverviewContactBuilder.Rebuild(contacts, session, catalog);
            var before = contacts.Select(value => value.Id).ToList();

            OverviewContactBuilder.ReconcileShips(contacts, session.State, catalog);

            Assert.That(contacts.Select(value => value.Id), Is.EqualTo(before),
                "A no-op reconcile must not reshuffle or duplicate rows.");
        }

        private GameSession CreateUndockedSession()
        {
            var session = new GameSession(universe, catalog, "Overview Pilot", FactionIds.Aurelian);
            session.Enqueue(new GameCommand(GameCommandType.Undock));
            session.AdvanceFrame(GameSession.FixedStepSeconds);
            return session;
        }
    }
}
