# Code review Н9 — SpaceTaxi (ночь 16/17.11.2026)

Методика: два независимых ревью-прохода (UI-слой и ядро) + сводка оркестратора.
Объём: 23 197 строк C#, 74 файла, слои Core/Physics/World/Orders/Economy/Audio/UI.
Аудитория: преподаватель со ставкой на стандартные ООП-паттерны и чистый код.

---

## 1. Вердикт

Код в хорошем рабочем состоянии: событийная архитектура вместо опроса, данные в
ScriptableObject-ассетах с кодовыми фолбэками, пулы/кэши/дедупы, именованные константы,
комментарии-история решений. **P0 (сломанное поведение, гарантированные утечки/NRE) не найдено.**
Главные системные замечания — P1: непоследовательная дисциплина подписок на события,
пара god-классов (OrderService 937, StationScreenController 1814, MapScreenBinder 3043,
AudioManager 669), дублированные константы/формулы между файлами, две нефиксированные
точки записи `linearVelocity` в FixedUpdate, мелкие утечки памяти (текстуры портретов,
материалы выхлопа). Ночью исправлены механические позиции (§6), остальное — roadmap (§7).

---

## 2. Архитектурная карта (сжатая — полная версия в docs/ARCHITECTURE.md)

- **Композиционный корень** — `Core/GameSession`: Awake создаёт чистые C#-сервисы
  (Wallet → FleetService → DebtService → PayoutService.Bind → RatingService), Start
  строит мир через `World/WorldBuilder` (фабрика: GravityField → FuelNetwork.OutageStations
  → тела из LevelAsset → CometSystem → Ship) и вешает UI/аудио/заказы.
- **Состояния** — два явных автомата: `ShipState` (Flying/Landed/Respawning/Swallowed)
  и `OrderState` (Offered/Active/Aboard/Delivered/Failed); переходы — события C#.
- **Данные** — ScriptableObject: LevelAsset, PhysicsConfig, PricingConfig, ShipConfig,
  UpgradeConfig, AudioCatalog, PassengerCatalog/Pools; тройной фолбэк «поле сцены →
  Resources → кодовый дефолт» (NRE невозможен).
- **Связь** — события: HullSystem (Died/ShipLost/Respawned/HpChanged), LandingSystem
  (ShipLanded/CrashImpact/…), OrderService (11 событий), Wallet/Debt/Rating/Fleet —
  UI, звук, сейв и рейтинг — независимые подписчики.
- **Физика** — своя суперпозиция 1/d² в `GravityField` (реестр тел, Register/Unregister
  в OnEnable/OnDisable) + интегратор Эйлера в `ShipController.FixedUpdate`; Unity-физика
  отвечает только за коллизии/триггеры (посадка — `LandingSystem`, [DefaultExecutionOrder(200)]).
- **Аудио** — синглтон `AudioManager` (DontDestroyOnLoad) + статический фасад `Sound`;
  идемпотентные StartLoop/StopLoop, пул голосов, equal-power кроссфейд, сторож тишины (Н9.4).
- **UI** — строится кодом: `OnboardComputerFactory` (фабрика каркаса + палитра) →
  биндеры (HudBinder/OnboardBinder/MinimapBinder/MapScreenBinder); тосты — `ToastNotifier`
  (пул вьюх + очередь + дедуп, 1 на экране — Н9.5); карта — `MapScreenBinder` + SO `MapStyleConfig`.
- **Сейв** — статический `SaveSystem`: плоский JSON-memento, применяется поверх
  дефолтно построенного мира.

---

## 3. Находки P1 (существенные — знать и уметь объяснить)

1. **Непоследовательная дисциплина подписок.** OnboardBinder:102 и MapScreenBinder:492 —
   образцовый `OnDestroy` с симметричной отпиской («проект уже ломался на этом»), а
   HudBinder:367, MinimapBinder:220, ToastNotifier, StationScreenController:263 —
   подписки без отписки с комментарием «стиль биндеров». Публикаторы живут столько же,
   сколько подписчики, поэтому не течёт — но два взаимоисключающих приёма в одном проекте
   объяснять тяжело. *Частично исправлено (Н9.7: HudBinder, MinimapBinder).*
