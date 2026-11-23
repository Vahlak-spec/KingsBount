using UnityEngine;

namespace KingdomBouns.Core
{
    /// <summary>Точка входу сцени головного меню.</summary>
    public sealed class MainMenuSceneBinder : SceneBinderBase
    {
        [SerializeField] private MainMenuView _view;
        [Tooltip("Назва сцени гри (має бути в Build Settings).")]
        [SerializeField] private string _gameSceneName = "GameScene";
        [Tooltip("Назва кліпу в Resources/Sounds/Music. Порожньо — без музики.")]
        [SerializeField] private string _music = "";

        protected override void RegisterServices(ServicesProvider services)
        {
            services.AddService(new SaveService());
            services.AddService(new SettingsSaveService());
            services.AddService(new SettingsService(_view.SettingsPanel));
            services.AddService(new MainMenuService(_view, _gameSceneName));
        }

        protected override void OnLaunched()
        {
            if (!string.IsNullOrEmpty(_music))
                SoundsManager.PlayMusic(_music);
        }
    }
}
