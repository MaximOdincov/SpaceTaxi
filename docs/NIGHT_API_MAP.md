# NIGHT_API_MAP — Space Taxi (beta night)

Unity 6000.2 URP, asmdef `SpaceTaxi.Gameplay` (refs: Unity.InputSystem, Unity.TextMeshPro), ns `SpaceTaxi.*`. Мир, корабль и весь UI строятся кодом в рантайме из LevelAsset; в сценах только сессия, камера и Canvas-каркасы.

## Core/GameSession.cs
- GameSession : MonoBehaviour — корень сессии: билд мира, кошелёк, заказы.
- Поля-биндинги: `[SerializeField] LevelAsset level; PhysicsConfig physicsConfig; ShipConfig currentShip; InputActionAsset input; PassengerCatalog passengerCatalog; Planet planetPrefab; Ship shipPrefab`.
- Свойства: `Ship`, `Orders` (OrderService), `Wallet`, `Level`, `Config`, `CurrentShip` (currentShip==null → `ShipConfig.EggRentalFallback`).
- Awake: `Wallet = new Wallet()`. Start: `new WorldBuilder(level, physicsConfig, CurrentShip, input, planetPrefab, shipPrefab).Build(out Ship ship, out _)`; на Camera.main навешивает `CameraFollow` + `SetTarget(Ship)` и `StarfieldParallax` (если нет); `Orders = gameObject.AddComponent<OrderService>(); Orders.Init(level, passengerCatalog, Ship)`.
- Неочевидное: кошелёк в Awake, мир в Start; UI-биндеры лениво ждут сессию в Update (пока `session.Orders == null`/`ship.Config == null`). Сейвов нет, Continue == New Game.

## Core/StubSprites.cs
- static StubSprites — процедурные спрайты-заглушки (кэш в статике).
- API: `Sprite Circle(float diameter)` (кэш по округлённому диаметру, PPU = 64/diameter, белый с тёмной окантовкой 8%); `Sprite ShipTriangle()` (192px = 14 юнитов); `Sprite Line()` (256px, капсула = 16 юнитов); `Texture2D CreateCircleTexture(int size, float outlineWidth)`; `Texture2D CreateMaskTexture(int size, Func<float,float,bool> inside)`; `bool InTriangle(x,y)`; `bool InCapsule(x,y)`.

## Physics/Ship.cs
- Ship : MonoBehaviour (RequireComponent Rigidbody2D) — модель корабля: состояние, HP, бак.
- Поля: `public ShipState State` (Flying/Landed/Respawning), `public float Hp`; свойства `Config`, `ShipConfig`, `Body` (Rigidbody2D), `LandedOn` (Planet, set — WorldBuilder/посадка/респавн).
- `float Fuel { get; set; }` — клампит 0..FuelMax, при изменении `event Action FuelChanged`.
- `void Init(PhysicsConfig config, ShipConfig shipConfig)` — gravityScale=0 (гравитацию считает GravityField), Hp/Fuel из конфига. `HpFraction`, `FuelFraction` — для HUD.

## Physics/ShipController.cs
- ShipController : MonoBehaviour — управление в полёте, тяга/тормоз/стрейф, нос по скорости.
- Поля: `[SerializeField] InputActionAsset input`, `float spriteNoseAngle` (0=вправо, 90=вверх).
- Свойства: `SpriteNoseAngle`, `LandingRetro` («липкая» ретро-ориентация), `ThrustOn`, `BrakeOn`, `StrafeLevel`, `InLandingRing`, `EnginePower` (1 или EngineMultiplier).
- `void SetInput(InputActionAsset asset)` — мапа "Ship", actions Thrust/Brake/Strafe.
- FixedUpdate: гравитация GravityField.GetAccelerationAt, «качель» (GravityBoost на входе в CaptureRing при v > escape), EngineMultiplier в LandingRing, расход `FuelDrainPerSecond` при любом двигателе, пустой бак = все входы off (лог порогов 10%/0 по одному разу). Нос — UpdateHeading (по скорости / ретро / от планеты на финальной прямой < Radius+40 и v<50). Update: взлёт — Thrust был нажат в кадре при State==Landed и Fuel>0 → `landing.Takeoff()`.

## Physics/LandingSystem.cs
- LandingSystem : MonoBehaviour — мягкое касание = посадка, жёсткое = урон, звезда = гибель.
- События: `event Action<Planet> ShipLanded`; `event Action<string> StationServiced` (имя станции, если чинила/заправляла).
- `void Takeoff()` — импульс `dir * config.TakeoffV` от центра планеты, AddShake(0.25).
- OnCollisionEnter2D: Star → `Hull.ApplyDamage(ship.Hp)` (смерть); Planet: v < config.LandSpeed → Land (снап на поверхность normal*(Radius+7), v=0, нос от центра, State=Landed, ShipLanded; HasStation → RepairAll + Fuel=FuelMax + StationServiced) иначе Crash (damage = max(5, (v-LandSpeed)*0.9), AddShake, при Hp>0 — Land).
- Неочевидное: `Hull` берётся лениво (HullSystem может быть добавлен позже Awake WorldBuilder); Takeoff дергается ShipController.Update.

