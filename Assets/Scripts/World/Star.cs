using UnityEngine;

namespace SpaceTaxi.World
{
    public class Star : CelestialBody
    {
        [Tooltip("Радиус тепловой зоны вокруг звезды (область повреждения).")]
        public float HeatRadius = 180f;

        [Tooltip("Урон корпуса в секунду у поверхности (на краю зоны — 0).")]
        public float HeatDamagePerSecond = 30f;

        public override void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig, bool fuelOutage = false)
        {
            base.Init(data, physicsConfig, fuelOutage);
            HeatRadius = data.HeatRadius;
        }

        public bool IsInHeatZone(Vector2 point)
        {
            return Vector2.Distance(point, transform.position) <= HeatRadius;
        }
    }
}
