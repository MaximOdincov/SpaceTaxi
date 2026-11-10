using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.World
{
    public enum BodyType
    {
        Planet = 0,
        Star = 1,
        BlackHole = 10,
        AsteroidBelt = 11,
        Comet = 12,
    }

    public enum SpawnMode
    {
        Landed = 0, // сидим на планете старта
        Free = 1,   // свободная точка, пока не используется
    }

    // Класс района системы: сектор планеты назначения влияет на цену заказа
    public enum Sector
    {
        Hub = 0,
        Near = 1,
        Mid1 = 2,
        Mid2 = 3,
        Far = 4,
    }

    [Serializable]
    public sealed class BodyVisuals
    {
        [Tooltip("Спрайт этого тела; пусто — из префаба Planet/Star")]
        public Sprite Sprite;
        [Tooltip("Материал; пусто — из префаба")]
        public Material Material;
        [Tooltip("Красить спрайт этим цветом")]
        public bool UseColor;
        public Color Color = Color.white;
        [Tooltip("Вращение спрайта, град/с — для планет с деталями")]
        public float SpinSpeed;
    }

    [Serializable]
    public sealed class CelestialBodyData
    {
        public string DisplayName = "Body";
        public BodyType Type = BodyType.Planet;
        public Vector2 Position;
        [Min(5f)] public float Radius = 50f;
        [Tooltip("Множитель размера планеты поверх Radius (0 = авто по имени, WorldSprites.PlanetSizeMul); только планеты")]
        [Min(0f)] public float VisualSizeMul;
        [Min(0.1f)] public float MassMultiplier = 1f; // планета 1, звезда 4.5
        [Min(0f)] public float HeatRadius = 0f;       // звёзды: 3.6 * r
        public BodyVisuals Visuals = new BodyVisuals();
        [Tooltip("Планета с заправкой: платная дозаправка топлива (экран [B])")]
        public bool HasFuel;
        [Tooltip("Тир топливной сети 1..4 (цена/ед и запас визита из PricingConfig); 0 — тир не задан, считается базовой сетью")]
        [Min(0)] public int FuelTier;
        [Tooltip("Планета с мастерской: платный ремонт корпуса (экран [B])")]
        public bool HasRepair;
        [Tooltip("Тир сети ремонта 1..3: макс. пакет и потолок визита из PricingConfig; 0 — не задан, чинит полностью (хаб = 3)")]
        [Min(0)] public int RepairTier;
        [Tooltip("Хаб МТО: заправка + мастерская + магазин кораблей и улучшения")]
        public bool IsHub;
        [Tooltip("Класс района: влияет на оплату заказов на это тело")]
        public Sector Sector = Sector.Near;

        [Header("Пояс астероидов (Type == AsteroidBelt)")]
        [Tooltip("Position = центр пояса, Radius = радиус осевой линии кольца")]
        [Min(0f)] public float BeltWidth = 40f;         // разброс астероидов вокруг осевой
        [Min(1)] public int BeltAsteroidCount = 30;      // кламп 24–40 при создании пояса

        [Header("Кольца (0 = считать по формуле)")]
        [Min(0f)] public float LandingRingRadius; // посадочное: 0 = r + 90
        [Min(0f)] public float CaptureRingRadius; // качели: 0 = r * 3.3 + 30
    }

    [CreateAssetMenu(fileName = "Level", menuName = "Space Taxi/Level Asset")]
    public sealed class LevelAsset : ScriptableObject
    {
        public string LevelName = "NewLevel";

        [Tooltip("Граница системы: дальше — потеря связи")]
        public float WorldRadius = 2500f;

        public List<CelestialBodyData> Bodies = new List<CelestialBodyData>();

        [Header("Старт игрока")]
        public SpawnMode SpawnMode = SpawnMode.Landed;
        public int StartPlanetIndex = 0;                    // планета старта = точка респавна
        [Range(0f, 360f)] public float SpawnAngleDeg = 90f; // 0 = восток, 90 = север

        // Только при SpawnMode = Free.
        public Vector2 SpawnPoint;
        [Range(-180f, 180f)] public float SpawnHeading;

        [Header("Кометы (радиус 0 = хорды по всему миру)")]
        [Tooltip("Центр зоны комет: маршруты не покидают эту окружность")]
        public Vector2 CometAreaCenter = Vector2.zero;
        [Min(0f)] public float CometAreaRadius = 0f;
    }
}