## Physics/HullSystem.cs
- HullSystem : MonoBehaviour — прочность, жар звёзд, гибель/респавн, граница мира.
- События: `event Action HpChanged`, `event Action Died`, `event Action<string> ShipLost`, `event Action Respawned`.
- `void Init(LevelAsset levelAsset)`; `void ApplyDamage(float amount)` (Hp<=0 → Die); `void RepairAll()` (через HpChanged, иначе HUD не обновится); `void Abandon(string reason)` (потеря без взрыва).
- Update: за `level.WorldRadius` (дефолт 2500) → Abandon("ПОТЕРЯНА СВЯЗЬ"). FixedUpdate: ApplyHeat — линейный урон от Star.HeatRadius/HeatDamagePerSecond. Die/Abandon → пауза 0.6с → респавн на стартовой планете (позиция r+7, нос по SpawnAngleDeg, Hp/Fuel max, State=Landed, `LandedOn` ищется по совпадению planet.name == DisplayName — хрупко при дублях имён).

## Physics/GravityField.cs
- GravityField : MonoBehaviour — реестр тел и суммарное ускорение.
- `IReadOnlyList<CelestialBody> Bodies`; `void Register(CelestialBody)`, `void Unregister(CelestialBody)` (сами тела из OnEnable/OnDisable).
- `Vector2 GetAccelerationAt(Vector2 position, PhysicsConfig config)` — a = min(GravParamOf/d², GravityAccelClamp) по каждому телу.

## Physics/CameraFollow.cs
- CameraFollow : MonoBehaviour — слежение с опережением, зум по скорости, trauma-тряска.
- `static CameraFollow Instance`; `static void AddShake(float power)` (0.25 взлёт, 0.5 удар, 0.85 гибель); `void SetTarget(Ship target)`; `Vector3 AnchorPosition { get; }` — позиция без тряски (якорь параллакса).
- Константы: FlightViewHeight 320, FastViewHeight 470, LandingViewHeight 190, Lookahead 0.45с/130.
- Вешается рантаймом из GameSession.Start на Camera.main.

## Physics/TrajectoryPredictor.cs
- TrajectoryPredictor : MonoBehaviour — пунктирный прогноз полёта (симуляция step=1/120).
- Поля-биндинги: `[SerializeField] Sprite dashSprite; Color dashColor(0.6a); float dashScale=0.4; float dashWidth=0.6; float spacing=12; float endFade=0.6; float noseOffset=6; float step=1/120; int maxDashes=64`.
- Неочевидное: без dashSprite — `StubSprites.Line()`; адаптивный горизонт (1.2 экрана, 2..4 с); скрыт в ретро, при v<30, вне полёта; зелёный (LineSafe) если касание планеты на скорости < LandSpeed; сглаживание направления (smoothDir) — линия не дёргается от доворотов.

## Physics/EngineEffects.cs
- EngineEffects : MonoBehaviour — частицы выхлопа (создаёт 4 ParticleSystem: MainJet/RetroJet/StrafeTop/StrafeBottom).
- Материал из шейдера SpriteRenderer корабля + `StubSprites.Circle(8).texture`; цвета тёплый/холодный.
- Update: эмит только MainJet по `controller.ThrustOn`; RetroJet/StrafeTop/StrafeBottom выключены закомментированными строками (временно); поворот ретро-сопла при LandingRetro; ApplyPower — множитель эмиссии/размера от EnginePower.

## Physics/SpeedTrail.cs
- SpeedTrail : MonoBehaviour — TrailRenderer-шлейф (MinSpeed 65, TrailTime 0.38).
- Материал: StubSprites.Circle(8).texture, тайлится; телепорт (смена состояния на не-Flying) → trail.Clear.

## Physics/PhysicsConfig.cs — ScriptableObject
- CreateAssetMenu "Space Taxi/Physics Config"; все поля public:
- `float Retro = 80` (торможение), `float StrafeMaxTurnRate = 60` (град/с), `float LandSpeed = 55` (безопасное касание), `float TakeoffV = 85`, `float EngineMultiplier = 3.2`, `float LandingRingOffset = 90`, `float FuelDrainPerSecond = 5.5`, `float GravityParamK = 52000` (g = K*r*масса), `float GravityAccelClamp = 240`, `float CaptureRingK = 3.3`, `float CaptureRingBase = 30`, `float HeadingTurnRate = 4`, `float HeadingMaxTurnSpeed = 260`, `float MinControlSpeed = 1`, `float GravityBoost = 1.15`.
- Методы: `float GravParamOf(CelestialBody)` = K*r*m; `float OrbitRingOf(CelestialBody)` = r*K+Base; `static float CircularVelocity(g,d)`, `static float EscapeVelocity(g,d)`.
- Значения в PhysicsConfig_Default.asset совпадают с кодом (ретро снижено с 115, LandSpeed поднята с 38 — по комментариям).

## World/LevelAsset.cs — ScriptableObject
- LevelAsset: `string LevelName`, `float WorldRadius = 2500`, `List<CelestialBodyData> Bodies`, `SpawnMode SpawnMode` (Landed/Free — Free не используется), `int StartPlanetIndex = 0` (планируется и точка респавна), `float SpawnAngleDeg = 90` (0=восток, 90=север), `Vector2 SpawnPoint`, `float SpawnHeading` (только при Free).
- CelestialBodyData [Serializable]: `string DisplayName`, `BodyType Type` (Planet/Star/BlackHole/AsteroidBelt/Comet — реализованы только Planet/Star), `Vector2 Position`, `float Radius = 50` [Min 5], `float MassMultiplier = 1` (звезда 4.5), `float HeatRadius = 0` (звезда ~3.6r), `BodyVisuals Visuals`, `bool HasStation`, `float LandingRingRadius` (0 = r+90), `float CaptureRingRadius` (0 = r*3.3+30).
- BodyVisuals [Serializable]: `Sprite Sprite`, `Material Material`, `bool UseColor`, `Color Color = white`, `float SpinSpeed` (град/с).

