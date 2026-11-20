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
        NoCollision,  // без столкновений с повреждением
        SpeedLimit,   // не быстрее лимита весь полёт
        NoHeat,       // не входить в зоны жара звёзд
        BeltAvoid,    // не влетать в астероидные пояса
        RingVisit,    // зайти в capture-кольцо планеты
        StarNearby,   // пролететь рядом со звездой
        HoleFlyby,    // пролететь близко к чёрной дыре
    }

    // Задание пассажира: одно на заказ, тип выбирается случайно.
    // Успех — +0.10 рейтинга, провал — жалоба (−0.10).
    public sealed class PassengerTask
    {
        public PassengerTaskKind Kind;
        public string Text;             // текст задания для карточки заказа
        public float DeadlineSeconds;   // лимит времени (Kind == Deadline)
        public float SpeedLimit;        // лимит скорости, юн/с (Kind == SpeedLimit)
        public float DistanceThreshold; // дистанция подлёта (CometFlyby/HoleFlyby)
        public int TargetBodyIndex;     // цель — индекс в LevelAsset.Bodies (RingVisit/StarBackside/WaitOnPlanet)
        // накопительные условия-избегания: эпизоды с гистерезисом; в сейв
        // не пишутся — до загрузки счёт начинается заново, Failed сохраняется
        public int Strikes;             // замечаний (NoHeat/BeltAvoid)
        public bool EpisodeOpen;        // эпизод открыт — замечание уже засчитано

        // внутреннее отслеживание исхода; итог подводится при доставке
        public bool Succeeded;
        public bool Failed;
        public bool MomentFired; // тост-момент (успех/провал) уже показан — один на заказ

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

    // Каталог заданий: тексты и числовые константы
    public static class PassengerTasks
    {
        private const float TaskChance = 0.5f;        // шанс задания у заказа
        private const float DeadlineSpeedUnit = 150f; // юн/с — знаменатель формулы T
        private const float DeadlineSlack = 45f;      // с — прибавка к чистому времени
        private const float QuietSpeedLimit = 250f;   // юн/с — лимит скорости
        private const float HoleFlybyDistance = 800f;   // юн — подлететь к дыре
        private const float StarNearbyDistance = 400f;  // юн — пролететь рядом со звездой

        // избегания-условия: эпизоды с гистерезисом; публичные — OnboardBinder показывает счёт
        public const int NoHeatStrikeLimit = 2;  // входов в жар звёзд — жалоба пассажира
        public const int BeltStrikeLimit = 2;    // пролетов сквозь пояс — жалоба пассажира

        // бонус к выплате за пролёт мимо чёрной дыры
        public const int HoleFlybyBonus = 150;

        // шанс «паникёра» при генерации профиля (таймер-срыв на заказ)
        public const float HurryChance = 0.2f;
        private const float HurrySlack = 75f;   // с — прибавка к чистому времени срыва

        // Равномерный случайный тип из доступных; шанс задания вообще — 50%,
        // иначе null (UI трактует как «задания нет»). Точкам интереса нужен
        // уровень: цель ищется по типу тела; объекта нет — тип выпадает из выбора
        public static PassengerTask CreateRandom(float routeDistance,
            int pickupIndex, int dropoffIndex, LevelAsset level)
        {
            if (UnityEngine.Random.value >= TaskChance)
                return null;

            int planet = RandomBodyIndex(level, BodyType.Planet, pickupIndex, dropoffIndex);
            int star = RandomBodyIndex(level, BodyType.Star);
            int hole = RandomBodyIndex(level, BodyType.BlackHole);
            int belt = RandomBodyIndex(level, BodyType.AsteroidBelt);

            var kinds = new List<PassengerTaskKind>(9)
            {
                PassengerTaskKind.Deadline,
                PassengerTaskKind.NoRefuel,
                PassengerTaskKind.NoCollision,
                PassengerTaskKind.SpeedLimit,
                PassengerTaskKind.NoHeat,
                PassengerTaskKind.BeltAvoid,
            };
            if (planet >= 0)
                kinds.Add(PassengerTaskKind.RingVisit);
            if (star >= 0)
                kinds.Add(PassengerTaskKind.StarNearby);
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
                        // провал ловит любой платный сервис (заправка и ремонт)
                        Text = "Ни заправки, ни ремонта на станциях — не останавливаемся, пожалуйста.",
                    };
                case PassengerTaskKind.NoCollision:
                    // провал — только удары с уроном (мягкие касания не считаются)
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.NoCollision,
                        Text = "У меня хрупкий груз — не врезаемся, пожалуйста.",
                    };
                case PassengerTaskKind.SpeedLimit:
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.SpeedLimit,
                        // лимит проверяется только с пассажиром на борту (TrackTask при Aboard)
                        Text = $"Тише едем: не быстрее {QuietSpeedLimit:0} юн/с, пока я на борту.",
                        SpeedLimit = QuietSpeedLimit,
                    };
                case PassengerTaskKind.RingVisit:
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.RingVisit,
                        // засчитывается вход в capture-кольцо (3.3r+30 — десятки
                        // юн над поверхностью)
                        Text = $"Зайдём в орбитальное кольцо планеты {level.Bodies[planet].DisplayName} — вид оттуда чудесный.", // enforced: вход в capture-кольцо
                        TargetBodyIndex = planet,
                    };
                case PassengerTaskKind.StarNearby:
                    // успех — простая близость к звезде
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.StarNearby,
                        // дистанция успеха — прямо в тексте (= StarNearbyDistance)
                        Text = $"Я вечный Холодрыга — весь год мёрз! Пролетим со звездой {level.Bodies[star].DisplayName} рядом: подлетите к ней ближе 400 юн.",
                        TargetBodyIndex = star,
                        DistanceThreshold = StarNearbyDistance,
                    };
                case PassengerTaskKind.NoHeat:
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.NoHeat,
                        // порог — прямо в тексте
                        Text = "К звёздам не приближайтесь: жар меня пугает. Вошли в жар — замечание; два замечания — провал.",
                    };
                case PassengerTaskKind.BeltAvoid:
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.BeltAvoid,
                        // замечание — вход в полосу пояса (±BeltWidth от осевой),
                        // а не удар астероида; порог эпизодов — прямо в тексте
                        Text = "Астероидные пояса меня пугают — пролетайте мимо. Вошли в пояс — замечание; два замечания — провал.",
                    };
                case PassengerTaskKind.HoleFlyby:
                    return new PassengerTask
                    {
                        Kind = PassengerTaskKind.HoleFlyby,
                        Text = $"Мимо бездны! Пролетите в {HoleFlybyDistance:0} юн от чёрной дыры — и живыми.",
                        DistanceThreshold = HoleFlybyDistance,
                    };
                default:
                    return null; // пул гарантированно содержит кейс — недостижимо
            }
        }

        // успех точки интереса фиксируется только трекингом — в отличие
        // от условий-избегания, где «не нарушал условие» = молодец
        public static bool IsInterestPoint(PassengerTaskKind kind)
        {
            return kind >= PassengerTaskKind.RingVisit;
        }

        // жёсткий дедлайн «паникёра» — та же формула «дистанция × коэф»,
        // что у задания-дедлайна, но с бо́льшим запасом: срыв убивает весь заказ,
        // а не только рейтинг, время должно быть достижимым
        public static float HurrySecondsFor(float routeDistance)
        {
            return routeDistance / DeadlineSpeedUnit + HurrySlack;
        }

        // тексты тостов-моментов — в одном месте, редактируются здесь.
        // Успех — момент свершения (точки интереса); провал — момент срыва условия
        public static string SuccessMomentText(PassengerTask task)
        {
            switch (task.Kind)
            {
                case PassengerTaskKind.HoleFlyby: return "Мимо бездны пролетели — пассажир доволен";
                case PassengerTaskKind.RingVisit: return "Орбитальное кольцо планеты пройдено";
                case PassengerTaskKind.StarNearby: return "Рядом со звездой — пассажир согрелся";
                default: return "Задание выполнено";
            }
        }

        public static string FailMomentText(PassengerTask task)
        {
            switch (task.Kind)
            {
                case PassengerTaskKind.Deadline: return "Дедлайн вышел — пассажир опаздывает";
                case PassengerTaskKind.NoCollision: return "Хрупкий груз разбит — задание провалено";
                case PassengerTaskKind.NoRefuel: return "Станция использована — задание провалено";
                case PassengerTaskKind.SpeedLimit: return "Превышение скорости — задание провалено";
                case PassengerTaskKind.NoHeat: return "Пассажир в зоне жара — задание провалено";
                case PassengerTaskKind.BeltAvoid: return "Пояс пройден насквозь — задание провалено";
                default: return "Задание провалено";
            }
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
