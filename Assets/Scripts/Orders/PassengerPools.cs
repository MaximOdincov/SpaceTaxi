using UnityEngine;

namespace SpaceTaxi.Orders
{
    // Пулы данных генератора пассажиров: имена, фамилии, общие реплики —
    // без деления по типажам: задания и чаевые от пассажира не зависят
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


    }
}