## World/CelestialBody.cs
- CelestialBody : MonoBehaviour (RequireComponent CircleCollider2D) — база тел системы.
- Поля: `string DisplayName`, `BodyType Type`, `float Radius`, `float Mass`; свойство `bool HasStation { get; private set; }`; protected `PhysicsConfig config`.
- Свойства: `float LandingRing` (override или Radius+LandingRingOffset), `float CaptureRing` (override или Radius*K+Base).
- `virtual void Init(CelestialBodyData data, PhysicsConfig physicsConfig)` — позиция, масштаб спрайта под Radius, collider.radius = Radius/scale. OnEnable регистрируется в GravityField, Update крутит спрайт по SpinSpeed.

## World/Planet.cs — пустой наследник CelestialBody (маркер-класс).
## World/Star.cs
- Star : CelestialBody — поля `float HeatRadius = 180`, `float HeatDamagePerSecond = 30`; Init читает data.HeatRadius; `bool IsInHeatZone(Vector2 point)`.

## World/WorldBuilder.cs
- sealed WorldBuilder — чистый C#-билдер мира (не MonoBehaviour).
- ctor: `WorldBuilder(LevelAsset level, PhysicsConfig config, ShipConfig shipConfig, InputActionAsset input, Planet planetPrefab = null, Ship shipPrefab = null)`.
- `void Build(out Ship ship, out GravityField gravity)`: первым создаёт GO "GravityField", затем тела по level.Bodies (планеты — инстанс planetPrefab с именем DisplayName, звёзды/без префаба — новый GO со StubSprites.Circle(Radius*2) и компонентом Star; Planet добавляется при отсутствии), у звезды с HeatRadius>0 — ребёнок "HeatZone" (спрайт-ореол, sortingOrder -1). ShipOffset = 7.
- Корабль: shipPrefab или кодом (GO неактивен: SpriteRenderer sortingOrder 2, CircleCollider2D r=4, Ship, ShipController, ShipAudio, LandingSystem, HeatAudio, TrajectoryPredictor; rb Continuous+Interpolate), спрайт-заглушка StubSprites.ShipTriangle() при пустом; спавн `start.Position + dir*(Radius+7)`, ротация SpawnAngleDeg - SpriteNoseAngle; `ship.Init(config, shipConfig)`; `ship.State = Landed`, `LandedOn = стартовая планета`; `controller.SetInput(input)`, `shipAudio.SetInput(input)`, `hull.Init(level)` (добавляется при отсутствии), EngineEffects и SpeedTrail добавляются при отсутствии, затем `go.SetActive(true)`.

## World/StarfieldParallax.cs
- StarfieldParallax : MonoBehaviour — 3 слоя звёзд-заглушек (StubSprites.Circle(6)), вешается рантаймом на камеру.
- [DefaultExecutionOrder(50)] — LateUpdate после CameraFollow; дрейф = AnchorPosition * Factor (0.12/0.35/0.65), тайлы ~1400x950, якорь z=0 (иначе слои в near-плоскости).

## Orders/OrderService.cs
- OrderService : MonoBehaviour — поток карточек входящих заказов, один активный; посадка = единственная точка взятия/сдачи.
- Тюнинг (public float/int): `offerLifetime = 60`, `spawnMin = 15`, `spawnMax = 30`, `maxOffers = 4`, `initialOffers = 2`.
- Данные: enum OrderState { Offered, Active, Aboard, Delivered, Failed }; class OrderOffer { PassengerProfile Passenger; int PickupIndex; int DropoffIndex; float ExpiresAt; OrderState State }.
- Свойства: `OrderOffer Active`, `IReadOnlyList<OrderOffer> Offers`, `int Deliveries`, `int Failures`.
- События: `event Action OffersChanged`, `event Action ActiveOrderChanged`, `event Action<OrderOffer> PassengerBoarded`, `OrderDelivered`, `OrderFailed`, `OrderCancelled`.
- API: `void Init(LevelAsset, PassengerCatalog, Ship)` (подписки: LandingSystem.ShipLanded → OnLanded; HullSystem.Died/ShipLost → FailActive; стартовые офферы); `bool Accept(OrderOffer)`, `bool Decline(OrderOffer)`, `bool CancelActive()` (с Aboard — только когда State==Landed); `string PlanetName(int index)`; `float RouteDistance(OrderOffer)`; `float DistanceToPlanet(int index, Vector2 from)`.
- Update: протухшие карточки удаляются (OffersChanged); догоняющий спавн по таймеру (while, пауза-безопасно); UpdateTargetGlow — ребёнок "TargetGlow" с копией спрайта планеты-цели (PickupIndex или DropoffIndex при Aboard), пульс 1.32+0.08sin(3t), цвет (1, 0.83, 0.3, 0.16), sortingOrder -2.
- Цикл: спавн AddOffer (PassengerCatalog.Pick, случайные планеты Type==Planet, pickup != dropoff) → Accept → посадка на pickup → Aboard (PassengerBoarded) → посадка на dropoff → Delivered (OrderDelivered) / смерть или потеря корабля → Failed (OrderFailed).
- Неочевидное: оплату за доставку никто не начисляет (Wallet.Add звать некому) — экономика до П4 не подключена.

