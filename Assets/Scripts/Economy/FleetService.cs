using System;
using System.Collections.Generic;
using SpaceTaxi.Physics;

namespace SpaceTaxi.Economy
{
    // Парк кораблей: владение купленными моделями, аренда «Яйца» как фолбэк,
    // уровни улучшений купленных кораблей (аренда всегда сток).
    // Живёт в GameSession (создаётся в Awake); UI магазина — экран станции
    public sealed class FleetService
    {
        private readonly List<ShipConfig> owned = new List<ShipConfig>();
        // уровни улучшений по ShipConfig.Id: [Engine, Tank, Hull, Maneuver], 0..3
        private readonly Dictionary<string, int[]> upgradeLevels = new Dictionary<string, int[]>();
        // выбранная покраска по ShipConfig.Id: 0 = заводская, 1..N —
        // PaintVariants. Запоминается при покупке/восстановлении и
        // подставляется при пересадке — иначе SwitchTo сбрасывал бы корпус
        // на заводскую (баг «купил расцветку — корабль стандартный»)
        private readonly Dictionary<string, int> paintByShipId = new Dictionary<string, int>();
        private Ship ship; // живой корабль для мгновенной пересадки
        private UpgradeConfig upgradeConfig;

        // всегда валиден: старт новой игры — арендное «Яйцо»
        public ShipConfig Current { get; private set; }

        // покраска текущего корабля: 0 = заводская, 1..N — PaintVariants.
        // Купленная покраска переживает пересадки — см. paintByShipId
        public int CurrentPaintIndex { get; private set; }

        public IReadOnlyList<ShipConfig> OwnedShips => owned;

        // конфиг улучшений — для UI магазина и проверки цен
        public UpgradeConfig Upgrades => upgradeConfig;

        // смена текущего корабля — для UI
        public event Action ShipChanged;

        // сменилась покраска текущего корабля — для UI
        public event Action PaintChanged;

        // уровни улучшений изменились — для UI
        public event Action UpgradesChanged;

        public FleetService()
        {
            // поле currentShip сцены не читаем: новая игра всегда с аренды
            Current = ShipConfig.EggRentalFallback;
        }

        // без подписок здесь — на гибель/потерю подписывается GameSession
        public void BindShip(Ship ship)
        {
            this.ship = ship;
            // корабль строится после Awake — конфиг улучшений доезжает сюда
            if (ship != null && upgradeConfig != null)
                ship.BindUpgradeConfig(upgradeConfig);
        }

        // конфиг улучшений (GameSession в Awake; null не сохраняем)
        public void BindUpgrades(UpgradeConfig config)
        {
            if (config == null || config == upgradeConfig)
                return;
            upgradeConfig = config;
            if (ship != null)
                ship.BindUpgradeConfig(config);
        }

        public bool IsOwned(ShipConfig config)
        {
            return config != null && owned.Contains(config);
        }

        // уровень линии конкретной модели, 0 = сток
        public int GetLevel(ShipConfig config, UpgradeLine line)
        {
            if (config == null || string.IsNullOrEmpty(config.Id))
                return 0;
            return upgradeLevels.TryGetValue(config.Id, out int[] levels)
                ? levels[(int)line]
                : 0;
        }

        // Покупка: только не-арендные (Price > 0) и ещё не купленные.
        // paintIndex — купленная покраска (витрина продаёт показанный вариант).
        // После оплаты — сразу пересадка на купленный: новый корпус = полные
        // HP и бак, применяется мгновенно (корабль стоит на станции)
        public bool TryBuy(ShipConfig config, Wallet wallet, int paintIndex = 0)
        {
            if (config == null || config.IsRental || config.Price <= 0 || IsOwned(config))
                return false;
            if (wallet == null || !wallet.TrySpend(config.Price))
                return false;

            owned.Add(config);
            SetCurrent(config, paintIndex);
            return true;
        }

        // Пересадка (станция): в любой СВОЙ корабль или обратно в аренду
        // (аренда всегда «в наличии» — своего продавать не нужно). Чужие и
        // непродаваемые модели — no-op. Корпус пересаживается сразу, новый
        // корпус = полные HP и бак (повреждения
        // не персистятся). Уже текущий — no-op без события.
        // покраска берётся из памяти по модели — купленный вариант
        // сохраняется при переключениях туда-обратно
        public void SwitchTo(ShipConfig config)
        {
            if (config == null || config == Current)
                return;
            if (!config.IsRental && !IsOwned(config))
                return;
            SetCurrent(config, RememberedPaint(config));
        }

