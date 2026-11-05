using UnityEngine;

namespace SpaceTaxi.Orders
{
    // Пулы данных генератора пассажиров (П5.2): имена, фамилии, общие реплики —
    // без деления по типажам (Н-14): задания и чаевые от пассажира не зависят
    [CreateAssetMenu(fileName = "PassengerPools", menuName = "Space Taxi/Passenger Pools")]
    public sealed class PassengerPools : ScriptableObject
    {
        [Tooltip("Имена пассажиров")]
        public string[] Names;

        [Tooltip("Фамилии пассажиров")]
        public string[] Surnames;

        [Header("Фразы (покажется случайная из списка)")]
        [TextArea(2, 4)]
        [Tooltip("Пассажир сел на борт")]
        public string[] Greetings;

        [TextArea(2, 4)]
        [Tooltip("«Пожелание» — видно в карточке заказа до принятия, если задания нет")]
        public string[] Wishes;

        [TextArea(2, 4)]
        [Tooltip("Доставлен на место")]
        public string[] Thanks;

        [TextArea(2, 4)]
        [Tooltip("Заказ провален")]
        public string[] Complaints;

        public string PickName()
        {
            return Names != null && Names.Length > 0
                ? Names[UnityEngine.Random.Range(0, Names.Length)]
                : null;
        }

        public string PickSurname()
        {
            return Surnames != null && Surnames.Length > 0
                ? Surnames[UnityEngine.Random.Range(0, Surnames.Length)]
                : null;
        }
    }
}
