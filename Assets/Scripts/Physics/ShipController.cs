using System.Collections.Generic; // множество тел внутри колец
using UnityEngine; // MonoBehaviour
using UnityEngine.InputSystem; // новый Input System
using SpaceTaxi.Economy; // конфиги статов
using SpaceTaxi.World; // тела и планеты

namespace SpaceTaxi.Physics
{
    // Ввод → скорость: собственная физика полёта (Эйлер в FixedUpdate)
    public class ShipController : MonoBehaviour // сердце управления
    {
        [SerializeField] private InputActionAsset input; // ассет ввода
        [Tooltip("Куда смотрит нос спрайта при нулевом повороте: 0 = вправо, 90 = вверх")]
        [SerializeField] private float spriteNoseAngle = 0f; // базовый угол носа арта

        public float SpriteNoseAngle => spriteNoseAngle; // угол для подмены ориентаций

        private Ship ship; // модель борта
        private GravityField gravity; // реестр тел
        private PhysicsConfig config; // физический баланс
        private ShipConfig shipConfig; // статы корпуса
        private LandingSystem landing; // посадка (для взлёта)
        private InputAction thrustAction; // действие тяги
        private InputAction brakeAction; // действие тормоза
        private InputAction strafeAction; // действие стрейфа

        private readonly HashSet<CelestialBody> insideRing = new HashSet<CelestialBody>(); // тела, в чьих кольцах мы внутри

        // лог порогов топлива — по одному разу
        private bool lowFuelLogged; // предупреждение 10% уже выводилось
        private bool emptyFuelLogged; // «бак пуст» уже выводилось

        // «липкая» посадочная ориентация: тормоз в кольце разворачивает
        // корабль двигателями к планете и держит до посадки/выхода/тяги
        private bool retroAttitude; // нос против скорости

        // нос против скорости — для камеры и прогноза
        public bool LandingRetro => retroAttitude; // признак ретро-позиции

        // для эффектов/индикации, пустой бак учтён
        public bool ThrustOn { get; private set; } // тяга нажата
        public bool BrakeOn { get; private set; } // тормоз нажат
        public float StrafeLevel { get; private set; } // уровень боковой тяги

        // сохранённый курс: на остановке по нему разворачиваемся и стартуем
        private Vector2 headingDir = Vector2.up; // текущий курс корабля
        // grace после взлёта: корабль поднимается в той ориентации, в которой стоял
        private float headingHoldUntil; // время удержания ориентации

        public bool InLandingRing { get; private set; } // внутри посадочного кольца

        // 1 обычно, EngineMultiplier у поверхности
        public float EnginePower { get; private set; } = 1f; // множитель тяги

        public void SetInput(InputActionAsset asset) // задать ассет ввода извне
        {
            input = asset;
            BindActions(); // перепривязать действия
        }

        private void Awake() // бинд соседей по кораблю
        {
            ship = GetComponent<Ship>(); // модель
            gravity = FindAnyObjectByType<GravityField>(); // реестр тел
            landing = GetComponent<LandingSystem>(); // посадка
        }

        private void OnEnable() // при включении — привязать ввод
        {
            BindActions();
        }

        // Привязка из OnEnable и из SetInput — кто раньше
        private void BindActions() // найти и включить действия корабля
        {
            InputActionMap map = input != null ? input.FindActionMap("Ship") : null; // карта "Ship"
            if (map == null) // ассет/карта нет
                return;
            thrustAction = map.FindAction("Thrust"); // тяга
            brakeAction = map.FindAction("Brake"); // тормоз
            strafeAction = map.FindAction("Strafe"); // стрейф
            map.Enable(); // включить карту
        }

        private void OnDisable() // при выключении — отключить ввод
        {
            if (input != null)
                input.FindActionMap("Ship")?.Disable(); // выключить карту
        }

