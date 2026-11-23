namespace KingdomBouns.Core
{
    /// <summary>
    /// Сервіс, який не може працювати одночасно з іншими з тієї ж групи (наприклад, вікна міста, героя, будівництва).
    /// Коли сервіс починає роботу, він викликає StartWork() — усі інші сервіси групи отримують EndServiceProcess().
    /// Реєстрація: services.AddService(x); exclusiveGroup.AddService(x);
    /// </summary>
    public abstract class ExclusiveServiceBase : IGameService
    {
        protected ExclusiveServicesGroup ExclusiveGroup { get; private set; }

        internal void SetGroup(ExclusiveServicesGroup group) => ExclusiveGroup = group;

        public abstract void Bind(ServicesProvider services);
        public abstract void OnLaunchGame();

        /// <summary>Закрити свій процес/вікно, бо почав працювати інший сервіс групи.</summary>
        public abstract void EndServiceProcess();

        /// <summary>Повідомити групу, що цей сервіс почав роботу.</summary>
        protected void StartWork() => ExclusiveGroup?.OnServiceStartWork(this);
    }
}
