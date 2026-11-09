using System;
using SpaceTaxi.World;

namespace SpaceTaxi.Economy
{
    // Фиксированная цена предложения на карточке заказа (16_BETA_PLAN §3.5; Н3.1 — премиальность тира)
    public static class OrderPricing
    {
        // цена = (база + min(дальность, кап) × ставка) × класс сектора планеты назначения
        //        × премиальность тира корабля-таксиста; кривой тир — кламп в геттере
        public static int ComputePrice(PricingConfig config, float routeDistance, Sector dropoffSector,
            int taxiTier = 1)
        {
            if (config == null)
                config = PricingConfig.Default;
            float effDistance = Math.Min(routeDistance, config.DistanceCap);
            float raw = (config.BasePrice + effDistance * config.PricePerDistanceUnit)
                        * config.GetSectorMultiplier(dropoffSector)
                        * config.GetTierPrestigeMul(taxiTier);
            return (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        }
    }
}
