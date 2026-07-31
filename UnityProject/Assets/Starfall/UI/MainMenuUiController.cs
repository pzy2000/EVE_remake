using UnityEngine;
using UnityEngine.UIElements;

namespace Starfall.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuUiController : MonoBehaviour, IMobileBackHandler
    {
        private UIDocument document;
        private TextField pilotName;
        private Label empireDescription;
        private Label legacyImportStatus;
        private Label continueSummary;
        private Button continueButton;
        private IStarfallUiHost host;
        private StarfallSettingsPanel settingsPanel;
        private MobileUiCoordinator mobileUi;
        private ConfirmationOverlay confirmation;
        private string empireId = "aurelian";

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            mobileUi = MobileUiCoordinator.Attach(document, MobileScreenKind.MainMenu);
            var root = document.rootVisualElement;
            pilotName = root.Q<TextField>("pilot-name");
            empireDescription = root.Q<Label>("empire-description");
            legacyImportStatus = root.Q<Label>("legacy-import-status");
            continueSummary = root.Q<Label>("continue-summary");
            continueButton = root.Q<Button>("continue");
            BindEmpire(root, "empire-aurelian", "aurelian", "Golden laser specialists with resilient armor.");
            BindEmpire(root, "empire-kaldari", "kaldari", "Missile and railgun doctrine backed by massive shields.");
            BindEmpire(root, "empire-meridian", "meridian", "Fast close-range blaster ships built for decisive brawls.");
            BindEmpire(root, "empire-varkhald", "varkhald", "Rugged projectile vessels with unmatched sublight speed.");
            root.Q<Button>("launch")?.RegisterCallback<ClickEvent>(_ => LaunchNewPilot());
            continueButton?.RegisterCallback<ClickEvent>(_ => host?.ContinueGame());
            root.Q<Button>("import")?.RegisterCallback<ClickEvent>(_ => host?.ImportLegacy());
            StarfallUiBridge.HostChanged += BindHost;
            BindHost();
            var safeRoot = root.Q<VisualElement>("main-menu") ?? root;
            settingsPanel = new StarfallSettingsPanel(safeRoot);
            confirmation = new ConfirmationOverlay(safeRoot);
            mobileUi?.ReapplyLayout();
            MobileBackNavigation.Current = this;
        }

        private void OnDisable()
        {
            StarfallUiBridge.HostChanged -= BindHost;
            if (host != null) host.SnapshotChanged -= RefreshLegacyImportStatus;
            host = null;
            settingsPanel?.Dispose();
            settingsPanel = null;
            confirmation?.Dispose();
            confirmation = null;
            if (ReferenceEquals(MobileBackNavigation.Current, this)) MobileBackNavigation.Current = null;
            mobileUi?.Dispose();
            mobileUi = null;
        }

        private void BindHost()
        {
            if (host != null) host.SnapshotChanged -= RefreshLegacyImportStatus;
            host = StarfallUiBridge.Host;
            if (host != null) host.SnapshotChanged += RefreshLegacyImportStatus;
            RefreshLegacyImportStatus();
        }

        private void RefreshLegacyImportStatus()
        {
            if (legacyImportStatus == null) return;
            var message = host?.LegacyImportStatus ?? string.Empty;
            legacyImportStatus.text = message;
            legacyImportStatus.style.display = string.IsNullOrWhiteSpace(message)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            legacyImportStatus.EnableInClassList("danger", host?.LegacyImportStatusIsError == true);
            if (continueSummary != null) continueSummary.text = host?.Snapshot?.ContinueSummary ?? "No valid save found";
            continueButton?.SetEnabled(host?.Snapshot?.CanContinue == true);
        }

        public bool HandleMobileBack()
        {
            if (confirmation?.IsOpen == true)
            {
                confirmation.Close();
                return true;
            }
            if (settingsPanel?.IsOpen == true)
            {
                settingsPanel.Close();
                return true;
            }
            confirmation?.Show("EXIT STARFALL ODYSSEY?",
                "Your imported and saved games remain on this device.", "EXIT",
                () => StarfallUiBridge.Host?.QuitGame());
            return confirmation != null;
        }

        private void BindEmpire(VisualElement root, string elementName, string id, string description)
        {
            var button = root.Q<Button>(elementName);
            if (button == null) return;
            button.clicked += () =>
            {
                empireId = id;
                root.Query<Button>(className: "empire-card").ForEach(card => card.RemoveFromClassList("chosen"));
                button.AddToClassList("chosen");
                if (empireDescription != null) empireDescription.text = description;
            };
        }

        private void LaunchNewPilot()
        {
            var name = string.IsNullOrWhiteSpace(pilotName?.value) ? "Pilot" : pilotName.value.Trim();
            if (host?.Snapshot?.CanContinue != true)
            {
                host?.StartNewGame(name, empireId);
                return;
            }
            confirmation?.Show("START A NEW PILOT?",
                "This replaces the current autosave. Manual saves remain available.",
                "START NEW", () => host?.StartNewGame(name, empireId));
        }
    }
}
