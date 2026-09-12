using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Starfall.Domain;
using static Starfall.Domain.L10n;

namespace Starfall.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class StationUiController : MonoBehaviour
    {
        private UIDocument document;
        private VisualElement root;
        private IStarfallUiHost host;
        private StarfallSettingsPanel settingsPanel;
        private IDisposable responsiveUi;
        private readonly string[] tabs = { "agents", "market", "fitting", "ships", "skills", "lp" };
        private ulong listsFingerprint = ulong.MaxValue;
        private static readonly ulong FingerprintOffset = 14695981039346656037UL;
        private static readonly ulong FingerprintPrime = 1099511628211UL;

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
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
            settingsPanel = new StarfallSettingsPanel(root);
            responsiveUi?.Dispose();
            responsiveUi = StarfallResponsiveUi.Attach(document);
            L10n.LanguageChanged += OnLanguageChanged;
            UiLocalizer.Apply(root);
        }

        private void OnDisable()
        {
            L10n.LanguageChanged -= OnLanguageChanged;
            StarfallUiBridge.HostChanged -= BindHost;
            if (host != null) host.SnapshotChanged -= Refresh;
            settingsPanel?.Dispose();
            settingsPanel = null;
            responsiveUi?.Dispose();
            responsiveUi = null;
        }

        private void BindHost()
        {
            if (host != null) host.SnapshotChanged -= Refresh;
            host = StarfallUiBridge.Host;
            if (host != null) host.SnapshotChanged += Refresh;
            Refresh();
        }

        private void OnLanguageChanged()
        {
            UiLocalizer.Apply(root);
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
            root.Q<Label>("station-title").text = Tr("{0} ORBITAL", s.SystemName);
            root.Q<Label>("pilot-summary").text = $"{s.PilotName} · {s.ShipName} · {s.Credits:N0} ISK · {s.LoyaltyPoints:N0} LP";
            // SnapshotChanged fires on every damage/log event; the four lists
            // only change when their contents actually differ, so guard the
            // rebuild behind a fingerprint like the HUD lists do.
            var fingerprint = Fingerprint(s);
            if (fingerprint != listsFingerprint)
            {
                listsFingerprint = fingerprint;
                Fill("agents-list", s.Agents, "agent");
                Fill("market-list", s.Market, "market");
                Fill("ships-list", s.Ships, "ship");
                Fill("fitting-list", s.Inventory, "fit");
                Fill("skills-list", s.Skills, "train");
            }
            root.Q<Label>("lp-summary").text = Tr("Available loyalty points: {0}", s.LoyaltyPoints.ToString("N0"));
        }

        private static ulong Fingerprint(UiSnapshot s)
        {
            var hash = FingerprintOffset;
            void Mix(IReadOnlyList<UiListItem> list)
            {
                hash = (hash ^ (ulong)list.Count) * FingerprintPrime;
                for (var i = 0; i < list.Count; i++)
                {
                    hash = (hash ^ (ulong)System.StringComparer.Ordinal.GetHashCode(list[i].Id)) * FingerprintPrime;
                    hash = (hash ^ (ulong)System.StringComparer.Ordinal.GetHashCode(list[i].Title)) * FingerprintPrime;
                    hash = (hash ^ (ulong)System.StringComparer.Ordinal.GetHashCode(list[i].Detail)) * FingerprintPrime;
                }
            }
            Mix(s.Agents);
            Mix(s.Market);
            Mix(s.Ships);
            Mix(s.Inventory);
            Mix(s.Skills);
            return hash;
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
                row.Add(new Label(item.Title) { tooltip = item.Detail });
                row.Add(new Button(() => host.Execute(command, copy.Id)) { text = ActionLabel(command, copy.Id) });
                list.Add(row);
            }
        }

        private static string ActionLabel(string command, string actionId)
        {
            if (command == "agent") return Tr("TALK");
            if (command == "market")
                return actionId != null && actionId.StartsWith("sell-", StringComparison.Ordinal) ? Tr("SELL") : Tr("BUY");
            if (command == "fit")
                return actionId != null && actionId.StartsWith("unfit|", StringComparison.Ordinal) ? Tr("UNFIT") : Tr("FIT");
            if (command == "ship") return Tr("ACTIVATE");
            if (command == "train") return Tr("TRAIN");
            return Tr("SELECT");
        }
    }
}
