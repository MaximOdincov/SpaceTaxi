using System.Globalization;
using UnityEngine;

namespace SpaceTaxi.UI
{
    // Единый формат денег, таймеров и дистанций — все экраны берут его
    // из одного места
    public static class UiFormat
    {
        // символ валюты проекта
        public const string Currency = "Đ";

        // компактный формат: 10000 -> 10k, 1 400 000 -> 1.4m — размер строки предсказуем
        public static string FormatCredits(int value)
        {
            if (value >= 1_000_000)
                return (value / 1_000_000f).ToString("0.#", CultureInfo.InvariantCulture) + "m";
            if (value >= 1000)
                return (value / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return value.ToString();
        }

        // таймеры в mm:ss; округление вверх, чтобы «0:59» не висело две секунды
        public static string FormatClock(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return total / 60 + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        // дистанция «834 юн» / «2.1k юн»
        public static string Distance(float юн)
        {
            if (юн < 1000f)
                return ((int)юн).ToString(CultureInfo.InvariantCulture);
            return (юн / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        }
    }
}
