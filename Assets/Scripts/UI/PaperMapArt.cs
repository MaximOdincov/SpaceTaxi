using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.Core;

namespace SpaceTaxi.UI
{
    // Арт «экспедиционной бумаги»: загрузка из Resources + процедурные фолбэки.
    // Канонические пути для владельца (PNG импортировать как Sprite, у коллажей — Multiple):
    //   Assets/Resources/Map/paper_sheet.png  (1024×1024) — лист карты
    //   Assets/Resources/Map/paper_note.png   (512×384)   — записка/легенда
    //   Assets/Resources/Map/map_icons.png    (коллаж 3×3, ячейки слева направо сверху вниз:
    //                                          0 планета, 1 звезда, 2 чёрная дыра, 3 пояс,
    //                                          4 комета, 5 буй, 6 корабль, 7 цель, 8 метка)
    //   Assets/Resources/Map/map_stamps.png   (коллаж 2×3: 0 заправка, 1 мастерская, 2 хаб,
    //                                          3 метка-waypoint, 4 потеря связи, 5 резерв)
    // Нет ресурса — рисуем «карандашом» кодом, карта остаётся рабочей.
    public static class PaperMapArt
    {
        public const int IconGrid = 3;   // map_icons: 3×3
        public const int StampGridX = 2; // map_stamps: 2×3
        public const int StampGridY = 3;

        // — палитра тёплых карандашей —
        public static readonly Color Ink = new Color(0.24f, 0.19f, 0.15f);
        public static readonly Color Paper = new Color(0.89f, 0.80f, 0.59f);
        public static readonly Color PaperDark = new Color(0.80f, 0.70f, 0.50f);
        public static readonly Color Night = new Color(0.10f, 0.09f, 0.11f);
        public static readonly Color Rust = new Color(0.68f, 0.32f, 0.17f);   // акцент: метки/тревога
        public static readonly Color Graphite = new Color(0.44f, 0.41f, 0.37f);
        public static readonly Color StarPencil = new Color(0.83f, 0.60f, 0.25f);
        public static readonly Color HolePencil = new Color(0.15f, 0.12f, 0.16f);
        public static readonly Color BeltPencil = new Color(0.45f, 0.40f, 0.34f, 0.6f);

        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        // Лист/записка: Sprite-ресурс, иначе полная текстура, иначе null (фолбэк — цвет Paper)
        public static Sprite Sheet()
        {
            return LoadSingle("paper_sheet");
        }

        public static Sprite Note()
        {
            return LoadSingle("paper_note");
        }

        // Ячейка коллажа map_icons (0..8) или map_stamps (0..5); null — арта нет
        public static Sprite Icon(int cell)
        {
            return GridCell("map_icons", IconGrid, IconGrid, cell);
        }

        public static Sprite Stamp(int cell)
        {
            return GridCell("map_stamps", StampGridX, StampGridY, cell);
        }

        // — процедурные карандашные спрайты (кэш в статике) —

        // тонкий частый пунктир орбит
        public static Sprite OrbitRing()
        {
            return RingSprite("orbit", 256, 0.955f, 0.985f, 0.55f, 90f, 0.004f);
        }

        // пунктир сети буёв / кометной зоны
        public static Sprite NetworkRing()
        {
            return RingSprite("network", 256, 0.95f, 0.99f, 0.62f, 44f, 0.006f);
        }

        // штриховая каракуля-дуга пояса
        public static Sprite BeltRing()
        {
            return RingSprite("belt", 256, 0.93f, 0.985f, 0.5f, 28f, 0.01f);
        }

        // сплошной карандашный обвод (цель заказа)
        public static Sprite TargetRing()
        {
            return RingSprite("target", 256, 0.94f, 0.99f, 1f, 1f, 0.012f);
        }

