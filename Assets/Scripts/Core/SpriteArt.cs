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

        private static readonly Dictionary<Sprite, Rect> cache = new Dictionary<Sprite, Rect>();

        // размер контента в юнитах спрайта (PPU); нечитаемая текстура — фолбэк на bounds
        public static Vector2 ContentSize(Sprite sprite)
        {
            return ContentRect(sprite).size;
        }

        // прямоугольник контента в юнитах спрайта: позиция — относительно центра
        // bounds (у центрированного арта это (0,0)), размер — габариты по альфе;
        // нечитаемая текстура или пустой спрайт — фолбэк на всю канву
        public static Rect ContentRect(Sprite sprite)
        {
            if (sprite == null)
                return Rect.zero;
            if (cache.TryGetValue(sprite, out Rect cached))
                return cached;

            Rect rect;
            Texture2D tex = sprite.texture;
            if (tex == null || !tex.isReadable)
            {
                // центр канвы = ноль относительно пивота (спрайт рисуется по центру);
                // Rect(0,0,size) давал content.center = size/2 — корпус уезжал влево-вниз
                Vector2 size = sprite.bounds.size;
                rect = new Rect(-size * 0.5f, size);
            }
            else
            {
                Rect frame = sprite.textureRect;
                int x0 = Mathf.RoundToInt(frame.x), y0 = Mathf.RoundToInt(frame.y);
                int w = Mathf.RoundToInt(frame.width), h = Mathf.RoundToInt(frame.height);
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
                {
                    rect = new Rect(Vector2.zero, sprite.bounds.size); // пустой спрайт — как было
                }
                else
                {
                    float ppu = sprite.pixelsPerUnit;
                    // центр контента относительно центра спрайт-rect (он же локальный
                    // ноль пивота 0.5/0.5). Скан идёт в координатах ректа, поэтому
                    // x0/y0 в вычитании не участвуют: лишний сдвиг на x0 разворачивал
                    // корпус вправо-вниз у всех спрайтов с обрезанным texRect
                    // (у яйца/скрепки texRect.x = 0 — потому они были ровными)
                    Vector2 center = new Vector2(
                        (minX + maxX + 1) * 0.5f - w * 0.5f,
                        (minY + maxY + 1) * 0.5f - h * 0.5f) / ppu;
                    Vector2 size = new Vector2(maxX - minX + 1, maxY - minY + 1) / ppu;
                    rect = new Rect(center - size * 0.5f, size);
                }
            }

            cache[sprite] = rect;
            return rect;
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
