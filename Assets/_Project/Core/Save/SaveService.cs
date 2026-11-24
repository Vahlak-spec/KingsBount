using System;

namespace KingdomBouns.Core
{
    /// <summary>
    /// Збереження прогресу за ключами. Кожен сервіс сам вирішує, що і під яким ключем зберігати:
    ///
    ///   Bind:          _save = services.GetService&lt;SaveService&gt;();
    ///   OnLaunchGame:  _data = _save.LoadOrCreate&lt;CitiesSaveData&gt;("cities");
    ///                  _save.onSaveRequested += () =&gt; _save.Save("cities", _data);
    ///
    /// onSaveRequested спрацьовує при SaveAll(), згортанні гри (мобілка) і виході.
    /// Налаштування лежать окремо (SettingsSaveService), тож DeleteAll() для «Нової гри» їх не зачіпає.
    /// </summary>
    public sealed class SaveService : IGameService, IDisposable
    {
        public const string DEFAULT_FOLDER = "Saves";

        public event Action onSaveRequested;

        public bool HasSave => _storage.Any();

        private readonly JsonFileStorage _storage;
        private Ticker _ticker;

        public SaveService(string folder = DEFAULT_FOLDER)
        {
            _storage = new JsonFileStorage(folder);
        }

        public void Bind(ServicesProvider services)
        {
            _ticker = services.Ticker;
        }

        public void OnLaunchGame()
        {
            _ticker.onApplicationPause += OnApplicationPause;
            _ticker.onApplicationQuit += SaveAll;
        }

        public void Save<T>(string key, T data) => _storage.Write(key, data);

        public bool TryLoad<T>(string key, out T data) => _storage.TryRead(key, out data);

        public T Load<T>(string key, T fallback = default)
            => _storage.TryRead(key, out T data) ? data : fallback;

        public T LoadOrCreate<T>(string key) where T : new()
            => _storage.TryRead(key, out T data) ? data : new T();

        public bool Has(string key) => _storage.Has(key);

        public void Delete(string key) => _storage.Delete(key);

        /// <summary>Видалити весь прогрес (налаштування лишаються).</summary>
        public void DeleteAll() => _storage.DeleteAll();

        /// <summary>Попросити всі сервіси зберегтися зараз.</summary>
        public void SaveAll() => onSaveRequested?.Invoke();

        public void Dispose()
        {
            if (_ticker == null)
                return;

            _ticker.onApplicationPause -= OnApplicationPause;
            _ticker.onApplicationQuit -= SaveAll;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                SaveAll();
        }
    }
}
