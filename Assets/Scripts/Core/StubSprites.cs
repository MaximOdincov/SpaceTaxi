using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.Core
{
    public static class StubSprites
    {
        private static readonly Dictionary<int, Sprite> circles = new Dictionary<int, Sprite>();
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
    }
}
