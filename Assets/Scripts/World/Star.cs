using UnityEngine;

namespace SpaceTaxi.World
{
    public class Star : CelestialBody
    {
        public float HeatRadius = 180f;
        public float HeatDamagePerSecond = 10f; // заглушка, настроим с балансом

        public override void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig)
        {
            base.Init(data, physicsConfig);
            HeatRadius = data.HeatRadius;
        }

        public bool IsInHeatZone(Vector2 point)
        {
            return Vector2.Distance(point, transform.position) <= HeatRadius;
        }
    }
}
