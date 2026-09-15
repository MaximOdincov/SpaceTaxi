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
        private float spinSpeed;
        private float landingRingOverride;
        private float captureRingOverride;

        // Радиус посадочного кольца (override из данных или формула r + offset).
        public float LandingRing =>
            landingRingOverride > 0f ? landingRingOverride : Radius + (config != null ? config.LandingRingOffset : 90f);

        // Радиус кольца качели (override или формула r * K + Base).
        public float CaptureRing =>
            captureRingOverride > 0f ? captureRingOverride
                : Radius * (config != null ? config.CaptureRingK : 3.3f) + (config != null ? config.CaptureRingBase : 30f);

        // Вызывается WorldBuilder'ом при сборке сцены.
        // Визуал (спрайт, цвет) — целиком из префаба, здесь только подгон
        // масштаба под радиус и настройка коллайдера.
        public virtual void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig)
        {
            DisplayName = data.DisplayName;
            Type = data.Type;
            Radius = data.Radius;
            Mass = data.MassMultiplier;
            spinSpeed = data.Visuals != null ? data.Visuals.SpinSpeed : 0f;
            landingRingOverride = data.LandingRingRadius;
            captureRingOverride = data.CaptureRingRadius;

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

        private void Update()
        {
            // Медленное вращение — для спрайтов с деталями. Коллайдер круглой
            // формы от вращения не меняется.
            if (!Mathf.Approximately(spinSpeed, 0f))
                transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);
        }
    }
}
