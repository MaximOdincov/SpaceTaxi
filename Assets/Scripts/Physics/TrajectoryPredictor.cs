using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.World;
using SpaceTaxi.Core;

namespace SpaceTaxi.Physics
{
    public class TrajectoryPredictor : MonoBehaviour
    {
        [Header("Вид пунктира")]
        [SerializeField] private Sprite dashSprite;
        [SerializeField] private Color dashColor = new Color(1f, 1f, 1f, 0.6f);
        [SerializeField] private float dashScale = 0.4f;
        [Tooltip("Множитель ширины чёрточки: 1 = как в спрайте, меньше = тоньше")]
        [SerializeField, Range(0.1f, 2f)] private float dashWidth = 0.6f;
        [SerializeField] private float spacing = 12f;    // юнитов между чёрточками
        [Tooltip("Насколько тускнеет линия к концу: 0 — не тускнеет, 1 — исчезает")]
        [SerializeField, Range(0f, 1f)] private float endFade = 0.6f;

        [Header("Привязка")]
        [Tooltip("Отступ старта линии от центра корабля — из носа, чуть под корпусом")]
        [SerializeField] private float noseOffset = 6f;

        [Header("Симуляция")]
        [SerializeField] private float step = 1f / 120f;
        [SerializeField] private int maxDashes = 64;

        // Горизонт адаптивный: ~1.2 экрана вперёд (от 2 до 4 секунд), опорная
        // ширина FIXED (не от живого зума камеры — зум «дышит» и линия дёргалась),
        // само значение сглажено 4/с. Короткий горизонт ещё и убирает «хлёст»
        // конца линии: чувствительность к начальным условиям растёт с временем².
        private float horizon = 3f;
        private float refWidth = 560f;
        private const float HorizonMin = 2f;
        private const float HorizonMax = 4f;
        private const float HorizonScreens = 1.2f;

        // Исход траектории: зелёная — коснёшься планеты ниже порога посадки
        // (цвет как у скорости в HUD). Красного сознательно нет: линия к
        // планете краснеет почти всегда — шумит. Опасность видят цифра
        // скорости и красный цвет в посадочном режиме.
        private static readonly Color LineSafe = new Color(0.235f, 1f, 0.557f);

        private Ship ship;
        private ShipController controller;
        private GravityField gravity;
        private readonly List<Transform> dashes = new List<Transform>();
        private readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();

        private void Awake()
        {
            ship = GetComponent<Ship>();
            controller = GetComponent<ShipController>();
            gravity = FindAnyObjectByType<GravityField>();

            maxDashes = Mathf.Max(4, maxDashes);

            for (int i = 0; i < maxDashes; i++)
            {
                var go = new GameObject("Dash");
                var r = go.AddComponent<SpriteRenderer>();
                r.sprite = dashSprite != null ? dashSprite : StubSprites.Line();
                r.color = dashColor;
                r.sortingOrder = 1;
                go.transform.localScale = new Vector3(dashScale, dashScale * dashWidth, 1f);
                go.SetActive(false);
                dashes.Add(go.transform);
                renderers.Add(r);
            }
        }

        private void Start()
        {
            // Опорная ширина экрана — константа на сессию: aspect камеры при
            // полёте 320 высоты. Живой зум (320–470) менять её не должен.
            var mainCam = Camera.main;
            if (mainCam != null)
                refWidth = 2f * 160f * mainCam.aspect;
        }

        private void Update()
        {
            // Скрываем: не в полёте, посадочные режимы (ретро/финал — дэш там
            // только мешает и лезет из «задней» точки), околонулевая скорость.
            if (ship == null || ship.Config == null || gravity == null
                || ship.State != ShipState.Flying
                || (controller != null && controller.LandingRetro)
                || ship.Body.linearVelocity.sqrMagnitude < 900f) // < 30 ед/с
            {
                HideAll();
                smoothDir = Vector2.zero;
                smoothSpacing = 0f;
                return;
            }

            // Горизонт по скорости: ~экран вперёд, сглаженно — без рывков
            // при разгоне/торможении и смене зума.
            float speed = ship.Body.linearVelocity.magnitude;
            float target = Mathf.Clamp(HorizonScreens * refWidth / Mathf.Max(speed, 1f), HorizonMin, HorizonMax);
            horizon += (target - horizon) * Mathf.Min(1f, 4f * Time.deltaTime);

            Simulate();
        }

