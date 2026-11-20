using UnityEngine;

namespace SpaceTaxi.Economy
{
    // Витрина магазина: базовые корабли
    // всегда в наличии, поверх — ротация покрасок. Каждые 2 игровых часа у
    // модели с PaintVariants меняется показанный вариант. Выбор детерминирован
    // (хэш Id + номер слота): витрина одинакова у всех и не прыгает при
    // перезапуске. Слот считается от игровых часов борткомпа 14:02 + t/3
    // (3 реальные секунды = 1 игровая минута, как в OnboardBinder).
    public static class ShopStockService
    {
        private const int SlotMinutes = 120;   // ротация раз в 2 игровых часа
        private const int StartMinutes = 842;  // 14:02 — старт борткомпа
        // соль слота (константа Кнута): без unchecked это long (> int.MaxValue),
        // и умножение уводит хэш в long — CS1503
        private const int SlotSalt = unchecked((int)0x9E3779B9);

        // номер текущего слота ротации (монотонно растёт с игровым временем)
        public static int CurrentSlot(float time)
        {
            return (StartMinutes + (int)time / 3) / SlotMinutes;
        }

        // Показанный вариант модели в слоте: 0 = заводская, 1..N — PaintVariants.
        // Модели без вариантов всегда заводская; у остальных заводская
        // появляется через раз — смена витрины заметнее
        public static int DisplayPaintIndex(ShipConfig config, int slot)
        {
            if (config == null)
                return 0;
            int variants = config.PaintVariantCount;
            if (variants == 0)
                return 0;
            return Mod(DeterministicHash.StableHash(config.Id) ^ (slot * SlotSalt), variants + 1);
        }



        private static int Mod(int value, int m)
        {
            return ((value % m) + m) % m;
        }
    }
}
