using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

namespace KingdomBouns.Core
{
    /// <summary>
    /// Одне джерело звуку з пулу SoundsManager. Створюється лише менеджером.
    /// Посилання на Sound не варто тримати після Stop(): об'єкт повертається в пул і буде перевикористаний.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class Sound : MonoBehaviour
    {
        private const int PRIORITY_MUSIC = 0;      // 0 — найвищий пріоритет
        private const int PRIORITY_EFFECTS = 128;

        public AudioClip Clip => _source.clip;
        public SoundStreams Stream { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }

        /// <summary>Гучність цього звуку 0..1 (поверх гучності потоку в мікшері).</summary>
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Mathf.Clamp01(value);
                _source.volume = _volume;
            }
        }

        /// <summary>Спрацьовує, коли звук зупинено (вручну, після фейду або коли кліп дограв).</summary>
        public event Action<Sound> onStop;
        internal event Action<Sound> released;

        private AudioSource _source;
        private Transform _follow;
        private Coroutine _fade;
        private float _volume = 1f;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.spread = 0f;
        }

        internal void Setup(AudioClip clip, SoundStreams stream, AudioMixerGroup group, bool loop, float pitch,
            bool is3D, Vector3 position, Transform follow)
        {
            Stream = stream;
            _follow = follow;

            _source.clip = clip;
            _source.outputAudioMixerGroup = group;
            _source.loop = loop;
            _source.pitch = pitch;
            _source.time = 0f;
            _source.priority = stream == SoundStreams.MUSIC ? PRIORITY_MUSIC : PRIORITY_EFFECTS;
            _source.spatialBlend = is3D ? 1f : 0f;
            _source.minDistance = SoundsManager.MinDistance3D;
            _source.maxDistance = SoundsManager.MaxDistance3D;

            transform.position = follow != null ? follow.position : position;
            Volume = 1f;
        }

        internal void Play()
        {
            gameObject.SetActive(true);
            IsPlaying = true;
            IsPaused = false;
            _source.Play();
        }

        public void Pause()
        {
            if (!IsPlaying || IsPaused)
                return;
            IsPaused = true;
            _source.Pause();
        }

        public void Resume()
        {
            if (!IsPlaying || !IsPaused)
                return;
            IsPaused = false;
            _source.UnPause();
        }

        public void Stop()
        {
            if (!IsPlaying)
                return;

            IsPlaying = false;
            IsPaused = false;
            StopFade();
            _source.Stop();
            _follow = null;

            onStop?.Invoke(this);
            onStop = null;
            released?.Invoke(this);
        }

        /// <summary>Плавно змінити гучність. Працює і при Time.timeScale = 0.</summary>
        public void FadeTo(float volume, float duration, bool stopAfter = false)
        {
            if (!IsPlaying)
                return;

            StopFade();
            if (duration <= 0f || !gameObject.activeInHierarchy)
            {
                Volume = volume;
                if (stopAfter)
                    Stop();
                return;
            }

            _fade = StartCoroutine(FadeRoutine(volume, duration, stopAfter));
        }

        public void FadeOut(float duration) => FadeTo(0f, duration, true);

        private void LateUpdate()
        {
            if (!IsPlaying)
                return;

            if (_follow != null)
                transform.position = _follow.position;

            // Кліп дограв (не loop). Під час AudioListener.pause isPlaying теж false — тоді чекаємо.
            if (!IsPaused && !_source.isPlaying && !AudioListener.pause)
                Stop();
        }

        private IEnumerator FadeRoutine(float target, float duration, bool stopAfter)
        {
            float start = _volume;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                Volume = Mathf.Lerp(start, target, t / duration);
                yield return null;
            }

            Volume = target;
            _fade = null;
            if (stopAfter)
                Stop();
        }

        private void StopFade()
        {
            if (_fade == null)
                return;
            StopCoroutine(_fade);
            _fade = null;
        }
    }
}
