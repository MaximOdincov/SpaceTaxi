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

        // услуги станции (Н-3): заправка и мастерская; хаб — ещё магазин/улучшения
        public bool HasFuel { get; private set; }
        public bool HasRepair { get; private set; }
        public bool IsHub { get; private set; }
        // топливная сеть (Н-4): тир планеты — цена/ед и запас визита из PricingConfig
        public int FuelTier { get; private set; }
        // сеть ремонта (Н-5): тир планеты — макс. пакет и потолок визита из PricingConfig
        public int RepairTier { get; private set; }

        protected Physics.PhysicsConfig config;

        private Physics.GravityField gravityField;
        private float spinSpeed;
        private float landingRingOverride;
        private float captureRingOverride;

        public float LandingRing =>
            landingRingOverride > 0f ? landingRingOverride : Radius + (config != null ? config.LandingRingOffset : 90f);

        public float CaptureRing =>
            captureRingOverride > 0f ? captureRingOverride
                : Radius * (config != null ? config.CaptureRingK : 3.3f) + (config != null ? config.CaptureRingBase : 30f);

        public virtual void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig)
        {
            DisplayName = data.DisplayName;
            Type = data.Type;
            Radius = data.Radius;
            Mass = data.MassMultiplier;
            HasFuel = data.HasFuel;
            HasRepair = data.HasRepair;
            IsHub = data.IsHub;
            FuelTier = data.FuelTier;
            RepairTier = data.RepairTier;
            spinSpeed = data.Visuals != null ? data.Visuals.SpinSpeed : 0f;
            landingRingOverride = data.LandingRingRadius;
            captureRingOverride = data.CaptureRingRadius;

            transform.position = data.Position;

            var renderer = GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null)
            {
                // по контенту арта (альфа-границы), не по канве: видимый шар
                // должен совпадать с логическим радиусом и коллайдером
                Vector2 content = Core.SpriteArt.ContentSize(renderer.sprite);
                float d = Mathf.Max(content.x, content.y);
                if (d > 0.0001f)
                {
                    float s = Radius * 2f / d;
                    transform.localScale = new Vector3(s, s, 1f);
                }
            }

            float scale = Mathf.Max(Mathf.Abs(transform.localScale.x), Mathf.Abs(transform.localScale.y), 0.0001f);
            GetComponent<CircleCollider2D>().radius = Radius / scale;

            config = physicsConfig;
        }

        protected virtual void OnEnable()
        {
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
            if (!Mathf.Approximately(spinSpeed, 0f))
                transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);
        }
    }
}
