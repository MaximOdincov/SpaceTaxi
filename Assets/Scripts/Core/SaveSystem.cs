using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SpaceTaxi.Economy;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.Core
{
    // Сейв забега (16_BETA_PLAN §3.9, П10.1): JSON в persistentDataPath.
    // Автосейв — при доставке заказа (GameSession) и при выходе в меню/из игры
    // (SceneFlowButtons); увольнение (3 непогашенные аварии) — сейв удаляется,
    // Save терминальное состояние не пишет. Кривой/чужого уровня сейв — тихий
    // false: «Продолжить» просто неактивно, ошибок в лог нет.
    // Настройки громкости в сейв не входят — они в PlayerPrefs (AudioManager).
    public static class SaveSystem
    {
        private const string FileName = "space_taxi_save.json";

        // смещение корабля над поверхностью — ShipOffset WorldBuilder /
        // магическая семёрка LandingSystem.RespawnAfter
        private const float ShipOffset = 7f;

        // флаг «продолжить»: ставится ContinueGame перед загрузкой сцены,
        // снимается при чтении; новая игра флаг не ставит и сейв игнорирует
        private static bool continuePending;

        private static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        // ------------------------------------------------------------------
        // Схема сейва: плоские сериализуемые типы, никаких Unity-объектов.
        // JsonUtility не умеет Dictionary и null-классы — пары id+уровни вместо
        // словаря, флаг HasOrder вместо null-активного-заказа
        // ------------------------------------------------------------------

        [Serializable]
        public class SaveData
        {
            public string LevelName;
            public int Credits;
            public int DebtBalance;
            public int UnpaidAccidents;
            public float Rating;
            public int Deliveries;
            public int Failures;
            public string CurrentShipId;
            public int PaintIndex; // покраска текущего корабля (Н-3а), 0 = заводская
            public string[] OwnedShipIds;
            public List<ShipUpgrades> Upgrades;
            public bool HasOrder;
            public OrderSave ActiveOrder;
            public ShipSave Ship;
            public string SavedAt; // ISO 8601, для отладки
        }

        [Serializable]
        public class ShipUpgrades
        {
            public string ShipId;
            public int[] Levels; // [Engine, Tank, Hull, Maneuver], 0..3
        }

        [Serializable]
        public class OrderSave
        {
            public int PickupIndex;
            public int DropoffIndex;
            public int Price;
            public int State; // OrderState: 1 = ждём пассажира, 2 = на борту
            public string PassengerName;
            public Color PortraitTint;
            public float TipsFraction; // доля чаевых заказа (Н-14), фиксируется при спавне
            public string TaskKind; // имя PassengerTaskKind; пусто — задания нет
            public string TaskText;
            public float TaskDeadlineRemaining; // остаток дедлайна (Kind == Deadline)
            public float TaskSpeedLimit;
            public float TaskDistanceThreshold;
            public int TaskTargetBodyIndex;
            public float TaskWaitSeconds;
            public bool TaskSucceeded;
            public bool TaskFailed;
        }

        [Serializable]
        public class ShipSave
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Hp;
            public float Fuel;
            public bool Landed;
            public int LandedPlanetIndex; // планета стоянки при Landed, иначе -1
        }

        // ------------------------------------------------------------------
        // Сохранение
        // ------------------------------------------------------------------

        // Снапшот сессии в файл. Не сейвит: сессии нет (меню), мир не собран
        // (Level/Ship/Orders ещё null) или забег терминален (увольнение — сейв
        // уже удалён, автосейв выхода не должен его воскресить)
        public static void Save(GameSession session)
        {
            if (session == null
                || session.Level == null || session.Wallet == null
                || session.Debt == null || session.Rating == null
                || session.Fleet == null || session.Ship == null
                || session.Orders == null
                || session.Debt.UnpaidAccidents >= 3
                // окно респавна: корабль мёртв на месте гибели — не сохраняем,
                // иначе continue поднимет его и Abandon списал бы свой корабль
                || session.Ship.State == ShipState.Respawning
                // проглочен дырой (Н-10): continue должен поднять корабль вне воронки,
                // а не сразу в болтании за горизонтом
                || session.Ship.State == ShipState.Swallowed)
                return;

            var data = new SaveData
            {
                LevelName = session.Level.LevelName,
                Credits = session.Wallet.Credits,
                DebtBalance = session.Debt.Balance,
                UnpaidAccidents = session.Debt.UnpaidAccidents,
                Rating = session.Rating.Value,
                Deliveries = session.Orders.Deliveries,
                Failures = session.Orders.Failures,
                CurrentShipId = session.Fleet.Current != null ? session.Fleet.Current.Id : null,
                PaintIndex = session.Fleet.CurrentPaintIndex,
                SavedAt = DateTime.UtcNow.ToString("o"),
            };

            // владение + уровни улучшений (аренда всегда сток — уровни не пишем)
            var ownedIds = new List<string>();
            var upgrades = new List<ShipUpgrades>();
            foreach (ShipConfig config in session.Fleet.OwnedShips)
            {
                if (config == null || string.IsNullOrEmpty(config.Id) || config.IsRental)
                    continue;
                ownedIds.Add(config.Id);
                upgrades.Add(new ShipUpgrades
                {
                    ShipId = config.Id,
                    Levels = new[]
                    {
                        session.Fleet.GetLevel(config, UpgradeLine.Engine),
                        session.Fleet.GetLevel(config, UpgradeLine.Tank),
                        session.Fleet.GetLevel(config, UpgradeLine.Hull),
                        session.Fleet.GetLevel(config, UpgradeLine.Maneuver),
                    },
                });
            }
            data.OwnedShipIds = ownedIds.ToArray();
            data.Upgrades = upgrades;

            data.ActiveOrder = CaptureActiveOrder(session);
            data.HasOrder = data.ActiveOrder != null;
            data.Ship = CaptureShip(session);

            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(data));
            }
            catch (Exception)
            {
                // упавшая запись не должна ронять игру — забег просто не сейвится
            }
        }

        // активный заказ или null (Active обнулён при доставке/провале — сюда
        // доезжают только состояния Active/Aboard)
        private static OrderSave CaptureActiveOrder(GameSession session)
        {
            OrderOffer offer = session.Orders.Active;
            if (offer == null)
                return null;

            var save = new OrderSave
            {
                PickupIndex = offer.PickupIndex,
                DropoffIndex = offer.DropoffIndex,
                Price = offer.Price,
                State = (int)offer.State,
            };
            if (offer.Passenger != null)
            {
                save.PassengerName = offer.Passenger.DisplayName;
                save.PortraitTint = offer.Passenger.PortraitTint;
            }
            save.TipsFraction = offer.TipsFraction;
            if (offer.Task != null)
            {
                save.TaskKind = offer.Task.Kind.ToString();
                save.TaskText = offer.Task.Text;
                save.TaskSpeedLimit = offer.Task.SpeedLimit;
                save.TaskDistanceThreshold = offer.Task.DistanceThreshold;
                save.TaskTargetBodyIndex = offer.Task.TargetBodyIndex;
                save.TaskWaitSeconds = offer.Task.WaitSeconds;
                save.TaskSucceeded = offer.Task.Succeeded;
                save.TaskFailed = offer.Task.Failed;
                // только остаток — при загрузке таймер продолжится с него
                if (offer.Task.Kind == PassengerTaskKind.Deadline)
                    save.TaskDeadlineRemaining = offer.Task.DeadlineRemaining(Time.time);
            }
            return save;
        }

        private static ShipSave CaptureShip(GameSession session)
        {
            Ship ship = session.Ship;
            var save = new ShipSave
            {
                Position = ship.transform.position,
                Velocity = ship.Body != null ? ship.Body.linearVelocity : Vector2.zero,
                Hp = ship.Hp,
                Fuel = ship.Fuel,
                Landed = ship.State == ShipState.Landed,
                LandedPlanetIndex = -1,
            };
            // планета стоянки: индекс в LevelAsset по имени — тела и офферы
            // сопоставляются именами по всему проекту
            if (save.Landed && ship.LandedOn != null)
                save.LandedPlanetIndex = PlanetIndexByName(session.Level, ship.LandedOn.DisplayName);
            return save;
        }

        private static int PlanetIndexByName(LevelAsset level, string name)
        {
            if (level == null || level.Bodies == null)
                return -1;
            for (int i = 0; i < level.Bodies.Count; i++)
                if (level.Bodies[i].DisplayName == name)
                    return i;
            return -1;
        }

        // ------------------------------------------------------------------
        // Чтение
        // ------------------------------------------------------------------

        // Разбор файла. Любая ошибка (нет/пустой файл, кривой JSON, чужой
        // уровень) — false без единой строки в лог: §3.9 «кривой сейв =
        // Продолжить неактивно»
        public static bool TryLoad(out SaveData data, string expectedLevelName = null)
        {
            data = null;
            try
            {
                if (!File.Exists(SavePath))
                    return false;
                string json = File.ReadAllText(SavePath);
                if (string.IsNullOrWhiteSpace(json))
                    return false;
                data = JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception)
            {
                data = null;
            }
            if (data == null || string.IsNullOrEmpty(data.LevelName))
                return false;
            if (!string.IsNullOrEmpty(expectedLevelName) && data.LevelName != expectedLevelName)
                return false;
            return true;
        }

        // для активности «Продолжить» (П10.2) — без разбора JSON
        public static bool HasSave()
        {
            try
            {
                var file = new FileInfo(SavePath);
                return file.Exists && file.Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void DeleteSave()
        {
            try
            {
                if (File.Exists(SavePath))
                    File.Delete(SavePath);
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
        public static void MarkContinue() => continuePending = true;

        // читает и снимает флаг — снимается после чтения
        public static bool ConsumeContinueFlag()
        {
            bool pending = continuePending;
            continuePending = false;
            return pending;
        }

        // ------------------------------------------------------------------
        // Восстановление
        // ------------------------------------------------------------------

        // Зовёт GameSession.Start ПОСЛЕ сборки мира: мир построен по умолчанию,
        // поверх применяется состояние сейва. Без флага «продолжить» или с
        // кривым сейвом — тихий выход, сессия остаётся дефолтной
        public static void ApplyTo(GameSession session)
        {
            if (session == null || !ConsumeContinueFlag())
                return;
            string expectedLevel = session.Level != null ? session.Level.LevelName : null;
            if (!TryLoad(out SaveData data, expectedLevel))
                return;

            session.Wallet?.Restore(data.Credits);
            session.Debt?.Restore(data.DebtBalance, data.UnpaidAccidents);
            session.Rating?.Restore(data.Rating);
            session.Orders?.RestoreCounters(data.Deliveries, data.Failures);

            RestoreFleet(session, data);
            RestoreShip(session, data.Ship);
            RestoreActiveOrder(session, data.HasOrder ? data.ActiveOrder : null);
        }

        // парк: конфиги по Id из каталога Resources/Ships (способ П3.2);
        // неизвестные модели пропускаются, кривой текущий id — аренда
        private static void RestoreFleet(GameSession session, SaveData data)
        {
            ShipConfig[] catalog = Resources.LoadAll<ShipConfig>("Ships");

            var owned = new List<ShipConfig>();
            if (data.OwnedShipIds != null)
                foreach (string id in data.OwnedShipIds)
                {
                    ShipConfig config = FindInCatalog(catalog, id);
                    if (config != null && !config.IsRental && !owned.Contains(config))
                        owned.Add(config);
                }

            var levels = new Dictionary<string, int[]>();
            if (data.Upgrades != null)
                foreach (ShipUpgrades entry in data.Upgrades)
                    if (entry != null && !string.IsNullOrEmpty(entry.ShipId) && entry.Levels != null)
                        levels[entry.ShipId] = entry.Levels;

            session.Fleet.RestoreState(FindInCatalog(catalog, data.CurrentShipId), owned, levels, data.PaintIndex);
        }

        private static ShipConfig FindInCatalog(ShipConfig[] catalog, string id)
        {
            if (catalog == null || string.IsNullOrEmpty(id))
                return null;
            foreach (ShipConfig config in catalog)
                if (config != null && config.Id == id)
                    return config;
            return null;
        }

        // корабль: Landed — ставим стоя на поверхность планеты стоянки (нос от
        // центра, как LandingSystem.Land после снапа), Flying — позиция и
        // скорость как есть. HP/топливо после RestoreState: пересадка в нём
        // наливает полный бак, сейв перекрывает
        private static void RestoreShip(GameSession session, ShipSave save)
        {
            if (save == null || session.Ship == null)
                return;
            Ship ship = session.Ship;

            Planet planet = save.Landed ? FindPlanet(session, save.LandedPlanetIndex) : null;
            if (planet != null)
            {
                Vector2 planetPos = planet.transform.position;
                Vector2 dir = save.Position - planetPos;
                dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.up;
                ship.transform.position = planetPos + dir * (planet.Radius + ShipOffset);
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                ship.transform.rotation = Quaternion.Euler(0f, 0f, angle - NoseAngle(ship));
                if (ship.Body != null)
                    ship.Body.linearVelocity = Vector2.zero;
                ship.LandedOn = planet;
                ship.State = ShipState.Landed;
            }
            else
            {
                ship.transform.position = save.Position;
                if (ship.Body != null)
                    ship.Body.linearVelocity = save.Velocity;
                ship.LandedOn = null;
                ship.State = ShipState.Flying;
                // нос по вектору скорости (как UpdateHeading); у нуля скорости
                // вектор «шумит» — оставляем ориентацию спавна
                if (save.Velocity.magnitude >= 30f)
                {
                    float angle = Mathf.Atan2(save.Velocity.y, save.Velocity.x) * Mathf.Rad2Deg;
                    ship.transform.rotation = Quaternion.Euler(0f, 0f, angle - NoseAngle(ship));
                }
            }

            // Hp > 0: мёртвый корабль в сейв не попадает (гибель → респавн до сейва)
            ship.Hp = Mathf.Clamp(save.Hp, 1f, ship.Eff.HpMax);
            ship.Fuel = Mathf.Clamp(save.Fuel, 0f, ship.Eff.FuelMax);
        }

        private static float NoseAngle(Ship ship)
        {
            var controller = ship.GetComponent<ShipController>();
            return controller != null ? controller.SpriteNoseAngle : 0f;
        }

        // тело уровня по индексу: сопоставление по имени, как в OrderService.BodyByIndex
        private static Planet FindPlanet(GameSession session, int index)
        {
            LevelAsset level = session.Level;
            if (level == null || level.Bodies == null
                || index < 0 || index >= level.Bodies.Count)
                return null;
            var gravity = UnityEngine.Object.FindAnyObjectByType<GravityField>();
            if (gravity == null)
                return null;
            string name = level.Bodies[index].DisplayName;
            foreach (CelestialBody body in gravity.Bodies)
            {
                Planet planet = body as Planet;
                if (planet != null && planet.DisplayName == name)
                    return planet;
            }
            return null;
        }

        // активный заказ: пассажир пересобирается по пулам (имя и тон портрета —
        // из сейва, реплики не храним), задание — из сохранённых параметров;
        // дедлайн продолжает тикать с сохранённого остатка
        private static void RestoreActiveOrder(GameSession session, OrderSave save)
        {
            if (save == null || session.Orders == null || session.Level == null)
                return;
            if (save.PickupIndex < 0 || save.PickupIndex >= session.Level.Bodies.Count
                || save.DropoffIndex < 0 || save.DropoffIndex >= session.Level.Bodies.Count)
                return; // индексы не сходятся с уровнем — заказ не восстановить

            var offer = new OrderOffer
            {
                Passenger = RebuildPassenger(session, save),
                PickupIndex = save.PickupIndex,
                DropoffIndex = save.DropoffIndex,
                Price = Mathf.Max(0, save.Price),
                TipsFraction = save.TipsFraction,
                State = save.State == (int)OrderState.Aboard ? OrderState.Aboard : OrderState.Active,
                ExpiresAt = float.MaxValue, // активный карточкой не протухает
                Task = RebuildTask(save),
            };
            offer.Task?.StartDeadline(Time.time); // с сохранённого остатка
            session.Orders.RestoreActive(offer);
        }

        private static PassengerTask RebuildTask(OrderSave save)
        {
            if (string.IsNullOrEmpty(save.TaskKind)
                || !Enum.TryParse(save.TaskKind, out PassengerTaskKind kind)
                || kind == PassengerTaskKind.None)
                return null;
            return new PassengerTask
            {
                Kind = kind,
                Text = save.TaskText,
                // остаток становится лимитом: таймер стартует на загрузке (StartDeadline)
                DeadlineSeconds = kind == PassengerTaskKind.Deadline
                    ? Mathf.Max(0f, save.TaskDeadlineRemaining)
                    : 0f,
                SpeedLimit = save.TaskSpeedLimit,
                DistanceThreshold = save.TaskDistanceThreshold,
                TargetBodyIndex = save.TaskTargetBodyIndex,
                WaitSeconds = save.TaskWaitSeconds,
                Succeeded = save.TaskSucceeded,
                Failed = save.TaskFailed,
            };
        }

        // runtime-профиль, как у генератора (Destroy при протухании безопасен);
        // реплики — общие пулы (Н-14), пулов нет — пустые, UI это терпит
        private static PassengerProfile RebuildPassenger(GameSession session, OrderSave save)
        {
            var profile = ScriptableObject.CreateInstance<PassengerProfile>();
            profile.RuntimeInstance = true;
            profile.DisplayName = string.IsNullOrEmpty(save.PassengerName)
                ? "Гость системы"
                : save.PassengerName;
            profile.PortraitTint = save.PortraitTint;

            PassengerPools pools = session.Passengers != null ? session.Passengers.Pools : null;
            if (pools != null)
            {
                // полные копии массивов — реплика выбирается случайно при показе
                profile.Greetings = pools.Greetings != null ? (string[])pools.Greetings.Clone() : null;
                profile.Wishes = pools.Wishes != null ? (string[])pools.Wishes.Clone() : null;
                profile.Thanks = pools.Thanks != null ? (string[])pools.Thanks.Clone() : null;
                profile.Complaints = pools.Complaints != null ? (string[])pools.Complaints.Clone() : null;
            }
            return profile;
        }
    }
}
