using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.World; // LevelAsset/BodyType — целям точек интереса нужен уровень

namespace SpaceTaxi.Orders
{
    public enum PassengerTaskKind
    {
        None = 0,     // задания нет
        Deadline,     // доставить за отведённое время
        NoRefuel,     // без заправки на станциях в пути
        NoCollision,  // без столкновений
        SpeedLimit,   // не быстрее лимита весь полёт
        CometFlyby,   // пролететь близко к комете (П8.1)
        RingVisit,    // зайти в capture-кольцо планеты
        StarBackside, // побывать за звездой относительно центра мира
        HoleFlyby,    // пролететь близко к чёрной дыре
        WaitOnPlanet, // суммарно постоять на планете
    }

    // Задание пассажира (П5.3): одно на заказ, тип выбирается случайно (Н-14).
    // Успех — +0.10 рейтинга, провал — жалоба (−0.10).
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
        private const float TaskChance = 0.5f;        // шанс задания у заказа (Н-14)
        private const float DeadlineSpeedUnit = 150f; // юн/с — знаменатель формулы T
        private const float DeadlineSlack = 45f;      // с — прибавка к чистому времени
        private const float QuietSpeedLimit = 250f;   // юн/с — лимит скорости
        private const float CometFlybyDistance = 400f;  // юн — подлететь к комете (§3.6)
        private const float HoleFlybyDistance = 800f;   // юн — подлететь к дыре (§3.6)
        private const float WaitOnPlanetSeconds = 10f;  // с — сидеть на планете (§3.6)

        // Равномерный случайный тип из доступных (Н-14); шанс задания вообще — 50%,
        // иначе null (UI трактует как «задания нет»). Точкам интереса (П8.1) нужен
        // уровень: цель ищется по типу тела; объекта нет — тип выпадает из выбора,
        // ядро условий (Deadline/NoRefuel/NoCollision/SpeedLimit) доступно всегда
        public static PassengerTask CreateRandom(float routeDistance,
            int pickupIndex, int dropoffIndex, LevelAsset level, bool cometsAvailable)
        {
            if (UnityEngine.Random.value >= TaskChance)
                return null;

            int planet = RandomBodyIndex(level, BodyType.Planet, pickupIndex, dropoffIndex);
            int star = RandomBodyIndex(level, BodyType.Star);
            int hole = RandomBodyIndex(level, BodyType.BlackHole);

            var kinds = new List<PassengerTaskKind>(9)
            {
                PassengerTaskKind.Deadline,
                PassengerTaskKind.NoRefuel,
                PassengerTaskKind.NoCollision,
                PassengerTaskKind.SpeedLimit,
            };
            if (cometsAvailable)
                kinds.Add(PassengerTaskKind.CometFlyby);
            if (planet >= 0)
            {
                kinds.Add(PassengerTaskKind.RingVisit);
                kinds.Add(PassengerTaskKind.WaitOnPlanet);
            }
            if (star >= 0)
                kinds.Add(PassengerTaskKind.StarBackside);
            if (hole >= 0)
                kinds.Add(PassengerTaskKind.HoleFlyby);

            switch (kinds[UnityEngine.Random.Range(0, kinds.Count)])
            {
                case PassengerTaskKind.Deadline:
                {
                    float t = routeDistance / DeadlineSpeedUnit + DeadlineSlack;
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.Deadline,
                        Text = $"Опаздываю! Доставьте меня за {t:0} с.",
                        DeadlineSeconds = t,
                    };
                }
                case PassengerTaskKind.NoRefuel:
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.NoRefuel,
                        Text = "Не заправляемся в пути — станции пропускайте.",
                    };
                case PassengerTaskKind.NoCollision:
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.NoCollision,
                        Text = "Ни одного столкновения, пожалуйста.",
                    };
                case PassengerTaskKind.SpeedLimit:
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.SpeedLimit,
                        Text = $"Тише едем: не быстрее {QuietSpeedLimit:0} юн/с весь полёт.",
                        SpeedLimit = QuietSpeedLimit,
                    };
                case PassengerTaskKind.CometFlyby:
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.CometFlyby,
                        Text = $"Покажите комету! Пролетите в {CometFlybyDistance:0} юн от неё.",
                        DistanceThreshold = CometFlybyDistance,
                    };
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
                case PassengerTaskKind.HoleFlyby:
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.HoleFlyby,
                        Text = $"Мимо бездны! Пролетите в {HoleFlybyDistance:0} юн от чёрной дыры — и живыми.",
                        DistanceThreshold = HoleFlybyDistance,
                    };
                default: // WaitOnPlanet
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.WaitOnPlanet,
                        Text = $"Посидим перед стартом на {level.Bodies[planet].DisplayName} минут десять. Тишина — тоже путешествие.",
                        TargetBodyIndex = planet,
                        WaitSeconds = WaitOnPlanetSeconds,
                    };
            }
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
    }
}
