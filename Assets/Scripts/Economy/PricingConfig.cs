using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Economy
{
    // Числа модели цены заказа (16_BETA_PLAN §3.5; Н3.1 добавил премиальность тира):
    // цена = (база + min(дальность, кап) × ставка) × класс сектора × премиальность тира таксиста
    [CreateAssetMenu(fileName = "PricingConfig", menuName = "Space Taxi/Pricing Config")]
    public sealed class PricingConfig : ScriptableObject
    {
        [Header("Формула цены предложения")]
        public int BasePrice = 45;
        public float PricePerDistanceUnit = 0.28f;
        [Tooltip("Кап дальности: заказ «через всю карту» не делает богатым")]
        public float DistanceCap = 1500f;

        [Header("Премиальность тира таксиста (Н3.1): индекс = тир корабля − 1")]
        [Tooltip("Множитель оплаты заказа: переход на следующий тир окупается за 6-10 заказов")]
        public float[] TierPrestigeMul = { 1f, 1.26f, 1.52f, 2.26f, 4.28f };

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
        public int RentalCommissionPct = 35;
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

        [Header("Пакеты ремонта (Н-5): индекс = пакет − 1 (мелкий/средний/полный)")]
        [Tooltip("Доля недостающего HP, которую восстанавливает пакет")]
        public float[] RepairPackFraction = { 0.25f, 0.6f, 1f };
        [Tooltip("Множитель базовой цены 1 кр/HP для пакета")]
        public float[] RepairPackPriceMul = { 0.8f, 1f, 1.2f };

        [Header("Сеть ремонта (Н-5): индекс = тир сети − 1")]
        [Tooltip("Потолок ремонта за визит, доля НУЖНОГО (недостающего) HP: бедная станция может взять не весь пакет")]
        public float[] RepairTierCapByTier = { 0.3f, 0.7f, 1f };

        // базовая сеть: планеты без проставленного FuelTier и старые ассеты карт
        public const int BaseFuelTier = 2;
        // незаданный тир ремонта — мастерская чинит полностью (как до Н-5)
        public const int BaseRepairTier = 3;

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

        // множитель премиальности по тиру корабля-таксиста; кривой тир/пустой
        // массив — кламп в 1..5 (короткий массив ассета — верхний кламп)
        public float GetTierPrestigeMul(int tier)
        {
            var muls = TierPrestigeMul;
            if (muls == null || muls.Length == 0)
                return 1f;
            return muls[Mathf.Clamp(tier, 1, muls.Length) - 1];
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
        private static int TierIndex(int tier, int length, int baseTier)
        {
            if (length <= 0)
                return -1;
            if (tier <= 0)
                tier = baseTier;
            return Mathf.Clamp(tier, 1, length) - 1;
        }

        private static int FuelTierIndex(int tier, int length) => TierIndex(tier, length, BaseFuelTier);

        private static int RepairTierIndex(int tier, int length) => TierIndex(tier, length, BaseRepairTier);

        // значение по индексу с фолбэком — для битых/коротких массивов в ассете
        private static float ArrayAt(float[] array, int index, float fallback)
        {
            return array != null && index >= 0 && index < array.Length ? array[index] : fallback;
        }

        // доля недостающего HP, которую берёт пакет (0..PackCount-1)
        public float GetRepairPackFraction(int packIndex)
        {
            return ArrayAt(RepairPackFraction, packIndex, 1f);
        }

        // множитель базовой цены 1 кр/HP для пакета
        public float GetRepairPackPriceMul(int packIndex)
        {
            return ArrayAt(RepairPackPriceMul, packIndex, 1f);
        }

        // потолок ремонта за визит, доля нужного HP; тир 0/некорректный — базовая сеть (полный)
        public float GetRepairTierCap(int tier)
        {
            var caps = RepairTierCapByTier;
            int index = RepairTierIndex(tier, caps != null ? caps.Length : 0);
            return index >= 0 ? caps[index] : 1f;
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
