using UnityEngine;

namespace SpaceTaxi.Orders
{
    public sealed class Passenger
    {
        public string FullName;
        public string Archetype;
    }

    // Случайный пассажир: архетип + имя/фамилия из пулов
    public static class PassengerGenerator
    {
        private static readonly string[] Archetypes =
        {
            "Торопыга", "Барыга", "Турист", "Тихоня", "Чудик",
        };

        private static readonly string[] FirstNames =
        {
            "Марк", "Нова", "Тим", "Рита", "Юки", "Аскольд", "Лем", "Вега",
            "Ирма", "Кастор", "Мила", "Отто", "Сур", "Эйра", "Ян", "Зара",
        };

        private static readonly string[] LastNames =
        {
            "Ветров", "Каро", "Лунь", "Норм", "Оз", "Пик", "Рош", "Сталь",
            "Тар", "Ул", "Фай", "Хан", "Цирон", "Дар", "Мурс", "Грин",
        };

        public static Passenger Generate()
        {
            return new Passenger
            {
                FullName = FirstNames[Random.Range(0, FirstNames.Length)] + " " +
                           LastNames[Random.Range(0, LastNames.Length)],
                Archetype = Archetypes[Random.Range(0, Archetypes.Length)],
            };
        }
    }
}
