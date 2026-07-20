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

#if UNITY_EDITOR
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Starfall.Mobile.EditModeTests")]
#endif

namespace Starfall.App
{
    [DefaultExecutionOrder(-1000)]
    public sealed partial class AppRoot : MonoBehaviour, IStarfallUiHost
    {
        private const uint DefaultSeed = 12345u;
        private const float UiTelemetryRefreshInterval = 0.1f;
        private const string SellItemActionPrefix = "sell-item|";
        private const string SellModuleActionPrefix = "sell-module|";
        private const string FitModuleActionPrefix = "fit-module|";
        private const string UnfitActionPrefix = "unfit|";
        private const string QualityPreferenceKey = "starfall.quality";
        private const string LegacyImportCacheDirectory = "legacy-import";
        private const string LegacyImportCachePrefix = "legacy-v1-";
        private const float AndroidLegacyImportPollInterval = 0.25f;
        private static AppRoot instance;
        private readonly UiSnapshot snapshot = new UiSnapshot();
        private readonly List<string> log = new List<string>();
        private readonly LifecycleSaveCoordinator lifecycleSaves = new LifecycleSaveCoordinator();
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
        private AndroidWindowMetricsProvider androidWindowMetrics;
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
        private string presentedStationShipInstanceId = string.Empty;
        private string legacyImportStatus = string.Empty;
        private bool legacyImportStatusIsError;
        private float nextAndroidLegacyImportPollTime;

        public UiSnapshot Snapshot => snapshot;
        public float MusicVolume => musicDirector ? musicDirector.MusicVolume : MusicDirector.DefaultMusicVolume;
        public bool MusicMuted => musicDirector && musicDirector.Muted;
        public string LegacyImportStatus => legacyImportStatus;
        public bool LegacyImportStatusIsError => legacyImportStatusIsError;
        public string QualityPreset
        {
            get
            {
                var names = QualitySettings.names;
                var index = QualitySettings.GetQualityLevel();
                return names.Length > 0 && index >= 0 && index < names.Length ? names[index] : "Default";
            }
        }
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
            catalog = GameContentCatalog.Default;
            generator = new UniverseGenerator();
            saves = new FileSaveService();
            legacyImporter = new LegacyV1Importer();
            RestoreQualityPreference();
            androidWindowMetrics = new AndroidWindowMetricsProvider();
            MobileWindowing.ProviderFactory = () => androidWindowMetrics;
            musicDirector = GetComponent<MusicDirector>();
            if (!musicDirector) musicDirector = gameObject.AddComponent<MusicDirector>();
            musicDirector.SettingsChanged += OnMusicSettingsChanged;
            StarfallUiBridge.Bind(this);
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.lowMemory += OnLowMemory;
            AndroidPlatformBridge.Initialize(gameObject.name);
        }

        private void Start()
        {
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            if (SceneManager.GetActiveScene().name == "Bootstrap") RequestScene("MainMenu");
#if STARFALL_ANDROID_CI
            StartCoroutine(WriteAndroidCiRenderReadyEvidence());
#endif
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.lowMemory -= OnLowMemory;
            androidWindowMetrics?.Dispose();
            androidWindowMetrics = null;
            MobileWindowing.ProviderFactory = null;
            AndroidPlatformBridge.Shutdown();
            if (musicDirector) musicDirector.SettingsChanged -= OnMusicSettingsChanged;
            instance = null;
        }

        private void Update()
        {
            PollPendingAndroidLegacyDocument();
            HandleKeyboard();
            if (session == null) return;
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
            AddLog($"Welcome to the stars, {session.State.Player.Name}.");
            AddLog("Talk to an agent, undock, then use click, W/L/D and modules 1–9.");
            Save(SaveSlot.Auto);
            MarkAllDirty();
            RefreshUiSnapshot(true);
            RequestScene("Station");
        }

