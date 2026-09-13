using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.Core;
using SpaceTaxi.Physics;
using SpaceTaxi.Audio;

namespace SpaceTaxi.World
{
    // Строит мир из LevelAsset: тела, жар-зоны, корабль. Вызывается GameSession-ом.
    public sealed class WorldBuilder
    {
        // Отступ корабля от поверхности при спавне (09_LEVEL_DESIGN §5, вопрос 3).
        private const float ShipOffset = 7f;

        // Цвета планет-заглушек, по кругу.
        private static readonly Color[] PlanetColors =
        {
            new Color(0.55f, 0.62f, 0.72f),
            new Color(0.60f, 0.55f, 0.70f),
            new Color(0.50f, 0.68f, 0.60f),
            new Color(0.70f, 0.62f, 0.50f),
            new Color(0.62f, 0.62f, 0.62f),
            new Color(0.55f, 0.70f, 0.68f),
        };

        private readonly LevelAsset level;
        private readonly PhysicsConfig config;
        private readonly InputActionAsset input;

        public WorldBuilder(LevelAsset level, PhysicsConfig config, InputActionAsset input)
        {
            this.level = level;
            this.config = config;
            this.input = input;
        }

        public void Build(out Ship ship, out GravityField gravity)
        {
            // Гравитация первой — тела регистрируются в ней при включении.
            gravity = new GameObject("GravityField").AddComponent<GravityField>();

            var bodies = new List<CelestialBody>();
            for (int i = 0; i < level.Bodies.Count; i++)
                bodies.Add(CreateBody(level.Bodies[i], i));

            ship = CreateShip(bodies);
        }

        private CelestialBody CreateBody(CelestialBodyData data, int index)
        {
            var go = new GameObject(data.DisplayName);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = StubSprites.Circle(data.Radius * 2f);
            if (data.Type == BodyType.Star)
            {
                renderer.color = new Color(1f, 0.82f, 0.35f);
                renderer.sortingOrder = 1;
            }
            else
            {
                renderer.color = PlanetColors[index % PlanetColors.Length];
            }

            CelestialBody body = data.Type == BodyType.Star
                ? (CelestialBody)go.AddComponent<Star>()
                : go.AddComponent<Planet>();
            body.Init(data, config);

            // Заглушка индикации жаровой зоны.
            if (data.Type == BodyType.Star && data.HeatRadius > 0f)
            {
                var heatGo = new GameObject("HeatZone");
                heatGo.transform.SetParent(go.transform, false);
                var heatRenderer = heatGo.AddComponent<SpriteRenderer>();
                heatRenderer.sprite = StubSprites.Circle(data.HeatRadius * 2f);
                heatRenderer.color = new Color(1f, 0.45f, 0.2f, 0.07f);
                heatRenderer.sortingOrder = -1;
            }

            return body;
        }

        private Ship CreateShip(List<CelestialBody> bodies)
        {
            CelestialBodyData start = level.Bodies[level.StartPlanetIndex];

            var go = new GameObject("Ship");
            go.SetActive(false); // включим после настройки ввода

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = StubSprites.ShipTriangle();
            renderer.color = Color.white;
            renderer.sortingOrder = 2;

            var collider = go.AddComponent<CircleCollider2D>();
            collider.radius = 3f;

            var ship = go.AddComponent<Ship>();
            var controller = go.AddComponent<ShipController>();
            var shipAudio = go.AddComponent<ShipAudio>();
            go.AddComponent<LandingSystem>();
            go.AddComponent<HeatAudio>();

            var rb = ship.GetComponent<Rigidbody2D>();
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            // Позиция на поверхности планеты старта, нос от центра.
            float angleRad = level.SpawnAngleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
            go.transform.position = start.Position + dir * (start.Radius + ShipOffset);
            go.transform.rotation = Quaternion.Euler(0f, 0f, level.SpawnAngleDeg);

            ship.Init(config);
            ship.State = ShipState.Landed;
            ship.LandedOn = bodies[level.StartPlanetIndex] as Planet;

            controller.SetInput(input);
            shipAudio.SetInput(input);

            go.SetActive(true);
            return ship;
        }
    }
}
