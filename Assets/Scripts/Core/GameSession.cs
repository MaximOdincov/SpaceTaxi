using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.Economy;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.Core
{
    public class GameSession : MonoBehaviour
    {
        [SerializeField] private LevelAsset level;
        [SerializeField] private PhysicsConfig physicsConfig;
        [SerializeField] private ShipConfig currentShip;
        [SerializeField] private InputActionAsset input;
        [SerializeField] private PassengerCatalog passengerCatalog;

        [SerializeField] private Planet planetPrefab;
        [SerializeField] private Ship shipPrefab;

        public Ship Ship { get; private set; }
        public OrderService Orders { get; private set; }
        public Wallet Wallet { get; private set; }
        public FleetService Fleet { get; private set; }
        public DebtService Debt { get; private set; }
        public LevelAsset Level => level;
        public PhysicsConfig Config => physicsConfig;
        // текущий корабль теперь живёт в парке; поле сцены currentShip не читается
        public ShipConfig CurrentShip => Fleet.Current;

        private bool accidentRegistered; // один инцидент = один долг

        private void Awake()
        {
            Wallet = new Wallet();
            // новая игра: арендное «Яйцо», своих кораблей нет
            Fleet = new FleetService();
            Debt = new DebtService();
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

