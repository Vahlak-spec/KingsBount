using KingdomBouns.Core;
using UnityEngine;

namespace KingdomBouns
{
    /// <summary>
    /// Точка входу ігрової сцени. Базові сервіси вже зареєстровані — сюди ж додавай сервіси гри
    /// (мапа, міста, герої, бої), у порядку залежностей не обов'язково: залежності беруться в Bind.
    /// </summary>
    public sealed class GameSceneBinder : SceneBinderBase
    {
        [SerializeField] private Camera _camera;
        [Tooltip("Вимкни, якщо камерою керує інший скрипт (наприклад, CityCameraController).")]
        [SerializeField] private bool _useStrategyCamera = true;
        [SerializeField] private StrategyCameraSettings _cameraSettings = new StrategyCameraSettings();
        [Space]
        [SerializeField] private Transform _poolContainer;
        [Tooltip("Необов'язково: вікно налаштувань у грі.")]
        [SerializeField] private SettingsPanelView _settingsPanel;
        [Tooltip("Назва кліпу в Resources/Sounds/Music. Порожньо — без музики.")]
        [SerializeField] private string _music = "";

        protected override void RegisterServices(ServicesProvider services)
        {
            // --- база
            services.AddService(new SaveService());
            services.AddService(new SettingsSaveService());
            if (_settingsPanel != null)
                services.AddService(new SettingsService(_settingsPanel));
            services.AddService(new PoolService(_poolContainer));
            services.AddService(new InputService());
            if (_useStrategyCamera)
                services.AddService(new StrategyCameraService(_camera != null ? _camera : Camera.main, _cameraSettings));
            services.AddService(new ExclusiveServicesGroup());

            // --- гра
            // services.AddService(new HexMapService(...));
            // var cityWindow = services.AddService(new CityWindowService(...));
            // services.GetService<ExclusiveServicesGroup>().AddService(cityWindow);
        }

        protected override void OnLaunched()
        {
            if (!string.IsNullOrEmpty(_music))
                SoundsManager.PlayMusic(_music);
        }
    }
}
