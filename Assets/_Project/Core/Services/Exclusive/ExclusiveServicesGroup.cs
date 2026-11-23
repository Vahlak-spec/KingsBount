using System.Collections.Generic;

namespace KingdomBouns.Core
{
    /// <summary>Група взаємовиключних сервісів: активним може бути лише один.</summary>
    public sealed class ExclusiveServicesGroup : IGameService
    {
        private readonly List<ExclusiveServiceBase> _services = new List<ExclusiveServiceBase>();

        public ExclusiveServiceBase Current { get; private set; }

        public T AddService<T>(T service) where T : ExclusiveServiceBase
        {
            if (!_services.Contains(service))
            {
                _services.Add(service);
                service.SetGroup(this);
            }
            return service;
        }

        public void Bind(ServicesProvider services) { }
        public void OnLaunchGame() { }

        public void OnServiceStartWork(ExclusiveServiceBase service)
        {
            Current = service;
            for (int i = 0; i < _services.Count; i++)
            {
                if (_services[i] != service)
                    _services[i].EndServiceProcess();
            }
        }

        /// <summary>Закрити все (наприклад, по кнопці «Назад»).</summary>
        public void EndAll()
        {
            Current = null;
            for (int i = 0; i < _services.Count; i++)
                _services[i].EndServiceProcess();
        }
    }
}
