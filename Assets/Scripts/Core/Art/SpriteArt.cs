using System.Collections.Generic; // кэш габаритов
using UnityEngine; // спрайты и текстуры

namespace SpaceTaxi.Core
{
    // Фактические границы контента спрайта (по альфе): пользовательский арт не
    // заполняет канву — если масштабировать по прямоугольнику кадра, видимый
    // объект меньше коллайдера/радиуса. Кэш в статике, текстуры планет/станций
    // читаемые (isReadable в метах).
    public static class SpriteArt // статический калькулятор габаритов арта
    {
        private const byte AlphaThreshold = 8; // почти нулевая альфа: хвост касается видимого края

        private static readonly Dictionary<Sprite, Rect> cache = new Dictionary<Sprite, Rect>(); // расчёт один раз на спрайт

        // размер контента в юнитах спрайта (PPU); нечитаемая текстура — фолбэк на bounds
        public static Vector2 ContentSize(Sprite sprite) // габариты видимого контента
        {
            return ContentRect(sprite).size; // из прямоугольника контента
        }

        // прямоугольник контента в юнитах спрайта: позиция — относительно центра
        // bounds (у центрированного арта это (0,0)), размер — габариты по альфе;
        // нечитаемая текстура или пустой спрайт — фолбэк на всю канву
        public static Rect ContentRect(Sprite sprite) // точные границы непрозрачных пикселей
        {
            if (sprite == null) // пустой запрос
                return Rect.zero;
            if (cache.TryGetValue(sprite, out Rect cached)) // уже считали — из кэша
                return cached;

            Rect rect; // результат расчёта
            Texture2D tex = sprite.texture; // текстура спрайта
            if (tex == null || !tex.isReadable) // пиксели недоступны
            {
                // центр канвы = ноль относительно пивота (спрайт рисуется по центру);
                // Rect(0,0,size) давал content.center = size/2 — корпус уезжал влево-вниз
                Vector2 size = sprite.bounds.size; // габариты из bounds
                rect = new Rect(-size * 0.5f, size); // центрированный прямоугольник
            }
            else
            {
                Rect frame = sprite.textureRect; // область спрайта в текстуре (атлас)
                int x0 = Mathf.RoundToInt(frame.x), y0 = Mathf.RoundToInt(frame.y); // смещение области
                int w = Mathf.RoundToInt(frame.width), h = Mathf.RoundToInt(frame.height); // размер области
                Color32[] pixels = tex.GetPixels32(); // копия пикселей (один раз, потом кэш)
                int minX = w, maxX = -1, minY = h, maxY = -1; // границы непрозрачного
                for (int y = 0; y < h; y++) // обход строк области
                {
                    int row = (y0 + y) * tex.width; // строка в полной текстуре
                    for (int x = 0; x < w; x++) // обход столбцов области
                    {
                        if (pixels[row + x0 + x].a <= AlphaThreshold) // прозрачный пиксель
                            continue;
                        if (x < minX) minX = x; // левая граница контента
                        if (x > maxX) maxX = x; // правая граница
                        if (y < minY) minY = y; // нижняя граница
                        if (y > maxY) maxY = y; // верхняя граница
                    }
                }
                if (maxX < minX || maxY < minY) // непрозрачных пикселей нет
                {
                    rect = new Rect(Vector2.zero, sprite.bounds.size); // пустой спрайт — исходные bounds
                }
                else
                {
                    float ppu = sprite.pixelsPerUnit; // пикселей на юнит
                    // центр контента относительно центра спрайт-rect (он же локальный
                    // ноль пивота 0.5/0.5). Скан идёт в координатах ректа, поэтому
                    // x0/y0 в вычитании не участвуют: лишний сдвиг на x0 сместил бы
                    // корпус вправо-вниз у спрайтов с обрезанным texRect
                    Vector2 center = new Vector2( // центр видимого контента
                        (minX + maxX + 1) * 0.5f - w * 0.5f, // середина по X в пикселях ректа
                        (minY + maxY + 1) * 0.5f - h * 0.5f) / ppu; // середина по Y, перевод в юниты
                    Vector2 size = new Vector2(maxX - minX + 1, maxY - minY + 1) / ppu; // габариты в юнитах
                    rect = new Rect(center - size * 0.5f, size); // прямоугольник с левым-нижним углом
                }
            }

            cache[sprite] = rect; // в кэш — повторные запросы мгновенны
            return rect;
        }

        // масштаб, при котором контент спрайта занимает заданную мировую ширину
        public static float ScaleForWorldSize(Sprite sprite, float worldSize, float currentScale) // подбор масштаба
        {
            Vector2 content = ContentSize(sprite); // габариты контента
            float dim = Mathf.Max(content.x, content.y); // бо́льшая сторона
            if (dim <= 0.0001f || currentScale <= 0.0001f) // защита от деления на ноль
                return 1f;
            return worldSize / (dim * currentScale); // нужный множитель масштаба
        }
    }
}
