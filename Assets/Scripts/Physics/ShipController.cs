using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    public class ShipController : MonoBehaviour
    {
        [SerializeField] private InputActionAsset input;
        [Tooltip("Куда смотрит нос спрайта при нулевом повороте: 0 = вправо, 90 = вверх")]
        [SerializeField] private float spriteNoseAngle = 0f;

        public float SpriteNoseAngle => spriteNoseAngle;

        private Ship ship;
        private GravityField gravity;
        private PhysicsConfig config;
        private LandingSystem landing;
        private InputAction thrustAction;
        private InputAction brakeAction;
        private InputAction strafeAction;

        private readonly HashSet<CelestialBody> insideRing = new HashSet<CelestialBody>();

        // Пороги топлива: лог один раз (в задаче 4 заменит баннер).
        private bool lowFuelLogged;
        private bool emptyFuelLogged;

        // Липкая посадочная ориентация: тормоз в кольце разворачивает корабль
        // двигателями к планете — и держит так до посадки, выхода из кольца
        // или тяги (W). Отпускание S больше не разворачивает нос обратно.
        private bool retroAttitude;

        // Ретро-режим посадки: нос против скорости (для камеры/прогноза).
        public bool LandingRetro => retroAttitude;

        // Состояние двигателей для эффектов/индикации — пустой бак учтён.
        public bool ThrustOn { get; private set; }
        public bool BrakeOn { get; private set; }
        public float StrafeLevel { get; private set; }

        /// <summary>Ближайшая планета ближе посадочного кольца.</summary>
        public bool InLandingRing { get; private set; }

        /// <summary>Текущая тяга двигателей: 1 обычно, EngineMultiplier у поверхности.</summary>
        public float EnginePower { get; private set; } = 1f;

        public void SetInput(InputActionAsset asset)
        {
            input = asset;
            BindActions();
        }

        private void Awake()
        {
            ship = GetComponent<Ship>();
            gravity = FindAnyObjectByType<GravityField>();
            landing = GetComponent<LandingSystem>();
        }

        private void OnEnable()
        {
            BindActions();
        }

        // Привязка действий: из OnEnable и из SetInput — кто раньше.
        private void BindActions()
        {
            InputActionMap map = input != null ? input.FindActionMap("Ship") : null;
            if (map == null)
                return;
            thrustAction = map.FindAction("Thrust");
            brakeAction = map.FindAction("Brake");
            strafeAction = map.FindAction("Strafe");
            map.Enable();
        }

        private void OnDisable()
        {
            if (input != null)
                input.FindActionMap("Ship")?.Disable();
        }

        private void FixedUpdate()
        {
            config = ship != null ? ship.Config : null;
            if (config == null)
                return;

            if (ship.State != ShipState.Flying)
            {
                ThrustOn = false;
                BrakeOn = false;
                StrafeLevel = 0f;
                InLandingRing = false;
                EnginePower = 1f;
                retroAttitude = false;
                return;
            }

            Vector2 velocity = ship.Body.linearVelocity;
            Vector2 pos = ship.transform.position;
            float dt = Time.fixedDeltaTime;

            // Гравитация от всех тел.
            if (gravity != null)
                velocity += gravity.GetAccelerationAt(pos, config) * dt;

            float v = velocity.magnitude;

            // Качель + ближайшая планета (для посадочного режима).
            Planet planet = null;
            float planetDist = float.MaxValue;
            if (gravity != null)
            {
                foreach (CelestialBody body in gravity.Bodies)
                {
                    float d = Vector2.Distance(body.transform.position, pos);
                    float ring = body.CaptureRing;
                    bool wasInside = insideRing.Contains(body);
                    if (d < ring)
                    {
                        if (!wasInside && v > PhysicsConfig.EscapeVelocity(config.GravParamOf(body), d))
                        {
                            velocity *= config.GravityBoost;
                            v = velocity.magnitude;
                        }
                        insideRing.Add(body);
                    }
                    else if (wasInside)
                    {
                        insideRing.Remove(body);
                    }

                    if (body is Planet p && d < planetDist)
                    {
                        planet = p;
                        planetDist = d;
                    }
                }
            }

            // Посадочный режим
            float engMul = 1f;
            if (planet != null && planetDist < planet.LandingRing)
                engMul = config.EngineMultiplier;
            InLandingRing = engMul > 1f;
            EnginePower = engMul;

            bool thrustOn = thrustAction != null && thrustAction.IsPressed();
            bool brakeOn = brakeAction != null && brakeAction.IsPressed();
            float strafe = strafeAction != null ? strafeAction.ReadValue<float>() : 0f;

            // Топливо: расход на любую тягу; пустой бак — двигатели молчат.
            if (thrustOn || brakeOn || Mathf.Abs(strafe) > 0f)
                ship.Fuel -= config.FuelDrainPerSecond * dt;
            if (ship.Fuel <= 0f)
            {
                thrustOn = false;
                brakeOn = false;
                strafe = 0f;
            }
            ThrustOn = thrustOn;
            BrakeOn = brakeOn;
            StrafeLevel = strafe;

            // Липкая посадочная ориентация (вход/выход).
            bool finalApproach = InLandingRing && planet != null
                && planetDist < planet.Radius + 40f && v < 50f;
            if (retroAttitude)
            {
                if (!InLandingRing || thrustOn)
                    retroAttitude = false;
            }
            else if (InLandingRing && !finalApproach && brakeOn && ship.Fuel > 0f)
            {
                retroAttitude = true;
            }

            // Пороги для проверки (в задаче 4 заменит баннер).
            if (ship.Fuel > config.FuelMax * 0.1f)
            {
                lowFuelLogged = false;
                emptyFuelLogged = false;
            }
            else if (!lowFuelLogged)
            {
                lowFuelLogged = true;
                Debug.Log("[Fuel] меньше 10% — ТОПЛИВО КОНЧАЕТСЯ");
            }
            if (ship.Fuel <= 0f && !emptyFuelLogged)
            {
                emptyFuelLogged = true;
                Debug.Log("[Fuel] бак пуст — двигатели не работают");
            }

            // Ручное управление — все ускорения к вектору скорости.
            if (v > config.MinControlSpeed)
            {
                Vector2 dir = velocity / v;
                if (thrustOn && v < config.MaxV)
                    velocity += dir * (config.Thrust * engMul * dt);
                if (brakeOn)
                {
                    float nv = Mathf.Max(0f, v - config.Retro * engMul * dt);
                    velocity *= nv / v;
                }
                if (Mathf.Abs(strafe) > 0f)
                {
                    Vector2 perp = new Vector2(dir.y, -dir.x);
                    float maxTurn = config.StrafeMaxTurnRate * Mathf.Deg2Rad;
                    float strafeAcc = Mathf.Min(config.StrafeAcc, v * maxTurn);
                    velocity += perp * (strafe * strafeAcc * engMul * dt);
                }
            }

            ship.Body.linearVelocity = velocity;

            UpdateHeading(velocity, v, planet, planetDist);
        }

        // Ориентация носа — как в жизни при посадке (Falcon):
        // 1) обычно — по вектору скорости;
        // 2) посадочная ориентация (липкая) — на 180°: двигателями вперёд;
        // 3) у поверхности при малой скорости — вертикально, носом от планеты.
        private void UpdateHeading(Vector2 velocity, float v, Planet planet, float planetDist)
        {
            // Финальное выравнивание работает и на малой скорости.
            bool inLandingRing = planet != null && planetDist < planet.LandingRing;
            bool finalApproach = inLandingRing
                && planetDist < planet.Radius + 40f
                && v < 50f;

            // Около нуля скорости вектор скорости «шумит» (гравитация крутит
            // его как хочет) — держим текущую ориентацию, без рывков носом.
            if (!finalApproach && v < 30f)
                return;

            float velocityAngle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            float targetWorld;
            if (finalApproach && planet != null)
            {
                // Вертикаль: нос от центра планеты.
                Vector2 away = (Vector2)ship.transform.position - (Vector2)planet.transform.position;
                targetWorld = Mathf.Atan2(away.y, away.x) * Mathf.Rad2Deg;
            }
            else if (retroAttitude)
            {
                targetWorld = velocityAngle + 180f; // двигателями вперёд по движению
            }
            else
            {
                targetWorld = velocityAngle;
            }

            float target = targetWorld - spriteNoseAngle;
            float current = ship.Body.rotation;
            float delta = Mathf.DeltaAngle(current, target);
            // Доворот: мягкий экспоненциальный, но с пределом угловой скорости —
            // иначе у нуля скорости нос «щёлкает» сотни градусов в секунду.
            float dt = Time.fixedDeltaTime;
            float step = Mathf.Clamp(
                delta * config.HeadingTurnRate * dt,
                -config.HeadingMaxTurnSpeed * dt,
                config.HeadingMaxTurnSpeed * dt);
            ship.Body.MoveRotation(current + step);
        }

        private void Update()
        {
            if (ship == null)
                return;

            // Взлёт требует топлива: с пустым баком стоим (выход — «Отключить корабль»).
            if (ship.State == ShipState.Landed && ship.Fuel > 0f
                && thrustAction != null && thrustAction.WasPressedThisFrame())
            {
                landing.Takeoff();
            }
        }
    }
}
