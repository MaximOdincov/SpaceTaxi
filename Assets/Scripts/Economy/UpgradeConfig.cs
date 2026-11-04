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
