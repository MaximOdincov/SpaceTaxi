using UnityEngine;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.Core
{
    /// <summary>
    /// Root of a running game: owns the current level and shared configs,
    /// spawns the ship and later drives save/load. Placed in the Game scene.
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        [Header("Assets")]
        [Tooltip("Level to build the world from.")]
        [SerializeField] private LevelAsset level;

        [Tooltip("Shared physics tuning.")]
        [SerializeField] private PhysicsConfig physicsConfig;

        [Header("Runtime")]
        [SerializeField] private Ship ship;

        public LevelAsset Level => level;
        public PhysicsConfig Config => physicsConfig;
        public Ship Ship => ship;

        private void Start()
        {
            // TODO (world building iteration, after level designer input):
            // build the world from Level via WorldBuilder, spawn the Ship at
            // Level.SpawnPoint with Level.SpawnHeading, init ship systems
            // with Config.
        }
    }
}