## Orders/PassengerProfile.cs — ScriptableObject
- CreateAssetMenu "Space Taxi/Passenger Profile"; поля: `string DisplayName = "Гость системы"`, `string Archetype = "Турист"`, `string Description` [TextArea], `Sprite Portrait` (пусто — UI покажет цветную заглушку), `Color PortraitTint = white`, `string[] Greetings`, `string[] Wishes`, `string[] Thanks`, `string[] Complaints` (показывается случайная фраза).
- `static string PickPhrase(string[])` — null, если пусто.

## Orders/PassengerCatalog.cs — ScriptableObject
- `List<PassengerProfile> Profiles`; `PassengerProfile Pick()` — случайный (null, если пусто).

## Orders/PassengerGenerator.cs
- static PassengerGenerator.Generate() + class Passenger { FullName, Archetype } — легаси, OrderService не использует (берёт из каталога).

## Economy/Wallet.cs
- sealed Wallet (не Unity-объект). `int Credits { get; }`; `event Action<int> CreditsChanged`; `void Add(int amount)` (только в плюс); `bool TrySpend(int amount)`.

## Economy/ShipConfig.cs — ScriptableObject
- Поля: `string Id`, `string DisplayName`, `int Tier = 2` [1..5], `float Thrust`, `float StrafeAcc`, `float MaxV`, `float FuelMax`, `float HpMax`, `int Price` (0 = не продаётся), `bool IsRental`, `string Description`.
- `static ShipConfig EggRentalFallback` — ленивый фолбэк "Яйцо": Thrust 95, StrafeAcc 85, MaxV 330, FuelMax 100, HpMax 100, Price 0, IsRental true.

## Audio/AudioManager.cs
- AudioManager : MonoBehaviour (DisallowMultipleComponent) — singleton `Instance`, DontDestroyOnLoad; поле `[SerializeField] AudioCatalog catalog`.
- One-shot: `void PlayOneShot(SoundId id, float intensity = 1)` — пул до 12 голосов, Explosion дакает остальные группы (0.2, 1.2 с).
- Лупы: `SetLoopActive(id, bool)`, `StartLoop(id)`, `StopLoop(id)`, `SetLoopIntensity(id, float 0..1)` (громкость 0.4..1, питч 0.9..1.12), `SetLoopPan(id, -1..1)`; фейд 0.15 с.
- Музыка: `PlayMusic(SoundId)`, `StopMusic()` — кроссфейд 1.5 с через два канала musicA/musicB.
- Громкости: `SetGroupVolume(AudioGroup, float 0..1)`, `GetGroupVolume`, `SetMasterVolume` (квадратичная кривая), `GetMasterVolume`; хранение PlayerPrefs "audio.vol.{Group|Master}".
- `static class Sound` — фасад: `Sound.Play(id, intensity)`, `Sound.SetLoop(id, active)`, `Sound.StartLoop/StopLoop/SetLoopIntensity/SetLoopPan`, `Sound.PlayMusic(id)`, `Sound.StopMusic()` (без менеджера — тихо).

## Audio/AudioCatalog.cs — ScriptableObject
- `[SerializeField] SoundEntry[] entries`; `SoundEntry Get(SoundId id)` (ленивый словарь).
- SoundEntry [Serializable]: `SoundId Id`, `AudioClip[] Clips`, `AudioGroup Group = Sfx`, `float Volume = 1`, `float Pitch = 1`, `float PitchJitter = 0.04`, `bool Loop`, `bool Sequential` (вариации по силе, иначе рандом), `bool IsValid`.

## Audio/SoundId.cs
- enum SoundId: None, EngineMain, EngineRetro, EngineStrafe, HeatZone (лупы); Collision (3 вариации), LandingTouch, Takeoff, Explosion, Respawn; UiClick, ComputerPing; AmbientSpace; MusicMenu, MusicFlight.
- enum AudioGroup: Engine, Sfx, Ui, Music.

## Audio/ShipAudio.cs
- ShipAudio : MonoBehaviour — поле `[SerializeField] InputActionAsset input`, `SetInput(InputActionAsset)`. Заглушка: каждый Update `Sound.SetLoop(SoundId.EngineMain, false)` — звуки двигателя отключены сознательно, осталась музыка.

## Audio/SceneMusic.cs
- SceneMusic : MonoBehaviour — поля `[SerializeField] SoundId music = MusicFlight`, `bool ambient = true`; Start: `Sound.PlayMusic(music)`. Поле ambient не читается (эмбиент отключён).

## Audio/HeatAudio.cs
- HeatAudio : MonoBehaviour — Update пустой (звук жара отключён), компонент ставится WorldBuilder/AudioSetupTool.

## UI/HudBinder.cs
- HudBinder : MonoBehaviour — HUD скорости/корпуса/топлива/тревоги.
- Ожидает детей по именам: `SpeedValue` (TMP), `HullValue` (TMP), `FuelTrack/FuelSeg_01..FuelSeg_10` (Image x10), `WarningBanner` c ребёнком `Text`; `[SerializeField] TMP_FontAsset font` (JetBrains Mono, назначен в сцене).
- Программно создаёт `GoalHint` (TMP, под тостом) — строка текущей цели: "ЗАКАЗ · забрать: …" / "ПАССАЖИР НА БОРТУ · доставить: … (N юн)".
- Ленивый бинд в Update: FindAnyObjectByType<Ship>/GravityField; подписки hull.HpChanged → UpdateHull, ship.FuelChanged → UpdateFuel. Скорость краснеет в посадочном кольце при v >= LandSpeed. Баннер по приоритету: пустой бак > жар (Star.IsInHeatZone) > <10% топлива.

