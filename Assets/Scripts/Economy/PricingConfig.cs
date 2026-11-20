using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Economy
{
    // Числа модели цены заказа (тарифные классы):
    // цена = (посадка_класса + ставка_класса × d_смягч) × множитель сектора назначения;
    // d_смягч = min(d, SoftCapDistance) + SoftCapTailFactor × max(0, d − SoftCapDistance)
    [CreateAssetMenu(fileName = "PricingConfig", menuName = "Space Taxi/Pricing Config")]
    public sealed class PricingConfig : ScriptableObject
    {
        [Header("Тарифы классов: индекс = ServiceClass корабля (0..3)")]
        [Tooltip("Посадка по классам эконом/комфорт/комфорт+/бизнес, кр")]
        public float[] ClassBaseFare = { 15f, 30f, 55f, 90f };
        [Tooltip("Ставка кр/юн по классам эконом/комфорт/комфорт+/бизнес")]
        public float[] ClassRatePerUnit = { 0.16f, 0.24f, 0.36f, 0.54f };
        [Tooltip("Смягчение дальности: дальше капа хвост считается с множителем SoftCapTailFactor")]
        public float SoftCapDistance = 2800f;
        [Tooltip("Множитель хвоста дальности за SoftCapDistance (0 — жёсткий кап)")]
        public float SoftCapTailFactor = 0.4f;

        [Header("Класс сектора планеты назначения")]
        public float SectorHubMul = 1.0f;
        public float SectorNearMul = 1.15f;
        public float SectorMid1Mul = 1.35f;
        public float SectorMid2Mul = 1.6f;
        public float SectorFarMul = 2.0f;

        [Header("Выплата: штраф, комиссия аренды, доля в долг")]
        [Tooltip("% от цены за одно столкновение (урон от контакта; жар не считается)")]
        public int PenaltyPerCollisionPct = 15;
        [Tooltip("Потолок суммарного штрафа, % от цены")]
        public int PenaltyCapPct = 45;
        [Tooltip("% от (цена − штраф), только на арендном корабле")]
        public int RentalCommissionPct = 35;
        [Tooltip("% выплаты, автоматически уходящий в счёт долга (пока долг > 0)")]
        public int DebtRepaySharePct = 50;

        [Header("Штрафы происшествий и увольнение: списание — кошелёк, недостача — в долг")]
        [Tooltip("Пассажир погиб или оставлен вместе с кораблём (гибель корпуса/отключение с пассажиром на борту); платится сверх долга за аренду")]
        public int PassengerLossPenalty = 300;
        [Tooltip("Долг свыше этой суммы — увольнение (экран «УВОЛЕН», забег сбрасывается)")]
        public int DebtDismissalThreshold = 2000;

        [Header("Эффекты рейтинга")]
        [Tooltip("Рейтинг ≥ порога — наценка к цене предложения")]
        public float RatingPriceBonusThreshold = 4.0f;
        [Tooltip("Множитель цены предложения при рейтинге ≥ порога")]
        public float RatingPriceBonusMul = 1.1f;
        [Tooltip("Рейтинг ниже порога — карточки заказов появляются реже")]
        public float LowRatingSpawnThreshold = 2.0f;
        [Tooltip("Множитель задержек спавна карточек при рейтинге < порога")]
        public float LowRatingSpawnSlowdown = 1.3f;

        [Header("Чаевые")]
        [Tooltip("Множитель чаевых на «Меридиане» — перк «комфорт-салон»")]
        public float MeridianTipsBonusMul = 1.1f;

        [Header("Топливные сети: индекс = тир сети − 1")]
        [Tooltip("Цена Đ/ед по тиру сети: 1 — дёшево и меньше всех, 4 — люкс (полный бак, оверпрайс)")]
        public float[] FuelPricePerUnitByTier = { 0.6f, 0.8f, 1f, 1.3f };
        [Tooltip("Запас топлива сети за один визит, доля бака корабля: бедная сеть продаёт меньше всех, но не ноль")]
        public float[] FuelStockFractionByTier = { 0.75f, 1f, 1f, 1f };

        [Header("Топливо и районы: индекс = Sector (Hub..Far)")]
        [Tooltip("Множитель цены топлива по классу района: богатый центр — дешевле, бедные окраины — дороже")]
        public float[] FuelPriceSectorMul = { 0.9f, 1f, 1.1f, 1.25f, 1.45f };
        [Tooltip("Шанс пустой сети на заправке, % (редкое исключение: детерминирован именем станции, хаб не пустеет, у двух соседних топлива не бывает одновременно)")]
        [Range(0, 100)] public int FuelOutageChancePct = 12;

        [Header("Пакеты ремонта: индекс = пакет − 1 (мелкий/средний/полный)")]
        [Tooltip("Доля недостающего HP, которую восстанавливает пакет")]
        public float[] RepairPackFraction = { 0.25f, 0.6f, 1f };
        [Tooltip("Множитель базовой цены 1 Đ/HP для пакета")]
        public float[] RepairPackPriceMul = { 0.8f, 1f, 1.2f };

        [Header("Сеть ремонта: индекс = тир сети − 1")]
        [Tooltip("Потолок ремонта за визит, доля НУЖНОГО (недостающего) HP: бедная станция может взять не весь пакет")]
        public float[] RepairTierCapByTier = { 0.3f, 0.7f, 1f };

        [Header("Наценка мастерских (С4): индекс = тир сети − 1")]
        [Tooltip("Множитель базовой цены ремонта: бедные мастерские дешевле, лучшие берут дороже (наценка выше в лучших мастерских)")]
        public float[] RepairTierSurchargeMul = { 0.9f, 1f, 1.25f };

        [Header("Престиж-фильтр заказов: индекс = Sector (Hub..Far)")]
        [Tooltip("Класс пассажиров сектора: ХАБ/БЛИЖНИЙ — эконом, МИД-1 — комфорт, МИД-2 — комфорт+, ДАЛЬНИЙ — бизнес; кораблю ниже класса дропофф в секторе не виден (кроме нетребовательных)")]
        public int[] SectorRequiredClass = { 0, 0, 1, 2, 3 };
        [Tooltip("Доля нетребовательных пассажиров, %: согласны на класс ниже — дропофф без гейта")]
        [Range(0f, 100f)] public float LenientPassengerChancePct = 20f;

        [Header("Близость пикапа (D2)")]
        [Tooltip("Радиус «рядом с игроком», юн: ближе вес пикапа максимален, дальше линейно спадает к нулю")]
        public float PickupProximityRange = 1400f;
        [Tooltip("Доля «ближних» офферов при рейтинге 0 (остаток равномерно по всем планетам пула)")]
        [Range(0f, 1f)] public float PickupNearShareAtMinRating = 0.9f;
        [Tooltip("Доля «ближних» офферов при рейтинге 5 — с ростом рейтинга дальние пикапы встречаются чаще")]
        [Range(0f, 1f)] public float PickupNearShareAtMaxRating = 0.3f;

        [Header("«Жирные» заказы (D2): редкие дальние с заданием и наценкой")]
        [Tooltip("Базовый шанс жирного заказа, % (рейтинг 0)")]
        public float FatOrderBaseChancePct = 3f;
        [Tooltip("Прибавка шанса жирного заказа за 1 пункт рейтинга, %")]
        public float FatOrderChancePerRatingPct = 2f;
        [Tooltip("Потолок шанса жирного заказа, %")]
        public float FatOrderMaxChancePct = 20f;
        [Tooltip("Множитель цены жирного заказа (сверх прайса и районного множителя)")]
        public float FatOrderPriceMul = 1.5f;
        [Tooltip("Минимальная дальность маршрута жирного заказа, юн (дальних пар нет — берётся самая дальняя планета)")]
        public float FatOrderMinRouteDistance = 1200f;
        [Tooltip("Шанс, % что жирный заказ пренебрегает гейтом районов — «вкус» закрытых секторов; отказаться можно бесплатно")]
        [Range(0f, 100f)] public float FatOrderLockedSectorChancePct = 25f;

        // базовая сеть: планеты без проставленного FuelTier и старые ассеты карт
        public const int BaseFuelTier = 2;
        // незаданный тир ремонта — мастерская чинит полностью
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

        // посадка по классу обслуживания; кривой индекс/пустой массив — кламп
        // в 0..3 (короткий массив ассета — верхний кламп)
        public float GetClassBaseFare(int serviceClass)
        {
            var fares = ClassBaseFare;
            if (fares == null || fares.Length == 0)
                return 0f;
            return fares[Mathf.Clamp(serviceClass, 0, fares.Length - 1)];
        }

        // ставка кр/юн по классу обслуживания; кламп как в GetClassBaseFare
        public float GetClassRatePerUnit(int serviceClass)
        {
            var rates = ClassRatePerUnit;
            if (rates == null || rates.Length == 0)
                return 0f;
            return rates[Mathf.Clamp(serviceClass, 0, rates.Length - 1)];
        }

        // цена единицы топлива сети; тир 0/некорректный → базовая сеть (1 Đ/ед)
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

        // районный множитель цены топлива: кривой/пустой массив — без наценки;
        // незаданный сектор не бывает (enum), верхний кламп — на короткий массив
        public float GetFuelSectorPriceMul(Sector sector)
        {
            var muls = FuelPriceSectorMul;
            if (muls == null || muls.Length == 0)
                return 1f;
            return muls[Mathf.Clamp((int)sector, 0, muls.Length - 1)];
        }

        // класс пассажиров сектора; кривой/пустой массив — эконом
        // (гейт не закрывает спавн наглухо, сектор открывается всем)
        public int GetSectorRequiredClass(Sector sector)
        {
            var classes = SectorRequiredClass;
            if (classes == null || classes.Length == 0)
                return 0;
            return classes[Mathf.Clamp((int)sector, 0, classes.Length - 1)];
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

        // множитель базовой цены 1 Đ/HP для пакета
        public float GetRepairPackPriceMul(int packIndex)
        {
            return ArrayAt(RepairPackPriceMul, packIndex, 1f);
        }

        // наценка мастерской по тиру сети; кривой/пустой массив — без наценки,
        // тир 0/некорректный — базовая сеть (средняя)
        public float GetRepairTierSurcharge(int tier)
        {
            var muls = RepairTierSurchargeMul;
            int index = RepairTierIndex(tier, muls != null ? muls.Length : 0);
            return index >= 0 ? muls[index] : 1f;
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
