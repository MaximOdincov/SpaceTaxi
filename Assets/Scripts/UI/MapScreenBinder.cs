using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Orders;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Карта системы (M): строится КОДОМ из LevelAsset (не нарисованная копия),
    // живой маркер корабля, цель заказа жёлтым кольцом, станции помечены.
    // Вешается на MapScreen/Backing, статичную заготовку удаляет.
    public class MapScreenBinder : MonoBehaviour
    {
        private LevelAsset level;
        private RectTransform mapRect;
        private TMP_FontAsset font;
        private RectTransform playerMarker;
        private RectTransform targetRing;
        private readonly Dictionary<CelestialBodyData, RectTransform> markers = new Dictionary<CelestialBodyData, RectTransform>();
        private Vector2 worldCenter;
        private float scale;

        private static readonly Color LabelColor = new Color(0.357f, 0.788f, 0.561f);
        private static readonly Color TaxiColor = new Color(1f, 0.831f, 0.302f);

        private void Awake()
        {
            mapRect = (RectTransform)transform;

            // Заготовку из сцены убираем — карту строим из данных.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                string name = transform.GetChild(i).name;
                if (name != "Frame" && name != "Background" && name != "Title")
                    Destroy(transform.GetChild(i).gameObject);
            }

            font = transform.Find("Title").GetComponent<TMP_Text>().font;
        }

        private void Update()
        {
            if (level == null)
            {
                var session = FindAnyObjectByType<GameSession>();
                if (session == null || session.Level == null)
                    return;
                level = session.Level;
                BuildMap();
            }

            var ship = FindAnyObjectByType<Physics.Ship>();
            if (ship != null && playerMarker != null)
                playerMarker.anchoredPosition = WorldToMap(ship.transform.position);

            UpdateTargetRing();
        }

        private void BuildMap()
        {
            // Границы мира -> вписываем в подложку с полем.
            var bounds = new Bounds();
            bool first = true;
            foreach (CelestialBodyData body in level.Bodies)
            {
                var b = new Bounds(body.Position, Vector2.one * body.Radius * 2f);
                if (first)
                {
                    bounds = b;
                    first = false;
                }
                else
                    bounds.Encapsulate(b);
            }
            bounds.Expand(200f);
            worldCenter = bounds.center;
            scale = Mathf.Min(mapRect.rect.width / bounds.size.x, mapRect.rect.height / bounds.size.y);

            foreach (CelestialBodyData body in level.Bodies)
                CreateBody(body);

            playerMarker = CreateCircle("PlayerMarker", 12f, Color.white, 100);
        }

        private void CreateBody(CelestialBodyData body)
        {
            bool star = body.Type == BodyType.Star;
            float size = Mathf.Max(10f, body.Radius * 2f * scale);

            if (star)
                CreateCircle(body.DisplayName + "Halo", size * 2.1f, new Color(1f, 0.8f, 0.3f, 0.22f));

            RectTransform circle = CreateCircle(body.DisplayName, size,
                star ? new Color(1f, 0.82f, 0.35f)
                     : body.Visuals != null && body.Visuals.UseColor ? body.Visuals.Color : new Color(0.55f, 0.62f, 0.72f));
            circle.anchoredPosition = WorldToMap(body.Position);
            markers[body] = circle;

            // Станция: жёлтая точка на краю.
            if (body.HasStation)
            {
                RectTransform station = CreateCircle(body.DisplayName + "Station", 10f, TaxiColor);
                station.SetParent(circle, false);
                station.anchoredPosition = new Vector2(size * 0.5f, size * 0.5f);
            }

            var labelGo = new GameObject(body.DisplayName + "Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(transform, false);
            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = 16f;
            label.color = LabelColor;
            label.text = body.DisplayName.ToUpper();
            label.alignment = TextAlignmentOptions.Center;
            ((RectTransform)labelGo.transform).anchoredPosition = WorldToMap(body.Position) + Vector2.up * (size * 0.5f + 18f);
        }

        // Кольцо цели: планета А (за пассажиром) или Б (на борту).
        private void UpdateTargetRing()
        {
            var session = FindAnyObjectByType<GameSession>();
            var orders = session != null ? session.Orders : null;
            OrderOffer active = orders != null ? orders.Active : null;

            if (active == null)
            {
                if (targetRing != null)
                    targetRing.gameObject.SetActive(false);
                return;
            }

            int index = active.State == OrderState.Aboard ? active.DropoffIndex : active.PickupIndex;
            if (index < 0 || index >= level.Bodies.Count)
                return;
            if (targetRing == null)
                targetRing = CreateRing("TargetRing", 40f, TaxiColor);

            CelestialBodyData body = level.Bodies[index];
            targetRing.gameObject.SetActive(true);
            targetRing.anchoredPosition = WorldToMap(body.Position);
            float pulse = 1f + 0.15f * Mathf.Sin(Time.unscaledTime * 3f);
            targetRing.sizeDelta = Vector2.one * (Mathf.Max(40f, body.Radius * 2f * scale) + 22f) * pulse;
        }

        private Vector2 WorldToMap(Vector2 world)
        {
            return (world - worldCenter) * scale;
        }

        private RectTransform CreateCircle(string name, float size, Color color, int siblingIndex = -1)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var image = go.GetComponent<Image>();
            image.sprite = circleSprite;
            image.color = color;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = Vector2.one * size;
            if (siblingIndex >= 0)
                rect.SetSiblingIndex(siblingIndex);
            return rect;
        }

        private RectTransform CreateRing(string name, float size, Color color)
        {
            // Кольцо-заглушка: жёлтый ореол с точкой (честное кольцо — с арт-паком).
            RectTransform halo = CreateCircle(name, size, new Color(color.r, color.g, color.b, 0.25f));
            RectTransform dot = CreateCircle(name + "Dot", 8f, color);
            dot.SetParent(halo, false);
            dot.anchoredPosition = Vector2.zero;
            return halo;
        }

        // Круглый спрайт точек (planet_stub), назначается в инспекторе.
        [SerializeField] private Sprite circleSprite;
    }
}
