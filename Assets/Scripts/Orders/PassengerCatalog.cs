using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.Orders
{
    // Каталог пассажиров: список профилей, заказы рандомно выбирают отсюда.
    // Добавил профиль в список — он сразу участвует в генерации заказов.
    [CreateAssetMenu(fileName = "PassengerCatalog", menuName = "Space Taxi/Passenger Catalog")]
    public sealed class PassengerCatalog : ScriptableObject
    {
        public List<PassengerProfile> Profiles = new List<PassengerProfile>();

        // Случайный профиль; null, если каталог пуст.
        public PassengerProfile Pick()
        {
            if (Profiles.Count == 0)
                return null;
            return Profiles[Random.Range(0, Profiles.Count)];
        }
    }
}
