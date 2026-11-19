# Н13 — ночь 19→20.11 (оркестратор + субагенты)

Бриф владельца (19.11 вечер):
1. Система проигрыша: увольнение ТОЛЬКО по долгу свыше порога, независимо от аварий.
   Убрать unpaidAccidents отовсюду. Авария остаётся событием (долг +400 Đ, урон, звук).
2. Прыжок спрайта: корабль садится чуть ниже, потом резко подпрыгивает до нормы — убрать.
3. Настройки: вырезать выбор оконного/полноэкранного режима и его обработку.
4. UiKit расширить: игровые экраны указывают ЧТО будет, а не подгоняют координаты/rect
   руками. Настройки (SettingsBinder «куча фигни») — первый кандидат на миграцию.
5. Убрать fuellog (ShipController).
6. Чеврон не показывает, пока стоишь на планете — исправить.
7. По каждому файлу — упростить/переписать/сгруппировать (субагенты). Особенно UI.
8. В финальном отчёте: какие паттерны ООП используются + примеры.
9. Почистить лишние комментарии в коде (владелец выбрал: комментарии, НЕ история коммитов) —
   оставить только необходимые.
10. После работы — отправить ноутбук в гибернацию (shutdown /h, последняя операция).

Ответы владельца на вопросы: авария = долг (+400 Đ); чистим комментарии в коде, историю
коммитов НЕ трогаем.

## Чекпоинт 1 — рекон и gameplay-правки

## Чекпоинт 4 — финал (~04:30, 20.11)

- **Н13.4/Н13.5**: прыжок при посадке (depenetration-зазор в PinchToPose), чеврон при борткомпе
  (левая колонна-зона клампа вместо глушения) — unit + живой прогон (y стабилен 748.233 ×4 кадра).
- **Н13.6**: Ui.Slider примитив + UiBox.LabelBox/SliderBox/Absolute/Text; SettingsBinder 306→112 —
  полная декларативная пересборка через UiKit (координаты в тулките).
- **Н13.7**: сваг — OnboardBinder 1121→4 partial (215/484/304/151), HudBinder 588→333+Build.cs 268,
  чистка комментариев (дубли/протухшие ссылки удалены, «почему» сохранены).
- Игровой старт чистый (0 ошибок), все экраны живы.

---

# ОТЧЁТ: паттерны ООП в коде SpaceTaxi (материал к защите)

## 1. Композиционный корень + Фасад
**GameSession.cs** — единственное место сборки графа: WorldBuilder строит мир, сервисы
(Wallet/DebtService/FleetService/RatingService/PayoutService) создаются и связываются здесь,
UI/звук/сейв — независимые подписчики. Фасад **Sound** (AudioManager.cs, static class):
любой код зовёт `Sound.Play(SoundId.Takeoff)` без знания о менеджере.
*Пример*: GameSession.cs:57-62 — `Payout.Bind(Wallet, Debt)`.

## 2. Одиночка (_service locator-ish)
**AudioManager.Instance**, **CameraFollow.Instance** — DontDestroyOnLoad-менеджеры с
public static точкой; захардкожен guard от дублей в Awake.
*Пример*: AudioManager.cs:49-54 (`if (Instance != null && Instance != this) Destroy`).

## 3. Наблюдатель / событийная архитектура (главный паттерн проекта)
Доменные события — C# delegates; UI/звук/сейв/рейтинг — независимые подписчики.
- `Wallet.CreditsChanged`, `HullSystem.HpChanged/Died/ShipLost/Respawned`
- `DebtService.DebtChanged/Dismissal/AccidentRegistered`
- `OrderService.TaskMomentSuccess/Fail/OrderWarning`, `WaypointChanged`
- `AudioManager.MusicStateChanged`, `LandingSystem.ShipLanded`
*Пример*: GameSession.cs:70-75 — `hull.Died += OnShipLost; hull.ShipLost += _ => OnShipLost();`
Конвенция: симметричная отписка в OnDestroy (HudBinder, MinimapBinder, WaypointIndicator).

