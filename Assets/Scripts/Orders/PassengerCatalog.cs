using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.Orders
{
    // Заказы выбирают профили отсюда
    [CreateAssetMenu(fileName = "PassengerCatalog", menuName = "Space Taxi/Passenger Catalog")]
    public sealed class PassengerCatalog : ScriptableObject
    {
        public List<PassengerProfile> Profiles = new List<PassengerProfile>();

        public PassengerProfile Pick()
        {
            if (Profiles.Count == 0)
                return null;
            return Profiles[Random.Range(0, Profiles.Count)];
        }
    }
}
