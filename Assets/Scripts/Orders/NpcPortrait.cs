using UnityEngine;

namespace SpaceTaxi.Orders
{
    // Портрет пассажира: с шансом 50% — готовый NPC из Resources/Art/npc/ready
    // (диспетчер исключён — отдельный персонаж), иначе сборка из слоёв 500×500:
    // тело (случайный цвет) + одежда (случайный цвет) + глаза + рот (без цвета).
    // Генерация детерминирована сидом — сейв восстанавливает портрет по
    // PortraitSeed (SaveSystem), цвета запечены в текстуру.
    public static class NpcPortrait
    {
        private const float ReadyChance = 0.5f;
        private const string DispatcherName = "dispetcher";

        private static Sprite[] ready, bodies, clothes, eyes, mouths;

        private static readonly Color[] BodyTints =
        {
            ColorRGB(0xE8, 0xB2, 0x7D), // песочный
            ColorRGB(0xD9, 0x8C, 0x6B), // терракота
            ColorRGB(0xC9, 0x6B, 0x7E), // пыльная роза
            ColorRGB(0x7D, 0xA9, 0xD9), // небесно-голубой
            ColorRGB(0x7D, 0xC9, 0xB4), // мятный
            ColorRGB(0xA0, 0x8C, 0xD9), // лавандовый
            ColorRGB(0x8F, 0xA3, 0xB8), // серо-голубой
            ColorRGB(0xB5, 0xC9, 0x6B), // фисташковый
        };

        private static readonly Color[] ClothesTints =
        {
            ColorRGB(0xC0, 0x49, 0x3D), // красный
            ColorRGB(0x2E, 0x5E, 0xAA), // синий
            ColorRGB(0x3D, 0x7A, 0x45), // зелёный
            ColorRGB(0x8A, 0x4E, 0xA8), // фиолетовый
            ColorRGB(0xD8, 0x8C, 0x2A), // оранжевый
            ColorRGB(0x2A, 0x8C, 0x8C), // бирюзовый
            ColorRGB(0xB0, 0x3A, 0x6E), // малиновый
            ColorRGB(0x5A, 0x5A, 0x66), // графит
        };

        public static Sprite Generate(int seed)
        {
            var rnd = new System.Random(seed);
            Sprite[] readyPool = Pool(ref ready, "Art/npc/ready");
            if (readyPool != null && rnd.NextDouble() < ReadyChance)
            {
                Sprite picked = readyPool[rnd.Next(readyPool.Length)];
                if (picked != null && picked.name != DispatcherName)
                    return Trimmed(picked);
                // выпал диспетчер — соберём случайного вместо него
            }
            return Compose(rnd);
        }

        private static Sprite Compose(System.Random rnd)
        {
            Sprite body = Pick(ref bodies, "Art/npc/body", rnd);
            if (body == null)
                return null; // слоёв нет — UI покажет цветную заглушку
            Sprite clothesSprite = Pick(ref clothes, "Art/npc/clothes", rnd);
            Sprite eyesSprite = Pick(ref eyes, "Art/npc/eyes", rnd);
            Sprite mouthSprite = Pick(ref mouths, "Art/npc/mouth", rnd);

            // канва = нативный размер слоёв (все нарезаны одним кадром):
            // пиксель в пиксель, никаких масштабирований
            Texture2D baseTex = body.texture;
            int w = baseTex.width, h = baseTex.height;
            var dst = new Color32[w * h];
            Blit(body, BodyTints[rnd.Next(BodyTints.Length)], dst);
            Blit(clothesSprite, ClothesTints[rnd.Next(ClothesTints.Length)], dst);
            Blit(eyesSprite, Color.white, dst); // глаза и рот не красим
            Blit(mouthSprite, Color.white, dst);

            // фигура должна заполнять rect спрайта: аватар в UI строится по
            // аспекту спрайта, прозрачные поля канвы — мёртвые зоны рамки
            Rect bounds = AlphaBounds(dst, w, h, new Rect(0f, 0f, w, h));
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(dst);
            tex.Apply(false, false);
            return Sprite.Create(tex, bounds, new Vector2(0.5f, 0.5f), w);
        }

        // --- Обрезка прозрачных полей ---

