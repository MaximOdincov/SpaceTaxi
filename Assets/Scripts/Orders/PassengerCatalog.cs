using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.Orders
{
    // Заказы выбирают профили отсюда
    [CreateAssetMenu(fileName = "PassengerCatalog", menuName = "Space Taxi/Passenger Catalog")]
    public sealed class PassengerCatalog : ScriptableObject
    {
        public List<PassengerProfile> Profiles = new List<PassengerProfile>();

        [Tooltip("Пулы генератора пассажиров (П5.2); пусто — загрузится из Resources")]
        [SerializeField] private PassengerPools pools;

        private bool poolsSearched;

        // старый путь: ручные профили ассета (Passenger_Default и др.)
        public PassengerProfile Pick()
        {
            if (Profiles.Count == 0)
                return null;
            return Profiles[Random.Range(0, Profiles.Count)];
        }

        // П5.2: свежий профиль из пулов; null — пулов нет (фолбэк Pick() у вызывающего)
        public PassengerProfile Generate()
        {
            var p = GetPools();
            return p != null ? PassengerGenerator.Create(p) : null;
        }

        // ленивый фолбэк в Resources (ассет П5.1); повторно не ищем, даже если не нашли
        private PassengerPools GetPools()
        {
            if (pools == null && !poolsSearched)
            {
                pools = Resources.Load<PassengerPools>("PassengerPools");
                poolsSearched = true;
            }
            return pools;
        }
    }
}
