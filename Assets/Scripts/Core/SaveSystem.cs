using System; // Serializable и Exception
using System.Collections.Generic; // списки/словари схемы сейва
using System.IO; // файлы сохранения
using UnityEngine; // JsonUtility/Vector2/Color
using SpaceTaxi.Economy; // кошелёк/парк
using SpaceTaxi.Orders; // заказы и задания
using SpaceTaxi.Physics; // состояния корабля
using SpaceTaxi.World; // тела уровня

namespace SpaceTaxi.Core
{
    // Сейв забега (16_BETA_PLAN §3.9, П10.1): JSON в persistentDataPath.
    // Автосейв — при доставке заказа (GameSession) и при выходе в меню/из игры
    // (SceneFlowButtons); увольнение (3 непогашенные аварии) — сейв удаляется,
    // Save терминальное состояние не пишет. Кривой/чужого уровня сейв — тихий
    // false: «Продолжить» просто неактивно, ошибок в лог нет.
    // Настройки громкости в сейв не входят — они в PlayerPrefs (AudioManager).
    public static class SaveSystem // статический класс сейва
    {
        private const string FileName = "space_taxi_save.json"; // имя файла сейва

        // флаг «продолжить»: ставится ContinueGame перед загрузкой сцены,
        // снимается при чтении; новая игра флаг не ставит и сейв игнорирует
        private static bool continuePending; // единственное глобальное состояние ядра

        private static string SavePath => Path.Combine(Application.persistentDataPath, FileName); // полный путь сейва

        // ------------------------------------------------------------------
        // Схема сейва: плоские сериализуемые типы, никаких Unity-объектов.
        // JsonUtility не умеет Dictionary и null-классы — пары id+уровни вместо
        // словаря, флаг HasOrder вместо null-активного-заказа
        // ------------------------------------------------------------------

        [Serializable] // верхний уровень схемы
        public class SaveData // весь снимок забега
        {
            public string LevelName; // имя уровня (сейв чужого уровня не грузится)
            public int Credits; // кредиты кошелька
            public int DebtBalance; // сумма долга

            public float Rating; // рейтинг таксиста
            public int Deliveries; // счётчик доставок
            public int Failures; // счётчик провалов
            public string CurrentShipId; // id текущего корабля
            public int PaintIndex; // покраска текущего корабля (Н-3а), 0 = заводская
            public string[] OwnedShipIds; // список своих кораблей
            public List<ShipUpgrades> Upgrades; // уровни улучшений по кораблям
            public bool HasOrder; // флаг вместо null-заказа
            public OrderSave ActiveOrder; // активный заказ
            public ShipSave Ship; // состояние корабля
            public string SavedAt; // ISO 8601, для отладки
        }

        [Serializable] // запись улучшений одного корабля
        public class ShipUpgrades
        {
            public string ShipId; // id корабля
            public int[] Levels; // [Engine, Tank, Hull, Maneuver], 0..3
        }

        [Serializable] // запись активного заказа
        public class OrderSave
        {
            public int PickupIndex; // индекс планеты посадки пассажира
            public int DropoffIndex; // индекс планеты сдачи
            public int Price; // зафиксированная цена
            public int State; // OrderState: 1 = ждём пассажира, 2 = на борту
            public string PassengerName; // имя пассажира
            public Color PortraitTint; // тон портрета
            public int PortraitSeed; // сид портрета NPC: 0 — нет (старые сейвы)
            public float TipsFraction; // доля чаевых заказа (Н-14), фиксируется при спавне
            public string TaskKind; // имя PassengerTaskKind; пусто — задания нет
            public string TaskText; // текст задания
            public float TaskDeadlineRemaining; // остаток дедлайна (Kind == Deadline)
            public float TaskSpeedLimit; // лимит скорости задания
            public float TaskDistanceThreshold; // порог дистанции задания
            public int TaskTargetBodyIndex; // целевое тело задания
            public float TaskWaitSeconds; // поле осталось для старых сейвов (не пишется, Н8.49)
            public bool TaskSucceeded; // задание выполнено
            public bool TaskFailed; // задание провалено
        }

