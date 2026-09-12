using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Starfall.Domain;
using static Starfall.Domain.L10n;

namespace Starfall.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class SpaceHudController : MonoBehaviour
    {
        private const float OverviewRowHeight = 40f;
        private const double DockInteractionDistance = 40d;
        private const double JumpInteractionDistance = 35d;
        private const string OverviewPreferencePrefix = "starfall.overview.v1";
        private const ulong ListFingerprintOffset = 14695981039346656037UL;
        private const ulong ListFingerprintPrime = 1099511628211UL;

        private readonly List<UiOverviewContact> filteredOverview = new();
        private readonly List<UiOverviewContact> visibleOverview = new();
        private readonly Dictionary<string, UiOverviewContact> filteredById = new(StringComparer.Ordinal);
        private readonly HashSet<string> visibleIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> staleIds = new(StringComparer.Ordinal);
        private readonly OverviewSortColumn[] sortColumns = new OverviewSortColumn[5];
        private readonly OverviewSortDirection[] sortDirections = new OverviewSortDirection[5];
        private readonly Button[] presetButtons = new Button[5];
        private readonly Button[] sortButtons = new Button[5];
        private readonly Button[] moduleButtons = new Button[9];

        private UIDocument document;
        private VisualElement root;
        private IStarfallUiHost host;
        private ListView overviewList;
        private Label overviewCount;
        private Label overviewEmpty;
        private ScrollView starmapList;
        private ScrollView journalList;
        private Button approachButton;
        private Button orbitButton;
        private Button warpButton;
        private Button lockButton;
        private Button dockButton;
        private StarfallSettingsPanel settingsPanel;
        private WorldBackdropInput worldBackdropInput;
        private VisualElement hudElement;
        private Vector4 appliedSafeArea = new Vector4(-1f, -1f, -1f, -1f);
        private IDisposable responsiveUi;
        private OverviewPresetId activePreset;
        private string selectedContactId = string.Empty;
        private bool controlsBound;
        private bool pointerOverOverview;
        private bool pointerPressedInOverview;
        private bool orderFrozen;
        private bool orderDirty;
        private bool starmapCacheValid;
        private bool journalCacheValid;
        private ulong starmapFingerprint;
        private ulong journalFingerprint;

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            var nextRoot = document.rootVisualElement;
            if (!ReferenceEquals(root, nextRoot))
            {
                root = nextRoot;
                controlsBound = false;
                overviewList = null;
                starmapList = null;
                journalList = null;
            }

            starmapList = root.Q<ScrollView>("starmap-list");
            journalList = root.Q<ScrollView>("journal-list");
            ResetAuxiliaryListCaches();

            // The HUD root doubles as the world-gesture backdrop: it must stay
            // pickable so taps in empty space reach WorldBackdropInput, while the
            // panels above it keep consuming their own events.
            if (root.Q<VisualElement>(className: "hud") is { } hud)
            {
                hud.pickingMode = PickingMode.Position;
                hudElement = hud;
                hud.RegisterCallback<GeometryChangedEvent>(OnHudGeometryChanged);
                worldBackdropInput = new WorldBackdropInput(hud);
            }
            if (root.Q<VisualElement>("map-overlay") is { } mapOverlay) mapOverlay.pickingMode = PickingMode.Position;
            if (root.Q<VisualElement>("journal-overlay") is { } journalOverlay) journalOverlay.pickingMode = PickingMode.Position;
            if (root.Q<VisualElement>("death-overlay") is { } deathOverlay) deathOverlay.pickingMode = PickingMode.Position;

            if (!controlsBound)
            {
                BindButtons();
                SetupOverview();
                controlsBound = true;
            }

            StarfallUiBridge.HostChanged += BindHost;
            BindHost();
            settingsPanel = new StarfallSettingsPanel(root);
            responsiveUi?.Dispose();
            responsiveUi = StarfallResponsiveUi.Attach(document, overviewList);
            L10n.LanguageChanged += OnLanguageChanged;
            UiLocalizer.Apply(root);
        }

        private void OnDisable()
        {
            L10n.LanguageChanged -= OnLanguageChanged;
            StarfallUiBridge.HostChanged -= BindHost;
            if (hudElement != null) hudElement.UnregisterCallback<GeometryChangedEvent>(OnHudGeometryChanged);
            if (host != null)
            {
                host.SnapshotChanged -= Refresh;
                host.TelemetryChanged -= RefreshTelemetry;
            }
            settingsPanel?.Dispose();
            settingsPanel = null;
            responsiveUi?.Dispose();
            responsiveUi = null;
            PlayerPrefs.Save();
        }

        // Keep the HUD out of camera cutouts and status bars. Re-evaluated on
        // every layout pass; margins are idempotent so this converges.
        private void OnHudGeometryChanged(GeometryChangedEvent evt)
        {
            if (hudElement == null || hudElement.panel == null) return;
            var area = Screen.safeArea;
            var pixelsPerPoint = hudElement.panel.scaledPixelsPerPoint;
            if (pixelsPerPoint <= 0f) pixelsPerPoint = 1f;
            var left = area.x / pixelsPerPoint;
            var top = area.y / pixelsPerPoint;
            var right = (Screen.width - area.xMax) / pixelsPerPoint;
            var bottom = (Screen.height - area.yMax) / pixelsPerPoint;
            if (appliedSafeArea == new Vector4(left, top, right, bottom)) return;
            appliedSafeArea = new Vector4(left, top, right, bottom);
            hudElement.style.marginLeft = left;
            hudElement.style.marginTop = top;
            hudElement.style.marginRight = right;
            hudElement.style.marginBottom = bottom;
        }

        private void BindHost()
        {
            if (host != null)
            {
                host.SnapshotChanged -= Refresh;
                host.TelemetryChanged -= RefreshTelemetry;
            }
            host = StarfallUiBridge.Host;
            if (host != null)
            {
                host.SnapshotChanged += Refresh;
                host.TelemetryChanged += RefreshTelemetry;
            }
            Refresh();
        }

        private void OnLanguageChanged()
        {
            UiLocalizer.Apply(root);
            ResetAuxiliaryListCaches();
            UpdateSortButtons();
            Refresh();
        }

        private void BindButtons()
        {
            approachButton = Bind("approach", "approach");
            orbitButton = Bind("orbit", "orbit");
            warpButton = Bind("warp", "warp");
            lockButton = Bind("lock", "lock");
            dockButton = Bind("dock", "dock");
            Bind("map", "map");
            Bind("journal", "journal");
            Bind("pilot", "pilot");
            Bind("save", "save");
            Bind("respawn", "respawn");
            Bind("map-close", "map");
            Bind("journal-close", "journal");
            for (var i = 0; i < 9; i++)
            {
                var index = i;
                var button = root.Q<Button>($"module-{i + 1}");
                moduleButtons[i] = button;
                button?.RegisterCallback<ClickEvent>(_ => host?.Execute("module", index.ToString()));
            }
        }

        private Button Bind(string name, string command)
        {
            var button = root.Q<Button>(name);
            button?.RegisterCallback<ClickEvent>(_ => host?.Execute(command));
            return button;
        }

        private void SetupOverview()
        {
            LoadOverviewPreferences();
            overviewList = root.Q<ListView>("overview-list");
            overviewCount = root.Q<Label>("overview-count");
            overviewEmpty = root.Q<Label>("overview-empty");
            if (overviewList == null) return;

            overviewList.fixedItemHeight = OverviewRowHeight;
            overviewList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            overviewList.selectionType = SelectionType.Single;
            overviewList.reorderable = false;
            overviewList.horizontalScrollingEnabled = false;
            overviewList.itemsSource = visibleOverview;
            overviewList.makeItem = () => new OverviewRow(SelectOverviewContact, ShowContactDetail);
            overviewList.bindItem = BindOverviewItem;
            overviewList.unbindItem = UnbindOverviewItem;

            RegisterPresetButton(OverviewPresetId.General, "overview-tab-general");
            RegisterPresetButton(OverviewPresetId.Combat, "overview-tab-combat");
            RegisterPresetButton(OverviewPresetId.Mining, "overview-tab-mining");
            RegisterPresetButton(OverviewPresetId.Travel, "overview-tab-travel");
            RegisterPresetButton(OverviewPresetId.All, "overview-tab-all");

            RegisterSortButton(OverviewSortColumn.Threat, "overview-sort-threat");
            RegisterSortButton(OverviewSortColumn.Distance, "overview-sort-distance");
            RegisterSortButton(OverviewSortColumn.Name, "overview-sort-name");
            RegisterSortButton(OverviewSortColumn.Type, "overview-sort-type");
            RegisterSortButton(OverviewSortColumn.Velocity, "overview-sort-velocity");

            overviewList.RegisterCallback<PointerEnterEvent>(_ =>
            {
                pointerOverOverview = true;
                UpdateOrderFreeze();
            });
            overviewList.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                pointerOverOverview = false;
                UpdateOrderFreeze();
            });
            overviewList.RegisterCallback<PointerDownEvent>(_ =>
            {
                pointerPressedInOverview = true;
                UpdateOrderFreeze();
            });
            overviewList.RegisterCallback<PointerUpEvent>(_ => ReleaseOverviewPointer());
            overviewList.RegisterCallback<PointerCaptureOutEvent>(_ => ReleaseOverviewPointer());
            root.RegisterCallback<PointerUpEvent>(_ => ReleaseOverviewPointer());

            UpdatePresetButtons();
            UpdateSortButtons();
            overviewList.Rebuild();
        }

        private void RegisterPresetButton(OverviewPresetId preset, string elementName)
        {
            var button = root.Q<Button>(elementName);
            presetButtons[(int)preset] = button;
            button?.RegisterCallback<ClickEvent>(_ => SetActivePreset(preset));
        }

        private void RegisterSortButton(OverviewSortColumn column, string elementName)
        {
            var button = root.Q<Button>(elementName);
            sortButtons[(int)column] = button;
            button?.RegisterCallback<ClickEvent>(_ => SetSortColumn(column));
        }

        private void Refresh()
        {
            if (root == null || host?.Snapshot == null) return;
            var snapshot = host.Snapshot;
            SetText("system-name", snapshot.SystemName);
            SetText("security", Tr("SEC {0}", snapshot.Security.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));
            SetText("pilot-name", snapshot.PilotName);
            SetText("credits", Tr("{0} ISK", snapshot.Credits.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)));
            SetText("ship-name", $"{snapshot.ShipName} · {snapshot.ShipClass}");
            SetText("speed", Tr("{0} m/s", snapshot.Speed.ToString("0", System.Globalization.CultureInfo.InvariantCulture)));
            SetText("target-name", snapshot.SelectedName);
            SetText("target-detail", snapshot.SelectedDetail);
            SetText("mission", snapshot.MissionSummary);
            SetBar("shield", snapshot.Shield01);
            SetBar("armor", snapshot.Armor01);
            SetBar("hull", snapshot.Hull01);
            RefreshOverview(snapshot);
            RefreshSelectedActions(snapshot);
            RefreshCachedList(starmapList, snapshot.Starmap, "destination",
                ref starmapCacheValid, ref starmapFingerprint);
            RefreshCachedList(journalList, snapshot.Missions, "mission",
                ref journalCacheValid, ref journalFingerprint);

            var log = root.Q<Label>("combat-log");
            if (log != null)
                log.text = string.Join("\n", snapshot.Log.Count > 8
                    ? snapshot.Log.GetRange(snapshot.Log.Count - 8, 8)
                    : snapshot.Log);

            for (var i = 0; i < 9; i++)
            {
                var button = moduleButtons[i];
                if (button == null) continue;
                if (i < snapshot.Modules.Count)
                {
                    var module = snapshot.Modules[i];
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

            Toggle("death-overlay", snapshot.PlayerDead);
            Toggle("map-overlay", snapshot.MapVisible);
            Toggle("journal-overlay", snapshot.JournalVisible);
        }

        private void RefreshTelemetry()
        {
            if (root == null || host?.Snapshot == null) return;
            var snapshot = host.Snapshot;
            SetText("speed", Tr("{0} m/s", snapshot.Speed.ToString("0", System.Globalization.CultureInfo.InvariantCulture)));
            SetText("target-name", snapshot.SelectedName);
            SetText("target-detail", snapshot.SelectedDetail);
            SetBar("shield", snapshot.Shield01);
            SetBar("armor", snapshot.Armor01);
            SetBar("hull", snapshot.Hull01);
            RefreshOverview(snapshot);
            RefreshSelectedActions(snapshot);

            var count = Math.Min(9, snapshot.Modules.Count);
            for (var i = 0; i < count; i++)
                moduleButtons[i]?.EnableInClassList("active", snapshot.Modules[i].Active);
        }

        private void RefreshOverview(UiSnapshot snapshot)
        {
            if (overviewList == null) return;
            selectedContactId = snapshot.SelectedId ?? string.Empty;
            var sortColumn = sortColumns[(int)activePreset];
            var sortDirection = sortDirections[(int)activePreset];
            OverviewFilter.FilterAndSort(
                snapshot.Overview, filteredOverview, activePreset, sortColumn, sortDirection);

            if (orderFrozen)
                MergeFrozenOverview();
            else
                ReplaceVisibleOverview();

            if (overviewCount != null)
                overviewCount.text = filteredOverview.Count == 1
                    ? Tr("1 CONTACT")
                    : Tr("{0} CONTACTS", filteredOverview.Count);
            if (overviewEmpty != null)
                overviewEmpty.style.display = visibleOverview.Count == 0
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
        }

        private void RefreshSelectedActions(UiSnapshot snapshot)
        {
            UiOverviewContact selected = null;
            for (var i = 0; i < snapshot.Overview.Count; i++)
            {
                var contact = snapshot.Overview[i];
                if (contact == null ||
                    !string.Equals(contact.Id, snapshot.SelectedId, StringComparison.Ordinal))
                    continue;
                selected = contact;
                break;
            }

            var actions = selected?.Actions ?? OverviewActionFlags.None;

            approachButton?.SetEnabled((actions & OverviewActionFlags.Approach) != 0);
            orbitButton?.SetEnabled((actions & OverviewActionFlags.Orbit) != 0);
            warpButton?.SetEnabled((actions & OverviewActionFlags.Warp) != 0);
            lockButton?.SetEnabled((actions & OverviewActionFlags.Lock) != 0);

            if (dockButton == null) return;
            var distance = selected?.DistanceMeters ?? -1d;
            var validDistance = !double.IsNaN(distance) && !double.IsInfinity(distance) && distance >= 0d;
            var canDock = selected != null && selected.Kind == OverviewKind.Station && validDistance &&
                          distance <= DockInteractionDistance &&
                          (actions & OverviewActionFlags.Dock) != 0;
            var canJump = selected != null && selected.Kind == OverviewKind.Stargate && validDistance &&
                          distance <= JumpInteractionDistance &&
                          (actions & OverviewActionFlags.Jump) != 0;
            dockButton.SetEnabled(canDock || canJump);
            dockButton.text = HudLabel("DOCK/JUMP [D]");
        }

        // Touch devices have no keyboard: strip the "[D]"-style hints from
        // control labels instead of teaching players about keys they lack.
        private static readonly System.Text.RegularExpressions.Regex KeyboardHintSuffix =
            new(@"\s*\[[A-Z0-9]{1,3}\]$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        protected static string HudLabel(string english)
        {
            var translated = Tr(english);
            return UnityEngine.Application.isMobilePlatform
                ? KeyboardHintSuffix.Replace(translated, string.Empty)
                : translated;
        }

        private void ReplaceVisibleOverview()
        {
            var countChanged = visibleOverview.Count != filteredOverview.Count;
            visibleOverview.Clear();
            visibleOverview.AddRange(filteredOverview);
            staleIds.Clear();
            orderDirty = false;

            if (countChanged)
                overviewList.Rebuild();
            else
                overviewList.RefreshItems();
        }

        private void MergeFrozenOverview()
        {
            filteredById.Clear();
            for (var i = 0; i < filteredOverview.Count; i++)
            {
                var contact = filteredOverview[i];
                filteredById[contact.Id ?? string.Empty] = contact;
            }

            visibleIds.Clear();
            staleIds.Clear();
            for (var i = 0; i < visibleOverview.Count; i++)
            {
                var id = visibleOverview[i]?.Id ?? string.Empty;
                visibleIds.Add(id);
                if (filteredById.TryGetValue(id, out var latest))
                    visibleOverview[i] = latest;
                else
                    staleIds.Add(id);
            }

            var countBeforeAppend = visibleOverview.Count;
            for (var i = 0; i < filteredOverview.Count; i++)
            {
                var contact = filteredOverview[i];
                if (visibleIds.Add(contact.Id ?? string.Empty)) visibleOverview.Add(contact);
            }

            orderDirty = staleIds.Count > 0 || visibleOverview.Count != filteredOverview.Count;
            if (!orderDirty)
            {
                for (var i = 0; i < filteredOverview.Count; i++)
                {
                    if (string.Equals(filteredOverview[i]?.Id, visibleOverview[i]?.Id, StringComparison.Ordinal))
                        continue;
                    orderDirty = true;
                    break;
                }
            }

            if (visibleOverview.Count != countBeforeAppend)
                overviewList.Rebuild();
            else
                overviewList.RefreshItems();
        }

        private void BindOverviewItem(VisualElement element, int index)
        {
            if (element is not OverviewRow row || index < 0 || index >= visibleOverview.Count) return;
            var contact = visibleOverview[index];
            row.Bind(contact,
                string.Equals(contact?.Id, selectedContactId, StringComparison.Ordinal),
                contact != null && staleIds.Contains(contact.Id ?? string.Empty));
        }

        private static void UnbindOverviewItem(VisualElement element, int _)
        {
            if (element is OverviewRow row) row.Unbind();
        }

        private void SelectOverviewContact(string id)
        {
            if (!string.IsNullOrEmpty(id)) host?.Execute("select", id);
        }

        // Long-press surfaces the row detail that desktop players get via hover
        // tooltips; on touch there is no other way to read it.
        private void ShowContactDetail(string detail)
        {
            if (!string.IsNullOrEmpty(detail)) host?.Execute("detail", detail);
        }

        private void SetActivePreset(OverviewPresetId preset)
        {
            if (!Enum.IsDefined(typeof(OverviewPresetId), preset)) return;
            activePreset = preset;
            PlayerPrefs.SetInt($"{OverviewPreferencePrefix}.active", (int)activePreset);
            PlayerPrefs.Save();
            UpdatePresetButtons();
            UpdateSortButtons();
            ApplyOverviewPreferenceChange();
        }

        private void SetSortColumn(OverviewSortColumn column)
        {
            var presetIndex = (int)activePreset;
            if (sortColumns[presetIndex] == column)
            {
                sortDirections[presetIndex] = sortDirections[presetIndex] == OverviewSortDirection.Ascending
                    ? OverviewSortDirection.Descending
                    : OverviewSortDirection.Ascending;
            }
            else
            {
                sortColumns[presetIndex] = column;
                sortDirections[presetIndex] = OverviewSortDirection.Ascending;
            }

            PlayerPrefs.SetInt(SortColumnPreferenceKey(activePreset), (int)sortColumns[presetIndex]);
            PlayerPrefs.SetInt(SortDirectionPreferenceKey(activePreset), (int)sortDirections[presetIndex]);
            PlayerPrefs.Save();
            UpdateSortButtons();
            ApplyOverviewPreferenceChange();
        }

        private void ApplyOverviewPreferenceChange()
        {
            orderFrozen = false;
            pointerOverOverview = false;
            pointerPressedInOverview = false;
            orderDirty = false;
            if (host?.Snapshot != null) RefreshOverview(host.Snapshot);
        }

        private void LoadOverviewPreferences()
        {
            activePreset = ReadEnumPreference(
                $"{OverviewPreferencePrefix}.active", OverviewPresetId.General);
            for (var i = 0; i < sortColumns.Length; i++)
            {
                var preset = (OverviewPresetId)i;
                sortColumns[i] = ReadEnumPreference(
                    SortColumnPreferenceKey(preset), OverviewRules.DefaultSortColumn(preset));
                sortDirections[i] = ReadEnumPreference(
                    SortDirectionPreferenceKey(preset), OverviewRules.DefaultSortDirection(preset));
            }
        }

        private static TEnum ReadEnumPreference<TEnum>(string key, TEnum fallback)
            where TEnum : struct, Enum
        {
            var value = PlayerPrefs.GetInt(key, Convert.ToInt32(fallback));
            return Enum.IsDefined(typeof(TEnum), value)
                ? (TEnum)Enum.ToObject(typeof(TEnum), value)
                : fallback;
        }

        private static string SortColumnPreferenceKey(OverviewPresetId preset) =>
            $"{OverviewPreferencePrefix}.{preset}.sort-column";

        private static string SortDirectionPreferenceKey(OverviewPresetId preset) =>
            $"{OverviewPreferencePrefix}.{preset}.sort-direction";

        private void UpdatePresetButtons()
        {
            for (var i = 0; i < presetButtons.Length; i++)
                presetButtons[i]?.EnableInClassList("chosen", i == (int)activePreset);
        }

        private void UpdateSortButtons()
        {
            var activeColumn = sortColumns[(int)activePreset];
            var direction = sortDirections[(int)activePreset];
            for (var i = 0; i < sortButtons.Length; i++)
            {
                var button = sortButtons[i];
                if (button == null) continue;
                var column = (OverviewSortColumn)i;
                button.EnableInClassList("sorted", column == activeColumn);
                button.text = ColumnHeader(column);
                if (column == activeColumn)
                    button.text += direction == OverviewSortDirection.Ascending ? " ↑" : " ↓";
            }
        }

        private static string ColumnHeader(OverviewSortColumn column)
        {
            switch (column)
            {
                case OverviewSortColumn.Threat:
                    return "!";
                case OverviewSortColumn.Distance:
                    return Tr("DIST");
                case OverviewSortColumn.Name:
                    return Tr("NAME");
                case OverviewSortColumn.Type:
                    return Tr("TYPE");
                case OverviewSortColumn.Velocity:
                    return Tr("SPEED");
                default:
                    return string.Empty;
            }
        }

        private void ReleaseOverviewPointer()
        {
            if (!pointerPressedInOverview) return;
            pointerPressedInOverview = false;
            UpdateOrderFreeze();
        }

        private void UpdateOrderFreeze()
        {
            var shouldFreeze = pointerOverOverview || pointerPressedInOverview;
            if (orderFrozen == shouldFreeze) return;
            orderFrozen = shouldFreeze;
            if (!orderFrozen && orderDirty && host?.Snapshot != null)
                RefreshOverview(host.Snapshot);
        }

        private void SetText(string name, string text)
        {
            if (root.Q<Label>(name) is { } label) label.text = text;
        }

        private void SetBar(string name, float value)
        {
            if (root.Q<VisualElement>(name) is { } bar)
                bar.style.width = Length.Percent(Mathf.Clamp01(value) * 100f);
        }

        private void Toggle(string name, bool visible)
        {
            if (root.Q<VisualElement>(name) is { } element)
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshCachedList(
            ScrollView list,
            IReadOnlyList<UiListItem> items,
            string command,
            ref bool cacheValid,
            ref ulong cachedFingerprint)
        {
            if (list == null) return;
            var fingerprint = ComputeListFingerprint(items);
            if (cacheValid && cachedFingerprint == fingerprint) return;

            list.Clear();
            foreach (var item in items)
            {
                if (item == null) continue;
                var itemId = item.Id;
                var button = new Button(() => host?.Execute(command, itemId)) { text = item.Title };
                button.AddToClassList("modal-list-row");
                button.tooltip = item.Detail;
                list.Add(button);
            }

            cachedFingerprint = fingerprint;
            cacheValid = true;
        }

        private void ResetAuxiliaryListCaches()
        {
            starmapCacheValid = false;
            journalCacheValid = false;
            starmapFingerprint = 0UL;
            journalFingerprint = 0UL;
        }

        private static ulong ComputeListFingerprint(IReadOnlyList<UiListItem> items)
        {
            unchecked
            {
                var hash = ListFingerprintOffset;
                hash = MixFingerprint(hash, (uint)(items?.Count ?? 0));
                if (items == null) return hash;

                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    hash = MixFingerprint(hash, item?.Id);
                    hash = MixFingerprint(hash, item?.Title);
                    hash = MixFingerprint(hash, item?.Detail);
                    hash = MixFingerprint(hash, item?.Accent);
                }
                return hash;
            }
        }

        private static ulong MixFingerprint(ulong hash, string value)
        {
            unchecked
            {
                hash = MixFingerprint(hash, value == null ? uint.MaxValue : (uint)value.Length);
                if (value == null) return hash;
                for (var i = 0; i < value.Length; i++)
                    hash = MixFingerprint(hash, value[i]);
                return hash;
            }
        }

        private static ulong MixFingerprint(ulong hash, uint value)
        {
            unchecked
            {
                hash = (hash ^ (byte)value) * ListFingerprintPrime;
                hash = (hash ^ (byte)(value >> 8)) * ListFingerprintPrime;
                hash = (hash ^ (byte)(value >> 16)) * ListFingerprintPrime;
                return (hash ^ (byte)(value >> 24)) * ListFingerprintPrime;
            }
        }

        private sealed class OverviewRow : VisualElement
        {
            private readonly Action<string> selectRequested;
            private readonly Action<string> detailRequested;
            private readonly OverviewIconElement icon;
            private readonly Label badges;
            private readonly Label distance;
            private readonly Label contactName;
            private readonly Label contactType;
            private readonly Label velocity;
            private UiOverviewContact contact;
            private bool stale;

            public OverviewRow(Action<string> selectRequested, Action<string> detailRequested)
            {
                this.selectRequested = selectRequested;
                this.detailRequested = detailRequested;
                AddToClassList("overview-row");
                pickingMode = PickingMode.Position;

                var state = new VisualElement { pickingMode = PickingMode.Ignore };
                state.AddToClassList("overview-state-cell");
                state.AddToClassList("overview-cell");
                icon = new OverviewIconElement();
                badges = CreateLabel("overview-badges");
                state.Add(icon);
                state.Add(badges);
                Add(state);

                distance = CreateLabel("overview-distance");
                contactName = CreateLabel("overview-name");
                contactType = CreateLabel("overview-type");
                velocity = CreateLabel("overview-velocity");
                Add(distance);
                Add(contactName);
                Add(contactType);
                Add(velocity);

                RegisterCallback<PointerDownEvent>(OnPointerDown);
                RegisterCallback<PointerUpEvent>(OnPointerUp);
                RegisterCallback<PointerLeaveEvent>(CancelLongPress);
            }

            // Rows select on pointer-up after a slop check: selecting on
            // pointer-down turned every scroll swipe into an accidental target
            // lock, which is punishing with a touch screen.
            private static readonly Vector2 TapSlop = new Vector2(18f, 18f);
            private static readonly long LongPressMilliseconds = 550;
            private Vector3 pointerDownPosition;
            private bool pointerPressValid;
            private bool longPressFired;
            private IVisualElementScheduledItem longPressTask;

            private void OnPointerDown(PointerDownEvent evt)
            {
                pointerPressValid = evt.button == 0 && contact != null && !stale;
                pointerDownPosition = evt.position;
                CancelLongPress(null);
                if (!pointerPressValid) return;
                longPressFired = false;
                longPressTask = schedule.Execute(FireLongPress).StartingIn(LongPressMilliseconds);
            }

            private void FireLongPress()
            {
                if (!pointerPressValid || longPressFired || contact == null || stale) return;
                longPressFired = true;
                detailRequested?.Invoke(tooltip);
            }

            private void CancelLongPress(PointerLeaveEvent evt)
            {
                longPressTask?.Pause();
                longPressTask = null;
            }

            private void OnPointerUp(PointerUpEvent evt)
            {
                var wasLongPress = longPressFired;
                CancelLongPress(null);
                if (!pointerPressValid || evt.button != 0 || contact == null || stale) return;
                pointerPressValid = false;
                if (wasLongPress) return;
                var delta = evt.position - pointerDownPosition;
                if (Mathf.Abs(delta.x) > TapSlop.x || Mathf.Abs(delta.y) > TapSlop.y) return;
                selectRequested?.Invoke(contact.Id);
            }

            public void Bind(UiOverviewContact value, bool selected, bool stale)
            {
                contact = value;
                this.stale = stale;
                if (value == null)
                {
                    Unbind();
                    return;
                }

                var attacking = (value.States & OverviewStateFlags.TargetingPlayer) != 0;
                var mission = (value.States & OverviewStateFlags.MissionObjective) != 0;
                var locked = (value.States & OverviewStateFlags.LockedByPlayer) != 0;
                var elite = (value.States & OverviewStateFlags.Elite) != 0;
                var lawEnforcement = (value.States & OverviewStateFlags.LawEnforcement) != 0;

                icon.Bind(value);
                badges.text = BadgeText(attacking, mission, locked, elite, lawEnforcement);
                distance.text = FormatDistance(value.DistanceMeters);
                contactName.text = string.IsNullOrEmpty(value.Name) ? value.Id : value.Name;
                contactType.text = value.Type ?? string.Empty;
                velocity.text = FormatVelocity(value.VelocityMetersPerSecond);
                tooltip = string.IsNullOrEmpty(value.Detail) ? value.Type : value.Detail;

                EnableInClassList("hostile", value.Disposition == OverviewDisposition.Hostile);
                EnableInClassList("friendly", value.Disposition == OverviewDisposition.Friendly);
                EnableInClassList("neutral", value.Disposition == OverviewDisposition.Neutral);
                EnableInClassList("attacking", attacking);
                EnableInClassList("mission", mission);
                EnableInClassList("locked", locked);
                EnableInClassList("selected", selected || (value.States & OverviewStateFlags.Selected) != 0);
                EnableInClassList("stale", stale);
            }

            public void Unbind()
            {
                contact = null;
                stale = false;
                badges.text = string.Empty;
                distance.text = string.Empty;
                contactName.text = string.Empty;
                contactType.text = string.Empty;
                velocity.text = string.Empty;
                tooltip = string.Empty;
                icon.Bind(null);
                EnableInClassList("hostile", false);
                EnableInClassList("friendly", false);
                EnableInClassList("neutral", false);
                EnableInClassList("attacking", false);
                EnableInClassList("mission", false);
                EnableInClassList("locked", false);
                EnableInClassList("selected", false);
                EnableInClassList("stale", false);
            }

            private static Label CreateLabel(string className)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("overview-cell");
                label.AddToClassList(className);
                return label;
            }

            private static string BadgeText(
                bool attacking,
                bool mission,
                bool locked,
                bool elite,
                bool lawEnforcement)
            {
                var key = (attacking ? 1 : 0) | (mission ? 2 : 0) | (locked ? 4 : 0);
                switch (key)
                {
                    case 1: return "!";
                    case 2: return Tr("M");
                    case 3: return Tr("!M");
                    case 4: return Tr("L");
                    case 5: return Tr("!L");
                    case 6: return Tr("ML");
                    case 7: return Tr("!ML");
                    default:
                        if (lawEnforcement) return Tr("P");
                        return elite ? Tr("E") : string.Empty;
                }
            }

            private static string FormatDistance(double meters)
            {
                if (double.IsNaN(meters) || double.IsInfinity(meters) || meters < 0d) return "—";
                const double astronomicalUnit = 149_597_870_700d;
                if (meters >= astronomicalUnit * 0.1d) return Tr("{0} AU", (meters / astronomicalUnit).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                if (meters >= 10_000_000d) return Tr("{0} km", (meters / 1000d).ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
                if (meters >= 10_000d) return Tr("{0} km", (meters / 1000d).ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                if (meters >= 1000d) return Tr("{0} km", (meters / 1000d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                return Tr("{0} m", meters.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            }

            private static string FormatVelocity(double metersPerSecond)
            {
                if (double.IsNaN(metersPerSecond) || double.IsInfinity(metersPerSecond) ||
                    metersPerSecond < 0d)
                    return "—";
                if (metersPerSecond >= 1000d) return (metersPerSecond / 1000d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "k";
                return Tr("{0} m/s", metersPerSecond.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}
