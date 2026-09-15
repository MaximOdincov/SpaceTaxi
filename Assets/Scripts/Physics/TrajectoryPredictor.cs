using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.World;
using SpaceTaxi.Core;

namespace SpaceTaxi.Physics
{
    // Пунктирный прогноз траектории: свободный полёт под гравитацией всех тел.
    // Длина зависит от скорости (горизонт в секундах), линия изгибается и
    // обрывается на столкновении. Вид полностью настраивается в инспекторе.
    public class TrajectoryPredictor : MonoBehaviour
    {
        [Header("Вид пунктира")]
        [SerializeField] private Sprite dashSprite; // чёрточка (пусто = заглушка)
        [SerializeField] private Color dashColor = new Color(1f, 1f, 1f, 0.6f);
        [SerializeField] private float dashScale = 0.25f; // чёрточка ~4 x 0.5 юнита
        [SerializeField] private float spacing = 10f;     // юнитов между чёрточками
        [Tooltip("Насколько тускнеет линия к концу: 0 — не тускнеет, 1 — исчезает")]
        [SerializeField, Range(0f, 1f)] private float endFade = 0.6f;

        [Header("Привязка")]
        [Tooltip("Отступ старта линии от центра корабля — из носа, чуть под корпусом")]
        [SerializeField] private float noseOffset = 6f;

        [Header("Симуляция")]
        [SerializeField] private float horizonSeconds = 4f;
        [SerializeField] private float step = 1f / 120f;
        [SerializeField] private int maxDashes = 64;

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

            maxDashes = Mathf.Max(4, maxDashes); // защита от курьёзов в инспекторе

            for (int i = 0; i < maxDashes; i++)
            {
                var go = new GameObject("Dash");
                var r = go.AddComponent<SpriteRenderer>();
                r.sprite = dashSprite != null ? dashSprite : StubSprites.Line();
                r.color = dashColor;
                r.sortingOrder = 1;
                go.transform.localScale = new Vector3(dashScale, dashScale, 1f);
                go.SetActive(false);
                dashes.Add(go.transform);
                renderers.Add(r);
            }
        }

        private void Update()
        {
            if (ship == null || ship.Config == null || gravity == null
                || ship.State != ShipState.Flying
                || ship.Body.linearVelocity.sqrMagnitude < 1f)
            {
                HideAll();
                return;
            }
            Simulate();
        }

        // Линия «приклеена» к носу: стартует из носа и направлена по носу
        // (нос сам плавно доворачивается к скорости), величина — фактическая скорость.
        private Vector2 StartVelocity(out Vector2 startPos)
        {
            float speed = ship.Body.linearVelocity.magnitude;
            float noseAngle = (ship.transform.eulerAngles.z
                + (controller != null ? controller.SpriteNoseAngle : 0f)) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(noseAngle), Mathf.Sin(noseAngle));
            startPos = (Vector2)ship.transform.position + dir * noseOffset;
            return dir * speed;
        }

        // Свободный полёт той же формулой, что в ShipController, без тяги.
        private void Simulate()
        {
            Vector2 pos;
            Vector2 vel = StartVelocity(out pos);
            var config = ship.Config;

            // Шаг пунктира растёт, если линии не хватает на весь горизонт
            // (иначе длина упирается в пул чёрточек).
            float spacingNow = Mathf.Max(spacing, vel.magnitude * horizonSeconds / maxDashes);

            float travelled = 0f; // путь с последней чёрточки
            float total = 0f;     // путь от корабля (для затухания)
            int dashIndex = 0;
            bool crashed = false;

            int steps = Mathf.CeilToInt(horizonSeconds / step);
            for (int i = 0; i < steps && !crashed; i++)
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
                        crashed = true;
                        break;
                    }
                }

                if (travelled >= spacingNow && dashIndex < maxDashes)
                {
                    PlaceDash(dashIndex++, next, vel, total, spacingNow);
                    travelled = 0f;
                }

                pos = next;
            }

            for (int i = dashIndex; i < dashes.Count; i++)
                dashes[i].gameObject.SetActive(false);
        }

        private void PlaceDash(int index, Vector2 pos, Vector2 vel, float total, float spacingNow)
        {
            Transform t = dashes[index];
            t.gameObject.SetActive(true);
            t.SetPositionAndRotation(pos,
                Quaternion.Euler(0f, 0f, Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg));

            // Плавное затухание к концу линии.
            float k = Mathf.Clamp01(total / (spacingNow * maxDashes));
            Color c = dashColor;
            c.a *= Mathf.Lerp(1f, 1f - endFade, k);
            renderers[index].color = c;
        }

        private void HideAll()
        {
            foreach (Transform dash in dashes)
                dash.gameObject.SetActive(false);
        }
    }
}
