using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomBouns.Core
{
    /// <summary>Пул екземплярів одного префабу.</summary>
    public sealed class PoolComponent<T> where T : PoolObject
    {
        private readonly List<T> _pool = new List<T>();
        private readonly T _prefab;
        private readonly bool _autoExpand;
        private Transform _container;

        public T Prefab => _prefab;
        public Transform Container => _container;
        public int Size => _pool.Count;

        public PoolComponent(T prefab, int count, Transform container, bool autoExpand)
        {
            if (prefab == null)
                throw new ArgumentNullException(nameof(prefab));

            _prefab = prefab;
            _autoExpand = autoExpand;
            _container = container;

            Add(count);
        }

        /// <summary>Додати amount нових (вимкнених) об'єктів.</summary>
        public void Add(int amount)
        {
            for (int i = 0; i < amount; i++)
                CreateObject(false);
        }

        public void BoundContainer(Transform container) => _container = container;

        public T GetFreeElement()
        {
            if (TryTakeFree(out T element))
                return element;

            if (_autoExpand)
                return Summon(CreateObject(true));

            throw new InvalidOperationException($"У пулі {_prefab.name} немає вільних об'єктів.");
        }

        public List<T> GetElements(int count)
        {
            var elements = new List<T>(count);
            for (int i = 0; i < count; i++)
                elements.Add(GetFreeElement());
            return elements;
        }

        /// <summary>Повернути об'єкт у пул.</summary>
        public void Release(T element)
        {
            if (element == null || !element.gameObject.activeSelf)
                return;

            element.OnDespawn();
            element.gameObject.SetActive(false);
            if (element.transform.parent != _container)
                element.transform.SetParent(_container, false);
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < _pool.Count; i++)
                Release(_pool[i]);
        }

        public List<T> GetAllFreeElements()
        {
            RemoveDestroyed();
            return _pool.FindAll(e => !e.gameObject.activeSelf);
        }

        public List<T> GetAllActiveElements()
        {
            RemoveDestroyed();
            return _pool.FindAll(e => e.gameObject.activeSelf);
        }

        public List<T> GetAllElements()
        {
            RemoveDestroyed();
            return new List<T>(_pool);
        }

        public void ClearPool()
        {
            foreach (T item in _pool)
            {
                if (item != null)
                    Object.Destroy(item.gameObject);
            }
            _pool.Clear();
        }

        private T CreateObject(bool active)
        {
            T created = Object.Instantiate(_prefab, _container);
            created.gameObject.SetActive(active);
            created.SetPool(o => Release((T)o));
            _pool.Add(created);
            return created;
        }

        private bool TryTakeFree(out T element)
        {
            for (int i = _pool.Count - 1; i >= 0; i--)
            {
                T candidate = _pool[i];
                if (candidate == null)
                {
                    _pool.RemoveAt(i);   // знищили ззовні
                    continue;
                }

                if (!candidate.gameObject.activeSelf)
                {
                    candidate.gameObject.SetActive(true);
                    element = Summon(candidate);
                    return true;
                }
            }

            element = null;
            return false;
        }

        private static T Summon(T element)
        {
            element.OnSummon();
            return element;
        }

        private void RemoveDestroyed() => _pool.RemoveAll(e => e == null);
    }
}
