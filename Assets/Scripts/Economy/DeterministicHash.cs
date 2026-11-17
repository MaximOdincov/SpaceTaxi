namespace SpaceTaxi.Economy
{
    // FNV-1a: строковый GetHashCode рандомизирован между сессиями Unity —
    // детерминированный хэш «сухих» станций/витрины (Н10.11: одна копия вместо
    // дубликата в FuelNetwork и ShopStockService, CODE_REVIEW_N9 §3.5)
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
