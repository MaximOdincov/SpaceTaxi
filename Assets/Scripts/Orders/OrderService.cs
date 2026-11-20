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
        public float TipsFraction; // доля чаевых заказа: случайные 10–25%, бросок при спавне
        public PassengerTask Task; // задание пассажира; null — задания нет
        public OrderState State = OrderState.Offered;

        // таймер «паникёра»: жёсткий дедлайн заказа, время вышло —
        // срыв (FailActive, штрафа деньгами нет). 0 — пассажир не торопится
        public float HurrySeconds;
        public float HurryStartedAt = -1f; // Time.time в момент Accept; < 0 — не запущен
        public bool HurryMissed;           // срыв по таймеру (для тоста)
        public string FailReason;          // причина провала для тоста (срыв/груз); null — обычный провал

        // остаток таймера «паникёра»; без таймера — MaxValue, до Accept — полный лимит
        public float HurryRemaining(float now)
        {
            if (HurrySeconds <= 0f)
                return float.MaxValue;
            return HurryStartedAt < 0f
                ? HurrySeconds
                : Mathf.Max(0f, HurrySeconds - (now - HurryStartedAt));
        }

        // таймер стартует один раз — при принятии заказа (как StartDeadline у задания)
        public void StartHurry(float now)
        {
            if (HurryStartedAt < 0f)
                HurryStartedAt = now;
        }
    }

    // Поток входящих карточек, один активный заказ; посадка — взятие/сдача пассажира
    public sealed class OrderService : MonoBehaviour
    {
        [Header("Поток карточек")]
        [Tooltip("Сколько секунд живёт карточка входящих")]
        public float offerLifetime = 90f;
        [Tooltip("Новая карточка: минимум через (сек)")]
        public float spawnMin = 20f;
        [Tooltip("Новая карточка: максимум через (сек)")]
        public float spawnMax = 35f;
        [Tooltip("Максимум карточек одновременно (лишние старейшие вытесняются)")]
        public int maxOffers = 4;
        [Tooltip("Карточек при старте сессии")]
        public int initialOffers = 2;

        [Header("Ценообразование")]
        [Tooltip("Пусто — фолбэк с дефолтными числами (PricingConfig.Default)")]
        [SerializeField] private PricingConfig pricingConfig;

        private readonly List<OrderOffer> offers = new List<OrderOffer>();
        private const int FatTaskRolls = 8; // бросков на обязательное задание «жирного» (шанс null 50% за бросок)
        private LevelAsset level;
        private PassengerCatalog catalog;
        private RatingService rating; // эффекты рейтинга — приходит Bind-ом из GameSession
        private Ship ship;            // для трекинга задания «тише едем»
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
            int serviceClass = ShipServiceClass();
            int basePrice = OrderPricing.ComputePrice(Pricing,
                RouteDistance(offer), SectorOf(offer.DropoffIndex), serviceClass);
            return PriceWithRatingBonus(basePrice);
        }

        // столкновения активного заказа (Active или Aboard); сброс при его закрытии/смене
        public int CollisionsOfActive { get; private set; }

        private PricingConfig resourcesPricing; // кэш попытки Resources.Load

        // конфиг ценообразования: тот же, что считает цену на карточке (для выплаты).
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

        // рейтинг таксиста: наценка к цене и замедление спавна — без FindAnyObjectByType
        public void Bind(RatingService ratingService)
        {
            rating = ratingService;
        }

        // события для UI
        public event Action OffersChanged;
        public event Action OfferAdded; // новая карточка — пинг заказа; OffersChanged не годится: он и на протухание
        public event Action ActiveOrderChanged;
        public event Action<OrderOffer> PassengerBoarded;
        public event Action<OrderOffer> OrderDelivered;
        public event Action<OrderOffer> OrderFailed;
        public event Action<OrderOffer> OrderCancelled;
        public event Action<OrderOffer> TaskSucceeded; // задание выполнено
        public event Action<OrderOffer> TaskFailed;    // задание провалено — жалоба
        // тост-момент: свершение точки интереса/ожидания и срыв условия
        // тостятся в момент события (итог и так показывается при доставке)
        public event Action<string> TaskMomentSuccess;
        public event Action<string> TaskMomentFail;
        public event Action<OrderOffer> OrderAccepted; // принят игроком (тост); RestoreActive не зовёт
        public event Action<string> OrderWarning;      // промежуточное предупреждение задания — тост Danger

        // первичная привязка домена к миру: уровень, каталог пассажиров, борт
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

        // тик домена: доживает/протухает поток карточек, трекинг задания, паникёр
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
            CheckHurry();
        }

        private GravityField gravityField; // реестр тел: трекинг заданий (заполняется лениво)

        // подсветка планеты назначения — презентация (TargetGlowPresenter);
        // домен только сообщает смену цели (А — пока ждёт, Б — на борту)
        private TargetGlowPresenter glowPresenter;

        // доклад презентеру о текущей цели (А — ждёт забор, Б — везёт): имя планеты
        private void UpdateTargetGlow()
        {
            if (glowPresenter == null)
            {
                glowPresenter = FindAnyObjectByType<TargetGlowPresenter>();
                if (glowPresenter == null)
                    glowPresenter = new GameObject("TargetGlowPresenter").AddComponent<TargetGlowPresenter>();
            }
            glowPresenter.SetTarget(Active == null
                ? null
                : PlanetName(Active.State == OrderState.Aboard ? Active.DropoffIndex : Active.PickupIndex));
        }

        // взятие заказа: активным становится ровно один — карточка уходит из ленты
        public bool Accept(OrderOffer offer)
        {
            if (Active != null || !offers.Contains(offer))
                return false;
            offers.Remove(offer);
            offer.State = OrderState.Active;
            Active = offer;
            CollisionsOfActive = 0; // новый заказ — счёт с нуля
            offer.Task?.StartDeadline(Time.time); // дедлайн пошёл
            offer.StartHurry(Time.time);          // таймер «паникёра» пошёл
            OffersChanged?.Invoke();
            ActiveOrderChanged?.Invoke();
            Debug.Log($"[Orders] принят: {offer.Passenger.DisplayName}, {PlanetName(offer.PickupIndex)} → {PlanetName(offer.DropoffIndex)}");
            OrderAccepted?.Invoke(offer); // тост принятия; RestoreActive его минует

            // корабль уже стоит на планете пикапа — пассажир сразу на борт,
            // без требования взлететь и сесть повторно
            if (ship != null && ship.State == ShipState.Landed && ship.LandedOn != null
                && PlanetIndexByName(ship.LandedOn.DisplayName) == offer.PickupIndex)
            {
                OnLanded(ship.LandedOn);
            }
            return true;
        }

        // отказ от входящей карточки: снять с ленты, runtime-профиль — в утиль
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

        // восстановление счётчиков из сейва
        public void RestoreCounters(int restoredDeliveries, int restoredFailures)
        {
            Deliveries = Math.Max(0, restoredDeliveries);
            Failures = Math.Max(0, restoredFailures);
        }

        // восстановление активного заказа из сейва: оффер построен
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
        // Один обработчик на оба потребителя CrashImpact: счётчик выплаты
        // считает удары в Active И Aboard, задание «без столкновений» — только Aboard
        private void OnCrashImpact()
        {
            if (Active == null)
                return;
            if (Active.State == OrderState.Active || Active.State == OrderState.Aboard)
                CollisionsOfActive++;
            if (Active.State == OrderState.Aboard && TaskIs(PassengerTaskKind.NoCollision))
            {
                Active.Task.Failed = true;
                TaskMomentFail?.Invoke(PassengerTasks.FailMomentText(Active.Task));
            }
            // CrashImpact приходит только на удар с уроном (скорость выше
            // посадочной) — это и есть «сильный удар»
        }

        // платный сервис (заправка/ремонт) на станции при пассажире — задание
        // «без заправки» провалено
        private void OnStationServiced(string station)
        {
            if (Active != null && Active.State == OrderState.Aboard
                && TaskIs(PassengerTaskKind.NoRefuel))
            {
                Active.Task.Failed = true;
                TaskMomentFail?.Invoke(PassengerTasks.FailMomentText(Active.Task));
            }
        }

        // покадровый трекинг задания активного заказа
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
            {
                task.Failed = true;
                TaskMomentFail?.Invoke(PassengerTasks.FailMomentText(task));
            }

            // превышение скорости ловим в одном кадре — только с пассажиром на борту
            if (active.State == OrderState.Aboard && task.Kind == PassengerTaskKind.SpeedLimit
                && !task.Failed && ship != null && ship.Body != null
                && ship.Body.linearVelocity.magnitude > task.SpeedLimit)
            {
                task.Failed = true;
                TaskMomentFail?.Invoke(PassengerTasks.FailMomentText(task));
            }

            // избегания-условия с эпизодами — жар звёзд и пояса:
            // превышение лимита замечаний = жалоба при доставке
            if (active.State == OrderState.Aboard && task.Kind == PassengerTaskKind.NoHeat
                && !task.Failed && ship != null && ship.Body != null)
                TrackAvoidEpisode(task, TrackKind.Heat);
            if (active.State == OrderState.Aboard && task.Kind == PassengerTaskKind.BeltAvoid
                && !task.Failed && ship != null && ship.Body != null)
                TrackAvoidEpisode(task, TrackKind.Belt);

            // точки интереса: трекинг в тот же период Aboard→доставка,
            // успех фиксируется один раз — провалить задание может только гибель заказа
            if (active.State == OrderState.Aboard
                && PassengerTasks.IsInterestPoint(task.Kind) && !task.Succeeded && !task.Failed)
                TrackInterestPoint(task);
        }

        // эпизоды избегания (жар/пояса) — одно замечание на эпизод
        // (гистерезис: «висеть» внутри зоны == один эпизод); предел — жалоба
        // при доставке; заказ не срывается
        private enum TrackKind { Heat, Belt }

        // эпизоды избегания (жар/пояс) с гистерезисом: открыли — замечание; закрыли без лимита — восстановление
        private void TrackAvoidEpisode(PassengerTask task, TrackKind kind)
        {
            bool inside = kind == TrackKind.Heat ? InStarHeat() : InBeltBand();
            if (!inside)
            {
                task.EpisodeOpen = false; // эпизод закрыт — новый вход даст новое замечание
                return;
            }
            if (task.EpisodeOpen)
                return; // тот же эпизод — замечание уже засчитано
            task.EpisodeOpen = true;
            task.Strikes++;
            int limit = kind == TrackKind.Heat
                ? PassengerTasks.NoHeatStrikeLimit
                : PassengerTasks.BeltStrikeLimit;
            if (task.Strikes >= limit)
            {
                task.Failed = true;
                TaskMomentFail?.Invoke(PassengerTasks.FailMomentText(task));
                OrderWarning?.Invoke($"Пассажир в ярости: замечаний {task.Strikes}/{limit} — будет жалоба");
            }
            else
            {
                OrderWarning?.Invoke(kind == TrackKind.Heat
                    ? $"Пассажир боится жара: замечание {task.Strikes}/{limit} — подальше от звёзд"
                    : $"Пассажир боится пояса: замечание {task.Strikes}/{limit} — облетайте");
            }
        }

        // в зоне жара любой звезды?
        private bool InStarHeat()
        {
            if (gravityField == null)
                gravityField = FindAnyObjectByType<GravityField>();
            if (gravityField == null)
                return false;
            foreach (CelestialBody body in gravityField.Bodies)
                if (body is Star star && star.IsInHeatZone(ship.transform.position))
                    return true;
            return false;
        }

        // внутри полосы астероидного пояса?
        private bool InBeltBand()
        {
            if (level == null)
                return false;
            Vector2 shipPos = ship.Body != null ? ship.Body.position : (Vector2)ship.transform.position;
            foreach (CelestialBodyData data in level.Bodies)
            {
                if (data.Type != BodyType.AsteroidBelt)
                    continue;
                float d = Vector2.Distance(shipPos, data.Position);
                if (Mathf.Abs(d - data.Radius) <= data.BeltWidth)
                    return true;
            }
            return false;
        }

        // срыв заказа «паникёра»: время вышло — заказ провален
        // (FailActive тостит через OrderFailed, рейтинг-штраф — обычный путь);
        // штрафа деньгами нет
        private void CheckHurry()
        {
            OrderOffer active = Active;
            if (active == null || active.HurrySeconds <= 0f
                || active.HurryStartedAt < 0f || active.HurryMissed)
                return;
            if (active.HurryRemaining(Time.time) > 0f)
                return;
            active.HurryMissed = true;
            active.FailReason = "время вышло";
            FailActive();
        }

        // трекинг точек интереса: комета/кольцо/тыл звезды/дыра/ожидание
        private void TrackInterestPoint(PassengerTask task)
        {
            if (ship == null || ship.Body == null)
                return;
            Vector2 shipPos = ship.Body.position;

            switch (task.Kind)
            {

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

                case PassengerTaskKind.StarNearby:
                {
                    // близость, а не тыл
                    CelestialBody star = BodyByIndex(task.TargetBodyIndex);
                    if (star != null
                        && Vector2.Distance(shipPos, star.transform.position) <= task.DistanceThreshold)
                        task.Succeeded = true;
                    break;
                }
            }

            // момент свершения — тост сразу, а не только при доставке
            if (task.Succeeded && !task.MomentFired)
            {
                task.MomentFired = true;
                TaskMomentSuccess?.Invoke(PassengerTasks.SuccessMomentText(task));
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

        // есть ли у активного заказа задание данного вида
        private bool TaskIs(PassengerTaskKind kind)
        {
            return Active != null && Active.Task != null && Active.Task.Kind == kind;
        }

        // контакт с планетой: забор пассажира (Aboard) или высадка — итог заказа
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
                ActiveOrderChanged?.Invoke(); // панель заказа перерисовывается и при пикапе
                Debug.Log($"[Orders] пассажир на борту: {Active.Passenger.DisplayName}");
            }
            else if (Active.State == OrderState.Aboard && idx == Active.DropoffIndex)
            {
                Active.State = OrderState.Delivered;
                Deliveries++;
                OrderOffer done = Active;
                SettleTask(done); // итог задания до события: экран завершения продолжает показывать дельту доставки
                Active = null;
                OrderDelivered?.Invoke(done); // выплата внутри читает CollisionsOfActive
                CollisionsOfActive = 0;
                ActiveOrderChanged?.Invoke();
                Debug.Log($"[Orders] доставлен: {done.Passenger.DisplayName} ({Deliveries}/{Failures})");
            }
        }

        // исход задания при доставке: успех — +0.10 рейтинга и тост с
        // благодарностью, провал — жалоба (−0.10); заказ при провале задания
        // НЕ проваливается. Точка интереса требует заработанного
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

        // жёсткий срыв активного заказа (гибель/дедлайн паникёра): жалоба и вылет пассажира
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

        // рождение карточки: пассажир из каталога, концы маршрута, цена, задание
        private void AddOffer(PassengerCatalog catalog)
        {
            if (catalog == null || level == null)
                return;
            // свежий профиль из пулов; пулов нет — профили из каталога
            PassengerProfile passenger = catalog.Generate();
            if (passenger == null)
                passenger = catalog.Pick();
            if (passenger == null)
                return;

            PricingConfig pricing = Pricing;
            int serviceClass = ShipServiceClass();
            float ratingValue = CurrentRating();

            // редкий «жирный» заказ — дальний, с заданием и наценкой;
            // шанс растёт с рейтингом
            bool fat = UnityEngine.Random.value * 100f < FatOrderChancePct(pricing, ratingValue);
            // жирный изредка пренебрегает гейтом районов — «вкус» закрытых
            // секторов; отказаться можно бесплатно (Decline без штрафа)
            bool ignoreGate = fat && UnityEngine.Random.value * 100f < pricing.FatOrderLockedSectorChancePct;

            // престиж-фильтр: пикап — любая планета; дропофф — только
            // сектора, чей класс пассажира по силам кораблю. Нетребовательный
            // пассажир (один бросок на заказ) согласен на класс ниже — дропофф
            // без гейта; жирный заказ — без гейта и без броска
            bool lenient = !ignoreGate
                && UnityEngine.Random.value * 100f < pricing.LenientPassengerChancePct;
            List<int> pickupPool = OfferPlanets();
            List<int> dropoffPool = ignoreGate || lenient
                ? pickupPool
                : OfferPlanets(serviceClass);
            if (dropoffPool.Count < 2)
            {
                // пустая доска хуже дальнего заказа — гейт ослабляется
                Debug.LogWarning("[Orders] гейт районов отсеял почти все планеты — ослабляю фильтр");
                dropoffPool = pickupPool;
            }

            int pickup = PickPickup(pickupPool, pricing);
            int dropoff = PickDropoff(dropoffPool, pickup, fat, pricing);
            if (pickup < 0 || dropoff < 0)
                return;

            var offer = new OrderOffer
            {
                Passenger = passenger,
                PickupIndex = pickup,
                DropoffIndex = dropoff,
                ExpiresAt = Time.time + offerLifetime,
            };
            // цена фиксируется в момент спавна карточки и дальше не меняется;
            // тариф — по классу обслуживания корабля-таксиста,
            // без корабля — эконом (аренда «Яйца»)
            int basePrice = OrderPricing.ComputePrice(pricing,
                RouteDistance(offer), SectorOf(offer.DropoffIndex), serviceClass);
            if (fat)
                basePrice = (int)Math.Round(basePrice * pricing.FatOrderPriceMul, MidpointRounding.AwayFromZero);
            offer.Price = PriceWithRatingBonus(basePrice);
            // чаевые: случайные 10–25% на заказ, бросок один раз при спавне —
            // число живёт до выплаты и не «перебрасывается»
            offer.TipsFraction = UnityEngine.Random.Range(0.10f, 0.25f);
            // «паникёр»: пассажир торопится — жёсткий таймер на весь заказ
            if (passenger.Hurries)
                offer.HurrySeconds = PassengerTasks.HurrySecondsFor(RouteDistance(offer));
            // задание пассажира: случайный тип с шансом 50%; дедлайну
            // нужна дальность маршрута, точкам интереса — концы маршрута
            // и контекст уровня. У «жирного» задание обязательно — добрасываем
            int taskRolls = fat ? FatTaskRolls : 1;
            for (int i = 0; i < taskRolls && offer.Task == null; i++)
                offer.Task = PassengerTasks.CreateRandom(RouteDistance(offer),
                    pickup, dropoff, level);
            offers.Add(offer);
            OffersChanged?.Invoke();
            OfferAdded?.Invoke();
            Debug.Log($"[Orders] предложение: {passenger.DisplayName}, " +
                      $"{PlanetName(offer.PickupIndex)} → {PlanetName(offer.DropoffIndex)}, {UI.UiFormat.Currency} {offer.Price}" +
                      (fat ? " [жирный]" : ""));
        }

        // класс обслуживания корабля-таксиста; без корабля — эконом
        // (аренда «Яйца» всегда эконом)
        private int ShipServiceClass()
        {
            return ship != null && ship.ShipConfig != null ? (int)ship.ShipConfig.ServiceClass : 0;
        }

        // рейтинг на момент спавна; Bind приходит после Init (GameSession),
        // первые карточки считаем от старта новой игры (RatingService.StartValue)
        private float CurrentRating()
        {
            return rating != null ? rating.Value : 3f;
        }

        // шанс «жирного» заказа, % — база + прибавка за рейтинг, кламп потолком
        private static float FatOrderChancePct(PricingConfig pricing, float ratingValue)
        {
            return Mathf.Clamp(pricing.FatOrderBaseChancePct + pricing.FatOrderChancePerRatingPct * ratingValue,
                0f, pricing.FatOrderMaxChancePct);
        }

        // пул всех планет уровня — пикап возможен в любом секторе
        private List<int> OfferPlanets()
        {
            var pool = new List<int>();
            for (int i = 0; i < level.Bodies.Count; i++)
            {
                if (level.Bodies[i].Type != BodyType.Planet)
                    continue;
                pool.Add(i);
            }
            return pool;
        }

        // пул дропоффов по классу корабля — сектор даёт пассажиров
        // своего класса (класс пассажира = SectorRequiredClass сектора),
        // кораблю ниже класса сектор не виден. Нетребовательные пассажиры
        // и жирные заказы идут мимо этого фильтра (общий пул)
        private List<int> OfferPlanets(int serviceClass)
        {
            var pool = new List<int>();
            for (int i = 0; i < level.Bodies.Count; i++)
            {
                if (level.Bodies[i].Type != BodyType.Planet)
                    continue;
                if (Pricing.GetSectorRequiredClass(level.Bodies[i].Sector) > serviceClass)
                    continue;
                pool.Add(i);
            }
            return pool;
        }

        // пикап со сдвигом «к игроку». Вес планеты = nearShare × близость
        // (линейный спад до нуля к PickupProximityRange) + остаток равномерно;
        // nearShare тает с рейтингом — доля дальних пикапов растёт с прокачкой.
        // Кривая от «низкого рейтинга» (тот же порог, что замедляет спавн)
        // до рейтинга 5: игра стартует с 3.0 — точка уже на спаде кривой
        private int PickPickup(List<int> pool, PricingConfig pricing)
        {
            if (pool.Count == 0)
                return -1;
            Vector2 playerPos = ShipPosition();
            float ratingSpan = Mathf.Max(0.001f, 5f - pricing.LowRatingSpawnThreshold);
            float ratingT = Mathf.Clamp01((CurrentRating() - pricing.LowRatingSpawnThreshold) / ratingSpan);
            float nearShare = Mathf.Lerp(pricing.PickupNearShareAtMinRating,
                pricing.PickupNearShareAtMaxRating, ratingT);
            float total = 0f;
            var weights = new float[pool.Count];
            for (int i = 0; i < pool.Count; i++)
            {
                float proximity = Mathf.Clamp01(1f - DistanceToPlanet(pool[i], playerPos)
                    / Mathf.Max(1f, pricing.PickupProximityRange));
                weights[i] = nearShare * proximity + (1f - nearShare);
                total += weights[i];
            }
            if (total <= 0f)
                return pool[UnityEngine.Random.Range(0, pool.Count)];
            float roll = UnityEngine.Random.value * total;
            for (int i = 0; i < pool.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0f)
                    return pool[i];
            }
            return pool[pool.Count - 1];
        }

        // цель: обычный заказ — равномерно по пулу; «жирный» — дальняя
        // пара: планеты дальше FatOrderMinRouteDistance, иначе самая дальняя
        private int PickDropoff(List<int> pool, int pickup, bool fat, PricingConfig pricing)
        {
            if (pickup < 0 || pickup >= level.Bodies.Count)
                return -1;
            var candidates = new List<int>();
            var far = new List<int>();
            float farthest = -1f;
            int farthestIndex = -1;
            foreach (int index in pool)
            {
                if (index == pickup)
                    continue;
                candidates.Add(index);
                float distance = Vector2.Distance(level.Bodies[pickup].Position, level.Bodies[index].Position);
                if (distance > farthest)
                {
                    farthest = distance;
                    farthestIndex = index;
                }
                if (fat && distance >= pricing.FatOrderMinRouteDistance)
                    far.Add(index);
            }
            if (candidates.Count == 0)
                return -1;
            if (!fat)
                return candidates[UnityEngine.Random.Range(0, candidates.Count)];
            return far.Count > 0
                ? far[UnityEngine.Random.Range(0, far.Count)]
                : farthestIndex;
        }

        // позиция для весов близости: корабль жив — от него; нет — планета старта
        private Vector2 ShipPosition()
        {
            if (ship != null)
                return ship.Body != null ? (Vector2)ship.Body.position : (Vector2)ship.transform.position;
            int start = level.StartPlanetIndex;
            return start >= 0 && start < level.Bodies.Count
                ? level.Bodies[start].Position
                : Vector2.zero;
        }

        // класс района планеты-цели (влияет на цену)
        private Sector SectorOf(int index)
        {
            return level != null && index >= 0 && index < level.Bodies.Count
                ? level.Bodies[index].Sector
                : Sector.Near;
        }

        // рейтинг ≥ порога — наценка к цене предложения («популярного
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

        // рейтинг < порога — карточки появляются медленнее; spawnMin/spawnMax
        // остаются базой, сами поля не трогаем
        private float SpawnDelay()
        {
            PricingConfig pricing = Pricing;
            float mul = rating != null && rating.Value < pricing.LowRatingSpawnThreshold
                ? pricing.LowRatingSpawnSlowdown
                : 1f;
            return UnityEngine.Random.Range(spawnMin, spawnMax) * mul;
        }

        // индекс тела по имени (для целей заданий); нет — -1
        private int PlanetIndexByName(string name)
        {
            for (int i = 0; i < level.Bodies.Count; i++)
                if (level.Bodies[i].DisplayName == name)
                    return i;
            return -1;
        }

        // имя тела по индексу (карточки/тексты); вне диапазона — «—»
        public string PlanetName(int index)
        {
            return level != null && index >= 0 && index < level.Bodies.Count
                ? level.Bodies[index].DisplayName
                : "?";
        }

        // прямая дистанция забор→высадка (юн) — база цены и дедлайна
        public float RouteDistance(OrderOffer offer)
        {
            if (offer == null || level == null
                || offer.PickupIndex < 0 || offer.PickupIndex >= level.Bodies.Count
                || offer.DropoffIndex < 0 || offer.DropoffIndex >= level.Bodies.Count)
                return 0f;
            return Vector2.Distance(level.Bodies[offer.PickupIndex].Position, level.Bodies[offer.DropoffIndex].Position);
        }

        // расстояние от точки до планеты-цели (живые расстояния карточек)
        public float DistanceToPlanet(int index, Vector2 from)
        {
            if (level == null || index < 0 || index >= level.Bodies.Count)
                return 0f;
            return Vector2.Distance(level.Bodies[index].Position, from);
        }
    }
}
