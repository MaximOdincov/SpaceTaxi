using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.Audio;
using SpaceTaxi.Core;
using SpaceTaxi.Physics;
using SpaceTaxi.UI;

namespace SpaceTaxi.World
{
    // Пояс астероидов: рой мелких тел вокруг осевой линии, вся система вращается вокруг центра.
    // В GravityField не регистрируется (без гравитации), урон — на принимающей стороне.
    public class AsteroidBeltSystem : MonoBehaviour
    {
        // числа §3.8 — не менять
        private const int MinAsteroids = 24;
        private const int MaxAsteroids = 40;
        private const float MinAsteroidRadius = 4f;
        private const float MaxAsteroidRadius = 10f;

        private const float ToastZoneMargin = 20f;  // предупреждение на подлёте к поясу
        private const float ToastResetMargin = 50f; // флаг сбрасывается дальше зоны (повторный вход = новый тост)

        // константа движения (решение координатора, не баланс §3.8)
        [Tooltip("Угловая скорость вращения пояса, рад/с")]
        public float BeltOrbitSpeed = 0.12f;

        public static readonly Color AsteroidColor = new Color(0.48f, 0.42f, 0.36f); // серо-коричневый

        private const float PlanetClearMargin = 8f;    // зазор планета–астероид сверх радиусов
        private const int RejectionAttempts = 12;      // rejection sampling до N попыток
        private const float AngleShiftStepDeg = 5f;    // сдвиг угла после неудачных попыток

        private readonly struct PlanetGuard
        {
            public readonly Vector2 Position;
            public readonly float Radius;

            public PlanetGuard(Vector2 position, float radius)
            {
                Position = position;
                Radius = radius;
            }
        }

        private float axisRadius;
        private float halfWidth;
        private Color asteroidColor = AsteroidColor;
        private Ship ship;
        private ToastNotifier toast;
        private bool warned;
        private readonly List<PlanetGuard> planetGuards = new List<PlanetGuard>();

        public void Init(CelestialBodyData data, PhysicsConfig config, List<CelestialBodyData> planets = null)
        {
            axisRadius = data.Radius;
            halfWidth = Mathf.Max(0f, data.BeltWidth) * 0.5f;
            if (data.Visuals != null && data.Visuals.UseColor)
                asteroidColor = data.Visuals.Color;

            planetGuards.Clear();
            if (planets != null)
            {
                for (int i = 0; i < planets.Count; i++)
                    planetGuards.Add(new PlanetGuard(planets[i].Position, planets[i].Radius));
            }

            int count = Mathf.Clamp(data.BeltAsteroidCount, MinAsteroids, MaxAsteroids);
            for (int i = 0; i < count; i++)
                SpawnAsteroid();
        }

        private void SpawnAsteroid()
        {
            var go = new GameObject("Asteroid");
            go.transform.SetParent(transform, false);

            float radius = Random.Range(MinAsteroidRadius, MaxAsteroidRadius);
            float distance = Random.Range(axisRadius - halfWidth, axisRadius + halfWidth);
            float angle = PickClearAngle(radius, distance);
            go.transform.localPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;

            var collider = go.AddComponent<CircleCollider2D>();
            collider.radius = radius; // физический, не триггер

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = StubSprites.Circle(radius * 2f);
            renderer.color = asteroidColor;
            renderer.sortingOrder = 1;

            go.AddComponent<Asteroid>().Init(radius);
        }

        // отсев планет на оси (П7.1): rejection sampling до N попыток, затем сдвиг
        // угла по кругу; перекрыт весь круг (планета накрывает пояс) — ставим как есть
        private float PickClearAngle(float asteroidRadius, float distance)
        {
            if (planetGuards.Count == 0)
                return Random.Range(0f, Mathf.PI * 2f);

            for (int attempt = 0; attempt < RejectionAttempts; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                if (AngleClear(angle, distance, asteroidRadius))
                    return angle;
            }

            float fallback = Random.Range(0f, Mathf.PI * 2f);
            for (float shift = 0f; shift < 360f; shift += AngleShiftStepDeg)
            {
                float angle = fallback + shift * Mathf.Deg2Rad;
                if (AngleClear(angle, distance, asteroidRadius))
                    return angle;
            }
            return fallback;
        }

        private bool AngleClear(float angle, float distance, float asteroidRadius)
        {
            // на момент спавна пояс не повёрнут, позиция в мире = центр пояса + локальная точка
            Vector2 world = (Vector2)transform.position
                            + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            for (int i = 0; i < planetGuards.Count; i++)
            {
                float clear = planetGuards[i].Radius + asteroidRadius + PlanetClearMargin;
                if ((world - planetGuards[i].Position).sqrMagnitude < clear * clear)
                    return false;
            }
            return true;
        }

        private void Update()
        {
            // весь рой крутится вместе с родителем вокруг центра пояса
            transform.Rotate(0f, 0f, BeltOrbitSpeed * Time.deltaTime);
            WatchWarningZone();
        }

        private void WatchWarningZone()
        {
            if (ship == null)
            {
                ship = FindAnyObjectByType<Ship>();
                if (ship == null)
                    return;
            }

            // кольцевая зона: пояс — кольцо вокруг центра, не диск (иначе тост
            // срабатывает в пустой середине и не перезводится до вылета из мира)
            float ringDistance = Mathf.Abs(Vector2.Distance(ship.transform.position, transform.position) - axisRadius);
            float zone = halfWidth + ToastZoneMargin;

            if (!warned && ringDistance <= zone)
            {
                warned = true;
                Sound.StartLoop(SoundId.BeltRumble); // стуки пояса — вход/выход по той же зоне, идемпотентно
                if (toast == null)
                    toast = FindAnyObjectByType<ToastNotifier>();
                if (toast != null)
                    toast.Show("Пояс астероидов: маневрируй аккуратно");
            }
            else if (warned && ringDistance > zone + ToastResetMargin)
            {
                warned = false;
                Sound.StopLoop(SoundId.BeltRumble);
            }
        }

        // сцена может выгрузиться внутри пояса — луп живёт в AudioManager, глушим
        private void OnDisable()
        {
            Sound.StopLoop(SoundId.BeltRumble);
        }
    }
}
