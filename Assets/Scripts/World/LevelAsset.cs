using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.World
{
    /// <summary>Type of a celestial body placed in a level.</summary>
    public enum BodyType
    {
        Planet = 0,
        Star = 1,

        // Reserved for future versions (see GameInfo/03_WORLD_MAP.md).
        BlackHole = 10,
        AsteroidBelt = 11,
        Comet = 12,
    }

    /// <summary>Serializable description of one celestial body inside a level.</summary>
    [Serializable]
    public sealed class CelestialBodyData
    {
        [Tooltip("Human-readable name shown in UI.")]
        public string DisplayName = "Body";

        public BodyType Type = BodyType.Planet;

        [Tooltip("World position of the body centre.")]
        public Vector2 Position;

        [Tooltip("Body radius in world units (1 unit = 1 pixel of the HTML prototype).")]
        public float Radius = 50f;

        [Tooltip("Mass multiplier used by gravity. The exact mass formula (raw or radius-scaled) is finalized in the physics iteration.")]
        public float MassMultiplier = 1f;

        [Tooltip("Heat zone radius, stars only.")]
        public float HeatRadius = 0f;
    }

    /// <summary>
    /// Data-only description of a level: celestial bodies and the player spawn
    /// point. The gameplay scene is built from this asset by code; the level
    /// editor (a later iteration) writes into it. The actual layout is
    /// coordinated with the level designer.
    /// </summary>
    [CreateAssetMenu(fileName = "Level", menuName = "Space Taxi/Level Asset")]
    public sealed class LevelAsset : ScriptableObject
    {
        [Tooltip("Level name, used by save data and UI.")]
        public string LevelName = "NewLevel";

        [Tooltip("All celestial bodies of the level.")]
        public List<CelestialBodyData> Bodies = new List<CelestialBodyData>();

        [Tooltip("Player spawn position.")]
        public Vector2 SpawnPoint;

        [Tooltip("Initial ship heading in degrees (0 = right, counterclockwise).")]
        [Range(-180f, 180f)]
        public float SpawnHeading;
    }
}
