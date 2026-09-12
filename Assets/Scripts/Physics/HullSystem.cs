using UnityEngine;

namespace SpaceTaxi.Physics
{
    /// <summary>
    /// Hull integrity: receives damage from collisions and star heat,
    /// tracks ship death and triggers respawn.
    /// </summary>
    public class HullSystem : MonoBehaviour
    {
        private Ship ship;

        private void Awake()
        {
            ship = GetComponent<Ship>();
        }

        /// <summary>Applies hull damage to the ship. Death happens at Hp &lt;= 0.</summary>
        public void ApplyDamage(float amount)
        {
            if (amount <= 0f || ship == null)
            {
                return;
            }

            // TODO (hull iteration): ship.Hp -= amount; clamp to 0..Config.HpMax;
            // raise an HpChanged event for the HUD; on Hp <= 0 -> death:
            // instant respawn on the start planet with full Hp (v0.1 rules).
        }
    }
}
