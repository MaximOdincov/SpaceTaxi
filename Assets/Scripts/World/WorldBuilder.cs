using System.Collections.Generic; // списки тел
using UnityEngine; // объекты Unity
using UnityEngine.InputSystem; // ассет ввода
using SpaceTaxi.Core; // заглушки спрайтов
using SpaceTaxi.Physics; // физические компоненты
using SpaceTaxi.Audio; // аудио корабля
using SpaceTaxi.Economy; // прайс-конфиг

namespace SpaceTaxi.World
{
    public sealed class WorldBuilder // фабрика мира (plain-класс, не MonoBehaviour)
    {
        private const float ShipOffset = 7f; // фолбэк-отступ корабля от планеты

        private readonly LevelAsset level; // данные уровня
        private readonly PhysicsConfig config; // физический баланс
        private readonly InputActionAsset input; // ассет ввода
        private readonly Planet planetPrefab; // префаб планеты (может отсутствовать)
        private readonly Ship shipPrefab; // префаб корабля (может отсутствовать)

        private readonly ShipConfig shipConfig; // модель стартового корпуса
        private List<CelestialBodyData> planetsForBelts; // лениво: планеты уровня для отсева поясов

        public WorldBuilder(LevelAsset level, PhysicsConfig config, ShipConfig shipConfig, InputActionAsset input,
            Planet planetPrefab = null, Ship shipPrefab = null) // конструктор с данными
        {
            this.level = level; // уровень
            this.config = config; // физика
            this.shipConfig = shipConfig; // корпус
            this.input = input; // ввод
            this.planetPrefab = planetPrefab; // префабы
            this.shipPrefab = shipPrefab;
        }

        public void Build(out Ship ship, out GravityField gravity) // построить мир целиком
        {
            gravity = new GameObject("GravityField").AddComponent<GravityField>(); // реестр гравитации

            // пустые топливные сети (Н4.5): один проход ДО создания тел — правило
            // «не двух соседних сразу» видит все заправки уровня сразу
            var fuelOutage = FuelNetwork.OutageStations(level.Bodies, Pricing()); // какие станции «сухие»

            var bodies = new List<CelestialBody>(); // построенные тела
            for (int i = 0; i < level.Bodies.Count; i++) // по данным уровня
                bodies.Add(CreateBody(level.Bodies[i], i, fuelOutage.Contains(level.Bodies[i].DisplayName))); // создать тело

            // кометы (П6.3): одна система на уровень, маршруты по таймеру, не тело гравитации
            var cometGo = new GameObject("CometSystem"); // объект системы комет
            cometGo.AddComponent<CometSystem>().Init(level); // инициализировать

            ship = CreateShip(bodies); // построить корабль

            Debug.Log($"[World] {level.LevelName}: тел={bodies.Count}, корабль в {ship.transform.position}, состояние={ship.State}"); // журнал сборки
        }

        // планеты уровня (Type == Planet) — оси поясов проходят через них,
        // без отсева астероид может заспавниться внутри планеты
        private List<CelestialBodyData> Planets() // планеты для отсева поясов
        {
            if (planetsForBelts == null) // ещё не собирали
            {
                planetsForBelts = new List<CelestialBodyData>(); // ленивая сборка
                for (int i = 0; i < level.Bodies.Count; i++) // по данным
                    if (level.Bodies[i].Type == BodyType.Planet) // только планеты
                        planetsForBelts.Add(level.Bodies[i]);
            }
            return planetsForBelts;
        }

        // конфиг цен (Н4.5): нужна билдеру ради шанса пустой сети — та же цепочка,
        // что у OrderService.Pricing (Resources → кодовый дефолт); поля сцены нет.
        // ИНВАРИАНТ: сериализованное pricingConfig-поле OrderService в сцене
        // НЕ назначать — иначе мир (сухие сети) и заказы разойдутся по конфигам;
        // билдер сознательно не видит поле сцены
        private static PricingConfig Pricing() // прайс-конфиг для сетей
        {
            var fromResources = Resources.Load<PricingConfig>("PricingConfig_Default"); // из Resources
            return fromResources != null ? fromResources : PricingConfig.Default; // фолбэк — кодовый
        }

