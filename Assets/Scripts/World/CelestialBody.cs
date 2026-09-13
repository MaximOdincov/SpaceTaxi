using UnityEngine;

namespace SpaceTaxi.World
{
    [RequireComponent(typeof(CircleCollider2D))]
    public class CelestialBody : MonoBehaviour
    {
        public string DisplayName = "Body";
        public BodyType Type = BodyType.Planet;
        public float Radius = 50f;
        public float Mass = 1f;

        protected Physics.PhysicsConfig config;

        private Physics.GravityField gravityField;

        // Вызывается WorldBuilder'ом при сборке сцены.
        public virtual void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig)
        {
            DisplayName = data.DisplayName;
            Type = data.Type;
            Radius = data.Radius;
            Mass = data.MassMultiplier; // множитель массы: планета 1, звезда 4.5

            transform.position = data.Position;
            GetComponent<CircleCollider2D>().radius = Radius;

            config = physicsConfig;
        }

        protected virtual void OnEnable()
        {
            // Важно: GravityField должен появиться в сцене раньше тел.
            gravityField = FindAnyObjectByType<Physics.GravityField>();
            if (gravityField != null)
                gravityField.Register(this);
        }

        protected virtual void OnDisable()
        {
            if (gravityField != null)
                gravityField.Unregister(this);
        }
    }
}
