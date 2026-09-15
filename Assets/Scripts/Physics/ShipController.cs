using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.Core;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    // Управление как в прототипе: тяга вдоль вектора скорости, нос доворачивается сам.
    // Здесь же гравитация и качель — одна точка полёта.
    public class ShipController : MonoBehaviour
    {
        [SerializeField] private InputActionAsset input;
        [SerializeField] private Sprite lineSprite; // полоска вектора скорости (префаб)

        private Ship ship;
        private GravityField gravity;
        private PhysicsConfig config;
        private LandingSystem landing;
        private InputAction thrustAction;
        private InputAction brakeAction;
        private InputAction strafeAction;
        private GameObject velocityIndicator;

        // Качель: тела, внутри кольца которых мы уже находились.
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

            // Полоска перед кораблём — показывает вектор скорости.
            velocityIndicator = new GameObject("VelocityIndicator");
            var indicatorRenderer = velocityIndicator.AddComponent<SpriteRenderer>();
            indicatorRenderer.sprite = lineSprite != null ? lineSprite : StubSprites.Line();
            indicatorRenderer.color = new Color(1f, 0.92f, 0.4f, 0.55f);
            indicatorRenderer.sortingOrder = 1;
            velocityIndicator.SetActive(false);
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

            // Стоим на планете — ввод обрабатывается в Update (взлёт).
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
                    float ring = config.OrbitRingOf(body);
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

            // Посадочный режим — тяга точнее у поверхности планеты.
            float engMul = 1f;
            if (planet != null && planetDist < planet.Radius + config.LandingRingOffset)
                engMul = config.EngineMultiplier;

            bool thrustOn = thrustAction != null && thrustAction.IsPressed();
            bool brakeOn = brakeAction != null && brakeAction.IsPressed();
            float strafe = strafeAction != null ? strafeAction.ReadValue<float>() : 0f;

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
                    // В прототипе Y экрана вниз, в Unity вверх — знак перпендикуляра
                    // противоположный: D (+1) изгибает траекторию вправо.
                    Vector2 perp = new Vector2(dir.y, -dir.x);
                    velocity += perp * (strafe * config.StrafeAcc * engMul * dt);
                }
            }

            ship.Body.linearVelocity = velocity;

            // Нос доворачивается к скорости — через физику, чтобы не спорить
            // с интерполяцией Rigidbody (иначе джиттер).
            if (v > config.MinControlSpeed)
            {
                float target = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
                float current = ship.Body.rotation;
                float delta = Mathf.DeltaAngle(current, target);
                ship.Body.MoveRotation(current + delta * Mathf.Min(1f, config.HeadingTurnRate * dt));
            }
        }

        private void Update()
        {
            if (ship == null)
                return;

            // Взлёт: W или ПРОБЕЛ (обе — тяга). Читаем здесь: WasPressedThisFrame
            // в FixedUpdate пропускает нажатия.
            if (ship.State == ShipState.Landed
                && thrustAction != null && thrustAction.WasPressedThisFrame())
            {
                landing.Takeoff();
            }

            UpdateVelocityIndicator();
        }

        // Полоска перед кораблём — куда движемся.
        private void UpdateVelocityIndicator()
        {
            if (velocityIndicator == null || ship.Body == null)
                return;
            Vector2 v = ship.Body.linearVelocity;
            if (ship.State != ShipState.Flying || v.sqrMagnitude < 4f)
            {
                velocityIndicator.SetActive(false);
                return;
            }
            velocityIndicator.SetActive(true);
            Vector2 dir = v.normalized;
            Vector2 pos = (Vector2)ship.transform.position + dir * 14f;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            velocityIndicator.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 0f, angle));
        }
    }
}