        private CelestialBody CreateBody(CelestialBodyData data, int index, bool fuelOutage) // создать тело
        {
            // пояс — не тело гравитации: в списке заглушка ради индексов level.Bodies
            if (data.Type == BodyType.AsteroidBelt) // пояс
            {
                var beltGo = new GameObject(data.DisplayName); // объект пояса
                beltGo.transform.position = data.Position; // центр кольца
                beltGo.AddComponent<AsteroidBeltSystem>().Init(data, config, Planets()); // система пояса
                return null; // заглушка (null) в списке тел
            }

            GameObject go; // объект тела
            if (data.Type == BodyType.Planet && planetPrefab != null) // планета с префабом
            {
                go = Object.Instantiate(planetPrefab.gameObject, data.Position, Quaternion.identity); // инстанс
                go.name = data.DisplayName; // имя тела
            }
            else // кодовая сборка
            {
                go = new GameObject(data.DisplayName); // новый объект
                go.transform.position = data.Position; // позиция

                var stubRenderer = go.AddComponent<SpriteRenderer>(); // рендерер заглушки
                stubRenderer.sprite = BodyStubSprite(data); // процедурный спрайт
                // звезда — тёплая заглушка, дыра — тёмное ядро (UseColor перекрасит)
                stubRenderer.color = data.Type == BodyType.BlackHole ? BlackHole.CoreColor : new Color(1f, 0.82f, 0.35f); // цвет заглушки
                stubRenderer.sortingOrder = 1; // над звёздным фоном
            }

            var renderer = go.GetComponent<SpriteRenderer>(); // итоговый рендерер
            if (renderer == null) // не было
                renderer = go.AddComponent<SpriteRenderer>();
            ApplyVisuals(renderer, data.Visuals); // визуал из данных
            ApplyPlanetArt(renderer, data); // арт планеты

            CelestialBody body; // компонент тела по типу
            if (data.Type == BodyType.Star) // звезда
            {
                body = go.AddComponent<Star>();
            }
            else if (data.Type == BodyType.BlackHole) // дыра
            {
                body = go.AddComponent<BlackHole>();
            }
            else // планета
            {
                body = go.GetComponent<Planet>(); // из префаба?
                if (body == null) // нет — добавить
                    body = go.AddComponent<Planet>();
            }
            body.Init(data, config, fuelOutage); // инициализация тела

            if (data.Type == BodyType.Star && data.HeatRadius > 0f) // звезда с жаром
            {
                var heatGo = new GameObject("HeatZone"); // зона жара
                heatGo.transform.SetParent(go.transform, false); // ребёнок звезды
                var heatRenderer = heatGo.AddComponent<SpriteRenderer>(); // рендерер зоны
                heatRenderer.sprite = StubSprites.Zone(data.HeatRadius * 2f); // мягкая зона
                heatRenderer.color = new Color(1f, 0.45f, 0.2f, 0.07f); // тёплый полупрозрачный
                heatRenderer.sortingOrder = -1; // за спрайтом звезды
            }

            // ореол зоны сжатия дыры — по образцу HeatZone
            if (body is BlackHole hole) // дыра
            {
                var zoneGo = new GameObject("EffectZone"); // зона эффекта
                zoneGo.transform.SetParent(go.transform, false); // ребёнок дыры
                var zoneRenderer = zoneGo.AddComponent<SpriteRenderer>(); // рендерер
                zoneRenderer.sprite = StubSprites.Zone(hole.EffectRadius * 2f); // зона по радиусу эффекта
                zoneRenderer.color = BlackHole.ZoneColor; // фиолетовый полупрозрачный
                zoneRenderer.sortingOrder = -1; // за ядром
            }

            return body;
        }

        // процедурный спрайт тела без префаба: звезда — светящееся ядро с плавной
        // короной, дыра — чёткий чёрный диск, остальное — круг-заглушка. Текстуры
        // звезды/дыры нарисованы так, что альфа-контент = вся канва: масштаб тела
        // в CelestialBody.Init остаётся 1 и мировые размеры зон не меняются
        private static Sprite BodyStubSprite(CelestialBodyData data) // заглушка по типу тела
        {
            float diameter = data.Radius * 2f; // диаметр канвы
            switch (data.Type) // по типу
            {
                case BodyType.Star: // звезда
                    return StubSprites.StarBody(diameter);
                case BodyType.BlackHole: // дыра
                    return StubSprites.HoleCore(diameter);
                default: // прочее
                    return StubSprites.Circle(diameter);
            }
        }

        private static void ApplyVisuals(SpriteRenderer renderer, BodyVisuals visuals) // визуал из данных
        {
            if (renderer == null || visuals == null) // нечего применять
                return;
            if (visuals.Sprite != null) // спрайт задан
                renderer.sprite = visuals.Sprite;
            if (visuals.Material != null) // материал задан
                renderer.sharedMaterial = visuals.Material;
            if (visuals.UseColor) // тонировка
                renderer.color = visuals.Color;
        }

        // пользовательский арт планет: заправка — планета со станцией по тиру
        // топливной сети (poor/norm/rich/high), остальные — случайный спрайт из 9
        // со случайным цветом (оба стабильны по имени тела). Спрайт из данных
        // LevelAsset (будущий хаб-арт) главнее
        private static void ApplyPlanetArt(SpriteRenderer renderer, CelestialBodyData data) // арт планеты
        {
            if (renderer == null || data.Type != BodyType.Planet) // не планета
                return;
            if (data.Visuals != null && data.Visuals.Sprite != null) // спрайт задан в данных
                return;

            // хаб (HasFuel + магазин/улучшения) — отдельный арт позже; механика
            // станции сохранена, спрайт заправки на него не вешаем
            if (data.HasFuel && !data.IsHub) // заправка не хаб
            {
                Sprite gas = WorldSprites.GasStation(data.FuelTier); // спрайт станции по тиру
                if (gas != null) // нашли
                {
                    renderer.sprite = gas;
                    renderer.color = Color.white; // спрайт заправки уже покрашен
                }
                return;
            }

            Sprite planet = WorldSprites.Planet(data.DisplayName); // спрайт планеты по имени
            if (planet != null) // нашли
            {
                renderer.sprite = planet;
                renderer.color = WorldSprites.PlanetTint(data.DisplayName); // стабильный тон
            }
        }

