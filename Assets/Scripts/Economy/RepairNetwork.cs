using UnityEngine;

namespace SpaceTaxi.Economy
{
    // Сеть ремонта станции (Н-5): тир планеты задаёт максимальный пакет и потолок
    // визита (PricingConfig). Пакет — доля от НУЖНОГО (недостающего) HP, зажатая
    // потолком сети: бедная станция может починить меньше, чем просит пакет, —
    // эффективные проценты и цена считаются заранее и видны на карточках.
    public static class RepairNetwork
    {
        public const int PackCount = 3; // мелкий / средний / полный

        // максимальный индекс пакета, который станция тира вообще берётся делать
        public static int MaxPackIndex(int tier)
        {
            int t = tier <= 0 ? PricingConfig.BaseRepairTier : tier;
            return Mathf.Clamp(t, 1, PackCount) - 1;
        }

        // --- Круг тиров кораблей, который берёт мастерская (С4): бедная (T1)
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

        // эффективная доля пакета: пакет не может превысить потолок сети за визит
        public static float EffectiveFraction(int packIndex, int tier, PricingConfig pricing)
        {
            float pack = pricing != null ? pricing.GetRepairPackFraction(packIndex) : 1f;
            float cap = pricing != null ? pricing.GetRepairTierCap(tier) : 1f;
            return Mathf.Min(Mathf.Max(0f, pack), Mathf.Max(0f, cap));
        }

        // сколько HP восстановит пакет: пакет от ТЕКУЩЕГО недостающего, зажатый
        // остатком бюджета визита (кап × недостающее на момент посадки) — иначе
        // повторные мелкие пакеты от нового недостающего собирают 100% на любом тире
        public static float RepairAmount(float hpMax, float hp, int packIndex, int tier,
            PricingConfig pricing, float visitBudget)
        {
            float pack = MissingHp(hpMax, hp) * EffectiveFraction(packIndex, tier, pricing);
            return Mathf.Min(pack, Mathf.Max(0f, visitBudget));
        }

        // ceil(HP × множитель пакета × наценка тира сети): базовая цена 1 кр/HP,
        // недостающая доля ед. тоже платная; лучшие мастерские дороже (С4,
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
