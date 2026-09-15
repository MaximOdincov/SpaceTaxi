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

        [Header("Симуляция")]
        [SerializeField] private float horizonSeconds = 4f;
        [SerializeField] private float step = 1f / 120f;
        [SerializeField] private int maxDashes = 64;
        [Tooltip("Скорость доворота линии за носом корабля, 1/с. 0 — мгновенно по скорости")]
        [SerializeField, Range(0f, 16f)] private float turnSmoothing = 4f;

        private Ship ship;
        private GravityField gravity;
        private Vector2 smoothedVelocity;
        private readonly List<Transform> dashes = new List<Transform>();
        private readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();

        private void Awake()
        {
            ship = GetComponent<Ship>();
            gravity = FindAnyObjectByType<GravityField>();

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

        // Стартовая скорость прогноза: фактическая, но доворачивается плавно —
        // линия «идёт» за носом корабля, а не прыгает.
        private Vector2 StartVelocity(float dt)
        {
            Vector2 vel = ship.Body.linearVelocity;
            if (turnSmoothing <= 0f)
                return vel;
            if (smoothedVelocity.sqrMagnitude < 0.0001f)
            {
                smoothedVelocity = vel;
                return vel;
            }
            Vector2 dir = smoothedVelocity.normalized;
            Vector2 targetDir = vel.normalized;
            float maxAngle = turnSmoothing * dt * Mathf.Rad2Deg;
            float angle = Mathf.Clamp(Vector2.SignedAngle(dir, targetDir), -maxAngle, maxAngle);
            dir = Quaternion.Euler(0f, 0f, angle) * dir;
            smoothedVelocity = dir * vel.magnitude;
            return smoothedVelocity;
        }

        // Свободный полёт той же формулой, что в ShipController, без тяги.
        private void Simulate()
        {
            Vector2 pos = ship.transform.position;
            Vector2 vel = StartVelocity(Time.deltaTime);
            var config = ship.Config;

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

                if (travelled >= spacing && dashIndex < maxDashes)
                {
                    PlaceDash(dashIndex++, next, vel, total);
                    travelled = 0f;
                }

                pos = next;
            }

            for (int i = dashIndex; i < dashes.Count; i++)
                dashes[i].gameObject.SetActive(false);
        }

        private void PlaceDash(int index, Vector2 pos, Vector2 vel, float total)
        {
            Transform t = dashes[index];
            t.gameObject.SetActive(true);
            t.SetPositionAndRotation(pos,
                Quaternion.Euler(0f, 0f, Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg));

            // Плавное затухание к концу линии.
            float k = Mathf.Clamp01(total / (spacing * maxDashes));
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
