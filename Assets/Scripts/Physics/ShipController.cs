using UnityEngine;

namespace SpaceTaxi.Physics
{
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
                return;

            // TODO: ввод W/S/A/D -> AddForce (Thrust/Retro/StrafeAcc), кламп MaxV,
            // EngineMultiplier в посадочном кольце, GravityBoost у массивных тел
        }
    }
}
