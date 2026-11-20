using UnityEngine;

namespace SpaceTaxi.UI
{
    // Цвета сегментов бака/уровней — один источник для HUD и экрана станции
    internal static class HudPalette
    {
        internal static readonly Color SegmentOn = new Color(0.235f, 1f, 0.557f, 1f);   // горящий сегмент
        internal static readonly Color SegmentOff = new Color(0.059f, 0.2f, 0.125f, 0.824f); // погашенный
    }
}
