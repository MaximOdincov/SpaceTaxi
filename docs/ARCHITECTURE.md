# ARCHITECTURE — как устроен SpaceTaxi (полная карта, Н9)

Документ для изучения: от крупных мазков к деталям. По каждой системе — что делает,
ГДЕ лежит (файл/класс) и как связана с остальными. Код смотреть по указанным путям
(построчные пояснения — ветка `feature/beta-annotated`; замечания по качеству кода —
`docs/CODE_REVIEW_N9.md`). Стек: Unity 6000.6, URP 2D, uGUI + TextMeshPro, новый
Input System, C#. Физика полёта — СВОЯ (не Rigidbody-гравитация), Unity-физика
отвечает только за коллизии и триггеры.

---

## 1. Крупные мазки

SpaceTaxi — 2D-игра про таксиста в маленькой звёздной системе: корабль летает между
планетами (своя гравитация 1/d²), подбирает пассажиров, выполняет их задания и сдаёт
заказы за кредиты. Вся игра живёт в **одной сцене** `Assets/Scenes/GameScene.unity`
(плюс `MainMenu.unity`). Почти весь UI и весь мир **строятся кодом**, а не в редакторе:
сцена содержит только «рюкзаки»-контейнеры, а контент собирается при запуске.

Три кита архитектуры:

1. **Композиционный корень** — `Assets/Scripts/Core/GameSession.cs`. Это MonoBehaviour,
   который при старте создаёт ВСЕ сервисы игры (кошелёк, флот, долги, выплаты, рейтинг),
   строит мир из данных уровня и соединяет системы подписками. Другие системы почти не
   знают друг о друге — они общаются **событиями C#**.
2. **Уровень = данные.** Вся карта (тела, пояса, сектора, дыра) лежит в ScriptableObject
   `Assets/Scripts/World/LevelAsset.cs` (ассеты `Assets/ScriptableObjects/BetaMap01.asset`
   и др.). Весь баланс физики — `Physics/PhysicsConfig.cs`, весь баланс денег —
   `Economy/PricingConfig.cs`. Код числа почти не хранит — только формулы и фолбэки.
3. **События вместо опроса.** Урон, посадка, заказы, деньги — всё это события
   (`HullSystem.Died`, `LandingSystem.ShipLanded`, `OrderService.ActiveOrderChanged`,
   `Wallet.CreditsChanged`…). UI, звук, сейв и рейтинг — просто подписчики. Поэтому
   системы можно добавлять, не трогая ядро.

Порядок зависимостей слоёв: `Core` (сборка всего) → `World/Physics/Orders/Economy/Audio`
(домены) → `UI` (только подписчики и чтение). Единственный прыжок «снизу вверх» —
`World/AsteroidBeltSystem` зовёт `UI.ToastNotifier` (замечание в ревью).

---

## 2. Как запускается один кадр игры

1. **Awake `GameSession`** (`Core/GameSession.cs:31-70`): создаёт чистые C#-сервисы
   в правильном порядке — `Wallet` (кошелёк, единственная точка списания) →
   `FleetService` (парк кораблей) → `DebtService` (долг) → `PayoutService.Bind(Wallet, Debt)`
   (выплата за заказ) → `RatingService` (рейтинг). Сервисы — не MonoBehaviour, живут
   пока жива сессия.
2. **Start `GameSession`** (`:74-130`): `WorldBuilder` (plain-класс-фабрика,
   `World/WorldBuilder.cs`) строит мир: реестр гравитации → «сухие» станции
   (`FuelNetwork.OutageStations`) → тела из LevelAsset → пояса → кометы → корабль.
   Затем вешаются `CameraFollow`, `StarfieldParallax`, `OrderService`, подписки
   на смерть/потерю борта и автосейв.
3. **FixedUpdate** (50 Гц): `ShipController` читает ввод и интегрирует скорость
   (Эйлер), `GravityField` добавляет всем телам притяжение 1/d², `BlackHole.FixedUpdate`
   добавляет воронку/закрутку/вязкость, `LandingSystem` ловит контакты (у него
   `[DefaultExecutionOrder(200)]` — он писатель скорости «последним словом»).
