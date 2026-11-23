using UnityEngine;
using UnityEngine.Audio;

namespace KingdomBouns.Core
{
    /// <summary>
    /// Точка входу сцени: ініціалізує звук, створює ServicesProvider, реєструє сервіси, робить Bind і LaunchGame.
    /// На сцені має бути рівно один біндер. При знищенні сцени всі IDisposable-сервіси отримують Dispose.
    /// </summary>
    public abstract class SceneBinderBase : MonoBehaviour
    {
        [SerializeField] private Ticker _ticker;
        [Header("Audio")]
        [SerializeField] private AudioMixer _audioMixer;
        [SerializeField] private AudioMixerGroup _effectsGroup;
        [SerializeField] private AudioMixerGroup _musicGroup;

        protected ServicesProvider Services { get; private set; }

        // Start, а не Awake: AudioMixer.SetFloat в Awake першого кадру ігнорується.
        private void Start()
        {
            SoundsManager.Init(_effectsGroup, _musicGroup, _audioMixer);

            Services = new ServicesProvider(ResolveTicker());
            RegisterServices(Services);
            Services.Bind();
            Services.LaunchGame();

            OnLaunched();
        }

        /// <summary>Створи й зареєструй сервіси сцени через services.AddService(...).</summary>
        protected abstract void RegisterServices(ServicesProvider services);

        /// <summary>Викликається після LaunchGame усіх сервісів.</summary>
        protected virtual void OnLaunched() { }

        protected virtual void OnDestroy()
        {
            Services?.Dispose();
            Services = null;
        }

        private Ticker ResolveTicker()
        {
            if (_ticker != null)
                return _ticker;

            _ticker = GetComponent<Ticker>();
            if (_ticker == null)
                _ticker = gameObject.AddComponent<Ticker>();

            return _ticker;
        }
    }
}