        // штриховка снаружи сети (зона потери связи)
        public static Sprite HatchRing()
        {
            if (cache.TryGetValue("hatch", out Sprite s) && s != null)
                return s;
            var tex = StubSprites.CreateMaskTexture(256, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float wob = 1f + 0.008f * Mathf.Sin(Mathf.Atan2(y, x) * 4f + 0.7f);
                r /= wob;
                if (r < 0.86f || r > 1f)
                    return false;
                float stripe = Mathf.Repeat((x + y) * 16f, 1f); // диагональная штриховка
                return stripe < 0.55f;
            });
            s = Sprite.Create(tex, new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f), 256f);
            cache["hatch"] = s;
            return s;
        }

        // метка-waypoint: булавка (головка + остриё)
        public static Sprite Pin()
        {
            if (cache.TryGetValue("pin", out Sprite s) && s != null)
                return s;
            var tex = StubSprites.CreateMaskTexture(128, (x, y) =>
            {
                float dx = x, dy = y - 0.35f;
                if (dx * dx + dy * dy <= 0.32f * 0.32f)
                    return true; // головка
                return y > -0.92f && y < 0.06f && Mathf.Abs(x) <= (y + 0.92f) * 0.36f; // остриё
            });
            s = Sprite.Create(tex, new Rect(0f, 0f, 128f, 128f), new Vector2(0.5f, 0.5f), 128f);
            cache["pin"] = s;
            return s;
        }

        // карандашная рамка (кнопки/записка): прозрачная середина, рваный контур
        public static Sprite InkFrame()
        {
            if (cache.TryGetValue("inkframe", out Sprite s) && s != null)
                return s;
            var tex = StubSprites.CreateMaskTexture(128, (x, y) =>
            {
                float d = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
                float wob = 0.015f * Mathf.Sin(Mathf.Atan2(y, x) * 5f + 2f)
                          + 0.01f * Mathf.Sin(9f * x + 3f * y);
                d += wob;
                return d >= 0.86f && d <= 0.96f;
            });
            // border задаётся только при создании (Sprite.border — read only)
            s = Sprite.Create(tex, new Rect(0f, 0f, 128f, 128f), new Vector2(0.5f, 0.5f), 128f,
                0, SpriteMeshType.FullRect, new Vector4(16f, 16f, 16f, 16f)); // 9-slice: 16+16 < высота кнопки 40
            cache["inkframe"] = s;
            return s;
        }

        // курсор-pointer на hover: белая стрелка с тёмной кромкой
        public static Texture2D CursorTexture()
        {
            if (cache.TryGetValue("cursor_tex", out Sprite s) && s != null)
                return s.texture;
            const int size = 24;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var pixels = new Color32[size * size];
            Vector2 a = new Vector2(4f, 22f), b = new Vector2(4f, 3f), c = new Vector2(19f, 13f);
            Vector2 centroid = (a + b + c) / 3f;
            for (int iy = 0; iy < size; iy++)
            {
                for (int ix = 0; ix < size; ix++)
                {
                    var p = new Vector2(ix + 0.5f, iy + 0.5f);
                    Color32 col = new Color32(0, 0, 0, 0);
                    if (InTriangle(p, a, b, c))
                    {
                        var inner = centroid + (p - centroid) / 0.68f;
                        bool edge = !InTriangle(inner, a, b, c);
                        col = edge
                            ? new Color32(35, 28, 22, 255)
                            : (Color32)new Color(0.95f, 0.93f, 0.86f, 1f);
                    }
                    pixels[iy * size + ix] = col;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            s = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            cache["cursor_tex"] = s;
            return tex;
        }

        public static Vector2 CursorHotspot => new Vector2(4f, 20f);

        // — внутреннее —

        private static Sprite LoadSingle(string name)
        {
            string key = "res:" + name;
            if (cache.TryGetValue(key, out Sprite s) && s != null)
                return s;
            s = Resources.Load<Sprite>("Map/" + name);
            if (s == null)
            {
                Texture2D tex = Resources.Load<Texture2D>("Map/" + name);
                if (tex != null) // импорт не в Sprite — режем целиком
                    s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
            }
            cache[key] = s;
            return s; // null — арта нет
        }

        private static Sprite GridCell(string name, int cols, int rows, int cell)
        {
            string key = name + ":" + cell;
            if (cache.TryGetValue(key, out Sprite s) && s != null)
                return s;
            Texture2D tex = Resources.Load<Texture2D>("Map/" + name);
            if (tex == null)
                return null;
            int col = cell % cols, row = cell / cols;
            float cw = (float)tex.width / cols, ch = (float)tex.height / rows;
            var rect = new Rect(col * cw, tex.height - (row + 1) * ch, cw, ch); // ряды сверху вниз
            s = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), cw);
            cache[key] = s;
            return s;
        }

        // кольцо: band [inner,outer], duty/period — пунктир по углу, wobble — дрожь руки
        private static Sprite RingSprite(string key, int size, float inner, float outer, float duty, float period, float wobble)
        {
            if (cache.TryGetValue(key, out Sprite s) && s != null)
                return s;
            var tex = StubSprites.CreateMaskTexture(size, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float wob = 1f + wobble * Mathf.Sin(Mathf.Atan2(y, x) * 3f + 1.3f);
                r /= wob;
                if (r < inner || r > outer)
                    return false;
                if (duty >= 1f)
                    return true;
                float t = Mathf.Repeat(Mathf.Atan2(y, x) / (2f * Mathf.PI) * period, 1f);
                return t < duty;
            });
            s = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            cache[key] = s;
            return s;
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s1 = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            float s2 = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x);
            float s3 = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);
            bool neg = s1 < 0f || s2 < 0f || s3 < 0f;
            bool pos = s1 > 0f || s2 > 0f || s3 > 0f;
            return !(neg && pos);
        }
    }
}