## UI/MinimapBinder.cs
- MinimapBinder : MonoBehaviour — радар, корабль в центре.
- Поля: `public float worldRange = 350` (юниты мира), `[SerializeField] Sprite circleSprite` (planet_stub.png).
- Ожидает детей `Bodies` (статичная заготовка стирается) и `PlayerArrow`. Строится лениво, когда есть GravityField.Bodies и rect.width >= 50; у звезды ореол+ядро; стрелка по вектору скорости.

## UI/MapScreenController.cs
- MapScreenController : MonoBehaviour — клавиша M / Esc для карты.
- Поля: `[SerializeField] GameObject mapScreen; OnboardComputerPanel onboard; PauseScreenController pause`.
- API: `bool IsOpened`, `bool EscJustClosed` (сбрасывается в LateUpdate), `void Open()`. Открывает/закрывает карту вместе с борткомпом (`onboard.Show(opened)`), уведомляет pause.NotifyMapOpened/Closed.

## UI/MapScreenBinder.cs
- MapScreenBinder : MonoBehaviour — карта системы, строится кодом из LevelAsset.
- Поле: `[SerializeField] Sprite circleSprite`. Ожидает детей `Frame`, `Background`, `Title` (font берётся из Title), остальные дети стираются.
- Лениво: ждёт GameSession.Level и rect.width >= 100; вписывает bounds тел в панель, планеты читаемо крупнее масштаба (x1.9, звёзды x1.6), у станций жёлтая точка, подписи DisplayName.ToUpper; `PlayerMarker` — живая позиция корабля; TargetGlow пульсирует по активному заказу (unscaledTime). `CreateRing` — заглушка «ореол+точка» (кольца с арт-паком).

## UI/OnboardComputerPanel.cs
- OnboardComputerPanel : MonoBehaviour — выезд панели борткомпа (TAB).
- Поля: `[SerializeField] RectTransform panel; float hiddenX = -510; float shownX = 0; float duration = 0.25`.
- API: `void Toggle()`, `void Show(bool value)` (программно из карты/паузы), `bool IsShown`. Анимация Slide на unscaledDeltaTime (работает в паузе).

## UI/OnboardComputerFactory.cs
- static OnboardComputerFactory — каркас борткомпа и карточки заказов кодом (для редактора сериализуется в сцену, в рантайме — фолбэк).
- `OnboardRefs Build(Transform panel, bool destroyImmediate = false)` — класс OnboardRefs: TMP_Text Clock, ShipState1..3, SysState2/3, EconCounters, OrderPay, MoneyValue, RatingValue; Button AbandonButton; RectTransform OrderArea; TMP_FontAsset Font.
- `GameObject BuildOfferCard()` — узлы Avatar / Info/Name, Info/Route, Info/Wish / Buttons/Accept, Buttons/Decline; `GameObject BuildActiveOrderBlock()` — Photo / Info/Name, Info/Route, Info/OrderStatus, Info/Wish.
- Хелперы: `H`, `Text`, `Button`, `FixedSize/FixedHeight/FixedWidth/Flexible/Stretch`, `SetFont(TMP_FontAsset)`; цвета LabelColor #5BC98F, BrightColor #A8FFCB, TaxiColor #FFD44D, PanelColor, DangerColor.
- Неочевидное: LoadSprite через AssetDatabase только под UNITY_EDITOR (в билде иконки каркаса null — реальные иконки биндятся в OnboardBinder).

## UI/OnboardBinder.cs
- OnboardBinder : MonoBehaviour — живые данные борткомпа (живёт в префабе OnboardComputer).
- Поля: `[SerializeField] Sprite cashIcon; Sprite ratingIcon; TMP_FontAsset panelFont; GameObject offerCardPrefab; GameObject activeOrderPrefab`.
- Ожидает дерево (пути): `Stack/Header/TitleRow/Clock`, `Stack/StateSection/ShipState1..3`, `SysState2/3`, `AbandonButton`, `Stack/OrderSection/OrderArea`, `OrderPay`, `Stack/EconSection/EconRow/EconCounters`, `Stack/Header/StatsRow/MoneyValue/Value`, `RatingValue/Value`; нет `Stack` — каркас строится фабрикой (фолбэк старых сцен).
- Бинд (лениво в Update): session.Orders, ship, gravity, hull, wallet; подписки `wallet.CreditsChanged → ShowCredits`, `orders.OffersChanged/ActiveOrderChanged → RefreshOrder`; AbandonButton → `hull.Abandon("ОТКЛЮЧЁН ОПЕРАТОРОМ")`; SlowUpdate каждые 0.25 с (часы 14:02+t/3, корпус/топливо/статус, ближайший объект, жаровая зона, счётчики, видимость кнопок).
- Карточки: офферы — map offer→card с FadeIn/FadeOut (CanvasGroup, unscaled), кнопки Accept/Decline → `orders.Accept/Decline(offer)`; активный заказ — блок + кнопка «ОТКАЗАТЬСЯ ОТ ЗАКАЗА» → `orders.CancelActive()`.
- Временное (до П4): кнопка «СМЕНА +100 КР» (EnsureShiftButton, `wallet.Add(DemoShiftReward=100)`) — единственный источник кредитов; WidenMoneyValue расширяет поле денег до 110.

