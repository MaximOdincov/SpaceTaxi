using UnityEngine;

namespace SpaceTaxi.Physics
{
    /// <summary>
    /// Landing and collision resolution: contact speed below LandSpeed means
    /// landing, above means damage (v - LandSpeed) * 0.9 plus a bounce.
    /// Also handles takeoff from the planet surface.
    /// </summary>
    public class LandingSystem : MonoBehaviour
    {
        private Ship ship;

        private void Awake()
        {
            ship = GetComponent<Ship>();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            // TODO (landing iteration): resolve contact with a Planet:
            // v < Config.LandSpeed -> land (ship.State = Landed, ship.LandedOn = planet);
            // otherwise -> HullSystem.ApplyDamage((v - LandSpeed) * 0.9) + bounce off.
        }

        /// <summary>Launches the ship from the planet surface it is landed on.</summary>
        public void Takeoff()
        {
            // TODO (landing iteration): impulse Config.TakeoffV away from the
            // planet centre, ship.State = Flying, ship.LandedOn = null.
        }
    }
}
