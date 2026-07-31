using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Starfall.App;
using Starfall.Presentation;
using Starfall.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Starfall.Tests.PlayMode
{
    public sealed class MusicDirectorPlayModeTests
    {
        private const float SceneTimeoutSeconds = 15f;
        private readonly Dictionary<string, float> floatPreferences = new();
        private readonly Dictionary<string, int> intPreferences = new();
        private readonly HashSet<string> missingPreferences = new();
        private int originalQualityLevel;
        private AppRoot app;
        private MusicDirector director;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            CapturePreference(MusicDirector.VolumePreferenceKey, true);
            CapturePreference(MusicDirector.MutedPreferenceKey, false);
            CapturePreference("starfall.quality", false);
            originalQualityLevel = QualitySettings.GetQualityLevel();
            PlayerPrefs.SetFloat(MusicDirector.VolumePreferenceKey, 0f);
            PlayerPrefs.SetInt(MusicDirector.MutedPreferenceKey, 1);

            yield return DestroyExistingAppRoots();
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return WaitForScene("MainMenu");
            yield return null;

            app = Object.FindFirstObjectByType<AppRoot>();
            director = Object.FindFirstObjectByType<MusicDirector>();
            Assert.That(app, Is.Not.Null);
            Assert.That(director, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return DestroyExistingAppRoots();
            RestorePreferences();
            QualitySettings.SetQualityLevel(originalQualityLevel, true);
        }

        [Test]
        public void Catalog_ContainsFiveDistinctTracks()
        {
            var catalog = director.Catalog;
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.MainMenuTrack, Is.Not.Null);
            Assert.That(catalog.StationTrack, Is.Not.Null);
            Assert.That(catalog.SpacePlaylist, Has.Length.EqualTo(3));
            Assert.That(catalog.SpacePlaylist, Has.None.Null);

            var allTracks = new[] { catalog.MainMenuTrack, catalog.StationTrack }
                .Concat(catalog.SpacePlaylist)
                .ToArray();
            Assert.That(allTracks.Distinct().Count(), Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator SceneFlow_UsesPersistentDirectorAndMappedTracks()
        {
            var catalog = director.Catalog;
            var originalInstanceId = director.GetInstanceID();
            Assert.That(director.CurrentContext, Is.EqualTo("MainMenu"));
            Assert.That(director.CurrentClip, Is.SameAs(catalog.MainMenuTrack));

            app.StartNewGame("Music Test", "aurelian");
            yield return WaitForScene("Station");
            Assert.That(director.GetInstanceID(), Is.EqualTo(originalInstanceId));
            Assert.That(director.CurrentContext, Is.EqualTo("Station"));
            Assert.That(director.CurrentClip, Is.SameAs(catalog.StationTrack));
            Assert.That(Object.FindObjectsByType<MusicDirector>(FindObjectsSortMode.None), Has.Length.EqualTo(1));

            app.Execute("undock");
            yield return WaitForScene("Space");
            Assert.That(director.GetInstanceID(), Is.EqualTo(originalInstanceId));
            Assert.That(director.CurrentContext, Is.EqualTo("Space"));
            Assert.That(director.CurrentClip, Is.SameAs(catalog.SpacePlaylist[0]));
            Assert.That(director.PlayingSourceCount, Is.EqualTo(2),
                "Scene transitions must cross-fade using both AudioSources.");
        }

        [UnityTest]
        public IEnumerator SpacePlaylist_AdvancesToTheNextTrack()
        {
            app.StartNewGame("Playlist Test", "aurelian");
            yield return WaitForScene("Station");
            app.Execute("undock");
            yield return WaitForScene("Space");
            yield return WaitForCondition(() => director.PlayingSourceCount == 1,
                MusicDirector.CrossFadeSeconds + 2f, "Initial space cross-fade did not finish.");

            var firstClip = director.CurrentClip;
            // A headless audio backend can report the source as stopped for the one frame in
            // which MusicDirector advances it. Keep the fixture anchored to the clip instead
            // of racing AudioSource.isPlaying; a stopped source will advance on the next Update.
            var activeSource = director.GetComponentsInChildren<AudioSource>()
                .FirstOrDefault(source => source.clip == firstClip);
            Assert.That(activeSource, Is.Not.Null,
                "The current space clip must remain assigned to one of the director's sources.");
            if (activeSource.isPlaying)
                activeSource.time = Mathf.Max(0f, firstClip.length - 0.25f);
            yield return WaitForCondition(() => director.CurrentClip != firstClip,
                2f, "The space playlist did not advance near the end of the current track.");

            Assert.That(director.CurrentClip, Is.SameAs(director.Catalog.SpacePlaylist[1]));
            Assert.That(director.PlayingSourceCount, Is.InRange(1, 2),
                "The next playlist entry must be playing even when a headless audio backend " +
                "cannot keep the previous source alive for a cross-fade.");
        }

        [UnityTest]
        public IEnumerator SettingsPanel_IsAvailableInAllScenesAndPersistsMusicValues()
        {
            yield return AssertSettingsPanel("MainMenu");

            var root = Object.FindFirstObjectByType<MainMenuUiController>()
                .GetComponent<UIDocument>().rootVisualElement;
            var slider = root.Q<Slider>("music-volume");
            var mute = root.Q<Toggle>("music-muted");
            slider.value = 0.42f;
            mute.value = false;
            Assert.That(director.MusicVolume, Is.EqualTo(0.42f).Within(0.001f));
            Assert.That(director.Muted, Is.False);
            Assert.That(PlayerPrefs.GetFloat(MusicDirector.VolumePreferenceKey), Is.EqualTo(0.42f).Within(0.001f));
            Assert.That(PlayerPrefs.GetInt(MusicDirector.MutedPreferenceKey), Is.Zero);

            app.StartNewGame("Settings Test", "aurelian");
            yield return WaitForScene("Station");
            yield return AssertSettingsPanel("Station");
            app.Execute("undock");
            yield return WaitForScene("Space");
            yield return AssertSettingsPanel("Space");

            app.CycleQuality();
            Assert.That(PlayerPrefs.GetInt("starfall.quality"), Is.EqualTo(QualitySettings.GetQualityLevel()));
            Assert.That(app.QualityPreset, Is.EqualTo(QualitySettings.names[QualitySettings.GetQualityLevel()]));
        }

        private IEnumerator AssertSettingsPanel(string sceneName)
        {
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(sceneName));
            var document = Object.FindFirstObjectByType<UIDocument>();
            Assert.That(document, Is.Not.Null);
            var root = document.rootVisualElement;
            var settingsButton = root.Q<Button>("settings");
            var overlay = root.Q<VisualElement>("settings-overlay");
            Assert.That(settingsButton, Is.Not.Null);
            Assert.That(overlay, Is.Not.Null);

            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = settingsButton;
                settingsButton.SendEvent(submit);
            }
            yield return null;
            Assert.That(overlay.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            var close = root.Q<Button>("settings-close");
            Assert.That(close, Is.Not.Null);
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = close;
                close.SendEvent(submit);
            }
            yield return null;
            Assert.That(overlay.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }

        private void CapturePreference(string key, bool isFloat)
        {
            if (!PlayerPrefs.HasKey(key))
            {
                missingPreferences.Add(key);
                return;
            }
            if (isFloat) floatPreferences[key] = PlayerPrefs.GetFloat(key);
            else intPreferences[key] = PlayerPrefs.GetInt(key);
        }

        private void RestorePreferences()
        {
            foreach (var key in missingPreferences) PlayerPrefs.DeleteKey(key);
            foreach (var pair in floatPreferences) PlayerPrefs.SetFloat(pair.Key, pair.Value);
            foreach (var pair in intPreferences) PlayerPrefs.SetInt(pair.Key, pair.Value);
            PlayerPrefs.Save();
            missingPreferences.Clear();
            floatPreferences.Clear();
            intPreferences.Clear();
        }

        private static IEnumerator DestroyExistingAppRoots()
        {
            foreach (var existing in Object.FindObjectsByType<AppRoot>(FindObjectsSortMode.None))
                if (existing) Object.Destroy(existing.gameObject);
            yield return null;
        }

        private static IEnumerator WaitForScene(string sceneName)
        {
            yield return WaitForCondition(() => SceneManager.GetActiveScene().name == sceneName,
                SceneTimeoutSeconds, $"Timed out waiting for scene {sceneName}.");
            yield return null;
        }

        private static IEnumerator WaitForCondition(System.Func<bool> predicate, float timeout, string message)
        {
            var deadline = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, message);
        }
    }
}