## UI/PauseScreenController.cs
- PauseScreenController : MonoBehaviour — пауза по ESC: `Time.timeScale = 0/1` + экран.
- Поля: `[SerializeField] GameObject pauseScreen; OnboardComputerPanel onboard; MapScreenController mapController; GameObject settingsUi`.
- API: `bool IsPaused`, `void Resume()`, `void OpenMapFromPause()`, `void OpenSettingsFromPause()` (настройки поверх, пауза остаётся), `void ReturnFromSettings()`, `void NotifyMapOpened/Closed()` (ESC уступает карте: не тогглится при IsOpened/EscJustClosed).

## UI/SettingsBinder.cs
- SettingsBinder : MonoBehaviour — слайдеры громкости, применяются сразу, хранит AudioManager.
- Ожидает `Panel/SliderMaster|SliderMusic|SliderSfx|SliderUi` + одноимённые `*Value` (TMP). Живёт в префабе SettingsUI.prefab (в корневых сценах компонента нет).

## UI/SettingsScreenController.cs
- SettingsScreenController : MonoBehaviour — `void Open()`, `void Close()` (SetActive). На корне SettingsUI.prefab.

## UI/SceneFlowButtons.cs
- SceneFlowButtons : MonoBehaviour — кнопки флоу: `StartNewGame()`/`ContinueGame()` → сцена "GameScene", `LoadMainMenu()` → "MainMenu", `QuitGame()`; сбрасывает Time.timeScale = 1.

## UI/ToastNotifier.cs
- ToastNotifier : MonoBehaviour — тосты событий (очередь, один за раз, unscaledTime).
- Поле: `public float duration = 3.5`. API: `void Show(string message, bool emphasized = false)` (важные — крупнее/шире, 5 с).
- Сам себя оформляет в Awake (фон+TMP). Автобинд: `orders.PassengerBoarded/OrderDelivered/OrderFailed` (с фразами пассажира), `landing.StationServiced`, `hull.Respawned`, `hull.ShipLost`.

## Editor/LevelEditorWindow.cs
- LevelEditorWindow : EditorWindow, меню "Space Taxi/Level Editor" — правка LevelAsset: список тел (+Планета/+Звезда/Дублировать/Удалить/Копировать-Вставить через EditorJsonUtility), поля тела/колец/визуала, спавн (popup планет), валидация (`static List<string> Validate(LevelAsset)`: пересечения, <260 центры, жар <550 от спавна, радиусы планет 15–40 и звёзд 46–64), стрелки двигают тело (snap 10, Shift 10x).
- `static Vector2 SpawnPointOf(LevelAsset)` — как в игре (r+7); свойства Level/SelectedIndex/SnapEnabled/ShowGrid/ShowTrajectory/ShowGravityField, `SetSelected(int)`.

## Editor/LevelEditorSceneView.cs
- static LevelEditorSceneView [InitializeOnLoad] — гизмо: тела с превью спрайта, кольца (посадочное зелёное / качели), жар, спавн-маркер, рамка камеры 320 (16:9), сетка 100, дистанции от выбранного (<300 красным), drag тел с snap и Undo, зонд гравитации по Ctrl, прогноз траектории после взлёта, векторное поле (шаг 150). PhysicsConfig ищется FindAssets с кэшем 5 с.

## Editor/OnboardComputerBuilder.cs
- static OnboardComputerBuilder — меню "Space Taxi/Rebuild Onboard Computer" (каркас фабрики в сцену с Undo) и "Space Taxi/Create Order UI Prefabs" (пишет Assets/Prefabs/UI/OfferCard.prefab и ActiveOrder.prefab и тут же привязывает их в OnboardBinder.offerCardPrefab/activeOrderPrefab).

## Editor/AudioSetupTool.cs
- static AudioSetupTool — "Space Taxi/Audio Setup" (импорт *.ogg: музыка CompressedInMemory/Vorbis, SFX DecompressOnLoad; сборка каталога; менеджер+музыка в обе сцены, ShipAudio/HeatAudio на корабль) и "Space Taxi/Audio Rebuild Catalog" (только каталог Assets/ScriptableObjects/AudioCatalog.asset). Записывает в каталог все SoundId с клипами по именам файлов (Collision — 3 sequential, громкости 0.16..1.0).

## Editor/StubSpriteBaker.cs
- static StubSpriteBaker — меню "Space Taxi/Bake Stub Sprites": печёт PNG в Assets/Sprites (planet_stub.png 256, ship_stub.png, line_stub.png) из StubSprites-текстур с Sprite-импортом. Эти PNG назначены как circleSprite в Minimap/Map биндерах.

## Точки интеграции (важно для новых механик)