        public void ContinueGame()
        {
            SaveEnvelopeV2 envelope = null;
            foreach (var slot in new[] { SaveSlot.Auto, SaveSlot.Slot1, SaveSlot.Slot2, SaveSlot.Slot3 })
            {
                try
                {
                    envelope = saves.Load(slot);
                    break;
                }
                catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is InvalidOperationException)
                {
                    // Try the next explicit slot. FileSaveService already attempts .bak recovery.
                }
            }
            if (envelope == null)
            {
                AddLog("No valid save slot was found.");
                SnapshotChanged?.Invoke();
                return;
            }
            LoadEnvelope(envelope);
        }

        public void ImportLegacy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!AndroidPlatformBridge.OpenLegacyDocumentPicker(out var error))
                OnAndroidLegacyDocumentPickerError("picker_unavailable:" + error);
            return;
#else
            var path = FindLegacySave();
            if (path == null)
            {
                ReportLegacyImport(
                    "Place a legacy JSON save in Downloads or as persistentDataPath/legacy-v1.json, then try again.",
                    true);
                return;
            }
            try
            {
                ImportLegacyBytes(File.ReadAllBytes(path));
            }
            catch (Exception exception)
            {
                ReportLegacyImport("Legacy import failed: " + exception.Message, true);
            }
#endif
        }

        public bool ImportLegacyBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                ReportLegacyImport("Legacy import rejected: the selected file is empty.", true);
                return false;
            }
            if (bytes.Length > LegacyV1Importer.MaximumSourceBytes)
            {
                ReportLegacyImport("Legacy import rejected: the selected file exceeds 5 MiB.", true);
                return false;
            }
            try
            {
                var seed = ReadLegacySeed(bytes);
                var universe = generator.Generate(seed);
                var references = BuildLegacyReferences(universe);
                var converted = legacyImporter.Convert(bytes, references);
                if (!converted.IsSuccess)
                {
                    ReportLegacyImport("Legacy import rejected: " + converted.Inspection.ErrorMessage, true);
                    return false;
                }
                saves.Save(SaveSlot.Slot1, converted.Envelope);
                LoadEnvelope(converted.Envelope);
                ReportLegacyImport("Legacy v1 imported to slot1. The source file was not modified.", false);
                return true;
            }
            catch (Exception exception)
            {
                ReportLegacyImport("Legacy import failed: " + exception.Message, true);
                return false;
            }
        }

        public void Execute(string command, string argument = null)
        {
            if (command == "settings")
            {
                CycleQuality();
                return;
            }
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
                case "lp-exchange": Queue(GameCommandType.ExchangeLoyalty); break;
                case "ship": Queue(GameCommandType.SwitchShip, argument); break;
                case "mission": MissionAction(argument); break;
                case "destination":
                    Queue(GameCommandType.SetDestination, argument);
                    mapVisible = false;
                    MarkUiDirty(true);
                    break;
                case "map":
                    mapVisible = !mapVisible;
                    if (mapVisible) journalVisible = false;
                    MarkUiDirty(mapVisible);
                    AddMapLog();
                    break;
                case "journal":
                    journalVisible = !journalVisible;
                    if (journalVisible) mapVisible = false;
                    AddJournalLog();
                    break;
                case "pilot": AddPilotLog(); break;
            }
        }

        public void SetMusicVolume(float value)
        {
            if (musicDirector) musicDirector.SetMusicVolume(value);
        }

        public void SetMusicMuted(bool value)
        {
            if (musicDirector) musicDirector.SetMuted(value);
        }

        public void CycleQuality()
        {
            var next = NextQualityLevel();
            QualitySettings.SetQualityLevel(next, true);
            PlayerPrefs.SetInt(QualityPreferenceKey, next);
            PlayerPrefs.Save();
            AddLog("Quality preset: " + QualityPreset + ".");
            SettingsChanged?.Invoke();
        }

        private void OnMusicSettingsChanged()
        {
            SettingsChanged?.Invoke();
        }

        private static int NextQualityLevel()
        {
            return QualityLevelPolicy.Next(QualitySettings.names, QualitySettings.GetQualityLevel(),
                Application.isMobilePlatform);
        }

        private static void RestoreQualityPreference()
        {
            var current = QualitySettings.GetQualityLevel();
            var persisted = PlayerPrefs.HasKey(QualityPreferenceKey)
                ? PlayerPrefs.GetInt(QualityPreferenceKey)
                : current;
            var resolved = QualityLevelPolicy.ResolvePersisted(QualitySettings.names, persisted, current,
                Application.isMobilePlatform);
            if (resolved != current) QualitySettings.SetQualityLevel(resolved, true);
        }

        public void ReturnToMainMenu()
        {
            TrySaveAuto("return to main menu");
            session = null;
            mapVisible = false;
            journalVisible = false;
            loadedGameplayScene = string.Empty;
            RequestScene("MainMenu");
        }

        public void QuitGame()
        {
            TrySaveAuto("quit");
            Application.Quit();
        }

        private void Queue(GameCommandType type, string argument = null)
        {
            if (session == null) return;
            session.Enqueue(new GameCommand(type, string.IsNullOrEmpty(argument) ? session.State.SelectedId : argument));
        }

        private void HandleKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
