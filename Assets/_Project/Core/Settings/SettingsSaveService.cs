using System;

namespace KingdomBouns.Core
{
    /// <summary>
    /// Дані налаштувань (гучність) + застосування до SoundsManager.
    /// Зміни пишуться на диск не на кожен рух повзунка, а при Save(), згортанні й виході.
    /// </summary>
    public sealed class SettingsSaveService : IGameService, IDisposable
    {
        private const string FOLDER = "Settings";
        private const string KEY = "settings";

        public float MusicVolume => _data.MusicVolume;
        public float EffectsVolume => _data.EffectsVolume;

        private readonly JsonFileStorage _storage = new JsonFileStorage(FOLDER);
        private SettingsSaveData _data;
        private bool _isDirty;
        private Ticker _ticker;

        public SettingsSaveService()
        {
            if (!_storage.TryRead(KEY, out _data))
                _data = new SettingsSaveData();
        }

        public void Bind(ServicesProvider services)
        {
            _ticker = services.Ticker;
        }

        public void OnLaunchGame()
        {
            ApplyToSoundsManager();
            _ticker.onApplicationPause += OnApplicationPause;
            _ticker.onApplicationQuit += Save;
        }

        public void SetMusicVolume(float value)
        {
            _data.MusicVolume = value;
            SoundsManager.SetStreamVolume(SoundStreams.MUSIC, value);
            _isDirty = true;
        }

        public void SetEffectsVolume(float value)
        {
            _data.EffectsVolume = value;
            SoundsManager.SetStreamVolume(SoundStreams.EFFECTS, value);
            _isDirty = true;
        }

        public void ApplyToSoundsManager()
        {
            SoundsManager.SetStreamVolume(SoundStreams.MUSIC, _data.MusicVolume);
            SoundsManager.SetStreamVolume(SoundStreams.EFFECTS, _data.EffectsVolume);
        }

        public void Save()
        {
            if (!_isDirty)
                return;

            _storage.Write(KEY, _data);
            _isDirty = false;
        }

        public void Dispose()
        {
            Save();
            if (_ticker == null)
                return;

            _ticker.onApplicationPause -= OnApplicationPause;
            _ticker.onApplicationQuit -= Save;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                Save();
        }
    }
}