- Сборка мира: единственная точка — `GameSession.Start` → `WorldBuilder.Build`. Тела = LevelAsset.Bodies (все поля выше), GravityField создаётся до тел (CelestialBody.OnEnable регистрируется), корабль включается последним (`SetActive(true)` в конце CreateShip). Корабль стартует в State=Landed на планете StartPlanetIndex. Компоненты корабля ищут друг друга через GetComponent/FindAnyObjectByType в Awake — при сборке корабля кодом GO создаётся неактивным.
- Подписка UI на системы: все биндеры лениво (в своём Update) делают `FindAnyObjectByType<GameSession/Ship/GravityField>` и подписываются на события, пока бинд не удался; отписок нет (сессия одна на сцену). Данные читаются напрямую из Ship/OrderService, деньги — только через Wallet.CreditsChanged.
- Аудио: вызовы через статический фасад `Sound.Play/SetLoop/StartLoop/StopLoop/SetLoopIntensity/SetLoopPan/PlayMusic/StopMusic`; контент — AudioCatalog.asset (SoundEntry: Id, Clips, Group, Volume, Pitch, PitchJitter, Loop, Sequential), пересборка меню "Space Taxi/Audio Rebuild Catalog". Сейчас реально играет только музыка (SceneMusic); EngineMain принудительно выключен в ShipAudio.Update, HeatAudio пуст.
- Цикл заказа: OrderService.Update спавнит карточки (offerLifetime 60 с, максимум 4) → UI (OnboardBinder) Accept/Decline → посадка на планете pickup → Aboard (PassengerBoarded) → посадка на dropoff → Delivered (OrderDelivered — `event Action<OrderOffer>`, оффер в аргументе; выплата через PayoutService — см. ДОПОЛНЕНИЕ НОЧИ ниже) / HullSystem.Died|ShipLost → Failed / CancelActive (Aboard только стоя) → OrderCancelled.
- Сцена GameScene.unity — значимые объекты: `[GameSession]` (GameSession: level=AlphaMap01, physicsConfig=PhysicsConfig_Default, currentShip=Ships/kaban.asset, input=Assets/Settings/GameInput.inputactions, passengerCatalog=PassengerCatalog.asset, planetPrefab=Prefabs/Planet.prefab, shipPrefab=Prefabs/Ship.prefab); Main Camera (+Global Light 2D); AudioManager (catalog=AudioCatalog.asset); SceneMusic (MusicFlight); EventSystem; Canvas UI_Root: HUD (HudBinder: SpeedValue, HullValue, FuelTrack/FuelSeg_01..10, WarningBanner/Text), Minimap (MinimapBinder: Bodies, PlayerArrow, worldRange=350, circleSprite=planet_stub), Toast (ToastNotifier); UI_Flow: MapScreenController (mapScreen=MapScreen, onboard=префаб OnboardComputer, pause=PauseScreenController), PauseScreenController (pauseScreen=PauseScreen, settingsUi=префаб SettingsUI), SceneFlowButtons, PauseScreen/PauseMenu (Button_Resume, Button_Map, Button_Settings, Button_ExitGame), MapScreen (правая панель с MapScreenBinder, circleSprite=planet_stub, PlayerMarker). Префаб OnboardComputer: OnboardComputerPanel + OnboardBinder (cashIcon, ratingIcon, offerCardPrefab=OfferCard.prefab, activeOrderPrefab=ActiveOrder.prefab, panelFont пуст — берётся из Clock).
- Сцена MainMenu.unity: AudioManager, SceneMusic (MusicMenu), кнопки Button_NewGame/Button_Continue/Button_Settings/Button_Exit (SceneFlowButtons), инстанс префаба SettingsUI (SettingsScreenController + SettingsBinder: Panel/SliderMaster(+Value), SliderMusic, SliderSfx, SliderUi, Button_Back, Button_Apply). В GameScene SettingsUI-префаб привязан к PauseScreenController.settingsUi.
- Заглушки графики: рантайм — статический `StubSprites` (Circle/ShipTriangle/Line — белые текстуры, красятся через SpriteRenderer.color; PPU подобран под юниты). Ассеты — `Assets/Sprites/*.png` печёт StubSpriteBaker ("Space Taxi/Bake Stub Sprites"), их же используют UI-биндеры как circleSprite. Для новых UI-экранов без текстур: либо `StubSprites.Circle(d)/Line()` в код, либо печать PNG; портреты пассажиров — PassengerProfile.Portrait + PortraitTint, при null биндер оставляет цветную подложку.
- PhysicsConfig: все поля и текущие значения — выше в разделе Physics/PhysicsConfig.cs; живой ассет PhysicsConfig_Default.asset (значения = коду).

## ДОПОЛНЕНИЕ НОЧИ (П2–П4) — новее текста выше, читай ВМЕСТЕ с ним

**Economy (новые классы):**
- `FleetService` (plain C#, GameSession.Fleet, Awake): `Current` (ShipConfig,
  старт egg_rental), `OwnedShips`, `IsOwned(config)`, `TryBuy(config, wallet)`
  (списывает, добавляет во владение, СРАЗУ пересаживает — полные HP/бак),
  `NotifyShipLost()` (свой — слетает с улучшениями, возврат на аренду),
  `SwitchTo(config)` (П3.2: owned или аренда, полные HP/бак, no-op на чужие),
  `BindShip(Ship)`, `BindUpgrades(UpgradeConfig)`, `GetLevel(config, line)`,
  `TryUpgrade(config, line, wallet)` (только owned, PricesPerLevel[level],
  Reconfigure(refill:false) — НЕ лечит), `event ShipChanged`, `event UpgradesChanged`.
