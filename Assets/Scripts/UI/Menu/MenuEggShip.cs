using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceTaxi.UI
{
    // Яйцо-такси на фоне меню (бриф E.2/E.3): дрейфует по плавной траектории
    // (сумма двух синусоид — Лиссажу), по клику летит в точку клика с easing
    // smoothstep, лёгкий поворот по направлению движения; по прибытии продолжает
    // дрейф вокруг новой точки. Спрайт смотрит носом вверх — визуальный угол
    // = atan2(velocity) − 90°, сглаживается LerpAngle.
    public sealed class MenuEggShip : MonoBehaviour
    {
        private const float FlySpeed = 620f;        // px/с канваса
        private const float FlySpeedMin = 0.45f;    // с — короче нет смысла
        private const float FlySpeedMax = 1.6f;     // с
        private const float TurnResponsiveness = 5.5f; // скорость доворота
        private const float DriftRampTime = 1.6f;   // разгон дрейфа после прибытия
        private const float FadeInTime = 1.2f;      // появление при входе в меню
        private const float BoundsMargin = 150f;    // запас от краёв канваса

        private enum State { Drift, Fly }

        private RectTransform rect;
        private CanvasGroup fade;
        private Vector2 halfBounds;
        private bool ready;

        private State state = State.Drift;

        // дрейф: позиция = центр + смещение(τ) * плавный разгон
        private Vector2 driftCenter;
        private Vector2 driftAmplitude; // 13.5% ширины / 17.5% высоты канваса
        private readonly Vector2 driftFrequency = new(0.42f, 0.31f); // рад/с
        private const float driftPhase = 0.9f; // рассогласование осей — траектория без «креста»
        private float driftTime;

        // перелёт к точке клика
        private Vector2 flyFrom, flyTo;
        private float flyDuration, flyTime;

        private float currentAngle;
        private Vector3 baseScale = Vector3.one;

        public bool Ready => ready;

        // спрайт яйца (яйцо1.png). Прямой путь не работает: имя ФАЙЛА на диске
        // в NFD («й» = и + U+0306), а литерал в коде — NFC; папка при этом NFC.
        // Поэтому грузим LoadAll по папке и сравниваем имена, нормализовав
        // обе стороны к NFC
        private const string EggFolder = "Art/ships/яйцо";
        private const string EggSpriteName = "яйцо1";

        public static Sprite LoadSprite()
        {
            Sprite[] pool = Resources.LoadAll<Sprite>(EggFolder);
            string wanted = EggSpriteName.Normalize(NormalizationForm.FormC);
            foreach (Sprite sprite in pool)
                if (sprite.name.Normalize(NormalizationForm.FormC) == wanted)
                    return sprite;
            return null;
        }

        public void Init(Vector2 canvasSize)
        {
            rect = (RectTransform)transform;
            halfBounds = canvasSize * 0.5f - new Vector2(BoundsMargin, BoundsMargin);

            // якорь дрейфа — правая половина экрана (терминальная панель слева),
            // амплитуды масштабируются от размера канваса
            driftCenter = new Vector2(canvasSize.x * 0.16f, canvasSize.y * 0.08f);
            driftAmplitude = new Vector2(canvasSize.x * 0.135f, canvasSize.y * 0.175f);
            driftTime = 0f;
            state = State.Drift;

            currentAngle = -35f; // нос вправо-вверх
            rect.anchoredPosition = driftCenter;
            rect.localEulerAngles = new Vector3(0f, 0f, currentAngle);

            fade = GetComponent<CanvasGroup>();
            if (fade == null)
                fade = gameObject.AddComponent<CanvasGroup>();
            fade.alpha = 0f;

            ready = true;
        }

        // Клик по экрану: лететь в точку (локальные координаты канваса).
        // Повторный клик в полёте перенацеливает из текущей позиции
        public void FlyTo(Vector2 localPoint)
        {
            if (!ready)
                return;
            flyFrom = rect.anchoredPosition;
            flyTo = ClampToBounds(localPoint);
            float distance = Vector2.Distance(flyFrom, flyTo);
            flyDuration = Mathf.Clamp(distance / FlySpeed, FlySpeedMin, FlySpeedMax);
            flyTime = 0f;
            state = State.Fly;
        }

        private void Update()
        {
            if (!ready)
                return;

            float dt = Time.unscaledDeltaTime;
            float time = Time.unscaledTime;

            Vector2 position;
            Vector2 velocity;
            if (state == State.Fly)
            {
                flyTime += dt;
                float k = Mathf.Clamp01(flyTime / flyDuration);
                float eased = k * k * (3f - 2f * k); // smoothstep — бриф E.3
                position = Vector2.LerpUnclamped(flyFrom, flyTo, eased);
                velocity = (flyTo - flyFrom); // направление постоянно, поворот предсказуем
                if (k >= 1f)
                {
                    state = State.Drift;
                    driftTime = 0f;
                    driftCenter = flyTo; // прибыл — дрейф вокруг точки клика
                }
            }
            else
            {
                driftTime += dt;
                float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(driftTime / DriftRampTime));
                Vector2 offset = new(
                    driftAmplitude.x * Mathf.Sin(driftFrequency.x * driftTime),
                    driftAmplitude.y * Mathf.Sin(driftFrequency.y * driftTime + driftPhase));
                position = driftCenter + offset * ramp;
                velocity = new Vector2(
                    driftAmplitude.x * driftFrequency.x * Mathf.Cos(driftFrequency.x * driftTime),
                    driftAmplitude.y * driftFrequency.y * Mathf.Cos(driftFrequency.y * driftTime + driftPhase)) * ramp;
            }

            rect.anchoredPosition = position;

            // лёгкий поворот по направлению движения
            if (velocity.sqrMagnitude > 1f)
            {
                float target = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - 90f;
                currentAngle = Mathf.LerpAngle(currentAngle, target, 1f - Mathf.Exp(-TurnResponsiveness * dt));
                rect.localEulerAngles = new Vector3(0f, 0f, currentAngle);
            }

            // появление и «дыхание»
            if (fade.alpha < 1f)
                fade.alpha = Mathf.Clamp01(fade.alpha + dt / FadeInTime);
            float breathe = 1f + 0.018f * Mathf.Sin(time * 2.1f);
            rect.localScale = baseScale * breathe;
        }

        private Vector2 ClampToBounds(Vector2 point) => new(
            Mathf.Clamp(point.x, -halfBounds.x, halfBounds.x),
            Mathf.Clamp(point.y, -halfBounds.y, halfBounds.y));
    }
}
