using UnityEngine;

namespace SpaceTaxi.World
{
    /// <summary>
    /// Base component for every celestial body (planet, star, later holes,
    /// belts and comets). Holds physical parameters and is meant to register
    /// itself in the GravityField so attraction can be summed from all bodies.
    /// </summary>
    [RequireComponent(typeof(CircleCollider2D))]
    public class CelestialBody : MonoBehaviour
    {
        [Tooltip("Human-readable name shown in UI.")]
        public string DisplayName = "Body";

        public BodyType Type = BodyType.Planet;

        [Tooltip("Body radius in world units (matches the circle collider).")]
        public float Radius = 50f;

        [Tooltip("Effective mass used by gravity: a = G * Mass / r^2.")]
        public float Mass = 1f;

        /// <summary>Shared physics tuning, set during Init.</summary>
        protected Physics.PhysicsConfig config;

        /// <summary>Initializes the body from level data. Called by the world builder.</summary>
        public virtual void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig)
        {
            DisplayName = data.DisplayName;
            Type = data.Type;
            Radius = data.Radius;
            // TODO (physics iteration): finalize the mass formula
            // (raw multiplier vs radius-scaled) together with GravityConstant.
            Mass = data.MassMultiplier;

            transform.position = data.Position;

            var circleCollider = GetComponent<CircleCollider2D>();
            circleCollider.radius = Radius;

            config = physicsConfig;
        }

        protected virtual void OnEnable()
        {
            // TODO (physics iteration): GravityField.Register(this).
        }

        protected virtual void OnDisable()
        {
            // TODO (physics iteration): GravityField.Unregister(this).
        }
    }
}
