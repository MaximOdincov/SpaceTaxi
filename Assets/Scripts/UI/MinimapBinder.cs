using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Orders;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Миникарта-радар: локальный фрагмент мира вокруг корабля (~1000 юнитов),
    // тела белыми точками (звезда с ореолом), стрелка игрока по вектору
    // скорости, цель заказа жёлтым. Вешается на HUD/Minimap.
    public class MinimapBinder : MonoBehaviour
    {
        [Tooltip("Радиус охвата минимапа в юнитах мира")]
        public float worldRange = 550f;

        [SerializeField] private Sprite circleSprite;

        private RectTransform bodiesRoot;
        private RectTransform playerArrow;
        private RectTransform targetMark;
        private readonly List<RectTransform> bodyDots = new List<RectTransform>();
        private readonly List<CelestialBody> bodies = new List<CelestialBody>();

        private bool built;
        private float pxPerUnit;

        private static readonly Color DotColor = new Color(0.85f, 1f, 0.9f);
        private static readonly Color StarColor = new Color(1f, 0.82f, 0.35f);
        private static readonly Color TaxiColor = new Color(1f, 0.831f, 0.302f);

        private void Awake()
        {
            // Статичную заготовку убираем — точки строим из живого мира.
            Transform bodiesTransform = transform.Find("Bodies");
            for (int i = bodiesTransform.childCount - 1; i >= 0; i--)
                Destroy(bodiesTransform.GetChild(i).gameObject);
            bodiesRoot = (RectTransform)bodiesTransform;
            playerArrow = (RectTransform)transform.Find("PlayerArrow");
        }

        private void Update()
        {
            if (!built)
            {
                var gravity = FindAnyObjectByType<Physics.GravityField>();
                if (gravity == null || gravity.Bodies.Count == 0 || bodiesRoot.rect.width < 50f)
                    return; // ждём мир и рассчитанный layout
                bodies.AddRange(gravity.Bodies);
                pxPerUnit = bodiesRoot.rect.width * 0.5f / worldRange;
                foreach (CelestialBody body in bodies)
                    CreateDot(body);
                targetMark = CreateDot("TargetMark", 14f);
                targetMark.GetComponent<Image>().color = TaxiColor;
                targetMark.gameObject.SetActive(false);
                built = true;
            }

            var ship = FindAnyObjectByType<Physics.Ship>();
            if (ship == null)
                return;
            Vector2 center = ship.transform.position;

            for (int i = 0; i < bodies.Count; i++)
                bodyDots[i].anchoredPosition = (Vector2)bodies[i].transform.position - center;

            // Стрелка по вектору скорости (спрайт остриём вверх).
            Vector2 velocity = ship.Body.linearVelocity;
            if (velocity.sqrMagnitude > 1f)
            {
                float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
                playerArrow.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
            }

            UpdateTarget(center);
        }

        // Цель: планета А (за пассажиром) или Б (на борту), жёлтым.
        private void UpdateTarget(Vector2 center)
        {
            var session = FindAnyObjectByType<GameSession>();
            var orders = session != null ? session.Orders : null;
            OrderOffer active = orders != null ? orders.Active : null;
            if (active == null)
            {
                targetMark.gameObject.SetActive(false);
                return;
            }
            int index = active.State == OrderState.Aboard ? active.DropoffIndex : active.PickupIndex;
            var gravity = FindAnyObjectByType<Physics.GravityField>();
            if (index < 0 || gravity == null)
                return;
            // Индексы заказа — по LevelAsset.Bodies; мир ищем по имени.
            CelestialBody target = null;
            foreach (CelestialBody body in bodies)
                if (body.name == session.Level.Bodies[index].DisplayName)
                    target = body;
            if (target == null)
                return;
            targetMark.gameObject.SetActive(true);
            targetMark.anchoredPosition = (Vector2)target.transform.position - center;
        }

        private RectTransform CreateDot(CelestialBody body)
        {
            bool star = body is Star;
            // Точки крупнее честного масштаба — заметнее движение относительно стрелки.
            float size = Mathf.Max(9f, body.Radius * 2f * pxPerUnit * 1.5f);
            if (star)
            {
                RectTransform halo = CreateDot(body.name + "Halo", size * 2f);
                halo.GetComponent<Image>().color = new Color(StarColor.r, StarColor.g, StarColor.b, 0.25f);
            }
            RectTransform dot = CreateDot(body.name, size);
            dot.GetComponent<Image>().color = star ? StarColor : DotColor;
            bodyDots.Add(dot);
            return dot;
        }

        private RectTransform CreateDot(string name, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(bodiesRoot, false);
            var image = go.GetComponent<Image>();
            image.sprite = circleSprite;
            image.raycastTarget = false;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = Vector2.one * size;
            return rect;
        }
    }
}
