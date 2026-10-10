using UnityEngine;

namespace SpaceTaxi.Orders
{
    // Профиль пассажира, наполняется руками в инспекторе
    [CreateAssetMenu(fileName = "Passenger", menuName = "Space Taxi/Passenger Profile")]
    public sealed class PassengerProfile : ScriptableObject
    {
        [Header("Кто это")]
        [Tooltip("Имя, которое видит игрок")]
        public string DisplayName = "Гость системы";

        [Tooltip("Архетип: Торопыга, Барыга, Турист, Тихоня, Чудик")]
        public string Archetype = "Турист";

        [TextArea(2, 4)]
        [Tooltip("Короткое описание для карточки заказа")]
        public string Description;

        [Header("Портрет")]
        [Tooltip("Спрайт портрета; пусто — UI покажет заглушку")]
        public Sprite Portrait;

        [Tooltip("Оттенок портрета: один спрайт — разные «люди»")]
        public Color PortraitTint = Color.white;

        [Header("Фразы (покажется случайная из списка)")]
        [TextArea(2, 4)]
        [Tooltip("Пассажир сел на борт")]
        public string[] Greetings;

        [TextArea(2, 4)]
        [Tooltip("«Пожелание» — видно в карточке заказа до принятия")]
        public string[] Wishes;

        [TextArea(2, 4)]
        [Tooltip("Доставлен на место")]
        public string[] Thanks;

        [TextArea(2, 4)]
        [Tooltip("Заказ провален")]
        public string[] Complaints;

        public static string PickPhrase(string[] phrases)
        {
            if (phrases == null || phrases.Length == 0)
                return null;
            return phrases[Random.Range(0, phrases.Length)];
        }
    }
}
