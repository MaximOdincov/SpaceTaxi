using UnityEngine;

namespace SpaceTaxi.UI
{
    // Палитра HUD-прибора (Н12.4): значения СЕГМЕНТОВ бака/уровней — один
    // источник для HudBinder и StationScreenController (полоски уровней).
    // прочие цвета HUD — роли UiTheme (UiKit)
    internal static class HudPalette
    {
        internal static readonly Color SegmentOn = new Color(0.235f, 1f, 0.557f, 1f);   // горящий сегмент
        internal static readonly Color SegmentOff = new Color(0.059f, 0.2f, 0.125f, 0.824f); // погашенный
    }
}
