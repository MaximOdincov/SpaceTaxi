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

        // — палитра «бумага + чернила» (M7.5, скайрим-дисциплина R; ревизия M8.1/M8.7) —
        // Бумага — арт владельца (Art/background.png, тёплая сепия, не трогается) + фолбэки.
        // Тела — РЕАЛЬНЫЕ игровые спрайты в родных цветах (отмывка снята владельцем);
        // чернила — текст и служебная графика листа. Цель заказа — жёлтый
        // (MapScreenBinder.TargetRingColor); красный зарезервирован за смертью.
        public static readonly Color Ink = new Color(0.243f, 0.184f, 0.137f);     // #3E2F23 — основные чернила
        public static readonly Color InkSoft = new Color(0.420f, 0.329f, 0.235f); // #6B543C — вторичные (периферия, пояса, кромка)
        // M8.7: янтарный акцент ВЫВЕДЕН из употребления (владелец вернул жёлтый
        // цели — MapScreenBinder.TargetRingColor); цвет оставлен в палитре как
        // вторичный, целью заказа больше не назначается
        public static readonly Color Accent = new Color(0.706f, 0.325f, 0.035f);  // #B45309 — жжёный янтарь
        public static readonly Color Alarm = new Color(0.549f, 0.169f, 0.118f);   // #8C2B1E — красный: ТОЛЬКО смерть
        public static readonly Color Paper = new Color(0.89f, 0.80f, 0.59f);
        public static readonly Color PaperDark = new Color(0.80f, 0.70f, 0.50f);
        public static readonly Color Desk = new Color(0.115f, 0.088f, 0.062f, 0.985f); // тёмный стол вокруг листа

        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        // Лист/записка: Sprite-ресурс, иначе полная текстура, иначе null (фолбэк — цвет Paper)
        public static Sprite Sheet()
        {
            return LoadSingle("paper_sheet");
        }


        // Жёлтая бумага карты — фон владельца Art/background.png (Resources/Art).
        // null — фолбэк: ровный цвет Paper.
        public static Sprite SheetArt()
        {
            return LoadArt("background");
        }

        // M8.3: бесшовный тайл бумаги для Image.Tiled — кроп 512×512 из
        // Art/background.png (генерируется из чистой центральной зоны листа с
        // перекрёстным затуханием краёв; ассет Assets/Resources/Art/map/paper_tile.png).
        // Тайлится Image'ом карты с pixelsPerUnitMultiplier = 1/scale — один тайл
        // всегда 512 мировых юнитов, зерно не «приближается» вместе с листом.
        // PPU спрайта 100 = канонический referencePixelsPerUnit канваса
        public static Sprite PaperTile()
        {
            string key = "paper_tile";
            if (cache.TryGetValue(key, out Sprite s) && s != null)
                return s;
            Texture2D tex = Resources.Load<Texture2D>("Art/map/paper_tile");
            if (tex == null)
                return null;
            s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            cache[key] = s;
            return s;
        }

        // Иконки тел владельца из Assets/Resources/Art/map: map_planet, map_sun,
        // map_black_hole, map_gas_station, map_repair, map_taxi_planet,
        // map_asteroid, map_rocket, map_cometa, map_for_fun_1/2.
        // Импорт бывает Single/Multiple — пробуем Sprite, потом "<имя>_0", потом текстуру.
        public static Sprite MapSprite(string name)
        {
            return LoadArt("map/" + name);
        }

        private static Sprite LoadArt(string relativeName)
        {
            string key = "art:" + relativeName;
            if (cache.TryGetValue(key, out Sprite s) && s != null)
                return s;
            string path = "Art/" + relativeName;
            s = Resources.Load<Sprite>(path);
            if (s == null)
                s = Resources.Load<Sprite>(path + "_0");
            if (s == null)
            {
                Texture2D tex = Resources.Load<Texture2D>(path);
                if (tex != null)
                    s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
            }
            cache[key] = s;
            return s;
        }

        // Ячейка коллажа map_icons (0..8) или map_stamps (0..5); null — арта нет
        public static Sprite Icon(int cell)
        {
            return GridCell("map_icons", IconGrid, IconGrid, cell);
        }


        // — процедурные карандашные спрайты (кэш в статике) —
        // Н5.21-5: пунктирные кольца-линии (орбиты, сеть, пояс, штриховка) убраны
        // вместе с их генераторами — остались только ореолы и штрих разделителя.

    // сплошной карандашный обвод (цель заказа, ореолы)
    public static Sprite TargetRing()
    {
        return RingSprite("target", 256, 0.94f, 0.99f, 1f, 1f, 0.012f);
    }



    // комета: кружок-голова с двойным контуром + штриховой хвост (голова смотрит вправо — по курсу)
    public static Sprite CometIcon()
    {
        if (cache.TryGetValue("comet", out Sprite s) && s != null)
            return s;
        var tex = StubSprites.CreateMaskTexture(128, (x, y) =>
        {
            float hx = x - 0.45f, hy = y + 0.02f;
            float hr = Mathf.Sqrt(hx * hx + hy * hy);
            float ha = Mathf.Atan2(hy, hx);
            float hw = 1f + 0.05f * Mathf.Sin(3f * ha + 1.1f);
            float hrr = hr / hw;
            if (Mathf.Abs(hrr - 0.22f) < 0.028f || Mathf.Abs(hrr - 0.15f) < 0.018f)
                return true; // двойной контур головы
            if (x < 0.20f && x > -0.92f) // хвост: расходящийся клин со штрихами
            {
                float spread = 0.02f + 0.11f * (x + 0.92f);
                bool edge = Mathf.Abs(Mathf.Abs(y) - spread) < 0.022f;
                bool hatch = Mathf.Abs(y) < spread && Mathf.Repeat((x - y) * 4.5f, 1f) < 0.5f;
                if (edge || hatch)
                    return true;
            }
            return false;
        });
        s = Sprite.Create(tex, new Rect(0f, 0f, 128f, 128f), new Vector2(0.5f, 0.5f), 128f);
        cache["comet"] = s;
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

        // чернильная клякса-уголок: неровное пятно для соединений рамок «от руки»
        public static Sprite InkDot()
        {
            if (cache.TryGetValue("inkdot", out Sprite s) && s != null)
                return s;
            var tex = StubSprites.CreateMaskTexture(64, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float wob = 1f + 0.10f * Mathf.Sin(Mathf.Atan2(y, x) * 5f + 2.1f);
                return r <= 0.34f * wob;
            });
            s = Sprite.Create(tex, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
            cache["inkdot"] = s;
            return s;
        }

        // маленькая звёздочка-отметка: буи на границе мира
        public static Sprite BuoyStar()
        {
            if (cache.TryGetValue("buoystar", out Sprite s) && s != null)
                return s;
            var tex = StubSprites.CreateMaskTexture(64, (x, y) =>
            {
                float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
                return (ax <= 0.055f && ay <= 0.52f) || (ay <= 0.055f && ax <= 0.52f)
                    || ax + ay <= 0.16f;
            });
            s = Sprite.Create(tex, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
            cache["buoystar"] = s;
            return s;
        }

        // край мира (M7.5): туманная кромка — бумага «растворяется» к границе.
        // RGBA-текстура с плавной альфой (не бинарный крап CreateMaskTexture:
        // при зуме 9 один техель крапа — 34px блоб), лёгкая мраморность синусами
        // — читается как пятно старой бумаги на любом зуме. ВНУТРЕННИЙ край
        // полосы лежит на доле EdgeFogBorderFrac радиуса спрайта — вызывающий
        // счcитает картинку так, чтобы растворение начиналось ЗАРАНЕЕ, до
        // кольца буёв (пан-клампы листа не дают заглянуть дальше ~R, Н6.HF1)
        public const float EdgeFogBorderFrac = 0.575f;
        public static Sprite EdgeFog()
        {
            if (cache.TryGetValue("edgefog", out Sprite s) && s != null)
                return s;
            const int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];
            for (int j = 0; j < size; j++)
            {
                for (int i = 0; i < size; i++)
                {
                    float x = (i + 0.5f) / size * 2f - 1f;
                    float y = (j + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(x * x + y * y);
                    float t = (r - EdgeFogBorderFrac) / (1f - EdgeFogBorderFrac);
                    float a = 0f;
                    if (t > 0f)
                    {
                        float ramp = Mathf.SmoothStep(0f, 1f, t); // плавный набор плотности
                        a = 0.85f * ramp * Mottle(x, y);
                    }
                    pixels[j * size + i] = new Color(1f, 1f, 1f, a); // тинтует Image (Ink)
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            cache["edgefog"] = s;
            return s;
        }

        // плавная «мраморность» [0.5..1]: сумма несоизмеримых синусов — без
        // решётки и без блобов при растяжении текстуры
        private static float Mottle(float x, float y)
        {
            float n = Mathf.Sin(x * 8.3f + y * 3.1f) * 0.5f
                    + Mathf.Sin(x * 17.7f - y * 11.3f + 2.1f) * 0.3f
                    + Mathf.Sin(x * 5.1f - y * 23.7f + 4.2f) * 0.2f;
            return Mathf.Clamp01(0.75f + 0.25f * n);
        }


        // — внутреннее —
        // (курсор-pointer убран: Н6.K8 — курсор системный всегда)

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
