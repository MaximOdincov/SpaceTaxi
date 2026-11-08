using UnityEngine;

namespace SpaceTaxi.Physics
{
    // Следование за кораблём с опережением по курсу, зум по скорости, дрожь от ударов
    public class CameraFollow : MonoBehaviour
    {
        private const float FlightViewHeight = 260f;  // медленный полёт — ближе
        private const float FastViewHeight = 560f;    // отъезд на максималке
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

        // без дрожи — якорь для параллакса
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
            // Пауза (модал завершения, меню ESC) ставит timeScale=0, но
            // LateUpdate живёт в реальном времени: в scaled-времени камера
            // замирала со смещением lookahead и после закрытия модала видимо
            // «уплывала» доседать на корабль (Н-9). На unscaled — стоит на
            // корабле уже во время модала, после закрытия догонять нечего
            float dt = Time.timeScale > 0f ? Time.deltaTime : Time.unscaledDeltaTime;

            // взгляд вперёд по курсу
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

            // дрожь — шум Перлина вместо рваного рандома; гаснет и шумит в
            // реальном времени — на паузе не висит застывшим смещением
            trauma = Mathf.Max(0f, trauma - ShakeDecay * dt);
            float shake = trauma * trauma;
            float t = Time.unscaledTime;
            Vector2 shakeOffset = new Vector2(
                Mathf.PerlinNoise(noiseSeed, t * 14f) * 2f - 1f,
                Mathf.PerlinNoise(noiseSeed + 31f, t * 14f) * 2f - 1f)
                * (ShakeAmplitude * shake);
            transform.position = pos + (Vector3)shakeOffset;
            transform.rotation = Quaternion.Euler(0f, 0f,
                (Mathf.PerlinNoise(noiseSeed + 77f, t * 14f) * 2f - 1f) * ShakeRotation * shake);

            // зум: посадка ближе, скорость дальше
            if (controller == null && ship != null)
                controller = ship.GetComponent<ShipController>();
            bool landing = ship.State == ShipState.Landed
                || (controller != null && controller.LandingRetro);
            float vNorm = ship.ShipConfig != null
                ? Mathf.Clamp01(velocity.magnitude / ship.ShipConfig.MaxV)
                : 0f;
            float viewHeight = landing
                ? LandingViewHeight
                : Mathf.Lerp(FlightViewHeight, FastViewHeight, vNorm);
            float targetSize = viewHeight * 0.5f;
            cam.orthographicSize += (targetSize - cam.orthographicSize) * Mathf.Min(1f, 3f * dt);
        }

        // ~0.25 — взлёт, ~0.5 — жёсткий удар, ~0.85 — гибель
        public static void AddShake(float power)
        {
            if (Instance != null)
                Instance.trauma = Mathf.Min(1f, Instance.trauma + power);
        }
    }
}
