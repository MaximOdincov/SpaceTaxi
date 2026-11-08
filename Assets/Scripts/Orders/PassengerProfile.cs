using UnityEngine;

namespace SpaceTaxi.Orders
{
    // Профиль пассажира, наполняется руками в инспекторе
    [CreateAssetMenu(fileName = "Passenger", menuName = "Space Taxi/Passenger Profile")]
    public sealed class PassengerProfile : ScriptableObject
    {
        // runtime-инстанс генератора (П5.2) — его можно Destroy при протухании
        // оффера; ассетные профили живут вечно и Destroy не проходят
        public bool RuntimeInstance { get; set; }

        [Header("Кто это")]
        [Tooltip("Имя, которое видит игрок")]
        public string DisplayName = "Гость системы";

        [TextArea(2, 4)]
        [Tooltip("Короткое описание для карточки заказа")]
        public string Description;

        [Header("Портрет")]
        [Tooltip("Спрайт портрета; пусто — UI покажет заглушку")]
        public Sprite Portrait;

        [Tooltip("Оттенок портрета: один спрайт — разные «люди»")]
        public Color PortraitTint = Color.white;

        [Tooltip("Сид портрета генератора NPC (восстановление из сейва); 0 — нет")]
        public int PortraitSeed;

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