## 4. Машина состояний + инварианты
**ShipState** (Flying/Landed/Respawning/Swallowed) — Ship.State приватный сеттер,
`SetState/SetHp` — единственные точки записи (Н12-рефакторинг §3.3):
*Пример*: Ship.cs — `SetState` (no-op на повторе), `SetHp` (кламп 0..HpMax); ShipController
читает State и морозит ввод в Respawning/Swallowed.

## 5. Фабрика (декларативное строительство)
**OnboardComputerFactory** — весь борткомп строится кодом (каркас → секции → карточки);
**WorldBuilder** — сборка мира из LevelAsset; **ShipPlacement** — позы.
*Пример*: OnboardComputerFactory.Build → BuildHeader/BuildActiveSection/BuildIncomingScroll.

## 6. Флюент-билдер (Builder)
**UiKit.UiBox** — `UiBox.Box(parent, name).Absolute(960,760).Background(UiTheme.Panel)
.Column(10).LabelBox("НАСТРОЙКИ", 42, ...).FixedHeight(56)`; координаты/слайдеры/стили —
внутри тулкита, экран — декларативная спецификация.
*Пример*: SettingsBinder.cs:52-90 — вся панель настроек ~40 строк.

## 7. Шаблонный метод (Template Method)
**ModalScreenControllerBase** — abstract: `BuildScreen()` абстрактный, каркас
(ActivateModal/Close/HandleModalKeys/OnDisable-страховка) — общий; наследники
OrderCompleteScreenController/DismissalScreenController переопределяют только хвосты.
*Пример*: ModalScreenControllerBase.cs + `OnClosed()` override в OrderComplete.

## 8. Стратегия / чистая функция
**ShipStats.Compute(config, shipConfig, upgradeLevels, upgradeConfig)** — расчёт статов
без состояния и побочных эффектов; **DeterministicHash** — FNV-1a без контекста.
*Пример*: ShipStats.cs — чистая функция, вызывается из RecomputeEff.

## 9. Хост-инъекция зависимостей (Interface Injection)
**IStationSectionHost** — секции станции (StationUpgradesSection/StationShowcaseSection)
получают Session/Ship/CanOpen/ShowToast через интерфейс, не зная о контроллере.
*Пример*: StationUpgradesSection.cs — конструктор принимает IStationSectionHost.

## 10. Пул объектов
Аудио-голоса (AudioManager.GetFreeOneShotVoice + oneShotPool), тост-вьюхи
(ToastNotifier.pool), — переиспользование вместо аллокаций.
*Пример*: AudioManager.cs — CreateVoice/GetFreeOneShotVoice.

## 11. ScriptableObject-данные + кодовые фолбэки
**LevelAsset/PhysicsConfig/PricingConfig/UpgradeConfig/ShipConfig** — баланс правится
инспектором; кодовые фолбэки (`PricingConfig.Default`, `UpgradeConfig.Default`) — билд
не падает без ассетов.
*Пример*: GameSession.cs:50-56 — цепочка ассет сцены → Resources → кодовый дефолт.

## 12. Partial-классы как секция-разделение
Крупные биндеры разделены partial-файлами по ответственности: OnboardBinder (.Cards/.Stats/
.Radio), HudBinder (+.Build), MapScreenBinder (диспетчер) + PaperMap-подсистемы.
*Пример*: OnboardBinder.Cards.cs — зона заказа целиком.

## 13. Декларативный UI-слой (самостоятельный мини-фреймворк)
**SpaceTaxi.UiKit** — отдельная asmdef без игровых зависимостей: UiTheme/Ui/UiBox +
Ui.Slider; игровые экраны — спецификации. Сборка подключена явной ссылкой в
SpaceTaxi.Gameplay.asmdef.
*Пример*: SettingsBinder.BuildPanel — панель настроек ~40 строк.

## Почему это связно
Паттерны не разбросаны: события соединяют домен с UI, фабрики строят UI декларативно,
состояние корабля централизовано, данные — в ScriptableObject-ах. Каждый паттерн решает
одну известную проблему проекта (н-р, симметричные отписки — после реального бага
Н6 «prefab Instantiate fires Awake before WorldBuilder adds the component»).
