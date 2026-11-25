using KingdomBouns.CityMap;
using KingdomBouns.Core;
using KingdomBouns.Economy;
using UnityEngine;

namespace KingdomBouns
{
    public sealed class CitySceneBinder : SceneBinderBase
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

        [Header("Гексова карта")]
        [Tooltip("HexGrid зі сцени. Порожньо — знайдеться автоматично.")]
        [SerializeField] private HexGrid _hexGrid;
        [Tooltip("Тимчасово, поки нема сервісу-завантажувача: JSON карти, яка відкриється на старті.")]
        [SerializeField] private TextAsset _testMap;

        [Header("Економіка")]
        [Tooltip("Параметри будівель і правила економіки міст.")]
        [SerializeField] private CityEconomyConfig _economyConfig;

        protected override void RegisterServices(ServicesProvider services)
        {
            services.AddService(new SaveService());
            services.AddService(new SettingsSaveService());

            if (_settingsPanel != null)
                services.AddService(new SettingsService(_settingsPanel));

            services.AddService(new PoolService(_poolContainer));
            services.AddService(new InputService());

            if (_useStrategyCamera)
                services.AddService(new StrategyCameraService(_camera != null ? _camera : Camera.main, _cameraSettings));

            services.AddService(new ExclusiveServicesGroup());

            var grid = _hexGrid != null ? _hexGrid : FindAnyObjectByType<HexGrid>();
            if (grid != null)
                services.AddService(new HexMapService(grid));
            else
                Debug.LogError("[CitySceneBinder] На сцені нема HexGrid — HexMapService не зареєстровано.", this);

            services.AddService(new EmpireResourcesService());
            if (_economyConfig != null)
                services.AddService(new CityEconomyService(_economyConfig));
            else
                Debug.LogError("[CitySceneBinder] Не призначено CityEconomyConfig — CityEconomyService не зареєстровано.", this);
        }

        protected override void OnLaunched()
        {
            if (!string.IsNullOrEmpty(_music))
                SoundsManager.PlayMusic(_music);

            // Тимчасово: поки нема сервісу-завантажувача, відкриваємо тестову карту.
            if (_testMap != null && Services.TryGetService(out HexMapService hexMap) && !hexMap.IsBound)
                hexMap.BindMapJson(_testMap.text);
        }
    }
}
