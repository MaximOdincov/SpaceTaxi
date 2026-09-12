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

        // Вызывается WorldBuilder'ом при сборке сцены.
        public virtual void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig)
        {
            DisplayName = data.DisplayName;
            Type = data.Type;
            Radius = data.Radius;
            // TODO: формула массы — согласовать с гравитацией
            Mass = data.MassMultiplier;

            transform.position = data.Position;
            GetComponent<CircleCollider2D>().radius = Radius;

            config = physicsConfig;
        }

        protected virtual void OnEnable()
        {
            // TODO: регистрация в GravityField
        }

        protected virtual void OnDisable()
        {
            // TODO: снятие регистрации
        }
    }
}
