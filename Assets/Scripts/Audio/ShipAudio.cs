using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.Physics;

namespace SpaceTaxi.Audio
{
    // Звуки корабля по вводу и контактам
    public sealed class ShipAudio : MonoBehaviour
    {
        [SerializeField] private InputActionAsset input; // тот же ассет, что у ShipController

        public void SetInput(InputActionAsset asset)
        {
            input = asset;
            BindActions();
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
            BindActions();
        }

        // из OnEnable и из SetInput — кто раньше
        private void BindActions()
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

            // звук эффектов корабля пока отключён — оставлена только музыка
            Sound.SetLoop(SoundId.EngineMain, false);

            lastHp = ship.Hp;
            lastState = ship.State;
        }
    }
}
