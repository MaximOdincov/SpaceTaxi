using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.UI
{
    // Звёздный текст (бриф E3): строка выкладывается по пиксельным маскам 5x7
    // (MenuStarFont) — на каждую включённую ячейку одна мерцающая звезда,
    // лёгкое дрожание позиции и примесь бликов-искр дают «россыпь», а малая
    // амплитуда мерцания — читаемость. E8.1: точка глифа — честная 4-лучевая
    // искра как звёзды параллакса (GlyphStyle=Spark, размер крупнее клетки —
    // вылет за счёт прозрачных диагоналей не собирается в блок). E8.2:
    // перекраска глифов (hover кнопок) — плавный лерп rgb к цели ~0.15с,
    // индивидуальная альфа каждой звезды сохраняется; мерцание идёт поверх
    // hover-цвета. Контейнер-ребёнок целевого RectTransform: hover-скейл
    // кнопки и гасящий CanvasGroup применяются к звёздам автоматически.
    // Текстовые компоненты под звёздами гасит вызывающий код — шрифта в
    // звёздных надписях нет.
    public sealed class MenuStarText : MonoBehaviour
    {
        private const int GlyphColumns = 5;
        private const int GlyphRows = 7;
        private const float LetterGap = 1f;    // клетки между буквами
        private const float SpaceAdvance = 3f; // ширина пробела, клеток
        private const float Jitter = 0.18f;    // дрожание звезды, доли клетки
        private const float SparkleChance = 0.18f;  // доля ярких бликов-акцентов
                                                    // (StarSparkle из StubSprites —
                                                    // всегда рисовался корректно;
                                                    // «квадратность» была у глифовой
                                                    // текстуры без SetPixels32)
        private const float MinCell = 1.5f;    // px, ниже текст перестаёт читаться

        // --- E7.1/E8.1, ручки владельца: форма/плотность точек-звёзд глифов ---
        // Форма точки: Spark — честная 4-лучевая искра как звёзды параллакса
        // (E8.1: тонкие лучи вертикаль+горизонталь, яркое круглое ядро,
        // прозрачные диагонали); Diamond — ромб с вогнутыми сторонами (Н7 —
        // отвергнуто: читалось «квадратами»); Circle — мягкая точка.
        // Откат — одна константа GlyphStyle. Сетка и маски 5x7 от стиля не
        // зависят; искры-блики (18%) и мерцание общие для всех стилей.
        private static readonly StarGlyphStyle GlyphStyle = StarGlyphStyle.Spark;
        // Spark (E8.1 + Н8.25 + правка 2026-11-15 «буквы из квадратиков»):
        // Н8.25 сделал лучи толстыми (0.38 — половина толщины плюса от
        // полразмера точки) ради видимости ~2px на экране — но яркая область
        // точки стала «толстым плюсом», который после мип-фильтрации читается
        // КВАДРАТОМ: владелец снова видит «квадратики», а не звёзды. Лечение —
        // форма как у параллаксовых искр (StubSprites.StarSparkle), которые
        // владельцу нравятся: компактное горячее круглое ядро + ТОНКИЕ длинные
        // лучи (0.10 ≈ 1-1.5 экранных px, но луч жив как светлая нить — так же
        // выживают лучи фоновых искр), диагонали прозрачны. Клетка ~9 экранных
        // px, точка ~1.85-2.25× клетки (16-20 экранного px) — ядро ~6px,
        // звёзды в штрихе различимы поодиночке («бусины на нити»), блоков нет:
        // в Н8.25 сливались именно ТОЛСТЫЕ плюсы, у компактных ядер зазор есть.
        private const float SparkArmWidth = 0.07f;
        private const float SparkArmLength = 1.0f;
        private const float SparkArmEdge = 0.03f;
        private const float SparkCore = 0.30f;
        private const float SparkCoreSoftness = 0.12f;
        private const float SparkArmBrightness = 0.65f;
        private const float SparkSizeMin = 1.25f;   // множители клетки: зазор между
        private const float SparkSizeMax = 1.45f;   // точками при шаге 2 клетки ~4px
        // Прореживание «созвездия» (ручка владельца): звезда ставится на
        // клетки маски с (row+column) % CellParityStep == 0 (диагонали штрихов
        // сохраняются — у соседа по диагонали сумма меняется на шаг). Шаг 2 —
        // звезда через клетку: пробовал 2026-11-15 после починки текстуры —
        // звёзды крупнее и отдельнее, но буквы рассыпаются (штрих из точек
        // через 2 клетки при клетке ~10 канвас-px не читается). Оставлен шаг 1
        // (без прореживания): буквы читаются, звёздность дают мерцание и
        // хвосты лучей. Откат к «созвездию» — поставить 2.
        private const int CellParityStep = 1;
        private const float DiamondFatness = 0.60f; // (откат Diamond) полнота лучей: меньше — тоньше/острее, 1 — простой ромб
        private const float DiamondCore = 0.30f;    // радиус горячего ядра (доля полуширины) — плотность букв
        private const float DiamondSizeMin = 1.15f; // множители клетки для Diamond — чуть крупнее кружков
        private const float DiamondSizeMax = 1.60f; // (кружки были 1.05-1.5): компенсация вогнутых сторон
        private const float CircleSizeMin = 1.05f;  // прежние множители для стиля Circle
        private const float CircleSizeMax = 1.5f;

        private readonly List<MenuTwinkle.Star> stars = new();
        private float boost;

        // E8.2: плавная перекраска глифов (hover кнопок): rgb лерпится к цели
        // каждый кадр (~0.15с на переход), без корутин; альфа каждой звезды
        // остаётся индивидуальной (урок TMP/uGUI: альфа — канал цвета, лерп
        // rgb её не трогает). Старт white — кнопки базово белые.
        private const float ColorTweenTime = 0.15f; // сек на переход цвета
        private Color glyphRgb = Color.white;
        private Color glyphRgbTarget = Color.white;

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

            // E7.1/E8.1: спрайт точки глифа строится один раз на текст; для
            // Circle остаётся мягкая точка StubSprites (baseSprite = null)
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

        // Н6/E8.2: задать ЦЕЛЬ цвета rgb глифов (белый базовый / жёлтый под
        // наведение); переход — плавный лерп в Update, индивидуальная альфа
        // каждой звезды сохраняется
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
