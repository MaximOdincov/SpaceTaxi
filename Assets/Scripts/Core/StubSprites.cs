using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.Core
{
    public static class StubSprites
    {
        private static readonly Dictionary<int, Sprite> circles = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> rings = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> softDots = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> sparkles = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> clusters = new Dictionary<int, Sprite>();
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

        // звезда с крестом-бликом: чёткое ядро + 4 сужающихся луча (не клякса)
        public static Sprite StarSparkle(float diameter)
        {
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter));
            if (!sparkles.TryGetValue(key, out Sprite sprite) || sprite == null)
            {
                var tex = CreateSparkleTexture(128);
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 128f, 128f), new Vector2(0.5f, 0.5f), 128f / diameter);
                sparkles[key] = sprite;
            }
            return sprite;
        }

        // компактное скопление из 3-5 чётких звёзд; сид фиксирует раскладку
        public static Sprite StarCluster(float diameter, int seed)
        {
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter)) * 1000 + (seed & 1023);
            if (!clusters.TryGetValue(key, out Sprite sprite) || sprite == null)
            {
                var tex = CreateClusterTexture(128, seed);
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 128f, 128f), new Vector2(0.5f, 0.5f), 128f / diameter);
                clusters[key] = sprite;
            }
            return sprite;
        }

        public static Sprite ShipTriangle()
        {
            if (shipTriangle == null)
                shipTriangle = Sprite.Create(CreateMaskTexture(192, InTriangle), new Rect(0f, 0f, 192f, 192f), new Vector2(0.5f, 0.5f), 192f / 14f);
            return shipTriangle;
        }

        // указатель цели у края экрана: классическая навигационная стрелка
        // с выемкой в хвосте (квадрат-заглушка ShipTriangle читался как «трапеция»)
        private static Sprite chevron;

        public static Sprite TargetChevron()
        {
            if (chevron != null)
                return chevron;
            chevron = Sprite.Create(CreateMaskTexture(128, (x, y) =>
            {
                // треугольник (0,0.9)-(−0.75,−0.7)-(0.75,−0.7) минус хвостовая выемка
                bool body = y <= 0.9f && y >= -0.7f
                            && Mathf.Abs(x) <= 0.75f * (y + 0.7f) / 1.6f;
                bool notch = y < -0.15f
                             && Mathf.Abs(x) < 0.22f * (y + 0.7f) / 0.55f;
                return body && !notch;
            }), new Rect(0f, 0f, 128f, 128f), new Vector2(0.5f, 0.5f), 128f / 3f);
            return chevron;
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

        public static Texture2D CreateSparkleTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear };
            var pixels = new Color32[size * size];
            for (int iy = 0; iy < size; iy++)
            {
                for (int ix = 0; ix < size; ix++)
                {
                    float x = (ix + 0.5f) / size * 2f - 1f;
                    float y = (iy + 0.5f) / size * 2f - 1f;
                    float ax = Mathf.Abs(x);
                    float ay = Mathf.Abs(y);

                    // ядро: компактная горячая точка с мягким краем
                    float dist = Mathf.Sqrt(x * x + y * y);
                    float core = Mathf.Clamp01((0.30f - dist) / 0.14f);
                    core = Mathf.SmoothStep(0f, 1f, core);

                    // лучи: тонкие сужающиеся крестовины
                    float armH = ay <= 0.05f ? Mathf.Clamp01(1f - ax) : 0f;
                    float armV = ax <= 0.05f ? Mathf.Clamp01(1f - ay) : 0f;
                    float arm = Mathf.Max(armH, armV);
                    arm *= arm; // сужение к концам

                    float a = Mathf.Clamp01(Mathf.Max(core, arm * 0.9f));
                    pixels[iy * size + ix] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, false);
            return tex;
        }

        public static Texture2D CreateClusterTexture(int size, int seed)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear };
            var rng = new System.Random(seed);
            int count = 3 + rng.Next(3); // 3..5 звёзд в группе
            var xs = new float[count];
            var ys = new float[count];
            var rs = new float[count];
            xs[0] = 0f;
            ys[0] = 0f;
            rs[0] = 0.16f; // самая яркая — в центре
            for (int i = 1; i < count; i++)
            {
                float ang = (float)(rng.NextDouble() * System.Math.PI * 2.0);
                float dist = 0.30f + (float)rng.NextDouble() * 0.45f;
                xs[i] = Mathf.Cos(ang) * dist;
                ys[i] = Mathf.Sin(ang) * dist;
                rs[i] = 0.06f + (float)rng.NextDouble() * 0.09f;
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
                        // чёткая точка: плоский диск + узкий мягкий край
                        float dx = x - xs[i];
                        float dy = y - ys[i];
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01((rs[i] + 0.05f - d) / 0.05f);
                        alpha = Mathf.Max(alpha, Mathf.Min(a, 1f));
                    }
                    pixels[iy * size + ix] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, false);
            return tex;
        }
    }
}
