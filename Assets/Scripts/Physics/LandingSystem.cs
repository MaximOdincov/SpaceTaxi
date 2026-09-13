using UnityEngine;

namespace SpaceTaxi.Physics
{
    // Мягкое касание (v < LandSpeed) — посадка, быстрее — урон (v-LandSpeed)*0.9 и отскок.
    public class LandingSystem : MonoBehaviour
    {
        private Ship ship;

        private void Awake()
        {
            ship = GetComponent<Ship>();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            // TODO (посадка): v < LandSpeed -> сесть (State = Landed), иначе
            // урон (v - LandSpeed) * 0.9 + отскок
        }

        // Взлёт со стоянки: импульс TakeoffV от центра планеты (ПРОБЕЛ).
        public void Takeoff()
        {
            if (ship == null || ship.State != ShipState.Landed || ship.LandedOn == null)
                return;
            Vector2 dir = ((Vector2)ship.transform.position - (Vector2)ship.LandedOn.transform.position).normalized;
            ship.Body.linearVelocity = dir * ship.Config.TakeoffV;
            ship.LandedOn = null;
            ship.State = ShipState.Flying;
            Debug.Log($"[Landing] взлёт: v={ship.Body.linearVelocity.magnitude}");
        }
    }
}
