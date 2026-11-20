using UnityEngine;

namespace SpaceTaxi.Economy
{
    // Сеть ремонта станции: тир планеты задаёт доступные пакеты
    // (PricingConfig). Пакет — доля от НУЖНОГО (недостающего) HP; эффективные
    // проценты и цена считаются заранее и видны на карточках. Ремонт
    // ограничен ТОЛЬКО деньгами, чиним сколько просит пакет.
    public static class RepairNetwork
    {
        public const int PackCount = 3; // мелкий / средний / полный


        // --- Круг тиров кораблей, который берёт мастерская: бедная (T1)
        // обслуживает корабли тиров 1–2, средняя (T2) — 1–3, лучшие (T3+) —
        // 1–5 (всё): лестница «чем богаче мастерская, тем шире круг», иначе
        // хаб «Планета МТО» (T3, точка респавна) отказывал бы арендному Яйцу.
        // Корабль вне круга не чинится вовсе ---

        // минимальный тир корабля, который возьмёт мастерская (для отказа «ниже тира N»)
        public static int MinShipTier(int shopTier)
        {
            return 1; // T1 чинит с первого тира, T2 — с 1-3, T3+ — всё (см. шапку)
        }

        // максимальный тир корабля, который возьмёт мастерская
        public static int MaxShipTier(int shopTier)
        {
            int t = shopTier <= 0 ? PricingConfig.BaseRepairTier : shopTier;
            return t <= 1 ? 2 : t == 2 ? 3 : 5;
        }

        // берёт ли мастерская этого тира корабль этого тира
        public static bool ShipTierAccepted(int shopTier, int shipTier)
        {
            return shipTier >= MinShipTier(shopTier) && shipTier <= MaxShipTier(shopTier);
        }

        // сколько HP недостаёт до полного
        public static float MissingHp(float hpMax, float hp)
        {
            return Mathf.Max(0f, hpMax - hp);
        }

        // эффективная доля пакета: доля пакета от недостающего HP
        // (ремонт ограничен только деньгами)
        public static float EffectiveFraction(int packIndex, int tier, PricingConfig pricing)
        {
            float pack = pricing != null ? pricing.GetRepairPackFraction(packIndex) : 1f;
            return Mathf.Max(0f, pack);
        }

        // сколько HP восстановит пакет: доля от ТЕКУЩЕГО недостающего
        public static float RepairAmount(float hpMax, float hp, int packIndex, int tier,
            PricingConfig pricing)
        {
            return MissingHp(hpMax, hp) * EffectiveFraction(packIndex, tier, pricing);
        }

        // ceil(HP × множитель пакета × наценка тира сети): базовая цена 1 Đ/HP,
        // недостающая доля ед. тоже платная; лучшие мастерские дороже (
        // RepairTierSurchargeMul) — базовая формула не тронута, умножение сверху
        public static int CostOf(float repairHp, int packIndex, int tier, PricingConfig pricing)
        {
            if (repairHp <= 0f)
                return 0;
            float mul = pricing != null ? pricing.GetRepairPackPriceMul(packIndex) : 1f;
            float surcharge = pricing != null ? pricing.GetRepairTierSurcharge(tier) : 1f;
            return Mathf.CeilToInt(repairHp * mul * surcharge);
        }
    }
}