4. **Update/LateUpdate**: `OrderService` спавнит/гонит таймеры заказов, `BlackHole`
   сжимает пойманный корабль, UI-биндеры перерисовывают панели, `CameraFollow`
   (LateUpdate) догоняет корабль с тряской.

---

## 3. Карта систем (оглавление погружения)

| Система | Ключевые файлы | Кратко |
|---|---|---|
| Мир и уровни | `World/LevelAsset, WorldBuilder, CelestialBody, Planet, Star` | уровень как SO, фабрика мира, тела |
| Гравитация | `Physics/GravityField` | реестр тел + суперпозиция 1/d² |
| Корабль | `Physics/Ship, ShipConfig, ShipStats, ShipController, ShipModelVisual` | состояние, ввод→скорость, визуал/покраска |
| Прогноз траектории | `Physics/TrajectoryPredictor` | пунктир будущего пути |
| Посадка | `Physics/LandingSystem, ShipPlacement` | контакты, посадка, взлёт |
| Корпус/урон | `Physics/HullSystem, PhysicsConfig` | урон, жар, гибель, респавн |
| Камера | `Physics/CameraFollow` | слежение + trauma-тряска |
| Чёрная дыра | `World/BlackHole, World/WreckSystem` | воронка, захват, окно 45с, обломки |
| Пояса и кометы | `World/AsteroidBeltSystem, CometSystem` | опасные зоны, траектории комет |
| Звёздное небо | `World/StarfieldParallax` | параллакс-слои, заглушки звёзд |
| Заказы | `Orders/OrderService, PassengerTask, PassengerProfile, PassengerGenerator, PassengerCatalog, PassengerPools, NpcPortrait` | спавн карточек, состояния, задания, портреты |
| Экономика | `Economy/PricingConfig, OrderPricing, Wallet, DebtService, PayoutService, RatingService, FleetService, FuelNetwork, RepairNetwork, ShopStockService` | все деньги игры |
| Аудио | `Audio/AudioManager, SoundId, AudioCatalog, SceneMusic, ShipAudio, HeatAudio, UiSfx` | синглтон-микшер + фасад `Sound` |
| Частицы | `Physics/EngineEffects, ShipExplosion` | выхлоп, взрыв |
| UI: каркас | `UI/OnboardComputerFactory` | фабрика борткомпа + палитра проекта |
| UI: HUD | `UI/HudBinder` | скорость/бак/корпус/баннер тревоги/подсказка цели |
| UI: радар | `UI/MinimapBinder` | круговой прибор с метками |
| UI: карта | `UI/MapScreenBinder, MapStyleConfig, PaperMapArt` | бумажная карта, метки, легенда |
| UI: борткомп | `UI/OnboardBinder` | заказы, деньги, радио, часы |
| UI: станция | `UI/StationScreenController` | заправка/ремонт/улучшения/магазин |
| UI: модалки | `UI/OrderCompleteScreenController, DismissalScreenController, PauseScreenController` | выплата, увольнение, пауза |
| UI: тосты | `UI/ToastNotifier` | уведомления (1 на экране, очередь) |
| UI: мир | `UI/WaypointIndicator` | ореолы цели/метки в мире, стрелки за кадром |
| UI: меню | `UI/Menu/*` | фон меню, мигающие звёзды, декор |
| Настройки | `UI/SettingsBinder` | громкости, фуллскрин |
| Сейв | `Core/SaveSystem` | JSON-memento поверх построенного мира |
| Спрайты | `Core/StubSprites, Core/SpriteArt, UI/PaperMapArt` | процедурные заглушки, габариты арта |
| Ввод | `ShipController` + Input Action Asset | клавиатура/геймпад |
| Читы | `Core/DebugCheats` | панель выдачи денег/кораблей |

---

## 4. Мир и уровни

- **`World/LevelAsset.cs`** — ScriptableObject: список `CelestialBodyData` (позиция,
  радиус, тип, сектор, услуги), пояса, WorldRadius, стартовая планета. Ассеты:
  `Assets/ScriptableObjects/{Alpha,Beta,Big}Map01.asset`. Редактируется кастомным
  окном `Assets/Editor/LevelEditorWindow.cs` (по конвенции: новое поле LevelAsset
  обязано появиться и в редакторе).
