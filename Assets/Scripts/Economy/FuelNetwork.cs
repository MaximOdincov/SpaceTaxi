using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Economy
{
    // Топливная сеть станции (21 §3.7; Н4.5 — наличие и районные цены): тир
    // планеты задаёт цену единицы и запас визита (PricingConfig), класс района
    // (Sector) — множитель цены: богатый центр дешевле, бедные окраины дороже.
    // Запас — детерминированный уровень заполнения: сеть может довести бак
    // только до floor(FuelMax × доля тира), платишь ровно за взятое. Бак полнее
    // уровня запаса — сети нечего продать.
    // Наличие (Н4.5): топливо есть почти на всех заправках; пустая сеть — редкое
    // исключение, детерминировано именем станции (FNV-1a, как в ShopStockService):
    // не меняется между визитами и одинаково при перезапуске — маршрут можно
    // планировать по карте. Хаб не пустеет никогда (страховка от софтлока на
    // точке респавна), а у двух СОСЕДНИХ заправок топлива не бывает одновременно —
    // иначе на маршруте возникает «сухая полоса». Графа связей районов в ассете
    // уровня нет, поэтому соседи = ближайшие по позиции заправки уровня
    // (NeighborCount штук); правило обеспечивает OutageStations по всем телам.
    public static class FuelNetwork
    {
        // сколько ближайших заправок считаем соседями одной станции (Н4.5)
        private const int NeighborCount = 2;
        // соль хэша (константа Кнута, как SlotSalt в ShopStockService): сдвигает
        // ведро Mod(хэш, 100) так, что исход не завязан на алфавит имён уровня
        private const uint OutageSalt = 0x9E3779B9u;

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

        // цена единицы топлива сети с районным множителем (Н4.5)
        public static float PricePerUnit(int tier, Sector sector, PricingConfig pricing)
        {
            float perUnit = pricing != null ? pricing.GetFuelPricePerUnit(tier) : 1f;
            return perUnit * (pricing != null ? pricing.GetFuelSectorPriceMul(sector) : 1f);
        }

        // стоимость units единиц по цене сети района: ceil(единицы × цена/ед)
        public static int CostOf(int units, int tier, Sector sector, PricingConfig pricing)
        {
            if (units <= 0)
                return 0;
            return Mathf.CeilToInt(units * PricePerUnit(tier, sector, pricing));
        }

        // имена станций (DisplayName) с пустой сетью на уровне: два прохода —
        // сначала «сырой» исход хэша каждой заправки, затем защита соседей: у
        // пары сухих соседей остаётся сухим только тот, что раньше в списке
        // уровня (детерминированный тай-брейк — порядок ассета стабилен). Хаб в
        // графе соседей участвует (защищает окружение), но сам не пустеет.
        public static HashSet<string> OutageStations(List<CelestialBodyData> bodies, PricingConfig pricing)
        {
            var dry = new HashSet<string>();
            if (bodies == null || pricing == null || pricing.FuelOutageChancePct <= 0)
                return dry;

            var names = new List<string>(bodies.Count);
            var positions = new List<Vector2>(bodies.Count);
            var rawDry = new List<bool>(bodies.Count);
            for (int i = 0; i < bodies.Count; i++)
            {
                var body = bodies[i];
                if (body == null || !body.HasFuel)
                    continue;
                names.Add(body.DisplayName);
                positions.Add(body.Position);
                rawDry.Add(!body.IsHub && IsRawOutage(body.DisplayName, pricing));
            }

            for (int i = 0; i < names.Count; i++)
            {
                if (rawDry[i] && !HasEarlierDryNeighbor(i, names.Count, positions, rawDry))
                    dry.Add(names[i]);
            }
            return dry;
        }

        // есть ли среди соседей станции уже «сырая» сухая с меньшим индексом —
        // тогда эта остаётся с топливом (пара не пустеет вместе)
        private static bool HasEarlierDryNeighbor(int index, int count, List<Vector2> positions, List<bool> rawDry)
        {
            for (int j = 0; j < count; j++)
            {
                if (j < index && rawDry[j] && IsNeighbor(index, j, count, positions))
                    return true;
            }
            return false;
        }

        // j — сосед i по симметричному замыканию «ближайших»: j входит в
        // NeighborCount ближайших к i ИЛИ i — в NeighborCount ближайших к j.
        // Симметрия обязательна: правило пары ниже опирается на то, что при
        // dry-паре (ранний i, поздний j) поздний гарантированно увидит раннего.
        // Равные дистанции — по порядку списка (строгое «меньше»)
        private static bool IsNeighbor(int index, int other, int count, List<Vector2> positions)
        {
            return AmongNearest(index, other, count, positions)
                || AmongNearest(other, index, count, positions);
        }

        // other входит в NeighborCount ближайших к index
        private static bool AmongNearest(int index, int other, int count, List<Vector2> positions)
        {
            float dist = Vector2.Distance(positions[index], positions[other]);
            int closer = 0;
            for (int k = 0; k < count; k++)
            {
                if (k == index || k == other)
                    continue;
                if (Vector2.Distance(positions[index], positions[k]) < dist)
                    closer++;
            }
            return closer < NeighborCount;
        }

        // «сырой» исход: хэш имени (с солью) попадает в шанс из конфига; мод —
        // по uint: бит-каст в int с отрицательным знаком сместил бы ведро
        private static bool IsRawOutage(string stationId, PricingConfig pricing)
        {
            int chance = Mathf.Clamp(pricing.FuelOutageChancePct, 0, 100);
            uint bucket = ((uint)StableHash(stationId) ^ OutageSalt) % 100u;
            return (int)bucket < chance;
        }

        private static int StableHash(string id)
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