        private void FixedUpdate() // физический шаг управления
        {
            config = ship != null ? ship.Config : null; // физический баланс
            shipConfig = ship != null ? ship.ShipConfig : null; // статы корпуса
            if (config == null || shipConfig == null) // не инициализирован
                return;

            if (ship.State != ShipState.Flying) // не в полёте — ввод гасим
            {
                SilenceControls();
                return;
            }

            Vector2 velocity = ship.Body.linearVelocity; // текущая скорость
            Vector2 pos = ship.transform.position; // позиция
            float dt = Time.fixedDeltaTime; // шаг физики

            Planet planet; // ближайшая планета
            float planetDist; // дистанция до неё
            float v = ApplyGravityAndRings(ref velocity, pos, dt, out planet, out planetDist); // гравитация, качели, ближайшая планета

            // Посадочный режим
            float engMul = 1f; // базовый множитель тяги
            if (planet != null && planetDist < planet.LandingRing) // внутри посадочного кольца
                engMul = config.EngineMultiplier; // тяга усилена
            InLandingRing = engMul > 1f; // признак кольца
            EnginePower = engMul; // для эффектов/индикации

            ReadEngines(dt, out bool thrustOn, out bool brakeOn, out float strafe); // двигатели + топливо

            UpdateRetroAttitude(v, planet, planetDist, thrustOn, brakeOn); // вход/выход липкой ориентации

            UpdateFuelLog(); // пороги — лог по одному разу

            // Все ускорения — к вектору скорости
            if (v > config.MinControlSpeed) // корабль движется
                ApplyMovingControl(ref velocity, v, pos, dt, thrustOn, brakeOn, strafe, engMul); // разгон/тормоз/стрейф
            else
                ApplyStoppedControl(ref velocity, dt, thrustOn, strafe, engMul); // разворот на месте и толчок

            ship.Body.linearVelocity = velocity; // записать итоговую скорость

            UpdateHeading(velocity, v, planet, planetDist); // доворот носа
        }

        // не в полёте: индикацию гасим, «липкая» ориентация сброшена
        private void SilenceControls()
        {
            ThrustOn = false; // тяга выкл
            BrakeOn = false; // тормоз выкл
            StrafeLevel = 0f; // стрейф нулевой
            InLandingRing = false; // вне колец
            EnginePower = 1f; // базовая тяга
            retroAttitude = false; // ориентация сброшена
        }

        // гравитация от всех тел, качели в кольцах захвата и ближайшая планета;
        // возвращает модуль скорости после гравитации/буста, velocity меняется на месте
        private float ApplyGravityAndRings(ref Vector2 velocity, Vector2 pos, float dt,
            out Planet planet, out float planetDist)
        {
            // Гравитация от всех тел
            if (gravity != null) // реестр есть
                velocity += gravity.GetAccelerationAt(pos, config) * dt; // добавить притяжение

            float v = velocity.magnitude; // модуль скорости

            // Качель + ближайшая планета
            planet = null; // ближайшая планета
            planetDist = float.MaxValue; // дистанция до неё
            if (gravity != null) // реестр есть
            {
                foreach (CelestialBody body in gravity.Bodies) // по всем телам
                {
                    float d = Vector2.Distance(body.transform.position, pos); // дистанция до тела
                    float ring = body.CaptureRing; // радиус кольца тела
                    bool wasInside = insideRing.Contains(body); // были внутри на прошлом шаге
                    if (d < ring) // вошли/остаёмся в кольце
                    {
                        // качели — только у звёзд и чёрных дыр: у планет
                        // буст отключён, сама гравитация и посадка не тронуты
                        if (!(body is Planet) && !wasInside // впервые вошли не у планеты
                            && v > PhysicsConfig.EscapeVelocity(config.GravParamOf(body), d)) // быстрее убегания
                        {
                            velocity *= ship.Eff.GravityBoost; // улучшение двигателя усиливает качели
                            v = velocity.magnitude; // пересчитать модуль
                        }
                        insideRing.Add(body); // запомнить «внутри»
                    }
                    else if (wasInside) // вышли из кольца
                    {
                        insideRing.Remove(body); // забыть
                    }

                    if (body is Planet p && d < planetDist) // ближайшая планета
                    {
                        planet = p;
                        planetDist = d;
                    }
                }
            }

            return v;
        }

        // читаем действия ввода и списываем топливо за любую тягу;
        // пустой бак или заглушенный борт — двигатели молчат
        private void ReadEngines(float dt, out bool thrustOn, out bool brakeOn, out float strafe)
        {
            thrustOn = thrustAction != null && thrustAction.IsPressed(); // тяга нажата
            brakeOn = brakeAction != null && brakeAction.IsPressed(); // тормоз нажат
            strafe = strafeAction != null ? strafeAction.ReadValue<float>() : 0f; // уровень стрейфа

            // Топливо: расход на любую тягу, пустой бак — двигатели молчат
            if (thrustOn || brakeOn || Mathf.Abs(strafe) > 0f) // есть активные двигатели
                ship.Fuel -= ship.Eff.FuelDrain * dt; // расход за шаг
            if (ship.Fuel <= 0f) // бак пуст
            {
                thrustOn = false; // тяга молчит
                brakeOn = false;
                strafe = 0f;
            }
            // борт заглушен в чёрной дыре — двигатели молчат, воронка дотягивает
            // до конца окна
            if (ship.PoweredDown) // борт заглушен
            {
                thrustOn = false; // все двигатели молчат
                brakeOn = false;
                strafe = 0f;
            }
            ThrustOn = thrustOn; // для эффектов
            BrakeOn = brakeOn;
            StrafeLevel = strafe;
        }

