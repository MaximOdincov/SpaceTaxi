using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Миникарта-радар: локальный фрагмент мира вокруг корабля (~1000 юнитов),
    // тела белыми точками (звезда с ореолом), стрелка игрока по вектору
    // скорости, цель заказа жёлтым. Вешается на HUD/Minimap.
    public class MinimapBinder : MonoBehaviour
    {
        [Tooltip("Радиус охвата минимапа в юнитах мира (примерно половина видимой области камеры)")]
        public float worldRange = 350f;

        [SerializeField] private Sprite circleSprite;

        private RectTransform bodiesRoot;
        private RectTransform playerArrow;
        private readonly List<RectTransform> bodyDots = new List<RectTransform>();
        private readonly List<CelestialBody> bodies = new List<CelestialBody>();

        private bool built;
        private float pxPerUnit;

        private static readonly Color DotColor = new Color(0.85f, 1f, 0.9f);
        private static readonly Color StarColor = new Color(1f, 0.82f, 0.35f);

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
        }


        private RectTransform CreateDot(CelestialBody body)
        {
            bool star = body is Star;
            // Честный масштаб: на экране планета — значит и на радаре; посадка
            // на планету оставляет её круг вплотную к стрелке корабля.
            float size = Mathf.Max(7f, body.Radius * 2f * pxPerUnit);

            RectTransform root;
            if (star)
            {
                root = CreateDot(body.name + "Halo", size * 1.3f);
                root.GetComponent<Image>().color = new Color(StarColor.r, StarColor.g, StarColor.b, 0.18f);

                RectTransform core = CreateDot(body.name, size);
                core.SetParent(root, false);
                core.anchoredPosition = Vector2.zero;
                core.GetComponent<Image>().color = StarColor;
            }
            else
            {
                root = CreateDot(body.name, size);
                root.GetComponent<Image>().color = DotColor;
            }

            bodyDots.Add(root);
            return root;
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
