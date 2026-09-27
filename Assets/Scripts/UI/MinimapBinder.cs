using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Радар: корабль всегда в центре, тела — вокруг в масштабе камеры.
    // Правило: планета видна на экране => видна на радаре.
    // Посадка на планету оставляет её круг вплотную к стрелке корабля.
    public class MinimapBinder : MonoBehaviour
    {
        [Tooltip("Радиус охвата в юнитах мира (примерно половина видимой области камеры)")]
        public float worldRange = 350f;

        [SerializeField] private Sprite circleSprite;

        private RectTransform bodiesRoot;
        private RectTransform playerArrow;
        private readonly List<RectTransform> dots = new List<RectTransform>();
        private readonly List<CelestialBody> bodies = new List<CelestialBody>();

        private bool built;
        private float pxPerUnit;
        private Ship ship;

        private static readonly Color DotColor = new Color(0.85f, 1f, 0.9f);
        private static readonly Color StarColor = new Color(1f, 0.82f, 0.35f);

        private void Awake()
        {
            // Статичную заготовку сцены убираем — радар строим из живого мира.
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
                var gravity = FindAnyObjectByType<GravityField>();
                if (gravity == null || gravity.Bodies.Count == 0 || bodiesRoot.rect.width < 50f)
                    return; // ждём мир и рассчитанный layout

                pxPerUnit = bodiesRoot.rect.width * 0.5f / worldRange;
                foreach (CelestialBody body in gravity.Bodies)
                {
                    bodies.Add(body);
                    dots.Add(CreateDot(body));
                }
                built = true;
            }

            if (ship == null)
                ship = FindAnyObjectByType<Ship>();
            if (ship == null)
                return;

            // Корабль — центр радара; позиция тела = (мир - корабль) * масштаб.
            Vector2 center = ship.transform.position;
            for (int i = 0; i < dots.Count; i++)
                dots[i].anchoredPosition = ((Vector2)bodies[i].transform.position - center) * pxPerUnit;

            // Стрелка по вектору скорости (спрайт остриём вверх).
            Vector2 velocity = ship.Body.linearVelocity;
            if (velocity.sqrMagnitude > 1f)
            {
                float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
                playerArrow.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
            }
        }

        // Точка тела честного масштаба; у звезды ядро — ребёнок полупрозрачного ореола.
        private RectTransform CreateDot(CelestialBody body)
        {
            bool star = body is Star;
            float size = Mathf.Max(6f, body.Radius * 2f * pxPerUnit);

            if (!star)
            {
                RectTransform dot = Make(body.name, size);
                dot.GetComponent<Image>().color = DotColor;
                return dot;
            }

            RectTransform halo = Make(body.name + "Halo", size * 1.35f);
            halo.GetComponent<Image>().color = new Color(StarColor.r, StarColor.g, StarColor.b, 0.18f);
            RectTransform core = Make(body.name, size);
            core.SetParent(halo, false);
            core.anchoredPosition = Vector2.zero;
            core.GetComponent<Image>().color = StarColor;
            return halo;
        }

        private RectTransform Make(string name, float size)
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
