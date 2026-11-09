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
        private List<CelestialBodyData> planetsForBelts; // лениво: планеты уровня для отсева поясов

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

            // пустые топливные сети (Н4.5): один проход ДО создания тел — правило
            // «не двух соседних сразу» видит все заправки уровня сразу
            var fuelOutage = FuelNetwork.OutageStations(level.Bodies, Pricing());

            var bodies = new List<CelestialBody>();
            for (int i = 0; i < level.Bodies.Count; i++)
                bodies.Add(CreateBody(level.Bodies[i], i, fuelOutage.Contains(level.Bodies[i].DisplayName)));

            // кометы (П6.3): одна система на уровень, маршруты по таймеру, не тело гравитации
            var cometGo = new GameObject("CometSystem");
            cometGo.AddComponent<CometSystem>().Init(level);

            ship = CreateShip(bodies);

            Debug.Log($"[World] {level.LevelName}: тел={bodies.Count}, корабль в {ship.transform.position}, состояние={ship.State}");
        }

        // планеты уровня (Type == Planet) — оси поясов проходят через них,
        // без отсева астероид может заспавниться внутри планеты
        private List<CelestialBodyData> Planets()
        {
            if (planetsForBelts == null)
            {
                planetsForBelts = new List<CelestialBodyData>();
                for (int i = 0; i < level.Bodies.Count; i++)
                    if (level.Bodies[i].Type == BodyType.Planet)
                        planetsForBelts.Add(level.Bodies[i]);
            }
            return planetsForBelts;
        }

        // конфиг цен (Н4.5): нужна билдеру ради шанса пустой сети — та же цепочка,
        // что у OrderService.Pricing (Resources → кодовый дефолт); поля сцены нет.
        // ИНВАРИАНТ: сериализованное pricingConfig-поле OrderService в сцене
        // НЕ назначать — иначе мир (сухие сети) и заказы разойдутся по конфигам;
        // билдер сознательно не видит поле сцены
        private static PricingConfig Pricing()
        {
            var fromResources = Resources.Load<PricingConfig>("PricingConfig_Default");
            return fromResources != null ? fromResources : PricingConfig.Default;
        }

        private CelestialBody CreateBody(CelestialBodyData data, int index, bool fuelOutage)
        {
            // пояс — не тело гравитации: в списке заглушка ради индексов level.Bodies
            if (data.Type == BodyType.AsteroidBelt)
            {
                var beltGo = new GameObject(data.DisplayName);
                beltGo.transform.position = data.Position;
                beltGo.AddComponent<AsteroidBeltSystem>().Init(data, config, Planets());
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
            ApplyPlanetArt(renderer, data);

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
            body.Init(data, config, fuelOutage);

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

        // пользовательский арт планет: заправка — планета со станцией по тиру
        // топливной сети (poor/norm/rich/high), остальные — случайный спрайт из 9
        // со случайным цветом (оба стабильны по имени тела). Спрайт из данных
        // LevelAsset (будущий хаб-арт) главнее
        private static void ApplyPlanetArt(SpriteRenderer renderer, CelestialBodyData data)
        {
            if (renderer == null || data.Type != BodyType.Planet)
                return;
            if (data.Visuals != null && data.Visuals.Sprite != null)
                return;

            // хаб (HasFuel + магазин/улучшения) — отдельный арт позже; механика
            // станции сохранена, спрайт заправки на него не вешаем
            if (data.HasFuel && !data.IsHub)
            {
                Sprite gas = WorldSprites.GasStation(data.FuelTier);
                if (gas != null)
                {
                    renderer.sprite = gas;
                    renderer.color = Color.white; // спрайт заправки уже покрашен
                }
                return;
            }

            Sprite planet = WorldSprites.Planet(data.DisplayName);
            if (planet != null)
            {
                renderer.sprite = planet;
                renderer.color = WorldSprites.PlanetTint(data.DisplayName);
            }
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
            if (renderer != null && renderer.sprite == null)
                renderer.sprite = StubSprites.ShipTriangle();

            // визуал модели (Resources/Ships/<Id>): спрайт покраски + слоты сопел.
            // Apply до расчёта позиции — точка спавна зависит от HalfExtent контента
            var modelVisual = go.GetComponent<ShipModelVisual>();
            if (modelVisual == null)
                modelVisual = go.AddComponent<ShipModelVisual>();
            modelVisual.Apply(shipConfig, 0);

            float angleRad = level.SpawnAngleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
            // радиус построенного тела, а не сырых данных уровня: облик планеты
            // умножен на PlanetSizeMul (CelestialBody.Init), спавн от start.Radius
            // оставлял крупный корпус хвостом в планете, солвер выталкивал;
            // фолбэк — сырой радиус, если тела нет (пояс-заглушка)
            CelestialBody startBody = level.StartPlanetIndex >= 0 && level.StartPlanetIndex < bodies.Count
                ? bodies[level.StartPlanetIndex]
                : null;
            float startRadius = startBody != null ? startBody.Radius : start.Radius;
            // корма на поверхности планеты; ShipOffset — фолбэк без визуала
            float spawnOffset = modelVisual.HasVisual ? ShipPlacement.LandedExtent(modelVisual) : ShipOffset;
            float nose = controller != null ? controller.SpriteNoseAngle : 0f;
            // Body ещё null (Init ниже) — телепорт деградирует до записи
            // transform: корабль неактивен, истории интерполяции нет
            ShipPlacement.TeleportLanded(ship, start.Position + dir * (startRadius + spawnOffset),
                level.SpawnAngleDeg - nose);

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
            if (go.GetComponent<DamageEffects>() == null)
                go.AddComponent<DamageEffects>();

            go.SetActive(true);
            return ship;
        }
    }
}
