using System;

namespace KingdomBouns.Core
{
    /// <summary>Логіка вікна налаштувань: повзунки гучності, відкрити/закрити.</summary>
    public sealed class SettingsService : IGameService, IDisposable
    {
        public bool IsOpen => _view.gameObject.activeSelf;

        private readonly SettingsPanelView _view;
        private SettingsSaveService _settings;

        public SettingsService(SettingsPanelView view)
        {
            _view = view;
        }

        public void Bind(ServicesProvider services)
        {
            _settings = services.GetService<SettingsSaveService>();
        }

        public void OnLaunchGame()
        {
            _view.MusicVolumeSlider.SetValueWithoutNotify(_settings.MusicVolume);
            _view.EffectsVolumeSlider.SetValueWithoutNotify(_settings.EffectsVolume);

            _view.MusicVolumeSlider.onValueChanged.AddListener(_settings.SetMusicVolume);
            _view.EffectsVolumeSlider.onValueChanged.AddListener(_settings.SetEffectsVolume);
            _view.CloseButton.onClick.AddListener(Close);

            _view.gameObject.SetActive(false);
        }

        public void Open() => _view.gameObject.SetActive(true);

        public void Close()
        {
            _view.gameObject.SetActive(false);
            _settings.Save();
        }

        public void Dispose()
        {
            if (_view == null)
                return;

            _view.MusicVolumeSlider.onValueChanged.RemoveListener(_settings.SetMusicVolume);
            _view.EffectsVolumeSlider.onValueChanged.RemoveListener(_settings.SetEffectsVolume);
            _view.CloseButton.onClick.RemoveListener(Close);
        }
    }
}
