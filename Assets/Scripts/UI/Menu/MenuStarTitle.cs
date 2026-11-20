using UnityEngine;
using TMPro;

namespace SpaceTaxi.UI
{
    // Звёздное название меню: «SPACE TAXI» из мерцающих звёзд по маскам 5x7
    // (MenuStarText); TMP-логотип под ними гасится — шрифта в заголовке нет.
    public sealed class MenuStarTitle : MonoBehaviour
    {
        private const string Title = "SPACE TAXI";

        // Клетка маски в канвас-px (буквы ~112 px высотой). Ручка размера
        // заголовка — только эта константа
        private const float TitleCell = 16f;
        // Колонки "SPACE TAXI": 9 букв × (5+1) + пробел (3+1) − хвостовой зазор = 57
        private const float TitleColumns = 57f;

        private static readonly Color[] Palette =
        {
            new(0.659f, 1f, 0.796f),   // мятный (цвет логотипа в сцене)
            new(1f, 0.94f, 0.78f),     // тёплый белый
            new(1f, 0.831f, 0.302f),   // такси-жёлтый
            new(0.80f, 0.88f, 1f)      // холодный голубой
        };

        private MenuStarText starText;

        // Заголовок строится в прямоугольнике TMP-логотипа (589x144). Клетка
        // задаётся явно (TitleCell): автоподбор по ширине 57 колонок дал бы ~10
        // (точка мельче ~13 экранных px не читается звездой). Когда надпись
        // шире ректа, сдвигаем её вправо на половину переполнения — левый край
        // встаёт на левый край шапки панели и заголовок не уезжает за экран
        public void Build(RectTransform logo)
        {
            if (logo.TryGetComponent(out TMP_Text text))
                text.enabled = false; // под звёздами шрифт не нужен

            starText = gameObject.AddComponent<MenuStarText>();
            var offset = new Vector2((TitleColumns * TitleCell - logo.rect.width) / 2f, 0f);
            starText.Build(logo, Title, Palette, maxWidthFactor: 0.98f, maxHeightFactor: 0.6f,
                cellOverride: TitleCell, containerOffset: offset);
        }
    }
}
