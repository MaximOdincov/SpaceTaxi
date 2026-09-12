using UnityEngine;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.Core
{
    // Корень игровой сцены: конфиги и корабль, позже — сейв.
    public class GameSession : MonoBehaviour
    {
        [SerializeField] private LevelAsset level;
        [SerializeField] private PhysicsConfig physicsConfig;
        [SerializeField] private Ship ship;

        public LevelAsset Level => level;
        public PhysicsConfig Config => physicsConfig;
        public Ship Ship => ship;

        private void Start()
        {
            // TODO: собрать мир из level, заспавнить корабль в SpawnPoint
        }
    }
}
