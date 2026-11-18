using System.Globalization;
using UnityEngine;

namespace SpaceTaxi.UI
{
    // Единый хелпер денежных строк и таймеров (Н10.9, Слой 1 — CODE_REVIEW_N9
    // §3.5/§7.3 «утилиты не по адресу»): прежде «Đ» дублировалась константой в
    // OnboardBinder / StationUI / OrderCompleteScreenController, а форматторы
    // жили на OnboardBinder и вызывались из 8 классов
    public static class UiFormat
    {
        // символ валюты — единственная копия значения в проекте
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

        // таймеры заказов в mm:ss (Н7 Q7.1): округление вверх — «0:59» не висит
        // две секунды; используется и HudBinder-ом в подсказке цели
        public static string FormatClock(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return total / 60 + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        // Н12.4: дистанция «834 юн» / «2.1k юн» — прежде дублировалась в
        // WaypointIndicator и MinimapBinder
        public static string Distance(float юн)
        {
            if (юн < 1000f)
                return ((int)юн).ToString(CultureInfo.InvariantCulture);
            return (юн / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        }
    }
}
