using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Starfall.App;
using Starfall.Domain;
using Starfall.Persistence;
using Starfall.Presentation;
using Starfall.Simulation;
using Starfall.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Starfall.Tests.PlayMode
{
    public sealed class SceneFlowAcceptanceTests
    {
        private const float SceneTimeoutSeconds = 15f;
        private AppRoot app;
        private string temporarySaveDirectory;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return DestroyExistingAppRoots();

            temporarySaveDirectory = Path.Combine(
                Application.temporaryCachePath,
                "StarfallPlayModeAcceptance",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporarySaveDirectory);

            Assert.That(Application.CanStreamedLevelBeLoaded("Bootstrap"), Is.True,
                "Bootstrap must be enabled in EditorBuildSettings.");
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;
            yield return WaitForScene("MainMenu");

            app = Object.FindFirstObjectByType<AppRoot>();
            Assert.That(app, Is.Not.Null, "Bootstrap must create a persistent AppRoot.");

            var saveServiceField = typeof(AppRoot).GetField("saves", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(saveServiceField, Is.Not.Null,
                "The acceptance fixture could not isolate AppRoot's save service.");
            saveServiceField.SetValue(app, new FileSaveService(temporarySaveDirectory));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return DestroyExistingAppRoots();

            if (!string.IsNullOrEmpty(temporarySaveDirectory) && Directory.Exists(temporarySaveDirectory))
                Directory.Delete(temporarySaveDirectory, true);
        }

        [UnityTest]
        public IEnumerator Bootstrap_TransitionsToMainMenu_WithLiveUiDocument()
        {
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MainMenu"));
            AssertUiDocument<MainMenuUiController>("MainMenu");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AurelianNewGame_EntersStation()
        {
            yield return StartNewGameAndAssertStation("aurelian");
        }

        [UnityTest]
        public IEnumerator KaldariNewGame_EntersStation()
        {
            yield return StartNewGameAndAssertStation("kaldari");
        }

        [UnityTest]
        public IEnumerator MeridianNewGame_EntersStation()
        {
            yield return StartNewGameAndAssertStation("meridian");
        }

        [UnityTest]
        public IEnumerator VarkhaldNewGame_EntersStation()
        {
            yield return StartNewGameAndAssertStation("varkhald");
        }

        [UnityTest]
        public IEnumerator Undock_TransitionsFromStationToSpace_WithLiveUiDocument()
        {
            yield return StartNewGameAndAssertStation("aurelian");

            app.Execute("undock");
            yield return WaitForScene("Space");
            yield return null;

            Assert.That(app.Snapshot.Docked, Is.False, "Undock must leave the player in space.");
            AssertUiDocument<SpaceHudController>("Space");
        }

        [UnityTest]
        public IEnumerator StationServices_ExposeData_AndRepairAndSaveComplete()
        {
            yield return StartNewGameAndAssertStation("aurelian");

            Assert.That(app.Snapshot.Agents.Count, Is.GreaterThan(0),
                "The station Agents service must expose at least one agent.");
            Assert.That(app.Snapshot.Market.Count, Is.GreaterThan(0),
                "The station Market service must expose tradable inventory.");
            Assert.That(app.Snapshot.Inventory.Count, Is.GreaterThan(0),
                "The station Fitting service must expose hangar inventory.");
            Assert.That(app.Snapshot.Ships.Count, Is.GreaterThan(0),
                "The station Ships service must expose the active ship.");
            Assert.That(app.Snapshot.EmpireName, Is.Not.Empty,
                "The loyalty service must identify the pilot's empire.");

            var session = GetSession();
            var player = session.State.Player;
            var ship = session.State.Player.ActiveShip();
            Assert.That(ship, Is.Not.Null, "A new pilot must own an active ship.");
            Assert.That(app.Snapshot.Market.Any(item => item.Id == "sell-module|" + ModuleIds.MiningLaser), Is.True,
                "Hangar modules must be exposed as explicit Market sell actions.");
            Assert.That(app.Snapshot.Inventory.Any(item => item.Id == "fit-module|" + ModuleIds.MiningLaser), Is.True,
                "Hangar modules must be exposed as explicit Fitting actions.");
            Assert.That(app.Snapshot.Inventory.Any(item => item.Id.StartsWith("unfit|", StringComparison.Ordinal)), Is.True,
                "Installed modules must be exposed as explicit Unfit actions.");

            player.Cargo[ItemIds.Ferrite] = 5d;
            player.LoyaltyPoints[player.EmpireId] = 100;
            var fullShield = ship.Shield;
            var fullArmor = ship.Armor;
            var fullHull = ship.Hull;
            ship.Shield = 0d;
            ship.Armor = 0d;
            ship.Hull = 1d;

            app.Execute("repair");
            yield return WaitForCondition(
                () => ship.Shield == fullShield && ship.Armor == fullArmor && ship.Hull == fullHull,
                "Station repair did not restore all three damage layers.");
            yield return WaitForCondition(
                () => app.Snapshot.Market.Any(item => item.Id == "sell-item|" + ItemIds.Ferrite) &&
                      app.Snapshot.LoyaltyPoints == 100,
                "Station lists did not refresh explicit cargo-sale and loyalty state.");
            Assert.That(app.Snapshot.Inventory.Any(item => item.Id == ItemIds.Ferrite ||
                item.Id == "sell-item|" + ItemIds.Ferrite), Is.False,
                "Cargo must not be mixed into the Fitting list.");

            var unfitAction = app.Snapshot.Inventory.First(item =>
                item.Id.StartsWith("unfit|", StringComparison.Ordinal)).Id;
            var unfitParts = unfitAction.Split('|');
            Assert.That(unfitParts.Length, Is.EqualTo(4));
            Assert.That(unfitParts[1], Is.EqualTo(ship.InstanceId));
            int unfitIndex;
            Assert.That(int.TryParse(unfitParts[3], out unfitIndex), Is.True);
            var unfitSlots = unfitParts[2] == "high" ? ship.Fitting.High :
                unfitParts[2] == "mid" ? ship.Fitting.Mid : ship.Fitting.Low;
            var unfitModuleId = unfitSlots[unfitIndex];
            var fittedBefore = ship.Fitting.All().Count(id => id == unfitModuleId);
            player.Hangar.TryGetValue(unfitModuleId, out var hangarBeforeUnfit);

            app.Execute("fit", unfitAction);
            yield return WaitForCondition(
                () => ship.Fitting.All().Count(id => id == unfitModuleId) == fittedBefore - 1 &&
                      player.Hangar.TryGetValue(unfitModuleId, out var count) && count == hangarBeforeUnfit + 1,
                "Encoded Unfit action did not move the installed module into the hangar.");
            app.Execute("fit", "fit-module|" + unfitModuleId);
            yield return WaitForCondition(
                () => ship.Fitting.All().Count(id => id == unfitModuleId) == fittedBefore,
                "Encoded hangar Fit action did not use the first compatible free slot.");

            var creditsBeforeModuleSale = player.Credits;
            app.Execute("market", "sell-module|" + ModuleIds.MiningLaser);
            yield return WaitForCondition(
                () => !player.Hangar.ContainsKey(ModuleIds.MiningLaser) && player.Credits > creditsBeforeModuleSale,
                "Encoded hangar-module sell action did not complete.");
            var creditsBeforeModuleBuy = player.Credits;
            app.Execute("market", ModuleIds.MiningLaser);
            yield return WaitForCondition(
                () => player.Hangar.TryGetValue(ModuleIds.MiningLaser, out var count) && count == 1 &&
                      player.Credits < creditsBeforeModuleBuy,
                "Ordinary catalog action ID no longer purchases a module.");

            var creditsBeforeCargoSale = player.Credits;
            app.Execute("market", "sell-item|" + ItemIds.Ferrite);
            yield return WaitForCondition(
                () => !player.Cargo.ContainsKey(ItemIds.Ferrite) && player.Credits > creditsBeforeCargoSale,
                "Encoded cargo sell action did not complete.");

            var stationController = Object.FindFirstObjectByType<StationUiController>();
            Assert.That(stationController, Is.Not.Null);
            var loyaltyButton = stationController.GetComponent<UIDocument>()
                .rootVisualElement.Q<Button>("lp-exchange");
            Assert.That(loyaltyButton, Is.Not.Null,
                "LP STORE exchange button must have a stable name.");
            using (var click = ClickEvent.GetPooled())
            {
                click.target = loyaltyButton;
                loyaltyButton.SendEvent(click);
            }
            yield return WaitForCondition(
                () => player.LoyaltyPoints[player.EmpireId] == 0 &&
                      player.Hangar.TryGetValue(ModuleIds.DamageAmp, out var count) && count == 1,
                "LP STORE button did not exchange 100 LP for a Damage Amp.");
            using (var click = ClickEvent.GetPooled())
            {
                click.target = loyaltyButton;
                loyaltyButton.SendEvent(click);
            }
            yield return WaitForCondition(
                () => app.Snapshot.Log.Any(line => line.Contains("requires 100 LP")),
                "Insufficient-LP feedback did not reach the station log.");

            var isolatedSaves = new FileSaveService(temporarySaveDirectory);
            var slotPath = isolatedSaves.GetSlotPath(SaveSlot.Slot1);
            app.Execute("save");
            yield return WaitForCondition(
                () => File.Exists(slotPath),
                "Manual save did not create isolated slot1.json.");
            Assert.That(app.Snapshot.Log.Any(line => line.Contains("Game saved to slot1.")), Is.True,
                "The station log must acknowledge the completed manual save.");
        }

        [UnityTest]
        public IEnumerator SpaceCommands_ToggleOverlays_AndDriveSelectedOverviewTarget()
        {
            yield return StartNewGameAndAssertStation("aurelian");
            app.Execute("undock");
            yield return WaitForScene("Space");
            yield return WaitForCondition(
                () => !app.Snapshot.Docked && app.Snapshot.Overview.Count > 0,
                "Space overview did not populate after undocking.");

            app.Execute("map");
            yield return WaitForCondition(() => app.Snapshot.MapVisible, "Starmap did not open.");
            app.Execute("journal");
            yield return WaitForCondition(() => app.Snapshot.JournalVisible, "Journal did not open.");

            var session = GetSession();
            var player = session.State.PlayerEntity();
            var target = session.State.Entities.FirstOrDefault(entity => entity.Kind == EntityKind.Npc);
            Assert.That(player, Is.Not.Null, "Undocking must spawn the player entity.");
            Assert.That(target, Is.Not.Null, "The generated system must contain an NPC overview target.");
            Assert.That(app.Snapshot.Overview.Any(item => item.Id == target.Id), Is.True,
                "The selected NPC must originate from the live overview snapshot.");

            target.Position = player.Position + new SimVec2(1000d, 0d);
            app.Execute("select", target.Id);
            yield return WaitForCondition(
                () => app.Snapshot.SelectedId == target.Id,
                "Overview selection did not reach the simulation/UI snapshot.");

            app.Execute("approach", target.Id);
            yield return WaitForCondition(
                () => player.Movement == MovementMode.Approach,
                "Approach command did not change player movement mode.");

            target.Position = player.Position + new SimVec2(10d, 0d);
            app.Execute("lock", target.Id);
            yield return WaitForCondition(
                () => player.LockedTargetId == target.Id,
                "Lock command did not lock the selected overview target.");

            Assert.That(player.Modules.Count, Is.GreaterThan(0),
                "The starter ship must expose at least one fitted module.");
            app.Execute("module", "0");
            yield return WaitForCondition(
                () => player.Modules[0].Active,
                "Module command did not activate the first fitted module.");

            app.Execute("warp", target.Id);
            yield return WaitForCondition(
                () => player.Movement == MovementMode.WarpAlign ||
                      player.Movement == MovementMode.WarpCruise ||
                      player.Movement == MovementMode.WarpDecelerate,
                "Warp command did not enter a warp movement phase.");

            app.Execute("map");
            app.Execute("journal");
            yield return WaitForCondition(
                () => !app.Snapshot.MapVisible && !app.Snapshot.JournalVisible,
                "Map and journal toggles did not close their overlays.");
        }

        [UnityTest]
        public IEnumerator NearbyStation_DockCommand_ReturnsSpaceToStation()
        {
            yield return StartNewGameAndAssertStation("aurelian");
            app.Execute("undock");
            yield return WaitForScene("Space");
            yield return WaitForCondition(() => !app.Snapshot.Docked, "Undock did not complete.");

            var session = GetSession();
            var player = session.State.PlayerEntity();
            var system = session.State.Universe.Systems[session.State.Player.CurrentSystemId];
            Assert.That(player, Is.Not.Null, "Undocking must spawn the player entity.");
            Assert.That(system.Stations.Count, Is.GreaterThan(0),
                "The starting system must have a dockable station.");
            var station = system.Stations[0];
            player.Position = station.Position;
            session.State.Player.X = station.Position.X;
            session.State.Player.Z = station.Position.Z;

            app.Execute("select", station.Id);
            app.Execute("dock", station.Id);
            yield return WaitForScene("Station");
            yield return WaitForCondition(() => app.Snapshot.Docked, "Dock command did not complete.");

            Assert.That(GetSession().State.Player.DockedAtStationId, Is.EqualTo(station.Id));
            AssertUiDocument<StationUiController>("Station");
        }

        [UnityTest]
        public IEnumerator ManualSave_ContinueGame_RoundTripsPlayerState()
        {
            yield return StartNewGameAndAssertStation("kaldari");

            var sessionBeforeSave = GetSession();
            const string expectedPilot = "Roundtrip Pilot";
            const long expectedCredits = 7654321L;
            sessionBeforeSave.State.Player.Name = expectedPilot;
            sessionBeforeSave.State.Player.Credits = expectedCredits;

            var isolatedSaves = new FileSaveService(temporarySaveDirectory);
            var slotPath = isolatedSaves.GetSlotPath(SaveSlot.Slot1);
            app.Execute("save");
            yield return WaitForCondition(
                () => File.Exists(slotPath),
                "Manual save did not create slot1 for the roundtrip.");

            DeleteIfPresent(isolatedSaves.GetSlotPath(SaveSlot.Auto));
            DeleteIfPresent(isolatedSaves.GetBackupPath(SaveSlot.Auto));
            sessionBeforeSave.State.Player.Name = "Unsaved Mutation";
            sessionBeforeSave.State.Player.Credits = 1L;

            app.ContinueGame();
            yield return WaitForCondition(
                () => app.Snapshot.PilotName == expectedPilot && app.Snapshot.Credits == expectedCredits,
                "ContinueGame did not restore the manual slot1 player state.");

            var restoredSession = GetSession();
            Assert.That(ReferenceEquals(restoredSession, sessionBeforeSave), Is.False,
                "ContinueGame must replace the live simulation session.");
            Assert.That(restoredSession.State.Player.EmpireId, Is.EqualTo("kaldari"));
            Assert.That(restoredSession.State.Player.Name, Is.EqualTo(expectedPilot));
            Assert.That(restoredSession.State.Player.Credits, Is.EqualTo(expectedCredits));
            Assert.That(File.Exists(slotPath), Is.True, "ContinueGame must not consume the manual save.");
        }

        private IEnumerator StartNewGameAndAssertStation(string empireId)
        {
            var pilotName = "Acceptance " + empireId;
            app.StartNewGame(pilotName, empireId);
            yield return WaitForScene("Station");
            yield return null;

            Assert.That(app.Snapshot.PilotName, Is.EqualTo(pilotName));
            Assert.That(app.Snapshot.EmpireId, Is.EqualTo(empireId));
            Assert.That(app.Snapshot.Docked, Is.True, "A new pilot must start docked.");
            AssertUiDocument<StationUiController>("Station");
        }

        private GameSession GetSession()
        {
            var sessionField = typeof(AppRoot).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(sessionField, Is.Not.Null,
                "The acceptance fixture could not inspect AppRoot's live session.");
            var session = sessionField.GetValue(app) as GameSession;
            Assert.That(session, Is.Not.Null, "AppRoot has no active GameSession.");
            return session;
        }

        private static void AssertUiDocument<TController>(string sceneName)
            where TController : Component
        {
            var controller = Object.FindFirstObjectByType<TController>();
            Assert.That(controller, Is.Not.Null, $"{sceneName} must contain {typeof(TController).Name}.");

            var document = controller.GetComponent<UIDocument>();
            Assert.That(document, Is.Not.Null, $"{sceneName} controller must have a UIDocument.");
            Assert.That(document.visualTreeAsset, Is.Not.Null,
                $"{sceneName} UIDocument must reference a UXML asset.");
            Assert.That(document.rootVisualElement, Is.Not.Null,
                $"{sceneName} UIDocument must create a visual tree.");
            Assert.That(document.rootVisualElement.childCount, Is.GreaterThan(0),
                $"{sceneName} visual tree must not be empty.");
        }

        private static IEnumerator WaitForScene(string sceneName)
        {
            var deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
            while (SceneManager.GetActiveScene().name != sceneName && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(sceneName),
                $"Timed out waiting for scene '{sceneName}'.");
        }

        private static IEnumerator WaitForCondition(
            Func<bool> condition,
            string failureMessage,
            float timeoutSeconds = 5f)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(condition(), Is.True, failureMessage);
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static IEnumerator DestroyExistingAppRoots()
        {
            var roots = Object.FindObjectsByType<AppRoot>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (var root in roots)
            {
                if (root) Object.Destroy(root.gameObject);
            }

            if (roots.Length > 0) yield return null;
        }
    }

    public sealed class ShipPrefabAcceptanceTests
    {
        [UnityTest]
        public IEnumerator VisualCatalog_ContainsEighteenCompleteShipPrefabs()
        {
            var catalog = Resources.Load<VisualCatalogAsset>("VisualCatalog");
            Assert.That(catalog, Is.Not.Null, "Resources/VisualCatalog must be loadable at runtime.");
            Assert.That(catalog.Ships.Count, Is.EqualTo(18),
                "The remake must ship all 18 stable ship visuals.");

            var stableIds = new HashSet<string>(StringComparer.Ordinal);
            var instances = new List<GameObject>(catalog.Ships.Count);
            try
            {
                foreach (var entry in catalog.Ships)
                {
                    Assert.That(entry, Is.Not.Null, "VisualCatalog contains a null ship entry.");
                    Assert.That(entry.StableId, Is.Not.Empty);
                    Assert.That(stableIds.Add(entry.StableId), Is.True,
                        $"Duplicate ship stable ID '{entry.StableId}'.");
                    Assert.That(entry.Prefab, Is.Not.Null,
                        $"Ship '{entry.StableId}' has no prefab.");

                    var instance = Object.Instantiate(entry.Prefab);
                    instances.Add(instance);
                    instance.name = "Acceptance_" + entry.StableId;

                    var missingScripts = FindMissingScripts(instance);
                    var identity = instance.GetComponent<ShipVisualIdentity>();
                    Assert.That(missingScripts, Is.Empty,
                        $"Ship '{entry.StableId}' contains Missing Script components: {string.Join(", ", missingScripts)}");
                    Assert.That(instance.GetComponentInChildren<Collider>(true), Is.Not.Null,
                        $"Ship '{entry.StableId}' must have a collider.");
                    Assert.That(instance.GetComponentsInChildren<Renderer>(true), Is.Not.Empty,
                        $"Ship '{entry.StableId}' must contain renderable 3D geometry.");
                    Assert.That(identity, Is.Not.Null,
                        $"Ship '{entry.StableId}' must have ShipVisualIdentity.");
                    Assert.That(identity.StableId, Is.EqualTo(entry.StableId));
                    Assert.That(identity.WeaponHardpoint, Is.Not.Null,
                        $"Ship '{entry.StableId}' must expose a weapon hardpoint.");
                    Assert.That(identity.EngineHardpoint, Is.Not.Null,
                        $"Ship '{entry.StableId}' must expose an engine hardpoint.");
                    Assert.That(identity.WeaponHardpoint.name, Is.EqualTo("WeaponHardpoint"));
                    Assert.That(identity.EngineHardpoint.name, Is.EqualTo("EngineHardpoint"));
                }
            }
            finally
            {
                foreach (var instance in instances)
                {
                    if (instance) Object.Destroy(instance);
                }
            }

            yield return null;
        }

        private static string[] FindMissingScripts(GameObject root)
        {
            var missing = new List<string>();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.GetComponents<Component>().Any(component => component == null))
                    missing.Add(HierarchyPath(root.transform, transform));
            }

            return missing.ToArray();
        }

        private static string HierarchyPath(Transform root, Transform current)
        {
            var parts = new Stack<string>();
            while (current)
            {
                parts.Push(current.name);
                if (current == root) break;
                current = current.parent;
            }

            return string.Join("/", parts);
        }
    }
}
