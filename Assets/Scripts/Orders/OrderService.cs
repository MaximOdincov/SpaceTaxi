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
        public event Action<OrderOffer> OrderCancelled;

        public void Init(LevelAsset levelAsset, PassengerCatalog catalog, Ship ship)
        {
            level = levelAsset;

            // Посадка — единственная точка взятия/сдачи пассажира (правила §4).
            ship.GetComponent<LandingSystem>().ShipLanded += OnLanded;
            var hull = ship.GetComponent<HullSystem>();
            hull.Died += FailActive;
            hull.ShipLost += reason => FailActive();

            if (catalog == null)
                Debug.LogWarning("[Orders] каталог пассажиров не назначен — предложений не будет");
            else if (catalog.Profiles.Count == 0)
                Debug.LogWarning("[Orders] каталог пассажиров пуст — добавь профиль в PassengerCatalog");
            else
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

            UpdateTargetGlow();
        }

        // Подсветка планеты назначения: жёлтый пульсирующий ореол позади планеты
        // (А — пока пассажир ждёт, Б — когда на борту).
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
            float pulse = 1.32f + 0.08f * Mathf.Sin(Time.time * 3f);
            targetGlow.localScale = Vector3.one * pulse;
            glowRenderer.color = new Color(1f, 0.83f, 0.3f, 0.16f);
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

        // Отклонить предложение — карточка исчезает, счётчики не трогаем.
        public bool Decline(OrderOffer offer)
        {
            if (!offers.Contains(offer))
                return false;
            offers.Remove(offer);
            OffersChanged?.Invoke();
            return true;
        }

        // Отказаться от активного заказа: без пассажира — всегда; с пассажиром
        // на борту — только стоя на планете (высадить).
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
            ActiveOrderChanged?.Invoke();
            Debug.Log("[Orders] заказ отменён оператором");
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

        // Дальность маршрута А→Б в юнитах.
        public float RouteDistance(OrderOffer offer)
        {
            if (offer == null || level == null
                || offer.PickupIndex < 0 || offer.PickupIndex >= level.Bodies.Count
                || offer.DropoffIndex < 0 || offer.DropoffIndex >= level.Bodies.Count)
                return 0f;
            return Vector2.Distance(level.Bodies[offer.PickupIndex].Position, level.Bodies[offer.DropoffIndex].Position);
        }

        // Дистанция от корабля до планеты заказа.
        public float DistanceToPlanet(int index, Vector2 from)
        {
            if (level == null || index < 0 || index >= level.Bodies.Count)
                return 0f;
            return Vector2.Distance(level.Bodies[index].Position, from);
        }
    }
}
