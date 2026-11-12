using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceTaxi.UI
{
    // Яйцо-такси на фоне меню (бриф E.2/E.3 + E5 + E7.3): дрейфует по плавной
    // траектории (сумма двух синусоид — Лиссажу), по клику ЛЕНИВО летит в
    // точку клика с easing smoothstep — скорость и доворот заметно ниже
    // прежних; по прибытии медленно продолжает дрейф вокруг новой точки.
    // E7.3: при наведении курсора (без клика) яйцо слабо тянется к нему —
    // вес HoverWeight от клик-поведения, поверх дрейфа Лиссажу; клик всегда
    // в приоритете (в полёте hover-смещение заморожено на нуле).
    // Спрайт смотрит носом вверх — визуальный угол = atan2(velocity) − 90°,
    // сглаживается LerpAngle.
    public sealed class MenuEggShip : MonoBehaviour
    {
        private const float FlySpeed = 250f;        // px/с канваса (E5: было 620)
        private const float FlySpeedMin = 0.9f;     // с — короче нет смысла
        private const float FlySpeedMax = 3.4f;     // с
        private const float TurnResponsiveness = 2.0f; // скорость доворота (E5: было 5.5)
        private const float DriftRampTime = 2.6f;   // разгон дрейфа после прибытия (ленивый)
        private const float FadeInTime = 1.2f;      // появление при входе в меню
        private const float BoundsMargin = 150f;    // запас от краёв канваса

        // E7.3, ручки владельца: слабое следование за курсором при наведении.
        // HoverWeight — доля от клик-поведения: и путь к курсору (30%), и
        // скорость подтягивания (FlySpeed * HoverWeight); HoverMaxOffset —
        // потолок смещения от траектории Лиссажу, чтобы далёкий курсор не
        // утаскивал яйцо за полэкрана
        private const float HoverWeight = 0.3f;
        private const float HoverMaxOffset = 160f;

        private enum State { Drift, Fly }

        private RectTransform rect;
        private CanvasGroup fade;
        private Vector2 halfBounds;
        private bool ready;

        private State state = State.Drift;

        // дрейф: позиция = центр + смещение(τ) * плавный разгон; частоты и
        // размах занижены (E5) — медленное ленивое парение
        private Vector2 driftCenter;
        private Vector2 driftAmplitude; // 10% ширины / 13% высоты канваса
        private readonly Vector2 driftFrequency = new(0.21f, 0.16f); // рад/с
        private const float driftPhase = 0.9f; // рассогласование осей — траектория без «креста»
        private float driftTime;

        // перелёт к точке клика
        private Vector2 flyFrom, flyTo;
        private float flyDuration, flyTime;

        // E7.3: hover-дрейф к курсору — точка курсора (в координатах канваса),
        // активность и текущее смещение поверх траектории Лиссажу
        private Vector2 hoverPoint;
        private bool hoverActive;
        private Vector2 hoverOffset;

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
            driftAmplitude = new Vector2(canvasSize.x * 0.10f, canvasSize.y * 0.13f);
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
        // Повторный клик в полёте перенацеливает из текущей позиции.
        // hover-смещение обнуляем: оно уже «запечено» в flyFrom (текущая
        // отрисованная позиция), в полёте клик — в приоритете
        public void FlyTo(Vector2 localPoint)
        {
            if (!ready)
                return;
            flyFrom = rect.anchoredPosition;
            flyTo = ClampToBounds(localPoint);
            float distance = Vector2.Distance(flyFrom, flyTo);
            flyDuration = Mathf.Clamp(distance / FlySpeed, FlySpeedMin, FlySpeedMax);
            flyTime = 0f;
            hoverOffset = Vector2.zero;
            state = State.Fly;
        }

        // E7.3: курсор над меню — яйцо слегка тянется к нему (вес HoverWeight
        // от клик-поведения). Скорость и клампы те же, что у клика, но с
        // весом; вызывается каждый кадр из MenuBackdropController
        public void HoverAt(Vector2 localPoint)
        {
            if (!ready)
                return;
            hoverPoint = ClampToBounds(localPoint);
            hoverActive = true;
        }

        // мышь исчезла (например, ввод с геймпада) — дрейф к курсору гаснет
        public void HoverOff() => hoverActive = false;

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

            // E7.3: слабый дрейф к курсору при наведении — только в покое
            // (в полёте приоритет клика), поверх дрейфа Лиссажу. Смещение
            // подтягивается к цели с «клик-скоростью», умноженной на вес,
            // а его производная подмешивается в velocity — нос слегка
            // подаётся в сторону курсора
            if (state == State.Drift)
            {
                Vector2 hoverTarget = hoverActive ? (hoverPoint - position) * HoverWeight : Vector2.zero;
                if (hoverTarget.sqrMagnitude > HoverMaxOffset * HoverMaxOffset)
                    hoverTarget = hoverTarget.normalized * HoverMaxOffset;
                Vector2 previous = hoverOffset;
                hoverOffset = Vector2.MoveTowards(hoverOffset, hoverTarget, FlySpeed * HoverWeight * dt);
                velocity += dt > 1e-5f ? (hoverOffset - previous) / dt : Vector2.zero;
                position += hoverOffset;
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
