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
        // Визуал (спрайт, цвет) — целиком из префаба, здесь только подгон
        // масштаба под радиус и настройка коллайдера.
        public virtual void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig)
        {
            DisplayName = data.DisplayName;
            Type = data.Type;
            Radius = data.Radius;
            Mass = data.MassMultiplier;

            transform.position = data.Position;

            var renderer = GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null)
            {
                float d = Mathf.Max(renderer.sprite.bounds.size.x, renderer.sprite.bounds.size.y);
                if (d > 0.0001f)
                {
                    // Визуальный диаметр = 2 * Radius, каким бы ни был спрайт.
                    float s = Radius * 2f / d;
                    transform.localScale = new Vector3(s, s, 1f);
                }
            }

            // Коллайдер живёт в локальных единицах — делим на масштаб.
            float scale = Mathf.Max(Mathf.Abs(transform.localScale.x), Mathf.Abs(transform.localScale.y), 0.0001f);
            GetComponent<CircleCollider2D>().radius = Radius / scale;

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