        private Vector2 smoothDir;
        private float smoothSpacing;

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        // Направление прогноза: быстро сглаженное к вектору скорости — линия
        // стабильна и не дёргается от доворотов носа; старт — от носа корабля.
        private Vector2 StartVelocity(out Vector2 startPos)
        {
            Vector2 velocity = ship.Body.linearVelocity;
            float speed = velocity.magnitude;

            float rate = Mathf.Min(1f, 10f * Time.deltaTime); // сглаживание 10/с
            Vector2 target = velocity / Mathf.Max(speed, 0.001f);
            if (smoothDir == Vector2.zero)
                smoothDir = target;
            float turn = Mathf.Clamp(Vector2.SignedAngle(smoothDir, target), -720f * rate, 720f * rate);
            smoothDir = Rotate(smoothDir, turn).normalized;

            float noseAngle = (ship.transform.eulerAngles.z
                + (controller != null ? controller.SpriteNoseAngle : 0f)) * Mathf.Deg2Rad;
            Vector2 noseDir = new Vector2(Mathf.Cos(noseAngle), Mathf.Sin(noseAngle));
            startPos = (Vector2)ship.transform.position + noseDir * noseOffset;
            return smoothDir * speed;
        }

        private void Simulate()
        {
            Vector2 pos;
            Vector2 vel = StartVelocity(out pos);
            var config = ship.Config;

            float targetSpacing = Mathf.Max(spacing, vel.magnitude * horizon / dashes.Count);
            if (smoothSpacing <= 0f)
                smoothSpacing = targetSpacing;
            smoothSpacing = Mathf.Lerp(smoothSpacing, targetSpacing, Mathf.Min(1f, 6f * Time.deltaTime));
            float spacingNow = smoothSpacing;

            float travelled = spacingNow - 2f; 
            float total = 0f;     
            int dashIndex = 0;
            bool contact = false;
            bool contactPlanet = false;
            float contactSpeed = 0f;

            int steps = Mathf.CeilToInt(horizon / step);
            for (int i = 0; i < steps && !contact; i++)
            {
                vel += gravity.GetAccelerationAt(pos, config) * step;
                Vector2 next = pos + vel * step;
                float seg = Vector2.Distance(pos, next);
                travelled += seg;
                total += seg;

                foreach (CelestialBody body in gravity.Bodies)
                {
                    if (Vector2.Distance(body.transform.position, next) < body.Radius + 4f)
                    {
                        contact = true;
                        contactPlanet = body is Planet;
                        contactSpeed = vel.magnitude;
                        break;
                    }
                }

                if (travelled >= spacingNow && dashIndex < dashes.Count)
                {
                    PlaceDash(dashIndex++, next, vel, total, spacingNow);
                    travelled = 0f;
                }

                pos = next;
            }

            for (int i = dashIndex; i < dashes.Count; i++)
                dashes[i].gameObject.SetActive(false);

            // Цвет всей линии: зелёная — будет мягкое касание планеты,
            // во всех остальных случаях (удар, звезда, чистый полёт) — обычная.
            Color lineColor = contact && contactPlanet && contactSpeed < config.LandSpeed
                ? LineSafe
                : dashColor;
            for (int i = 0; i < dashIndex; i++)
            {
                Color c = lineColor;
                c.a *= Mathf.Lerp(1f, 1f - endFade, i / (float)dashes.Count);
                renderers[i].color = c;
            }
        }

        private void PlaceDash(int index, Vector2 pos, Vector2 vel, float total, float spacingNow)
        {
            Transform t = dashes[index];
            t.gameObject.SetActive(true);
            t.SetPositionAndRotation(pos,
                Quaternion.Euler(0f, 0f, Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg));
            // Цвет назначается после симуляции — по исходу траектории.
        }

        private void HideAll()
        {
            foreach (Transform dash in dashes)
                dash.gameObject.SetActive(false);
        }
    }
}
