using UnityEngine; // камера и шум Перлина

namespace SpaceTaxi.Physics
{
    // Следование за кораблём с опережением по курсу, зум по скорости, дрожь от ударов
    public class CameraFollow : MonoBehaviour // один экземпляр, синглтон для тряски
    {
        // ХОТФИКС владельца: камера дальше от корабля (~+21%) — «приближение чуть
        // меньше и вообще чуть отдали»
        private const float FlightViewHeight = 320f;  // медленный полёт — ближе
        private const float FastViewHeight = 680f;    // отъезд на максималке
        private const float LandingViewHeight = 300f; // посадка/ретро (владелец: «слишком сильно приближение при посадке» — 230 → 300, зум мягче)
        private const float LookaheadSeconds = 0.45f; // взгляд вперёд
        private const float LookaheadMax = 130f;      // предел смещения
        private const float ShakeDecay = 1.6f;        // спад trauma в секунду
        private const float ShakeAmplitude = 15f;     // единиц при trauma=1
        private const float ShakeRotation = 2.5f;     // градусы при trauma=1

        public static CameraFollow Instance { get; private set; } // статическая точка для AddShake

        [SerializeField] private Ship ship; // цель слежения

        private Camera cam; // компонент камеры
        private ShipController controller; // контроллер (для признака ретро-тормоза)
        private float trauma; // накопленная тряска 0..1
        private float noiseSeed; // сид Перлина (стабильный рисунок шума)
        private Vector2 lookOffset; // текущий сдвиг «взгляда вперёд»

        // без дрожи — якорь для параллакса
        public Vector3 AnchorPosition { get; private set; } // позиция камеры без шейка

        public void SetTarget(Ship target) // задать цель слежения
        {
            ship = target;
        }

        private void Awake() // инициализация камеры
        {
            cam = GetComponent<Camera>(); // свой компонент камеры
            Instance = this; // регистрация синглтона
            noiseSeed = Random.value * 100f; // случайный сид шума
            AnchorPosition = transform.position; // стартовый якорь
            if (ship == null) // цель не задана в инспекторе
                ship = FindAnyObjectByType<Ship>(); // найти корабль
        }

        private void LateUpdate() // после всей логики кадра — камера последняя
        {
            if (ship == null) // цели нет — ничего не делаем
                return;
            // Пауза (модал завершения, меню ESC) ставит timeScale=0, но
            // LateUpdate живёт в реальном времени: в scaled-времени камера
            // замирала со смещением lookahead и после закрытия модала видимо
            // «уплывала» доседать на корабль (Н-9). На unscaled — стоит на
            // корабле уже во время модала, после закрытия догонять нечего
            float dt = Time.timeScale > 0f ? Time.deltaTime : Time.unscaledDeltaTime; // время кадра

            // взгляд вперёд по курсу
            Vector2 velocity = ship.Body != null ? ship.Body.linearVelocity : Vector2.zero; // скорость корабля
            Vector2 targetOffset = ship.State == ShipState.Flying // в полёте
                ? Vector2.ClampMagnitude(velocity * LookaheadSeconds, LookaheadMax) // смещение вперёд с потолком
                : Vector2.zero; // на стоянке смотрим на корабль
            lookOffset += (targetOffset - lookOffset) * Mathf.Min(1f, 3f * dt); // плавный сдвиг взгляда

            Vector3 pos = transform.position; // текущая позиция
            Vector2 target = (Vector2)ship.transform.position + lookOffset; // цель — корабль плюс взгляд
            pos.x += (target.x - pos.x) * Mathf.Min(1f, 3.5f * dt); // экспоненциальное слежение X
            pos.y += (target.y - pos.y) * Mathf.Min(1f, 3.5f * dt); // и Y
            AnchorPosition = pos; // якорь без тряски

            // дрожь — шум Перлина вместо рваного рандома; гаснет и шумит в
            // реальном времени — на паузе не висит застывшим смещением
            trauma = Mathf.Max(0f, trauma - ShakeDecay * dt); // спад тряски
            float shake = trauma * trauma; // квадрат trauma — «мягкий старт»
            float t = Time.unscaledTime; // реальное время шума
            Vector2 shakeOffset = new Vector2( // смещение от шума
                Mathf.PerlinNoise(noiseSeed, t * 14f) * 2f - 1f, // шум X
                Mathf.PerlinNoise(noiseSeed + 31f, t * 14f) * 2f - 1f) // шум Y
                * (ShakeAmplitude * shake); // амплитуда по trauma
            transform.position = pos + (Vector3)shakeOffset; // позиция с дрожью
            transform.rotation = Quaternion.Euler(0f, 0f, // поворот камеры
                (Mathf.PerlinNoise(noiseSeed + 77f, t * 14f) * 2f - 1f) * ShakeRotation * shake); // крен от шума

            // зум: посадка ближе, скорость дальше
            if (controller == null && ship != null) // контроллер ещё не найден
                controller = ship.GetComponent<ShipController>(); // одноразовый поиск
            bool landing = ship.State == ShipState.Landed // стоит
                || (controller != null && controller.LandingRetro); // или ретро-тормоз
            float vNorm = ship.ShipConfig != null // доля максимальной скорости
                ? Mathf.Clamp01(velocity.magnitude / ship.ShipConfig.MaxV)
                : 0f;
            float viewHeight = landing // целевая высота вида
                ? LandingViewHeight // при посадке — ближе
                : Mathf.Lerp(FlightViewHeight, FastViewHeight, vNorm); // в полёте — по скорости
            float targetSize = viewHeight * 0.5f; // orthographicSize = полвысоты
            cam.orthographicSize += (targetSize - cam.orthographicSize) * Mathf.Min(1f, 3f * dt); // плавный зум
        }

        // ~0.25 — взлёт, ~0.5 — жёсткий удар, ~0.85 — гибель
        public static void AddShake(float power) // добавить тряску извне
        {
            if (Instance != null) // камера существует
                Instance.trauma = Mathf.Min(1f, Instance.trauma + power); // накопить с потолком
        }
    }
}
