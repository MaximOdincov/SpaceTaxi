using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.UI
{
    // Звёздный текст (бриф E3): строка выкладывается по пиксельным маскам 5x7
    // (MenuStarFont) — на каждую включённую ячейку одна мерцающая звезда,
    // лёгкое дрожание позиции и примесь бликов-искр дают «россыпь», а малая
    // амплитуда мерцания — читаемость. Контейнер-ребёнок целевого
    // RectTransform: hover-скейл кнопки и гасящий CanvasGroup применяются к
    // звёздам автоматически. Текстовые компоненты под звёздами гасит
    // вызывающий код — шрифта в звёздных надписях нет.
    public sealed class MenuStarText : MonoBehaviour
    {
        private const int GlyphColumns = 5;
        private const int GlyphRows = 7;
        private const float LetterGap = 1f;    // клетки между буквами
        private const float SpaceAdvance = 3f; // ширина пробела, клеток
        private const float Jitter = 0.18f;    // дрожание звезды, доли клетки
        private const float SparkleChance = 0.18f;
        private const float MinCell = 1.5f;    // px, ниже текст перестаёт читаться

        private readonly List<MenuTwinkle.Star> stars = new();
        private float boost;

        public float Width { get; private set; }
        public float Height { get; private set; }

        // Выложить text по центру площади target. Клетка подбирается под
        // прямоугольник (maxWidth/maxHeight — доли площади) либо задаётся
        // явно через cellOverride. Возвращает итоговый размер клетки (px).
        public float Build(RectTransform target, string text, Color[] palette,
            float maxWidthFactor = 0.92f, float maxHeightFactor = 0.86f, float cellOverride = 0f)
        {
            var go = new GameObject("StarGlyphs", typeof(RectTransform));
            go.transform.SetParent(target, false);
            RectTransform container = (RectTransform)go.transform;
            container.anchorMin = new Vector2(0.5f, 0.5f);
            container.anchorMax = new Vector2(0.5f, 0.5f);
            container.anchoredPosition = Vector2.zero;
            container.pivot = new Vector2(0.5f, 0.5f);

            float columns = MeasureColumns(text);
            float cell = cellOverride > 0f
                ? cellOverride
                : Mathf.Min(target.rect.width * maxWidthFactor / columns,
                            target.rect.height * maxHeightFactor / GlyphRows);
            cell = Mathf.Max(cell, MinCell);
            Width = columns * cell;
            Height = GlyphRows * cell;
            container.sizeDelta = new Vector2(Width, Height);

            float cursor = -Width * 0.5f; // левый край текущей буквы
            foreach (char character in text)
            {
                if (character == ' ')
                {
                    cursor += (SpaceAdvance + LetterGap) * cell;
                    continue;
                }
                if (!MenuStarFont.TryGet(character, out string[] rows))
                {
                    cursor += (GlyphColumns + LetterGap) * cell; // нет маски — отступ
                    continue;
                }
                for (int row = 0; row < GlyphRows; row++)
                {
                    for (int column = 0; column < GlyphColumns; column++)
                    {
                        if (rows[row][column] != 'X')
                            continue;
                        Vector2 position = new(
                            cursor + (column + 0.5f) * cell + Random.Range(-Jitter, Jitter) * cell,
                            Height * 0.5f - (row + 0.5f) * cell + Random.Range(-Jitter, Jitter) * cell);
                        bool sparkle = Random.value < SparkleChance;
                        Color color = palette[Random.Range(0, palette.Length)];
                        color.a *= Random.Range(0.75f, 1f);
                        MenuTwinkle.CreateStar(container, position, cell * Random.Range(1.05f, 1.5f),
                            color, 0.22f * Random.Range(0.7f, 1.3f), Random.Range(0.8f, 2.2f),
                            Random.Range(0f, Mathf.PI * 2f), sparkle, stars);
                    }
                }
                cursor += (GlyphColumns + LetterGap) * cell;
            }
            return cell;
        }

        // Общая добавка к яркости (наведение на кнопку), 0..1
        public void SetBoost(float value) => boost = value;

        private void Update() => MenuTwinkle.Step(stars, Time.unscaledTime, boost);

        private static float MeasureColumns(string text)
        {
            float total = 0f;
            foreach (char character in text)
                total += character == ' ' ? SpaceAdvance + LetterGap : GlyphColumns + LetterGap;
            return Mathf.Max(total - LetterGap, 1f);
        }
    }
}
