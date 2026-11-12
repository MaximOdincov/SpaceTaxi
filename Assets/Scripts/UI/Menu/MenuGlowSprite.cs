using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.UI
{
    // Спрайт hover-свечения кнопок (E7.2): тонкая полоса мягкого света вдоль
    // края скруглённого прямоугольника — снаружи длинный экспоненциальный
    // хвост альфы, внутрь короткий подзатухающий забег. Заменяет прежний
    // растянутый SoftDot, который читался как «пятно» в центре кнопки.
    // Image с этим спрайтом ставится типом Sliced (рамка BorderPx): толщина
    // полосы одинакова по периметру и у кнопок любой ширины (НОВАЯ ИГРА /
    // ВЫХОД). PPU спрайта = referencePixelsPerUnit канваса — uGUI делит
    // рамку на PPU/ReferencePPU, иначе толщина поплывёт у канвасов с другим
    // Reference Pixels Per Unit (в MainMenu он 1000). Текстура строится один
    // раз и кэшируется; геометрия — ручки настройки ниже, итоговая яркость =
    // PeakAlpha * StarButtonDecorator.HaloAlpha.
    public static class MenuGlowSprite
    {
        private const int Size = 128;             // размер текстуры, px
        private const int BorderPx = 26;          // 9-slice рамка = вся зона полосы, не растягивается
        private const float RectHalf = 0.72f;     // полуширина свечения, доля полуразмера текстуры
        private const float CornerRadius = 0.18f; // скругление углов, доля
        private const float OuterTail = 0.22f;    // длина хвоста наружу — «длинный хвост альфы»
        private const float InnerTail = 0.08f;    // забег внутрь кнопки — мягкий стык
        private const float EdgeFade = 0.12f;     // гашение остатка хвоста к границе текстуры (без среза)
        private const float PeakAlpha = 0.8f;     // альфа на кромке свечения в самой текстуре

        private static readonly Dictionary<int, Sprite> sprites = new Dictionary<int, Sprite>();

        // referencePixelsPerUnit — Reference Pixels Per Unit канваса с кнопкой
        public static Sprite Rim(float referencePixelsPerUnit)
        {
            int key = Mathf.Max(1, Mathf.RoundToInt(referencePixelsPerUnit));
            if (sprites.TryGetValue(key, out Sprite cached) && cached != null)
                return cached;
            Texture2D tex = CreateBandTexture();
            var border = new Vector4(BorderPx, BorderPx, BorderPx, BorderPx);
            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f),
                (float)key, 0u, SpriteMeshType.FullRect, border);
            sprites[key] = sprite;
            return sprite;
        }

        private static Texture2D CreateBandTexture()
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear };
            var pixels = new Color32[Size * Size];
            for (int iy = 0; iy < Size; iy++)
            {
                for (int ix = 0; ix < Size; ix++)
                {
                    float x = (ix + 0.5f) / Size * 2f - 1f;
                    float y = (iy + 0.5f) / Size * 2f - 1f;

                    // знаковое расстояние до скруглённого прямоугольника: 0 на
                    // кромке, >0 снаружи, <0 внутри
                    float d = RoundedRectDistance(x, y, RectHalf, CornerRadius);
                    float a = d >= 0f
                        ? Mathf.Exp(-d / OuterTail)             // мягкий длинный хвост наружу
                        : Mathf.Clamp01(1f + d / InnerTail);    // короткий забег внутрь

                    // страховка от жёсткого среза на границе текстуры: хвост
                    // длиннее запаса до края — досыпаем плавное затухание
                    float edge = Mathf.Min(1f - Mathf.Abs(x), 1f - Mathf.Abs(y));
                    a *= Mathf.Clamp01(edge / EdgeFade);

                    pixels[iy * Size + ix] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * PeakAlpha * 255f));
                }
            }
            tex.Apply(true, false);
            return tex;
        }

        // классический SDF скруглённого прямоугольника (полуразмер half, радиус radius)
        private static float RoundedRectDistance(float x, float y, float half, float radius)
        {
            float qx = Mathf.Abs(x) - (half - radius);
            float qy = Mathf.Abs(y) - (half - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - radius;
        }
    }
}
