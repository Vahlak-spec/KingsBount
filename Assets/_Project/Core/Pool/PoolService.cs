using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomBouns.Core
{
    /// <summary>
    /// Реєстр пулів сцени: один пул на префаб.
    ///   var pool = _pools.CreatePool(_unitPrefab, 20);
    ///   UnitView unit = pool.GetFreeElement();
    ///   unit.ReturnToPool();
    /// </summary>
    public sealed class PoolService : IGameService, IDisposable
    {
        private readonly Dictionary<GameObject, object> _pools = new Dictionary<GameObject, object>();
        private Transform _container;

        public PoolService(Transform container = null)
        {
            _container = container;
        }

        public void Bind(ServicesProvider services) { }
        public void OnLaunchGame() { }

        /// <summary>Повертає наявний пул для префабу (доростивши до amount) або створює новий.</summary>
        public PoolComponent<T> CreatePool<T>(T prefab, int amount, bool autoExpand = true) where T : PoolObject
        {
            if (prefab == null)
                throw new ArgumentNullException(nameof(prefab));

            if (_pools.TryGetValue(prefab.gameObject, out object existing))
            {
                if (!(existing is PoolComponent<T> pool))
                    throw new InvalidOperationException(
                        $"Для префабу {prefab.name} уже є пул іншого типу ({existing.GetType().Name}).");

                if (pool.Size < amount)
                    pool.Add(amount - pool.Size);
                return pool;
            }

            var holder = new GameObject($"[{prefab.name}] Pool").transform;
            holder.SetParent(GetRoot(), false);

            var newPool = new PoolComponent<T>(prefab, amount, holder, autoExpand);
            _pools.Add(prefab.gameObject, newPool);
            return newPool;
        }

        public bool TryGetPool<T>(T prefab, out PoolComponent<T> pool) where T : PoolObject
        {
            pool = null;
            return prefab != null
                && _pools.TryGetValue(prefab.gameObject, out object existing)
                && (pool = existing as PoolComponent<T>) != null;
        }

        /// <summary>Забути всі пули (об'єкти знищуються разом зі сценою).</summary>
        public void Clear() => _pools.Clear();

        public void Dispose() => Clear();

        private Transform GetRoot()
        {
            if (_container == null)
                _container = new GameObject("[POOLS]").transform;
            return _container;
        }
    }
}
