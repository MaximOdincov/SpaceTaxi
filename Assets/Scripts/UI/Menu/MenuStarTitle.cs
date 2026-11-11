using UnityEngine;
using TMPro;

namespace SpaceTaxi.UI
{
    // Звёздное название меню (бриф E3): «SPACE TAXI» выложен из мерцающих
    // звёзд по пиксельным маскам 5x7 (MenuStarText), TMP-логотип под ними
    // гасится — шрифта в заголовке больше нет. Россыпь-подложка и shimmer
    // CanvasGroup прошлой итерации убраны: владелец просил буквы из звёзд
    // вместо фонового разброса, остатки старого вида не сохраняем.
    public sealed class MenuStarTitle : MonoBehaviour
    {
        private const string Title = "SPACE TAXI";

        private static readonly Color[] Palette =
        {
            new(0.659f, 1f, 0.796f),   // мятный (цвет логотипа в сцене)
            new(1f, 0.94f, 0.78f),     // тёплый белый
            new(1f, 0.831f, 0.302f),   // такси-жёлтый
            new(0.80f, 0.88f, 1f)      // холодный голубой
        };

        private MenuStarText starText;

        // Заголовок строится в прямоугольнике TMP-логотипа (589x144): клетка
        // ~10.5px, буквы 7 клеток ≈ 73px высотой — как прежний шрифт 96pt
        public void Build(RectTransform logo)
        {
            if (logo.TryGetComponent(out TMP_Text text))
                text.enabled = false; // под звёздами шрифт не нужен

            starText = gameObject.AddComponent<MenuStarText>();
            starText.Build(logo, Title, Palette, maxWidthFactor: 0.98f, maxHeightFactor: 0.6f);
        }
    }
}