2. **God-классы.** OrderService 937 (спавн предложений + ценообразование + трекинг
   заданий + таймер паникёра + подсветка цели UpdateTargetGlow:203-276 — презентация в
   доменном сервисе); StationScreenController 1814 (4 вкладки + витрина магазина);
   MapScreenBinder 3043 (подписи/деклаттеринг + кометы + эффекты смерти + зум/пан + хром);
   AudioManager 669 (пул + лупы + музыка/кроссфейд + плейлист + даккинг + настройки).
   Все разбиты по регионам и читаемы, но правка любой зоны трогает общий файл.
3. **Публичное мутируемое состояние автоматов.** `Ship.State`/`Ship.Hp` — 8 прямых записей
   из 4 файлов (SaveSystem:391, HullSystem:76/144/200, LandingSystem:166/211,
   WorldBuilder:285); инварианты («Respawning замораживает ввод») держатся на дисциплине.
   Плюс публичные поля-«тюны» OrderService:61-69, CometSystem:14-20, CelestialBody:8-11 —
   отступление от собственной конвенции `[SerializeField] private`.
4. **Нефиксированный порядок писателей скорости.** `ShipController` и `BlackHole` оба пишут
   `ship.Body.linearVelocity` в FixedUpdate; LandingSystem закреплён [DefaultExecutionOrder(200)],
   BlackHole — нет: нелинейные клампы воронки формально зависят от порядка скриптов.
   Рекомендация — закрепить `[DefaultExecutionOrder(100)]` на BlackHole (не сделано ночью:
   могло бы сместить ощущение воронки, требуется плейтест владельца).
5. **Дублированные константы/формулы.** StableHash (FuelNetwork:138 ↔ ShopStockService:39),
   формула глубины жара (HullSystem:128 ↔ HeatAudio:40), порог 3 аварии (DebtService:16 ↔
   SaveSystem:108), посадочные пороги 40/50/30 (ShipController:186-187 ↔ 292-295), фолбэк
   колец (CelestialBody:36-40 ↔ PhysicsConfig), spawn-формула (WorldBuilder:277 ↔
   CometSystem:185), фолбэк «Яйца» (ShipConfig:85-115 ↔ ассет egg_rental), `Đ` и
   FormatCredits в трёх местах. Правка одной копии рассинхронизирует другую.
6. **Медленные утечки памяти за сессию.** NpcPortrait.Compose:77-80 — Texture2D 500×500 +
   Sprite на каждый сгенерированный пассажир, не уничтожаются при протухании оффера;
   EngineEffects:386-395 и ShipExplosion:298-300 — `new Material(...)` без Destroy при
   каждом Rebind/взрыве. Рост памяти на длинной сессии, фреймрейт не задевают.
7. **Мёртвый код.** PassengerPools.Names/Surnames + PickName/PickSurname (генератор имён
   не использует пулы), OnboardBinder.MaxUnpaidAccidents, PhysicsConfig.BlackHoleDeathProximity
   и MinSpeed (честно помечены «для схемы ассета/tools»), ShipAudio.SetInput (пустой),
   RepairNetwork.MinShipTier/EffectiveFraction (параметры-заглушки), LevelAsset.SpawnMode.Free.
   Держать осознанно (совместимость сейвов/ассетов) или убрать — решение владельца.

---

## 4. Находки P2 (кратко — косметика и микро-оптимизации)

- Аллокации в кадре: HudBinder.UpdateGoalHint:422-460 (строки + ColorUtility каждый кадр —
  паттерн кэша строк уже есть в WaypointIndicator:66-70), OnboardBinder.SlowUpdate:629-640
  (`transform.Find` по строкам на карточку каждые 0.25с), `((int)speed).ToString()`.
- `GetComponent`/`Find` в горячих путях: LandingSystem:116-119 (4×GetComponentInParent на
  контакт), HullSystem:210 (FindObjectsByType<Planet> на респавн), CometSystem/AsteroidBeltSystem
  (ленивые поиски до первого нахождения), GameSession:184.
- Дубли каркаса: Bootstrap/OnSceneLoaded/Spawn в 4 модалках, EscJustClosed+LateUpdate в 3
  классах, CanvasScaler-блок в 3 классах, три строителя кнопок в StationScreenController —
  просится базовый `ModalScreenControllerBase` (−150 строк).
