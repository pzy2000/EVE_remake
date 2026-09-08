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
        private const string OverviewPreferencePrefix = "starfall.overview.v1";
        private static readonly string[] OverviewPreferenceKeys =
        {
            OverviewPreferencePrefix + ".active",
            OverviewPreferencePrefix + ".General.sort-column",
            OverviewPreferencePrefix + ".General.sort-direction",
            OverviewPreferencePrefix + ".Combat.sort-column",
            OverviewPreferencePrefix + ".Combat.sort-direction",
            OverviewPreferencePrefix + ".Mining.sort-column",
            OverviewPreferencePrefix + ".Mining.sort-direction",
            OverviewPreferencePrefix + ".Travel.sort-column",
            OverviewPreferencePrefix + ".Travel.sort-direction",
            OverviewPreferencePrefix + ".All.sort-column",
            OverviewPreferencePrefix + ".All.sort-direction",
        };

        private AppRoot app;
        private string temporarySaveDirectory;
        private readonly Dictionary<string, int> savedOverviewPreferences = new(StringComparer.Ordinal);
        private readonly HashSet<string> missingOverviewPreferences = new(StringComparer.Ordinal);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return DestroyExistingAppRoots();
            CaptureAndClearOverviewPreferences();

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
            RestoreOverviewPreferences();

            if (!string.IsNullOrEmpty(temporarySaveDirectory) && Directory.Exists(temporarySaveDirectory))
                Directory.Delete(temporarySaveDirectory, true);
        }

        [UnityTest]
        public IEnumerator Bootstrap_TransitionsToMainMenu_WithLiveUiDocument()
        {
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MainMenu"));
            AssertUiDocument<MainMenuUiController>("MainMenu");
            yield return null;

            var controller = Object.FindFirstObjectByType<MainMenuUiController>();
            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var pilotInput = root.Q<TextField>("pilot-name")
                .Q<VisualElement>(className: "unity-base-text-field__input");
            Assert.That(pilotInput, Is.Not.Null, "Pilot callsign must expose a styled text input.");
            AssertReadableText(pilotInput, "MainMenu/Pilot callsign");
            Assert.That(RelativeLuminance(EffectiveBackground(pilotInput)), Is.LessThan(0.08f),
                "Pilot callsign input must use the dark game theme instead of Unity's light default.");

            AssertReadableText(root.Q<Label>("empire-description"), "MainMenu/Muted description");
            AssertReadableText(root.Q<Button>("launch"), "MainMenu/Primary button");
            AssertReadableText(root.Q<Button>("empire-aurelian"), "MainMenu/Chosen empire");
        }

        [UnityTest]
        public IEnumerator LegacyImportFailure_ShowsSpecificReasonOnMainMenu()
        {
            const string reason = "Legacy save JSON root must be an object";
            app.OnAndroidLegacyDocumentPickerError("invalid_json:" + reason);
            yield return null;

            var controller = Object.FindFirstObjectByType<MainMenuUiController>();
            Assert.That(controller, Is.Not.Null);
            var status = controller.GetComponent<UIDocument>()
                .rootVisualElement.Q<Label>("legacy-import-status");
            Assert.That(status, Is.Not.Null, "MainMenu must expose a stable legacy import status label.");
            Assert.That(status.text, Is.EqualTo("Legacy import failed: " + reason));
            Assert.That(status.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(status.ClassListContains("danger"), Is.True);
            Assert.That(app.LegacyImportStatusIsError, Is.True);
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
            Assert.That(app.Snapshot.Market.All(item => !item.Title.Contains(" ISK base")), Is.True,
                "Market rows must expose authoritative station prices, not hidden base prices.");
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

            var firstAgent = app.Snapshot.Agents.First(item => item.Enabled);
            app.Execute("agent", firstAgent.Id);
            yield return WaitForCondition(
                () => !string.IsNullOrEmpty(app.Snapshot.OfferedMissionId),
                "Talking to an agent did not open a reviewable mission offer.");
            Assert.That(app.Snapshot.OfferedMissionDetail, Does.Contain("Reward"));
            Assert.That(app.Snapshot.OfferedMissionDetail, Does.Contain("jumps"));
            var stationRoot = Object.FindFirstObjectByType<StationUiController>()
                .GetComponent<UIDocument>().rootVisualElement;
            Assert.That(stationRoot.Q<VisualElement>("mission-offer-overlay").resolvedStyle.display,
                Is.EqualTo(DisplayStyle.Flex));
            app.Execute("mission-accept", app.Snapshot.OfferedMissionId);
            yield return WaitForCondition(
                () => string.IsNullOrEmpty(app.Snapshot.OfferedMissionId) &&
                      app.Snapshot.Missions.Any(item => item.PrimaryAction == "mission-route"),
                "Accepting the offer did not create an active, routable mission.");
            Assert.That(GetSession().State.Player.DestinationSystemId, Is.Not.Empty);

            app.Execute("fit", "fit-module|" + ModuleIds.MiningLaser);
            yield return WaitForCondition(
                () => ship.Fitting.High.Contains(ModuleIds.MiningLaser) &&
                      !player.Hangar.ContainsKey(ModuleIds.MiningLaser),
                "One-tap mining setup did not swap the laser into a full high-slot rack.");
            var miningSlot = ship.Fitting.High.FindIndex(value => value == ModuleIds.MiningLaser);
            app.Execute("fit", $"unfit|{ship.InstanceId}|high|{miningSlot}");
            yield return WaitForCondition(
                () => player.Hangar.TryGetValue(ModuleIds.MiningLaser, out var count) && count == 1,
                "Mining laser did not return to the hangar after the swap test.");

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
            yield return null;
            Assert.That(loyaltyButton.enabledSelf, Is.False,
                "LP exchange must disable immediately when the pilot cannot afford it.");
            Assert.That(loyaltyButton.tooltip, Does.Contain("Need 100 more LP"),
                "A disabled LP action must explain why it is unavailable.");

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
            var pausedAt = GetSession().State.SimulationTime;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(GetSession().State.SimulationTime, Is.EqualTo(pausedAt),
                "Opening the starmap must pause the single-player simulation.");
            app.Execute("journal");
            yield return WaitForCondition(
                () => app.Snapshot.JournalVisible && !app.Snapshot.MapVisible,
                "Journal did not replace the starmap as the topmost full-screen overlay.");
            app.Execute("journal");
            yield return WaitForCondition(() => !app.Snapshot.JournalVisible,
                "Journal did not close before gameplay commands resumed.");

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

            app.Execute("journal");
            yield return WaitForCondition(() => app.Snapshot.JournalVisible,
                "Journal did not reopen after gameplay commands.");
            app.Execute("journal");
            yield return WaitForCondition(
                () => !app.Snapshot.MapVisible && !app.Snapshot.JournalVisible,
                "Map and journal toggles did not close their overlays.");
        }

        [UnityTest]
        public IEnumerator EveStyleOverview_TypedContactsPresetsAndUiStayInSync()
        {
            yield return StartNewGameAndAssertStation("aurelian");
            app.Execute("undock");
            yield return WaitForScene("Space");
            yield return WaitForCondition(
                () => !app.Snapshot.Docked && app.Snapshot.Overview.Count > 0,
                "Typed Overview contacts did not populate after undocking.");

            var session = GetSession();
            var state = session.State;
            var player = state.PlayerEntity();
            Assert.That(player, Is.Not.Null, "Undocking must create the player ship entity.");
            Assert.That(app.Snapshot.Overview.Any(contact => contact.Id == player.Id), Is.False,
                "The player's own ship must never be listed as an Overview contact.");

            var requiredKinds = new[]
            {
                OverviewKind.Ship,
                OverviewKind.Station,
                OverviewKind.Stargate,
                OverviewKind.AsteroidBelt,
                OverviewKind.Asteroid,
                OverviewKind.Star,
                OverviewKind.Planet,
                OverviewKind.Moon,
            };
            foreach (var kind in requiredKinds)
            {
                Assert.That(app.Snapshot.Overview.Any(contact => contact.Kind == kind), Is.True,
                    $"The seed-12345 starting system must expose a typed {kind} contact.");
            }

            foreach (var contact in app.Snapshot.Overview)
            {
                Assert.That(contact, Is.Not.Null);
                Assert.That(contact.Id, Is.Not.Empty);
                Assert.That(contact.Name, Is.Not.Empty, $"Overview contact '{contact.Id}' has no name.");
                Assert.That(contact.Category, Is.EqualTo(OverviewRules.CategoryFor(contact.Kind)),
                    $"Overview contact '{contact.Id}' has an inconsistent category.");
                Assert.That(contact.DistanceMeters, Is.GreaterThanOrEqualTo(0d),
                    $"Overview contact '{contact.Id}' has unresolved distance telemetry.");
            }

            foreach (OverviewPresetId preset in Enum.GetValues(typeof(OverviewPresetId)))
            {
                foreach (var contact in app.Snapshot.Overview)
                {
                    Assert.That(OverviewRules.IsVisible(preset, contact),
                        Is.EqualTo(ExpectedOverviewVisibility(preset, contact)),
                        $"{preset} visibility disagrees with the acceptance matrix for " +
                        $"'{contact.Id}' ({contact.Kind}, {contact.Disposition}, {contact.States}).");
                }
            }

            var routeSystemId = state.Universe.Adjacency[state.Player.CurrentSystemId].First();
            app.Execute("destination", routeSystemId);
            yield return WaitForCondition(
                () => app.Snapshot.Overview.Any(contact =>
                    (contact.States & OverviewStateFlags.RouteNext) != 0),
                "Setting a destination did not mark the next stargate in Overview.");
            Assert.That(app.Snapshot.RouteSummary, Does.Contain("NEXT JUMP"));

            var target = state.Entities.FirstOrDefault(entity => entity.Kind == EntityKind.Npc && !entity.Dead);
            Assert.That(target, Is.Not.Null, "The generated system must contain a live NPC ship.");
            target.Position = player.Position + new SimVec2(10d, 0d);

            app.Execute("select", target.Id);
            yield return WaitForCondition(
                () => HasOverviewState(target.Id, OverviewStateFlags.Selected),
                "Selecting an Overview ship did not set its Selected presentation flag.");
            Assert.That(app.Snapshot.SelectedHasHealth, Is.True,
                "Selecting a ship must expose target health telemetry.");
            Assert.That(app.Snapshot.CargoCapacity, Is.GreaterThan(0f),
                "The space HUD must expose cargo capacity.");

            app.Execute("lock", target.Id);
            yield return WaitForCondition(
                () => HasOverviewState(target.Id, OverviewStateFlags.LockedByPlayer),
                "Locking an Overview ship did not set its LockedByPlayer presentation flag.");

            var controller = Object.FindFirstObjectByType<SpaceHudController>();
            Assert.That(controller, Is.Not.Null, "Space must contain the Overview UI controller.");
            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var overviewList = root.Q<ListView>("overview-list");
            Assert.That(overviewList, Is.Not.Null,
                "The Overview must use the stable 'overview-list' virtualized ListView.");
            Assert.That(overviewList.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));
            yield return WaitForCondition(
                () => overviewList.itemsSource != null,
                "Overview ListView never received its typed itemsSource.");
            Assert.That(overviewList.itemsSource.Cast<object>().All(item => item is UiOverviewContact), Is.True,
                "Overview ListView itemsSource must contain only typed UiOverviewContact rows.");
            yield return AssertOverviewTextContrast(root);

            Assert.That(root.Q<Button>("approach").enabledSelf, Is.True);
            Assert.That(root.Q<Button>("orbit").enabledSelf, Is.True);
            Assert.That(root.Q<Button>("warp").enabledSelf, Is.True);
            Assert.That(root.Q<Button>("lock").enabledSelf, Is.True);
            Assert.That(root.Q<Button>("dock").enabledSelf, Is.False,
                "A selected ship must not expose dock or jump as a valid Overview action.");

            var dockButton = root.Q<Button>("dock");
            var stationContact = app.Snapshot.Overview.First(contact =>
                contact.Kind == OverviewKind.Station && contact.DistanceMeters > 40d);
            app.Execute("select", stationContact.Id);
            yield return WaitForCondition(
                () => app.Snapshot.SelectedId == stationContact.Id && !dockButton.enabledSelf,
                "A station outside docking range incorrectly exposed Dock as executable.");
            Assert.That(dockButton.text, Is.EqualTo("DOCK/JUMP"));
            Assert.That(root.Q<Button>("orbit").enabledSelf, Is.False);
            Assert.That(root.Q<Button>("lock").enabledSelf, Is.False);

            var system = state.Universe.Systems[state.Player.CurrentSystemId];
            player.Position = system.Stations.First(value => value.Id == stationContact.Id).Position;
            yield return WaitForCondition(
                () => dockButton.enabledSelf,
                "Moving within 40 m did not enable Dock for the selected station.");

            var gateContact = app.Snapshot.Overview.First(contact =>
                contact.Kind == OverviewKind.Stargate && contact.DistanceMeters > 35d);
            app.Execute("select", gateContact.Id);
            yield return WaitForCondition(
                () => app.Snapshot.SelectedId == gateContact.Id && !dockButton.enabledSelf,
                "A stargate outside jump range incorrectly exposed Jump as executable.");
            player.Position = system.Gates.First(value => value.Id == gateContact.Id).Position;
            yield return WaitForCondition(
                () => dockButton.enabledSelf,
                "Moving within 35 m did not enable Jump for the selected stargate.");

            yield return AssertOverviewPresetUi(root, overviewList, OverviewPresetId.Mining, "mining");
            yield return AssertOverviewPresetUi(root, overviewList, OverviewPresetId.Travel, "travel");
            yield return AssertOverviewPresetUi(root, overviewList, OverviewPresetId.All, "all");

            var nameSort = root.Q<Button>("overview-sort-name");
            Assert.That(nameSort, Is.Not.Null, "The NAME column must expose a stable sort button.");
            SendClick(nameSort);
            yield return WaitForCondition(
                () => nameSort.ClassListContains("sorted") && nameSort.text.Contains("↑") &&
                      IsOverviewSorted(overviewList, OverviewSortColumn.Name,
                          OverviewSortDirection.Ascending),
                "Clicking NAME did not produce a marked, ascending stable sort.");
            var ascendingIds = OverviewItems(overviewList).Select(contact => contact.Id).ToArray();

            SendClick(nameSort);
            yield return WaitForCondition(
                () => nameSort.ClassListContains("sorted") && nameSort.text.Contains("↓") &&
                      IsOverviewSorted(overviewList, OverviewSortColumn.Name,
                          OverviewSortDirection.Descending),
                "Clicking NAME twice did not produce a marked, descending stable sort.");
            var descendingIds = OverviewItems(overviewList).Select(contact => contact.Id).ToArray();
            Assert.That(ascendingIds.Length, Is.GreaterThan(1));
            Assert.That(ascendingIds.SequenceEqual(descendingIds), Is.False,
                "Reversing a populated NAME sort must visibly change the row order.");

            var starmapList = root.Q<ScrollView>("starmap-list");
            Assert.That(starmapList, Is.Not.Null);
            Assert.That(starmapList.contentContainer.childCount, Is.GreaterThan(0));
            var firstStarmapRow = starmapList.contentContainer[0];
            yield return new WaitForSecondsRealtime(0.25f);
            CollectionAssert.AreEqual(descendingIds,
                OverviewItems(overviewList).Select(contact => contact.Id).ToArray(),
                "10 Hz telemetry refresh must not destabilize a name-sorted Overview.");
            Assert.That(starmapList.contentContainer[0], Is.SameAs(firstStarmapRow),
                "10 Hz Overview telemetry must not rebuild unchanged hidden starmap buttons.");

            var depletedAsteroid = state.Asteroids.First(asteroid => asteroid.Amount > 0d);
            var deadNpcId = target.Id;
            var depletedAsteroidId = depletedAsteroid.Id;
            app.Execute("select", deadNpcId);
            yield return WaitForCondition(
                () => app.Snapshot.SelectedId == deadNpcId,
                "Could not reselect the NPC before testing stale-target cleanup.");
            player.LockedTargetId = deadNpcId;
            target.Dead = true;
            yield return WaitForCondition(
                () => state.FindEntity(deadNpcId) == null && string.IsNullOrEmpty(app.Snapshot.SelectedId),
                "Despawning the selected NPC left a stale Overview selection.");
            Assert.That(player.LockedTargetId, Is.Empty);
            Assert.That(root.Q<Label>("target-name").text, Is.EqualTo("No target"));
            Assert.That(root.Q<Button>("approach").enabledSelf, Is.False);
            Assert.That(root.Q<Button>("warp").enabledSelf, Is.False);
            Assert.That(dockButton.enabledSelf, Is.False);

            depletedAsteroid.Amount = 0d;
            RefreshUiSnapshotWithStructuralLists();
            yield return WaitForCondition(
                () => app.Snapshot.Overview.All(contact => contact.Id != deadNpcId) &&
                      app.Snapshot.Overview.All(contact => contact.Id != depletedAsteroidId),
                "A structural Overview refresh retained a dead NPC or depleted asteroid.");

            yield return AssertOverviewPresetUi(root, overviewList, OverviewPresetId.All, "all");
            Assert.That(OverviewItems(overviewList).Any(contact => contact.Id == deadNpcId), Is.False);
            Assert.That(OverviewItems(overviewList).Any(contact => contact.Id == depletedAsteroidId), Is.False);
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

            app.Execute("undock");
            yield return WaitForScene("Space");
            yield return WaitForCondition(() => !app.Snapshot.Docked, "Roundtrip pilot did not undock.");

            var sessionBeforeSave = GetSession();
            var runtimePlayer = sessionBeforeSave.State.PlayerEntity();
            const string expectedPilot = "Roundtrip Pilot";
            const long expectedCredits = 7654321L;
            sessionBeforeSave.State.Player.Name = expectedPilot;
            sessionBeforeSave.State.Player.Credits = expectedCredits;
            runtimePlayer.Shield = 123d;
            runtimePlayer.Armor = 87d;
            runtimePlayer.Position += new SimVec2(333d, -222d);
            runtimePlayer.Movement = MovementMode.Approach;
            runtimePlayer.MoveTargetPosition = runtimePlayer.Position + new SimVec2(500d, 0d);

            var isolatedSaves = new FileSaveService(temporarySaveDirectory);
            var slotPath = isolatedSaves.GetSlotPath(SaveSlot.Slot1);
            app.Execute("save");
            yield return WaitForCondition(
                () => File.Exists(slotPath),
                "Manual save did not create slot1 for the roundtrip.");
            var persistedRuntime = isolatedSaves.Load(SaveSlot.Slot1).ReadRuntime<RuntimeSaveState>();
            var persistedPlayer = persistedRuntime.Entities.Single(value => value.Kind == EntityKind.Player);

            sessionBeforeSave.State.Player.Name = "Unsaved Mutation";
            sessionBeforeSave.State.Player.Credits = 1L;
            runtimePlayer.Shield = 1d;

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
            var restoredPlayer = restoredSession.State.PlayerEntity();
            Assert.That(restoredPlayer.Shield, Is.EqualTo(persistedPlayer.Shield).Within(1e-9d));
            Assert.That(restoredPlayer.Armor, Is.EqualTo(persistedPlayer.Armor).Within(1e-9d));
            Assert.That(restoredPlayer.Position, Is.EqualTo(persistedPlayer.Position));
            Assert.That(restoredPlayer.Movement, Is.EqualTo(persistedPlayer.Movement));
            Assert.That(app.Snapshot.ContinueSummary, Does.StartWith("SLOT1"),
                "Continue metadata must identify the newest slot selected for loading.");
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

        private bool HasOverviewState(string contactId, OverviewStateFlags state)
        {
            var contact = app.Snapshot.Overview.FirstOrDefault(item => item.Id == contactId);
            return contact != null && (contact.States & state) != 0;
        }

        private IEnumerator AssertOverviewPresetUi(
            VisualElement root,
            ListView list,
            OverviewPresetId preset,
            string buttonSuffix)
        {
            var button = root.Q<Button>("overview-tab-" + buttonSuffix);
            Assert.That(button, Is.Not.Null, $"{preset} must expose a stable tab button.");
            SendClick(button);

            var expectedIds = new HashSet<string>(
                app.Snapshot.Overview
                    .Where(contact => ExpectedOverviewVisibility(preset, contact))
                    .Select(contact => contact.Id),
                StringComparer.Ordinal);
            yield return WaitForCondition(
                () => button.ClassListContains("chosen") &&
                      OverviewItems(list).Count == expectedIds.Count &&
                      OverviewItems(list).All(contact => expectedIds.Contains(contact.Id)),
                $"{preset} tab did not apply its expected filter to the live ListView.");

            var actual = OverviewItems(list);
            Assert.That(actual.All(contact => OverviewRules.IsVisible(preset, contact)), Is.True,
                $"{preset} ListView contains a contact rejected by OverviewRules.");
            CollectionAssert.AreEquivalent(expectedIds, actual.Select(contact => contact.Id).ToArray(),
                $"{preset} ListView does not match the typed snapshot filter.");
        }

        private void RefreshUiSnapshotWithStructuralLists()
        {
            var refresh = typeof(AppRoot).GetMethod(
                "RefreshUiSnapshot",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool) },
                null);
            Assert.That(refresh, Is.Not.Null,
                "The acceptance fixture could not request a structural UI refresh.");
            refresh.Invoke(app, new object[] { true });
        }

        private static bool ExpectedOverviewVisibility(
            OverviewPresetId preset,
            UiOverviewContact contact)
        {
            if (contact == null) return false;
            if ((contact.States & OverviewStateFlags.RouteNext) != 0) return true;
            switch (preset)
            {
                case OverviewPresetId.General:
                    return contact.Kind == OverviewKind.Ship ||
                           contact.Kind == OverviewKind.Station ||
                           contact.Kind == OverviewKind.Stargate ||
                           contact.Kind == OverviewKind.AsteroidBelt;
                case OverviewPresetId.Combat:
                    return contact.Kind == OverviewKind.Ship && IsThreatOrExplicitTarget(contact);
                case OverviewPresetId.Mining:
                    return contact.Kind == OverviewKind.AsteroidBelt ||
                           contact.Kind == OverviewKind.Asteroid ||
                           contact.Kind == OverviewKind.Ship && IsThreatOrExplicitTarget(contact);
                case OverviewPresetId.Travel:
                    return contact.Kind == OverviewKind.Station ||
                           contact.Kind == OverviewKind.Stargate ||
                           contact.Kind == OverviewKind.AsteroidBelt ||
                           contact.Kind == OverviewKind.Star ||
                           contact.Kind == OverviewKind.Planet ||
                           contact.Kind == OverviewKind.Moon ||
                           contact.Kind == OverviewKind.Ship && IsThreatOrExplicitTarget(contact);
                case OverviewPresetId.All:
                    return contact.Kind >= OverviewKind.Ship && contact.Kind <= OverviewKind.Star;
                default:
                    return false;
            }
        }

        private static bool IsThreatOrExplicitTarget(UiOverviewContact contact)
        {
            const OverviewStateFlags explicitTargetStates =
                OverviewStateFlags.TargetingPlayer |
                OverviewStateFlags.LockedByPlayer |
                OverviewStateFlags.MissionObjective;
            return contact.Disposition == OverviewDisposition.Hostile ||
                   (contact.States & explicitTargetStates) != 0;
        }

        private static List<UiOverviewContact> OverviewItems(ListView list)
        {
            return list?.itemsSource == null
                ? new List<UiOverviewContact>()
                : list.itemsSource.Cast<object>().OfType<UiOverviewContact>().ToList();
        }

        private static bool IsOverviewSorted(
            ListView list,
            OverviewSortColumn column,
            OverviewSortDirection direction)
        {
            var items = OverviewItems(list);
            var comparer = OverviewContactComparer.Get(column, direction);
            for (var i = 1; i < items.Count; i++)
            {
                if (comparer.Compare(items[i - 1], items[i]) > 0) return false;
            }
            return true;
        }

        private static void SendClick(Button button)
        {
            using (var click = ClickEvent.GetPooled())
            {
                click.target = button;
                button.SendEvent(click);
            }
        }

        private void CaptureAndClearOverviewPreferences()
        {
            savedOverviewPreferences.Clear();
            missingOverviewPreferences.Clear();
            foreach (var key in OverviewPreferenceKeys)
            {
                if (PlayerPrefs.HasKey(key)) savedOverviewPreferences[key] = PlayerPrefs.GetInt(key);
                else missingOverviewPreferences.Add(key);
                PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.Save();
        }

        private void RestoreOverviewPreferences()
        {
            foreach (var key in OverviewPreferenceKeys)
            {
                if (savedOverviewPreferences.TryGetValue(key, out var value)) PlayerPrefs.SetInt(key, value);
                else if (missingOverviewPreferences.Contains(key)) PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.Save();
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
            Assert.That(document.panelSettings, Is.Not.Null,
                $"{sceneName} UIDocument must reference shared panel settings.");
            Assert.That(document.panelSettings.scaleMode, Is.EqualTo(PanelScaleMode.ConstantPixelSize),
                $"{sceneName} UI must render at one-to-one pixel scale for crisp text.");
            Assert.That(document.panelSettings.scale, Is.EqualTo(1f),
                $"{sceneName} UI must not apply fractional panel scaling.");
            Assert.That(document.rootVisualElement, Is.Not.Null,
                $"{sceneName} UIDocument must create a visual tree.");
            Assert.That(document.rootVisualElement.childCount, Is.GreaterThan(0),
                $"{sceneName} visual tree must not be empty.");
        }

        private static IEnumerator AssertOverviewTextContrast(VisualElement root)
        {
            var empty = root.Q<Label>("overview-empty");
            Assert.That(empty, Is.Not.Null, "Space Overview must expose its empty-state label.");
            AssertReadableText(empty, "Space/Overview empty state");

            var overview = root.Q<VisualElement>(className: "overview");
            Assert.That(overview, Is.Not.Null, "Space must expose the Overview panel.");
            var staleRow = new VisualElement();
            staleRow.AddToClassList("overview-row");
            staleRow.AddToClassList("stale");
            var staleType = new Label("STALE CONTACT");
            staleType.AddToClassList("overview-cell");
            staleType.AddToClassList("overview-type");
            staleRow.Add(staleType);
            overview.Add(staleRow);

            yield return null;
            AssertReadableText(staleType, "Space/Overview stale contact");
            staleRow.RemoveFromHierarchy();
        }

        private static void AssertReadableText(VisualElement element, string context)
        {
            Assert.That(element, Is.Not.Null, $"{context}: element is missing.");
            var background = EffectiveBackground(element);
            var foreground = element.resolvedStyle.color;
            foreground.a *= EffectiveOpacity(element);
            var paintedForeground = Composite(foreground, background);
            var ratio = ContrastRatio(paintedForeground, background);
            Assert.That(ratio, Is.GreaterThanOrEqualTo(4.5f),
                $"{context}: text contrast {ratio:F2}:1 is below WCAG AA 4.5:1 " +
                $"(foreground {paintedForeground}, background {background}).");
        }

        private static Color EffectiveBackground(VisualElement element)
        {
            var ancestors = new Stack<VisualElement>();
            for (var current = element; current != null; current = current.parent)
                ancestors.Push(current);

            var result = Color.black;
            while (ancestors.Count > 0)
            {
                var current = ancestors.Pop();
                var layer = current.resolvedStyle.backgroundColor;
                layer.a *= current.resolvedStyle.opacity;
                result = Composite(layer, result);
            }
            return result;
        }

        private static float EffectiveOpacity(VisualElement element)
        {
            var opacity = 1f;
            for (var current = element; current != null; current = current.parent)
                opacity *= current.resolvedStyle.opacity;
            return opacity;
        }

        private static Color Composite(Color foreground, Color background)
        {
            var alpha = foreground.a + background.a * (1f - foreground.a);
            if (alpha <= 0f) return Color.clear;
            return new Color(
                (foreground.r * foreground.a + background.r * background.a * (1f - foreground.a)) / alpha,
                (foreground.g * foreground.a + background.g * background.a * (1f - foreground.a)) / alpha,
                (foreground.b * foreground.a + background.b * background.a * (1f - foreground.a)) / alpha,
                alpha);
        }

        private static float ContrastRatio(Color first, Color second)
        {
            var lighter = Mathf.Max(RelativeLuminance(first), RelativeLuminance(second));
            var darker = Mathf.Min(RelativeLuminance(first), RelativeLuminance(second));
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        private static float RelativeLuminance(Color color)
        {
            return 0.2126f * LinearChannel(color.r) +
                   0.7152f * LinearChannel(color.g) +
                   0.0722f * LinearChannel(color.b);
        }

        private static float LinearChannel(float channel)
        {
            return channel <= 0.04045f
                ? channel / 12.92f
                : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
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
