using System;
using SpaceTaxi.World;

namespace SpaceTaxi.Economy
{
    // Фиксированная цена предложения на карточке заказа
    // (тарифные классы как в жизни)
    public static class OrderPricing
    {
        // цена = (посадка_класса + ставка_класса × d_смягч) × множитель сектора
        // планеты назначения; d_смягч = min(d, SoftCap) + TailFactor × max(0, d − SoftCap),
        // класс = ServiceClass корабля-таксиста (кривой — кламп в геттерах)
        public static int ComputePrice(PricingConfig config, float routeDistance, Sector dropoffSector,
            int serviceClass = 0)
        {
            if (config == null)
                config = PricingConfig.Default;
            float softCap = Math.Max(0f, config.SoftCapDistance);
            float tail = routeDistance > softCap
                ? softCap + config.SoftCapTailFactor * (routeDistance - softCap)
                : routeDistance;
            float raw = (config.GetClassBaseFare(serviceClass) + config.GetClassRatePerUnit(serviceClass) * tail)
                        * config.GetSectorMultiplier(dropoffSector);
            return (int)Math.Round(raw, MidpointRounding.AwayFromZero);
        }
    }
}
