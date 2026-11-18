using UnityEngine;

namespace SpaceTaxi.Core
{
    // Пулы пользовательских спрайтов из Resources/Art (лениво, кэш в статике).
    // Планета и её цвет выбираются детерминированно по имени тела: облик держится
    // между сессиями и сейвами. Астероид — случайный из 9 на каждый спавн.
    public static class WorldSprites
    {
        private static Sprite[] planets;
        private static Sprite[] asteroids;
        private static readonly Sprite[] gas = new Sprite[5]; // 1..4 = poor/norm/rich/high

        // палитра тёплых/холодных тонов — умножается на серую базу спрайта планеты
        private static readonly Color[] PlanetTints =
        {
            new Color(0.94f, 0.66f, 0.50f), // терракота
            new Color(0.93f, 0.80f, 0.55f), // охра
            new Color(0.72f, 0.82f, 0.58f), // олива
            new Color(0.55f, 0.78f, 0.68f), // мята
            new Color(0.60f, 0.74f, 0.90f), // пыльно-голубой
            new Color(0.72f, 0.62f, 0.88f), // лаванда
            new Color(0.92f, 0.60f, 0.72f), // пыльная роза
            new Color(0.90f, 0.88f, 0.66f), // песок
            new Color(0.62f, 0.72f, 0.78f), // сталь
            new Color(0.86f, 0.55f, 0.42f), // ржавчина
        };

        // спрайт планеты по имени тела (стабилен между сессиями); null — арта нет
        public static Sprite Planet(string seedName)
        {
            Sprite[] pool = Pool(ref planets, "Art/planets");
            return pool != null ? pool[StableIndex(seedName, pool.Length)] : null;
        }

        // цвет планеты по имени тела (стабилен)
        public static Color PlanetTint(string seedName)
        {
            return PlanetTints[StableIndex(seedName + ":tint", PlanetTints.Length)];
        }

        // спрайт планеты-заправки по тиру топливной сети: 1 poor, 2 norm, 3 rich, 4 high
        public static Sprite GasStation(int fuelTier)
        {
            int tier = Mathf.Clamp(fuelTier, 1, 4);
            if (gas[tier] == null)
            {
                string[] names = { null, "gas_poor", "gas_norm", "gas_rich", "gas_high" };
                gas[tier] = Resources.Load<Sprite>("Art/stations/" + names[tier]);
            }
            return gas[tier];
        }

        // случайный астероид из 9 (общий пул с кометами); null — арта нет
        public static Sprite RandomAsteroid()
        {
            Sprite[] pool = Pool(ref asteroids, "Art/asteroids");
            return pool != null ? pool[Random.Range(0, pool.Length)] : null;
        }

        private static Sprite[] Pool(ref Sprite[] field, string path)
        {
            if (field == null)
            {
                field = Resources.LoadAll<Sprite>(path);
                if (field == null || field.Length == 0)
                {
                    Debug.LogWarning("[Art] Пусто: Assets/Resources/" + path);
                    field = new Sprite[0];
                }
            }
            return field.Length > 0 ? field : null;
        }

        // множитель размера планеты по имени: не все растут (жалоба «мелкие»),
        // треть остаётся как есть, треть x1.5, треть x2 — облик стабилен между сессиями
        public static float PlanetSizeMul(string seedName)
        {
            float[] muls = { 1f, 1.5f, 2f };
            return muls[StableIndex(seedName + ":size", muls.Length)];
        }

        // FNV-1a: строковый GetHashCode рандомизирован между сессиями Unity
        private static int StableIndex(string seed, int count)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < seed.Length; i++)
                {
                    hash ^= seed[i];
                    hash *= 16777619;
                }
                return (int)(hash % (uint)count);
            }
        }
    }
}