        // Вход/выход липкой ориентации
        private void UpdateRetroAttitude(float v, Planet planet, float planetDist, bool thrustOn, bool brakeOn)
        {
            bool finalApproach = InLandingRing && planet != null // финальное сближение
                && planetDist < planet.Radius + 40f && v < 50f; // близко и медленно
            if (retroAttitude) // уже в ретро-позиции
            {
                if (!InLandingRing || thrustOn) // вышли из кольца или дал тягу
                    retroAttitude = false; // снять ретро
            }
            else if (InLandingRing && !finalApproach && brakeOn && ship.Fuel > 0f) // тормозим в кольце
            {
                retroAttitude = true; // встать двигателями к планете
            }
        }

        // Пороги — лог по одному разу
        private void UpdateFuelLog()
        {
            if (ship.Fuel > ship.Eff.FuelMax * 0.1f) // бак выше 10%
            {
                lowFuelLogged = false; // сброс флагов лога
                emptyFuelLogged = false;
            }
            else if (!lowFuelLogged) // впервые ниже 10%
            {
                lowFuelLogged = true;
#if UNITY_EDITOR
                Debug.Log("[Fuel] меньше 10% — ТОПЛИВО КОНЧАЕТСЯ"); // лог только в редакторе
#endif
            }
            if (ship.Fuel <= 0f && !emptyFuelLogged) // бак впервые пуст
            {
                emptyFuelLogged = true;
#if UNITY_EDITOR
                Debug.Log("[Fuel] бак пуст — двигатели не работают");
#endif
            }
        }

        // разгон по курсу, торможение с анти-ховером и боковая тяга
        private void ApplyMovingControl(ref Vector2 velocity, float v, Vector2 pos, float dt,
            bool thrustOn, bool brakeOn, float strafe, float engMul)
        {
            headingDir = velocity / v; // курс живёт, пока корабль движется
            Vector2 dir = headingDir; // то же деление — без повторного
            if (thrustOn && v < shipConfig.MaxV) // тяга до потолка скорости
                velocity += dir * (ship.Eff.Thrust * engMul * dt); // разгон по курсу
            if (brakeOn) // торможение
            {
                // Анти-ховер: тормоз не может полностью погасить гравитацию —
                // у падающего корабля (скорость по направлению гравитации)
                // остаётся мин. скорость падения = BrakeHoverFraction × локальная
                // гравитация. В пустоте пол нулевой — можно остановиться. Тормоз
                // никогда не разгоняет (nv зажат и сверху текущей v) — джиттера
                // на пороге нет.
                Vector2 gravityNow = gravity != null // локальная гравитация
                    ? gravity.GetAccelerationAt(pos, config)
                    : Vector2.zero;
                float brakeFloor = gravityNow.magnitude > 0f // падаем по гравитации
                    && Vector2.Dot(velocity, gravityNow) > 0f
                    ? config.BrakeHoverFraction * gravityNow.magnitude // мин. скорость падения
                    : 0f;
                float nv = Mathf.Clamp( // новая скорость торможения
                    v - ship.Eff.Retro * engMul * dt, // гасим тормозом
                    Mathf.Min(brakeFloor, v), // не ниже «пола» и не выше текущей
                    v); // тормоз не разгоняет
                velocity *= nv / Mathf.Max(0.0001f, v); // нормировать к новой скорости
            }
            if (Mathf.Abs(strafe) > 0f) // боковая тяга
            {
                Vector2 perp = new Vector2(dir.y, -dir.x); // перпендикуляр курсу
                float maxTurn = config.StrafeMaxTurnRate * Mathf.Deg2Rad; // потолок угловой скорости
                float strafeAcc = Mathf.Min(ship.Eff.Strafe, v * maxTurn); // ускорение стрейфа
                velocity += perp * (strafe * strafeAcc * engMul * dt); // боковое ускорение
            }
        }

