using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KingdomBouns.Core
{
    public sealed class MainMenuService : IGameService, IDisposable
    {
        private readonly MainMenuView _view;
        private readonly string _gameSceneName;

        private SaveService _save;
        private SettingsService _settings;

        public MainMenuService(MainMenuView view, string gameSceneName)
        {
            _view = view;
            _gameSceneName = gameSceneName;
        }

        public void Bind(ServicesProvider services)
        {
            _save = services.GetService<SaveService>();
            _settings = services.GetService<SettingsService>();
        }

        public void OnLaunchGame()
        {
            _view.ContinueButton.onClick.AddListener(ContinueGame);
            _view.NewGameButton.onClick.AddListener(NewGame);
            _view.SettingsButton.onClick.AddListener(_settings.Open);
            _view.QuitButton.onClick.AddListener(QuitGame);

            _view.ContinueButton.interactable = _save.HasSave;

#if UNITY_IOS
            _view.QuitButton.gameObject.SetActive(false);   // на iOS застосунки себе не закривають
#endif
        }

        public void Dispose()
        {
            if (_view == null)
                return;

            _view.ContinueButton.onClick.RemoveListener(ContinueGame);
            _view.NewGameButton.onClick.RemoveListener(NewGame);
            _view.SettingsButton.onClick.RemoveListener(_settings.Open);
            _view.QuitButton.onClick.RemoveListener(QuitGame);
        }

        private void ContinueGame() => LoadGame();

        private void NewGame()
        {
            _save.DeleteAll();
            LoadGame();
        }

        private void LoadGame()
        {
            if (!Application.CanStreamedLevelBeLoaded(_gameSceneName))
            {
                Debug.LogError($"[MainMenu] Сцени '{_gameSceneName}' немає в Build Settings (File → Build Profiles / Build Settings).");
                return;
            }

            SceneManager.LoadScene(_gameSceneName);
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
