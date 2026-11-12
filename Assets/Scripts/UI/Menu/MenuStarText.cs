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

        // --- E7.1, ручки владельца: форма/плотность точек-звёзд глифов ---
        // Форма точки: Diamond — 4-лучевая «искра» (ромб с вогнутыми
        // сторонами + горячее ядро), Circle — прежняя мягкая точка.
        // Сетка и маски 5x7 от стиля не зависят; искры-блики (18%) и мерцание
        // общие для обоих стилей.
        private const StarGlyphStyle GlyphStyle = StarGlyphStyle.Diamond;
        private const float DiamondFatness = 0.60f; // полнота лучей: меньше — тоньше/острее, 1 — простой ромб
        private const float DiamondCore = 0.30f;    // радиус горячего ядра (доля полуширины) — плотность букв
        private const float DiamondSizeMin = 1.15f; // множители клетки для Diamond — чуть крупнее кружков
        private const float DiamondSizeMax = 1.60f; // (кружки были 1.05-1.5): компенсация вогнутых сторон
        private const float CircleSizeMin = 1.05f;  // прежние множители для стиля Circle
        private const float CircleSizeMax = 1.5f;

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

            // E7.1: спрайт точки глифа строится один раз на текст; для Circle
            // остаётся мягкая точка StubSprites (baseSprite = null)
            bool diamond = GlyphStyle == StarGlyphStyle.Diamond;
            Sprite glyphDot = diamond ? MenuTwinkle.DiamondSparkle(DiamondFatness, DiamondCore) : null;
            float sizeMin = diamond ? DiamondSizeMin : CircleSizeMin;
            float sizeMax = diamond ? DiamondSizeMax : CircleSizeMax;

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
                        MenuTwinkle.CreateStar(container, position, cell * Random.Range(sizeMin, sizeMax),
                            color, 0.22f * Random.Range(0.7f, 1.3f), Random.Range(0.8f, 2.2f),
                            Random.Range(0f, Mathf.PI * 2f), sparkle, stars, baseSprite: glyphDot);
                    }
                }
                cursor += (GlyphColumns + LetterGap) * cell;
            }
            return cell;
        }

        // Общая добавка к яркости (наведение на кнопку), 0..1
        public void SetBoost(float value) => boost = value;

        // Н6: перекрасить все глифы (rgb) — белый базовый / жёлтый под
        // наведение; индивидуальная альфа каждой звезды сохраняется
        public void SetGlyphColor(Color color)
        {
            var rgb = (Color32)color;
            for (int i = 0; i < stars.Count; i++)
            {
                MenuTwinkle.Star star = stars[i];
                star.BaseColor = new Color32(rgb.r, rgb.g, rgb.b, star.BaseColor.a);
                stars[i] = star;
                star.Image.color = star.BaseColor; // до ближайшего шага мерцания
            }
        }

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
