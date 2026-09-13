using System.Linq;
using NUnit.Framework;
using Starfall.Content;
using Starfall.Domain;
using Starfall.Simulation;

namespace Starfall.Tests.EditMode.Gameplay
{
    /// <summary>
    /// Save-payload robustness: fittings referencing module ids a future
    /// catalog no longer ships must not KeyNotFoundException the first tick.
    /// </summary>
    public sealed class SaveRobustnessTests
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
        public void UnknownFittedModuleIds_AreStrippedInsteadOfCrashing()
        {
            var first = new GameSession(universe, catalog, "Robustness Pilot", FactionIds.Aurelian);
            var ship = first.State.Player.ActiveShip();
            var midIndex = ship.Fitting.Mid.IndexOf(null);
            Assert.That(midIndex, Is.GreaterThanOrEqualTo(0));
            ship.Fitting.Mid[midIndex] = "module_from_the_future";

            GameSession reloaded;
            Assert.DoesNotThrow(() =>
            {
                reloaded = new GameSession(universe, catalog, first.State.Player,
                    first.State.SimulationTime, first.State.RngState, first.State.NextEntityId);
                reloaded.Enqueue(new GameCommand(GameCommandType.Undock));
                reloaded.AdvanceFrame(GameSession.FixedStepSeconds);
            }, "Loading a save with dropped catalog ids must not throw.");

            reloaded = new GameSession(universe, catalog, first.State.Player,
                first.State.SimulationTime, first.State.RngState, first.State.NextEntityId);
            reloaded.Enqueue(new GameCommand(GameCommandType.Undock));
            reloaded.AdvanceFrame(GameSession.FixedStepSeconds);
            var entity = reloaded.State.PlayerEntity();
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity.Modules.Select(runtime => runtime.ModuleId),
                Has.None.EqualTo("module_from_the_future"),
                "The ghost module must be gone from the live entity.");
        }
    }
}