#if !UNITY_ANDROID || UNITY_EDITOR
            // Android system Back is owned by StarfallMobileBridge on every supported
            // API. GameActivity can also expose the committed key through Input System;
            // handling escape here would close and immediately reopen the top overlay.
            if (keyboard.escapeKey.wasPressedThisFrame) HandleMobileBack("keyboard");
#endif
            if (session == null) return;
            if (keyboard.wKey.wasPressedThisFrame) Queue(GameCommandType.Warp);
            if (keyboard.lKey.wasPressedThisFrame) Queue(GameCommandType.Lock);
            if (keyboard.dKey.wasPressedThisFrame) Queue(GameCommandType.DockOrJump);
            if (keyboard.mKey.wasPressedThisFrame) Execute("map");
            if (keyboard.jKey.wasPressedThisFrame) Execute("journal");
            if (keyboard.cKey.wasPressedThisFrame) Execute("pilot");
            if (keyboard.hKey.wasPressedThisFrame) AddLog("Help: click to select · double-click approach · W warp · L lock · D dock/jump · V/X focus · 1–9 modules.");
            for (var i = 0; i < 9; i++)
            {
                var key = i == 0 ? keyboard.digit1Key : i == 1 ? keyboard.digit2Key : i == 2 ? keyboard.digit3Key :
                    i == 3 ? keyboard.digit4Key : i == 4 ? keyboard.digit5Key : i == 5 ? keyboard.digit6Key :
                    i == 6 ? keyboard.digit7Key : i == 7 ? keyboard.digit8Key : keyboard.digit9Key;
                if (key.wasPressedThisFrame) session.Enqueue(new GameCommand(GameCommandType.ActivateModule, index: i));
            }
        }

        private void OnApplicationPause(bool paused)
        {
            lifecycleSaves.SetPaused(paused, () => Save(SaveSlot.Auto), OnLifecycleSaveError);
        }

        private void OnApplicationFocus(bool focused)
        {
            lifecycleSaves.SetFocused(focused, () => Save(SaveSlot.Auto), OnLifecycleSaveError);
        }

        private void OnLifecycleSaveError(Exception exception)
        {
            AddLog("Autosave failed: " + exception.Message);
        }

        private void TrySaveAuto(string reason)
        {
            if (session == null) return;
            try
            {
                Save(SaveSlot.Auto);
            }
            catch (Exception exception)
            {
                AddLog("Autosave failed during " + reason + ": " + exception.Message);
            }
        }

        private void OnLowMemory()
        {
            var releasedTransientVfx = spacePresenter
                ? spacePresenter.ReleaseTransientResources()
                : 0;
            var releasedSpaceCacheEntries = ProceduralSpaceMaterials.ReleaseUnusedRuntimeCaches();
            var releasedShipCacheEntries = ProceduralShipFactory.ReleaseUnusedRuntimeCaches();
            Resources.UnloadUnusedAssets();
            worldDirty = true;
#if STARFALL_ANDROID_CI
            WriteAndroidCiLowMemoryEvidence(
                releasedTransientVfx,
                releasedSpaceCacheEntries,
                releasedShipCacheEntries);
#endif
        }

        private void HandleMobileBack(string dispatchId)
        {
            var handler = MobileBackNavigation.Current;
            var handled = handler != null && handler.HandleMobileBack();
#if STARFALL_ANDROID_CI
            Debug.Log("STARFALL_ANDROID_BACK_MANAGED_DISPATCH="
                      + (string.IsNullOrEmpty(dispatchId) ? "unknown" : dispatchId)
                      + ":handled=" + handled
                      + ":handler=" + (handler == null ? "none" : handler.GetType().Name));
#endif
            if (handled) return;
            AddLog("Back action is unavailable while this screen is loading.");
        }

        public void OnAndroidBackInvoked(string payload)
        {
            HandleMobileBack(payload);
        }

        public void OnAndroidWindowLayoutInfo(string json)
        {
            AndroidPlatformBridge.PublishWindowLayoutInfo(json);
        }

        public void OnAndroidLegacyDocumentPicked(string absoluteCachePath)
        {
            if (string.IsNullOrWhiteSpace(absoluteCachePath))
            {
                OnAndroidLegacyDocumentPickerError("empty_path:No document was returned.");
                return;
            }
            try
            {
                if (!AndroidPlatformBridge.TryGetLegacyImportCacheRoot(out var cacheRoot, out var cacheError))
                    throw new InvalidOperationException("Could not verify the Android import cache: " + cacheError);
                var bytes = ReadAndDeleteLegacyImportCache(
                    absoluteCachePath, cacheRoot, out var cleanupWarning);
                if (!string.IsNullOrEmpty(cleanupWarning))
                    Debug.LogWarning("Could not clean legacy import cache: " + cleanupWarning);
                ImportLegacyBytes(bytes);
            }
            catch (Exception exception)
            {
                ReportLegacyImport("Legacy import failed: " + exception.Message, true);
            }
            finally
            {
                AndroidPlatformBridge.AcknowledgeLegacyDocument(absoluteCachePath);
            }
        }

        private void PollPendingAndroidLegacyDocument()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Time.unscaledTime < nextAndroidLegacyImportPollTime) return;
            nextAndroidLegacyImportPollTime = Time.unscaledTime + AndroidLegacyImportPollInterval;
            if (AndroidPlatformBridge.TryGetPendingLegacyDocument(out var path, out _))
                OnAndroidLegacyDocumentPicked(path);
