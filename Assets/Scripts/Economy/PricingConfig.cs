using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Economy
{
    // Числа модели цены заказа (16_BETA_PLAN §3.5, не менять):
    // цена = (база + min(дальность, кап) × ставка) × класс сектора планеты назначения
    [CreateAssetMenu(fileName = "PricingConfig", menuName = "Space Taxi/Pricing Config")]
    public sealed class PricingConfig : ScriptableObject
    {
        [Header("Формула цены предложения")]
        public int BasePrice = 40;
        public float PricePerDistanceUnit = 0.25f;
        [Tooltip("Кап дальности: заказ «через всю карту» не делает богатым")]
        public float DistanceCap = 1500f;

        [Header("Класс сектора планеты назначения")]
        public float SectorHubMul = 1.0f;
        public float SectorNearMul = 1.0f;
        public float SectorMid1Mul = 1.3f;
        public float SectorMid2Mul = 1.6f;
        public float SectorFarMul = 2.0f;

        [Header("Выплата (§3.5): штраф, комиссия аренды, доля в долг")]
        [Tooltip("% от цены за одно столкновение (урон от контакта; жар не считается)")]
        public int PenaltyPerCollisionPct = 15;
        [Tooltip("Потолок суммарного штрафа, % от цены")]
        public int PenaltyCapPct = 45;
        [Tooltip("% от (цена − штраф), только на арендном корабле")]
        public int RentalCommissionPct = 25;
        [Tooltip("% выплаты, автоматически уходящий в счёт долга (пока долг > 0)")]
        public int DebtRepaySharePct = 50;

        [Header("Эффекты рейтинга (§3.7)")]
        [Tooltip("Рейтинг ≥ порога — наценка к цене предложения")]
        public float RatingPriceBonusThreshold = 4.0f;
        [Tooltip("Множитель цены предложения при рейтинге ≥ порога")]
        public float RatingPriceBonusMul = 1.1f;
        [Tooltip("Рейтинг ниже порога — карточки заказов появляются реже")]
        public float LowRatingSpawnThreshold = 2.0f;
        [Tooltip("Множитель задержек спавна карточек при рейтинге < порога")]
        public float LowRatingSpawnSlowdown = 1.3f;

        public float GetSectorMultiplier(Sector sector)
        {
            switch (sector)
            {
                case Sector.Hub: return SectorHubMul;
                case Sector.Near: return SectorNearMul;
                case Sector.Mid1: return SectorMid1Mul;
                case Sector.Mid2: return SectorMid2Mul;
                case Sector.Far: return SectorFarMul;
                default: return 1f;
            }
        }

        private static PricingConfig fallback;

        // Фолбэк с дефолтными числами, если ассет не назначен (паттерн ShipConfig.EggRentalFallback)
        public static PricingConfig Default
        {
            get
            {
                if (fallback == null)
                {
                    fallback = CreateInstance<PricingConfig>();
                    fallback.name = "PricingConfig (fallback)";
                }
                return fallback;
            }
        }
    }
}
