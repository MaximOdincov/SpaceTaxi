using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.Core
{
    public static class StubSprites
    {
        private static readonly Dictionary<int, Sprite> circles = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> rings = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> softDots = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> blobs = new Dictionary<int, Sprite>();
        private static Sprite shipTriangle;
        private static Sprite line;
        public static Sprite Circle(float diameter)
        {
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter));
            if (!circles.TryGetValue(key, out Sprite sprite) || sprite == null)
            {
                var tex = CreateCircleTexture(64, 0.08f);
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f / diameter);
                circles[key] = sprite;
            }
            return sprite;
        }

        // тонкое посадочное кольцо: одна маска на все радиусы (как BeltRing в карте),
        // PPU подгоняет спрайт под диаметр в юнитах
        public static Sprite Ring(float diameter)
        {
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter));
            if (!rings.TryGetValue(key, out Sprite sprite) || sprite == null)
            {
                var tex = CreateMaskTexture(256, (x, y) =>
                {
                    float d = Mathf.Sqrt(x * x + y * y);
                    return d <= 1f && d >= 0.96f;
                });
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f), 256f / diameter);
                rings[key] = sprite;
            }
            return sprite;
        }

        // мягкая точка без тёмной окантовки: пыль, далёкие светила. core — доля
        // радиуса с полной альфой (0 = полностью мягкая, 0.7 = горячее ядро)
        public static Sprite SoftDot(float diameter, float core = 0f)
        {
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter)) * 100 + Mathf.RoundToInt(core * 10f);
            if (!softDots.TryGetValue(key, out Sprite sprite) || sprite == null)
            {
                var tex = CreateSoftDotTexture(64, core);
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f / diameter);
                softDots[key] = sprite;
            }
            return sprite;
        }

        // неровная мягкая клякса (туманность): пачка смещённых кругов с плавным
        // краем; сид фиксирует форму — раскладка слоёв одинакова между запусками
        public static Sprite Blob(int seed)
        {
            if (!blobs.TryGetValue(seed, out Sprite sprite) || sprite == null)
            {
                var tex = CreateBlobTexture(128, seed);
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 128f, 128f), new Vector2(0.5f, 0.5f), 128f / 16f);
                blobs[seed] = sprite;
            }
            return sprite;
        }

        public static Sprite ShipTriangle()
        {
            if (shipTriangle == null)
                shipTriangle = Sprite.Create(CreateMaskTexture(192, InTriangle), new Rect(0f, 0f, 192f, 192f), new Vector2(0.5f, 0.5f), 192f / 14f);
            return shipTriangle;
        }

        public static Sprite Line()
        {
            if (line == null)
                line = Sprite.Create(CreateMaskTexture(256, InCapsule), new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f), 256f / 16f);
            return line;
        }

        public static bool InTriangle(float x, float y)
        {
            float s1 = (-1.7f) * y - 0.7f * (x - 1f);
            float s2 = 0f * (y - 0.7f) - (-1.4f) * (x + 0.7f);
            float s3 = 1.7f * (y + 0.7f) - 0f * (x + 0.7f);
            return (s1 >= 0f && s2 >= 0f && s3 >= 0f) || (s1 <= 0f && s2 <= 0f && s3 <= 0f);
        }

        public static bool InCapsule(float x, float y)
        {
            float dx = Mathf.Max(Mathf.Abs(x) - 7f, 0f);
            return dx * dx + y * y <= 1f;
        }

        public static Texture2D CreateCircleTexture(int size, float outlineWidth)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear };
            var pixels = new Color32[size * size];
            var fill = new Color32(255, 255, 255, 255);
            var outline = new Color32(40, 40, 50, 255);
            float pixel = 2f / size;
            for (int iy = 0; iy < size; iy++)
            {
                for (int ix = 0; ix < size; ix++)
                {
                    float x = (ix + 0.5f) / size * 2f - 1f;
                    float y = (iy + 0.5f) / size * 2f - 1f;
                    float dist = Mathf.Sqrt(x * x + y * y);
                    float alpha = Mathf.Clamp01((1f - dist) / pixel + 0.5f);
                    Color c = dist > 1f - outlineWidth ? (Color)outline : (Color)fill;
                    c.a = alpha;
                    pixels[iy * size + ix] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, false);
            return tex;
        }

        public static Texture2D CreateMaskTexture(int size, System.Func<float, float, bool> inside)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear };
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
            return tex;
        }

        public static Texture2D CreateSoftDotTexture(int size, float core)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear };
            var pixels = new Color32[size * size];
            float edge = Mathf.Clamp01(1f - core);
            for (int iy = 0; iy < size; iy++)
            {
                for (int ix = 0; ix < size; ix++)
                {
                    float x = (ix + 0.5f) / size * 2f - 1f;
                    float y = (iy + 0.5f) / size * 2f - 1f;
                    float dist = Mathf.Sqrt(x * x + y * y);
                    float t = Mathf.Clamp01((1f - dist) / Mathf.Max(0.001f, edge));
                    byte a = (byte)(Mathf.SmoothStep(0f, 1f, t) * 255f);
                    pixels[iy * size + ix] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, false);
            return tex;
        }

        public static Texture2D CreateBlobTexture(int size, int seed)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear };
            var rng = new System.Random(seed);
            int count = 6 + rng.Next(3); // 6..8 кругов: ядро + неровные лепестки
            var xs = new float[count];
            var ys = new float[count];
            var rs = new float[count];
            xs[0] = 0f;
            ys[0] = 0f;
            rs[0] = 0.52f; // центральное ядро — клякса не распадается
            for (int i = 1; i < count; i++)
            {
                float ang = (float)(rng.NextDouble() * System.Math.PI * 2.0);
                float dist = 0.12f + (float)rng.NextDouble() * 0.30f;
                xs[i] = Mathf.Cos(ang) * dist;
                ys[i] = Mathf.Sin(ang) * dist;
                rs[i] = 0.24f + (float)rng.NextDouble() * 0.26f;
            }
            var pixels = new Color32[size * size];
            for (int iy = 0; iy < size; iy++)
            {
                for (int ix = 0; ix < size; ix++)
                {
                    float x = (ix + 0.5f) / size * 2f - 1f;
                    float y = (iy + 0.5f) / size * 2f - 1f;
                    float alpha = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        float dx = x - xs[i];
                        float dy = y - ys[i];
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        alpha = Mathf.Max(alpha, Mathf.Clamp01((rs[i] - d) / 0.16f + 0.5f));
                    }
                    byte a = (byte)(alpha * 255f);
                    pixels[iy * size + ix] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, false);
            return tex;
        }
    }
}
