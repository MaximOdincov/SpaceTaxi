using UnityEngine;

namespace SpaceTaxi.Physics
{
    /// <summary>High-level state of the player ship.</summary>
    public enum ShipState
    {
        Flying = 0,
        Landed = 1,
        Respawning = 2,
    }

    /// <summary>
    /// Player ship: physical body plus hull/fuel state. Movement forces are
    /// applied by ShipController, gravity by GravityField, landing and damage
    /// are resolved by LandingSystem and HullSystem.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class Ship : MonoBehaviour
    {
        [Header("State")]
        [Tooltip("Current high-level state of the ship.")]
        public ShipState State = ShipState.Flying;

        [Tooltip("Current hull integrity.")]
        public float Hp = 100f;

        [Tooltip("Current fuel level (not consumed in v0.1).")]
        public float Fuel = 100f;

        /// <summary>Shared physics tuning, assigned by GameSession at startup.</summary>
        public PhysicsConfig Config { get; private set; }

        /// <summary>Cached rigidbody of the ship.</summary>
        public Rigidbody2D Body { get; private set; }

        /// <summary>Planet the ship is currently landed on, null while flying.</summary>
        public World.Planet LandedOn { get; set; }

        /// <summary>Initializes the ship with the shared physics config.</summary>
        public void Init(PhysicsConfig config)
        {
            Config = config;
            Body = GetComponent<Rigidbody2D>();

            // Unity gravity is off: the custom GravityField drives all attraction.
            Body.gravityScale = 0f;

            Hp = config.HpMax;
            Fuel = config.FuelMax;
        }

        /// <summary>Normalized hull integrity 0..1, for HUD.</summary>
        public float HpFraction => Config != null ? Hp / Config.HpMax : 0f;

        /// <summary>Normalized fuel level 0..1, for HUD.</summary>
        public float FuelFraction => Config != null ? Fuel / Config.FuelMax : 0f;
    }
}
