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
        public LevelAsset Level => level;
        public PhysicsConfig Config => physicsConfig;
        // модель корабля сессии; без ассета — арендное «Яйцо»
        public ShipConfig CurrentShip => currentShip != null ? currentShip : ShipConfig.EggRentalFallback;

        private void Awake()
        {
            Wallet = new Wallet();
        }

        private void Start()
        {
            var builder = new WorldBuilder(level, physicsConfig, CurrentShip, input, planetPrefab, shipPrefab);
            builder.Build(out Ship ship, out _);
            Ship = ship;

            var follow = Camera.main.gameObject.AddComponent<CameraFollow>();
            follow.SetTarget(Ship);

            var camGo = Camera.main.gameObject;
            if (camGo.GetComponent<World.StarfieldParallax>() == null)
                camGo.AddComponent<World.StarfieldParallax>();

            Orders = gameObject.AddComponent<OrderService>();
            Orders.Init(level, passengerCatalog, Ship);
        }
    }
}

