using UnityEngine;
using UnityEngine.InputSystem;
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
        // текущий корабль теперь живёт в парке; поле сцены currentShip не читается
        public ShipConfig CurrentShip => Fleet.Current;

        private bool accidentRegistered; // один инцидент = один долг
        private OrderCompleteScreenController orderComplete; // спавнится сам (стиль Dismissal)

        private void Awake()
        {
            Wallet = new Wallet();
            // новая игра: арендное «Яйцо», своих кораблей нет
            Fleet = new FleetService();
            // улучшения: ассет сцены, при отсутствии — кодовый инстанс
            // с числами плана (никаких NRE)
            Fleet.BindUpgrades(upgradeConfig != null ? upgradeConfig : UpgradeConfig.Default);
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
            // инцидент закрыт: после респавна следующая потеря — новый случай
            hull.Respawned += () => accidentRegistered = false;

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
            Orders.OrderCancelled += _ => Rating.FailNoDeath();
            Orders.OrderFailed += _ => Rating.FailWithLoss();
            // столкновение при активном заказе — минус рейтинг. Рейтинг слушает
            // CrashImpact напрямую; штраф выплаты считает счётчик OrderService —
            // два независимых потребителя одного события
            ship.GetComponent<LandingSystem>().CrashImpact += OnCrashImpact;
        }

        private void OnOrderDelivered(OrderOffer offer)
        {
            // сначала дельта рейтинга — экран завершения показывает её же
            Rating.AddDelivery();
            // выплата: тосты уходят синхронно внутри Paid, экран открываем после них.
            // Чаевые (П8.2, §3.5): только за выполненное задание пассажира, доля —
            // щедрость архетипа из пулов; перк «Меридианы» домножается в выплате
            Payout.Pay(Orders.Pricing, offer.Price, Fleet.Current.IsRental,
                Orders.CollisionsOfActive, TipsFractionOf(offer), IsMeridian);
            if (orderComplete == null)
                orderComplete = FindAnyObjectByType<OrderCompleteScreenController>();
            orderComplete?.Show(offer);
        }

        // доля чаевых выполненного задания (П8.2, §3.5–3.6): Succeeded ставится
        // в OrderService.SettleTask до этого события; задания нет или провалено — 0
        private float TipsFractionOf(OrderOffer offer)
        {
            if (offer?.Task == null || !offer.Task.Succeeded || offer.Passenger == null)
                return 0f;
            var pools = passengerCatalog != null ? passengerCatalog.Pools : null;
            return pools != null ? pools.TipsPctOf(offer.Passenger.Archetype) : 0f;
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
            if (accidentRegistered)
                return;
            accidentRegistered = true;
            if (!Fleet.IsOwned(Fleet.Current))
                Debt.RegisterAccident();
            Fleet.NotifyShipLost();
        }
    }
}

