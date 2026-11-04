using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.Physics;

namespace SpaceTaxi.Audio
{
    // Звуки корабля по вводу и контактам
    public sealed class ShipAudio : MonoBehaviour
    {
        private Ship ship;
        private ShipController controller;

        // ввод читается через ShipController (ThrustOn/BrakeOn/StrafeLevel) —
        // свой биндинг не нужен, метод оставлен для WorldBuilder
        public void SetInput(InputActionAsset asset) { }

        private void Awake()
        {
            ship = GetComponent<Ship>();
            controller = GetComponent<ShipController>();
        }

        private void Update()
        {
            if (ship == null || ship.Config == null)
                return;

            // SetLoop идемпотентен: вызов каждый кадр дешевле флагов-переходов;
            // пустой бак и не-Flying гасят входы в самом контроллере
            bool thrust = controller != null && controller.ThrustOn;
            Sound.SetLoop(SoundId.EngineMain, thrust);
            Sound.SetLoop(SoundId.EngineRetro, controller != null && controller.BrakeOn);
            Sound.SetLoop(SoundId.EngineStrafe,
                controller != null && Mathf.Abs(controller.StrafeLevel) > 0.01f);

            if (controller == null)
                return;

            // качели/посадочное кольцо (EnginePower 1 → EngineMultiplier) — громче и выше тоном
            if (thrust)
            {
                float mulSpan = Mathf.Max(ship.Config.EngineMultiplier - 1f, 0.001f);
                Sound.SetLoopIntensity(SoundId.EngineMain,
                    Mathf.Clamp01((controller.EnginePower - 1f) / mulSpan));
            }

            // лёгкий пан в сторону боковой тяги
            Sound.SetLoopPan(SoundId.EngineMain,
                Mathf.Clamp(controller.StrafeLevel * 0.35f, -0.35f, 0.35f));
        }
    }
}
