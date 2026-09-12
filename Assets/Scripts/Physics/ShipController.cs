using UnityEngine;

namespace SpaceTaxi.Physics
{
    /// <summary>
    /// Translates player input into forces on the ship rigidbody:
    /// W forward thrust, S retro brake, A/D lateral thrust.
    /// </summary>
    public class ShipController : MonoBehaviour
    {
        private Ship ship;

        private void Awake()
        {
            ship = GetComponent<Ship>();
        }

        private void FixedUpdate()
        {
            if (ship == null || ship.State != ShipState.Flying)
            {
                return;
            }

            // TODO (physics iteration): read W/S/A/D via Input System and apply
            // AddForce on ship.Body: forward * Thrust, retro * Retro, lateral * StrafeAcc;
            // clamp linear velocity to MaxV; apply EngineMultiplier inside the
            // landing ring and GravityBoost while thrusting in a strong field.
            // Fuel drain is out of scope for v0.1.
        }
    }
}