        // порог альфы: срезаем прозрачные поля и полупрозрачную «пыль» краёв
        // (32 — отсекает туман/антиалиас фигур, тонкие конечности не рвутся)
        private const byte AlphaThreshold = 32;

        // готовые NPC — ассеты на общей канве с полями; трим в момент выдачи
        // (текстуры readable, пересчёт дешёвый), кэш по имени исходника —
        // один портрет всегда даёт один и тот же обрезанный спрайт
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> trimmedCache =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        private static Sprite Trimmed(Sprite source)
        {
            if (source == null)
                return null;
            if (trimmedCache.TryGetValue(source.name, out Sprite cached) && cached != null)
                return cached;

            Texture2D tex = source.texture;
            if (!tex.isReadable)
                return source; // нечитаемая — покажем как есть (крайний случай)
            Rect area = source.textureRect;
            Color32[] px = tex.GetPixels32();
            Rect bounds = AlphaBounds(px, tex.width, tex.height, area);
            if (bounds.width <= 0f || bounds.height <= 0f)
                return source; // фигура не найдена — покажем как есть

            // PPU исходника сохраняет масштаб; имя не меняем — кэш по нему
            Sprite result = Sprite.Create(tex, bounds, new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
            result.name = source.name;
            trimmedCache[source.name] = result;
            return result;
        }

        // границы непрозрачных пикселей в области area (координаты в пространстве
        // текстуры); не нашли ни одного пикселя — вернём пустой rect, вызывающий
        // откатит трим
        private static Rect AlphaBounds(Color32[] px, int w, int h, Rect area)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            int x0 = Mathf.Max(0, (int)area.x);
            int y0 = Mathf.Max(0, (int)area.y);
            int x1 = Mathf.Min(w, x0 + (int)area.width);
            int y1 = Mathf.Min(h, y0 + (int)area.height);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    if (px[y * w + x].a > AlphaThreshold)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            if (maxX < 0)
                return Rect.zero;
            return Rect.MinMaxRect(minX, minY, maxX + 1, maxY + 1);
        }

        // слой поверх dst пиксель-в-пиксель (размер канвы = размеру слоёв),
        // rgb с тином, обычная альфа-композиция «over»; слой другого размера
        // пропускается — масштабировать слои нельзя, нарезка фиксированная
        private static void Blit(Sprite layer, Color tint, Color32[] dst)
        {
            if (layer == null)
                return;
            Texture2D tex = layer.texture;
            if (!tex.isReadable)
                return;
            if (tex.width * tex.height != dst.Length)
            {
                Debug.LogWarning($"[NPC] Слой «{layer.name}» {tex.width}×{tex.height} не совпадает с канвой — пропущен");
                return;
            }
            Color32[] src = tex.GetPixels32();
            for (int i = 0; i < src.Length; i++)
            {
                Color32 c = src[i];
                if (c.a == 0)
                    continue;
                float sa = c.a / 255f;
                float da = dst[i].a / 255f;
                float oa = sa + da * (1f - sa);
                if (oa <= 0f)
                    continue;
                byte r = (byte)((c.r * tint.r * sa + dst[i].r * da * (1f - sa)) / oa);
                byte g = (byte)((c.g * tint.g * sa + dst[i].g * da * (1f - sa)) / oa);
                byte b = (byte)((c.b * tint.b * sa + dst[i].b * da * (1f - sa)) / oa);
                dst[i] = new Color32(r, g, b, (byte)(oa * 255f));
            }
        }

        private static Sprite Pick(ref Sprite[] field, string path, System.Random rnd)
        {
            Sprite[] pool = Pool(ref field, path);
            return pool != null ? pool[rnd.Next(pool.Length)] : null;
        }

        private static Sprite[] Pool(ref Sprite[] field, string path)
        {
            if (field == null)
            {
                field = Resources.LoadAll<Sprite>(path);
                if (field == null || field.Length == 0)
                {
                    Debug.LogWarning("[NPC] Пусто: Assets/Resources/" + path);
                    field = new Sprite[0];
                }
            }
            return field.Length > 0 ? field : null;
        }

        private static Color ColorRGB(int r, int g, int b)
        {
            return new Color32((byte)r, (byte)g, (byte)b, 255);
        }
    }
}
