using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.Core
{
    // Заглушки графики: спрайты рисуются кодом (решение 00_MAIN №15).
    // Края с аналитическим сглаживанием + мипмапы, иначе «лесенка».
    public static class StubSprites
    {
        private static readonly Dictionary<int, Sprite> circles = new Dictionary<int, Sprite>();
        private static Sprite shipTriangle;
        private static Sprite line;

        // Круг с тёмной обводкой по краю.
        // Null-check обязателен: кэш может содержать спрайты, уничтоженные
        // при выходе из Play Mode (domain reload выключен в проекте).
        public static Sprite Circle(float diameter)
        {
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter));
            if (!circles.TryGetValue(key, out Sprite sprite) || sprite == null)
            {
                sprite = CreateCircle(key);
                circles[key] = sprite;
            }
            return sprite;
        }

        // Корабль: треугольник длиной 14 юнитов, остриём вправо.
        public static Sprite ShipTriangle()
        {
            if (shipTriangle == null)
                shipTriangle = Create(14f, InTriangle);
            return shipTriangle;
        }

        // Полоска-указатель вектора скорости: капсула 16 x 2 юнита.
        public static Sprite Line()
        {
            if (line == null)
                line = Create(16f, InCapsule);
            return line;
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

        // Капсула: цилиндр длиной 14 (от -7 до 7 по X) радиусом 1.
        private static bool InCapsule(float x, float y)
        {
            float dx = Mathf.Max(Mathf.Abs(x) - 7f, 0f);
            return dx * dx + y * y <= 1f;
        }

        private static Sprite CreateCircle(float diameter)
        {
            int size = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(diameter * 8f)), 64, 1024);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                filterMode = FilterMode.Trilinear,
            };
            var pixels = new Color32[size * size];
            var fill = new Color32(255, 255, 255, 255);
            var outline = new Color32(40, 40, 50, 255);
            const float outlineWidth = 0.08f; // доля радиуса
            float pixel = 2f / size;          // размер пикселя в единичном круге
            for (int iy = 0; iy < size; iy++)
            {
                for (int ix = 0; ix < size; ix++)
                {
                    float x = (ix + 0.5f) / size * 2f - 1f;
                    float y = (iy + 0.5f) / size * 2f - 1f;
                    float dist = Mathf.Sqrt(x * x + y * y);
                    // Мягкая альфа на границе — без «лесенки».
                    float alpha = Mathf.Clamp01((1f - dist) / pixel + 0.5f);
                    bool edge = dist > 1f - outlineWidth;
                    Color c = edge ? (Color)outline : (Color)fill;
                    c.a = alpha;
                    pixels[iy * size + ix] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, false);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size / diameter);
        }

        private static Sprite Create(float diameter, System.Func<float, float, bool> inside)
        {
            int size = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(diameter * 12f)), 64, 1024);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                filterMode = FilterMode.Trilinear,
            };
            var pixels = new Color32[size * size];
            var fill = new Color32(255, 255, 255, 255);
            for (int iy = 0; iy < size; iy++)
            {
                for (int ix = 0; ix < size; ix++)
                {
                    float x = (ix + 0.5f) / size * 2f - 1f;
                    float y = (iy + 0.5f) / size * 2f - 1f;
                    pixels[iy * size + ix] = inside(x, y) ? fill : new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, false);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size / diameter);
        }
    }
}
