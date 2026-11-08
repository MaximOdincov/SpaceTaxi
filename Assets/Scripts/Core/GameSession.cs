using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.Audio;
using SpaceTaxi.Economy;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;
using SpaceTaxi.UI;
using SpaceTaxi.World;

namespace SpaceTaxi.Core
{
    public class GameSession : MonoBehaviour
    {
        [SerializeField] private LevelAsset level;
        [SerializeField] private PhysicsConfig physicsConfig;
        [SerializeField] private ShipConfig currentShip;
        [SerializeField] private UpgradeConfig upgradeConfig;
        [SerializeField] private InputActionAsset input;
        [SerializeField] private PassengerCatalog passengerCatalog;

        [SerializeField] private Planet planetPrefab;
        [SerializeField] private Ship shipPrefab;

        public Ship Ship { get; private set; }
        public OrderService Orders { get; private set; }
        public Wallet Wallet { get; private set; }
        public FleetService Fleet { get; private set; }
        public DebtService Debt { get; private set; }
        public PayoutService Payout { get; private set; }
        public RatingService Rating { get; private set; }
        public LevelAsset Level => level;
        public PhysicsConfig Config => physicsConfig;
        // ассет ввода сцены: контроллерам вне сцены (экран станции строится кодом)
        public InputActionAsset Input => input;
        // каталог пассажиров: восстановление пассажира активного заказа из сейва (П10.1)
        public PassengerCatalog Passengers => passengerCatalog;
        // текущий корабль теперь живёт в парке; поле сцены currentShip не читается
        public ShipConfig CurrentShip => Fleet.Current;

        private bool accidentRegistered; // один инцидент = один долг
        private OrderCompleteScreenController orderComplete; // спавнится сам (стиль Dismissal)

        private void Awake()
        {
            Wallet = new Wallet();
            // новая игра: арендное «Яйцо», своих кораблей нет
            Fleet = new FleetService();
            // улучшения: ассет сцены → ассет из Resources (инспектор правит числа)
            // → кодовый инстанс с числами плана (никаких NRE)
            UpgradeConfig upgrades = upgradeConfig;
            if (upgrades == null)
                upgrades = Resources.Load<UpgradeConfig>("UpgradeConfig_Default");
            Fleet.BindUpgrades(upgrades != null ? upgrades : UpgradeConfig.Default);
            Debt = new DebtService();
            Payout = new PayoutService();
            Payout.Bind(Wallet, Debt);
            Rating = new RatingService();
        }

