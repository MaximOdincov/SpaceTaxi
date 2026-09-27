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
                return;

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

            if (v > config.MinControlSpeed)
            {
                float target = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - spriteNoseAngle;
                float current = ship.Body.rotation;
                float delta = Mathf.DeltaAngle(current, target);
                ship.Body.MoveRotation(current + delta * Mathf.Min(1f, config.HeadingTurnRate * dt));
            }
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
