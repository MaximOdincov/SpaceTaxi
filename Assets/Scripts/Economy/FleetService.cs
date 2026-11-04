using System;
using System.Collections.Generic;
using SpaceTaxi.Physics;

namespace SpaceTaxi.Economy
{
    // Парк кораблей: владение купленными моделями, аренда «Яйца» как фолбэк,
    // уровни улучшений купленных кораблей (аренда всегда сток).
    // Владелец — GameSession (создаётся в Awake); UI магазина подключается в П3.2,
    // долг за потерю аренды — в П2.2
    public sealed class FleetService
    {
        private readonly List<ShipConfig> owned = new List<ShipConfig>();
        // уровни улучшений по ShipConfig.Id: [Engine, Tank, Hull, Maneuver], 0..3
        private readonly Dictionary<string, int[]> upgradeLevels = new Dictionary<string, int[]>();
        private Ship ship; // живой корабль для мгновенной пересадки
        private UpgradeConfig upgradeConfig;

        // всегда валиден: старт новой игры — арендное «Яйцо»
        public ShipConfig Current { get; private set; }

        public IReadOnlyList<ShipConfig> OwnedShips => owned;

        // конфиг улучшений — для UI магазина и проверки цен
        public UpgradeConfig Upgrades => upgradeConfig;

        // смена текущего корабля — для UI (П3+)
        public event Action ShipChanged;

        // уровни улучшений изменились — для UI (П3.3)
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
        // После оплаты — сразу пересадка на купленный: новый корпус = полные
        // HP и бак, применяется мгновенно (корабль стоит на станции)
        public bool TryBuy(ShipConfig config, Wallet wallet)
        {
            if (config == null || config.IsRental || config.Price <= 0 || IsOwned(config))
                return false;
            if (wallet == null || !wallet.TrySpend(config.Price))
                return false;

            owned.Add(config);
            SetCurrent(config);
            return true;
        }

        // Улучшение: только на купленный (аренда всегда сток), уровень < 3,
        // цена следующего уровня — PricesPerLevel[текущий]. Пересадки нет, но
        // живой корпус этого корабля сразу пересчитывает Eff (полные HP/бак —
        // семантика Reconfigure «нового корпуса»)
        public bool TryUpgrade(ShipConfig config, UpgradeLine line, Wallet wallet)
        {
            if (config == null || string.IsNullOrEmpty(config.Id)
                || !IsOwned(config) || upgradeConfig == null)
                return false;
            int[] prices = upgradeConfig.PricesPerLevel;
            int[] levels = LevelsOf(config.Id);
            int idx = (int)line;
            if (idx < 0 || idx >= ShipStats.LineCount
                || levels[idx] >= ShipStats.MaxLevel
                || prices == null || levels[idx] >= prices.Length)
                return false;
            if (wallet == null || !wallet.TrySpend(prices[levels[idx]]))
                return false;

            levels[idx]++;
            UpgradesChanged?.Invoke();
            if (config == Current && ship != null)
                ship.Reconfigure(config, levels);
            return true;
        }

        // Потеря корабля: свой — списывается вместе с улучшениями (сброс уровней),
        // арендный — просто остаёмся на аренде
        public void NotifyShipLost()
        {
            if (Current != null && !string.IsNullOrEmpty(Current.Id))
                upgradeLevels.Remove(Current.Id); // улучшения сгорают вместе с корпусом
            owned.Remove(Current);
            SetCurrent(ShipConfig.EggRentalFallback);
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

        private void SetCurrent(ShipConfig config)
        {
            Current = config;
            if (ship != null)
                ship.Reconfigure(config, LevelsOf(config.Id));
            ShipChanged?.Invoke();
        }
    }
}
