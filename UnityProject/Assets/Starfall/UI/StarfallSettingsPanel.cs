using System;
using UnityEngine.UIElements;
using Starfall.Domain;
using static Starfall.Domain.L10n;

namespace Starfall.UI
{
    public sealed class StarfallSettingsPanel : IDisposable
    {
        private readonly VisualElement overlay;
        private readonly Button openButton;
        private readonly Button closeButton;
        private readonly Button qualityButton;
        private readonly Button languageButton;
        private readonly Slider uiScaleSlider;
        private readonly Label uiScaleValue;
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

            var uiScaleRow = new VisualElement();
            uiScaleRow.AddToClassList("settings-volume-row");
            uiScaleSlider = new Slider("UI SCALE", StarfallResponsiveUi.MinUiScale, StarfallResponsiveUi.MaxUiScale) { name = "ui-scale" };
            uiScaleSlider.AddToClassList("settings-slider");
            uiScaleValue = new Label("100%") { name = "ui-scale-value" };
            uiScaleValue.AddToClassList("settings-value");
            uiScaleRow.Add(uiScaleSlider);
            uiScaleRow.Add(uiScaleValue);
            card.Add(uiScaleRow);

            card.Add(new Label("LANGUAGE") { name = "settings-language-heading" });
            languageButton = new Button(CycleLanguage) { name = "language-cycle" };
            languageButton.AddToClassList("settings-control");
            // The caption is the language's own name and must not be localized.
            languageButton.AddToClassList("l10n-skip");
            card.Add(languageButton);

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
            uiScaleSlider.RegisterValueChangedCallback(OnUiScaleChanged);
            StarfallUiBridge.HostChanged += BindHost;
            L10n.LanguageChanged += OnLanguageChanged;
            BindHost();
            UpdateLanguageButton();
            Close();
        }

        public bool IsOpen => overlay.style.display.value == DisplayStyle.Flex;

        public void Dispose()
        {
            L10n.LanguageChanged -= OnLanguageChanged;
            StarfallUiBridge.HostChanged -= BindHost;
            if (host != null) host.SettingsChanged -= Refresh;
            if (openButton != null) openButton.clicked -= Open;
            volumeSlider.UnregisterValueChangedCallback(OnVolumeChanged);
            muteToggle.UnregisterValueChangedCallback(OnMutedChanged);
            uiScaleSlider.UnregisterValueChangedCallback(OnUiScaleChanged);
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
            volumeValue.text = FormattableString.Invariant($"{host.MusicVolume * 100f:0}%");
            muteToggle.SetValueWithoutNotify(host.MusicMuted);
            uiScaleSlider.SetValueWithoutNotify(host.UiScale);
            uiScaleValue.text = FormattableString.Invariant($"{host.UiScale * 100f:0}%");
            qualityButton.text = Tr("QUALITY · {0}", Tr(host.QualityPreset));
            refreshing = false;
        }

        private void OnLanguageChanged()
        {
            UpdateLanguageButton();
            Refresh();
        }

        private void UpdateLanguageButton()
        {
            languageButton.text = L10n.IsChinese ? "中文" : "English";
        }

        private void CycleLanguage()
        {
            host?.SetLanguage(L10n.IsChinese ? L10nLanguage.English : L10nLanguage.Chinese);
        }

        private void OnVolumeChanged(ChangeEvent<float> evt)
        {
            if (refreshing) return;
            volumeValue.text = FormattableString.Invariant($"{evt.newValue * 100f:0}%");
            host?.SetMusicVolume(evt.newValue);
        }

        private void OnMutedChanged(ChangeEvent<bool> evt)
        {
            if (!refreshing) host?.SetMusicMuted(evt.newValue);
        }

        private void OnUiScaleChanged(ChangeEvent<float> evt)
        {
            if (refreshing) return;
            uiScaleValue.text = FormattableString.Invariant($"{evt.newValue * 100f:0}%");
            host?.SetUiScale(evt.newValue);
        }
    }
}
