using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Starfall.Presentation
{
    [DisallowMultipleComponent]
    public sealed class MusicDirector : MonoBehaviour
    {
        public const float DefaultMusicVolume = 0.35f;
        public const float CrossFadeSeconds = 1.5f;
        public const string VolumePreferenceKey = "starfall.music.volume";
        public const string MutedPreferenceKey = "starfall.music.muted";

        private const string CatalogResourcePath = "MusicCatalog";
        private readonly AudioSource[] sources = new AudioSource[2];
        private readonly float[] sourceGains = new float[2];
        private readonly HashSet<string> warnedAssets = new(StringComparer.Ordinal);

        private MusicCatalogAsset catalog;
        private Coroutine transition;
        private int activeSourceIndex = -1;
        private int spacePlaylistIndex = -1;
        private string currentContext = string.Empty;
        private float musicVolume;
        private bool muted;

        public event Action SettingsChanged;

        public MusicCatalogAsset Catalog => catalog;
        public AudioClip CurrentClip => activeSourceIndex >= 0 ? sources[activeSourceIndex].clip : null;
        public string CurrentContext => currentContext;
        public float MusicVolume => musicVolume;
        public bool Muted => muted;
        public int PlayingSourceCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < sources.Length; i++)
                    if (sources[i] && sources[i].isPlaying) count++;
                return count;
            }
        }

        private void Awake()
        {
            musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePreferenceKey, DefaultMusicVolume));
            muted = PlayerPrefs.GetInt(MutedPreferenceKey, 0) != 0;
            catalog = Resources.Load<MusicCatalogAsset>(CatalogResourcePath);
            EnsureAudioListener();
            sources[0] = CreateSource("Music Source A");
            sources[1] = CreateSource("Music Source B");
            ApplyVolumes();
        }

        private void Update()
        {
            if (!string.Equals(currentContext, "Space", StringComparison.Ordinal) || transition != null || activeSourceIndex < 0)
                return;

            var source = sources[activeSourceIndex];
            if (!source || !source.clip)
            {
                PlayNextSpaceTrack();
                return;
            }

            if (!source.isPlaying || source.clip.length - source.time <= CrossFadeSeconds)
                PlayNextSpaceTrack();
        }

        public void PlayForScene(string sceneName)
        {
            EnsureAudioListener();
            if (string.Equals(currentContext, sceneName, StringComparison.Ordinal) &&
                activeSourceIndex >= 0 && sources[activeSourceIndex].isPlaying)
                return;

            currentContext = sceneName ?? string.Empty;
            if (!catalog)
            {
                WarnOnce(CatalogResourcePath, "[Starfall Music] MusicCatalog resource is missing; background music is disabled.");
                StopAll();
                return;
            }

            switch (currentContext)
            {
                case "MainMenu":
                    PlayClip(catalog.MainMenuTrack, true, "main menu");
                    break;
                case "Space":
                    PlayNextSpaceTrack();
                    break;
                case "Station":
                    PlayClip(catalog.StationTrack, true, "station");
                    break;
                default:
                    StopAll();
                    break;
            }
        }

        public void SetMusicVolume(float value)
        {
            SetMusicVolume(value, persist: true);
        }

        // Dragging a slider used to fsync PlayerPrefs on every tick; the
        // settings panel now applies live and persists once on pointer release.
        public void SetMusicVolume(float value, bool persist)
        {
            var next = Mathf.Clamp01(value);
            if (Mathf.Approximately(next, musicVolume) && !persist) return;
            musicVolume = next;
            PlayerPrefs.SetFloat(VolumePreferenceKey, musicVolume);
            if (persist) PlayerPrefs.Save();
            ApplyVolumes();
            SettingsChanged?.Invoke();
        }

        public void SetMuted(bool value)
        {
            if (muted == value) return;
            muted = value;
            PlayerPrefs.SetInt(MutedPreferenceKey, muted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyVolumes();
            SettingsChanged?.Invoke();
        }

        private AudioSource CreateSource(string sourceName)
        {
            var sourceObject = new GameObject(sourceName);
            sourceObject.transform.SetParent(transform, false);
            var source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.priority = 32;
            return source;
        }

        private void EnsureAudioListener()
        {
            if (!GetComponent<AudioListener>() && !FindFirstObjectByType<AudioListener>())
                gameObject.AddComponent<AudioListener>();
        }

        private void PlayNextSpaceTrack()
        {
            var playlist = catalog ? catalog.SpacePlaylist : null;
            if (playlist == null || playlist.Length == 0)
            {
                WarnOnce("space-playlist", "[Starfall Music] The space playlist is empty; background music is disabled in space.");
                StopAll();
                return;
            }

            for (var checkedCount = 0; checkedCount < playlist.Length; checkedCount++)
            {
                spacePlaylistIndex = (spacePlaylistIndex + 1) % playlist.Length;
                var clip = playlist[spacePlaylistIndex];
                if (!clip)
                {
                    WarnOnce($"space-{spacePlaylistIndex}", $"[Starfall Music] Space playlist entry {spacePlaylistIndex} is missing and was skipped.");
                    continue;
                }
                PlayClip(clip, false, $"space playlist entry {spacePlaylistIndex}");
                return;
            }

            StopAll();
        }

        private void PlayClip(AudioClip clip, bool loop, string label)
        {
            if (!clip)
            {
                WarnOnce(label, $"[Starfall Music] The {label} track is missing and was skipped.");
                StopAll();
                return;
            }

            if (activeSourceIndex >= 0 && sources[activeSourceIndex].clip == clip && sources[activeSourceIndex].isPlaying)
            {
                sources[activeSourceIndex].loop = loop;
                return;
            }

            var fromIndex = PrepareForTransition();
            if (fromIndex < 0)
            {
                activeSourceIndex = 0;
                ConfigureAndPlay(sources[0], clip, loop);
                sourceGains[0] = 1f;
                sourceGains[1] = 0f;
                ApplyVolumes();
                return;
            }

            var toIndex = 1 - fromIndex;
            sources[toIndex].Stop();
            ConfigureAndPlay(sources[toIndex], clip, loop);
            sourceGains[toIndex] = 0f;
            activeSourceIndex = toIndex;
            transition = StartCoroutine(CrossFade(fromIndex, toIndex, sourceGains[fromIndex]));
        }

        private int PrepareForTransition()
        {
            if (transition != null)
            {
                StopCoroutine(transition);
                transition = null;
            }

            var firstPlaying = sources[0].isPlaying;
            var secondPlaying = sources[1].isPlaying;
            if (!firstPlaying && !secondPlaying) return -1;

            var fromIndex = !secondPlaying || (firstPlaying && sourceGains[0] >= sourceGains[1]) ? 0 : 1;
            var discardedIndex = 1 - fromIndex;
            sources[discardedIndex].Stop();
            sourceGains[discardedIndex] = 0f;
            return fromIndex;
        }

        private static void ConfigureAndPlay(AudioSource source, AudioClip clip, bool loop)
        {
            source.clip = clip;
            source.loop = loop;
            source.time = 0f;
            source.Play();
        }

        private IEnumerator CrossFade(int fromIndex, int toIndex, float startingFromGain)
        {
            var elapsed = 0f;
            while (elapsed < CrossFadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsed / CrossFadeSeconds);
                sourceGains[fromIndex] = Mathf.Lerp(startingFromGain, 0f, progress);
                sourceGains[toIndex] = progress;
                ApplyVolumes();
                yield return null;
            }

            sources[fromIndex].Stop();
            sourceGains[fromIndex] = 0f;
            sourceGains[toIndex] = 1f;
            ApplyVolumes();
            transition = null;
        }

        private void StopAll()
        {
            if (transition != null)
            {
                StopCoroutine(transition);
                transition = null;
            }
            for (var i = 0; i < sources.Length; i++)
            {
                if (sources[i]) sources[i].Stop();
                sourceGains[i] = 0f;
            }
            activeSourceIndex = -1;
            ApplyVolumes();
        }

        private void ApplyVolumes()
        {
            var audibleVolume = muted ? 0f : musicVolume;
            for (var i = 0; i < sources.Length; i++)
                if (sources[i]) sources[i].volume = audibleVolume * sourceGains[i];
        }

        private void WarnOnce(string key, string message)
        {
            if (warnedAssets.Add(key)) Debug.LogWarning(message, this);
        }
    }
}
