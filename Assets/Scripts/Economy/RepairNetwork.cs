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

        // ceil(HP × множитель пакета): базовая цена 1 кр/HP, недостающая доля ед. тоже платная
        public static int CostOf(float repairHp, int packIndex, PricingConfig pricing)
        {
            if (repairHp <= 0f)
                return 0;
            float mul = pricing != null ? pricing.GetRepairPackPriceMul(packIndex) : 1f;
            return Mathf.CeilToInt(repairHp * mul);
        }
    }
}