- **`World/WorldBuilder.cs`** — фабрика мира: один проход собирает `GravityField`,
  отмечает «сухие» станции через `FuelNetwork.OutageStations` (детерминированный хэш —
  у каких планет нет топлива), создаёт тела (`Planet`/`Star`/`BlackHole` — у каждого
  свой класс-маркер на `CelestialBody`), пояса (`AsteroidBeltSystem`), кометы
  (`CometSystem`), и корабль (префаб или код-сборка — `Ship` + `ShipModelVisual` +
  `ShipController` + `HullSystem` + `LandingSystem` + аудио-компоненты).
- **`World/CelestialBody.cs`** — базовое тело: Init применяет данные + масштабирует
  коллайдер, регистрируется в `GravityField` (OnEnable/OnDisable — без утечек).
  Спрайт тела — арт владельца из `Assets/Resources/Art/planets/...` или процедурная
  заглушка (`StubSprites.StarBody/HoleCore/Circle`).
- **`Physics/GravityField.cs`** — реестр тел (40 строк): каждый FixedUpdate складывает
  `1/d²`-притяжение всех тел, кламп ускорения на тело. Дыра в общий вклад входит
  ослабленной (`BlackHole.FieldGravityScale = 0.03`) — настоящее притяжение дыры даёт
  её воронка (см. §7).

---

## 5. Корабль, физика, прогноз

- **`Physics/Ship.cs`** — состояние борта: `ShipState` (Flying/Landed/Respawning/
  Swallowed), бак (`Fuel` + событие `FuelChanged`), `Eff` — пересчитанные характеристики
  (`RecomputeEff` из `ShipConfig` + апгрейдов `FleetService`). `Eff` — единственный
  источник чисел для контроллера.
- **`Physics/ShipConfig.cs`** — SO модели корабля (VisualLength, тяга, бак, корпус,
  капсула). Ассеты — `Assets/Resources/Ships/*.asset` (delta, egg_own, egg_rental,
  barin, igla, kaban, meridian, skrepka, strela). Кодовый фолбэк «Яйца» обязан совпадать
  с `egg_rental.asset` (комментарий в коде).
- **`Physics/ShipController.cs`** — сердце полёта: читает Input System (тяга/тормоз/
  стрейф/взлёт), в `FixedUpdate` интегрирует скорость Эйлером, добавляет анти-ховер
  тормоз с «полом» по гравитации, удержание в кольце планет. Пустой бак блокирует
  двигатели (правило игры — в контроллере). Пишет `Body.linearVelocity`.
- **`Physics/ShipModelVisual.cs`** — визуал модели: спрайт корпуса из арта, покраска
  (`SetPaint`), длина корабля (`VisualLength` — единая ручка: визуал + капсула +
  посадочный габарит), «утопление» арта при посадке (LandedSinkWorld).
- **`Physics/TrajectoryPredictor.cs`** — пунктир прогноза: симулирует полёт шагами
  `1/120` на 4 секунды вперёд по тем же силам (гравитация + воронка дыры), рисует пул
  дэшей; цвет меняется у опасных траекторий.
- **`Physics/CameraFollow.cs`** — камера: экспоненциальное слежение, trauma-тряска
  (`AddShake` → trauma² + Перлин), зум посадки. `CameraFollow.AnchorPosition` — точка
  без тряски, от которой считают параллакс/звёзды.

---

## 6. Посадка и корпус

- **`Physics/LandingSystem.cs`** — `[DefaultExecutionOrder(200)]`: ловит контакты
  корабля (кэш `preSolveVelocity` — Unity портит relativeVelocity при обработке),
  различает мягкое касание и удар (`CrashDamage` по квадрату скорости → `CrashImpact`
  для экономики). При контакте — предиктивная посадка (радиальная скорость, фолбэк-
  нормаль), `FreezeAll` + корутина «дожима» в позу (`ShipPlacement`), события
  `ShipLanded/StationHint/StationServiced`. Взлёт — `Takeoff()` (по тяге или [B]/[T]).
