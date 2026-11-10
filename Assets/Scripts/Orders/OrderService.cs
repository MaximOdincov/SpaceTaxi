using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.Orders
{
    public enum OrderState
    {
        Offered = 0,  // карточка входящего заказа
        Active = 1,   // принят, летим за пассажиром
        Aboard = 2,   // пассажир на борту, летим сдавать
        Delivered = 3,
        Failed = 4,
    }

    public sealed class OrderOffer
    {
        public PassengerProfile Passenger;
        public int PickupIndex;    // планета А (индекс в LevelAsset.Bodies)
        public int DropoffIndex;   // планета Б
        public float ExpiresAt;    // время смерти карточки (Time.time)
        public int Price;          // цена предложения, считается при спавне
        public float TipsFraction; // доля чаевых заказа (Н-14): случайные 10–25%, бросок при спавне
        public PassengerTask Task; // задание пассажира (П5.3); null — задания нет
        public OrderState State = OrderState.Offered;
    }

    // Поток входящих карточек, один активный заказ; посадка — взятие/сдача пассажира
    public sealed class OrderService : MonoBehaviour
    {
        [Header("Поток карточек")]
        [Tooltip("Сколько секунд живёт карточка входящего")]
        public float offerLifetime = 60f;
        [Tooltip("Новая карточка: минимум через")]
        public float spawnMin = 15f;
        [Tooltip("Новая карточка: максимум через")]
        public float spawnMax = 30f;
        [Tooltip("Максимум карточек одновременно (лишние старейшие вытесняются)")]
        public int maxOffers = 4;
        [Tooltip("Сколько карточек сразу на старте")]
        public int initialOffers = 2;

        [Header("Ценообразование")]
        [Tooltip("Пусто — фолбэк с дефолтными числами (PricingConfig.Default)")]
        [SerializeField] private PricingConfig pricingConfig;

        private readonly List<OrderOffer> offers = new List<OrderOffer>();
        private readonly List<int> planetIndices = new List<int>();
        private LevelAsset level;
        private PassengerCatalog catalog;
        private RatingService rating; // эффекты рейтинга (§3.7) — приходит Bind-ом из GameSession
        private Ship ship;            // для трекинга задания «тише едем» (П5.3)
        private CometSystem cometSystem; // лениво: жива — задание «покажите комету» доступно (П8.1)
        private float nextSpawnAt;

        public OrderOffer Active { get; private set; }
        public IReadOnlyList<OrderOffer> Offers => offers;
        public int Deliveries { get; private set; }
        public int Failures { get; private set; }

        // штраф за отмену заказа оператором: доля цены, деньги списывает
        // GameSession на OrderCancelled (кошелёк → долг)
        public const float CancelPenaltyFraction = 0.25f;

        // 25% цены, округление вниз — и на кнопке отмены, и при списании
        public static int CancelPenalty(int price)
        {
            return Mathf.FloorToInt(price * CancelPenaltyFraction);
        }

        // цена по текущему прайсу — пересчёт активным заказам из старых сейвов,
        // где поле цены ещё не писалось (иначе штраф отмены показывал «−Đ 0»)
        public int PriceFor(OrderOffer offer)
        {
            int taxiTier = ship != null && ship.ShipConfig != null ? ship.ShipConfig.Tier : 2;
            int basePrice = OrderPricing.ComputePrice(Pricing,
                RouteDistance(offer), SectorOf(offer.DropoffIndex), taxiTier);
            return PriceWithRatingBonus(basePrice);
        }

        // столкновения активного заказа (Active или Aboard); сброс при его закрытии/смене
        public int CollisionsOfActive { get; private set; }

        private PricingConfig resourcesPricing; // кэш попытки Resources.Load

        // конфиг ценообразования: тот же, что считает цену на карточке (для выплаты П4.2).
        // Цепочка: поле сцены → ассет из Resources (инспектор правит числа) → кодовый дефолт
        public PricingConfig Pricing
        {
            get
            {
                if (pricingConfig != null)
                    return pricingConfig;
                if (resourcesPricing == null)
                    resourcesPricing = Resources.Load<PricingConfig>("PricingConfig_Default");
                return resourcesPricing != null ? resourcesPricing : PricingConfig.Default;
            }
        }

        // рейтинг таксиста (П4.3): наценка к цене и замедление спавна — без FindAnyObjectByType
        public void Bind(RatingService ratingService)
        {
            rating = ratingService;
        }

        // события для UI
        public event Action OffersChanged;
        public event Action OfferAdded; // новая карточка (П9.3) — пинг заказа; OffersChanged не годится: он и на протухание
        public event Action ActiveOrderChanged;
        public event Action<OrderOffer> PassengerBoarded;
        public event Action<OrderOffer> OrderDelivered;
        public event Action<OrderOffer> OrderFailed;
        public event Action<OrderOffer> OrderCancelled;
        public event Action<OrderOffer> TaskSucceeded; // задание выполнено (П5.3)
        public event Action<OrderOffer> TaskFailed;    // задание провалено — жалоба

        public void Init(LevelAsset levelAsset, PassengerCatalog catalog, Ship ship)
        {
            level = levelAsset;
            this.catalog = catalog;
            this.ship = ship;

            // посадка — единственная точка взятия/сдачи пассажира
            var landing = ship.GetComponent<LandingSystem>();
            landing.ShipLanded += OnLanded;
            landing.CrashImpact += OnCrashImpact;
            landing.StationServiced += OnStationServiced;
            var hull = ship.GetComponent<HullSystem>();
            hull.Died += FailActive;
            hull.ShipLost += reason => FailActive();

            if (catalog == null)
                Debug.LogWarning("[Orders] каталог пассажиров не назначен — предложений не будет");
            else if (catalog.Profiles.Count == 0 && !catalog.CanGenerate)
                Debug.LogWarning("[Orders] ни профилей, ни пулов пассажиров — предложений не будет");
            else
                for (int i = 0; i < initialOffers; i++)
                    AddOffer(catalog);
            nextSpawnAt = Time.time + SpawnDelay();
        }

        private void Update()
        {
            // смертность карточек; runtime-профили протухших больше не нужны
            int before = offers.Count;
            for (int i = offers.Count - 1; i >= 0; i--)
            {
                if (Time.time < offers[i].ExpiresAt)
                    continue;
                ReleaseProfile(offers[i]);
                offers.RemoveAt(i);
            }
            if (offers.Count != before)
                OffersChanged?.Invoke();

            // while — догоняем пропущенные тики после паузы/лага
            while (Time.time >= nextSpawnAt)
            {
                if (offers.Count < maxOffers)
                    AddOffer(catalog);
                nextSpawnAt = Mathf.Max(nextSpawnAt + SpawnDelay(), Time.time + 2f);
            }

            UpdateTargetGlow();
            TrackTask();
        }

        // подсветка планеты назначения (А — пока ждёт, Б — на борту): компактный
        // жёлтый ореол «чуть больше тела» (Н5.A7 — большое кольцо-подчёркивание
        // на посадочном радиусе убрано; оно было чисто визуальным)
        private Transform targetGlow;
        private SpriteRenderer glowRenderer;
        private string glowBodyName;
        private GravityField gravityField;

        private void UpdateTargetGlow()
        {
            if (Active == null)
            {
                if (targetGlow != null)
                    targetGlow.gameObject.SetActive(false);
                return;
            }

            if (gravityField == null)
                gravityField = FindAnyObjectByType<GravityField>();
            if (gravityField == null)
                return;

            int index = Active.State == OrderState.Aboard ? Active.DropoffIndex : Active.PickupIndex;
            string bodyName = PlanetName(index);

            if (targetGlow == null)
            {
                var glowGo = new GameObject("TargetGlow");
                glowRenderer = glowGo.AddComponent<SpriteRenderer>();
                glowRenderer.sortingOrder = -2; // позади планеты — виден как ободок
                targetGlow = glowGo.transform;
            }

            if (glowBodyName != bodyName || targetGlow.parent == null)
            {
                glowBodyName = bodyName;
                foreach (CelestialBody body in gravityField.Bodies)
                {
                    if (body.DisplayName != bodyName)
                        continue;
                    var bodyRenderer = body.GetComponent<SpriteRenderer>();
                    glowRenderer.sprite = bodyRenderer != null ? bodyRenderer.sprite : null;
                    targetGlow.SetParent(body.transform, false);
                    break;
                }
            }

            targetGlow.gameObject.SetActive(glowRenderer.sprite != null);
            if (!targetGlow.gameObject.activeSelf)
                return;
            // пульс и масштабом (±6%), и альфой вокруг прежних 0.16 — ореол «дышит»
            float wave = Mathf.Sin(Time.time * 3f);
            float pulse = 1.32f + 0.08f * wave;
            targetGlow.localScale = Vector3.one * pulse;
            glowRenderer.color = new Color(1f, 0.83f, 0.3f, Mathf.Lerp(0.10f, 0.22f, 0.5f + 0.5f * wave));
        }

        public bool Accept(OrderOffer offer)
        {
            if (Active != null || !offers.Contains(offer))
                return false;
            offers.Remove(offer);
            offer.State = OrderState.Active;
            Active = offer;
            CollisionsOfActive = 0; // новый заказ — счёт с нуля
            offer.Task?.StartDeadline(Time.time); // дедлайн пошёл (П5.3)
            OffersChanged?.Invoke();
            ActiveOrderChanged?.Invoke();
            Debug.Log($"[Orders] принят: {offer.Passenger.DisplayName}, {PlanetName(offer.PickupIndex)} → {PlanetName(offer.DropoffIndex)}");
            return true;
        }

        public bool Decline(OrderOffer offer)
        {
            if (!offers.Contains(offer))
                return false;
            offers.Remove(offer);
            ReleaseProfile(offer);
            OffersChanged?.Invoke();
            return true;
        }

        // runtime-профиль генератора живёт без сцены — отдаём протухшие/отклонённые
        // (ассетные профили не трогаем: Destroy(asset) в билде ломает их на сессию)
        private void ReleaseProfile(OrderOffer offer)
        {
            if (offer.Passenger != null && offer.Passenger.RuntimeInstance)
                Destroy(offer.Passenger);
        }

        // с пассажиром на борту — только стоя на планете (высадить)
        public bool CancelActive()
        {
            if (Active == null)
                return false;
            if (Active.State == OrderState.Aboard)
            {
                Ship ship = FindAnyObjectByType<Ship>();
                if (ship == null || ship.State != ShipState.Landed)
                    return false;
            }
            OrderOffer cancelled = Active;
            Active = null;
            OrderCancelled?.Invoke(cancelled);
            CollisionsOfActive = 0;
            ActiveOrderChanged?.Invoke();
            Debug.Log("[Orders] заказ отменён оператором");
            return true;
        }

        // восстановление счётчиков из сейва (П10.1)
        public void RestoreCounters(int restoredDeliveries, int restoredFailures)
        {
            Deliveries = Math.Max(0, restoredDeliveries);
            Failures = Math.Max(0, restoredFailures);
        }

        // восстановление активного заказа из сейва (П10.1): оффер построен
        // вызывающим (пассажир и задание уже собраны, дедлайн стартует у него же).
        // Минует Accept — тосты/звук принятия при загрузке не нужны
        public void RestoreActive(OrderOffer restored)
        {
            if (restored == null || Active != null)
                return;
            restored.State = restored.State == OrderState.Aboard
                ? OrderState.Aboard
                : OrderState.Active;
            // старые сейвы поле цены не писали (Price=0) — пересчёт по маршруту,
            // иначе штраф отмены на кнопке показывал «−Đ 0»
            if (restored.Price <= 0)
                restored.Price = PriceFor(restored);
            Active = restored;
            CollisionsOfActive = 0; // удары до сейва не доезжают — счёт с нуля
            ActiveOrderChanged?.Invoke();
        }

        // жёсткое касание с уроном; жар звезды до сюда не доходит.
        // Один обработчик на оба потребителя CrashImpact: счётчик выплаты (П4.2)
        // считает удары в Active И Aboard, задание «без столкновений» — только Aboard
        private void OnCrashImpact()
        {
            if (Active == null)
                return;
            if (Active.State == OrderState.Active || Active.State == OrderState.Aboard)
                CollisionsOfActive++;
            if (Active.State == OrderState.Aboard && TaskIs(PassengerTaskKind.NoCollision))
                Active.Task.Failed = true;
        }

        // платный сервис (заправка/ремонт) на станции при пассажире — задание
        // «без заправки» провалено
        private void OnStationServiced(string station)
        {
            if (Active != null && Active.State == OrderState.Aboard
                && TaskIs(PassengerTaskKind.NoRefuel))
                Active.Task.Failed = true;
        }

        // покадровый трекинг задания активного заказа (П5.3)
        private void TrackTask()
        {
            OrderOffer active = Active;
            if (active == null
                || (active.State != OrderState.Active && active.State != OrderState.Aboard))
                return;
            PassengerTask task = active.Task;
            if (task == null)
                return;

            // время вышло до доставки — задание провалено, заказ может доставиться
            if (task.Kind == PassengerTaskKind.Deadline && !task.Failed
                && task.DeadlineRemaining(Time.time) <= 0f)
                task.Failed = true;

            // превышение скорости ловим в одном кадре — только с пассажиром на борту
            if (active.State == OrderState.Aboard && task.Kind == PassengerTaskKind.SpeedLimit
                && !task.Failed && ship != null && ship.Body != null
                && ship.Body.linearVelocity.magnitude > task.SpeedLimit)
                task.Failed = true;

            // точки интереса (П8.1): трекинг в тот же период Aboard→доставка,
            // успех фиксируется один раз — провалить задание может только гибель заказа
            if (active.State == OrderState.Aboard
                && PassengerTasks.IsInterestPoint(task.Kind) && !task.Succeeded && !task.Failed)
                TrackInterestPoint(task);
        }

        // трекинг точек интереса (П8.1, §3.6): комета/кольцо/тыл звезды/дыра/ожидание
        private void TrackInterestPoint(PassengerTask task)
        {
            if (ship == null || ship.Body == null)
                return;
            Vector2 shipPos = ship.Body.position;

            switch (task.Kind)
            {
                case PassengerTaskKind.CometFlyby:
                    if (cometSystem == null)
                        cometSystem = FindAnyObjectByType<CometSystem>();
                    if (cometSystem == null)
                        return;
                    foreach (Comet comet in cometSystem.ActiveComets)
                        if (comet != null // Destroy обнуляет ссылку до чистки списка
                            && Vector2.Distance(shipPos, comet.transform.position) < task.DistanceThreshold)
                        {
                            task.Succeeded = true;
                            return;
                        }
                    break;

                case PassengerTaskKind.HoleFlyby:
                    if (gravityField == null)
                        gravityField = FindAnyObjectByType<GravityField>();
                    if (gravityField == null)
                        return;
                    foreach (CelestialBody body in gravityField.Bodies)
                        if (body is BlackHole
                            && Vector2.Distance(shipPos, body.transform.position) < task.DistanceThreshold)
                        {
                            task.Succeeded = true;
                            return;
                        }
                    break;

                case PassengerTaskKind.RingVisit:
                {
                    CelestialBody planet = BodyByIndex(task.TargetBodyIndex);
                    if (planet != null
                        && Vector2.Distance(shipPos, planet.transform.position) <= planet.CaptureRing)
                        task.Succeeded = true;
                    break;
                }

                case PassengerTaskKind.StarBackside:
                {
                    CelestialBody star = BodyByIndex(task.TargetBodyIndex);
                    if (star != null)
                    {
                        // тыл звезды относительно центра мира: проекция вектора
                        // «звезда → корабль» на направление «центр → звезда»
                        Vector2 away = star.transform.position.normalized;
                        if (Vector2.Dot(shipPos - (Vector2)star.transform.position, away) > star.Radius)
                            task.Succeeded = true;
                    }
                    break;
                }

                case PassengerTaskKind.WaitOnPlanet:
                {
                    CelestialBody planet = BodyByIndex(task.TargetBodyIndex);
                    if (planet != null && ship.State == ShipState.Landed && ship.LandedOn == planet)
                        task.WaitAccumulated += Time.deltaTime; // копится с прерываниями
                    if (task.WaitAccumulated >= task.WaitSeconds)
                        task.Succeeded = true;
                    break;
                }
            }
        }

        // тело уровня по индексу: сопоставление по имени, как в OnLanded/UpdateTargetGlow
        // (пояса в GravityField не регистрируются, но целями и не бывают)
        private CelestialBody BodyByIndex(int index)
        {
            if (level == null || index < 0 || index >= level.Bodies.Count)
                return null;
            if (gravityField == null)
                gravityField = FindAnyObjectByType<GravityField>();
            if (gravityField == null)
                return null;
            string name = level.Bodies[index].DisplayName;
            foreach (CelestialBody body in gravityField.Bodies)
                if (body.DisplayName == name)
                    return body;
            return null;
        }

        // кометы ставит WorldBuilder всегда; система есть — задание доступно
        private bool CometsAvailable()
        {
            if (cometSystem == null)
                cometSystem = FindAnyObjectByType<CometSystem>();
            return cometSystem != null;
        }

        private bool TaskIs(PassengerTaskKind kind)
        {
            return Active != null && Active.Task != null && Active.Task.Kind == kind;
        }

        private void OnLanded(Planet planet)
        {
            if (Active == null)
                return;
            int idx = PlanetIndexByName(planet.DisplayName);
            if (idx < 0)
                return;

            if (Active.State == OrderState.Active && idx == Active.PickupIndex)
            {
                Active.State = OrderState.Aboard;
                PassengerBoarded?.Invoke(Active);
                Debug.Log($"[Orders] пассажир на борту: {Active.Passenger.DisplayName}");
            }
            else if (Active.State == OrderState.Aboard && idx == Active.DropoffIndex)
            {
                Active.State = OrderState.Delivered;
                Deliveries++;
                OrderOffer done = Active;
                SettleTask(done); // итог задания (П5.3) до события: экран завершения продолжает показывать дельту доставки
                Active = null;
                OrderDelivered?.Invoke(done); // выплата внутри читает CollisionsOfActive
                CollisionsOfActive = 0;
                ActiveOrderChanged?.Invoke();
                Debug.Log($"[Orders] доставлен: {done.Passenger.DisplayName} ({Deliveries}/{Failures})");
            }
        }

        // исход задания при доставке (П5.3): успех — +0.10 рейтинга и тост с
        // благодарностью, провал — жалоба (−0.10); заказ при провале задания
        // НЕ проваливается. Точка интереса (П8.1) требует заработанного
        // трекингом успеха — довёз, не выполнив, = жалоба
        private void SettleTask(OrderOffer delivered)
        {
            PassengerTask task = delivered.Task;
            if (task == null || task.Kind == PassengerTaskKind.None)
                return;
            if (task.Failed
                || (PassengerTasks.IsInterestPoint(task.Kind) && !task.Succeeded))
            {
                TaskFailed?.Invoke(delivered);
                rating?.TaskFailed();
            }
            else
            {
                task.Succeeded = true;
                TaskSucceeded?.Invoke(delivered);
                rating?.TaskCompleted();
            }
        }

        private void FailActive()
        {
            if (Active == null)
                return;
            Active.State = OrderState.Failed;
            Failures++;
            OrderOffer failed = Active;
            Active = null;
            OrderFailed?.Invoke(failed);
            CollisionsOfActive = 0;
            ActiveOrderChanged?.Invoke();
            Debug.Log($"[Orders] провален: {failed.Passenger.DisplayName} ({Deliveries}/{Failures})");
        }

        private void AddOffer(PassengerCatalog catalog)
        {
            if (catalog == null || level == null)
                return;
            // П5.2: свежий профиль из пулов; пулов нет — старые профили каталога
            PassengerProfile passenger = catalog.Generate();
            if (passenger == null)
                passenger = catalog.Pick();
            if (passenger == null)
                return;

            int pickup = RandomPlanetIndex(-1);
            if (pickup < 0)
                return;
            var offer = new OrderOffer
            {
                Passenger = passenger,
                PickupIndex = pickup,
                DropoffIndex = RandomPlanetIndex(pickup),
                ExpiresAt = Time.time + offerLifetime,
            };
            // цена фиксируется в момент спавна карточки и дальше не меняется;
            // премиальность — по тиру корабля-таксиста (Н3.1), без корабля — тир арендного Яйца
            int taxiTier = ship != null && ship.ShipConfig != null ? ship.ShipConfig.Tier : 2;
            int basePrice = OrderPricing.ComputePrice(Pricing,
                RouteDistance(offer), SectorOf(offer.DropoffIndex), taxiTier);
            offer.Price = PriceWithRatingBonus(basePrice);
            // чаевые (Н-14): случайные 10–25% на заказ, бросок один раз при спавне —
            // число живёт до выплаты и не «перебрасывается»
            offer.TipsFraction = UnityEngine.Random.Range(0.10f, 0.25f);
            // задание пассажира (П5.3): случайный тип с шансом 50% (Н-14); дедлайну
            // нужна дальность маршрута, точкам интереса (П8.1) — концы маршрута
            // и контекст уровня
            offer.Task = PassengerTasks.CreateRandom(RouteDistance(offer),
                pickup, offer.DropoffIndex, level, CometsAvailable());
            offers.Add(offer);
            OffersChanged?.Invoke();
            OfferAdded?.Invoke();
            Debug.Log($"[Orders] предложение: {passenger.DisplayName}, " +
                      $"{PlanetName(offer.PickupIndex)} → {PlanetName(offer.DropoffIndex)}, {offer.Price} кр");
        }

        // класс района планеты-цели (влияет на цену)
        private Sector SectorOf(int index)
        {
            return level != null && index >= 0 && index < level.Bodies.Count
                ? level.Bodies[index].Sector
                : Sector.Near;
        }

        // §3.7: рейтинг ≥ порога — наценка к цене предложения («популярного
        // водителя видно»); без рейтинга или ниже порога — базовая цена
        private int PriceWithRatingBonus(int basePrice)
        {
            if (rating == null)
                return basePrice;
            PricingConfig pricing = Pricing;
            if (rating.Value < pricing.RatingPriceBonusThreshold)
                return basePrice;
            return (int)Math.Round(basePrice * pricing.RatingPriceBonusMul, MidpointRounding.AwayFromZero);
        }

        // §3.7: рейтинг < порога — карточки появляются медленнее; spawnMin/spawnMax
        // остаются базой, сами поля не трогаем
        private float SpawnDelay()
        {
            PricingConfig pricing = Pricing;
            float mul = rating != null && rating.Value < pricing.LowRatingSpawnThreshold
                ? pricing.LowRatingSpawnSlowdown
                : 1f;
            return UnityEngine.Random.Range(spawnMin, spawnMax) * mul;
        }

        // случайная планета уровня, exclude — не эта
        private int RandomPlanetIndex(int exclude)
        {
            planetIndices.Clear();
            for (int i = 0; i < level.Bodies.Count; i++)
                if (level.Bodies[i].Type == BodyType.Planet && i != exclude)
                    planetIndices.Add(i);
            return planetIndices.Count == 0 ? -1 : planetIndices[UnityEngine.Random.Range(0, planetIndices.Count)];
        }

        private int PlanetIndexByName(string name)
        {
            for (int i = 0; i < level.Bodies.Count; i++)
                if (level.Bodies[i].DisplayName == name)
                    return i;
            return -1;
        }

        public string PlanetName(int index)
        {
            return level != null && index >= 0 && index < level.Bodies.Count
                ? level.Bodies[index].DisplayName
                : "?";
        }

        public float RouteDistance(OrderOffer offer)
        {
            if (offer == null || level == null
                || offer.PickupIndex < 0 || offer.PickupIndex >= level.Bodies.Count
                || offer.DropoffIndex < 0 || offer.DropoffIndex >= level.Bodies.Count)
                return 0f;
            return Vector2.Distance(level.Bodies[offer.PickupIndex].Position, level.Bodies[offer.DropoffIndex].Position);
        }

        public float DistanceToPlanet(int index, Vector2 from)
        {
            if (level == null || index < 0 || index >= level.Bodies.Count)
                return 0f;
            return Vector2.Distance(level.Bodies[index].Position, from);
        }
    }
}
