using System;
using UnityEngine;

namespace KingdomBouns.Core
{
    /// <summary>
    /// Базовий клас для об'єктів пулу. Вільний об'єкт = вимкнений (activeSelf == false).
    /// Повернути в пул: ReturnToPool() (або просто gameObject.SetActive(false), як у Pawnshop).
    /// </summary>
    public abstract class PoolObject : MonoBehaviour
    {
        private Action<PoolObject> _returnToPool;

        internal void SetPool(Action<PoolObject> returnToPool) => _returnToPool = returnToPool;

        /// <summary>Об'єкт видали з пулу (вже активний). Скинь тут стан.</summary>
        public virtual void OnSummon() { }

        /// <summary>Об'єкт повертається в пул (ще активний).</summary>
        public virtual void OnDespawn() { }

        public void ReturnToPool()
        {
            if (_returnToPool != null)
                _returnToPool(this);
            else
                gameObject.SetActive(false);
        }
    }
}
