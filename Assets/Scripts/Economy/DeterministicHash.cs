namespace SpaceTaxi.Economy
{
    // FNV-1a: строковый GetHashCode рандомизирован между сессиями Unity —
    // детерминированный хэш «сухих» станций/витрины — общая копия для
    // FuelNetwork и ShopStockService
    public static class DeterministicHash
    {
        public static int StableHash(string id)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < id.Length; i++)
                {
                    hash ^= id[i];
                    hash *= 16777619;
                }
                return (int)hash;
            }
        }
    }
}
