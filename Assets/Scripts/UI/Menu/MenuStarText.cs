using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.UI
{
    // Звёздный текст: строка выкладывается по маскам 5x7 (MenuStarFont) — по
    // мерцающей звезде-искре на включённую ячейку. Контейнер — ребёнок целевого
    // ректа: hover-скейл и CanvasGroup кнопки действуют на звёзды автоматически.
    public sealed class MenuStarText : MonoBehaviour
    {
        private const int GlyphColumns = 5;
        private const int GlyphRows = 7;
        private const float LetterGap = 1f;    // клетки между буквами
        private const float SpaceAdvance = 3f; // ширина пробела, клеток
        private const float Jitter = 0.08f;    // дрожание звезды, доли клетки
        private const float MinCell = 1.5f;    // px, ниже текст перестаёт читаться

        // Форма точки глифа: Spark — 4-лучевая искра как звёзды фона; Diamond —
        // ромб с вогнутыми сторонами; Circle — мягкая точка. Переключение —
        // константа GlyphStyle; сетка и маски 5x7 от стиля не зависят.
        private static readonly StarGlyphStyle GlyphStyle = StarGlyphStyle.Spark;
        // Параметры искры Spark: тонкие длинные лучи и компактное горячее ядро.
        // Точка ~1.6-1.8 клетки: ореолы соседей смыкаются в сплошную светящуюся
        // линию штриха, ядра-«бусины» остаются различимы поодиночке.
        private const float SparkArmWidth = 0.07f;
        private const float SparkArmLength = 1.0f;
        private const float SparkArmEdge = 0.03f;
        private const float SparkCore = 0.40f;      // радиус горячего ядра (доля полуразмера)
        private const float SparkCoreSoftness = 0.14f; // мягкий край ядра
        private const float SparkArmBrightness = 0.65f; // яркость лучей относительно ядра
        private const float SparkSizeMin = 1.6f;    // размер точки в клетках
        private const float SparkSizeMax = 1.8f;
        // Прореживание «созвездия»: звезда ставится на клетки маски с
        // (row+column) % CellParityStep == 0 (у диагонального соседа сумма
        // меняется на шаг — диагонали штрихов сохраняются). Шаг 2 рассыпает
        // буквы, оставлен шаг 1 — без прореживания.
        private const int CellParityStep = 1;
        private const float DiamondFatness = 0.60f; // полнота лучей: меньше — тоньше/острее, 1 — простой ромб
        private const float DiamondCore = 0.30f;    // радиус горячего ядра (доля полуширины)
        private const float DiamondSizeMin = 1.15f; // размер точки в клетках для Diamond
        private const float DiamondSizeMax = 1.60f; // (крупнее Circle — компенсация вогнутых сторон)
        private const float CircleSizeMin = 1.05f;  // размер точки в клетках для Circle
        private const float CircleSizeMax = 1.5f;

        private readonly List<MenuTwinkle.Star> stars = new();
        private float boost;

        // Плавная перекраска глифов (hover кнопок): rgb лерпится к цели каждый
        // кадр (~0.15с), без корутин; альфа каждой звезды остаётся своей — в
        // uGUI альфа лежит в цвете, лерп rgb её не трогает. Старт — белый.
        private const float ColorTweenTime = 0.15f; // сек на переход цвета
        private Color glyphRgb = Color.white;
        private Color glyphRgbTarget = Color.white;

        public float Width { get; private set; }
        public float Height { get; private set; }

        // Выложить text по центру площади target. Клетка подбирается под
        // прямоугольник (maxWidth/maxHeight — доли площади) либо задаётся
        // явно через cellOverride. containerOffset — сдвиг контейнера от центра
        // target (заголовок крупнее ректа растёт вправо от его левого края,
        // не уезжая за экран). Возвращает итоговый размер клетки (px).
        public float Build(RectTransform target, string text, Color[] palette,
            float maxWidthFactor = 0.92f, float maxHeightFactor = 0.86f, float cellOverride = 0f,
            Vector2 containerOffset = default)
        {
            var go = new GameObject("StarGlyphs", typeof(RectTransform));
            go.transform.SetParent(target, false);
            RectTransform container = (RectTransform)go.transform;
            container.anchorMin = new Vector2(0.5f, 0.5f);
            container.anchorMax = new Vector2(0.5f, 0.5f);
            container.anchoredPosition = containerOffset;
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

            // спрайт точки глифа строится один раз на текст; для Circle —
            // мягкая точка StubSprites (baseSprite = null)
            Sprite glyphDot;
            float sizeMin;
            float sizeMax;
            if (GlyphStyle == StarGlyphStyle.Spark)
            {
                glyphDot = MenuTwinkle.GlyphSpark(SparkArmWidth, SparkArmLength, SparkArmEdge,
                    SparkCore, SparkCoreSoftness, SparkArmBrightness);
                sizeMin = SparkSizeMin;
                sizeMax = SparkSizeMax;
            }
            else if (GlyphStyle == StarGlyphStyle.Diamond)
            {
                glyphDot = MenuTwinkle.DiamondSparkle(DiamondFatness, DiamondCore);
                sizeMin = DiamondSizeMin;
                sizeMax = DiamondSizeMax;
            }
            else
            {
                glyphDot = null;
                sizeMin = CircleSizeMin;
                sizeMax = CircleSizeMax;
            }

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
                        if ((row + column) % CellParityStep != 0)
                            continue; // прореживание «созвездия» — см. константу
                        Vector2 position = new(
                            cursor + (column + 0.5f) * cell + Random.Range(-Jitter, Jitter) * cell,
                            Height * 0.5f - (row + 0.5f) * cell + Random.Range(-Jitter, Jitter) * cell);
                        Color color = palette[Random.Range(0, palette.Length)];
                        color.a *= Random.Range(0.75f, 1f);
                        MenuTwinkle.CreateStar(container, position, cell * Random.Range(sizeMin, sizeMax),
                            color, 0.22f * Random.Range(0.7f, 1.3f), Random.Range(0.8f, 2.2f),
                            Random.Range(0f, Mathf.PI * 2f), sparkle: false, stars, baseSprite: glyphDot);
                    }
                }
                cursor += (GlyphColumns + LetterGap) * cell;
            }
            return cell;
        }

        // Общая добавка к яркости (наведение на кнопку), 0..1
        public void SetBoost(float value) => boost = value;

        // Задать цель цвета rgb глифов (белый базовый / жёлтый под наведение);
        // переход — плавный лерп в Update, альфа каждой звезды сохраняется
        public void SetGlyphColor(Color color) => glyphRgbTarget = color;

        private void Update()
        {
            // твин цвета: каждый кадр движемся к цели; запись в звёзды — только
            // пока rgb не сошёлся. Мерцание (Step) продолжается ПОВЕРХ
            // hover-цвета: жёлтые звёзды тоже мерцают
            float step = Time.unscaledDeltaTime / ColorTweenTime;
            float r = Mathf.MoveTowards(glyphRgb.r, glyphRgbTarget.r, step);
            float g = Mathf.MoveTowards(glyphRgb.g, glyphRgbTarget.g, step);
            float b = Mathf.MoveTowards(glyphRgb.b, glyphRgbTarget.b, step);
            if (r != glyphRgb.r || g != glyphRgb.g || b != glyphRgb.b)
            {
                glyphRgb = new Color(r, g, b);
                var rgb = (Color32)glyphRgb;
                for (int i = 0; i < stars.Count; i++)
                {
                    MenuTwinkle.Star star = stars[i];
                    star.BaseColor = new Color32(rgb.r, rgb.g, rgb.b, star.BaseColor.a);
                    stars[i] = star;
                }
            }
            MenuTwinkle.Step(stars, Time.unscaledTime, boost);
        }

        private static float MeasureColumns(string text)
        {
            float total = 0f;
            foreach (char character in text)
                total += character == ' ' ? SpaceAdvance + LetterGap : GlyphColumns + LetterGap;
            return Mathf.Max(total - LetterGap, 1f);
        }
    }
}