- `DebtService` (plain C#, GameSession.Debt): `Balance` (старт 0),
  `event DebtChanged`, `RegisterAccident()` (долг +400, UnpaidAccidents++,
  при 3 — `event Dismissal`), `Repay(amount)` (кламп, при 0 — счётчик аварий
  в 0). Кошельком не управляет.
- `UpgradeConfig` (SO, ассет UpgradeConfig_Default): `int[] PricesPerLevel =
  {300,700,1500}`, русские имена линий, max-эффекты (ThrustPct 25, BoostMax
  1.30, CapacityPct 60, DrainRedPct 15, HpPct 60, HeatRedPct 30, StrafePct 35);
  статический фолбэк `Default`. enum UpgradeLine { Engine, Tank, Hull, Maneuver }.
- `ShipStats` (static): `EffectiveStats Compute(PhysicsConfig, ShipConfig,
  int[] levels, UpgradeConfig)` → { Thrust, Strafe, FuelMax, HpMax, FuelDrain,
  GravityBoost, HeatMul } — линейно N/3; нулевые уровни = базовое поведение.
- `PricingConfig` (SO, ассет PricingConfig_Default, фолбэк `Default`):
  BasePrice 40, PricePerDistanceUnit 0.25, DistanceCap 1500, множители секторов
  (Hub/Near 1.0, Mid1 1.3, Mid2 1.6, Far 2.0) + `GetSectorMultiplier(Sector)`
  + поля выплаты (PenaltyPerCollisionPct 15, PenaltyCapPct 45,
  RentalCommissionPct 25, DebtRepaySharePct 50) + рейтинговые (порог 4.0 →
  цена ×1.1; <2.0 → спавн ×1.3) + MeridianTipsBonusMul 1.1 (П8.2).
- `OrderPricing` (static): `int ComputePrice(PricingConfig, float distance,
  Sector dropoff)` — (base + min(dist,cap)×0.25) × mul, AwayFromZero.
- `PayoutService` (plain C#, GameSession.Payout): `PayoutResult Pay(pricing,
  price, rentalShip, collisions, tipsFraction)` → {Price, Penalty, Commission,
  Tips, ToDebt, Net, Collisions}; wallet.Add(Net) + debt.Repay(ToDebt);
  `PayoutResult LastResult` (снапшот для экрана завершения), `event Paid`.
- `RatingService` (plain C#, GameSession.Rating): `Value` 0–5 (старт 3.0),
  `event RatingChanged`, AddDelivery +0.15 / AddCollisionPenalty −0.10 /
  TaskCompleted +0.10 / TaskFailed −0.10 (жалоба) / FailNoDeath −0.30 /
  FailWithLoss −0.50; `LastDelta`.

**Orders:**
- `OrderOffer`: `int Price` (фиксируется при спавне, с рейтинг-бонусом).
- События: `OrderDelivered` = `Action<OrderOffer>`; `OfferAdded` (П9.3 может
  добавить); `CrashImpact` — в LandingSystem (Action), файрится ТОЛЬКО в
  ветке Crash (контакт с планетой/астероидом); `CollisionsOfActive` (OrderService).
- Задания пассажиров (П5.3): `PassengerTask` {Kind: None/Deadline/NoRefuel/
  NoCollision/SpeedLimit (+П8.1: CometFlyby/RingVisit/StarBackside/HoleFlyby/
  WaitOnPlanet), Text, параметры, Succeeded/Failed}; создание по архетипу в
  AddOffer, дедлайн-таймер с Accept; трекинг условий в OrderService.Update
  (период Aboard); итог: TaskCompleted/TaskFailed + тосты.
- `PassengerPools` (SO, ассет в Resources): имена/фамилии, 5 ArchetypePool
  (Greetings/Wishes/Thanks/Complaints, TipsPct с П8.2); `PassengerGenerator.
  Create(pools)` — runtime-профили (PassengerCatalog.Generate() → фолбэк Pick()).

**Physics:**
- `Ship`: `Reconfigure(config, levels, refill=true)` (refill=false — не лечит,
  мёртвому не ставит Hp — guard Hp>0), `EffectiveStats Eff`, `UpgradeLevels`.
- ShipController/HullSystem/LandingSystem читают Eff (тяга/стрейф/расход/
  качели/максимумы/жар ×HeatMul; станция заправляет до Eff.FuelMax).
- `LandingSystem`: бесплатный автосервис станции УДАЛЁН; платный сервис —
  StationScreenController (1 кр = 1 ед топлива = 1 HP); `ReportServiced(name)`
  файрит StationServiced; `StationHint` — подсказка [B] при посадке.
- `HullSystem.Repair(float)` — частичный ремонт (HpChanged).
- `BlackHole` (CelestialBody, масса ×8, горизонт = гибель, EffectZone r×6 —
  уменьшение корабля 1→0 за ~4 с), `AsteroidBeltSystem`+`Asteroid` (N 24–40,
  r 4–10, вращение 0.12 рад/с, урон (v−55)×0.9 кап 30 = CrashImpact),
  `CometSystem`+`Comet` (3 маршрута 90–150 с, v 400, предупреждение 20 с →
  CometIncoming, гибель при контакте).

**UI:**
- `StationScreenController` ([B], только Landed+HasStation; сорингOrder 100;
  секции СЕРВИС/УЛУЧШЕНИЯ/ДОЛГ/АНГАР в ScrollRect; CanvasScaler 1920×1080;
  ESC-договорённости: NotifyStationOpened/Closed в PauseScreenController).
- `OrderCompleteScreenController` (модал завершения, sortingOrder 110,
  timeScale 0↔1, данные LastResult; NotifyOrderCompleteOpened/Closed).
- `DismissalScreenController` (увольнение, sortingOrder 200, timeScale 0,
  глушит остальные экраны; RuntimeInitializeOnLoad самоспавн).
- OnboardBinder: строка долга (DebtValue), цена на карточках офферов,
  RatingValue слот; демо-кнопка «СМЕНА +100 КР» УДАЛЕНА (П4.2).
- Каталог кораблей: `Resources.LoadAll<ShipConfig>("Ships")` — ассеты в
  Assets/ScriptableObjects/Resources/Ships/.
- Ввод: мапа "UI", действие `Station` (B) в GameInput.inputactions;
  `GameSession.Input` — доступ к ассету ввода.

**Сцена GameScene:** GameSession.currentShip (kaban) больше не читается —
новая игра всегда с аренды (FleetService). GameSession.level — см. П7.2
(возможна замена на BetaMap01).