        private Ship CreateShip(List<CelestialBody> bodies) // построить корабль
        {
            CelestialBodyData start = level.Bodies[level.StartPlanetIndex]; // данные стартовой планеты

            GameObject go; // объект корабля
            Ship ship; // модель
            ShipController controller; // контроллер
            ShipAudio shipAudio; // аудио
            if (shipPrefab != null) // есть префаб
            {
                go = Object.Instantiate(shipPrefab.gameObject); // инстанс префаба
                ship = go.GetComponent<Ship>();
                controller = go.GetComponent<ShipController>();
                shipAudio = go.GetComponent<ShipAudio>();
            }
            else // кодовая сборка
            {
                go = new GameObject("Ship"); // объект
                go.SetActive(false); // собираем неактивным (Init позже)

                var spriteRenderer = go.AddComponent<SpriteRenderer>(); // рендерер
                spriteRenderer.sortingOrder = 2; // над телами
                var collider = go.AddComponent<CircleCollider2D>(); // коллайдер
                collider.radius = 4f; // стартовый радиус (заменит ShipModelVisual)
                ship = go.AddComponent<Ship>(); // модель
                controller = go.AddComponent<ShipController>(); // контроллер
                shipAudio = go.AddComponent<ShipAudio>(); // аудио
                go.AddComponent<LandingSystem>(); // посадка
                go.AddComponent<HeatAudio>(); // жар
                go.AddComponent<TrajectoryPredictor>(); // прогноз

                var rb = ship.GetComponent<Rigidbody2D>(); // физическое тело
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; // непрерывные коллизии (не пролетать)
                rb.interpolation = RigidbodyInterpolation2D.Interpolate; // интерполяция визуала
            }

            var renderer = go.GetComponent<SpriteRenderer>(); // рендерер
            if (renderer != null && renderer.sprite == null) // спрайта нет
                renderer.sprite = StubSprites.ShipTriangle(); // треугольник-заглушка

            // визуал модели (Resources/Ships/<Id>): спрайт покраски + слоты сопел.
            // Apply до расчёта позиции — точка спавна зависит от HalfExtent контента
            var modelVisual = go.GetComponent<ShipModelVisual>(); // визуал модели
            if (modelVisual == null) // нет — добавить
                modelVisual = go.AddComponent<ShipModelVisual>();
            modelVisual.Apply(shipConfig, 0); // применить модель с заводской покраской

            float angleRad = level.SpawnAngleDeg * Mathf.Deg2Rad; // угол спавна
            Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)); // направление от центра
            // радиус построенного тела, а не сырых данных уровня: облик планеты
            // умножен на PlanetSizeMul (CelestialBody.Init), спавн от start.Radius
            // оставлял крупный корпус хвостом в планете, солвер выталкивал;
            // фолбэк — сырой радиус, если тела нет (пояс-заглушка)
            CelestialBody startBody = level.StartPlanetIndex >= 0 && level.StartPlanetIndex < bodies.Count // тело построено
                ? bodies[level.StartPlanetIndex]
                : null;
            float startRadius = startBody != null ? startBody.Radius : start.Radius; // живой или сырой радиус
            // корма на поверхности планеты; ShipOffset — фолбэк без визуала
            float spawnOffset = modelVisual.HasVisual ? ShipPlacement.LandedExtent(modelVisual) : ShipOffset; // отступ спавна
            float nose = controller != null ? controller.SpriteNoseAngle : 0f; // угол носа арта
            // Body ещё null (Init ниже) — телепорт деградирует до записи
            // transform: корабль неактивен, истории интерполяции нет
            ShipPlacement.TeleportLanded(ship, start.Position + dir * (startRadius + spawnOffset), // точка спавна
                level.SpawnAngleDeg - nose); // поза носом наружу

            ship.Init(config, shipConfig); // инициализация модели
            ship.State = ShipState.Landed; // стартует на планете
            ship.LandedOn = bodies[level.StartPlanetIndex] as Planet; // планета стоянки

            if (controller != null) // контроллер есть
                controller.SetInput(input); // передать ввод
            if (shipAudio != null) // аудио есть
                shipAudio.SetInput(input);
            var hull = go.GetComponent<HullSystem>(); // корпус
            if (hull != null) // из префаба
                hull.Init(level);
            else // кодовая сборка
                go.AddComponent<HullSystem>().Init(level);

            if (go.GetComponent<EngineEffects>() == null) // выхлоп
                go.AddComponent<EngineEffects>();
            if (go.GetComponent<SpeedTrail>() == null) // след скорости
                go.AddComponent<SpeedTrail>();
            if (go.GetComponent<DamageEffects>() == null) // эффекты урона
                go.AddComponent<DamageEffects>();

            go.SetActive(true); // включить корабль
            return ship;
        }
    }
}
