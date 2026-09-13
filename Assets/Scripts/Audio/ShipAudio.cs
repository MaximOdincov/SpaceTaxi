using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.Physics;

namespace SpaceTaxi.Audio
{
    // Звуки корабля: двигатели по вводу, контакты (посадка/удар), гибель и взлёт.
    // Тонкая прослойка — только слушает состояние Ship, логику не дублирует.
    public sealed class ShipAudio : MonoBehaviour
    {
        [SerializeField] private InputActionAsset input; // тот же ассет, что у ShipController

        public void SetInput(InputActionAsset asset)
        {
            input = asset;
        }

        private Ship ship;
        private InputAction thrustAction;
        private InputAction brakeAction;
        private InputAction strafeAction;
        private float lastHp;
        private ShipState lastState;

        private void Awake()
        {
            ship = GetComponent<Ship>();
            lastHp = ship != null ? ship.Hp : 0f;
            lastState = ship != null ? ship.State : ShipState.Flying;
        }

        private void OnEnable()
        {
            InputActionMap map = input != null ? input.FindActionMap("Ship") : null;
            if (map == null)
                return;
            thrustAction = map.FindAction("Thrust");
            brakeAction = map.FindAction("Brake");
            strafeAction = map.FindAction("Strafe");
        }

        private void Update()
        {
            if (ship == null)
                return;

            bool flying = ship.State == ShipState.Flying;
            bool thrustOn = flying && thrustAction != null && thrustAction.IsPressed();
            bool brakeOn = flying && brakeAction != null && brakeAction.IsPressed();
            bool strafeOn = flying && strafeAction != null && Mathf.Abs(strafeAction.ReadValue<float>()) > 0f;

            Sound.SetLoop(SoundId.EngineMain, thrustOn);
            Sound.SetLoop(SoundId.EngineRetro, brakeOn);
            Sound.SetLoop(SoundId.EngineStrafe, strafeOn);

            if (strafeOn)
                Sound.SetLoopPan(SoundId.EngineStrafe, strafeAction.ReadValue<float>());

            // Тон основного двигателя слегка растёт со скоростью.
            if (thrustOn && ship.Config != null)
            {
                float v = ship.Body != null ? ship.Body.linearVelocity.magnitude : 0f;
                Sound.SetLoopIntensity(SoundId.EngineMain, Mathf.Clamp01(v / ship.Config.MaxV));
            }

            // Взлёт: стоянка → полёт.
            if (lastState == ShipState.Landed && flying)
                Sound.Play(SoundId.Takeoff);

            // Гибель: Hp упал до нуля.
            if (lastHp > 0f && ship.Hp <= 0f)
                Sound.Play(SoundId.Explosion);

            // Респавн завершён: снова цел и летит.
            if (lastState == ShipState.Respawning && flying && ship.Hp > 0f)
                Sound.Play(SoundId.Respawn);

            lastHp = ship.Hp;
            lastState = ship.State;
        }

        // Приходит вместе с LandingSystem — звук по жёсткости контакта.
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (ship == null || ship.Config == null)
                return;
            float v = collision.relativeVelocity.magnitude;
            if (v < ship.Config.LandSpeed)
            {
                Sound.Play(SoundId.LandingTouch);
            }
            else
            {
                // Сила 0..1: от порога посадки до двойного порога.
                float power = Mathf.Clamp01((v - ship.Config.LandSpeed) / ship.Config.LandSpeed);
                Sound.Play(SoundId.Collision, power);
            }
        }
    }
}
