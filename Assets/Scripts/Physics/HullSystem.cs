using UnityEngine;

namespace SpaceTaxi.Physics
{
    public class HullSystem : MonoBehaviour
    {
        private Ship ship;

        private void Awake()
        {
            ship = GetComponent<Ship>();
        }

        public void ApplyDamage(float amount)
        {
            if (amount <= 0f || ship == null)
                return;

            // TODO: Hp -= amount, событие для HUD; при Hp <= 0 — респавн на старте
        }
    }
}
