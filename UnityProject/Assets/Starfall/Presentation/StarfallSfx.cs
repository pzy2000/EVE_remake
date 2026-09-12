using UnityEngine;

namespace Starfall.Presentation
{
    /// <summary>
    /// Procedurally synthesized sound effects. The project ships no audio
    /// assets, so weapon fire, explosions, mining ticks and docking chimes are
    /// generated once at startup into short mono clips and played through a
    /// small pool of round-robin voices.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StarfallSfx : MonoBehaviour
    {
        public const float DefaultVolume = 0.7f;
        private const string VolumePreferenceKey = "starfall.sfxvolume";
        private const int VoiceCount = 4;
        private const int SampleRate = 22050;

        public static StarfallSfx Current { get; private set; }

        private AudioSource[] voices = new AudioSource[VoiceCount];
        private AudioClip laser;
        private AudioClip explosion;
        private AudioClip mining;
        private AudioClip dock;
        private AudioClip jump;
        private int nextVoice;

        public float Volume { get; private set; } = DefaultVolume;

        private void OnEnable()
        {
            Current = this;
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
        }

        private void Start()
        {
            Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePreferenceKey, DefaultVolume));
            laser = CreateLaser();
            explosion = CreateExplosion();
            mining = CreateMining();
            dock = CreateDock();
            jump = CreateJump();
            for (var i = 0; i < VoiceCount; i++)
            {
                var voiceObject = new GameObject("SfxVoice" + i);
                voiceObject.transform.SetParent(transform, false);
                var source = voiceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.priority = 64;
                voices[i] = source;
            }
        }

        public void SetVolume(float value)
        {
            Volume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumePreferenceKey, Volume);
            PlayerPrefs.Save();
        }

        public void PlayLaser() => Play(laser);
        public void PlayExplosion() => Play(explosion);
        public void PlayMining() => Play(mining);
        public void PlayDock() => Play(dock);
        public void PlayJump() => Play(jump);

        private void Play(AudioClip clip)
        {
            if (clip == null) return;
            var source = voices[nextVoice % VoiceCount];
            nextVoice++;
            source.PlayOneShot(clip, Volume);
        }

        private static AudioClip CreateLaser()
        {
            const float duration = 0.16f;
            var samples = (int)(SampleRate * duration);
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)samples;
                var frequency = Mathf.Lerp(920f, 210f, t);
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * (i / (float)SampleRate)) * Mathf.Exp(-t * 7f) * 0.55f;
            }
            return Bake("sfx_laser", data);
        }

        private static AudioClip CreateExplosion()
        {
            const float duration = 0.55f;
            var samples = (int)(SampleRate * duration);
            var data = new float[samples];
            var lowpassed = 0f;
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)samples;
                lowpassed += (Random.value * 2f - 1f - lowpassed) * 0.22f;
                data[i] = lowpassed * Mathf.Exp(-t * 5.5f) * 0.9f;
            }
            return Bake("sfx_explosion", data);
        }

        private static AudioClip CreateMining()
        {
            const float duration = 0.09f;
            var samples = (int)(SampleRate * duration);
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)samples;
                data[i] = Mathf.Sin(2f * Mathf.PI * 240f * (i / (float)SampleRate)) * Mathf.Exp(-t * 9f) * 0.3f;
            }
            return Bake("sfx_mining", data);
        }

        private static AudioClip CreateDock()
        {
            const float duration = 0.26f;
            var samples = (int)(SampleRate * duration);
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)SampleRate;
                var first = t < 0.1f;
                var frequency = first ? 520f : 780f;
                var local = first ? t / 0.1f : (t - 0.1f) / 0.16f;
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * Mathf.Exp(-local * 5f) * 0.4f;
            }
            return Bake("sfx_dock", data);
        }

        private static AudioClip CreateJump()
        {
            const float duration = 0.45f;
            var samples = (int)(SampleRate * duration);
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)samples;
                var frequency = Mathf.Lerp(170f, 880f, t);
                var envelope = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * (i / (float)SampleRate)) * envelope * 0.45f;
            }
            return Bake("sfx_jump", data);
        }

        private static AudioClip Bake(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
