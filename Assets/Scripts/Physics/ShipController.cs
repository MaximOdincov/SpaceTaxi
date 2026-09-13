using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    // Управление как в прототипе: тяга вдоль вектора скорости, нос доворачивается сам.
    // Здесь же гравитация, автопилот циркуляции и качель — одна точка полёта.
    public class ShipController : MonoBehaviour
    {
        [SerializeField] private InputActionAsset input;

        private Ship ship;
        private GravityField gravity;
        private PhysicsConfig config;
        private InputAction thrustAction;
        private InputAction brakeAction;
        private InputAction strafeAction;

        // Качель: тела, внутри кольца которых мы уже находились.
        private readonly HashSet<CelestialBody> insideRing = new HashSet<CelestialBody>();

        private void Awake()
        {
            ship = GetComponent<Ship>();
            gravity = FindObjectOfType<GravityField>();
        }

        private void OnEnable()
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
            if (config == null || ship.State != ShipState.Flying)
                return;

            Vector2 velocity = ship.Body.linearVelocity;
            Vector2 pos = ship.transform.position;
            float dt = Time.fixedDeltaTime;

            // Гравитация от всех тел.
            if (gravity != null)
                velocity += gravity.GetAccelerationAt(pos, config) * dt;

            float v = velocity.magnitude;

            // Качель + ближайшая планета (для посадочного режима и автопилота).
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
            bool manual = thrustOn || brakeOn || Mathf.Abs(strafe) > 0f;

            // Автопилот: в кольце захвата, без ручного управления — плавная циркуляция.
            if (!manual && planet != null && v > config.MinControlSpeed)
            {
                float ring = config.OrbitRingOf(planet);
                if (planetDist > planet.Radius + 80f && planetDist < ring)
                {
                    float g = config.GravParamOf(planet);
                    if (v < PhysicsConfig.EscapeVelocity(g, planetDist))
                    {
                        Vector2 radial = (pos - (Vector2)planet.transform.position).normalized;
                        float sign = radial.x * velocity.y - radial.y * velocity.x;
                        Vector2 tangent = new Vector2(-radial.y, radial.x) * (sign >= 0f ? 1f : -1f);
                        float vc = PhysicsConfig.CircularVelocity(g, planetDist);
                        float k = Mathf.Min(1f, config.AutopilotRate * dt);
                        velocity += (tangent * vc - velocity) * k;
                    }
                }
            }

            // Ручное управление — все ускорения к вектору скорости.
            v = velocity.magnitude;
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
                    Vector2 perp = new Vector2(-dir.y, dir.x);
                    velocity += perp * (strafe * config.StrafeAcc * engMul * dt);
                }
            }

            ship.Body.linearVelocity = velocity;
        }

        // Нос доворачивается к скорости — чисто визуально.
        private void Update()
        {
            if (ship == null || config == null)
                return;
            Vector2 v = ship.Body.linearVelocity;
            if (v.sqrMagnitude <= config.MinControlSpeed * config.MinControlSpeed)
                return;
            float target = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            float current = transform.eulerAngles.z;
            float delta = Mathf.DeltaAngle(current, target);
            transform.rotation = Quaternion.Euler(0f, 0f, current + delta * Mathf.Min(1f, config.HeadingTurnRate * Time.deltaTime));
        }
    }
}
