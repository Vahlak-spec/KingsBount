using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace KingdomBouns.Core
{
    public enum SoundStreams
    {
        MUSIC,
        EFFECTS,
    }

    /// <summary>
    /// Статичний менеджер звуку з пулом AudioSource, що живе між сценами (DontDestroyOnLoad).
    ///
    /// Кліпи за назвою вантажаться з Resources:
    ///   Resources/Sounds/Effects/&lt;name&gt;  — ефекти
    ///   Resources/Sounds/Music/&lt;name&gt;    — музика
    ///
    ///   SoundsManager.PlaySound("Click");                      // 2D (UI)
    ///   SoundsManager.PlaySoundAt("Hit", worldPos);            // 3D у точці
    ///   SoundsManager.PlaySoundAttached("March", hero.transform); // 3D, слідує за об'єктом
    ///   SoundsManager.PlayMusic("Map", fadeDuration: 1f);      // кросфейд з попередньою
    ///
    /// Мікшер: групи SOUNDS і MUSIC, exposed-параметри MUSIC_VOLUME, EFFECTS_VOLUME (і MASTER_VOLUME).
    /// </summary>
    public static class SoundsManager
    {
        public const string MASTER_VOLUME_KEY = "MASTER_VOLUME";
        public const string MUSIC_VOLUME_KEY = "MUSIC_VOLUME";
        public const string EFFECTS_VOLUME_KEY = "EFFECTS_VOLUME";

        public const string SOUNDS_FOLDER_PATH = "Sounds/Effects/";
        public const string MUSIC_FOLDER_PATH = "Sounds/Music/";

        /// <summary>Скільки копій одного ефекту може звучати одночасно. Найстаріша обривається.</summary>
        public const int MAX_SAME_EFFECTS = 5;

        private const float MUTED_DB = -80f;

        /// <summary>
        /// Дистанції 3D-звуку. AudioListener зазвичай на камері; для стратегічної камери на висоті 10–40
        /// дистанції мають бути більші, ніж у шутері від першої особи.
        /// </summary>
        public static float MinDistance3D = 10f;
        public static float MaxDistance3D = 80f;

        public static bool IsInitialized => _root != null;
        public static Sound CurrentMusic => _music != null && _music.IsPlaying ? _music : null;

        private static Transform _root;
        private static Transform _effectsHolder;
        private static Transform _musicHolder;

        private static AudioMixer _mixer;
        private static AudioMixerGroup _effectsGroup;
        private static AudioMixerGroup _musicGroup;

        private static readonly List<Sound> _active = new List<Sound>();
        private static readonly Stack<Sound> _free = new Stack<Sound>();
        private static readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();

        private static Sound _music;
        private static float _musicVolume = 1f;
        private static float _effectsVolume = 1f;
        private static bool _musicMuted;
        private static bool _effectsMuted;

        // Скидання статики, якщо в Project Settings вимкнено Domain Reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _root = _effectsHolder = _musicHolder = null;
            _mixer = null;
            _effectsGroup = _musicGroup = null;
            _active.Clear();
            _free.Clear();
            _clipCache.Clear();
            _music = null;
            _musicVolume = _effectsVolume = 1f;
            _musicMuted = _effectsMuted = false;
        }

        public static void Init(AudioMixerGroup effectsGroup, AudioMixerGroup musicGroup, AudioMixer mixer)
        {
            if (IsInitialized)
                return;

            if (mixer == null)
                Debug.LogWarning("[Sounds] AudioMixer не призначено — гучність потоків не працюватиме.");

            _mixer = mixer;
            _effectsGroup = effectsGroup;
            _musicGroup = musicGroup;

            var root = new GameObject("[SOUNDS]");
            Object.DontDestroyOnLoad(root);
            _root = root.transform;
            _effectsHolder = new GameObject("<EFFECTS>").transform;
            _effectsHolder.SetParent(_root);
            _musicHolder = new GameObject("<MUSIC>").transform;
            _musicHolder.SetParent(_root);
        }

        // ---------------------------------------------------------------- ефекти

        public static Sound PlaySound(string name, float pitch = 1f, bool loop = false)
            => PlayEffect(LoadClip(SOUNDS_FOLDER_PATH, name), pitch, loop, false, Vector3.zero, null);

        public static Sound PlaySound(AudioClip clip, float pitch = 1f, bool loop = false)
            => PlayEffect(clip, pitch, loop, false, Vector3.zero, null);

        public static Sound PlaySoundAt(string name, Vector3 position, float pitch = 1f, bool loop = false)
            => PlayEffect(LoadClip(SOUNDS_FOLDER_PATH, name), pitch, loop, true, position, null);

        public static Sound PlaySoundAt(AudioClip clip, Vector3 position, float pitch = 1f, bool loop = false)
            => PlayEffect(clip, pitch, loop, true, position, null);

        public static Sound PlaySoundAttached(string name, Transform target, float pitch = 1f, bool loop = false)
            => PlayEffect(LoadClip(SOUNDS_FOLDER_PATH, name), pitch, loop, true, Vector3.zero, target);

        public static Sound PlaySoundAttached(AudioClip clip, Transform target, float pitch = 1f, bool loop = false)
            => PlayEffect(clip, pitch, loop, true, Vector3.zero, target);

        // ---------------------------------------------------------------- музика

        public static Sound PlayMusic(string name, float fadeDuration = 0.5f, bool loop = true)
            => PlayMusic(LoadClip(MUSIC_FOLDER_PATH, name), fadeDuration, loop);

        /// <summary>Якщо цей кліп уже грає — нічого не робить. Інакше кросфейд зі старою музикою.</summary>
        public static Sound PlayMusic(AudioClip clip, float fadeDuration = 0.5f, bool loop = true)
        {
            if (!CheckReady(clip))
                return null;

            if (_music != null && _music.IsPlaying && _music.Clip == clip)
                return _music;

            StopMusic(fadeDuration);

            Sound sound = Rent(_musicHolder);
            sound.Setup(clip, SoundStreams.MUSIC, _musicGroup, loop, 1f, false, Vector3.zero, null);
            sound.Play();
            if (fadeDuration > 0f)
            {
                sound.Volume = 0f;
                sound.FadeTo(1f, fadeDuration);
            }

            _music = sound;
            return sound;
        }

        public static void StopMusic(float fadeDuration = 0.5f)
        {
            if (_music == null)
                return;

            if (_music.IsPlaying)
                _music.FadeOut(fadeDuration);
            _music = null;
        }

        // ---------------------------------------------------------------- потоки

        public static void StopStream(SoundStreams stream)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (i < _active.Count && _active[i] != null && _active[i].Stream == stream)
                    _active[i].Stop();
            }

            if (stream == SoundStreams.MUSIC)
                _music = null;
        }

        public static void StopAll()
        {
            StopStream(SoundStreams.EFFECTS);
            StopStream(SoundStreams.MUSIC);
        }

        /// <summary>Пауза всього звуку (наприклад, при паузі гри). Працює через AudioListener.pause.</summary>
        public static void SetPaused(bool paused) => AudioListener.pause = paused;

        /// <summary>Гучність потоку 0..1. Переводиться в децибели логарифмічно, як чує вухо.</summary>
        public static void SetStreamVolume(SoundStreams stream, float value)
        {
            value = Mathf.Clamp01(value);
            if (stream == SoundStreams.MUSIC)
                _musicVolume = value;
            else
                _effectsVolume = value;

            ApplyStream(stream);
        }

        public static float GetStreamVolume(SoundStreams stream)
            => stream == SoundStreams.MUSIC ? _musicVolume : _effectsVolume;

        /// <summary>Вимкнути/увімкнути потік, не втрачаючи збережену гучність.</summary>
        public static void SetStreamMuted(SoundStreams stream, bool muted)
        {
            if (stream == SoundStreams.MUSIC)
                _musicMuted = muted;
            else
                _effectsMuted = muted;

            ApplyStream(stream);
        }

        public static void SetMasterVolume(float value)
        {
            if (_mixer != null)
                _mixer.SetFloat(MASTER_VOLUME_KEY, LinearToDb(Mathf.Clamp01(value)));
        }

        public static float LinearToDb(float value)
            => value <= 0.0001f ? MUTED_DB : Mathf.Max(MUTED_DB, 20f * Mathf.Log10(value));

        // ---------------------------------------------------------------- внутрішнє

        private static Sound PlayEffect(AudioClip clip, float pitch, bool loop, bool is3D, Vector3 position, Transform follow)
        {
            if (!CheckReady(clip))
                return null;

            LimitSameEffects(clip);

            Sound sound = Rent(_effectsHolder);
            sound.Setup(clip, SoundStreams.EFFECTS, _effectsGroup, loop, pitch, is3D, position, follow);
            sound.Play();
            return sound;
        }

        private static void LimitSameEffects(AudioClip clip)
        {
            Sound oldest = null;
            int count = 0;
            for (int i = 0; i < _active.Count; i++)
            {
                Sound s = _active[i];
                if (s != null && s.Stream == SoundStreams.EFFECTS && s.Clip == clip)
                {
                    count++;
                    if (oldest == null)
                        oldest = s;
                }
            }

            if (count >= MAX_SAME_EFFECTS && oldest != null)
                oldest.Stop();
        }

        private static Sound Rent(Transform holder)
        {
            Sound sound = null;
            while (_free.Count > 0 && sound == null)
                sound = _free.Pop();

            if (sound == null)
            {
                var go = new GameObject("Sound", typeof(AudioSource), typeof(Sound));
                sound = go.GetComponent<Sound>();
                sound.released += Release;
            }

            sound.transform.SetParent(holder, false);
            _active.Add(sound);
            return sound;
        }

        private static void Release(Sound sound)
        {
            _active.Remove(sound);
            if (sound == null)
                return;

            sound.gameObject.SetActive(false);
            _free.Push(sound);
        }

        private static AudioClip LoadClip(string folder, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogError("[Sounds] Порожня назва звуку.");
                return null;
            }

            string path = folder + name;
            if (_clipCache.TryGetValue(path, out AudioClip cached) && cached != null)
                return cached;

            AudioClip clip = Resources.Load<AudioClip>(path);
            if (clip == null)
            {
                Debug.LogError($"[Sounds] Не знайдено кліп Resources/{path}");
                return null;
            }

            _clipCache[path] = clip;
            return clip;
        }

        private static bool CheckReady(AudioClip clip)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[Sounds] SoundsManager.Init ще не викликано (це робить SceneBinderBase).");
                return false;
            }

            return clip != null;
        }

        private static void ApplyStream(SoundStreams stream)
        {
            if (_mixer == null)
                return;

            if (stream == SoundStreams.MUSIC)
                _mixer.SetFloat(MUSIC_VOLUME_KEY, _musicMuted ? MUTED_DB : LinearToDb(_musicVolume));
            else
                _mixer.SetFloat(EFFECTS_VOLUME_KEY, _effectsMuted ? MUTED_DB : LinearToDb(_effectsVolume));
        }
    }
}
