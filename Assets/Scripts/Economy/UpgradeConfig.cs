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

    // Цены и максимальные эффекты улучшений (цены уровней —
    // по классу корабля). Эффект растёт линейно по
    // уровням: ур.1 = 1/3, ур.2 = 2/3, ур.3 = максимум —
    // интерполяция в ShipStats.Compute. Улучшения только на купленные корабли
    [CreateAssetMenu(fileName = "UpgradeConfig", menuName = "Space Taxi/Upgrade Config")]
    public sealed class UpgradeConfig : ScriptableObject
    {
        [Header("Цены уровней по классу корабля: индекс = ServiceClass (0..3)")]
        [Tooltip("Цена ЛЮБОГО уровня = значение по классу корабля (эконом/комфорт/комфорт+/бизнес) — уровень накапливается за один заказ своего класса")]
        public int[] ClassLevelPrices = { 250, 450, 800, 1500 };

        [Header("Названия линий (для магазина)")]
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

        // Цена ЛЮБОГО уровня (1..3) ЛЮБОЙ линии — по классу корабля:
        // цена не зависит от линии и номера уровня. Кривой класс/пустой
        // массив — кламп в 0..3 (короткий массив ассета — верхний кламп);
        // нет данных — 0 (кнопка недоступна, покупка не проходит)
        public int GetPrice(int serviceClass)
        {
            var prices = ClassLevelPrices;
            if (prices == null || prices.Length == 0)
                return 0;
            return prices[Mathf.Clamp(serviceClass, 0, prices.Length - 1)];
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
