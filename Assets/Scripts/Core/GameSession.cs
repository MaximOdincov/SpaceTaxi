using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.Core
{
    public class GameSession : MonoBehaviour
    {
        [SerializeField] private LevelAsset level;
        [SerializeField] private PhysicsConfig physicsConfig;
        [SerializeField] private InputActionAsset input;

        [SerializeField] private Planet planetPrefab;
        [SerializeField] private Ship shipPrefab;

        public Ship Ship { get; private set; }
        public LevelAsset Level => level;
        public PhysicsConfig Config => physicsConfig;

        private void Start()
        {
            var builder = new WorldBuilder(level, physicsConfig, input, planetPrefab, shipPrefab);
            builder.Build(out Ship ship, out _);
            Ship = ship;

            var follow = Camera.main.gameObject.AddComponent<CameraFollow>();
            follow.SetTarget(Ship);
        }
    }
}

