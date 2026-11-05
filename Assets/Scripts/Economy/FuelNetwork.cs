using UnityEngine;

namespace SpaceTaxi.Economy
{
    // Топливная сеть станции (21 §3.7): тир планеты задаёт цену единицы и запас
    // визита (PricingConfig). Запас — детерминированный уровень заполнения: сеть
    // может довести бак только до floor(FuelMax × доля тира), платишь ровно за
    // взятое. Бак полнее уровня запаса — сети нечего продать.
    public static class FuelNetwork
    {
        // уровень (в единицах топлива), до которого сеть способна заправить за визит
        public static float StockLevel(float fuelMax, int tier, PricingConfig pricing)
        {
            float fraction = pricing != null ? pricing.GetFuelStockFraction(tier) : 1f;
            return Mathf.Floor(Mathf.Max(0f, fuelMax) * fraction);
        }

        // сколько целых единиц можно купить при текущем топливе: запас сверх уже
        // имеющегося; дробная часть имеющегося не продаётся (ceil — в запас)
        public static int AvailableUnits(float fuelMax, float fuel, int tier, PricingConfig pricing)
        {
            int stock = Mathf.FloorToInt(StockLevel(fuelMax, tier, pricing));
            return Mathf.Max(0, stock - Mathf.CeilToInt(fuel));
        }

        // стоимость units единиц по цене сети: ceil(единицы × цена/ед)
        public static int CostOf(int units, int tier, PricingConfig pricing)
        {
            if (units <= 0)
                return 0;
            float perUnit = pricing != null ? pricing.GetFuelPricePerUnit(tier) : 1f;
            return Mathf.CeilToInt(units * perUnit);
        }
    }
}