- **`Physics/HullSystem.cs`** — корпус: `ApplyDamage`, жар звезды (`ApplyHeat` —
  эпизод-гистерезис), гибель (`Died` — взрыв `ShipExplosion.Spawn`, скрытие визуала),
  потеря борта (`ShipLost` — без взрыва: дыра/списание), респавн — идемпотентная
  корутина `RespawnAfter` на стартовой планете (масштаб, PoweredDown, скрытые
  рендереры восстанавливаются списком `hiddenVisuals`).

---

## 7. Чёрная дыра, пояса, кометы, небо

- **`World/BlackHole.cs`** — самое «математическое» место игры. Радиус тела ×3 в ассете
  (BetaMap01, тело 28), зона эффекта ×6, кольцо захвата. Воронка: притяжение растёт
  `depth²` к горизонту, закрутка орбиты, вязкий потолок падения — «исход решает сила»:
  тяга против воронки сравнивается чисто (v5.1, без гасилки). Захват запускает окно
  45с (`PhysicsConfig.BlackHoleShrinkSeconds`): корабль сжимается ease-out
  `1-(1-x)²` и спиралит внутрь; в конце — «ПОТЕРЯНА СВЯЗЬ» (`FullSwallow` →
  `HullSystem.Abandon` → респавн на стартовой). Отключение борта (X → `RequestAbandon`)
  глушит тяги (`Ship.PoweredDown`), но оператор остаётся в борту до конца окна — дыра
  держит заглушенный борт на любой дистанции. Н9.6: в момент потери у дыры остаётся
  обломок-призрак, который догорает в спирали за то же окно. Обломок виден с нового
  корабля и исчезает полностью (окно → фейд → удаление).
- **`World/WreckSystem.cs`** — обломки-призраки (кинематика без физики, максимум 3):
  два режима — СПИРАЛЬ (гибель в захвате) и ОРБИТА-УГАСАНИЕ (потеря борта Н9.6:
  радиус и масштаб идут к нулю за lifetime, потом фейд).
- **`World/AsteroidBeltSystem.cs`** — пояса из LevelAsset: ring-зона, rejection-sampling
  точек внутри полосы с симметричным отсевом планет; урон и луп при полёте в поясе.
- **`World/CometSystem.cs`** — кометы: периодический спавн по хорде через систему,
  предупреждение `CometIncoming` за 20с (тост + маркер), события для радара/карты.
- **`World/StarfieldParallax.cs`** — фоновые слои звёзд (seed-детерминизм, wrap за
  краем, ноль аллокаций в кадре), декоры. Спрайты — `StubSprites.StarSparkle/Cluster`.

---

## 8. Заказы и задания

- **`Orders/OrderService.cs`** — домен заказов (MonoBehaviour, вешается на сессию кодом):
  спавн карточек-офферов (Н9 v5.3: 2 на старте, жизнь 90с, новые каждые 20–35с —
  дефолты класса, т.к. компонент создаётся `AddComponent`), автомат
  `OrderState` (Offered→Active→Aboard→Delivered/Failed) с событием на каждый переход
  (11 событий — на них висят UI, звук, сейв, рейтинг), цена фиксируется при спавне
  (`OrderPricing`), гейт районов, таймер «паникёра». Трекинг заданий (§ ниже) в том же
  файле (`TrackTask/TrackInterestPoint`) — заметка в ревью: презентация цели
  (`UpdateTargetGlow`) просится наружу.
- **`Orders/PassengerTask.cs`** — задания пассажира: 9 видов (безопасность: NoCollision/
  NoHeat/BeltAvoid/NervousSpeed/NoRefuel; интересы: RingVisit/StarNearby/HoleFlyby/
  Deadline). Тексты и числа заданий — в одном месте (`PassengerTasks.CreateRandom/
  SuccessMomentText/FailMomentText`) — владелец правит через меня. Два паттерна:
  «условие-избегание» (эпизоды с гистерезисом, лимит = жалоба) и «точка интереса»
  (Succeeded трекингом, только `State==Aboard`). Момент свершения/срыва → тост сразу.
