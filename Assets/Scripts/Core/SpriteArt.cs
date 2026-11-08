using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.Core
{
    // Фактические границы контента спрайта (по альфе): пользовательский арт не
    // заполняет канву — если масштабировать по прямоугольнику кадра, видимый
    // объект меньше коллайдера/радиуса. Кэш в статике, текстуры планет/станций
    // читаемые (isReadable в метах).
    public static class SpriteArt
    {
        private const byte AlphaThreshold = 24;

        private static readonly Dictionary<Sprite, Vector2> cache = new Dictionary<Sprite, Vector2>();

        // размер контента в юнитах спрайта (PPU); нечитаемая текстура — фолбэк на bounds
        public static Vector2 ContentSize(Sprite sprite)
        {
            if (sprite == null)
                return Vector2.zero;
            if (cache.TryGetValue(sprite, out Vector2 cached))
                return cached;

            Vector2 size;
            Texture2D tex = sprite.texture;
            if (tex == null || !tex.isReadable)
            {
                size = sprite.bounds.size;
            }
            else
            {
                Rect rect = sprite.textureRect;
                int x0 = Mathf.RoundToInt(rect.x), y0 = Mathf.RoundToInt(rect.y);
                int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
                Color32[] pixels = tex.GetPixels32();
                int minX = w, maxX = -1, minY = h, maxY = -1;
                for (int y = 0; y < h; y++)
                {
                    int row = (y0 + y) * tex.width;
                    for (int x = 0; x < w; x++)
                    {
                        if (pixels[row + x0 + x].a <= AlphaThreshold)
                            continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
                if (maxX < minX || maxY < minY)
                    size = sprite.bounds.size; // пустой спрайт — как было
                else
                    size = new Vector2(maxX - minX + 1, maxY - minY + 1) / sprite.pixelsPerUnit;
            }

            cache[sprite] = size;
            return size;
        }

        // масштаб, при котором контент спрайта занимает заданную мировую ширину
        public static float ScaleForWorldSize(Sprite sprite, float worldSize, float currentScale)
        {
            Vector2 content = ContentSize(sprite);
            float dim = Mathf.Max(content.x, content.y);
            if (dim <= 0.0001f || currentScale <= 0.0001f)
                return 1f;
            return worldSize / (dim * currentScale);
        }
    }
}
