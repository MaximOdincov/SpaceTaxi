using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.Core
{
    // Заглушки графики: спрайты рисуются кодом (решение 00_MAIN №15).
    public static class StubSprites
    {
        private static readonly Dictionary<int, Sprite> circles = new Dictionary<int, Sprite>();
        private static Sprite shipTriangle;

        // Круг с тёмной обводкой по краю — заметнее на тёмном фоне.
        public static Sprite Circle(float diameter)
        {
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter));
            if (!circles.TryGetValue(key, out Sprite sprite))
            {
                sprite = Create(key, InCircle);
                circles[key] = sprite;
            }
            return sprite;
        }

        // Корабль: треугольник длиной 8 юнитов, остриём вправо.
        public static Sprite ShipTriangle()
        {
            if (shipTriangle == null)
                shipTriangle = Create(8f, InTriangle);
            return shipTriangle;
        }

        private static bool InCircle(float x, float y) => x * x + y * y <= 1f;

        // Вершины: нос (1, 0), верх (-0.7, 0.7), низ (-0.7, -0.7).
        private static bool InTriangle(float x, float y)
        {
            float s1 = (-1.7f) * y - 0.7f * (x - 1f);
            float s2 = 0f * (y - 0.7f) - (-1.4f) * (x + 0.7f);
            float s3 = 1.7f * (y + 0.7f) - 0f * (x + 0.7f);
            return (s1 >= 0f && s2 >= 0f && s3 >= 0f) || (s1 <= 0f && s2 <= 0f && s3 <= 0f);
        }

        private static Sprite Create(float diameter, System.Func<float, float, bool> inside)
        {
            // ~6 пикселей на юнит, степень двойки, в разумных пределах.
            int size = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(diameter * 6f)), 32, 512);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            var fill = new Color32(255, 255, 255, 255);
            var outline = new Color32(40, 40, 50, 255);
            // Толщина обводки — ~5% радиуса (в долях от единичного круга).
            float outlineWidth = 0.09f;
            for (int iy = 0; iy < size; iy++)
            {
                for (int ix = 0; ix < size; ix++)
                {
                    float x = (ix + 0.5f) / size * 2f - 1f;
                    float y = (iy + 0.5f) / size * 2f - 1f;
                    float dist = Mathf.Sqrt(x * x + y * y);
                    Color32 c;
                    if (dist > 1f)
                        c = new Color32(0, 0, 0, 0);
                    else if (dist > 1f - outlineWidth && inside(x, y))
                        c = outline;
                    else if (inside(x, y))
                        c = fill;
                    else
                        c = new Color32(0, 0, 0, 0);
                    pixels[iy * size + ix] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            // pixelsPerUnit подобран так, чтобы спрайт имел заданный размер в юнитах.
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size / diameter);
        }
    }
}