#endif
        }

        internal static byte[] ReadAndDeleteLegacyImportCache(
            string untrustedPath, string cacheRootPath, out string cleanupWarning)
        {
            cleanupWarning = string.Empty;
            string validatedCachePath = null;
            try
            {
                validatedCachePath = ValidateLegacyImportCachePath(untrustedPath, cacheRootPath);
                return ReadBoundedLegacyImport(validatedCachePath);
            }
            finally
            {
                if (validatedCachePath != null)
                {
                    try
                    {
                        if (File.Exists(validatedCachePath)) File.Delete(validatedCachePath);
                    }
                    catch (Exception exception)
                    {
                        cleanupWarning = exception.Message;
                    }
                }
            }
        }

        internal static string ValidateLegacyImportCachePath(string untrustedPath, string cacheRootPath)
        {
            if (string.IsNullOrWhiteSpace(untrustedPath) || string.IsNullOrWhiteSpace(cacheRootPath))
                throw new InvalidDataException("The picker returned an invalid cache path.");

            var fullPath = Path.GetFullPath(untrustedPath);
            var importRoot = Path.GetFullPath(Path.Combine(cacheRootPath, LegacyImportCacheDirectory))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parentDirectory = Path.GetDirectoryName(fullPath)?.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fileName = Path.GetFileName(fullPath);
            if (!string.Equals(parentDirectory, importRoot, StringComparison.Ordinal) ||
                !fileName.StartsWith(LegacyImportCachePrefix, StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The picker returned an invalid cache path.");
            return fullPath;
        }

        internal static byte[] ReadBoundedLegacyImport(string validatedCachePath)
        {
            using var stream = new FileStream(
                validatedCachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > LegacyV1Importer.MaximumSourceBytes)
                throw new InvalidDataException("The selected file exceeds 5 MiB.");

            var bytes = new byte[(int)stream.Length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) throw new EndOfStreamException("The selected file changed while it was being read.");
                offset += read;
            }
            if (stream.ReadByte() != -1)
                throw new InvalidDataException("The selected file changed while it was being read.");
            return bytes;
        }

        public void OnAndroidLegacyDocumentPickerError(string error)
        {
            if (string.IsNullOrWhiteSpace(error) || error.StartsWith("cancelled", StringComparison.OrdinalIgnoreCase))
                ReportLegacyImport("Legacy import cancelled.", false);
            else
            {
                var separator = error.IndexOf(':');
                var detail = separator >= 0 && separator + 1 < error.Length
                    ? error.Substring(separator + 1).Trim()
                    : error.Trim();
                ReportLegacyImport("Legacy import failed: " + detail, true);
            }
        }

        private void ReportLegacyImport(string message, bool isError)
        {
            legacyImportStatus = message ?? string.Empty;
            legacyImportStatusIsError = isError;
            AddLog(legacyImportStatus);
            // MainMenu has no live session, so it cannot rely on the normal dirty-snapshot update loop.
            SnapshotChanged?.Invoke();
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
                    evt.Type == SimulationEventType.Inventory || evt.Type == SimulationEventType.Dock || evt.Type == SimulationEventType.Jump))
                    AddLog(evt.Message);
                if (evt.Type == SimulationEventType.SaveRequested)
                    Save(evt.Detail == "auto" ? SaveSlot.Auto : SaveSlot.Slot1);
                if (evt.Type == SimulationEventType.Inventory) stationVisualDirty = true;
                if (spacePresenter && evt.Type == SimulationEventType.Weapon)
                {
                    var color = evt.Detail != null && evt.Detail.StartsWith("mining", StringComparison.Ordinal)
                        ? new Color(0.35f, 1f, 0.42f) : new Color(1f, 0.68f, 0.18f);
                    spacePresenter.FireBeam(evt.SourceId, evt.TargetId, color);
                }
                if (spacePresenter && evt.Type == SimulationEventType.Death)
                {
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
            if (musicDirector) musicDirector.PlayForScene(scene.name);
            loadedGameplayScene = scene.name == "Space" || scene.name == "Station" ? scene.name : string.Empty;
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
            AddLog("Context: Approach · Orbit · Warp · Lock · Dock/Jump.");
        }

        private void RequestScene(string sceneName)
        {
            if (sceneTransitionQueued || SceneManager.GetActiveScene().name == sceneName) return;
            sceneTransitionQueued = true;
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
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

        private SpaceSnapshot BuildSpaceSnapshot()
        {
            var state = session.State;
            var system = state.Universe.Systems[state.Player.CurrentSystemId];
            var result = new SpaceSnapshot
            {
                SystemId = system.Id,
                SystemName = system.Name,
                FactionId = system.FactionId,
                Security = (float)system.Security,
                FactionColor = FactionColor(system.FactionId),
                SelectedId = state.SelectedId,
            };
            result.Objects.Add(new WorldObjectViewData
            {
                Id = system.Id + "_star", Name = system.Name + " Star", Kind = WorldViewKind.Star,
                Position = Vector3.zero, Radius = Mathf.Max(18f, (float)system.Star.Radius * 0.3f), Color = ParseColor(system.Star.Color),
            });
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
                result.Objects.Add(new WorldObjectViewData
                {
                    Id = entity.Id,
                    Name = entity.Name,
                    Kind = WorldViewKind.Ship,
                    Position = new Vector3((float)entity.Position.X, 0f, (float)entity.Position.Z),
                    Radius = definition.Class == ShipClass.Battleship ? 10f : definition.Class == ShipClass.Cruiser ? 7f : definition.Class == ShipClass.Destroyer ? 5f : 3f,
                    Color = FactionColor(entity.FactionId),
                    ShipId = entity.ShipId,
                    ShipClass = definition.Class.ToString().ToLowerInvariant(),
                    FactionId = entity.FactionId,
                    IsPlayer = entity.Kind == EntityKind.Player,
                    IsHostile = IsHostile(entity),
                    HeadingDegrees = (float)(-entity.HeadingRadians * Mathf.Rad2Deg + 90f),
                    Shield01 = (float)(entity.Shield / Math.Max(1d, entity.MaxShield)),
                    Armor01 = (float)(entity.Armor / Math.Max(1d, entity.MaxArmor)),
                    Hull01 = (float)(entity.Hull / Math.Max(1d, entity.MaxHull)),
                });
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
            snapshot.EmpireName = catalog.Factions[player.EmpireId].Name;
            snapshot.SystemName = system.Name + (mapVisible ? " · STARMAP" : string.Empty);
            snapshot.Security = (float)system.Security;
            snapshot.ShipName = ship != null ? ship.Name : "No ship";
            snapshot.ShipClass = shipDefinition != null ? shipDefinition.Class.ToString() : string.Empty;
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
                        Name = definition.Name,
                        Slot = definition.Slot.ToString(),
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
            foreach (var mapSystem in state.Universe.OrderedSystems)
            {
                var route = UniverseRoutes.FindRoute(state.Universe, player.CurrentSystemId, mapSystem.Id);
                var jumps = route == null ? "NO ROUTE" : (route.Count - 1) + " jumps";
                var destination = player.DestinationSystemId == mapSystem.Id ? " · DESTINATION" : string.Empty;
                snapshot.Starmap.Add(Item(mapSystem.Id,
                    mapSystem.Name + " · SEC " + mapSystem.Security.ToString("0.0") + destination,
                    jumps + " · " + catalog.Factions[mapSystem.FactionId].Name));
            }

            snapshot.Agents.Clear();
            var dockedStation = system.Stations.Find(value => value.Id == player.DockedAtStationId);
            if (dockedStation != null)
                foreach (var agent in dockedStation.Agents) snapshot.Agents.Add(Item(agent.Id, agent.Name + " · L" + agent.Level, agent.Division));

            snapshot.Market.Clear();
            foreach (var module in catalog.Modules.Values) snapshot.Market.Add(Item(module.Id, module.Name + " · " + PriceText(module.Id), module.Description));
            foreach (var ship in catalog.Ships.Values) if (!ship.NpcOnly) snapshot.Market.Add(Item(ship.Id, ship.Name + " · " + PriceText(ship.Id), ship.Description));
            foreach (var pair in player.Cargo)
            {
                if (pair.Value <= 0d || pair.Key == ItemIds.SealedCargo || !catalog.Items.TryGetValue(pair.Key, out var item)) continue;
                snapshot.Market.Add(Item(SellItemActionPrefix + pair.Key,
                    "SELL CARGO · " + item.Name + " ×" + pair.Value.ToString("0"),
                    "Sell the full stack · " + PriceText(pair.Key) + " per unit base"));
            }
            foreach (var pair in player.Hangar)
            {
                if (pair.Value <= 0 || !catalog.Modules.TryGetValue(pair.Key, out var module)) continue;
                snapshot.Market.Add(Item(SellModuleActionPrefix + pair.Key,
                    "SELL HANGAR · " + module.Name + " ×" + pair.Value,
                    "Sell one module · " + PriceText(pair.Key)));
            }

            snapshot.Ships.Clear();
            foreach (var owned in player.Ships) snapshot.Ships.Add(Item(owned.InstanceId, (owned.InstanceId == player.ActiveShipInstanceId ? "ACTIVE · " : string.Empty) + owned.Name, catalog.Ships[owned.ShipId].Description));

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
                    "HANGAR · " + module.Name + " ×" + pair.Value,
                    "Click to fit the first compatible free slot"));
            }

            snapshot.Missions.Clear();
            foreach (var mission in player.Missions.Where(value => value.Status != MissionStatus.Done))
                snapshot.Missions.Add(Item(mission.Id, mission.Title, mission.Status + " · " + mission.ProgressText()));
        }

        private string PriceText(string itemId)
        {
            // The authoritative transaction price remains in GameSession. This deterministic preview mirrors its broad range.
            long basePrice = catalog.Modules.TryGetValue(itemId, out var module) ? module.Price :
                catalog.Ships.TryGetValue(itemId, out var ship) ? ship.Price :
                catalog.Items.TryGetValue(itemId, out var item) ? item.BasePrice : 0;
            return basePrice.ToString("N0") + " ISK base";
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
            var index = slots.FindIndex(string.IsNullOrEmpty);
            if (index >= 0) Queue(GameCommandType.Fit, ship.InstanceId + "|" + slotName + "|" + index + "|" + moduleId);
            else AddLog("No compatible free slot.");
        }

        private void AddFittedModules(ShipInstanceState ship, IReadOnlyList<string> slots, string slotName)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                var moduleId = slots[i];
                if (string.IsNullOrEmpty(moduleId) || !catalog.Modules.TryGetValue(moduleId, out var module)) continue;
                snapshot.Inventory.Add(Item(
                    UnfitActionPrefix + ship.InstanceId + "|" + slotName + "|" + i,
                    "FITTED · " + slotName.ToUpperInvariant() + " " + (i + 1) + " · " + module.Name,
                    "Click to unfit this module to the hangar"));
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
            else AddLog(mission.Title + " — " + mission.ProgressText());
        }

        private void Save(SaveSlot slot)
        {
            if (session == null) return;
            var state = session.State;
            var player = JObject.FromObject(state.Player, serializer);
            var payload = new JObject
            {
                ["name"] = state.Player.Name,
                ["credits"] = state.Player.Credits,
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
            AddLog("Game saved to " + slot.ToString().ToLowerInvariant() + ".");
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
            session = new GameSession(universe, catalog, player, envelope.SimulationTime, envelope.RngState, envelope.NextEntityId);
            log.Clear();
            AddLog("Save loaded.");
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
            var neighbors = state.Universe.Adjacency[current.Id].Select(id => state.Universe.Systems[id].Name);
            AddLog((mapVisible ? "Starmap open · Adjacent: " : "Starmap closed · ") + string.Join(", ", neighbors));
        }

        private void AddJournalLog()
        {
            var missions = session.State.Player.Missions.Where(value => value.Status != MissionStatus.Done).ToList();
            AddLog(missions.Count == 0 ? "Journal: no active missions." : "Journal: " + string.Join(" · ", missions.Select(value => value.Title + " [" + value.ProgressText() + "]")));
        }

        private void AddPilotLog()
        {
            var player = session.State.Player;
            AddLog($"Pilot {player.Name} · {player.Stats.Kills} kills · {player.Stats.MissionsDone} missions · {player.Stats.OreMined:0} ore · {player.Stats.Jumps} jumps.");
        }

        private string ActiveMissionSummary()
        {
            var mission = session.State.Player.Missions.Find(value => value.Status == MissionStatus.Active || value.Status == MissionStatus.ObjectivesMet);
            return mission == null ? "No active mission" : mission.Title + " · " + mission.ProgressText();
        }

        private string SelectedName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "No target";
            var entity = session.State.FindEntity(id);
            if (entity != null) return entity.Name;
            var asteroid = session.State.FindAsteroid(id);
            if (asteroid != null) return catalog.Items[asteroid.OreId].Name;
            var system = session.State.Universe.Systems[session.State.Player.CurrentSystemId];
            var station = system.Stations.Find(value => value.Id == id); if (station != null) return station.Name;
            var gate = system.Gates.Find(value => value.Id == id); if (gate != null) return gate.Name;
            var belt = system.Belts.Find(value => value.Id == id); if (belt != null) return belt.Name;
            if (string.Equals(id, system.Id + "_star", StringComparison.Ordinal)) return system.Name + " Star";
            var planet = system.Planets.Find(value => value.Id == id); if (planet != null) return planet.Name;
            for (var i = 0; i < system.Planets.Count; i++)
            {
                var moon = system.Planets[i].Moons.Find(value => value.Id == id);
                if (moon != null) return moon.Name;
            }
            return id;
        }

        private string SelectedDetail(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Select an object in space";
            if (!TryWorldPosition(id, out var target)) return "Command target";
            var player = session.State.PlayerEntity();
            return player == null ? "Docked" : SimVec2.Distance(player.Position, target).ToString("0") + " m";
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
            return new WorldObjectViewData { Id = id, Name = name, Kind = kind, Position = new Vector3((float)position.X, 0f, (float)position.Z), Radius = radius, Color = color };
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