        private void Start()
        {
            var builder = new WorldBuilder(level, physicsConfig, Fleet.Current, input, planetPrefab, shipPrefab);
            builder.Build(out Ship ship, out _);
            Ship = ship;

            Fleet.BindShip(ship);
            var hull = ship.GetComponent<HullSystem>();
            // гибель/потеря корабля → долг за аренду + возврат парка на аренду
            // (ещё один подписчик тех же событий, что и OrderService)
            hull.Died += OnShipLost;
            hull.ShipLost += _ => OnShipLost();
            // долг и замена корпуса — ПОСЛЕ респавна: подмена спрайта в момент
            // удара выглядела как мгновенная пересадка в аренду (жалоба плейтеста)
            hull.Respawned += OnRespawned;

            var follow = Camera.main.gameObject.AddComponent<CameraFollow>();
            follow.SetTarget(Ship);

            var camGo = Camera.main.gameObject;
            if (camGo.GetComponent<World.StarfieldParallax>() == null)
                camGo.AddComponent<World.StarfieldParallax>();

            Orders = gameObject.AddComponent<OrderService>();
            Orders.Init(level, passengerCatalog, Ship);
            Orders.Bind(Rating);
            // доставка платит (16_BETA_PLAN §3.5): цена с карточки, штраф по
            // столкновениям заказа, комиссия на аренде; оффер приходит в аргументе
            // события (Active уже обнулён) — отдельный доступ не нужен
            Orders.OrderDelivered += OnOrderDelivered;
            // дельты рейтинга (§3.7): отказ и гибель; доставка — внутри OnOrderDelivered
            // отказ от заказа, пока пассажир не на борту, — без штрафа рейтингу
            Orders.OrderCancelled += offer =>
            {
                if (offer.State == SpaceTaxi.Orders.OrderState.Aboard)
                    Rating.FailNoDeath();
            };
            Orders.OrderFailed += _ => Rating.FailWithLoss();
            // столкновение при активном заказе — минус рейтинг. Рейтинг слушает
            // CrashImpact напрямую; штраф выплаты считает счётчик OrderService —
            // два независимых потребителя одного события
            ship.GetComponent<LandingSystem>().CrashImpact += OnCrashImpact;

            // звуковые хуки заказов/экономики (П9.3): системные события, не UI.
            // «новый заказ» — реюз ComputerPing (отдельный OrderPing не заводили)
            Orders.OfferAdded += () => Sound.Play(SoundId.ComputerPing);
            Orders.PassengerBoarded += _ => Sound.Play(SoundId.BoardBip);
            Orders.OrderDelivered += _ => Sound.Play(SoundId.DeliveryChime);
            Orders.TaskSucceeded += _ => Sound.Play(SoundId.TaskSuccess);
            Orders.TaskFailed += _ => Sound.Play(SoundId.TaskFail);

            // продолжение забега (П10.1): мир построен по умолчанию — поверх
            // применяется состояние сейва (без флага «продолжить» — тихий выход)
            SaveSystem.ApplyTo(this);

            // Н-8: waypoint с карты в мире (скобка-луч+подпись) и краевые стрелки
            // (waypoint янтарный, цель заказа зелёная)
            gameObject.AddComponent<WaypointIndicator>();

            // тон долга — только на рост (авария); гашения тонов не дают.
            // ПОСЛЕ ApplyTo: иначе восстановленный из сейва долг сыграл бы
            // ложный «аварийный» тон при каждом Continue
            int prevDebt = Debt.Balance;
            Debt.DebtChanged += value =>
            {
                if (value > prevDebt)
                    Sound.Play(SoundId.DebtTone);
                prevDebt = value;
            };
        }

        private void OnOrderDelivered(OrderOffer offer)
        {
            // сначала дельта рейтинга — экран завершения показывает её же
            Rating.AddDelivery();
            // выплата: тосты уходят синхронно внутри Paid, экран открываем после них.
            // Чаевые (Н-14): случайная доля 10–25%, брошенная при спавне карточки;
            // перк «Меридианы» домножается в выплате
            Payout.Pay(Orders.Pricing, offer.Price, Fleet.Current.IsRental,
                Orders.CollisionsOfActive, offer.TipsFraction, IsMeridian);
            // автосейв §3.9 (П10.1): сразу после выплаты — счётчики/рейтинг/долг
            // уже консистентны, экран завершения данных не меняет
            SaveSystem.Save(this);
            if (orderComplete == null)
                orderComplete = FindAnyObjectByType<OrderCompleteScreenController>();
            orderComplete?.Show(offer);
        }

        // перк «комфорт-салон» (16 §3.3) — только у «Меридиана»
        private bool IsMeridian =>
            Fleet.Current != null && Fleet.Current.Id == PayoutService.MeridianShipId;

        private void OnCrashImpact()
        {
            if (Orders != null && Orders.Active != null)
                Rating.AddCollisionPenalty();
        }

        // потеря корабля: арендный — долг за аварию. Проверка владения ДО
        // NotifyShipLost: тот списывает корабль и пересаживает на аренду.
        // Died и ShipLost при одном инциденте не должны сработать оба — guard
        // как у OrderService.FailActive, снимается на респавне
        private void OnShipLost()
        {
            // Died и ShipLost при одном инциденте не должны сработать оба — guard
            if (accidentRegistered)
                return;
            accidentRegistered = true;
        }

        private void OnRespawned()
        {
            if (!accidentRegistered)
                return;
            if (!Fleet.IsOwned(Fleet.Current))
                Debt.RegisterAccident();
            Fleet.NotifyShipLost();
            accidentRegistered = false; // следующая потеря — новый инцидент
        }
    }
}

