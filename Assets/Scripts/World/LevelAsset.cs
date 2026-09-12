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

    [Serializable]
    public sealed class CelestialBodyData
    {
        public string DisplayName = "Body";
        public BodyType Type = BodyType.Planet;
        public Vector2 Position;
        public float Radius = 50f;
        public float MassMultiplier = 1f; // формула массы уточнится в итерации физики
        public float HeatRadius = 0f;     // только для звёзд
    }

    // Данные уровня: тела и точка старта. Мир собирается из ассета кодом.
    [CreateAssetMenu(fileName = "Level", menuName = "Space Taxi/Level Asset")]
    public sealed class LevelAsset : ScriptableObject
    {
        public string LevelName = "NewLevel";
        public List<CelestialBodyData> Bodies = new List<CelestialBodyData>();
        public Vector2 SpawnPoint;
        [Range(-180f, 180f)] public float SpawnHeading; // градусы, 0 = вправо
    }
}
