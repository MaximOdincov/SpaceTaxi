using SpaceTaxi.World;

namespace SpaceTaxi.Economy
{
    // Множитель цены заказа по сектору планеты назначения
    public static class SectorPricing
    {
        public static float GetPriceMultiplier(Sector sector)
        {
            switch (sector)
            {
                case Sector.Mid1: return 1.3f;
                case Sector.Mid2: return 1.6f;
                case Sector.Far: return 2.0f;
                default: return 1.0f; // Hub и Near — базовая цена
            }
        }
    }
}
