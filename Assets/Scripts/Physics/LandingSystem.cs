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
            // TODO: посадка или урон+отскок, по скорости контакта
        }

        public void Takeoff()
        {
            // TODO: импульс TakeoffV от центра планеты
        }
    }
}
