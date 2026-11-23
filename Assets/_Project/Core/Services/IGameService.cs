namespace KingdomBouns.Core
{
    /// <summary>
    /// Сервіс сцени. Життєвий цикл:
    /// конструктор (лише власні дані) → Bind (беремо інші сервіси) → OnLaunchGame (старт логіки, підписки на Ticker).
    /// Якщо сервіс підписується на події, реалізуй ще System.IDisposable — ServicesProvider викличе Dispose при знищенні сцени.
    /// </summary>
    public interface IGameService
    {
        void Bind(ServicesProvider services);
        void OnLaunchGame();
    }
}
