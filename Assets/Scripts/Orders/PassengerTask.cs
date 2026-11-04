using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.World; // LevelAsset/BodyType — целям точек интереса нужен уровень

namespace SpaceTaxi.Orders
{
    public enum PassengerTaskKind
    {
        None = 0,     // задания нет
        Deadline,     // Торопыга: доставить за отведённое время
        NoRefuel,     // Барыга: без заправки на станциях в пути
        NoCollision,  // Барыга/Тихоня: без столкновений
        SpeedLimit,   // Тихоня: не быстрее лимита весь полёт
        CometFlyby,   // Турист (П8.1): пролететь близко к комете
        RingVisit,    // Турист: зайти в capture-кольцо планеты
        StarBackside, // Турист: побывать за звездой относительно центра мира
        HoleFlyby,    // Чудик: пролететь близко к чёрной дыре
        WaitOnPlanet, // Чудик: суммарно постоять на планете
    }

    // Задание пассажира (П5.3, §3.6): одно на заказ, тип задаёт архетип.
    // Успех — +0.10 рейтинга и чаевые (П8.2), провал — жалоба (−0.10).
    public sealed class PassengerTask
    {
        public PassengerTaskKind Kind;
        public string Text;             // текст задания для карточки заказа
        public float DeadlineSeconds;   // лимит времени (Kind == Deadline)
        public float SpeedLimit;        // лимит скорости, юн/с (Kind == SpeedLimit)
        public float DistanceThreshold; // дистанция подлёта (CometFlyby/HoleFlyby)
        public int TargetBodyIndex;     // цель — индекс в LevelAsset.Bodies (RingVisit/StarBackside/WaitOnPlanet)
        public float WaitSeconds;       // сколько сидеть (WaitOnPlanet)
        public float WaitAccumulated;   // накоплено ожидания, копится с прерываниями (внутреннее)

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
        private const float CometFlybyDistance = 400f;  // юн — подлететь к комете (§3.6)
        private const float HoleFlybyDistance = 800f;   // юн — подлететь к дыре (§3.6)
        private const float WaitOnPlanetSeconds = 10f;  // с — сидеть на планете (§3.6)

        // null — у архетипа задания нет. Точкам интереса (П8.1) нужен уровень:
        // цель ищется по типу тела; объекта нет — замена доступным типом того же
        // архетипа, совсем нечего — задания нет
        public static PassengerTask Create(string archetype, float routeDistance,
            int pickupIndex, int dropoffIndex, LevelAsset level, bool cometsAvailable)
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
                case "Турист":
                    return TouristTask(pickupIndex, dropoffIndex, level, cometsAvailable);
                case "Чудик":
                    return OddballTask(pickupIndex, dropoffIndex, level);
                default:
                    return null;
            }
        }

        // Турист (§3.6): комета / кольцо случайной планеты (не А/Б) / тыл звезды
        private static PassengerTask TouristTask(int pickupIndex, int dropoffIndex,
            LevelAsset level, bool cometsAvailable)
        {
            int planet = RandomBodyIndex(level, BodyType.Planet, pickupIndex, dropoffIndex);
            int star = RandomBodyIndex(level, BodyType.Star);
            var kinds = new List<PassengerTaskKind>(3);
            if (cometsAvailable)
                kinds.Add(PassengerTaskKind.CometFlyby);
            if (planet >= 0)
                kinds.Add(PassengerTaskKind.RingVisit);
            if (star >= 0)
                kinds.Add(PassengerTaskKind.StarBackside);
            if (kinds.Count == 0)
                return null;

            switch (kinds[UnityEngine.Random.Range(0, kinds.Count)])
            {
                case PassengerTaskKind.RingVisit:
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.RingVisit,
                        Text = $"Облетим кольцо планеты {level.Bodies[planet].DisplayName}?",
                        TargetBodyIndex = planet,
                    };
                case PassengerTaskKind.StarBackside:
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.StarBackside,
                        Text = $"С теневой стороны звезды {level.Bodies[star].DisplayName} — говорят, вид необыкновенный.",
                        TargetBodyIndex = star,
                    };
                default: // CometFlyby — система комет есть всегда (WorldBuilder)
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.CometFlyby,
                        Text = $"Покажите комету! Пролетите в {CometFlybyDistance:0} юн от неё.",
                        DistanceThreshold = CometFlybyDistance,
                    };
            }
        }

        // Чудик (§3.6): мимо чёрной дыры / посидеть на планете (не А/Б)
        private static PassengerTask OddballTask(int pickupIndex, int dropoffIndex, LevelAsset level)
        {
            int hole = RandomBodyIndex(level, BodyType.BlackHole);
            int planet = RandomBodyIndex(level, BodyType.Planet, pickupIndex, dropoffIndex);
            var kinds = new List<PassengerTaskKind>(2);
            if (hole >= 0)
                kinds.Add(PassengerTaskKind.HoleFlyby);
            if (planet >= 0)
                kinds.Add(PassengerTaskKind.WaitOnPlanet);
            if (kinds.Count == 0)
                return null;

            if (kinds[UnityEngine.Random.Range(0, kinds.Count)] == PassengerTaskKind.HoleFlyby)
                return new PassengerTask
                {
                    Kind = PassengerTaskKind.HoleFlyby,
                    Text = $"Мимо бездны! Пролетите в {HoleFlybyDistance:0} юн от чёрной дыры — и живыми.",
                    DistanceThreshold = HoleFlybyDistance,
                };
            return new PassengerTask
            {
                Kind = PassengerTaskKind.WaitOnPlanet,
                Text = $"Посидим перед стартом на {level.Bodies[planet].DisplayName} минут десять. Тишина — тоже путешествие.",
                TargetBodyIndex = planet,
                WaitSeconds = WaitOnPlanetSeconds,
            };
        }

        // П8.1: успех точки интереса фиксируется только трекингом — в отличие
        // от условий П5.3, где «не нарушал условие» = молодец
        public static bool IsInterestPoint(PassengerTaskKind kind)
        {
            return kind >= PassengerTaskKind.CometFlyby;
        }

        // случайное тело типа (исключения — концы маршрута); нет — -1
        private static int RandomBodyIndex(LevelAsset level, BodyType type,
            int excludeA = -1, int excludeB = -1)
        {
            if (level == null)
                return -1;
            int count = 0;
            for (int i = 0; i < level.Bodies.Count; i++)
                if (level.Bodies[i].Type == type && i != excludeA && i != excludeB)
                    count++;
            if (count == 0)
                return -1;
            int pick = UnityEngine.Random.Range(0, count);
            for (int i = 0; i < level.Bodies.Count; i++)
            {
                if (level.Bodies[i].Type != type || i == excludeA || i == excludeB)
                    continue;
                if (pick-- == 0)
                    return i;
            }
            return -1; // недостижимо: count посчитан по тому же фильтру
        }

        private static PassengerTask NoCollision(string text)
        {
            return new PassengerTask { Kind = PassengerTaskKind.NoCollision, Text = text };
        }
    }
}