- **`Orders/PassengerProfile.cs`** (SO-профиль пассажира, реплики по 4 пулам), —
  `PassengerGenerator` строит имена с согласованием родов, `PassengerCatalog` —
  каталог + ленивые пулы, `Orders/NpcPortrait.cs` — процедурный портрет (4 слоя,
  детерминизм по seed — сейв восстанавливает облик; `Compose` создаёт текстуру на
  пассажира — утечка, см. ревью §3.6).
- **Бонус «мимо бездны»**: `HoleFlyby` +150Đ — `GameSession.OnOrderDelivered`
  складывает бонус с ценой (строка «Перевозка» модала выплаты).

---

## 9. Экономика

- **`Economy/PricingConfig.cs`** — «все деньги в одном SO»: цены заказов, штрафы,
  рейтинговые кривые, топливные сети, ремонт, гейты секторов, веса «жирных» заказов.
  Геттеры с клампом — ассет невозможно сломать кривыми данными.
- **`Economy/OrderPricing.cs`** — чистая формула цены заказа (27 строк, формула в
  комментарии). **`Wallet.cs`** — единственная точка списания («в минус нельзя»).
  **`DebtService.cs`** — долг/аварии/увольнение. **`PayoutService.cs`** — выплата:
  штраф → комиссия → чаевые → долг; `LastResult` для модала.
- **`Economy/FleetService.cs`** — парк: собственные/аренда, апгрейды (уровни), покраски;
  события `ShipChanged/PaintChanged/UpgradesChanged`. **`RatingService.cs`** — рейтинг
  с дельтами-константами. **`FuelNetwork.cs`** — детерминированные «сухие» станции
  (FNV-1a + соль Кнута; хаб не пустеет). **`RepairNetwork.cs`** — цена ремонта.
  **`ShopStockService.cs`** — детерминированная ротация витрины покрасок.
- Аудио денег: `Wallet.TrySpend`/`PayoutService.Pay` зовут `Sound.Play` напрямую
  (замечание ревью — было бы чище событием).

---

## 10. Аудио

- **`Audio/AudioManager.cs`** — синглтон (DontDestroyOnLoad) + статический фасад
  `Sound.Play/StartLoop/PlayMusic` (вызывается отовсюду). Внутри: пул one-shot голосов
  (12), словарь лупов (двигатель, жар, тревога, гул дыры, эмбиент), музыкальные каналы
  A/B с equal-power кроссфейдом, «станции»-плейлисты (трек по очереди, сторож тишины
  Н9.4 — канал с клипом обязан играть), даккинг при взрыве, PlayerPrefs громкости
  (квадратичная кривая). Группы: Music/Sfx/Ui/Engine (`SoundId.cs` — enum, добавлять
  только в конец!).
- **`Audio/SceneMusic.cs`** — компонент сцен: включает `MusicFlight`/`MusicMenu` +
  эмбиент кабины. **`Audio/ShipAudio.cs`** — звуковые пороги топлива/корпуса (симметричные
  отписки — образец). **`Audio/HeatAudio.cs`** — жар. **`Audio/UiSfx.cs`** — первый хук
  клика UI. Каталог клипов — `AudioCatalog.asset` (MusicStation1-4 пока без файлов).
- Идемпотентность: `StartLoop/StopLoop` можно звать каждый кадр — без флагов.

---

## 11. Частицы и эффекты

- **`Physics/EngineEffects.cs`** — процедурный выхлоп: строит ParticleSystem-слоты
  (`ExhaustMain/ExhaustRetro`) под конкретную модель (`ExhaustTune` на корабль),
  `Drive(power)` — единая точка play/stop/мощности. При смене корпуса/покраски
  `Rebind` сносит созданные слоты. Материалы создаются кодом (`new Material(shader)`)
  — утечка на Rebind, см. ревью.
- **`Physics/ShipExplosion.cs`** — разовый взрыв: вспышка, обломки-осколки, дым;
  `useUnscaledTime` и мировые оси — взрыв не «едет» с респавном. Пул не нужен
  (один выстрел).
- Тряска камеры — `CameraFollow.AddShake` (trauma-модель).

