# KingdomBouns.Core

Базові системи, перенесені з PawnshopSimulatorFinale і узагальнені. Namespace `KingdomBouns.Core`, збірка `KingdomBouns.Core.asmdef`.

## Швидкий старт

1. **KingdomBouns → Core → Створити сцену головного меню**: створює `Assets/_Project/Scenes/MainMenuScene.unity` з кнопками, вікном налаштувань і вже прив'язаним `MainMenuSceneBinder`.
2. Відкрий сцену з гекс-картою і вибери **KingdomBouns → Core → Додати Bootstrap гри в поточну сцену**. На сцені з'явиться `[Bootstrap]` з `Ticker` і `GameSceneBinder`: камера, мікшер і пули вже призначені.
3. **KingdomBouns → Core → Додати поточну сцену в Build Settings**, а в `[Bootstrap]` меню впиши назву цієї сцени в поле *Game Scene Name*.
4. Межі камери (`Bounds`) і зум налаштуй у `GameSceneBinder → Camera Settings` під розмір своєї мапи.

## Архітектура

| Що | Файл | Коротко |
|---|---|---|
| Сервіси | `Services/ServicesProvider.cs`, `IGameService.cs` | `AddService` → `Bind` (беремо залежності) → `OnLaunchGame`. `IDisposable`-сервіси отримують `Dispose`, коли сцена знищується |
| Ticker | `Services/Ticker.cs` | `onUpdate`, `onLateUpdate`, `onFixedUpdate`, `onApplicationPause`, `onApplicationQuit` |
| Біндер сцени | `Services/SceneBinderBase.cs` | Базовий клас точки входу: ініціалізує звук і проводить сервіси через Bind і LaunchGame |
| Взаємовиключні | `Services/Exclusive/*` | Сервіс викликає `StartWork()`, і решта сервісів групи закривається (`EndServiceProcess`) |
| Збереження | `Save/SaveService.cs` | `Save<T>(key)`, `LoadOrCreate<T>(key)`, `onSaveRequested` (спрацьовує при згортанні й виході), `DeleteAll()` для нової гри |
| Налаштування | `Settings/*` | Гучність музики й ефектів. Зберігається окремо від прогресу (`Settings/`) |
| Звук | `Audio/SoundsManager.cs` | Статичний клас: `PlaySound`, `PlaySoundAt(Vector3)`, `PlaySoundAttached`, `PlayMusic` з кросфейдом. Кліпи беруться з `Resources/Sounds/Effects` і `Resources/Sounds/Music` |
| Пул | `Pool/*` | `CreatePool(prefab, n)`, `GetFreeElement()`, `ReturnToPool()` |
| Інпут | `Input/InputService.cs` | Тап, довге натискання, drag, pinch і коліщатко. Жест, що почався над UI, ігнорується |
| Камера | `Input/StrategyCameraService.cs` | Pan («прилипає» до пальця), зум до точки, межі, інерція. Метод `TryGetGroundPoint(screenPos)` повертає точку на мапі |

## Приклад сервісу гри

```csharp
public sealed class HexSelectService : IGameService, IDisposable
{
    private InputService _input;
    private StrategyCameraService _camera;
    private SaveService _save;
    private SelectionSave _data;

    public void Bind(ServicesProvider services)
    {
        _input = services.GetService<InputService>();
        _camera = services.GetService<StrategyCameraService>();
        _save = services.GetService<SaveService>();
    }

    public void OnLaunchGame()
    {
        _data = _save.LoadOrCreate<SelectionSave>("selection");
        _save.onSaveRequested += Save;
        _input.onTap += OnTap;
    }

    private void OnTap(Vector2 screenPos)
    {
        if (_camera.TryGetGroundPoint(screenPos, out Vector3 world))
            SoundsManager.PlaySound("Match");
    }

    private void Save() => _save.Save("selection", _data);

    public void Dispose()
    {
        _input.onTap -= OnTap;
        _save.onSaveRequested -= Save;
    }
}

[System.Serializable] public class SelectionSave { public int X, Y; }
```

## Що змінено порівняно з Pawnshop

- `PoolComponent.Add` більше не зациклюється. Вільний об'єкт визначається за `activeSelf`, а не `activeInHierarchy`. Додано `ReturnToPool()` і `OnDespawn()`.
- `SaveService` більше не прив'язаний до ресурсів, інвентарю й будівель: він зберігає будь-які дані за ключами. Запис іде через тимчасовий файл.
- `SoundsManager`: позиції тепер `Vector3`, гучність переводиться в дБ логарифмічно, кліпи кешуються, звук не обривається при знищенні об'єкта, за яким він слідує, музика має кросфейд. Виправлено подвійну видачу одного джерела з пулу.
- Налаштування пишуться на диск при закритті вікна або згортанні гри, а не на кожен рух повзунка.
- Кирилиця в UTF-8.
- `InputService` написано заново під тач і мишу. Працює і зі старим Input Manager, і з новим Input System.
