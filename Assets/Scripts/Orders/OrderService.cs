using System;
using System.Collections.Generic;
using UnityEngine;
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
        public OrderState State = OrderState.Offered;
    }

    // Заказы альфы (12_ALPHA_PLAN §3.3): живой поток входящих карточек,
    // один активный заказ, посадка — единственная точка взятия/сдачи пассажира.
    // Провал — гибель или потеря корабля. Счётчики доставок и провалов.
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

        private readonly List<OrderOffer> offers = new List<OrderOffer>();
        private readonly List<int> planetIndices = new List<int>();
        private LevelAsset level;
        private PassengerCatalog catalog;
        private float nextSpawnAt;

        public OrderOffer Active { get; private set; }
        public IReadOnlyList<OrderOffer> Offers => offers;
        public int Deliveries { get; private set; }
        public int Failures { get; private set; }

        // События для UI (задача 4).
        public event Action OffersChanged;
        public event Action ActiveOrderChanged;
        public event Action<OrderOffer> PassengerBoarded;
        public event Action<OrderOffer> OrderDelivered;
        public event Action<OrderOffer> OrderFailed;

        public void Init(LevelAsset levelAsset, PassengerCatalog catalog, Ship ship)
        {
            level = levelAsset;

            // Посадка — единственная точка взятия/сдачи пассажира (правила §4).
            ship.GetComponent<LandingSystem>().ShipLanded += OnLanded;
            var hull = ship.GetComponent<HullSystem>();
            hull.Died += FailActive;
            hull.ShipLost += reason => FailActive();

            if (catalog != null)
                for (int i = 0; i < initialOffers; i++)
                    AddOffer(catalog);
            nextSpawnAt = Time.time + UnityEngine.Random.Range(spawnMin, spawnMax);
        }

        private void Update()
        {
            // Смертность карточек.
            int before = offers.Count;
            offers.RemoveAll(o => Time.time >= o.ExpiresAt);
            if (offers.Count != before)
                OffersChanged?.Invoke();

            // Появление новой.
            if (Time.time >= nextSpawnAt)
            {
                if (offers.Count < maxOffers)
                    AddOffer(catalog);
                nextSpawnAt = Time.time + UnityEngine.Random.Range(spawnMin, spawnMax);
            }
        }

        // Принять заказ. Один активный заказ — правило альфы.
        public bool Accept(OrderOffer offer)
        {
            if (Active != null || !offers.Contains(offer))
                return false;
            offers.Remove(offer);
            offer.State = OrderState.Active;
            Active = offer;
            OffersChanged?.Invoke();
            ActiveOrderChanged?.Invoke();
            Debug.Log($"[Orders] принят: {offer.Passenger.DisplayName}, {PlanetName(offer.PickupIndex)} → {PlanetName(offer.DropoffIndex)}");
            return true;
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
                Active = null;
                OrderDelivered?.Invoke(done);
                ActiveOrderChanged?.Invoke();
                Debug.Log($"[Orders] доставлен: {done.Passenger.DisplayName} ({Deliveries}/{Failures})");
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
            ActiveOrderChanged?.Invoke();
            Debug.Log($"[Orders] провален: {failed.Passenger.DisplayName} ({Deliveries}/{Failures})");
        }

        private void AddOffer(PassengerCatalog catalog)
        {
            if (catalog == null || level == null)
                return;
            PassengerProfile passenger = catalog.Pick();
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
            offers.Add(offer);
            OffersChanged?.Invoke();
            Debug.Log($"[Orders] предложение: {passenger.DisplayName} ({passenger.Archetype}), " +
                      $"{PlanetName(offer.PickupIndex)} → {PlanetName(offer.DropoffIndex)}");
        }

        // Полный рандом по планетам уровня (решение 2026-09-27); exclude — не эта.
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

        // Имя планеты для UI.
        public string PlanetName(int index)
        {
            return level != null && index >= 0 && index < level.Bodies.Count
                ? level.Bodies[index].DisplayName
                : "?";
        }
    }
}
