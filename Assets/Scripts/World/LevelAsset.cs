using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.World
{
    public enum BodyType
    {
        Planet = 0,
        Star = 1,

        // На будущее (03_WORLD_MAP.md).
        BlackHole = 10,
        AsteroidBelt = 11,
        Comet = 12,
    }

    public enum SpawnMode
    {
        Landed = 0, // сидим на планете старта
        Free = 1,   // свободная точка, в v0.1 не используется
    }

    [Serializable]
    public sealed class CelestialBodyData
    {
        public string DisplayName = "Body";
        public BodyType Type = BodyType.Planet;
        public Vector2 Position;
        [Min(5f)] public float Radius = 50f;
        [Min(0.1f)] public float MassMultiplier = 1f; // планета 1, звезда 4.5
        [Min(0f)] public float HeatRadius = 0f;       // звёзды: 3.6 * r
    }

    // Данные уровня: тела и старт. Мир собирает WorldBuilder, редактор уровня
    // пишет сюда же.
    [CreateAssetMenu(fileName = "Level", menuName = "Space Taxi/Level Asset")]
    public sealed class LevelAsset : ScriptableObject
    {
        public string LevelName = "NewLevel";
        public List<CelestialBodyData> Bodies = new List<CelestialBodyData>();

        [Header("Старт игрока")]
        public SpawnMode SpawnMode = SpawnMode.Landed;
        public int StartPlanetIndex = 0;                    // планета старта = точка респавна
        [Range(0f, 360f)] public float SpawnAngleDeg = 90f; // 0 = восток, 90 = север

        // Только при SpawnMode = Free.
        public Vector2 SpawnPoint;
        [Range(-180f, 180f)] public float SpawnHeading;
    }
}
