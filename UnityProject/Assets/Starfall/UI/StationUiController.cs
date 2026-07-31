using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starfall.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class StationUiController : MonoBehaviour, IMobileBackHandler
    {
        private UIDocument document;
        private VisualElement root;
        private IStarfallUiHost host;
        private StarfallSettingsPanel settingsPanel;
        private MobileUiCoordinator mobileUi;
        private ConfirmationOverlay confirmation;
        private readonly string[] tabs = { "agents", "market", "fitting", "ships", "lp" };

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            mobileUi = MobileUiCoordinator.Attach(document, MobileScreenKind.Station);
            root = document.rootVisualElement;
            root.Q<VisualElement>(className: "station-ui").pickingMode = PickingMode.Ignore;
            foreach (var tab in tabs)
            {
                var captured = tab;
                root.Q<Button>($"tab-{tab}")?.RegisterCallback<ClickEvent>(_ => ShowTab(captured));
            }
            root.Q<Button>("undock")?.RegisterCallback<ClickEvent>(_ => host?.Execute("undock"));
            root.Q<Button>("repair")?.RegisterCallback<ClickEvent>(_ => host?.Execute("repair"));
            root.Q<Button>("save")?.RegisterCallback<ClickEvent>(_ => host?.Execute("save"));
            root.Q<Button>("lp-exchange")?.RegisterCallback<ClickEvent>(_ => host?.Execute("lp-exchange"));
            StarfallUiBridge.HostChanged += BindHost;
            BindHost();
            ShowTab("agents");
            var safeRoot = root.Q<VisualElement>("station-ui") ?? root;
            settingsPanel = new StarfallSettingsPanel(safeRoot, open => host?.SetGameplayOverlayOpen(open));
            confirmation = new ConfirmationOverlay(safeRoot, open => host?.SetGameplayOverlayOpen(open));
            mobileUi?.ReapplyLayout();
            MobileBackNavigation.Current = this;
        }

        private void OnDisable()
        {
            StarfallUiBridge.HostChanged -= BindHost;
            if (host != null) host.SnapshotChanged -= Refresh;
            settingsPanel?.Dispose();
            settingsPanel = null;
            confirmation?.Dispose();
            confirmation = null;
            if (ReferenceEquals(MobileBackNavigation.Current, this)) MobileBackNavigation.Current = null;
            mobileUi?.Dispose();
            mobileUi = null;
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
            confirmation?.Show("RETURN TO MAIN MENU?",
                "The current game will be saved before returning.", "SAVE & RETURN",
                () => host?.ReturnToMainMenu());
            return confirmation != null;
        }

        private void BindHost()
        {
            if (host != null) host.SnapshotChanged -= Refresh;
            host = StarfallUiBridge.Host;
            if (host != null) host.SnapshotChanged += Refresh;
            Refresh();
        }

        private void ShowTab(string id)
        {
            foreach (var tab in tabs)
            {
                if (root.Q<VisualElement>($"panel-{tab}") is { } panel)
                    panel.style.display = tab == id ? DisplayStyle.Flex : DisplayStyle.None;
                root.Q<Button>($"tab-{tab}")?.EnableInClassList("active", tab == id);
            }
        }

        private void Refresh()
        {
            if (root == null || host?.Snapshot == null) return;
            var s = host.Snapshot;
            root.Q<Label>("station-title").text = $"{s.SystemName} ORBITAL";
            root.Q<Label>("pilot-summary").text = $"{s.PilotName} · {s.ShipName} · {s.Credits:N0} ISK · {s.LoyaltyPoints:N0} LP";
            root.Q<Label>("progression-summary").text = s.ProgressionSummary;
            Fill("agents-list", s.Agents, "agent");
            Fill("market-list", s.Market, "market");
            Fill("ships-list", s.Ships, "ship");
            Fill("fitting-list", s.Inventory, "fit");
            root.Q<Label>("lp-summary").text = $"Available loyalty points: {s.LoyaltyPoints:N0}";
            var status = root.Q<Label>("station-status");
            if (status != null) status.text = s.Log.Count > 0 ? s.Log[s.Log.Count - 1] : "Station services ready.";
        }

        private void Fill(string elementName, IReadOnlyList<UiListItem> items, string command)
        {
            var list = root.Q<ScrollView>(elementName);
            if (list == null) return;
            list.Clear();
            foreach (var item in items)
            {
                var row = new VisualElement();
                row.AddToClassList("service-row");
                var copy = item;
                var copyBlock = new VisualElement();
                copyBlock.AddToClassList("service-copy");
                var title = new Label(item.Title);
                title.AddToClassList("service-title");
                var detail = new Label(item.Detail);
                detail.AddToClassList("service-detail");
                copyBlock.Add(title);
                copyBlock.Add(detail);
                row.Add(copyBlock);
                var action = new Button(() => host.Execute(command, copy.Id))
                {
                    text = item.Enabled ? ActionLabel(command, copy.Id) : "LOCKED",
                };
                action.SetEnabled(item.Enabled);
                row.Add(action);
                list.Add(row);
            }
        }

        private static string ActionLabel(string command, string actionId)
        {
            if (command == "agent") return "TALK";
            if (command == "market")
                return actionId != null && actionId.StartsWith("sell-", StringComparison.Ordinal) ? "SELL" : "BUY";
            if (command == "fit")
                return actionId != null && actionId.StartsWith("unfit|", StringComparison.Ordinal) ? "UNFIT" : "FIT";
            if (command == "ship") return "ACTIVATE";
            return "SELECT";
        }
    }
}
