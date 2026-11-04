using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.Physics;

namespace SpaceTaxi.Audio
{
    // Звуки корабля по вводу и контактам
    public sealed class ShipAudio : MonoBehaviour
    {
        // пороги как в баннере HudBinder: 25% и 10% топлива, 25% корпуса
        private const float LowFuelWarn = 0.25f;
        private const float LowFuelCrit = 0.10f;
        private const float LowHullWarn = 0.25f;

        private Ship ship;
        private ShipController controller;
        private HullSystem hull;

        private bool fuelWarned;     // сигнал на пороге 25% уже прозвучал
        private bool fuelCritWarned; // второй сигнал на пороге 10%
        private bool hullWarned;

        // ввод читается через ShipController (ThrustOn/BrakeOn/StrafeLevel) —
        // свой биндинг не нужен, метод оставлен для WorldBuilder
        public void SetInput(InputActionAsset asset) { }

        private void Awake()
        {
            ship = GetComponent<Ship>();
            controller = GetComponent<ShipController>();
            // бак меняется только после Init (корабль активируется позже) — подписка тут безопасна
            ship.FuelChanged += OnFuelChanged;
        }

        private void Update()
        {
            if (ship == null || ship.Config == null)
                return;

            // HullSystem может добавиться после Awake (WorldBuilder) — ленивый бинд
            if (hull == null)
            {
                hull = GetComponent<HullSystem>();
                if (hull != null)
                    hull.HpChanged += OnHullChanged;
            }

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

        // один сигнал на порог за «спуск», сброс при заправке выше порога —
        // станция и респавн шлют FuelChanged, флаги сбрасываются сами
        private void OnFuelChanged()
        {
            if (ship.Config == null)
                return;
            float f = ship.FuelFraction;
            if (f <= LowFuelCrit && !fuelCritWarned)
            {
                fuelCritWarned = true;
                fuelWarned = true; // провал обоих порогов за тик — один сигнал
                Sound.Play(SoundId.LowFuel);
            }
            else if (f <= LowFuelWarn && !fuelWarned)
            {
                fuelWarned = true;
                Sound.Play(SoundId.LowFuel);
            }
            else if (f > LowFuelWarn)
            {
                fuelWarned = false;
                fuelCritWarned = false;
            }
            else if (f > LowFuelCrit)
            {
                fuelCritWarned = false;
            }
        }

        // один сигнал до ремонта выше порога (Repair/RepairAll/респавн шлют HpChanged)
        private void OnHullChanged()
        {
            float f = ship.HpFraction;
            if (f <= LowHullWarn && !hullWarned)
            {
                hullWarned = true;
                Sound.Play(SoundId.LowHull);
            }
            else if (f > LowHullWarn)
            {
                hullWarned = false;
            }
        }
    }
}
