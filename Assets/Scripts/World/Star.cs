using UnityEngine;

namespace SpaceTaxi.World
{
    /// <summary>
    /// A star: strong gravity plus a heat zone that gradually damages the hull
    /// of any ship inside it.
    /// </summary>
    public class Star : CelestialBody
    {
        [Tooltip("Radius of the heat zone around the star (damage area).")]
        public float HeatRadius = 180f;

        [Tooltip("Hull damage per second inside the heat zone. Placeholder default, tune with the level designer.")]
        public float HeatDamagePerSecond = 10f;

        public override void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig)
        {
            base.Init(data, physicsConfig);
            HeatRadius = data.HeatRadius;
        }

        /// <summary>True when the point lies inside the heat zone.</summary>
        public bool IsInHeatZone(Vector2 point)
        {
            // TODO (star iteration): damage over time via HullSystem while the
            // ship stays inside the zone.
            return Vector2.Distance(point, transform.position) <= HeatRadius;
        }
    }
}
