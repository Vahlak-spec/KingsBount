using System;
using KingdomBouns.Core;

namespace KingdomBouns.Economy
{
    /// <summary>Ресурси рівня імперії (GDD «Ресурси»): гроші й наука спільні для всіх міст.</summary>
    public enum EmpireResource
    {
        Gold = 0,
        Science = 1,
    }

    [Serializable]
    public class EmpireResourcesData
    {
        public float gold;
        public float science;
    }

    /// <summary>
    /// Скарбниця імперії. Зберігається через SaveService (ключ "empire").
    /// Гроші можуть піти в мінус (GDD: механіка боргу — відкрите питання).
    /// </summary>
    public sealed class EmpireResourcesService : IGameService, IDisposable
    {
        public const string SAVE_KEY = "empire";

        /// <summary>Ресурс змінився: (ресурс, нове значення, зміна).</summary>
        public event Action<EmpireResource, float, float> onChanged;

        public float Gold => _data.gold;
        public float Science => _data.science;

        private SaveService _save;
        private EmpireResourcesData _data = new EmpireResourcesData();

        public void Bind(ServicesProvider services)
        {
            _save = services.GetService<SaveService>();
        }

        public void OnLaunchGame()
        {
            _data = _save.LoadOrCreate<EmpireResourcesData>(SAVE_KEY);
            _save.onSaveRequested += Save;
        }

        public float Get(EmpireResource resource) => resource == EmpireResource.Gold ? _data.gold : _data.science;

        public void Add(EmpireResource resource, float amount)
        {
            if (amount == 0f)
                return;
            Set(resource, Get(resource) + amount);
        }

        /// <summary>Списати, якщо вистачає. Повертає false і нічого не змінює, якщо ні.</summary>
        public bool TrySpend(EmpireResource resource, float amount)
        {
            if (amount < 0f || Get(resource) < amount)
                return false;
            Set(resource, Get(resource) - amount);
            return true;
        }

        public void Set(EmpireResource resource, float value)
        {
            float old = Get(resource);
            if (resource == EmpireResource.Gold) _data.gold = value;
            else _data.science = value;
            onChanged?.Invoke(resource, value, value - old);
        }

        public void Save() => _save.Save(SAVE_KEY, _data);

        public void Dispose()
        {
            if (_save != null)
                _save.onSaveRequested -= Save;
            onChanged = null;
        }
    }
}
