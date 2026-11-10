using UnityEngine;

namespace SpaceTaxi.Economy
{
    // Линии улучшений; индекс совпадает с levels[] в FleetService и Ship
    public enum UpgradeLine
    {
        Engine = 0,
        Tank = 1,
        Hull = 2,
        Maneuver = 3,
    }

    // Цены и максимальные эффекты улучшений (16_BETA_PLAN §3.4, числа не менять).
    // Эффект растёт линейно по уровням: ур.1 = 1/3, ур.2 = 2/3, ур.3 = максимум —
    // интерполяция в ShipStats.Compute. Улучшения только на купленные корабли
    [CreateAssetMenu(fileName = "UpgradeConfig", menuName = "Space Taxi/Upgrade Config")]
    public sealed class UpgradeConfig : ScriptableObject
    {
        [Header("Цены за уровень (индекс = уровень - 1)")]
        public int[] PricesPerLevel = { 300, 700, 1500 };

        // По-линейные цены (D1/BALANCE_N4): 0 или пустой массив — фолбэк на общий
        // PricesPerLevel (старые ассеты). Индекс = уровень - 1
        [Header("Цены по линиям (индекс = уровень - 1; 0/пусто — общий PricesPerLevel)")]
        public int[] EnginePrices = { 10, 30, 40 };
        public int[] TankPrices = { 1110, 1930, 2580 };
        public int[] HullPrices = { 70, 150, 230 };
        public int[] ManeuverPrices = { 40, 40, 40 };

        [Header("Названия линий (для магазина П3.3)")]
        public string EngineName = "Двигатель";
        public string TankName = "Бак";
        public string HullName = "Корпус";
        public string ManeuverName = "Манёвровые";

        [Header("Максимальные эффекты (ур. 3)")]
        [Tooltip("Прибавка тяги, %")]
        public float EngineThrustPct = 25f;
        [Tooltip("Буст качели на ур. 3 (база — PhysicsConfig.GravityBoost)")]
        public float EngineBoostMax = 1.3f;
        [Tooltip("Прибавка ёмкости бака, %")]
        public float TankCapacityPct = 60f;
        [Tooltip("Снижение расхода топлива, %")]
        public float TankDrainRedPct = 15f;
        [Tooltip("Прибавка прочности корпуса, %")]
        public float HullHpPct = 60f;
        [Tooltip("Снижение урона от жара, %")]
        public float HullHeatRedPct = 30f;
        [Tooltip("Прибавка боковой тяги, %")]
        public float ManeuverStrafePct = 35f;

        // Цена уровня level (1..3) линии lineIndex (порядок UpgradeLine):
        // если массив линии задан ненулевым — его значение, иначе общий
        // PricesPerLevel[level - 1] (обратная совместимость); нет данных — 0
        public int GetPrice(int lineIndex, int level)
        {
            if (level < 1 || level > ShipStats.MaxLevel)
                return 0;
            int[] perLine = LinePrices(lineIndex);
            if (perLine != null && level <= perLine.Length && perLine[level - 1] > 0)
                return perLine[level - 1];
            int[] common = PricesPerLevel;
            return common != null && level <= common.Length && common[level - 1] > 0
                ? common[level - 1]
                : 0;
        }

        private int[] LinePrices(int lineIndex)
        {
            switch (lineIndex)
            {
                case (int)UpgradeLine.Engine: return EnginePrices;
                case (int)UpgradeLine.Tank: return TankPrices;
                case (int)UpgradeLine.Hull: return HullPrices;
                case (int)UpgradeLine.Maneuver: return ManeuverPrices;
                default: return null;
            }
        }

        private static UpgradeConfig fallback;

        // Фолбэк, если ассет не назначен в GameSession (паттерн EggRentalFallback):
        // поля-инициализаторы выше дают числа плана
        public static UpgradeConfig Default
        {
            get
            {
                if (fallback == null)
                {
                    fallback = CreateInstance<UpgradeConfig>();
                    fallback.name = "UpgradeConfig_Fallback";
                }
                return fallback;
            }
        }
    }
}
