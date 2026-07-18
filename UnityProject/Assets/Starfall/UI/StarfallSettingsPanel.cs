using System;
using UnityEngine.UIElements;

namespace Starfall.UI
{
    public sealed class StarfallSettingsPanel : IDisposable
    {
        private readonly VisualElement overlay;
        private readonly Button openButton;
        private readonly Button closeButton;
        private readonly Button qualityButton;
        private readonly Slider volumeSlider;
        private readonly Label volumeValue;
        private readonly Toggle muteToggle;
        private IStarfallUiHost host;
        private bool refreshing;

        public StarfallSettingsPanel(VisualElement documentRoot)
        {
            overlay = new VisualElement { name = "settings-overlay", pickingMode = PickingMode.Position };
            overlay.AddToClassList("screen");
            overlay.AddToClassList("settings-overlay");

            var card = new VisualElement { name = "settings-card" };
            card.AddToClassList("panel");
            card.AddToClassList("settings-card");

            var header = new VisualElement();
            header.AddToClassList("settings-header");
            header.Add(new Label("SETTINGS") { name = "settings-title" });
            closeButton = new Button(Close) { name = "settings-close", text = "CLOSE" };
            header.Add(closeButton);
            card.Add(header);

            card.Add(new Label("DISPLAY") { name = "settings-display-heading" });
            qualityButton = new Button(() => host?.CycleQuality()) { name = "quality-cycle" };
            qualityButton.AddToClassList("settings-control");
            card.Add(qualityButton);

            card.Add(new Label("AUDIO") { name = "settings-audio-heading" });
            var volumeRow = new VisualElement();
            volumeRow.AddToClassList("settings-volume-row");
            volumeSlider = new Slider("MUSIC VOLUME", 0f, 1f) { name = "music-volume" };
            volumeSlider.AddToClassList("settings-slider");
            volumeValue = new Label("35%") { name = "music-volume-value" };
            volumeValue.AddToClassList("settings-value");
            volumeRow.Add(volumeSlider);
            volumeRow.Add(volumeValue);
            card.Add(volumeRow);

            muteToggle = new Toggle("MUTE MUSIC") { name = "music-muted" };
            muteToggle.AddToClassList("settings-control");
            card.Add(muteToggle);

            card.Add(new Label("Music settings are saved automatically.") { name = "settings-footer" });
            overlay.Add(card);
            documentRoot.Add(overlay);

            openButton = documentRoot.Q<Button>("settings");
            if (openButton != null) openButton.clicked += Open;
            volumeSlider.RegisterValueChangedCallback(OnVolumeChanged);
            muteToggle.RegisterValueChangedCallback(OnMutedChanged);
            StarfallUiBridge.HostChanged += BindHost;
            BindHost();
            Close();
        }

        public bool IsOpen => overlay.style.display.value == DisplayStyle.Flex;

        public void Dispose()
        {
            StarfallUiBridge.HostChanged -= BindHost;
            if (host != null) host.SettingsChanged -= Refresh;
            if (openButton != null) openButton.clicked -= Open;
            volumeSlider.UnregisterValueChangedCallback(OnVolumeChanged);
            muteToggle.UnregisterValueChangedCallback(OnMutedChanged);
            overlay.RemoveFromHierarchy();
            host = null;
        }

        public void Open()
        {
            Refresh();
            overlay.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            overlay.style.display = DisplayStyle.None;
        }

        private void BindHost()
        {
            if (host != null) host.SettingsChanged -= Refresh;
            host = StarfallUiBridge.Host;
            if (host != null) host.SettingsChanged += Refresh;
            Refresh();
        }

        private void Refresh()
        {
            if (host == null) return;
            refreshing = true;
            volumeSlider.SetValueWithoutNotify(host.MusicVolume);
            volumeValue.text = $"{host.MusicVolume * 100f:0}%";
            muteToggle.SetValueWithoutNotify(host.MusicMuted);
            qualityButton.text = "QUALITY · " + host.QualityPreset.ToUpperInvariant();
            refreshing = false;
        }

        private void OnVolumeChanged(ChangeEvent<float> evt)
        {
            if (refreshing) return;
            volumeValue.text = $"{evt.newValue * 100f:0}%";
            host?.SetMusicVolume(evt.newValue);
        }

        private void OnMutedChanged(ChangeEvent<bool> evt)
        {
            if (!refreshing) host?.SetMusicMuted(evt.newValue);
        }
    }
}
