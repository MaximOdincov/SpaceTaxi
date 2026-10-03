using UnityEngine;

namespace SpaceTaxi.Physics
{
    // Следование за кораблём с опережением по курсу: камера смещается
    // вперёд по вектору скорости (видно, куда летишь), плавно отъезжает
    // на большой скорости и дрожит от ударов — сила дрожи = trauma²,
    // спад линейный (классическая модель, живая и без укачивания).
    public class CameraFollow : MonoBehaviour
    {
        private const float FlightViewHeight = 320f;  // обычный полёт
        private const float FastViewHeight = 470f;    // отъезд на максималке
        private const float LandingViewHeight = 190f; // посадка/ретро
        private const float LookaheadSeconds = 0.45f; // взгляд вперёд
        private const float LookaheadMax = 130f;      // предел смещения
        private const float ShakeDecay = 1.6f;        // спад trauma в секунду
        private const float ShakeAmplitude = 15f;     // единиц при trauma=1
        private const float ShakeRotation = 2.5f;     // градусы при trauma=1

        public static CameraFollow Instance { get; private set; }

        [SerializeField] private Ship ship;

        private Camera cam;
        private ShipController controller;
        private float trauma;
        private float noiseSeed;
        private Vector2 lookOffset;

        /// <summary>Позиция камеры без дрожи — якорь для параллакса.</summary>
        public Vector3 AnchorPosition { get; private set; }

        public void SetTarget(Ship target)
        {
            ship = target;
        }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            Instance = this;
            noiseSeed = Random.value * 100f;
            AnchorPosition = transform.position;
            if (ship == null)
                ship = FindAnyObjectByType<Ship>();
        }

        private void LateUpdate()
        {
            if (ship == null)
                return;
            float dt = Time.deltaTime;

            // Взгляд вперёд по курсу: сглаженное смещение по скорости,
            // ограничено по длине — и всегда к нулю на стоянке.
            Vector2 velocity = ship.Body != null ? ship.Body.linearVelocity : Vector2.zero;
            Vector2 targetOffset = ship.State == ShipState.Flying
                ? Vector2.ClampMagnitude(velocity * LookaheadSeconds, LookaheadMax)
                : Vector2.zero;
            lookOffset += (targetOffset - lookOffset) * Mathf.Min(1f, 3f * dt);

            Vector3 pos = transform.position;
            Vector2 target = (Vector2)ship.transform.position + lookOffset;
            pos.x += (target.x - pos.x) * Mathf.Min(1f, 3.5f * dt);
            pos.y += (target.y - pos.y) * Mathf.Min(1f, 3.5f * dt);
            AnchorPosition = pos;

            // Дрожь: гладкий шум Перлина вместо рваного рандома.
            trauma = Mathf.Max(0f, trauma - ShakeDecay * dt);
            float shake = trauma * trauma;
            float t = Time.time;
            Vector2 shakeOffset = new Vector2(
                Mathf.PerlinNoise(noiseSeed, t * 14f) * 2f - 1f,
                Mathf.PerlinNoise(noiseSeed + 31f, t * 14f) * 2f - 1f)
                * (ShakeAmplitude * shake);
            transform.position = pos + (Vector3)shakeOffset;
            transform.rotation = Quaternion.Euler(0f, 0f,
                (Mathf.PerlinNoise(noiseSeed + 77f, t * 14f) * 2f - 1f) * ShakeRotation * shake);

            // Зум: посадочный ближе, полёт дальше, на скорости ещё дальше.
            if (controller == null && ship != null)
                controller = ship.GetComponent<ShipController>();
            bool landing = ship.State == ShipState.Landed
                || (controller != null && controller.LandingRetro);
            float vNorm = ship.Config != null
                ? Mathf.Clamp01(velocity.magnitude / ship.Config.MaxV)
                : 0f;
            float viewHeight = landing
                ? LandingViewHeight
                : Mathf.Lerp(FlightViewHeight, FastViewHeight, vNorm);
            float targetSize = viewHeight * 0.5f;
            cam.orthographicSize += (targetSize - cam.orthographicSize) * Mathf.Min(1f, 3f * dt);
        }

        /// <summary>Толчок камеры: ~0.25 — взлёт, ~0.5 — жёсткий удар, ~0.85 — гибель.</summary>
        public static void AddShake(float power)
        {
            if (Instance != null)
                Instance.trauma = Mathf.Min(1f, Instance.trauma + power);
        }
    }
}
