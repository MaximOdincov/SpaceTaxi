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

        [Header("Чаевые (§3.5, П8.2)")]
        [Tooltip("Множитель чаевых на «Меридиане» — перк «комфорт-салон» (16 §3.3)")]
        public float MeridianTipsBonusMul = 1.1f;

        [Header("Топливные сети (21 §3.7): индекс = тир сети − 1")]
        [Tooltip("Цена кр/ед по тиру сети: 1 — дёшево и мало, 4 — люкс (полный бак, оверпрайс)")]
        public float[] FuelPricePerUnitByTier = { 0.8f, 1f, 1.3f, 1.8f };
        [Tooltip("Запас топлива сети за один визит, доля бака корабля")]
        public float[] FuelStockFractionByTier = { 0.35f, 0.7f, 1f, 1f };

        // базовая сеть: планеты без проставленного FuelTier и старые ассеты карт
        public const int BaseFuelTier = 2;

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

        // цена единицы топлива сети; тир 0/некорректный → базовая сеть (1 кр/ед)
        public float GetFuelPricePerUnit(int tier)
        {
            var prices = FuelPricePerUnitByTier;
            int index = FuelTierIndex(tier, prices != null ? prices.Length : 0);
            return index >= 0 ? prices[index] : 1f;
        }

        // запас сети за визит, доля бака; тир 0/некорректный → базовая сеть
        public float GetFuelStockFraction(int tier)
        {
            var fractions = FuelStockFractionByTier;
            int index = FuelTierIndex(tier, fractions != null ? fractions.Length : 0);
            return index >= 0 ? fractions[index] : 1f;
        }

        // индекс массива 0..n-1: незаданный тир считаем базовым, верхний кламп — на короткий массив
        private static int FuelTierIndex(int tier, int length)
        {
            if (length <= 0)
                return -1;
            if (tier <= 0)
                tier = BaseFuelTier;
            return Mathf.Clamp(tier, 1, length) - 1;
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
