using UnityEngine;

namespace SpaceTaxi.Physics
{
    /// <summary>
    /// Tunable physics constants for flight, landing and gravity.
    /// Default values come from the HTML prototype v3 (ORBITA-TAXI) and are
    /// the starting point for balance (see GameInfo/01_MECHANICS.md par.1).
    /// </summary>
    [CreateAssetMenu(fileName = "PhysicsConfig", menuName = "Space Taxi/Physics Config")]
    public sealed class PhysicsConfig : ScriptableObject
    {
        [Header("Thrust")]
        [Tooltip("Forward acceleration along the ship heading (W).")]
        public float Thrust = 95f;

        [Tooltip("Retro brake acceleration (S).")]
        public float Retro = 115f;

        [Tooltip("Lateral (strafe) acceleration (A/D).")]
        public float StrafeAcc = 85f;

        [Tooltip("Hard speed limit for the ship.")]
        public float MaxV = 330f;

        [Header("Landing")]
        [Tooltip("Maximum safe contact speed; a faster impact deals damage and bounces off.")]
        public float LandSpeed = 38f;

        [Tooltip("Takeoff impulse applied from the planet centre.")]
        public float TakeoffV = 85f;

        [Tooltip("Engine thrust multiplier inside the landing ring (distance to centre < radius + offset).")]
        public float EngineMultiplier = 3.2f;

        [Tooltip("Offset from the planet surface where the landing mode ring begins.")]
        public float LandingRingOffset = 90f;

        [Header("Fuel & Hull")]
        [Tooltip("Fuel tank capacity of the stock ship.")]
        public float FuelMax = 100f;

        [Tooltip("Fuel drained per second while any engine burns.")]
        public float FuelDrainPerSecond = 5.5f;

        [Tooltip("Hull integrity of the stock ship.")]
        public float HpMax = 100f;

        [Header("Gravity")]
        [Tooltip("Gravity constant for a = G * M / r^2. Calibrated together with body masses during the physics iteration.")]
        public float GravityConstant = 1f;

        [Tooltip("Thrust multiplier for the gravity manoeuvre (thrusting deep inside a gravity well).")]
        public float GravityBoost = 1.15f;
    }
}
