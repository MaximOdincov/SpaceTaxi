using System;
using SpaceTaxi.World;

namespace SpaceTaxi.Economy
{
    // Фиксированная цена предложения на карточке заказа (16_BETA_PLAN §3.5)
    public static class OrderPricing
    {
        // цена = (база + min(дальность, кап) × ставка) × класс сектора планеты назначения
        public static int ComputePrice(PricingConfig config, float routeDistance, Sector dropoffSector)
        {
            if (config == null)
                config = PricingConfig.Default;
            float effDistance = Math.Min(routeDistance, config.DistanceCap);
            float raw = (config.BasePrice + effDistance * config.PricePerDistanceUnit)
                        * config.GetSectorMultiplier(dropoffSector);
            return (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        }
    }
}
