using System; // Serializable
using System.Collections.Generic; // список тел
using UnityEngine; // SO и векторы

namespace SpaceTaxi.World
{
    public enum BodyType // типы тел системы
    {
        Planet = 0, // планета
        Star = 1, // звезда
        BlackHole = 10, // чёрная дыра
        AsteroidBelt = 11, // пояс астероидов
        Comet = 12, // комета
    }

    public enum SpawnMode // режим старта игрока
    {
        Landed = 0, // сидим на планете старта
        Free = 1,   // свободная точка, пока не используется
    }

    // Класс района системы: сектор планеты назначения влияет на цену заказа
    public enum Sector // районы системы
    {
        Hub = 0, // хаб
        Near = 1, // ближний
        Mid1 = 2, // средний I
        Mid2 = 3, // средний II
        Far = 4, // дальний
    }

    [Serializable] // сериализуемый блок визуала
    public sealed class BodyVisuals // визуальные настройки тела
    {
        [Tooltip("Спрайт этого тела; пусто — из префаба Planet/Star")]
        public Sprite Sprite; // спрайт тела
        [Tooltip("Материал; пусто — из префаба")]
        public Material Material; // материал
        [Tooltip("Красить спрайт этим цветом")]
        public bool UseColor; // тонировать цветом
        public Color Color = Color.white; // цвет тонировки
        [Tooltip("Вращение спрайта, град/с — для планет с деталями")]
        public float SpinSpeed; // скорость вращения
    }

    [Serializable] // сериализуемая запись тела
    public sealed class CelestialBodyData // данные одного тела уровня
    {
        public string DisplayName = "Body"; // имя тела
        public BodyType Type = BodyType.Planet; // тип
        public Vector2 Position; // позиция в мире
        [Min(5f)] public float Radius = 50f; // логический радиус
        [Tooltip("Множитель размера планеты поверх Radius (0 = авто по имени, WorldSprites.PlanetSizeMul); только планеты")]
        [Min(0f)] public float VisualSizeMul; // визуальный множитель размера
        [Min(0.1f)] public float MassMultiplier = 1f; // планета 1, звезда 4.5
        [Min(0f)] public float HeatRadius = 0f;       // звёзды: 3.6 * r
        public BodyVisuals Visuals = new BodyVisuals(); // визуал
        [Tooltip("Планета с заправкой: платная дозаправка топлива (экран [B])")]
        public bool HasFuel; // есть заправка
        [Tooltip("Тир топливной сети 1..4 (цена/ед и запас визита из PricingConfig); 0 — тир не задан, считается базовой сетью")]
        [Min(0)] public int FuelTier; // тир топлива
        [Tooltip("Планета с мастерской: платный ремонт корпуса (экран [B])")]
        public bool HasRepair; // есть мастерская
        [Tooltip("Тир сети ремонта 1..3: макс. пакет и потолок визита из PricingConfig; 0 — не задан, чинит полностью (хаб = 3)")]
        [Min(0)] public int RepairTier; // тир ремонта
        [Tooltip("Хаб МТО: заправка + мастерская + магазин кораблей и улучшения")]
        public bool IsHub; // хаб
        [Tooltip("Класс района: влияет на оплату заказов на это тело")]
        public Sector Sector = Sector.Near; // район

        [Header("Пояс астероидов (Type == AsteroidBelt)")] // параметры пояса
        [Tooltip("Position = центр пояса, Radius = радиус осевой линии кольца")]
        [Min(0f)] public float BeltWidth = 40f;         // разброс астероидов вокруг осевой
        [Min(1)] public int BeltAsteroidCount = 30;      // кламп 24–40 при создании пояса

        [Header("Кольца (0 = считать по формуле)")] // переопределения колец
        [Min(0f)] public float LandingRingRadius; // посадочное: 0 = r + 90
        [Min(0f)] public float CaptureRingRadius; // качели: 0 = r * 3.3 + 30
    }

    [CreateAssetMenu(fileName = "Level", menuName = "Space Taxi/Level Asset")] // ассет-меню
    public sealed class LevelAsset : ScriptableObject // уровень целиком как данные
    {
        public string LevelName = "NewLevel"; // имя уровня

        [Tooltip("Граница системы: дальше — потеря связи")]
        public float WorldRadius = 2500f; // радиус мира

        public List<CelestialBodyData> Bodies = new List<CelestialBodyData>(); // тела уровня

        [Header("Старт игрока")] // параметры старта
        public SpawnMode SpawnMode = SpawnMode.Landed; // режим старта
        public int StartPlanetIndex = 0;                    // планета старта = точка респавна
        [Range(0f, 360f)] public float SpawnAngleDeg = 90f; // 0 = восток, 90 = север

        // Только при SpawnMode = Free.
        public Vector2 SpawnPoint; // точка спавна
        [Range(-180f, 180f)] public float SpawnHeading; // курс спавна

        [Header("Кометы (радиус 0 = хорды по всему миру)")] // зона комет
        [Tooltip("Центр зоны комет: маршруты не покидают эту окружность")]
        public Vector2 CometAreaCenter = Vector2.zero; // центр зоны
        [Min(0f)] public float CometAreaRadius = 0f; // радиус зоны
    }
}
