using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Starfall.Content;
using Starfall.Domain;
using Starfall.Persistence;
using Starfall.Presentation;
using Starfall.Simulation;
using Starfall.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static Starfall.Domain.L10n;

namespace Starfall.App
{
    [DefaultExecutionOrder(-1000)]
    public sealed class AppRoot : MonoBehaviour, IStarfallUiHost
    {
        private const uint DefaultSeed = 12345u;
        private const float UiTelemetryRefreshInterval = 0.1f;
        private const float AutosaveIntervalSeconds = 60f;
        private const string SellItemActionPrefix = "sell-item|";
        private const string SellModuleActionPrefix = "sell-module|";
        private const string SellShipActionPrefix = "sell-ship|";
        private const string FitModuleActionPrefix = "fit-module|";
        private const string UnfitActionPrefix = "unfit|";
        private const string LanguagePreferenceKey = "starfall.language";
        private const string UiScalePreferenceKey = "starfall.uiscale";
        private const string QualityPreferenceKey = "starfall.quality";
        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        private static AppRoot instance;
        private readonly UiSnapshot snapshot = new UiSnapshot();
        private readonly List<string> log = new List<string>();
        private readonly JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Culture = System.Globalization.CultureInfo.InvariantCulture,
            TypeNameHandling = TypeNameHandling.None,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        });

        private GameContentCatalog catalog;
        private UniverseGenerator generator;
        private ISaveService saves;
        private ILegacyV1Importer legacyImporter;
        private GameSession session;
        private MusicDirector musicDirector;
        private StarfallSfx sfx;
        private SpaceWorldPresenter spacePresenter;
        private StationHangarPresenter stationPresenter;
        private string loadedGameplayScene = string.Empty;
        private bool sceneTransitionQueued;
        private bool mapVisible;
        private bool journalVisible;
        private bool worldDirty = true;
        private bool uiDirty = true;
        private bool uiListsDirty = true;
        private bool telemetryDirty = true;
        private bool stationVisualDirty = true;
        private float uiTelemetryElapsed;
        private float autosaveElapsed;
        private float uiScale = 1f;
        private string presentedStationShipInstanceId = string.Empty;

        public UiSnapshot Snapshot => snapshot;
        public float MusicVolume => musicDirector ? musicDirector.MusicVolume : MusicDirector.DefaultMusicVolume;
        public float SfxVolume => StarfallSfx.Current ? StarfallSfx.Current.Volume : StarfallSfx.DefaultVolume;
        public bool MusicMuted => musicDirector && musicDirector.Muted;
        public string QualityPreset
        {
            get
            {
                var names = QualitySettings.names;
                var index = QualitySettings.GetQualityLevel();
                return names.Length > 0 && index >= 0 && index < names.Length ? names[index] : "Default";
            }
        }
        public float UiScale => uiScale;
        public event Action SnapshotChanged;
        public event Action TelemetryChanged;
        public event Action SettingsChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeRoot()
        {
            if (instance || FindFirstObjectByType<AppRoot>()) return;
            var root = new GameObject("StarfallApp");
            root.AddComponent<AppRoot>();
        }

        private void Awake()
        {
            if (instance && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            // Library default is English (tests assert canonical strings); the game
            // itself defaults to Chinese unless the player chose English before.
            var preferredLanguage = PlayerPrefs.GetString(LanguagePreferenceKey, "zh") == "en"
                ? L10nLanguage.English
                : L10nLanguage.Chinese;
            L10n.SetLanguage(preferredLanguage);
            L10n.LanguageChanged += OnLanguageChanged;
            ApplyLocalizedSnapshotDefaults();
            uiScale = Mathf.Clamp(PlayerPrefs.GetFloat(UiScalePreferenceKey, 1f), StarfallResponsiveUi.MinUiScale, StarfallResponsiveUi.MaxUiScale);
            ApplyUiScale();
            ApplySavedQuality();
            catalog = GameContentCatalog.Default;
            generator = new UniverseGenerator();
            saves = new FileSaveService();
            legacyImporter = new LegacyV1Importer();
            musicDirector = GetComponent<MusicDirector>();
            if (!musicDirector) musicDirector = gameObject.AddComponent<MusicDirector>();
            musicDirector.SettingsChanged += OnMusicSettingsChanged;
            sfx = GetComponent<StarfallSfx>();
            if (!sfx) sfx = gameObject.AddComponent<StarfallSfx>();
            StarfallUiBridge.Bind(this);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            if (SceneManager.GetActiveScene().name == "Bootstrap") RequestScene("MainMenu");
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            L10n.LanguageChanged -= OnLanguageChanged;
            if (musicDirector) musicDirector.SettingsChanged -= OnMusicSettingsChanged;
            instance = null;
        }

        // Android can kill a paused process at any moment, so the pause/quit
        // hooks are the last line of defense for progress made since the last
        // dock/jump autosave. Death is deliberately not saved here yet: the
        // save envelope does not carry PlayerDead, and persisting the pre-death
        // state would let a force-kill undo the loss.
        private void OnApplicationPause(bool pause)
        {
            if (pause) AutosaveNow();
            else autosaveElapsed = 0f;
        }

        private void OnApplicationQuit()
        {
            AutosaveNow();
        }

        private void AutosaveNow()
        {
            if (session == null || session.State.PlayerDead) return;
            Save(SaveSlot.Auto, announce: false);
        }

        public void SetLanguage(L10nLanguage value)
        {
            if (L10n.Language == value) return;
            PlayerPrefs.SetString(LanguagePreferenceKey, value == L10nLanguage.Chinese ? "zh" : "en");
            PlayerPrefs.Save();
            L10n.SetLanguage(value);
            SettingsChanged?.Invoke();
        }

        private void OnLanguageChanged()
        {
            ApplyLocalizedSnapshotDefaults();
            MarkAllDirty();
            RefreshUiSnapshot(true);
        }

        private void ApplyLocalizedSnapshotDefaults()
        {
            snapshot.PilotName = Tr("Pilot");
            snapshot.EmpireName = Tr("Aurelian Ascendancy");
            snapshot.SystemName = TrName("Helios Prime");
            snapshot.ShipName = Tr("Acolyte");
            snapshot.ShipClass = Tr("Frigate");
            snapshot.SelectedName = Tr("No target");
            snapshot.SelectedDetail = Tr("Select an object in space");
            snapshot.MissionSummary = Tr("No active mission");
        }

        private void Update()
        {
            HandleKeyboard();
            if (session == null) return;
            autosaveElapsed += Time.unscaledDeltaTime;
            if (autosaveElapsed >= AutosaveIntervalSeconds)
            {
                autosaveElapsed = 0f;
                AutosaveNow();
            }
            var simulationTimeBeforeFrame = session.State.SimulationTime;
            var batch = session.AdvanceFrame(Time.unscaledDeltaTime);
            var simulationAdvanced = session.State.SimulationTime > simulationTimeBeforeFrame;
            if (simulationAdvanced && !session.State.Docked && !session.State.PlayerDead)
            {
                worldDirty = true;
                telemetryDirty = true;
            }
            if (batch.Count > 0) HandleEvents(batch);
            if (!sceneTransitionQueued)
            {
                var desired = session.State.Docked ? "Station" : "Space";
                if (loadedGameplayScene != desired) RequestScene(desired);
            }
            var activeScene = SceneManager.GetActiveScene().name;
            if (activeScene == "Space" && worldDirty)
            {
                PresentSpace();
                worldDirty = false;
            }
            if (activeScene == "Station" && stationVisualDirty)
            {
                PresentStation();
                stationVisualDirty = false;
            }

            uiTelemetryElapsed += Time.unscaledDeltaTime;
            if (uiDirty)
            {
                RefreshUiSnapshot(uiListsDirty);
            }
            else if (telemetryDirty && uiTelemetryElapsed >= UiTelemetryRefreshInterval)
            {
                if (UiTelemetryChanged()) RefreshUiTelemetrySnapshot();
                else
                {
                    telemetryDirty = false;
                    uiTelemetryElapsed = 0f;
                }
            }
        }

        public void StartNewGame(string pilotName, string empireId)
        {
            session = new GameSession(generator.Generate(DefaultSeed), catalog, pilotName, empireId);
            log.Clear();
            AddLog(Tr("Welcome to the stars, {0}.", session.State.Player.Name));
            AddLog(Application.isMobilePlatform
                ? Tr("Talk to an agent, undock, then tap targets and use the command buttons and modules 1–9.")
                : Tr("Talk to an agent, undock, then use click, W/L/D and modules 1–9."));
            Save(SaveSlot.Auto);
            MarkAllDirty();
            RefreshUiSnapshot(true);
            RequestScene("Station");
        }

        public void ContinueGame()
        {
            // Resume the most recently written slot instead of always preferring
            // auto: a manual save made after an autosave must win. Equal
            // timestamps tie-break toward manual slots.
            var candidates = saves.List()
                .Where(info => info.Exists && !info.IsCorrupt && info.LastWriteTimeUtc.HasValue)
                .OrderByDescending(info => info.LastWriteTimeUtc.Value)
                .ThenBy(info => ManualSlotPriority(info.Slot));
            foreach (var candidate in candidates)
            {
                try
                {
                    LoadEnvelope(saves.Load(candidate.Slot));
                    return;
                }
                catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is InvalidOperationException)
                {
                    // Try the next slot. FileSaveService already attempts .bak recovery.
                }
            }
            AddLog(Tr("No valid save slot was found."));
            SnapshotChanged?.Invoke();
        }

        private static int ManualSlotPriority(SaveSlot slot)
        {
            switch (slot)
            {
                case SaveSlot.Slot1: return 0;
                case SaveSlot.Slot2: return 1;
                case SaveSlot.Slot3: return 2;
                default: return 3;
            }
        }

        public void ImportLegacy()
        {
            var path = FindLegacySave();
            if (path == null)
            {
                AddLog(Tr("Place a legacy JSON save in Downloads or as persistentDataPath/legacy-v1.json, then try again."));
                SnapshotChanged?.Invoke();
                return;
            }
            try
            {
                var bytes = File.ReadAllBytes(path);
                var seed = ReadLegacySeed(bytes);
                var universe = generator.Generate(seed);
                var references = BuildLegacyReferences(universe);
                var converted = legacyImporter.Convert(bytes, references);
                if (!converted.IsSuccess)
                {
                    AddLog(Tr("Legacy import rejected: {0}", converted.Inspection.ErrorMessage));
                    SnapshotChanged?.Invoke();
                    return;
                }
                saves.Save(SaveSlot.Slot1, converted.Envelope);
                LoadEnvelope(converted.Envelope);
                AddLog(Tr("Legacy v1 imported to slot1. The source file was not modified."));
            }
            catch (Exception exception)
            {
                AddLog(Tr("Legacy import failed: {0}", exception.Message));
                SnapshotChanged?.Invoke();
            }
        }

        public void Execute(string command, string argument = null)
        {
            if (session == null) return;
            switch (command)
            {
                case "select": Queue(GameCommandType.Select, argument); break;
                case "approach": Queue(GameCommandType.Approach, argument); break;
                case "orbit": Queue(GameCommandType.Orbit, argument); break;
                case "warp": Queue(GameCommandType.Warp, argument); break;
                case "lock": Queue(GameCommandType.Lock, argument); break;
                case "dock": Queue(GameCommandType.DockOrJump, argument); break;
                case "undock": Queue(GameCommandType.Undock); break;
                case "module":
                    if (int.TryParse(argument, out var moduleIndex)) session.Enqueue(new GameCommand(GameCommandType.ActivateModule, index: moduleIndex));
                    break;
                case "save": Queue(GameCommandType.Save); break;
                case "repair": Queue(GameCommandType.Repair); break;
                case "respawn": Queue(GameCommandType.Respawn); break;
                case "agent": Queue(GameCommandType.TalkToAgent, argument); break;
                case "market": MarketAction(argument); break;
                case "fit": FittingAction(argument); break;
                case "lp-exchange":
                    // LP rows arrive as "lp-item|<moduleId>"; the classic button sends no argument.
                    Queue(GameCommandType.ExchangeLoyalty,
                        TryActionPayload(argument, "lp-item|", out var lpModuleId) ? lpModuleId : argument);
                    break;
                case "train": Queue(GameCommandType.TrainSkill, argument); break;
                case "ship": Queue(GameCommandType.SwitchShip, argument); break;
                case "mission": MissionAction(argument); break;
                case "destination":
                    Queue(GameCommandType.SetDestination, argument);
                    mapVisible = false;
                    MarkUiDirty(true);
                    break;
                case "map": mapVisible = !mapVisible; MarkUiDirty(mapVisible); AddMapLog(); break;
                case "journal": journalVisible = !journalVisible; AddJournalLog(); break;
                case "pilot": AddPilotLog(); break;
            }
        }

        public void SetMusicVolume(float value)
        {
            if (musicDirector) musicDirector.SetMusicVolume(value, persist: false);
        }

        public void SetSfxVolume(float value)
        {
            if (StarfallSfx.Current) StarfallSfx.Current.SetVolume(value);
        }

        public void SetMusicMuted(bool value)
        {
            if (musicDirector) musicDirector.SetMuted(value);
        }

        public void SetUiScale(float value)
        {
            uiScale = Mathf.Clamp(value, StarfallResponsiveUi.MinUiScale, StarfallResponsiveUi.MaxUiScale);
            // The preference is flushed on slider release (PlayerPrefs.Save is
            // expensive on flash storage); here we only stage the value.
            PlayerPrefs.SetFloat(UiScalePreferenceKey, uiScale);
            ApplyUiScale();
            SettingsChanged?.Invoke();
        }

        private void ApplyUiScale()
        {
            if (FindFirstObjectByType<UIDocument>() is { } document && document.panelSettings)
                document.panelSettings.scale = uiScale;
        }

        public void CycleQuality()
        {
            var next = NextQualityLevel();
            QualitySettings.SetQualityLevel(next, true);
            PlayerPrefs.SetString(QualityPreferenceKey, QualityPreset);
            PlayerPrefs.Save();
            AddLog(Tr("Quality preset: {0}.", Tr(QualityPreset)));
            SettingsChanged?.Invoke();
        }

        // Restores the quality level by name. Legacy installs stored a numeric
        // index picked when mobile only exposed the Mobile tier; those values do
        // not resolve to a tier name, so fresh installs fall back to the tier
        // matching the platform — shipping the PC pipeline on phones costs both
        // battery and frame rate.
        private void ApplySavedQuality()
        {
            var names = QualitySettings.names;
            if (names.Length == 0) return;
            var saved = PlayerPrefs.GetString(QualityPreferenceKey, string.Empty);
            var index = Array.FindIndex(names, name => string.Equals(name, saved, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                var platformDefault = Application.isMobilePlatform ? "Mobile" : "PC";
                index = Array.FindIndex(names, name => string.Equals(name, platformDefault, StringComparison.OrdinalIgnoreCase));
            }
            if (index < 0)
                index = Array.FindIndex(names, name => string.Equals(name, "PC", StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = names.Length - 1;
            if (index != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(index, true);
        }

        private void OnMusicSettingsChanged()
        {
            SettingsChanged?.Invoke();
        }

        private static int NextQualityLevel()
        {
            var names = QualitySettings.names;
            if (names.Length == 0) return 0;

            if (!Application.isMobilePlatform)
            {
                var desktop = Array.FindIndex(names,
                    name => string.Equals(name, "PC", StringComparison.OrdinalIgnoreCase));
                return desktop >= 0 ? desktop : names.Length - 1;
            }

            var current = QualitySettings.GetQualityLevel();
            return current >= names.Length - 1 ? 0 : current + 1;
        }

        private void Queue(GameCommandType type, string argument = null)
        {
            if (session == null) return;
            session.Enqueue(new GameCommand(type, string.IsNullOrEmpty(argument) ? session.State.SelectedId : argument));
        }

        private void HandleKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || session == null) return;
            if (keyboard.wKey.wasPressedThisFrame) Queue(GameCommandType.Warp);
            if (keyboard.lKey.wasPressedThisFrame) Queue(GameCommandType.Lock);
            if (keyboard.dKey.wasPressedThisFrame) Queue(GameCommandType.DockOrJump);
            if (keyboard.mKey.wasPressedThisFrame) Execute("map");
            if (keyboard.jKey.wasPressedThisFrame) Execute("journal");
            if (keyboard.cKey.wasPressedThisFrame) Execute("pilot");
            if (keyboard.hKey.wasPressedThisFrame)
                AddLog(Application.isMobilePlatform
                    ? Tr("Help: tap to select · double-tap approach · long-press menu · buttons for warp, lock and docking.")
                    : Tr("Help: click to select · double-click approach · W warp · L lock · D dock/jump · V/X focus · 1–9 modules."));
            // Android's back gesture arrives as Escape: close the topmost HUD
            // overlay instead of letting the system back the app out.
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (mapVisible) Execute("map");
                else if (journalVisible) Execute("journal");
            }
            for (var i = 0; i < 9; i++)
            {
                var key = i == 0 ? keyboard.digit1Key : i == 1 ? keyboard.digit2Key : i == 2 ? keyboard.digit3Key :
                    i == 3 ? keyboard.digit4Key : i == 4 ? keyboard.digit5Key : i == 5 ? keyboard.digit6Key :
                    i == 6 ? keyboard.digit7Key : i == 7 ? keyboard.digit8Key : keyboard.digit9Key;
                if (key.wasPressedThisFrame) session.Enqueue(new GameCommand(GameCommandType.ActivateModule, index: i));
            }
        }

        private void HandleEvents(SimulationEventBatch batch)
        {
            for (var i = 0; i < batch.Count; i++)
            {
                var evt = batch[i];
                switch (evt.Type)
                {
                    case SimulationEventType.Spawn:
                    case SimulationEventType.Despawn:
                    case SimulationEventType.SystemPopulated:
                        worldDirty = true;
                        MarkUiDirty(true);
                        break;
                    case SimulationEventType.Mission:
                    case SimulationEventType.Inventory:
                    case SimulationEventType.Dock:
                    case SimulationEventType.Jump:
                    case SimulationEventType.Death:
                    case SimulationEventType.SkillTrained:
                        worldDirty = true;
                        MarkUiDirty(true);
                        break;
                    case SimulationEventType.Damage:
                    case SimulationEventType.Weapon:
                    case SimulationEventType.Warp:
                    case SimulationEventType.Selection:
                    case SimulationEventType.Log:
                    case SimulationEventType.SaveRequested:
                        MarkUiDirty();
                        break;
                }
                if (!string.IsNullOrEmpty(evt.Message) && (evt.Type == SimulationEventType.Log || evt.Type == SimulationEventType.Mission ||
                    evt.Type == SimulationEventType.Inventory || evt.Type == SimulationEventType.Dock || evt.Type == SimulationEventType.Jump ||
                    evt.Type == SimulationEventType.SkillTrained))
                    AddLog(evt.Message);
                if (evt.Type == SimulationEventType.SaveRequested)
                    // Auto saves stay quiet: a dock/jump log line on every
                    // transition just buried the useful combat log.
                    Save(evt.Detail == "auto" ? SaveSlot.Auto : SaveSlot.Slot1, announce: evt.Detail != "auto");
                if (evt.Type == SimulationEventType.Inventory) stationVisualDirty = true;
                if (sfx && evt.Type == SimulationEventType.Weapon)
                    sfx.PlayMining();
                if (sfx && evt.Type == SimulationEventType.Dock)
                    sfx.PlayDock();
                if (sfx && evt.Type == SimulationEventType.Jump)
                    sfx.PlayJump();
                if (spacePresenter && evt.Type == SimulationEventType.Weapon)
                {
                    var color = evt.Detail != null && evt.Detail.StartsWith("mining", StringComparison.Ordinal)
                        ? new Color(0.35f, 1f, 0.42f) : new Color(1f, 0.68f, 0.18f);
                    spacePresenter.FireBeam(evt.SourceId, evt.TargetId, color);
                    if (sfx && !(evt.Detail != null && evt.Detail.StartsWith("mining", StringComparison.Ordinal)))
                        sfx.PlayLaser();
                }
                if (spacePresenter && evt.Type == SimulationEventType.Death)
                {
                    if (sfx) sfx.PlayExplosion();
                    var hasPosition = evt.Position.HasValue;
                    var position = evt.Position.GetValueOrDefault();
                    if (!hasPosition) hasPosition = TryWorldPosition(evt.TargetId, out position);
                    if (hasPosition)
                        spacePresenter.Explosion(new Vector3((float)position.X, 0f, (float)position.Z),
                            new Color(1f, 0.28f, 0.06f), 7f);
                }
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            sceneTransitionQueued = false;
            ApplyUiScale();
            if (musicDirector) musicDirector.PlayForScene(scene.name);
            loadedGameplayScene = scene.name == "Space" || scene.name == "Station" ? scene.name : string.Empty;
            HideSceneCover();
            spacePresenter = FindFirstObjectByType<SpaceWorldPresenter>();
            stationPresenter = FindFirstObjectByType<StationHangarPresenter>();
            presentedStationShipInstanceId = string.Empty;
            MarkAllDirty();
            if (spacePresenter)
            {
                spacePresenter.SelectionChanged += OnSelectionChanged;
                spacePresenter.ApproachRequested += OnApproachRequested;
                spacePresenter.ContextRequested += OnContextRequested;
                PresentSpace();
                worldDirty = false;
            }
            if (stationPresenter && session != null)
            {
                PresentStation();
                stationVisualDirty = false;
            }
            RefreshUiSnapshot(true);
        }

        private void OnSelectionChanged(string stableId)
        {
            if (session == null) return;
            session.Enqueue(new GameCommand(GameCommandType.Select, stableId));
        }

        private void OnApproachRequested(Vector3 position, string selectedId)
        {
            if (session == null) return;
            session.Enqueue(new GameCommand(GameCommandType.Approach, selectedId, position: new SimVec2(position.x, position.z)));
        }

        private void OnContextRequested(string stableId)
        {
            if (session == null) return;
            session.Enqueue(new GameCommand(GameCommandType.Select, stableId));
            AddLog(Tr("Context: Approach · Orbit · Warp · Lock · Dock/Jump."));
        }

        private void RequestScene(string sceneName)
        {
            if (sceneTransitionQueued || SceneManager.GetActiveScene().name == sceneName) return;
            sceneTransitionQueued = true;
            StartCoroutine(SceneTransitionRoutine(sceneName));
        }

        // Scene swaps used to be raw synchronous loads: on Android each
        // dock/undock/jump froze the frame with no feedback. The cover is
        // attached to the OUTGOING scene's own UI document, so it renders one
        // frame before the load stalls the thread and dies with that scene —
        // no persistent UIDocument that could shadow the real scene UI.
        private System.Collections.IEnumerator SceneTransitionRoutine(string sceneName)
        {
            ShowSceneCover();
            if (fadeCover != null) yield return null;
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        private VisualElement fadeCover;

        private void ShowSceneCover()
        {
            if (fadeCover != null) return;
            foreach (var document in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                var element = document.rootVisualElement;
                if (element == null) continue;
                var scene = document.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded) continue; // skip DontDestroyOnLoad objects
                fadeCover = BuildSceneCover();
                element.Add(fadeCover);
                return;
            }
        }

        private void HideSceneCover()
        {
            if (fadeCover == null) return;
            fadeCover.RemoveFromHierarchy();
            fadeCover = null;
        }

        private static VisualElement BuildSceneCover()
        {
            var cover = new VisualElement();
            cover.style.position = Position.Absolute;
            cover.style.left = 0f; cover.style.right = 0f; cover.style.top = 0f; cover.style.bottom = 0f;
            cover.style.backgroundColor = new Color(0.001f, 0.004f, 0.012f, 1f);
            cover.style.alignItems = Align.Center;
            cover.style.justifyContent = Justify.Center;
            var label = new Label(Tr("Loading…"));
            label.style.fontSize = 34;
            label.style.color = new Color(0.31f, 0.88f, 1f);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.letterSpacing = 8f;
            cover.Add(label);
            return cover;
        }

        private void PresentStation()
        {
            if (!stationPresenter || session == null) return;
            var player = session.State.Player;
            var ship = player.ActiveShip();
            if (ship == null) return;
            if (string.Equals(presentedStationShipInstanceId, ship.InstanceId, StringComparison.Ordinal)) return;
            var definition = catalog.Ships[ship.ShipId];
            stationPresenter.SetShip(ship.ShipId, definition.Class.ToString().ToLowerInvariant(), FactionColor(player.EmpireId));
            presentedStationShipInstanceId = ship.InstanceId;
        }

        private void PresentSpace()
        {
            if (!spacePresenter || session == null || session.State.Docked) return;
            spacePresenter.Present(BuildSpaceSnapshot());
        }

        // The space snapshot used to be rebuilt (allocated) at sim rate, ~20
        // times per second while flying. These slots are reused instead; the
        // presenter only reads them, so in-place mutation is safe.
        private readonly SpaceSnapshot spaceSnapshot = new SpaceSnapshot();
        private readonly List<WorldObjectViewData> spaceObjectPool = new List<WorldObjectViewData>();
        private int spaceObjectCount;

        private WorldObjectViewData NextSpaceObject()
        {
            if (spaceObjectCount < spaceObjectPool.Count) return spaceObjectPool[spaceObjectCount];
            var created = new WorldObjectViewData();
            spaceObjectPool.Add(created);
            spaceObjectCount++;
            return created;
        }

        private SpaceSnapshot BuildSpaceSnapshot()
        {
            var state = session.State;
            var system = state.Universe.Systems[state.Player.CurrentSystemId];
            var result = spaceSnapshot;
            result.SystemId = system.Id;
            result.SystemName = system.Name;
            result.FactionId = system.FactionId;
            result.Security = (float)system.Security;
            result.FactionColor = FactionColor(system.FactionId);
            result.SelectedId = state.SelectedId;
            result.Objects.Clear();
            spaceObjectCount = 0;

            var star = NextSpaceObject();
            star.Id = system.Id + "_star"; star.Name = system.Name + " Star"; star.Kind = WorldViewKind.Star;
            star.Position = Vector3.zero; star.Radius = Mathf.Max(18f, (float)system.Star.Radius * 0.3f); star.Color = ParseColor(system.Star.Color);
            result.Objects.Add(star);
            foreach (var planet in system.Planets)
            {
                result.Objects.Add(WorldObject(planet.Id, planet.Name, WorldViewKind.Planet, planet.Position,
                    Mathf.Max(3f, (float)planet.Radius * 0.32f), ParseColor(planet.Color)));
                foreach (var moon in planet.Moons)
                    result.Objects.Add(WorldObject(moon.Id, moon.Name, WorldViewKind.Moon, moon.Position,
                        Mathf.Max(1.2f, (float)moon.Radius * 0.35f), new Color(0.52f, 0.58f, 0.68f)));
            }
            foreach (var station in system.Stations)
                result.Objects.Add(WorldObject(station.Id, station.Name, WorldViewKind.Station, station.Position, 9f, FactionColor(station.FactionId)));
            foreach (var gate in system.Gates)
                result.Objects.Add(WorldObject(gate.Id, gate.Name, WorldViewKind.Gate, gate.Position, 7f, FactionColor(system.FactionId)));
            foreach (var belt in system.Belts)
                result.Objects.Add(WorldObject(belt.Id, belt.Name, WorldViewKind.Belt, belt.Position, 2f, new Color(0.52f, 0.42f, 0.3f)));
            foreach (var asteroid in state.Asteroids)
                result.Objects.Add(WorldObject(asteroid.Id, catalog.Items[asteroid.OreId].Name, WorldViewKind.Asteroid,
                    asteroid.Position, (float)asteroid.Radius, new Color(0.34f, 0.28f, 0.22f)));
            foreach (var entity in state.Entities)
            {
                var definition = catalog.Ships[entity.ShipId];
                var ship = NextSpaceObject();
                ship.Id = entity.Id;
                ship.Name = entity.Name;
                ship.Kind = WorldViewKind.Ship;
                ship.Position = new Vector3((float)entity.Position.X, 0f, (float)entity.Position.Z);
                ship.Radius = definition.Class == ShipClass.Battleship ? 10f
                    : definition.Class == ShipClass.Battlecruiser ? 8.5f
                    : definition.Class == ShipClass.Cruiser ? 7f
                    : definition.Class == ShipClass.Destroyer ? 5f : 3f;
                ship.Color = FactionColor(entity.FactionId);
                ship.ShipId = entity.ShipId;
                ship.ShipClass = definition.Class.ToString().ToLowerInvariant();
                ship.FactionId = entity.FactionId;
                ship.IsPlayer = entity.Kind == EntityKind.Player;
                ship.IsHostile = IsHostile(entity);
                ship.HeadingDegrees = (float)(-entity.HeadingRadians * Mathf.Rad2Deg + 90f);
                ship.Shield01 = (float)(entity.Shield / Math.Max(1d, entity.MaxShield));
                ship.Armor01 = (float)(entity.Armor / Math.Max(1d, entity.MaxArmor));
                ship.Hull01 = (float)(entity.Hull / Math.Max(1d, entity.MaxHull));
                result.Objects.Add(ship);
            }
            return result;
        }

        private void MarkUiDirty(bool rebuildLists = false)
        {
            uiDirty = true;
            if (rebuildLists) uiListsDirty = true;
        }

        private void MarkAllDirty()
        {
            worldDirty = true;
            uiDirty = true;
            uiListsDirty = true;
            telemetryDirty = true;
            stationVisualDirty = true;
            uiTelemetryElapsed = 0f;
        }

        private void RefreshUiSnapshot(bool rebuildLists)
        {
            var refreshLists = rebuildLists || uiListsDirty;
            BuildUiSnapshot(refreshLists);
            uiDirty = false;
            if (refreshLists) uiListsDirty = false;
            telemetryDirty = false;
            uiTelemetryElapsed = 0f;
        }

        private bool UiTelemetryChanged()
        {
            if (session == null) return false;
            var state = session.State;
            if (snapshot.Docked != state.Docked || snapshot.PlayerDead != state.PlayerDead ||
                !string.Equals(snapshot.SelectedId, state.SelectedId, StringComparison.Ordinal)) return true;

            var entity = state.PlayerEntity();
            var speed = entity != null ? (float)entity.Speed : 0f;
            var shield = entity != null ? (float)(entity.Shield / Math.Max(1d, entity.MaxShield)) : snapshot.Shield01;
            var armor = entity != null ? (float)(entity.Armor / Math.Max(1d, entity.MaxArmor)) : snapshot.Armor01;
            var hull = entity != null ? (float)(entity.Hull / Math.Max(1d, entity.MaxHull)) : snapshot.Hull01;
            if (Mathf.Abs(snapshot.Speed - speed) > 0.01f ||
                Mathf.Abs(snapshot.Shield01 - shield) > 0.0005f ||
                Mathf.Abs(snapshot.Armor01 - armor) > 0.0005f ||
                Mathf.Abs(snapshot.Hull01 - hull) > 0.0005f) return true;

            if (entity != null)
            {
                if (snapshot.Modules.Count != entity.Modules.Count) return true;
                for (var i = 0; i < entity.Modules.Count; i++)
                    if (snapshot.Modules[i].Active != entity.Modules[i].Active) return true;
            }

            // Distance is presentation-only telemetry and changes while either the
            // player or the selected target moves. Throttle it rather than rebuilding
            // the entire UI every rendered frame.
            return !state.Docked && snapshot.Overview.Count > 0;
        }

        private void RefreshUiTelemetrySnapshot()
        {
            if (session == null) return;
            var state = session.State;
            var player = state.Player;
            var ship = player.ActiveShip();
            var shipDefinition = ship != null ? catalog.Ships[ship.ShipId] : null;
            var entity = state.PlayerEntity();

            snapshot.SelectedId = state.SelectedId;
            snapshot.SelectedName = SelectedName(state.SelectedId);
            snapshot.SelectedDetail = SelectedDetail(state.SelectedId);
            snapshot.Docked = state.Docked;
            snapshot.PlayerDead = state.PlayerDead;
            snapshot.Speed = entity != null ? (float)entity.Speed : 0f;
            snapshot.Shield01 = entity != null
                ? (float)(entity.Shield / Math.Max(1d, entity.MaxShield))
                : shipDefinition != null ? (float)(ship.Shield / shipDefinition.HitPoints.Shield) : 0f;
            snapshot.Armor01 = entity != null
                ? (float)(entity.Armor / Math.Max(1d, entity.MaxArmor))
                : shipDefinition != null ? (float)(ship.Armor / shipDefinition.HitPoints.Armor) : 0f;
            snapshot.Hull01 = entity != null
                ? (float)(entity.Hull / Math.Max(1d, entity.MaxHull))
                : shipDefinition != null ? (float)(ship.Hull / shipDefinition.HitPoints.Hull) : 0f;

            if (entity != null && snapshot.Modules.Count != entity.Modules.Count)
            {
                MarkUiDirty();
                return;
            }
            if (entity != null)
            {
                for (var i = 0; i < entity.Modules.Count; i++)
                {
                    var runtime = entity.Modules[i];
                    var module = snapshot.Modules[i];
                    var definition = catalog.Modules[runtime.ModuleId];
                    module.Active = runtime.Active;
                    module.Cooldown01 = definition.CycleTime > 0d
                        ? (float)(runtime.Cooldown / definition.CycleTime)
                        : 0f;
                }
            }

            OverviewContactBuilder.RefreshTelemetry(snapshot.Overview, session, catalog);
            telemetryDirty = false;
            uiTelemetryElapsed = 0f;
            TelemetryChanged?.Invoke();
        }

        private void BuildUiSnapshot(bool rebuildLists)
        {
            if (session == null)
            {
                SnapshotChanged?.Invoke();
                return;
            }
            var state = session.State;
            var player = state.Player;
            var system = state.Universe.Systems[player.CurrentSystemId];
            var ship = player.ActiveShip();
            var shipDefinition = ship != null ? catalog.Ships[ship.ShipId] : null;
            snapshot.PilotName = player.Name;
            snapshot.EmpireId = player.EmpireId;
            snapshot.EmpireName = Tr(catalog.Factions[player.EmpireId].Name);
            snapshot.SystemName = TrName(system.Name) + (mapVisible ? Tr(" · STARMAP") : string.Empty);
            snapshot.Security = (float)system.Security;
            snapshot.ShipName = ship != null ? Tr(ship.Name) : Tr("No ship");
            snapshot.ShipClass = shipDefinition != null ? Tr(shipDefinition.Class.ToString()) : string.Empty;
            snapshot.Credits = player.Credits;
            player.LoyaltyPoints.TryGetValue(player.EmpireId, out snapshot.LoyaltyPoints);
            snapshot.SelectedId = state.SelectedId;
            snapshot.SelectedName = SelectedName(state.SelectedId);
            snapshot.SelectedDetail = SelectedDetail(state.SelectedId);
            snapshot.Docked = state.Docked;
            snapshot.PlayerDead = state.PlayerDead;
            snapshot.MapVisible = mapVisible;
            snapshot.JournalVisible = journalVisible;
            var entity = state.PlayerEntity();
            snapshot.Speed = entity != null ? (float)entity.Speed : 0f;
            snapshot.Shield01 = entity != null ? (float)(entity.Shield / Math.Max(1d, entity.MaxShield)) : shipDefinition != null ? (float)(ship.Shield / shipDefinition.HitPoints.Shield) : 0f;
            snapshot.Armor01 = entity != null ? (float)(entity.Armor / Math.Max(1d, entity.MaxArmor)) : shipDefinition != null ? (float)(ship.Armor / shipDefinition.HitPoints.Armor) : 0f;
            snapshot.Hull01 = entity != null ? (float)(entity.Hull / Math.Max(1d, entity.MaxHull)) : shipDefinition != null ? (float)(ship.Hull / shipDefinition.HitPoints.Hull) : 0f;
            snapshot.MissionSummary = ActiveMissionSummary();
            snapshot.FittingSummary = FittingSummary();
            if (rebuildLists) RebuildLists();
            else OverviewContactBuilder.RefreshTelemetry(snapshot.Overview, session, catalog);
            snapshot.Log.Clear();
            snapshot.Log.AddRange(log);
            snapshot.Modules.Clear();
            if (entity != null)
            {
                foreach (var module in entity.Modules)
                {
                    var definition = catalog.Modules[module.ModuleId];
                    snapshot.Modules.Add(new UiModuleState
                    {
                        Id = module.ModuleId,
                        Name = Tr(definition.Name),
                        Slot = Tr(definition.Slot.ToString()),
                        Active = module.Active,
                        Cooldown01 = definition.CycleTime > 0d ? (float)(module.Cooldown / definition.CycleTime) : 0f,
                    });
                }
            }
            SnapshotChanged?.Invoke();
        }

        private void RebuildLists()
        {
            var state = session.State;
            var player = state.Player;
            var system = state.Universe.Systems[player.CurrentSystemId];
            OverviewContactBuilder.Rebuild(snapshot.Overview, session, catalog);

            snapshot.Starmap.Clear();
            // One BFS from the current system answers every starmap row;
            // per-system FindRoute calls made rebuilds O(systems × graph).
            var hops = UniverseRoutes.CountHopsFrom(state.Universe, player.CurrentSystemId);
            foreach (var mapSystem in state.Universe.OrderedSystems)
            {
                var jumpCount = hops.TryGetValue(mapSystem.Id, out var value) ? value : -1;
                var jumps = jumpCount < 0 ? Tr("NO ROUTE") : Tr("{0} jumps", jumpCount);
                var destination = player.DestinationSystemId == mapSystem.Id ? Tr(" · DESTINATION") : string.Empty;
                snapshot.Starmap.Add(Item(mapSystem.Id,
                    TrName(mapSystem.Name) + Tr(" · SEC {0}", mapSystem.Security.ToString("0.0", Inv)) + destination,
                    jumps + " · " + Tr(catalog.Factions[mapSystem.FactionId].Name)));
            }

            snapshot.Agents.Clear();
            var dockedStation = system.Stations.Find(value => value.Id == player.DockedAtStationId);
            if (dockedStation != null)
                foreach (var agent in dockedStation.Agents) snapshot.Agents.Add(Item(agent.Id, TrPersonName(agent.Name) + " · L" + agent.Level, Tr(agent.Division)));

            snapshot.Market.Clear();
            foreach (var module in catalog.Modules.Values) snapshot.Market.Add(Item(module.Id, Tr(module.Name) + " · " + PriceText(module.Id), Tr(module.Description)));
            foreach (var ship in catalog.Ships.Values) if (!ship.NpcOnly) snapshot.Market.Add(Item(ship.Id, Tr(ship.Name) + " · " + PriceText(ship.Id), Tr(ship.Description)));
            foreach (var pair in player.Cargo)
            {
                if (pair.Value <= 0d || pair.Key == ItemIds.SealedCargo || !catalog.Items.TryGetValue(pair.Key, out var item)) continue;
                snapshot.Market.Add(Item(SellItemActionPrefix + pair.Key,
                    Tr("SELL CARGO · {0} ×{1}", Tr(item.Name), pair.Value.ToString("0", Inv)),
                    Tr("Sell the full stack · {0} per unit base", PriceText(pair.Key))));
            }
            foreach (var pair in player.Hangar)
            {
                if (pair.Value <= 0 || !catalog.Modules.TryGetValue(pair.Key, out var module)) continue;
                snapshot.Market.Add(Item(SellModuleActionPrefix + pair.Key,
                    Tr("SELL HANGAR · {0} ×{1}", Tr(module.Name), pair.Value),
                    Tr("Sell one module · {0}", PriceText(pair.Key))));
            }

            snapshot.Ships.Clear();
            foreach (var owned in player.Ships) snapshot.Ships.Add(Item(owned.InstanceId, owned.InstanceId == player.ActiveShipInstanceId ? Tr("ACTIVE · {0}", Tr(owned.Name)) : Tr(owned.Name), Tr(catalog.Ships[owned.ShipId].Description)));
            // Reserve hangar: every inactive ship is exposed as an explicit
            // sell action; the active ship keeps its switch row.
            foreach (var owned in player.Ships)
            {
                if (owned.InstanceId == player.ActiveShipInstanceId) continue;
                if (player.Ships.Count <= 1) continue;
                snapshot.Ships.Add(Item(SellShipActionPrefix + owned.InstanceId,
                    Tr("SELL SHIP · {0}", Tr(owned.Name)),
                    Tr("Sell the ship · {0}", PriceText(owned.ShipId))));
            }

            snapshot.Inventory.Clear();
            var activeShip = player.ActiveShip();
            if (activeShip != null)
            {
                AddFittedModules(activeShip, activeShip.Fitting.High, "high");
                AddFittedModules(activeShip, activeShip.Fitting.Mid, "mid");
                AddFittedModules(activeShip, activeShip.Fitting.Low, "low");
            }
            foreach (var pair in player.Hangar)
            {
                if (pair.Value <= 0 || !catalog.Modules.TryGetValue(pair.Key, out var module)) continue;
                snapshot.Inventory.Add(Item(FitModuleActionPrefix + pair.Key,
                    Tr("HANGAR · {0} ×{1}", Tr(module.Name), pair.Value),
                    Tr("Click to fit the first compatible free slot")));
            }

            snapshot.Missions.Clear();
            foreach (var mission in player.Missions.Where(value => value.Status != MissionStatus.Done))
                snapshot.Missions.Add(Item(mission.Id, Tr(mission.Title), Tr(mission.Status.ToString()) + " · " + mission.ProgressText()));

            snapshot.Skills.Clear();
            foreach (var skill in catalog.Skills.Values)
            {
                var level = player.SkillLevels.TryGetValue(skill.Id, out var trained) ? trained : 0;
                var queueIndex = player.SkillQueue.IndexOf(skill.Id);
                string status;
                if (level >= SkillRules.MaxLevel) status = Tr("MAX");
                else if (queueIndex == 0) status = Tr("TRAINING");
                else if (queueIndex > 0) status = Tr("QUEUED #{0}", (queueIndex + 1).ToString("0", Inv));
                else status = Tr("REQUIRED FOR: {0}", SkillClassGates(skill.Id));
                var progress = string.Empty;
                if (level < SkillRules.MaxLevel)
                {
                    var needed = SkillRules.PointsToNextLevel(skill.Rank, level);
                    var points = player.SkillPoints.TryGetValue(skill.Id, out var partial) ? partial : 0d;
                    progress = " · " + (points / needed).ToString("0%", Inv);
                }
                snapshot.Skills.Add(Item(skill.Id,
                    Tr(skill.Name) + " · L" + level.ToString("0", Inv) + "/" + SkillRules.MaxLevel.ToString("0", Inv) + progress + " · " + status,
                    Tr(skill.Description)));
            }

            snapshot.LpStore.Clear();
            foreach (var offer in GameSession.LoyaltyOffers)
            {
                var module = catalog.Modules[offer.ModuleId];
                snapshot.LpStore.Add(Item("lp-item|" + module.Id,
                    Tr(module.Name) + " · " + offer.Cost.ToString("N0", Inv) + " LP",
                    Tr(module.Description)));
            }
        }

        /// <summary>Short list of what a gating skill unlocks, for the skill row detail.</summary>
        private string SkillClassGates(string skillId)
        {
            if (skillId != SkillIds.SpaceshipCommand) return Tr("various equipment");
            var gates = new List<string>();
            foreach (var ship in catalog.Ships.Values)
                if (!ship.NpcOnly && SkillRules.RequiredForShipClass(ship.Class) > 1)
                    gates.Add(Tr(ship.Name) + " L" + SkillRules.RequiredForShipClass(ship.Class).ToString("0", Inv));
            return gates.Count > 0 ? string.Join(Tr(", "), gates) : Tr("various equipment");
        }

        private string PriceText(string itemId)
        {
            // The authoritative transaction price remains in GameSession. This deterministic preview mirrors its broad range.
            long basePrice = catalog.Modules.TryGetValue(itemId, out var module) ? module.Price :
                catalog.Ships.TryGetValue(itemId, out var ship) ? ship.Price :
                catalog.Items.TryGetValue(itemId, out var item) ? item.BasePrice : 0;
            return Tr("{0} ISK base", basePrice.ToString("N0", Inv));
        }

        private void MarketAction(string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return;
            if (TryActionPayload(actionId, SellItemActionPrefix, out var itemId))
            {
                if (catalog.Items.ContainsKey(itemId)) Queue(GameCommandType.Sell, itemId);
                return;
            }
            if (TryActionPayload(actionId, SellModuleActionPrefix, out var moduleId))
            {
                if (catalog.Modules.ContainsKey(moduleId)) Queue(GameCommandType.Sell, moduleId);
                return;
            }
            if (TryActionPayload(actionId, SellShipActionPrefix, out var shipInstanceId))
            {
                Queue(GameCommandType.SellShip, shipInstanceId);
                return;
            }
            if (catalog.Modules.ContainsKey(actionId) ||
                catalog.Ships.TryGetValue(actionId, out var ship) && !ship.NpcOnly)
                Queue(GameCommandType.Buy, actionId);
        }

        private void FittingAction(string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return;
            if (TryActionPayload(actionId, UnfitActionPrefix, out var unfitArgument))
            {
                if (unfitArgument.Split('|').Length == 3) Queue(GameCommandType.Unfit, unfitArgument);
                return;
            }
            if (TryActionPayload(actionId, FitModuleActionPrefix, out var moduleId))
            {
                FitFirstAvailable(moduleId);
                return;
            }
            FitFirstAvailable(actionId);
        }

        private void FitFirstAvailable(string moduleId)
        {
            if (!catalog.Modules.TryGetValue(moduleId, out var module)) return;
            var ship = session.State.Player.ActiveShip();
            if (ship == null) return;
            var slotName = module.Slot.ToString().ToLowerInvariant();
            var slots = module.Slot == SlotType.High ? ship.Fitting.High : module.Slot == SlotType.Mid ? ship.Fitting.Mid : ship.Fitting.Low;
            var skillLevel = new Func<string, int>(skillId =>
                session.State.Player.SkillLevels.TryGetValue(skillId, out var level) ? level : 0);
            for (var index = 0; index < slots.Count; index++)
            {
                if (!string.IsNullOrEmpty(slots[index])) continue;
                if (FittingRules.CanFitModule(catalog, ship, slotName, index, moduleId, skillLevel, out var reason, out var reasonArgs))
                {
                    Queue(GameCommandType.Fit, ship.InstanceId + "|" + slotName + "|" + index + "|" + moduleId);
                    return;
                }
                AddLog(reasonArgs == null ? Tr(reason) : Tr(reason, reasonArgs));
                return;
            }
            AddLog(Tr("No compatible free slot."));
        }

        private void AddFittedModules(ShipInstanceState ship, IReadOnlyList<string> slots, string slotName)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                var moduleId = slots[i];
                if (string.IsNullOrEmpty(moduleId) || !catalog.Modules.TryGetValue(moduleId, out var module)) continue;
                snapshot.Inventory.Add(Item(
                    UnfitActionPrefix + ship.InstanceId + "|" + slotName + "|" + i,
                    Tr("FITTED · {0} {1} · {2}", Tr(slotName.ToUpperInvariant()), i + 1, Tr(module.Name)),
                    Tr("Click to unfit this module to the hangar")));
            }
        }

        private static bool TryActionPayload(string actionId, string prefix, out string payload)
        {
            payload = string.Empty;
            if (string.IsNullOrEmpty(actionId) || !actionId.StartsWith(prefix, StringComparison.Ordinal) ||
                actionId.Length == prefix.Length) return false;
            payload = actionId.Substring(prefix.Length);
            return true;
        }

        private void MissionAction(string missionId)
        {
            var mission = session.State.Player.Missions.Find(value => value.Id == missionId);
            if (mission == null) return;
            if (mission.Status == MissionStatus.Offered) Queue(GameCommandType.AcceptMission, missionId);
            else if (mission.Status == MissionStatus.ObjectivesMet) Queue(GameCommandType.CompleteMission, missionId);
            else AddLog(Tr("{0} — {1}", Tr(mission.Title), mission.ProgressText()));
        }

        private void Save(SaveSlot slot, bool announce = true)
        {
            if (session == null) return;
            try
            {
                var state = session.State;
                var player = JObject.FromObject(state.Player, serializer);
                var payload = new JObject
                {
                    ["name"] = state.Player.Name,
                    ["credits"] = state.Player.Credits,
                    // Persisting the death flag is what makes dying stick: the
                    // death save itself records PlayerDead = true.
                    ["playerDead"] = state.PlayerDead,
                    // Wall-clock stamp drives offline skill training on the next load.
                    ["savedAtUtc"] = DateTime.UtcNow.Ticks,
                    ["runtime"] = player,
                };
                saves.Save(slot, new SaveEnvelopeV2
                {
                    Seed = state.Seed,
                    SimulationTime = state.SimulationTime,
                    PlayerLocation = new PlayerLocationV2(state.Player.CurrentSystemId,
                        string.IsNullOrEmpty(state.Player.DockedAtStationId) ? null : state.Player.DockedAtStationId,
                        state.Player.X, state.Player.Z),
                    Player = payload,
                    RngState = state.RngState,
                    NextEntityId = state.NextEntityId,
                });
                if (announce) AddLog(Tr("Game saved to {0}.", Tr(slot.ToString().ToLowerInvariant())));
            }
            catch (Exception exception)
            {
                // Mobile storage can be temporarily unavailable (disk full,
                // unmounted volume); a failed save must never take the app down.
                Debug.LogException(exception);
                AddLog(Tr("Save failed: {0}.", exception.Message));
            }
        }

        private void LoadEnvelope(SaveEnvelopeV2 envelope)
        {
            var universe = generator.Generate(envelope.Seed);
            PlayerState player;
            if (envelope.Player["runtime"] is JObject runtime)
            {
                player = runtime.ToObject<PlayerState>(serializer);
            }
            else
            {
                player = ConvertLegacyPlayer(envelope.Player, envelope.PlayerLocation);
            }
            player.CurrentSystemId = envelope.PlayerLocation.SystemId;
            player.DockedAtStationId = envelope.PlayerLocation.DockedAt ?? string.Empty;
            player.X = envelope.PlayerLocation.X;
            player.Z = envelope.PlayerLocation.Z;
            var playerDead = envelope.Player.Value<bool?>("playerDead") ?? false;
            session = new GameSession(universe, catalog, player, envelope.SimulationTime, envelope.RngState, envelope.NextEntityId, playerDead);
            ApplyOfflineTraining(envelope.Player.Value<long?>("savedAtUtc"));
            log.Clear();
            AddLog(Tr("Save loaded."));
            MarkAllDirty();
            RefreshUiSnapshot(true);
            RequestScene(session.State.Docked ? "Station" : "Space");
        }

        private PlayerState ConvertLegacyPlayer(JObject source, PlayerLocationV2 location)
        {
            var player = new PlayerState
            {
                Name = source.Value<string>("name") ?? "Pilot",
                EmpireId = source.Value<string>("empire") ?? FactionIds.Aurelian,
                Credits = source.Value<long?>("credits") ?? 50000L,
                CurrentSystemId = location.SystemId,
                DockedAtStationId = location.DockedAt ?? string.Empty,
                X = location.X,
                Z = location.Z,
                HomeSystemId = source.Value<string>("homeSystemId") ?? location.SystemId,
                HomeStationId = source.Value<string>("homeStationId") ?? location.DockedAt ?? string.Empty,
                ActiveShipInstanceId = source.Value<string>("activeShip") ?? string.Empty,
                CriminalTimer = source.Value<double?>("criminalTimer") ?? 0d,
                DestinationSystemId = source.Value<string>("destination") ?? string.Empty,
            };
            CopyStringDouble(source["standings"], player.Standings);
            CopyStringInt(source["lp"], player.LoyaltyPoints);
            CopyStringDouble(source["cargo"], player.Cargo);
            CopyStringInt(source["hangar"], player.Hangar);
            CopyStringInt(source["missionCounts"], player.MissionCounts);
            if (source["ships"] is JArray ships)
            {
                foreach (var token in ships.OfType<JObject>())
                {
                    var shipId = token.Value<string>("shipId");
                    if (!catalog.Ships.TryGetValue(shipId, out var definition)) continue;
                    var fitting = new FittingState();
                    CopySlots(token["fitting"]?["high"], fitting.High, definition.Slots.High);
                    CopySlots(token["fitting"]?["mid"], fitting.Mid, definition.Slots.Mid);
                    CopySlots(token["fitting"]?["low"], fitting.Low, definition.Slots.Low);
                    player.Ships.Add(new ShipInstanceState
                    {
                        InstanceId = token.Value<string>("instId") ?? "ship_start",
                        ShipId = shipId,
                        Name = token.Value<string>("name") ?? definition.Name,
                        Fitting = fitting,
                        Shield = token["hp"]?.Value<double?>("shield") ?? definition.HitPoints.Shield,
                        Armor = token["hp"]?.Value<double?>("armor") ?? definition.HitPoints.Armor,
                        Hull = token["hp"]?.Value<double?>("hull") ?? definition.HitPoints.Hull,
                    });
                }
            }
            if (player.Ships.Count == 0)
            {
                var starterId = player.EmpireId == FactionIds.Kaldari ? ShipIds.Shrike : player.EmpireId == FactionIds.Meridian ? ShipIds.Wasp : player.EmpireId == FactionIds.Varkhald ? ShipIds.Fang : ShipIds.Acolyte;
                var definition = catalog.Ships[starterId];
                var fitting = new FittingState();
                CopySlots(null, fitting.High, definition.Slots.High);
                CopySlots(null, fitting.Mid, definition.Slots.Mid);
                CopySlots(null, fitting.Low, definition.Slots.Low);
                var fallback = new ShipInstanceState { InstanceId = "ship_start", ShipId = starterId, Name = definition.Name, Fitting = fitting, Shield = definition.HitPoints.Shield, Armor = definition.HitPoints.Armor, Hull = definition.HitPoints.Hull };
                player.Ships.Add(fallback);
                player.ActiveShipInstanceId = fallback.InstanceId;
            }
            return player;
        }

        private LegacyReferenceCatalog BuildLegacyReferences(GeneratedUniverse universe)
        {
            var stations = new List<string>();
            var agents = new List<string>();
            foreach (var system in universe.OrderedSystems)
                foreach (var station in system.Stations)
                {
                    stations.Add(station.Id);
                    foreach (var agent in station.Agents) agents.Add(agent.Id);
                }
            return LegacyReferenceCatalog.FromContent(catalog, universe.Systems.Keys, stations, agents);
        }

        private string FindLegacySave()
        {
            var explicitPath = Path.Combine(Application.persistentDataPath, "legacy-v1.json");
            if (File.Exists(explicitPath)) return explicitPath;
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloads)) return null;
            return Directory.GetFiles(downloads, "starfall_save_*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }

        private static uint ReadLegacySeed(byte[] bytes)
        {
            var root = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            return root.Value<uint?>("seed") ?? DefaultSeed;
        }

        private void AddMapLog()
        {
            var state = session.State;
            var current = state.Universe.Systems[state.Player.CurrentSystemId];
            var neighbors = state.Universe.Adjacency[current.Id].Select(id => TrName(state.Universe.Systems[id].Name));
            var joined = string.Join(", ", neighbors);
            AddLog(mapVisible ? Tr("Starmap open · Adjacent: {0}", joined) : Tr("Starmap closed · {0}", joined));
        }

        private void AddJournalLog()
        {
            var missions = session.State.Player.Missions.Where(value => value.Status != MissionStatus.Done).ToList();
            AddLog(missions.Count == 0 ? Tr("Journal: no active missions.")
                : Tr("Journal: {0}", string.Join(" · ", missions.Select(value => Tr(value.Title) + " [" + value.ProgressText() + "]"))));
        }

        private void AddPilotLog()
        {
            var player = session.State.Player;
            AddLog(Tr("Pilot {0} · {1} kills · {2} missions · {3} ore · {4} jumps.",
                player.Name, player.Stats.Kills, player.Stats.MissionsDone,
                player.Stats.OreMined.ToString("0", Inv), player.Stats.Jumps));
            if (player.SkillQueue.Count > 0 && catalog.Skills.TryGetValue(player.SkillQueue[0], out var training))
                AddLog(Tr("Now training: {0}.", Tr(training.Name)));
        }

        private string ActiveMissionSummary()
        {
            var mission = session.State.Player.Missions.Find(value => value.Status == MissionStatus.Active || value.Status == MissionStatus.ObjectivesMet);
            return mission == null ? Tr("No active mission") : Tr(mission.Title) + " · " + mission.ProgressText();
        }

        /// <summary>Power grid / CPU readout for the fitting panel header.</summary>
        private string FittingSummary()
        {
            var ship = session.State.Player.ActiveShip();
            if (ship == null) return string.Empty;
            var hull = catalog.Ships[ship.ShipId];
            FittingRules.FittingUsage(catalog, ship, out var grid, out var cpu);
            return Tr("PG {0}/{1} · CPU {2}/{3}",
                grid.ToString("0", Inv), hull.PowerGrid.ToString("0", Inv),
                cpu.ToString("0", Inv), hull.Cpu.ToString("0", Inv));
        }

        private string SelectedName(string id)
        {
            if (string.IsNullOrEmpty(id)) return Tr("No target");
            var entity = session.State.FindEntity(id);
            if (entity != null) return TrName(entity.Name);
            var asteroid = session.State.FindAsteroid(id);
            if (asteroid != null) return Tr(catalog.Items[asteroid.OreId].Name);
            var system = session.State.Universe.Systems[session.State.Player.CurrentSystemId];
            var station = system.Stations.Find(value => value.Id == id); if (station != null) return TrName(station.Name);
            var gate = system.Gates.Find(value => value.Id == id); if (gate != null) return TrName(gate.Name);
            var belt = system.Belts.Find(value => value.Id == id); if (belt != null) return TrName(belt.Name);
            if (string.Equals(id, system.Id + "_star", StringComparison.Ordinal)) return TrName(system.Name + " Star");
            var planet = system.Planets.Find(value => value.Id == id); if (planet != null) return TrName(planet.Name);
            for (var i = 0; i < system.Planets.Count; i++)
            {
                var moon = system.Planets[i].Moons.Find(value => value.Id == id);
                if (moon != null) return TrName(moon.Name);
            }
            return id;
        }

        private string SelectedDetail(string id)
        {
            if (string.IsNullOrEmpty(id)) return Tr("Select an object in space");
            if (!TryWorldPosition(id, out var target)) return Tr("Command target");
            var player = session.State.PlayerEntity();
            return player == null ? Tr("Docked") : Tr("{0} m", SimVec2.Distance(player.Position, target).ToString("0"));
        }

        private bool TryWorldPosition(string id, out SimVec2 result)
        {
            var entity = session.State.FindEntity(id); if (entity != null) { result = entity.Position; return true; }
            var asteroid = session.State.FindAsteroid(id); if (asteroid != null) { result = asteroid.Position; return true; }
            var system = session.State.Universe.Systems[session.State.Player.CurrentSystemId];
            var station = system.Stations.Find(value => value.Id == id); if (station != null) { result = station.Position; return true; }
            var gate = system.Gates.Find(value => value.Id == id); if (gate != null) { result = gate.Position; return true; }
            var belt = system.Belts.Find(value => value.Id == id); if (belt != null) { result = belt.Position; return true; }
            if (string.Equals(id, system.Id + "_star", StringComparison.Ordinal)) { result = SimVec2.Zero; return true; }
            var planet = system.Planets.Find(value => value.Id == id); if (planet != null) { result = planet.Position; return true; }
            for (var i = 0; i < system.Planets.Count; i++)
            {
                var moon = system.Planets[i].Moons.Find(value => value.Id == id);
                if (moon != null) { result = moon.Position; return true; }
            }
            result = SimVec2.Zero;
            return false;
        }

        private bool IsHostile(EntityState entity)
        {
            if (entity == null || session == null) return false;
            var player = session.State.PlayerEntity();
            return EntityDispositionPolicy.Evaluate(entity, session.State.Player, catalog,
                       player != null ? player.Id : "player") == EntityDisposition.Hostile;
        }

        private Color FactionColor(string factionId)
        {
            return catalog.Factions.TryGetValue(factionId, out var faction) ? ParseColor(faction.Color) : new Color(0.35f, 0.8f, 1f);
        }

        private static Color ParseColor(string html)
        {
            return ColorUtility.TryParseHtmlString(html, out var color) ? color : Color.white;
        }

        private static WorldObjectViewData WorldObject(string id, string name, WorldViewKind kind, SimVec2 position, float radius, Color color)
        {
            // Slot reuse means every field must be (re)written: the slot may
            // have held a ship on the previous frame.
            var view = NextSpaceObjectInstance();
            view.Id = id;
            view.Name = name;
            view.Kind = kind;
            view.Position = new Vector3((float)position.X, 0f, (float)position.Z);
            view.Radius = radius;
            view.Color = color;
            view.ShipId = string.Empty;
            view.ShipClass = "frigate";
            view.FactionId = string.Empty;
            view.IsPlayer = false;
            view.IsHostile = false;
            view.HeadingDegrees = 0f;
            view.Shield01 = 1f;
            view.Armor01 = 1f;
            view.Hull01 = 1f;
            return view;
        }

        private static WorldObjectViewData NextSpaceObjectInstance()
        {
            // Static relay into the instance pool; BuildSpaceSnapshot resets
            // the counter right before it starts consuming slots.
            return instance.NextSpaceObject();
        }

        private static UiListItem Item(string id, string title, string detail)
        {
            return new UiListItem { Id = id, Title = title, Detail = detail };
        }

        private void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            log.Add(message);
            if (log.Count > 80) log.RemoveRange(0, log.Count - 80);
            MarkUiDirty();
        }

        /// <summary>
        /// Credits the wall-clock gap between save and load to the skill queue.
        /// Thresholds skip the noise of quick reloads; long absences are capped
        /// inside the simulation (SkillRules.MaxOfflineSeconds).
        /// </summary>
        private void ApplyOfflineTraining(long? savedAtUtcTicks)
        {
            if (session == null || !savedAtUtcTicks.HasValue) return;
            var elapsedSeconds = (DateTime.UtcNow.Ticks - savedAtUtcTicks.Value) / (double)TimeSpan.TicksPerSecond;
            if (elapsedSeconds < 30d) return;
            HandleEvents(session.ApplyOfflineTraining(elapsedSeconds));
        }

        private static void CopyStringDouble(JToken source, Dictionary<string, double> target)
        {
            if (!(source is JObject obj)) return;
            foreach (var property in obj.Properties()) target[property.Name] = property.Value.Value<double>();
        }

        private static void CopyStringInt(JToken source, Dictionary<string, int> target)
        {
            if (!(source is JObject obj)) return;
            foreach (var property in obj.Properties()) target[property.Name] = property.Value.Value<int>();
        }

        private static void CopySlots(JToken source, List<string> target, int count)
        {
            var array = source as JArray;
            for (var i = 0; i < count; i++) target.Add(array != null && i < array.Count && array[i].Type != JTokenType.Null ? array[i].Value<string>() : null);
        }
    }
}