---

## 12. UI — как устроен интерфейс

Общий принцип: **весь UI построен кодом** (сцена содержит только контейнеры и
префаб настроек). Цвета и каркас борткомпа — `UI/OnboardComputerFactory` (статическая
фабрика + палитра проекта: `BrightColor`/`DangerColor`/`TaxiColor`...). Экраны общаются
через «хендшейки» (`NotifyXOpened/Closed`, `EscJustClosed`) и димминг HUD при открытых
панелях.

- **`UI/HudBinder.cs`** — боевой HUD: скорость, бак-сегменты, корпус, баннер тревоги
  (приоритеты: пустой бак > захват дыры > жар > зона дыры > мало топлива; Н9.5 —
  баннер наверху), подсказка цели. Подписки `HpChanged/FuelChanged`.
- **`UI/MinimapBinder.cs`** — радар: нелинейная проекция (ближе к краю — сжатие),
  метки тел/комет/поясов, точка статуса хаба без обводки (Н8.45), метки по событиям
  кометов.
- **`UI/MapScreenBinder.cs`** (3043 строки — самый большой файл) — бумажная карта:
  тайл-бумага от мира, тела/метки/кометы/сектора, легенда (Н9.2: один столбец),
  зум/пан/следование за ракетой, клик-выбор тела → waypoint (событие `WaypointChanged` →
  мир), граница области — сегментный круг (Н9.2), эффекты смерти корабля, стиль — SO
  `UI/MapStyleConfig.cs` (правится окном `Assets/Editor/MapStyleEditor.cs` на лету),
  карандашные спрайты — `UI/PaperMapArt.cs`.
- **`UI/OnboardComputerFactory.cs` + `UI/OnboardBinder.cs`** — борткомп ([B]): карточки
  заказов (дедуп по `OrderOffer`), деньги/долг/рейтинг с «догоняющими» анимациями,
  радио (ВОЛНА), часы. SlowUpdate-троттлинг 0.25с.
- **`UI/StationScreenController.cs`** — экран станции (заправка слайдером, ремонт,
  улучшения, магазин с витриной покрасок), арбитраж с паузой.
- **`UI/OrderCompleteScreenController.cs`** — модал выплаты (строки с прокаткой чисел,
  `Time.timeScale=0` на время модала). **`DismissalScreenController`** — увольнение за
  долги. **`PauseScreenController`** — пауза.
- **`UI/ToastNotifier.cs`** — уведомления (Н9.5): максимум ОДИН на экране (верх, сортировка
  120 — поверх всего), очередь со стаггером и дедуп-окном 10с, аварийные (emphasized)
  идут первыми, пул вьюх, тоны Neutral/Danger/Success. Подписки — в `Update`-бинде
  (заказы, посадка, долг, выплата, кометы, смерть).
- **`UI/WaypointIndicator.cs`** — маркеры в мире: жёлтый ореол цели заказа + зелёный
  ореол метки игрока (мягкие диски 1024px — Н9.1, пульс, ×1.28, за спрайтом планеты),
  скобка с подписью у метки, стрелки-компасы у края экрана с дистанцией, чеврон
  `svrn.png` (фолбэк Шеврон.png).
- **`UI/Menu/*`** — главное меню: слои параллакса (`MenuStarLayer.Config` — данные),
  мигающие звёзды по маске шрифта (`MenuTwinkle`), декор кнопок.
- **`UI/SettingsBinder.cs`** — громкости (делегаты `VolumeRow`), фуллскрин.

---

## 13. Спрайты, арт, ввод

- **`Core/StubSprites.cs`** — процедурные заглушки: Circle/Ring/SoftDot (параметр
  textureSize — Н9.1: ореолы 1024px без ступеней), Zone (жар/сияние), StarBody (Н9.3 —
  корона до нуля у края канвы), HoleCore, Sparkle/Cluster, Line/Capsule. **Важно:**
  диаметр запекается в PPU — спрайт = `diameter` юнитов при scale=1, итог = texturePPU ×
  localScale (формула: localScale = целевой_диаметр/256).