        // остановившийся корабль: A/D — разворот на месте, SPACE — толчок
        // по носу (старое поведение «кнопки мертвы до разгона»
        // выглядело нелогично — жалоба плейтеста)
        private void ApplyStoppedControl(ref Vector2 velocity, float dt, bool thrustOn, float strafe, float engMul)
        {
            if (Mathf.Abs(strafe) > 0f) // разворот на месте
            {
                float turn = -strafe * config.StrafeMaxTurnRate * Mathf.Deg2Rad * engMul * dt; // шаг поворота
                headingDir = new Vector2( // повернуть вектор курса
                    headingDir.x * Mathf.Cos(turn) - headingDir.y * Mathf.Sin(turn), // поворот X
                    headingDir.x * Mathf.Sin(turn) + headingDir.y * Mathf.Cos(turn)); // поворот Y
                float nose = Mathf.Atan2(headingDir.y, headingDir.x) * Mathf.Rad2Deg - spriteNoseAngle; // мировой угол носа
                float current = ship.Body.rotation; // текущий поворот
                float step = Mathf.Clamp( // шаг к цели с потолком скорости
                    Mathf.DeltaAngle(current, nose) * config.HeadingTurnRate * dt,
                    -config.HeadingMaxTurnSpeed * dt,
                    config.HeadingMaxTurnSpeed * dt);
                ship.Body.MoveRotation(current + step); // повернуть тело
            }
            if (thrustOn) // толчок с места
                velocity = headingDir * (ship.Eff.Thrust * engMul * dt); // старт по курсу
        }
        // Нос: обычно по вектору скорости, на посадке — ретро или вертикально от планеты
        // взлёт: не разворачивать нос сразу — подъём «низом вперёд», как стоял
        public void HoldHeading(float seconds) // удержать ориентацию
        {
            headingHoldUntil = Time.time + seconds;
        }

        private void UpdateHeading(Vector2 velocity, float v, Planet planet, float planetDist) // доворот носа
        {
            // grace снимается боковой тягой: игрок сам просит повернуть
            if (Time.time < headingHoldUntil && Mathf.Abs(StrafeLevel) < 0.1f) // grace активен
                return;
            bool inLandingRing = planet != null && planetDist < planet.LandingRing; // в посадочном кольце
            bool finalApproach = inLandingRing // финальное сближение
                && planetDist < planet.Radius + 40f // близко к поверхности
                && v < 50f; // и медленно

            // у нуля скорости вектор «шумит» — держим текущую ориентацию
            if (!finalApproach && v < 30f) // слишком медленно
                return;

            float velocityAngle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg; // угол скорости
            float targetWorld; // целевой угол носа в мире
            if (finalApproach && planet != null) // на финальном сближении
            {
                Vector2 away = (Vector2)ship.transform.position - (Vector2)planet.transform.position; // от планеты
                targetWorld = Mathf.Atan2(away.y, away.x) * Mathf.Rad2Deg; // нос наружу (двигателями к поверхности)
            }
            else if (retroAttitude) // ретро-позиция
            {
                targetWorld = velocityAngle + 180f; // двигателями вперёд по движению
            }
            else
            {
                targetWorld = velocityAngle; // нос по скорости
            }

            float target = targetWorld - spriteNoseAngle; // целевой поворот тела
            float current = ship.Body.rotation; // текущий поворот
            float delta = Mathf.DeltaAngle(current, target); // разница углов
            // с пределом угловой скорости — иначе у нуля скорости нос «щёлкает»
            float dt = Time.fixedDeltaTime; // шаг
            float step = Mathf.Clamp( // шаг доворота
                delta * config.HeadingTurnRate * dt, // пропорциональный
                -config.HeadingMaxTurnSpeed * dt, // потолок скорости влево
                config.HeadingMaxTurnSpeed * dt); // и вправо
            ship.Body.MoveRotation(current + step); // повернуть
        }

        private void Update() // вне физики: заморозка на стоянке и взлёт
        {
            if (ship == null) // модели нет
                return;

            // на стоянке корпус заморожен: капсула касается планеты, солвер
            // раскручивал корабль вокруг оси, выравнивание приходило с разгоном
            if (ship.Body != null) // тело есть
            {
                if (ship.State == ShipState.Landed) // стоит на планете
                {
                    if (!ship.Body.freezeRotation) // вращение ещё живое
                    {
                        ship.Body.angularVelocity = 0f; // обнулить раскрутку
                        ship.Body.freezeRotation = true; // заморозить вращение
                    }
                }
                else if (ship.Body.freezeRotation) // слетели с планеты
                {
                    ship.Body.freezeRotation = false; // разморозить
                }
            }

            // Взлёт — только на живом кадре: Update и Input System живут в
            // реальном времени, timeScale=0 (пауза, модал завершения заказа)
            // их не останавливает. Space = Thrust и «продолжить» в UI — без
            // гейта закрывающий модал клик взлетал тем же кадром, и камера
            // уходила с места доставки
            if (Time.timeScale <= 0f) // пауза
            {
                ThrustOn = false; // индикация гасим
                BrakeOn = false;
                StrafeLevel = 0f;
                return;
            }

            // с пустым баком взлёт невозможен
            if (ship.State == ShipState.Landed && ship.Fuel > 0f // стоит и есть топливо
                && thrustAction != null && thrustAction.WasPressedThisFrame()) // тяга нажата
            {
                landing.Takeoff(); // взлёт
            }
        }
    }
}
