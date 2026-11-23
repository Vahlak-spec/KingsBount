using System;
using UnityEngine;

namespace KingdomBouns.Core
{
    /// <summary>
    /// Єдина точка Update для сервісів (сервіси — не MonoBehaviour).
    /// На мобілках гра часто вбивається з фону без OnApplicationQuit, тому зберігатися варто на onApplicationPause(true).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Ticker : MonoBehaviour
    {
        public event Action onUpdate;
        public event Action onLateUpdate;
        public event Action onFixedUpdate;

        /// <summary>true — гру згорнули / вона втратила фокус на мобілці, false — повернулися.</summary>
        public event Action<bool> onApplicationPause;
        public event Action<bool> onApplicationFocus;
        public event Action onApplicationQuit;

        private void Update() => onUpdate?.Invoke();
        private void LateUpdate() => onLateUpdate?.Invoke();
        private void FixedUpdate() => onFixedUpdate?.Invoke();

        private void OnApplicationPause(bool paused) => onApplicationPause?.Invoke(paused);
        private void OnApplicationFocus(bool hasFocus) => onApplicationFocus?.Invoke(hasFocus);
        private void OnApplicationQuit() => onApplicationQuit?.Invoke();
    }
}