- **`Core/SpriteArt.cs`** — габариты контента по альфе (арт не заполняет канву →
  масштаб по видимому контенту, кэш). Отсюда правило «хвост альфы выше 8/255 = контент
  = вся канва» у дыры/звезды.
- **`UI/PaperMapArt.cs`** — карандашные спрайты карты (кэш по ключу), палитра чернил.
- **Ввод** — `Assets/...inputactions` (новый Input System): читает `ShipController`
  (тяга/тормоз/стрейф/взлёт), `HudBinder`/`MapScreenController` (клавиша M), пауза Esc.
  Оверлей отключения борта (X) — реле захвата дыры.
- **`Core/DebugCheats.cs`** — OnGUI-панель читов (деньги/корабли/телепорт) через
  публичные API сервисов; самоспавн `[RuntimeInitializeOnLoadMethod]` в каждой сцене.
- **`Core/SaveSystem.cs`** — сейв: плоский JSON-memento (кошелёк/долг/рейтинг/флот/
  корабль/заказ), применяется поверх дефолтно построенного мира (`ApplyTo`); guard
  «не сейвим в Respawning/Swallowed»; флаг `continuePending` — единственное глобальное
  состояние ядра. Сейв владельца руками не трогать.

---

## 14. Типовые сценарии (как системы сцепляются)

1. **Взять заказ**: спавн карточек (`OrderService` таймер) → клик карточки (`OnboardBinder`)
   → `Accept` → `OrderAccepted` → тост + проверка района + деньги не трогаются;
   `ActiveOrderChanged` → `WaypointIndicator` жёлтый ореол + радар/карта метят цель.
2. **Полёт с заданием**: `OrderService.TrackTask` каждый FixedUpdate проверяет условия
   только в `State==Aboard`; нарушение → `TaskMomentFail` → тост сразу; точка интереса
   → `TaskMomentSuccess`. Пассажир на борту = `PassengerBoarded` при посадке.
3. **Доставка**: посадка у цели (`LandingSystem.ShipLanded`) → `OrderService` финалит
   заказ → `OrderDelivered` + `Payout.Pay` (штраф→комиссия→чаевые→долг; бонус бездны)
   → модал выплаты (`OrderCompleteScreenController`, timeScale=0) → тост денег →
   `RatingService.Apply` → `Wallet.CreditsChanged` → борткомп догоняет цифры.
4. **Гибель**: удар/жар → `HullSystem.Died` → взрыв → скрытие визуала → `RespawnAfter`
   → респавн на стартовой → `Respawned` → тост; долговые аварии — `DebtService.RegisterAccident`
   → тост «ДОЛГ +…».
5. **Дыра**: вход в кольцо захвата → окно 45с (сжатие+спираль) → X глушит борт
   (`PoweredDown`, оператор остаётся) или авто-конец окна → `FullSwallow` → обломок-
   призрак догорает в дыре 45с (Н9.6) + `Abandon` → респавн на стартовой → новый борт.
6. **Сейв/лоад**: автосейв по интервалу и на ключевых событиях; продолжение —
   `SaveSystem.continuePending` → `ApplyTo` поверх свежепостроенного мира.

---

## 15. Конвенции (соблюдать при правках)

- Имена/комменты — English identifiers, комментарии по-русски коротко и по делу;
  `[SerializeField] private` + read-only свойства (без публичных полей).
- Физика — в `FixedUpdate`; подписка/отписка событий — симметрично в OnEnable/OnDisable
  или OnDestroy (образец: `OnboardBinder:102`, `ShipAudio:41`).
- Тексты игровых систем (задания) — в одном месте: `Orders/PassengerTask.cs`.
- Новое поле LevelAsset → синхронно в `Assets/Editor/LevelEditorWindow.cs` + SceneView.
- Числа баланса — в SO-ассетах (PhysicsConfig/PricingConfig); добавленное ПОСЛЕ
  создания ассета поле при первом изменении пишется в ассет (урок №13 NIGHT_LOG8).
- Сцены/.unity/.prefab руками не править — SerializedObject/eval через unity-cli.
- Детальный разбор качества кода — `docs/CODE_REVIEW_N9.md`.
