using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomBouns.Core
{
    /// <summary>
    /// Контейнер сервісів сцени (service locator).
    /// Сервіс реєструється під своїм конкретним типом, а якщо T — інтерфейс/базовий клас, то ще й під T:
    ///   services.AddService(new SaveService());                 // GetService&lt;SaveService&gt;()
    ///   services.AddService&lt;IHexMap&gt;(new HexMapService());     // GetService&lt;IHexMap&gt;() і GetService&lt;HexMapService&gt;()
    /// Bind / OnLaunchGame викликаються в порядку реєстрації, Dispose — у зворотному.
    /// </summary>
    public sealed class ServicesProvider : IDisposable
    {
        public Ticker Ticker { get; }

        private readonly Dictionary<Type, IGameService> _byType = new Dictionary<Type, IGameService>();
        private readonly List<IGameService> _ordered = new List<IGameService>();
        private bool _isBound;
        private bool _isDisposed;

        public ServicesProvider(Ticker ticker)
        {
            if (ticker == null)
                throw new ArgumentNullException(nameof(ticker));

            Ticker = ticker;
        }

        public T AddService<T>(T service) where T : class, IGameService
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));
            if (_isBound)
                throw new InvalidOperationException($"Сервіс {service.GetType().Name} треба реєструвати до Bind().");

            Type concrete = service.GetType();
            Register(concrete, service);

            Type contract = typeof(T);
            if (contract != concrete && contract != typeof(IGameService))
                Register(contract, service);

            _ordered.Add(service);
            return service;
        }

        public T GetService<T>() where T : class
        {
            if (_byType.TryGetValue(typeof(T), out IGameService service))
                return (T)service;

            throw new KeyNotFoundException($"Сервіс {typeof(T).Name} не зареєстровано.");
        }

        public bool TryGetService<T>(out T service) where T : class
        {
            if (_byType.TryGetValue(typeof(T), out IGameService found))
            {
                service = (T)found;
                return true;
            }

            service = null;
            return false;
        }

        public bool HasService<T>() where T : class => _byType.ContainsKey(typeof(T));

        public void Bind()
        {
            _isBound = true;
            for (int i = 0; i < _ordered.Count; i++)
                _ordered[i].Bind(this);
        }

        public void LaunchGame()
        {
            for (int i = 0; i < _ordered.Count; i++)
                _ordered[i].OnLaunchGame();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;

            for (int i = _ordered.Count - 1; i >= 0; i--)
            {
                if (_ordered[i] is IDisposable disposable)
                {
                    try { disposable.Dispose(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }

            _ordered.Clear();
            _byType.Clear();
        }

        private void Register(Type type, IGameService service)
        {
            if (_byType.ContainsKey(type))
                throw new InvalidOperationException($"Сервіс {type.Name} уже зареєстровано.");

            _byType.Add(type, service);
        }
    }
}
