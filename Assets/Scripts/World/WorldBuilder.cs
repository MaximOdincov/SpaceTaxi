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

        private readonly LevelAsset level;
        private readonly PhysicsConfig config;
        private readonly InputActionAsset input;
        private readonly Planet planetPrefab; // пусто — собрать кодом
        private readonly Ship shipPrefab;     // пусто — собрать кодом

        public WorldBuilder(LevelAsset level, PhysicsConfig config, InputActionAsset input,
            Planet planetPrefab = null, Ship shipPrefab = null)
        {
            this.level = level;
            this.config = config;
            this.input = input;
            this.planetPrefab = planetPrefab;
            this.shipPrefab = shipPrefab;
        }

        public void Build(out Ship ship, out GravityField gravity)
        {
            // Гравитация первой — тела регистрируются в ней при включении.
            gravity = new GameObject("GravityField").AddComponent<GravityField>();

            var bodies = new List<CelestialBody>();
            for (int i = 0; i < level.Bodies.Count; i++)
                bodies.Add(CreateBody(level.Bodies[i], i));

            ship = CreateShip(bodies);

            Debug.Log($"[World] {level.LevelName}: тел={bodies.Count}, корабль в {ship.transform.position}, состояние={ship.State}");
        }

        private CelestialBody CreateBody(CelestialBodyData data, int index)
        {
            // Планета — из префаба (спрайт/цвет задаёт префаб), звезда — кодом.
            // Визуальные override-ы из данных применяются поверх.
            GameObject go;
            if (data.Type != BodyType.Star && planetPrefab != null)
            {
                go = Object.Instantiate(planetPrefab.gameObject, data.Position, Quaternion.identity);
                go.name = data.DisplayName;
            }
            else
            {
                go = new GameObject(data.DisplayName);
                go.transform.position = data.Position;

                var starRenderer = go.AddComponent<SpriteRenderer>();
                starRenderer.sprite = StubSprites.Circle(data.Radius * 2f);
                starRenderer.color = new Color(1f, 0.82f, 0.35f);
                starRenderer.sortingOrder = 1;
            }

            var renderer = go.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = go.AddComponent<SpriteRenderer>();
            ApplyVisuals(renderer, data.Visuals);

            CelestialBody body;
            if (data.Type == BodyType.Star)
            {
                body = go.AddComponent<Star>();
            }
            else
            {
                body = go.GetComponent<Planet>();
                if (body == null)
                    body = go.AddComponent<Planet>();
            }
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

        // Визуальные override-ы тела: спрайт/материал/цвет. Пусто — как в префабе.
        private static void ApplyVisuals(SpriteRenderer renderer, BodyVisuals visuals)
        {
            if (renderer == null || visuals == null)
                return;
            if (visuals.Sprite != null)
                renderer.sprite = visuals.Sprite;
            if (visuals.Material != null)
                renderer.sharedMaterial = visuals.Material;
            if (visuals.UseColor)
                renderer.color = visuals.Color;
        }

        private Ship CreateShip(List<CelestialBody> bodies)
        {
            CelestialBodyData start = level.Bodies[level.StartPlanetIndex];

            GameObject go;
            Ship ship;
            ShipController controller;
            ShipAudio shipAudio;
            if (shipPrefab != null)
            {
                go = Object.Instantiate(shipPrefab.gameObject);
                ship = go.GetComponent<Ship>();
                controller = go.GetComponent<ShipController>();
                shipAudio = go.GetComponent<ShipAudio>();
            }
            else
            {
                go = new GameObject("Ship");
                go.SetActive(false); // включим после настройки ввода

                var spriteRenderer = go.AddComponent<SpriteRenderer>();
                spriteRenderer.sortingOrder = 2;
                var collider = go.AddComponent<CircleCollider2D>();
                collider.radius = 4f;
                ship = go.AddComponent<Ship>();
                controller = go.AddComponent<ShipController>();
                shipAudio = go.AddComponent<ShipAudio>();
                go.AddComponent<LandingSystem>();
                go.AddComponent<HeatAudio>();
                go.AddComponent<TrajectoryPredictor>();

                var rb = ship.GetComponent<Rigidbody2D>();
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            }

            var renderer = go.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite == null)
                renderer.sprite = StubSprites.ShipTriangle();

            // Позиция на поверхности планеты старта, нос от центра.
            float angleRad = level.SpawnAngleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
            go.transform.position = start.Position + dir * (start.Radius + ShipOffset);
            float nose = controller != null ? controller.SpriteNoseAngle : 0f;
            go.transform.rotation = Quaternion.Euler(0f, 0f, level.SpawnAngleDeg - nose);

            ship.Init(config);
            ship.State = ShipState.Landed;
            ship.LandedOn = bodies[level.StartPlanetIndex] as Planet;

            if (controller != null)
                controller.SetInput(input);
            if (shipAudio != null)
                shipAudio.SetInput(input);

            go.SetActive(true);
            return ship;
        }
    }
}