        // Улучшение: только на купленный (аренда всегда сток), уровень < 3,
        // цена уровня — UpgradeConfig.GetPrice по классу корабля.
        // Пересадки нет, но живой корпус этого корабля сразу пересчитывает Eff
        // (полные HP/бак — семантика Reconfigure «нового корпуса»)
        public bool TryUpgrade(ShipConfig config, UpgradeLine line, Wallet wallet)
        {
            if (config == null || string.IsNullOrEmpty(config.Id)
                || !IsOwned(config) || upgradeConfig == null)
                return false;
            int[] levels = LevelsOf(config.Id);
            int idx = (int)line;
            if (idx < 0 || idx >= ShipStats.LineCount
                || levels[idx] >= ShipStats.MaxLevel)
                return false;
            int price = upgradeConfig.GetPrice((int)config.ServiceClass);
            if (price <= 0)
                return false;
            if (wallet == null || !wallet.TrySpend(price))
                return false;

            levels[idx]++;
            UpgradesChanged?.Invoke();
            if (config == Current && ship != null)
                ship.Reconfigure(config, levels, refill: false); // без лечения/заправки
            return true;
        }

        // Потеря корабля: свой — списывается вместе с улучшениями и покраской
        // (сброс уровней), арендный — просто остаёмся на аренде
        public void NotifyShipLost()
        {
            if (Current != null && !string.IsNullOrEmpty(Current.Id))
            {
                upgradeLevels.Remove(Current.Id); // улучшения сгорают вместе с корпусом
                paintByShipId.Remove(Current.Id); // покраска тоже
            }
            owned.Remove(Current);
            SetCurrent(ShipConfig.EggRentalFallback);
        }

        // восстановление парка из сейва: владение, уровни, текущая модель
        // и её покраска. Конфиги резолвит вызывающий (SaveSystem) из
        // каталога Resources/Ships; кривой текущий id — аренда (фолбэк), чужие
        // в списке владения пропускаются.
        // SetCurrent пересобирает Eff живого корпуса с полными HP/баком («пересадка»),
        // поэтому сохранённые HP/топливо сейва применяются ПОСЛЕ этого метода
        public void RestoreState(ShipConfig restoredCurrent, IReadOnlyList<ShipConfig> restoredOwned,
            IReadOnlyDictionary<string, int[]> restoredLevels, int restoredPaintIndex = 0)
        {
            owned.Clear();
            if (restoredOwned != null)
                foreach (ShipConfig config in restoredOwned)
                    if (config != null && !config.IsRental && !owned.Contains(config))
                        owned.Add(config);

            upgradeLevels.Clear();
            if (restoredLevels != null)
                foreach (KeyValuePair<string, int[]> pair in restoredLevels)
                    if (!string.IsNullOrEmpty(pair.Key))
                        upgradeLevels[pair.Key] = ShipStats.NormalizeLevels(pair.Value);

            if (restoredCurrent == null
                || (!restoredCurrent.IsRental && !owned.Contains(restoredCurrent)))
                restoredCurrent = ShipConfig.EggRentalFallback;
            SetCurrent(restoredCurrent, restoredPaintIndex);
        }

        // запись уровней создаётся лениво — нулевая (сток); без Id хранить некуда
        private int[] LevelsOf(string id)
        {
            if (string.IsNullOrEmpty(id))
                return ShipStats.NormalizeLevels(null);
            if (!upgradeLevels.TryGetValue(id, out int[] levels))
            {
                levels = ShipStats.NormalizeLevels(null);
                upgradeLevels[id] = levels;
            }
            return levels;
        }

        // Пересадка с покраской: кривой индекс (нет варианта/чужой
        // слот) — заводская. Reconfigure применяет спрайт со СТАРЫМ индексом,
        // SetPaint тут же ставит правильный — двойное присвоение спрайта
        // дешевле размазывания покраски по перегрузкам Reconfigure.
        // явный индекс (покупка/восстановление из сейва) запоминается
        // по модели — SwitchTo потом подставляет его же
        private void SetCurrent(ShipConfig config, int paintIndex = 0)
        {
            Current = config;
            paintIndex = ValidPaintIndex(config, paintIndex);
            if (!config.IsRental && !string.IsNullOrEmpty(config.Id))
                paintByShipId[config.Id] = paintIndex;
            int previousPaint = CurrentPaintIndex;
            CurrentPaintIndex = paintIndex;
            if (ship != null)
            {
                ship.Reconfigure(config, LevelsOf(config.Id));
                ship.SetPaint(paintIndex);
            }
            if (paintIndex != previousPaint)
                PaintChanged?.Invoke();
            ShipChanged?.Invoke();
        }

        // сохранённая покраска модели (0 — не покупали/заводская)
        private int RememberedPaint(ShipConfig config)
        {
            return config != null && !string.IsNullOrEmpty(config.Id)
                && paintByShipId.TryGetValue(config.Id, out int paint)
                    ? paint
                    : 0;
        }

        // валидный индекс покраски модели: 0, если вариант не существует
        private static int ValidPaintIndex(ShipConfig config, int paintIndex)
        {
            return config != null && paintIndex > 0 && config.GetPaintSprite(paintIndex) != null
                ? paintIndex
                : 0;
        }
    }
}