- Утилиты не по адресу: OnboardBinder.FormatCredits/FormatClock (используют 5 классов) —
  место в статическом хелпере; PickPhrase в PassengerProfile; Currency «Đ» ×3.
- Строковые контракты имён узлов («ModelVisual», «Compact/Info/Dist», пути фабрики) —
  ломаются молча при переименовании.
- Debug.Log в билде: ShipController:205-212, LandingSystem:313/335 — *заглушены ночью (Н9.7)*.
- MapStyleConfig/CometSystem/OrderService: публичные поля SO-конфигов — разнобой с
  конвенцией `[SerializeField] private`.
- ShipModelVisual:113-118 меняет коллайдер префаба на лету (Circle→Capsule) — работает,
  но на защите спросят «почему не в префабе» (ответ: капсула зависит от VisualLength модели).

---

## 5. Сильные стороны (что препод похвалит)

1. **События вместо опроса** — полный событийный контур заказов/экономики/корпуса; UI,
   звук, сейв, рейтинг — независимые подписчики (GameSession:105-116 это прямо документирует).
2. **ScriptableObject-данные + кодовые фолбэки** — баланс правится инспектором, билд не
   падает без ассетов (PricingConfig.Default / ShipConfig.EggRentalFallback / UpgradeConfig.Default).
3. **Реестр-наблюдатель GravityField** — Register/Unregister в OnEnable/OnDisable, ни одной
   утечки в физике, ноль Find-ов.
4. **Пулы, кэши, дедупы** — голоса аудио, дэши прогноза, SpriteArt-габариты,trimmedCache
   портретов, кэш строк дистанций, пул вьюх тостов с очередью/дедуп-окном.
5. **Идемпотентность и guard-флаги** — StartLoop каждый кадр, touchHandled на шаг физики,
   while-догон спавна после паузы, счётчик кроссфейдов с try/finally, детерминированные
   FNV-1a-хэши «сухих» станций и витрины.
6. **Культура комментариев-истории** («Н6 U4», «22_BALANCE_REWORK §1.5», «владелец: …») —
   видно, почему код такой; редкая дисциплина для учебного проекта.

---

## 6. Исправлено этой ночью (Н9.7 — безопасные механические правки, поведение не менялось)

- `HudBinder`: null-guard'ы в Awake + `OnDestroy` с симметричной отпиской HpChanged/FuelChanged.
- `MinimapBinder`: `OnDestroy` с отпиской CometIncoming/CometSpawned.
- `ShipController`: убрано двойное деление velocity (UpdateHeading), Debug.Log порогов
  топлива обёрнуты в `#if UNITY_EDITOR`.
- `LandingSystem`: Debug.Log посадок/ударов обёрнуты в `#if UNITY_EDITOR`.
- `AudioManager`: массив групп закэширован в DuckRoutine (без Enum.GetValues в цикле).
- Compile EXIT 0 после правок; живых замеров не требовалось (поведенческих изменений нет).

---

## 7. Roadmap (что чинить следующим — по убыванию выгоды)

1. Закрепить `[DefaultExecutionOrder(100)]` на BlackHole + плейтест воронки (15 мин).
2. Утечка портретов: PassengerProfile хранит ссылку на текстуру и Destroy-ит её в OnDestroy
   (runtime-инстансы only); материалы EngineEffects — кэш одного материала на слот (1-2 ч).
3. Вынести `FormatCredits/FormatClock/Currency` в статический `UiFormat` (30 мин, −3 дубля).
4. Общий `StableHash`/формула жара/порог 3 аварии — одна копия (30 мин).
5. Вырезать `UpdateTargetGlow` из OrderService в отдельный `TargetGlowPresenter` (1 ч).
6. Базовый класс для модалок (Bootstrap/Esc/timeScale) — минус ~150 строк (1-2 ч).
7. Инкапсулировать `Ship.State` в Set-метод с инвариантами (полдня, аккуратно).
8. Мёртвый код (§3.7) — решить держать/убирать, пометить атрибутом [Obsolete] с комментарием.
