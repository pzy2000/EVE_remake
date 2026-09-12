using System;
using System.Collections.Generic;
using Starfall.Domain;

namespace Starfall.UI
{
    [Serializable]
    public sealed class UiListItem
    {
        public string Id = string.Empty;
        public string Title = string.Empty;
        public string Detail = string.Empty;
        public string Accent = "#4edbff";
    }

    [Serializable]
    public sealed class UiModuleState
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Slot = string.Empty;
        public bool Active;
        public float Cooldown01;
    }

    [Serializable]
    public sealed class UiSnapshot
    {
        public string PilotName = "Pilot";
        public string EmpireId = "aurelian";
        public string EmpireName = "Aurelian Ascendancy";
        public string SystemName = "Helios Prime";
        public float Security = 1f;
        public string ShipName = "Acolyte";
        public string ShipClass = "Frigate";
        public string SelectedId = string.Empty;
        public string SelectedName = "No target";
        public string SelectedDetail = "Select an object in space";
        public string MissionSummary = "No active mission";
        public string FittingSummary = string.Empty;
        public long Credits = 50000;
        public int LoyaltyPoints;
        public float Shield01 = 1f;
        public float Armor01 = 1f;
        public float Hull01 = 1f;
        public float Speed;
        public bool Docked;
        public bool PlayerDead;
        public bool MapVisible;
        public bool JournalVisible;
        public readonly List<UiOverviewContact> Overview = new();
        public readonly List<UiListItem> Starmap = new();
        public readonly List<UiListItem> Agents = new();
        public readonly List<UiListItem> Market = new();
        public readonly List<UiListItem> Ships = new();
        public readonly List<UiListItem> Missions = new();
        public readonly List<UiListItem> Skills = new();
        public readonly List<UiListItem> LpStore = new();
        public readonly List<UiListItem> Inventory = new();
        public readonly List<UiModuleState> Modules = new();
        public readonly List<string> Log = new();
    }

    public interface IStarfallUiHost
    {
        UiSnapshot Snapshot { get; }
        float MusicVolume { get; }
        float SfxVolume { get; }
        bool MusicMuted { get; }
        string QualityPreset { get; }
        float UiScale { get; }
        event Action SnapshotChanged;
        event Action TelemetryChanged;
        event Action SettingsChanged;
        void StartNewGame(string pilotName, string empireId);
        void ContinueGame();
        void ImportLegacy();
        void Execute(string command, string argument = null);
        void SetMusicVolume(float value);
        void SetSfxVolume(float value);
        void SetMusicMuted(bool value);
        void SetLanguage(L10nLanguage language);
        void SetUiScale(float value);
        void CycleQuality();
    }

    public static class StarfallUiBridge
    {
        public static IStarfallUiHost Host { get; private set; }
        public static event Action HostChanged;

        public static void Bind(IStarfallUiHost host)
        {
            Host = host;
            HostChanged?.Invoke();
        }
    }
}
