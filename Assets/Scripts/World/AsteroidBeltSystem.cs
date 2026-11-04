using UnityEngine;
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

        private float axisRadius;
        private float halfWidth;
        private Color asteroidColor = AsteroidColor;
        private Ship ship;
        private ToastNotifier toast;
        private bool warned;

        public void Init(CelestialBodyData data, PhysicsConfig config)
        {
            axisRadius = data.Radius;
            halfWidth = Mathf.Max(0f, data.BeltWidth) * 0.5f;
            if (data.Visuals != null && data.Visuals.UseColor)
                asteroidColor = data.Visuals.Color;

            int count = Mathf.Clamp(data.BeltAsteroidCount, MinAsteroids, MaxAsteroids);
            for (int i = 0; i < count; i++)
                SpawnAsteroid();
        }

        private void SpawnAsteroid()
        {
            var go = new GameObject("Asteroid");
            go.transform.SetParent(transform, false);

            float radius = Random.Range(MinAsteroidRadius, MaxAsteroidRadius);
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float distance = Random.Range(axisRadius - halfWidth, axisRadius + halfWidth);
            go.transform.localPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;

            var collider = go.AddComponent<CircleCollider2D>();
            collider.radius = radius; // физический, не триггер

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = StubSprites.Circle(radius * 2f);
            renderer.color = asteroidColor;
            renderer.sortingOrder = 1;

            go.AddComponent<Asteroid>().Init(radius);
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
                if (toast == null)
                    toast = FindAnyObjectByType<ToastNotifier>();
                if (toast != null)
                    toast.Show("Пояс астероидов: маневрируй аккуратно");
            }
            else if (warned && ringDistance > zone + ToastResetMargin)
            {
                warned = false;
            }
        }
    }
}
