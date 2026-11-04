using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Радар: корабль всегда в центре, тела вокруг в масштабе камеры
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
        private static readonly Color HoleCoreColor = new Color(0.1f, 0.08f, 0.14f);
        private static readonly Color HoleHaloColor = new Color(0.55f, 0.35f, 0.85f, 0.25f);
        private static readonly Color CometWarnColor = new Color(1f, 0.42f, 0.36f);

        // маркер приближающейся кометы (П6.3): от события до спавна или по таймеру
        private CometSystem cometSystem;
        private bool cometsBound;
        private RectTransform cometMarker;
        private Vector2 cometEntry;
        private Vector2 cometDirection;
        private float cometWarnUntil;

        private void Awake()
        {
            // статичную заготовку убираем — радар строим из живого мира
            Transform bodiesTransform = transform.Find("Bodies");
            for (int i = bodiesTransform.childCount - 1; i >= 0; i--)
                Destroy(bodiesTransform.GetChild(i).gameObject);
            bodiesRoot = (RectTransform)bodiesTransform;
            playerArrow = (RectTransform)transform.Find("PlayerArrow");
        }

        private void Update()
        {
            BindComets();

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

            // корабль — центр радара
            Vector2 center = ship.transform.position;
            for (int i = 0; i < dots.Count; i++)
                dots[i].anchoredPosition = ((Vector2)bodies[i].transform.position - center) * pxPerUnit;

            // стрелка по вектору скорости (спрайт остриём вверх)
            Vector2 velocity = ship.Body.linearVelocity;
            if (velocity.sqrMagnitude > 1f)
            {
                float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
                playerArrow.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
            }

            UpdateCometMarker();
        }

        private void BindComets()
        {
            if (cometsBound)
                return;
            cometSystem = FindAnyObjectByType<CometSystem>();
            if (cometSystem == null)
                return; // CometSystem ставится WorldBuilder'ом в Start — подождём
            cometSystem.CometIncoming += OnCometIncoming;
            cometSystem.CometSpawned += HideCometMarker;
            cometsBound = true;
        }

        private void OnCometIncoming(Vector2 entry, Vector2 direction)
        {
            cometEntry = entry;
            cometDirection = direction;
            cometWarnUntil = Time.time + cometSystem.WarningSeconds;

            if (cometMarker == null)
            {
                cometMarker = Make("CometMarker", 14f);
                cometMarker.GetComponent<Image>().sprite = StubSprites.ShipTriangle(); // стиль PlayerArrow
                cometMarker.GetComponent<Image>().color = CometWarnColor;
            }
            cometMarker.gameObject.SetActive(true);
        }

        private void HideCometMarker()
        {
            cometWarnUntil = 0f;
            if (cometMarker != null)
                cometMarker.gameObject.SetActive(false);
        }

        private void UpdateCometMarker()
        {
            if (cometMarker == null || !cometMarker.gameObject.activeSelf)
                return;

            // страховка по таймеру на случай пропущенного CometSpawned
            if (Time.time >= cometWarnUntil)
            {
                HideCometMarker();
                return;
            }

            if (!built || ship == null)
                return;

            // направление на точку входа кометы; за пределами радара — кламп к краю
            Vector2 offset = (cometEntry - (Vector2)ship.transform.position) * pxPerUnit;
            float edge = bodiesRoot.rect.width * 0.5f - 8f;
            if (offset.magnitude > edge)
                offset = offset.normalized * edge;
            cometMarker.anchoredPosition = offset;

            float angle = Mathf.Atan2(cometDirection.y, cometDirection.x) * Mathf.Rad2Deg;
            cometMarker.localRotation = Quaternion.Euler(0f, 0f, angle - 90f); // спрайт остриём вверх
        }

        // у звезды ядро — ребёнок полупрозрачного ореола; у дыры — тёмное кольцо
        private RectTransform CreateDot(CelestialBody body)
        {
            bool star = body is Star;
            float size = Mathf.Max(6f, body.Radius * 2f * pxPerUnit);

            if (body is BlackHole)
            {
                RectTransform holeHalo = Make(body.name + "Halo", size * 1.35f);
                holeHalo.GetComponent<Image>().color = HoleHaloColor;
                RectTransform holeCore = Make(body.name, Mathf.Max(4f, size * 0.6f));
                holeCore.SetParent(holeHalo, false);
                holeCore.anchoredPosition = Vector2.zero;
                holeCore.GetComponent<Image>().color = HoleCoreColor;
                return holeHalo;
            }

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
