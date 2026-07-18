using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starfall.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class SpaceHudController : MonoBehaviour
    {
        private UIDocument document;
        private VisualElement root;
        private IStarfallUiHost host;

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            root = document.rootVisualElement;
            root.Q<VisualElement>(className: "hud").pickingMode = PickingMode.Ignore;
            root.Q<VisualElement>("map-overlay").pickingMode = PickingMode.Position;
            root.Q<VisualElement>("journal-overlay").pickingMode = PickingMode.Position;
            root.Q<VisualElement>("death-overlay").pickingMode = PickingMode.Position;
            BindButtons();
            StarfallUiBridge.HostChanged += BindHost;
            BindHost();
        }

        private void OnDisable()
        {
            StarfallUiBridge.HostChanged -= BindHost;
            if (host != null) host.SnapshotChanged -= Refresh;
        }

        private void BindHost()
        {
            if (host != null) host.SnapshotChanged -= Refresh;
            host = StarfallUiBridge.Host;
            if (host != null) host.SnapshotChanged += Refresh;
            Refresh();
        }

        private void BindButtons()
        {
            Bind("approach", "approach");
            Bind("orbit", "orbit");
            Bind("warp", "warp");
            Bind("lock", "lock");
            Bind("dock", "dock");
            Bind("map", "map");
            Bind("journal", "journal");
            Bind("pilot", "pilot");
            Bind("save", "save");
            Bind("settings", "settings");
            Bind("respawn", "respawn");
            Bind("map-close", "map");
            Bind("journal-close", "journal");
            for (var i = 0; i < 9; i++)
            {
                var index = i;
                root.Q<Button>($"module-{i + 1}")?.RegisterCallback<ClickEvent>(_ => host?.Execute("module", index.ToString()));
            }
        }

        private void Bind(string name, string command) =>
            root.Q<Button>(name)?.RegisterCallback<ClickEvent>(_ => host?.Execute(command));

        private void Refresh()
        {
            if (root == null || host?.Snapshot == null) return;
            var s = host.Snapshot;
            SetText("system-name", s.SystemName);
            SetText("security", $"SEC {s.Security:0.0}");
            SetText("pilot-name", s.PilotName);
            SetText("credits", $"{s.Credits:N0} ISK");
            SetText("ship-name", $"{s.ShipName} · {s.ShipClass}");
            SetText("speed", $"{s.Speed:0} m/s");
            SetText("target-name", s.SelectedName);
            SetText("target-detail", s.SelectedDetail);
            SetText("mission", s.MissionSummary);
            SetBar("shield", s.Shield01);
            SetBar("armor", s.Armor01);
            SetBar("hull", s.Hull01);
            RebuildList(root.Q<ScrollView>("overview-list"), s.Overview, item => host.Execute("select", item.Id));
            RebuildList(root.Q<ScrollView>("starmap-list"), s.Starmap, item => host.Execute("destination", item.Id));
            RebuildList(root.Q<ScrollView>("journal-list"), s.Missions, item => host.Execute("mission", item.Id));
            var log = root.Q<Label>("combat-log");
            if (log != null) log.text = string.Join("\n", s.Log.Count > 8 ? s.Log.GetRange(s.Log.Count - 8, 8) : s.Log);
            for (var i = 0; i < 9; i++)
            {
                var button = root.Q<Button>($"module-{i + 1}");
                if (button == null) continue;
                if (i < s.Modules.Count)
                {
                    var module = s.Modules[i];
                    button.text = $"{i + 1}\n{module.Name}";
                    button.EnableInClassList("active", module.Active);
                    button.SetEnabled(true);
                }
                else
                {
                    button.text = $"{i + 1}\n—";
                    button.SetEnabled(false);
                }
            }
            Toggle("death-overlay", s.PlayerDead);
            Toggle("map-overlay", s.MapVisible);
            Toggle("journal-overlay", s.JournalVisible);
        }

        private void SetText(string name, string text)
        {
            if (root.Q<Label>(name) is { } label) label.text = text;
        }

        private void SetBar(string name, float value)
        {
            if (root.Q<VisualElement>(name) is { } bar) bar.style.width = Length.Percent(Mathf.Clamp01(value) * 100f);
        }

        private void Toggle(string name, bool visible)
        {
            if (root.Q<VisualElement>(name) is { } element)
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void RebuildList(ScrollView list, System.Collections.Generic.IReadOnlyList<UiListItem> items, Action<UiListItem> clicked)
        {
            if (list == null) return;
            list.Clear();
            foreach (var item in items)
            {
                var button = new Button(() => clicked(item)) { text = item.Title };
                button.AddToClassList("overview-row");
                button.tooltip = item.Detail;
                list.Add(button);
            }
        }
    }
}
