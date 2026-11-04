using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.Core;
using SpaceTaxi.Physics;
using SpaceTaxi.Audio;
using SpaceTaxi.Economy;

namespace SpaceTaxi.World
{
    public sealed class WorldBuilder
    {
        private const float ShipOffset = 7f;

        private readonly LevelAsset level;
        private readonly PhysicsConfig config;
        private readonly InputActionAsset input;
        private readonly Planet planetPrefab;
        private readonly Ship shipPrefab;

        private readonly ShipConfig shipConfig;

        public WorldBuilder(LevelAsset level, PhysicsConfig config, ShipConfig shipConfig, InputActionAsset input,
            Planet planetPrefab = null, Ship shipPrefab = null)
        {
            this.level = level;
            this.config = config;
            this.shipConfig = shipConfig;
            this.input = input;
            this.planetPrefab = planetPrefab;
            this.shipPrefab = shipPrefab;
        }

        public void Build(out Ship ship, out GravityField gravity)
        {
            gravity = new GameObject("GravityField").AddComponent<GravityField>();

            var bodies = new List<CelestialBody>();
            for (int i = 0; i < level.Bodies.Count; i++)
                bodies.Add(CreateBody(level.Bodies[i], i));

            // кометы (П6.3): одна система на уровень, маршруты по таймеру, не тело гравитации
            var cometGo = new GameObject("CometSystem");
            cometGo.AddComponent<CometSystem>().Init(level);

            ship = CreateShip(bodies);

            Debug.Log($"[World] {level.LevelName}: тел={bodies.Count}, корабль в {ship.transform.position}, состояние={ship.State}");
        }

        private CelestialBody CreateBody(CelestialBodyData data, int index)
        {
            // пояс — не тело гравитации: в списке заглушка ради индексов level.Bodies
            if (data.Type == BodyType.AsteroidBelt)
            {
                var beltGo = new GameObject(data.DisplayName);
                beltGo.transform.position = data.Position;
                beltGo.AddComponent<AsteroidBeltSystem>().Init(data, config);
                return null;
            }

            GameObject go;
            if (data.Type == BodyType.Planet && planetPrefab != null)
            {
                go = Object.Instantiate(planetPrefab.gameObject, data.Position, Quaternion.identity);
                go.name = data.DisplayName;
            }
            else
            {
                go = new GameObject(data.DisplayName);
                go.transform.position = data.Position;

                var stubRenderer = go.AddComponent<SpriteRenderer>();
                stubRenderer.sprite = StubSprites.Circle(data.Radius * 2f);
                // звезда — тёплая заглушка, дыра — тёмное ядро (UseColor перекрасит)
                stubRenderer.color = data.Type == BodyType.BlackHole ? BlackHole.CoreColor : new Color(1f, 0.82f, 0.35f);
                stubRenderer.sortingOrder = 1;
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
            else if (data.Type == BodyType.BlackHole)
            {
                body = go.AddComponent<BlackHole>();
            }
            else
            {
                body = go.GetComponent<Planet>();
                if (body == null)
                    body = go.AddComponent<Planet>();
            }
            body.Init(data, config);

            if (data.Type == BodyType.Star && data.HeatRadius > 0f)
            {
                var heatGo = new GameObject("HeatZone");
                heatGo.transform.SetParent(go.transform, false);
                var heatRenderer = heatGo.AddComponent<SpriteRenderer>();
                heatRenderer.sprite = StubSprites.Circle(data.HeatRadius * 2f);
                heatRenderer.color = new Color(1f, 0.45f, 0.2f, 0.07f);
                heatRenderer.sortingOrder = -1;
            }

            // ореол зоны сжатия дыры — по образцу HeatZone
            if (body is BlackHole hole)
            {
                var zoneGo = new GameObject("EffectZone");
                zoneGo.transform.SetParent(go.transform, false);
                var zoneRenderer = zoneGo.AddComponent<SpriteRenderer>();
                zoneRenderer.sprite = StubSprites.Circle(hole.EffectRadius * 2f);
                zoneRenderer.color = BlackHole.ZoneColor;
                zoneRenderer.sortingOrder = -1;
            }

            return body;
        }

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
                go.SetActive(false);

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
            if (renderer != null)
            {
                // спрайт модели из ShipConfig (арт А1/А2); пусто — что было
                // в префабе, в последнюю очередь — заглушка-треугольник
                if (shipConfig != null && shipConfig.ShipSprite != null)
                    renderer.sprite = shipConfig.ShipSprite;
                else if (renderer.sprite == null)
                    renderer.sprite = StubSprites.ShipTriangle();
            }

            float angleRad = level.SpawnAngleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
            go.transform.position = start.Position + dir * (start.Radius + ShipOffset);
            float nose = controller != null ? controller.SpriteNoseAngle : 0f;
            go.transform.rotation = Quaternion.Euler(0f, 0f, level.SpawnAngleDeg - nose);

            ship.Init(config, shipConfig);
            ship.State = ShipState.Landed;
            ship.LandedOn = bodies[level.StartPlanetIndex] as Planet;

            if (controller != null)
                controller.SetInput(input);
            if (shipAudio != null)
                shipAudio.SetInput(input);
            var hull = go.GetComponent<HullSystem>();
            if (hull != null)
                hull.Init(level);
            else
                go.AddComponent<HullSystem>().Init(level);

            if (go.GetComponent<EngineEffects>() == null)
                go.AddComponent<EngineEffects>();
            if (go.GetComponent<SpeedTrail>() == null)
                go.AddComponent<SpeedTrail>();

            go.SetActive(true);
            return ship;
        }
    }
}
