using UnityEngine;

namespace SpaceTaxi.Orders
{
    public enum PassengerTaskKind
    {
        None = 0,     // задания нет (Турист/Чудик — появятся в П8.1)
        Deadline,     // Торопыга: доставить за отведённое время
        NoRefuel,     // Барыга: без заправки на станциях в пути
        NoCollision,  // Барыга/Тихоня: без столкновений
        SpeedLimit,   // Тихоня: не быстрее лимита весь полёт
    }

    // Задание пассажира (П5.3, §3.6): одно на заказ, тип задаёт архетип.
    // Успех — +0.10 рейтинга и чаевые (П8.2), провал — жалоба (−0.10).
    public sealed class PassengerTask
    {
        public PassengerTaskKind Kind;
        public string Text;           // текст задания для карточки заказа
        public float DeadlineSeconds; // лимит времени (Kind == Deadline)
        public float SpeedLimit;      // лимит скорости, юн/с (Kind == SpeedLimit)

        // внутреннее отслеживание исхода; итог подводится при доставке
        public bool Succeeded;
        public bool Failed;

        // Time.time в момент Accept; < 0 — таймер ещё не запущен
        private float deadlineStartedAt = -1f;

        // таймер стартует при принятии заказа; пауза время не двигает (Time.time)
        public void StartDeadline(float now)
        {
            if (deadlineStartedAt < 0f)
                deadlineStartedAt = now;
        }

        // остаток времени дедлайна; до старта — полный лимит
        public float DeadlineRemaining(float now)
        {
            return deadlineStartedAt < 0f
                ? DeadlineSeconds
                : Mathf.Max(0f, DeadlineSeconds - (now - deadlineStartedAt));
        }
    }

    // Каталог заданий: тексты утверждены (§3.6), числа — решение координатора
    public static class PassengerTasks
    {
        private const float DeadlineSpeedUnit = 150f; // юн/с — знаменатель формулы T
        private const float DeadlineSlack = 45f;      // с — прибавка к чистому времени
        private const float QuietSpeedLimit = 250f;   // юн/с — лимит Тихони

        // null — у архетипа задания нет (Турист/Чудик до П8.1)
        public static PassengerTask Create(string archetype, float routeDistance)
        {
            switch (archetype)
            {
                case "Торопыга":
                {
                    float t = routeDistance / DeadlineSpeedUnit + DeadlineSlack;
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.Deadline,
                        Text = $"Опаздываю! Доставьте меня за {t:0} с.",
                        DeadlineSeconds = t,
                    };
                }
                case "Барыга":
                    return UnityEngine.Random.value < 0.5f
                        ? new PassengerTask
                        {
                            Kind = PassengerTaskKind.NoRefuel,
                            Text = "Не заправляемся в пути — станции пропускайте.",
                        }
                        : NoCollision("Бережёный груз: ни одного столкновения.");
                case "Тихоня":
                    return UnityEngine.Random.value < 0.5f
                        ? new PassengerTask
                        {
                            Kind = PassengerTaskKind.SpeedLimit,
                            Text = $"Тише едем: не быстрее {QuietSpeedLimit:0} юн/с весь полёт.",
                            SpeedLimit = QuietSpeedLimit,
                        }
                        : NoCollision("Без трясок, пожалуйста.");
                default:
                    return null;
            }
        }

        private static PassengerTask NoCollision(string text)
        {
            return new PassengerTask { Kind = PassengerTaskKind.NoCollision, Text = text };
        }
    }
}