        [Serializable] // запись состояния корабля
        public class ShipSave
        {
            public Vector2 Position; // позиция корабля
            public Vector2 Velocity; // скорость
            public float Hp; // корпус
            public float Fuel; // топливо
            public bool Landed; // стоит на планете
            public int LandedPlanetIndex; // планета стоянки при Landed, иначе -1
        }

        // ------------------------------------------------------------------
        // Сохранение
        // ------------------------------------------------------------------

        // Снапшот сессии в файл. Не сейвит: сессии нет (меню), мир не собран
        // (Level/Ship/Orders ещё null) или забег терминален (увольнение — сейв
        // уже удалён, автосейв выхода не должен его воскресить)
        public static void Save(GameSession session) // сохранить сессию
        {
            if (session == null // сессии нет
                || session.Level == null || session.Wallet == null // мир не собран
                || session.Debt == null || session.Rating == null // сервисы не готовы
                || session.Fleet == null || session.Ship == null // корабля нет
                || session.Orders == null // заказы не готовы

                // окно респавна: корабль мёртв на месте гибели — не сохраняем,
                // иначе continue поднимет его и Abandon списал бы свой корабль
                || session.Ship.State == ShipState.Respawning // в окне респавна
                // проглочен дырой (Н-10): continue должен поднять корабль вне воронки,
                // а не сразу в болтании за горизонтом
                || session.Ship.State == ShipState.Swallowed) // проглочен дырой
                return; // тихий отказ

            var data = new SaveData // сборка снимка простых полей
            {
                LevelName = session.Level.LevelName, // имя уровня
                Credits = session.Wallet.Credits, // кредиты
                DebtBalance = session.Debt.Balance, // долг

                Rating = session.Rating.Value, // рейтинг
                Deliveries = session.Orders.Deliveries, // доставки
                Failures = session.Orders.Failures, // провалы
                CurrentShipId = session.Fleet.Current != null ? session.Fleet.Current.Id : null, // id борта
                PaintIndex = session.Fleet.CurrentPaintIndex, // покраска
                SavedAt = DateTime.UtcNow.ToString("o"), // отметка времени
            };

            // владение + уровни улучшений (аренда всегда сток — уровни не пишем)
            var ownedIds = new List<string>(); // список своих кораблей
            var upgrades = new List<ShipUpgrades>(); // их уровни улучшений
            foreach (ShipConfig config in session.Fleet.OwnedShips) // обход парка
            {
                if (config == null || string.IsNullOrEmpty(config.Id) || config.IsRental) // аренду пропускаем
                    continue;
                ownedIds.Add(config.Id); // id в список своих
                upgrades.Add(new ShipUpgrades // запись улучшений
                {
                    ShipId = config.Id, // чьи уровни
                    Levels = new[] // четыре линии улучшений
                    {
                        session.Fleet.GetLevel(config, UpgradeLine.Engine), // двигатель
                        session.Fleet.GetLevel(config, UpgradeLine.Tank), // бак
                        session.Fleet.GetLevel(config, UpgradeLine.Hull), // корпус
                        session.Fleet.GetLevel(config, UpgradeLine.Maneuver), // управляемость
                    },
                });
            }
            data.OwnedShipIds = ownedIds.ToArray(); // в снимок
            data.Upgrades = upgrades;

            data.ActiveOrder = CaptureActiveOrder(session); // снимок активного заказа
            data.HasOrder = data.ActiveOrder != null; // флаг вместо null
            data.Ship = CaptureShip(session); // снимок корабля

            try // запись файла
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(data)); // JSON в диск
            }
            catch (Exception)
            {
                // упавшая запись не должна ронять игру — забег просто не сейвится
            }
        }

        // активный заказ или null (Active обнулён при доставке/провале — сюда
        // доезжают только состояния Active/Aboard)
        private static OrderSave CaptureActiveOrder(GameSession session) // снять активный заказ
        {
            OrderOffer offer = session.Orders.Active; // текущий оффер
            if (offer == null) // заказа нет
                return null;

            var save = new OrderSave // простые поля оффера
            {
                PickupIndex = offer.PickupIndex, // планета посадки
                DropoffIndex = offer.DropoffIndex, // планета сдачи
                Price = offer.Price, // цена
                State = (int)offer.State, // состояние числом
            };
            if (offer.Passenger != null) // пассажир назначен
            {
                save.PassengerName = offer.Passenger.DisplayName; // имя
                save.PortraitTint = offer.Passenger.PortraitTint; // тон портрета
                save.PortraitSeed = offer.Passenger.PortraitSeed; // сид портрета
            }
            save.TipsFraction = offer.TipsFraction; // доля чаевых
            if (offer.Task != null) // задание есть
            {
                save.TaskKind = offer.Task.Kind.ToString(); // вид задания строкой
                save.TaskText = offer.Task.Text; // текст
                save.TaskSpeedLimit = offer.Task.SpeedLimit; // лимит скорости
                save.TaskDistanceThreshold = offer.Task.DistanceThreshold; // порог дистанции
                save.TaskTargetBodyIndex = offer.Task.TargetBodyIndex; // целевое тело
                save.TaskSucceeded = offer.Task.Succeeded; // статус выполнено
                save.TaskFailed = offer.Task.Failed; // статус провалено
                // только остаток — при загрузке таймер продолжится с него
                if (offer.Task.Kind == PassengerTaskKind.Deadline) // дедлайн-задание
                    save.TaskDeadlineRemaining = offer.Task.DeadlineRemaining(Time.time); // остаток сейчас
            }
            return save;
        }

        private static ShipSave CaptureShip(GameSession session) // снять состояние корабля
        {
            Ship ship = session.Ship; // борт
            var save = new ShipSave // простые поля
            {
                Position = ship.transform.position, // позиция
                Velocity = ship.Body != null ? ship.Body.linearVelocity : Vector2.zero, // скорость
                Hp = ship.Hp, // корпус
                Fuel = ship.Fuel, // бак
                Landed = ship.State == ShipState.Landed, // стоит
                LandedPlanetIndex = -1, // планета пока неизвестна
            };
            // планета стоянки: индекс в LevelAsset по имени — тела и офферы
            // сопоставляются именами по всему проекту
            if (save.Landed && ship.LandedOn != null) // стоит на планете
                save.LandedPlanetIndex = PlanetIndexByName(session.Level, ship.LandedOn.DisplayName); // индекс по имени
            return save;
        }

        private static int PlanetIndexByName(LevelAsset level, string name) // индекс тела по имени
        {
            if (level == null || level.Bodies == null) // данных нет
                return -1;
            for (int i = 0; i < level.Bodies.Count; i++) // линейный поиск
                if (level.Bodies[i].DisplayName == name) // имя совпало
                    return i;
            return -1;
        }

        // ------------------------------------------------------------------
        // Чтение
        // ------------------------------------------------------------------

        // Разбор файла. Любая ошибка (нет/пустой файл, кривой JSON, чужой
        // уровень) — false без единой строки в лог: §3.9 «кривой сейв =
        // Продолжить неактивно»
        public static bool TryLoad(out SaveData data, string expectedLevelName = null) // безопасное чтение
        {
            data = null; // по умолчанию ничего
            try // чтение может упасть на чём угодно
            {
                if (!File.Exists(SavePath)) // файла нет
                    return false;
                string json = File.ReadAllText(SavePath); // прочитать
                if (string.IsNullOrWhiteSpace(json)) // пустой
                    return false;
                data = JsonUtility.FromJson<SaveData>(json); // разобрать
            }
            catch (Exception)
            {
                data = null; // кривой JSON — тихо
            }
            if (data == null || string.IsNullOrEmpty(data.LevelName)) // не разобралось
                return false;
            if (!string.IsNullOrEmpty(expectedLevelName) && data.LevelName != expectedLevelName) // чужой уровень
                return false;
            return true;
        }

        // для активности «Продолжить» (П10.2) — без разбора JSON
        public static bool HasSave() // есть ли файл сейва
        {
            try // доступ к диску может упасть
            {
                var file = new FileInfo(SavePath); // информация о файле
                return file.Exists && file.Length > 0; // есть и не пустой
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void DeleteSave() // удалить сейв (увольнение/новая игра)
        {
            try
            {
                if (File.Exists(SavePath)) // файл есть
                    File.Delete(SavePath); // удалить
            }
            catch (Exception)
            {
                // нет файла/нет доступа — считать сброшенным
            }
        }

        // ------------------------------------------------------------------
        // Режим «продолжить»
        // ------------------------------------------------------------------

        // ставит флаг до загрузки сцены (SceneFlowButtons.ContinueGame)
        public static void MarkContinue() => continuePending = true; // отметить продолжение

        // читает и снимает флаг — снимается после чтения
        public static bool ConsumeContinueFlag() // взять флаг одним чтением
        {
            bool pending = continuePending; // запомнить
            continuePending = false; // снять
            return pending;
        }

        // ------------------------------------------------------------------
        // Восстановление
        // ------------------------------------------------------------------

        // Зовёт GameSession.Start ПОСЛЕ сборки мира: мир построен по умолчанию,
        // поверх применяется состояние сейва. Без флага «продолжить» или с
        // кривым сейвом — тихий выход, сессия остаётся дефолтной
        public static void ApplyTo(GameSession session) // наложить сейв на сессию
        {
            if (session == null || !ConsumeContinueFlag()) // не «продолжить» — игнор
                return;
            string expectedLevel = session.Level != null ? session.Level.LevelName : null; // имя уровня сцены
            if (!TryLoad(out SaveData data, expectedLevel)) // кривой/чужой сейв
                return;

            session.Wallet?.Restore(data.Credits); // кредиты
            session.Debt?.Restore(data.DebtBalance); // долг (Н13.1: без счётчика аварий)
            session.Rating?.Restore(data.Rating); // рейтинг
            session.Orders?.RestoreCounters(data.Deliveries, data.Failures); // счётчики заказов

            RestoreFleet(session, data); // парк и апгрейды
            RestoreShip(session, data.Ship); // корабль
            RestoreActiveOrder(session, data.HasOrder ? data.ActiveOrder : null); // заказ
        }

        // парк: конфиги по Id из каталога Resources/Ships (способ П3.2);
        // неизвестные модели пропускаются, кривой текущий id — аренда
        private static void RestoreFleet(GameSession session, SaveData data) // восстановить парк
        {
            ShipConfig[] catalog = Resources.LoadAll<ShipConfig>("Ships"); // все модели из каталога

            var owned = new List<ShipConfig>(); // восстановленный список своих
            if (data.OwnedShipIds != null) // список есть
                foreach (string id in data.OwnedShipIds) // по ids из сейва
                {
                    ShipConfig config = FindInCatalog(catalog, id); // найти ассет
                    if (config != null && !config.IsRental && !owned.Contains(config)) // валидный новый
                        owned.Add(config);
                }

            var levels = new Dictionary<string, int[]>(); // уровни по id
            if (data.Upgrades != null) // записи есть
                foreach (ShipUpgrades entry in data.Upgrades) // по записям
                    if (entry != null && !string.IsNullOrEmpty(entry.ShipId) && entry.Levels != null) // валидна
                        levels[entry.ShipId] = entry.Levels; // в словарь

            session.Fleet.RestoreState(FindInCatalog(catalog, data.CurrentShipId), owned, levels, data.PaintIndex); // в парк
        }

        private static ShipConfig FindInCatalog(ShipConfig[] catalog, string id) // поиск ассета по id
        {
            if (catalog == null || string.IsNullOrEmpty(id)) // пусто
                return null;
            foreach (ShipConfig config in catalog) // линейный поиск
                if (config != null && config.Id == id) // совпал
                    return config;
            return null;
        }

        // корабль: Landed — ставим стоя на поверхность планеты стоянки (нос от
        // центра, как LandingSystem.Land после снапа), Flying — позиция и
        // скорость как есть. HP/топливо после RestoreState: пересадка в нём
        // наливает полный бак, сейв перекрывает
        private static void RestoreShip(GameSession session, ShipSave save) // восстановить корабль
        {
            if (save == null || session.Ship == null) // данных нет
                return;
            Ship ship = session.Ship; // борт

            Planet planet = save.Landed ? FindPlanet(session, save.LandedPlanetIndex) : null; // планета стоянки
            if (planet != null) // стоял на планете
            {
                Vector2 planetPos = planet.transform.position; // центр планеты
                Vector2 dir = save.Position - planetPos; // направление от центра
                dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.up; // нормаль или вверх
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg; // угол направления
                // посадочная поза как в игре (ShipPlacement): отступ от капсулы,
                // а не константа — крупный корпус при загрузке не уходит
                // хвостом в планету; телепорт и тела, и transform
                float extent = ShipPlacement.LandedExtent(ship.GetComponent<ShipModelVisual>()); // габарит корпуса
                ShipPlacement.TeleportLanded(ship, planetPos + dir * (planet.Radius + extent), angle - NoseAngle(ship)); // телепорт в позу
                if (ship.Body != null) // физическое тело есть
                    ship.Body.linearVelocity = Vector2.zero; // стоит
                ship.LandedOn = planet; // планета стоянки
                ship.SetState(ShipState.Landed); // состояние «стоит»
            }
            else // летел
            {
                ship.transform.position = save.Position; // позиция
                if (ship.Body != null) // тело есть
                    ship.Body.linearVelocity = save.Velocity; // скорость
                ship.LandedOn = null; // без планеты
                ship.SetState(ShipState.Flying); // состояние «летит»
                // нос по вектору скорости (как UpdateHeading); у нуля скорости
                // вектор «шумит» — оставляем ориентацию спавна
                if (save.Velocity.magnitude >= 30f) // скорость достаточна
                {
                    float angle = Mathf.Atan2(save.Velocity.y, save.Velocity.x) * Mathf.Rad2Deg; // угол вектора
                    ship.transform.rotation = Quaternion.Euler(0f, 0f, angle - NoseAngle(ship)); // ориентировать корпус
                }
            }

            // Hp > 0: мёртвый корабль в сейв не попадает (гибель → респавн до сейва)
            ship.SetHp(Mathf.Max(1f, save.Hp)); // корпус (пол 1 сохранён, верхний кламп в SetHp — Н10.14)
            ship.Fuel = Mathf.Clamp(save.Fuel, 0f, ship.Eff.FuelMax); // бак с клампом
        }

        private static float NoseAngle(Ship ship) // смещение носа спрайта
        {
            var controller = ship.GetComponent<ShipController>(); // контроллер знает угол
            return controller != null ? controller.SpriteNoseAngle : 0f; // 0 без контроллера
        }

        // тело уровня по индексу: сопоставление по имени, как в OrderService.BodyByIndex
        private static Planet FindPlanet(GameSession session, int index) // планета по индексу уровня
        {
            LevelAsset level = session.Level; // данные уровня
            if (level == null || level.Bodies == null // данных нет
                || index < 0 || index >= level.Bodies.Count) // индекс вне диапазона
                return null;
            var gravity = UnityEngine.Object.FindAnyObjectByType<GravityField>(); // реестр тел
            if (gravity == null) // мира нет
                return null;
            string name = level.Bodies[index].DisplayName; // имя по индексу
            foreach (CelestialBody body in gravity.Bodies) // поиск среди построенных тел
            {
                Planet planet = body as Planet; // только планеты
                if (planet != null && planet.DisplayName == name) // имя совпало
                    return planet;
            }
            return null;
        }

        // активный заказ: пассажир пересобирается по пулам (имя и тон портрета —
        // из сейва, реплики не храним), задание — из сохранённых параметров;
        // дедлайн продолжает тикать с сохранённого остатка
        private static void RestoreActiveOrder(GameSession session, OrderSave save) // восстановить заказ
        {
            if (save == null || session.Orders == null || session.Level == null) // данных нет
                return;
            if (save.PickupIndex < 0 || save.PickupIndex >= session.Level.Bodies.Count // индекс посадки вне уровня
                || save.DropoffIndex < 0 || save.DropoffIndex >= session.Level.Bodies.Count) // индекс сдачи вне уровня
                return; // индексы не сходятся с уровнем — заказ не восстановить

            var offer = new OrderOffer // пересборка оффера
            {
                Passenger = RebuildPassenger(session, save), // пассажир из сейва
                PickupIndex = save.PickupIndex, // планета посадки
                DropoffIndex = save.DropoffIndex, // планета сдачи
                Price = Mathf.Max(0, save.Price), // цена без отрицательных
                TipsFraction = save.TipsFraction, // чаевые
                State = save.State == (int)OrderState.Aboard ? OrderState.Aboard : OrderState.Active, // состояние
                ExpiresAt = float.MaxValue, // активный карточкой не протухает
                Task = RebuildTask(save), // задание
            };
            // старый сейв без цены — цену досчитает RestoreActive (единая точка)
            offer.Task?.StartDeadline(Time.time); // дедлайн с сохранённого остатка
            session.Orders.RestoreActive(offer); // в сервис заказов
        }

        private static PassengerTask RebuildTask(OrderSave save) // пересборка задания
        {
            if (string.IsNullOrEmpty(save.TaskKind) // вида нет
                || !Enum.TryParse(save.TaskKind, out PassengerTaskKind kind) // не парсится
                || kind == PassengerTaskKind.None) // пустой вид
                return null;
            return new PassengerTask // задание из полей сейва
            {
                Kind = kind, // вид
                Text = save.TaskText, // текст
                // остаток становится лимитом: таймер стартует на загрузке (StartDeadline)
                DeadlineSeconds = kind == PassengerTaskKind.Deadline // только для дедлайна
                    ? Mathf.Max(0f, save.TaskDeadlineRemaining) // остаток как лимит
                    : 0f,
                SpeedLimit = save.TaskSpeedLimit, // лимит скорости
                DistanceThreshold = save.TaskDistanceThreshold, // порог дистанции
                TargetBodyIndex = save.TaskTargetBodyIndex, // целевое тело
                Succeeded = save.TaskSucceeded, // статус
                Failed = save.TaskFailed, // статус
            };
        }

        // runtime-профиль, как у генератора (Destroy при протухании безопасен);
        // реплики — общие пулы (Н-14), пулов нет — пустые, UI это терпит
        private static PassengerProfile RebuildPassenger(GameSession session, OrderSave save) // пересборка пассажира
        {
            var profile = ScriptableObject.CreateInstance<PassengerProfile>(); // runtime-инстанс профиля
            profile.RuntimeInstance = true; // помечен как runtime — можно Destroy
            profile.DisplayName = string.IsNullOrEmpty(save.PassengerName) // имя из сейва или заглушка
                ? "Гость системы"
                : save.PassengerName;
            profile.PortraitTint = save.PortraitTint; // тон портрета
            if (save.PortraitSeed != 0) // сид есть
                profile.Portrait = NpcPortrait.Generate(save.PortraitSeed); // тот же облик, что до сейва

            PassengerPools pools = session.Passengers != null ? session.Passengers.Pools : null; // пулы реплик
            if (pools != null) // пулы найдены
            {
                // полные копии массивов — реплика выбирается случайно при показе
                profile.Greetings = pools.Greetings != null ? (string[])pools.Greetings.Clone() : null; // приветствия
                profile.Wishes = pools.Wishes != null ? (string[])pools.Wishes.Clone() : null; // пожелания
                profile.Thanks = pools.Thanks != null ? (string[])pools.Thanks.Clone() : null; // благодарности
                profile.Complaints = pools.Complaints != null ? (string[])pools.Complaints.Clone() : null; // жалобы
            }
            return profile;
        }
    }
}
