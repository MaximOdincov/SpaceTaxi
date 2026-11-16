using System.Collections.Generic; // словари-кэши спрайтов
using UnityEngine; // текстуры и спрайты

namespace SpaceTaxi.Core
{
    // Процедурные спрайты-заглушки: всё, что рисуется без арта. Диаметр запекается
    // в PPU — спрайт занимает `diameter` мировых юнитов при localScale = 1
    public static class StubSprites // статическая фабрика белых спрайтов (тонировка цветом)
    {
        private static readonly Dictionary<int, Sprite> circles = new Dictionary<int, Sprite>(); // кэш кругов
        private static readonly Dictionary<int, Sprite> rings = new Dictionary<int, Sprite>(); // кэш колец
        private static readonly Dictionary<int, Sprite> softDots = new Dictionary<int, Sprite>(); // кэш мягких точек
        private static readonly Dictionary<int, Sprite> sparkles = new Dictionary<int, Sprite>(); // кэш искр
        private static readonly Dictionary<int, Sprite> clusters = new Dictionary<int, Sprite>(); // кэш скоплений
        private static readonly Dictionary<int, Sprite> zones = new Dictionary<int, Sprite>(); // кэш зон
        private static readonly Dictionary<int, Sprite> starBodies = new Dictionary<int, Sprite>(); // кэш звёзд
        private static readonly Dictionary<int, Sprite> holeCores = new Dictionary<int, Sprite>(); // кэш ядер дыры
        private static Sprite shipTriangle; // треугольник корабля-заглушки (один)
        private static Sprite line; // капсула-линия (одна)

        public static Sprite Circle(float diameter) // жёсткий круг заданного диаметра
        {
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter)); // ключ кэша — округлённый диаметр
            if (!circles.TryGetValue(key, out Sprite sprite) || sprite == null) // нет в кэше
            {
                var tex = CreateCircleTexture(64, 0.08f); // текстура 64px с тонкой обводкой
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f / diameter); // PPU под диаметр
                circles[key] = sprite; // в кэш
            }
            return sprite;
        }

        // тонкое посадочное кольцо: одна маска на все радиусы (как BeltRing в карте),
        // PPU подгоняет спрайт под диаметр в юнитах
        public static Sprite Ring(float diameter) // тонкое кольцо
        {
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter)); // ключ кэша
            if (!rings.TryGetValue(key, out Sprite sprite) || sprite == null) // нет в кэше
            {
                var tex = CreateMaskTexture(256, (x, y) => // бинарная маска 256px
                {
                    float d = Mathf.Sqrt(x * x + y * y); // расстояние от центра
                    return d <= 1f && d >= 0.96f; // кольцо шириной 4% радиуса
                });
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f), 256f / diameter); // PPU под диаметр
                rings[key] = sprite; // в кэш
            }
            return sprite;
        }

        // мягкая точка без тёмной окантовки: пыль, далёкие светила. core — доля
        // радиуса с полной альфой (0 = полностью мягкая, 0.7 = горячее ядро)
        public static Sprite SoftDot(float diameter, float core = 0f, int textureSize = 64) // диск с мягкой кромкой
        {
            int key = Mathf.RoundToInt(diameter) * 100000 + Mathf.RoundToInt(core * 10f) * 1000 + Mathf.Clamp(textureSize, 16, 2048); // ключ включает размер текстуры (Н9.1)
            if (!softDots.TryGetValue(key, out Sprite sprite) || sprite == null) // нет в кэше
            {
                int size = Mathf.Clamp(textureSize, 16, 2048); // разрешение текстуры
                var tex = CreateSoftDotTexture(size, core); // градиент кромки в пикселях размера
                sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), (float)size / diameter); // PPU под диаметр
                softDots[key] = sprite; // в кэш
            }
            return sprite;
        }

        // мягкая зона эффекта (жар звезды, сияние дыры): плато от центра до core
        // (доля радиуса), дальше гладкий спад ровно в ноль на краю канвы — без
        // ступеней и квадратного края. Канва = диаметр зоны (PPU = size/diameter),
        // видимый край совпадает с логическим радиусом (HeatRadius/EffectRadius)
        public static Sprite Zone(float diameter, float core = 0.32f) // полупрозрачная зона эффекта
        {
            core = Mathf.Clamp(core, 0f, 0.95f); // плато не на всю канву
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter)) * 1000 + Mathf.RoundToInt(core * 100f); // ключ кэша
            if (!zones.TryGetValue(key, out Sprite sprite) || sprite == null) // нет в кэше
            {
                var tex = CreateZoneTexture(512, core); // текстура 512px с smoothstep-спадом
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 512f, 512f), new Vector2(0.5f, 0.5f), 512f / diameter); // PPU под диаметр
                zones[key] = sprite; // в кэш
            }
            return sprite;
        }

        // тело звезды: яркое ядро с плавной экспоненциальной короной — без лучей,
        // ступеней и тёмной обводки круга-заглушки. У края канвы корона доходит
        // ДО НУЛЯ (StarFadeStart): раньше хвост 0.05 читался квадратной кромкой
        // канвы на тёмном фоне (владелец Н9.3: «звезда квадратная»); контент-бокс
        // ~0.96 канвы — CelestialBody.Init компенсирует масштабом ~×1.04
        public static Sprite StarBody(float diameter) // тело звезды
        {
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter)); // ключ кэша
            if (!starBodies.TryGetValue(key, out Sprite sprite) || sprite == null) // нет в кэше
            {
                var tex = CreateStarTexture(256); // текстура звезды 256px
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f), 256f / diameter); // PPU под диаметр
                starBodies[key] = sprite; // в кэш
            }
            return sprite;
        }

        // ядро чёрной дыры: чистый тёмный диск с ровной (но сглаженной) границей.
        // Спад у края канвы — выше альфа-порога SpriteArt: контент = вся канва,
        // масштаб тела = 1, зона эффекта сохраняет прежний мировой размер
        public static Sprite HoleCore(float diameter) // тёмный диск дыры
        {
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter)); // ключ кэша
            if (!holeCores.TryGetValue(key, out Sprite sprite) || sprite == null) // нет в кэше
            {
                var tex = CreateHoleCoreTexture(256); // текстура ядра 256px
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f), 256f / diameter); // PPU под диаметр
                holeCores[key] = sprite; // в кэш
            }
            return sprite;
        }

        // звезда с крестом-бликом: чёткое ядро + 4 сужающихся луча (не клякса)
        public static Sprite StarSparkle(float diameter) // искра для параллакса
        {
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter)); // ключ кэша
            if (!sparkles.TryGetValue(key, out Sprite sprite) || sprite == null) // нет в кэше
            {
                var tex = CreateSparkleTexture(128); // текстура искры 128px
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 128f, 128f), new Vector2(0.5f, 0.5f), 128f / diameter); // PPU под диаметр
                sparkles[key] = sprite; // в кэш
            }
            return sprite;
        }

        // компактное скопление из 3-5 чётких звёзд; сид фиксирует раскладку
        public static Sprite StarCluster(float diameter, int seed) // скопление звёзд по сиду
        {
            int key = Mathf.Max(2, Mathf.RoundToInt(diameter)) * 1000 + (seed & 1023); // ключ кэша с сидом
            if (!clusters.TryGetValue(key, out Sprite sprite) || sprite == null) // нет в кэше
            {
                var tex = CreateClusterTexture(128, seed); // текстура 128px с фиксированной раскладкой
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 128f, 128f), new Vector2(0.5f, 0.5f), 128f / diameter); // PPU под диаметр
                clusters[key] = sprite; // в кэш
            }
            return sprite;
        }

        public static Sprite ShipTriangle() // треугольник-заглушка корабля
        {
            if (shipTriangle == null) // создаётся один раз
                shipTriangle = Sprite.Create(CreateMaskTexture(192, InTriangle), new Rect(0f, 0f, 192f, 192f), new Vector2(0.5f, 0.5f), 192f / 14f); // 14 юнитов
            return shipTriangle;
        }

        // указатель цели у края экрана: классическая навигационная стрелка
        // с выемкой в хвосте (квадрат-заглушка ShipTriangle читался как «трапеция»)
        private static Sprite chevron; // кэш шеврона

        public static Sprite TargetChevron() // навигационная стрелка-фолбэк
        {
            if (chevron != null) // уже создан
                return chevron;
            chevron = Sprite.Create(CreateMaskTexture(128, (x, y) => // маска 128px
            {
                // треугольник (0,0.9)-(−0.75,−0.7)-(0.75,−0.7) минус хвостовая выемка
                bool body = y <= 0.9f && y >= -0.7f // корпус стрелки
                            && Mathf.Abs(x) <= 0.75f * (y + 0.7f) / 1.6f; // сужение к вершине
                bool notch = y < -0.15f // хвостовая выемка ниже линии
                             && Mathf.Abs(x) < 0.22f * (y + 0.7f) / 0.55f; // ширина выемки
                return body && !notch; // корпус без выемки
            }), new Rect(0f, 0f, 128f, 128f), new Vector2(0.5f, 0.5f), 128f / 3f); // 3 юнита
            return chevron;
        }

        public static Sprite Line() // капсула-линия (штрихи/сегменты)
        {
            if (line == null) // создаётся один раз
                line = Sprite.Create(CreateMaskTexture(256, InCapsule), new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f), 256f / 16f); // 16 юнитов
            return line;
        }

        public static bool InTriangle(float x, float y) // тест треугольника корабля
        {
            float s1 = (-1.7f) * y - 0.7f * (x - 1f); // первое полупространство
            float s2 = 0f * (y - 0.7f) - (-1.4f) * (x + 0.7f); // второе
            float s3 = 1.7f * (y + 0.7f) - 0f * (x + 0.7f); // третье
            return (s1 >= 0f && s2 >= 0f && s3 >= 0f) || (s1 <= 0f && s2 <= 0f && s3 <= 0f); // внутри всех трёх
        }

        public static bool InCapsule(float x, float y) // тест капсулы линии
        {
            float dx = Mathf.Max(Mathf.Abs(x) - 7f, 0f); // вырез «сегмента» капсулы
            return dx * dx + y * y <= 1f; // эллиптическая проверка
        }

        public static Texture2D CreateCircleTexture(int size, float outlineWidth) // круг с тёмной обводкой
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear }; // текстура с мипами
            var pixels = new Color32[size * size]; // буфер пикселей
            var fill = new Color32(255, 255, 255, 255); // белая заливка (тонировка цветом)
            var outline = new Color32(40, 40, 50, 255); // тёмный обод
            float pixel = 2f / size; // ширина одного текселя в норм. координатах
            for (int iy = 0; iy < size; iy++) // строки
            {
                for (int ix = 0; ix < size; ix++) // столбцы
                {
                    float x = (ix + 0.5f) / size * 2f - 1f; // нормализованный X (-1..1)
                    float y = (iy + 0.5f) / size * 2f - 1f; // нормализованный Y
                    float dist = Mathf.Sqrt(x * x + y * y); // расстояние от центра
                    float alpha = Mathf.Clamp01((1f - dist) / pixel + 0.5f); // сглаженный край круга
                    Color c = dist > 1f - outlineWidth ? (Color)outline : (Color)fill; // обод или заливка
                    c.a = alpha; // альфа края
                    pixels[iy * size + ix] = c; // записать пиксель
                }
            }
            tex.SetPixels32(pixels); // залить текстуру
            tex.Apply(true, false); // применить с мипами, без CPU-копии
            return tex;
        }

        public static Texture2D CreateMaskTexture(int size, System.Func<float, float, bool> inside) // бинарная маска по предикату
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear }; // с мипами
            var pixels = new Color32[size * size]; // буфер
            var fill = new Color32(255, 255, 255, 255); // белый непрозрачный
            for (int iy = 0; iy < size; iy++) // строки
            {
                for (int ix = 0; ix < size; ix++) // столбцы
                {
                    float x = (ix + 0.5f) / size * 2f - 1f; // нормализованный X
                    float y = (iy + 0.5f) / size * 2f - 1f; // нормализованный Y
                    pixels[iy * size + ix] = inside(x, y) ? fill : new Color32(0, 0, 0, 0); // белый или прозрачный
                }
            }
            tex.SetPixels32(pixels); // залить
            tex.Apply(true, false); // применить
            return tex;
        }

        public static Texture2D CreateSoftDotTexture(int size, float core) // диск с мягкой кромкой
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear }; // с мипами
            var pixels = new Color32[size * size]; // буфер
            float edge = Mathf.Clamp01(1f - core); // доля радиуса на спад кромки
            for (int iy = 0; iy < size; iy++) // строки
            {
                for (int ix = 0; ix < size; ix++) // столбцы
                {
                    float x = (ix + 0.5f) / size * 2f - 1f; // нормализованный X
                    float y = (iy + 0.5f) / size * 2f - 1f; // нормализованный Y
                    float dist = Mathf.Sqrt(x * x + y * y); // расстояние от центра
                    float t = Mathf.Clamp01((1f - dist) / Mathf.Max(0.001f, edge)); // прогресс спада
                    byte a = (byte)(Mathf.SmoothStep(0f, 1f, t) * 255f); // гладкая альфа кромки
                    pixels[iy * size + ix] = new Color32(255, 255, 255, a); // белый с альфой
                }
            }
            tex.SetPixels32(pixels); // залить
            tex.Apply(true, false); // применить
            return tex;
        }

        // зона эффекта: плато до core, дальше smoothstep-спад ровно в ноль у края
        // канвы (C1-кривая без ступеней, альфа 0 в крайнем кольце пикселей)
        public static Texture2D CreateZoneTexture(int size, float core) // зона с гладким спадом
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear }; // с мипами
            var pixels = new Color32[size * size]; // буфер
            float span = Mathf.Max(0.001f, 1f - core); // ширина спада
            for (int iy = 0; iy < size; iy++) // строки
            {
                for (int ix = 0; ix < size; ix++) // столбцы
                {
                    float x = (ix + 0.5f) / size * 2f - 1f; // нормализованный X
                    float y = (iy + 0.5f) / size * 2f - 1f; // нормализованный Y
                    float dist = Mathf.Sqrt(x * x + y * y); // расстояние от центра
                    float t = Mathf.Clamp01((dist - core) / span); // прогресс спада за плато
                    float alpha = 1f - t * t * (3f - 2f * t); // smoothstep-инверсия
                    pixels[iy * size + ix] = new Color32(255, 255, 255, (byte)(alpha * 255f)); // белый с альфой
                }
            }
            tex.SetPixels32(pixels); // залить
            tex.Apply(true, false); // применить
            return tex;
        }

        // кривая короны звезды: плато до StarCoreEnd, дальше экспоненциальный
        // спад; с StarFadeStart линейный гас в ноль — край канвы не читается
        private const float StarCoreEnd = 0.55f; // радиус яркого ядра (доля канвы)
        private const float StarTailAlpha = 0.05f; // альфа экспоненты у нулевой точки фейда
        private const float StarFadeStart = 0.94f; // с какой доли радиуса корона гаснет в ноль

        public static Texture2D CreateStarTexture(int size) // тело звезды (Н9.3)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear }; // с мипами
            var pixels = new Color32[size * size]; // буфер
            float k = Mathf.Log(1f / StarTailAlpha) / (StarFadeStart - StarCoreEnd); // скорость экспоненты
            for (int iy = 0; iy < size; iy++) // строки
            {
                for (int ix = 0; ix < size; ix++) // столбцы
                {
                    float x = (ix + 0.5f) / size * 2f - 1f; // нормализованный X
                    float y = (iy + 0.5f) / size * 2f - 1f; // нормализованный Y
                    float dist = Mathf.Sqrt(x * x + y * y); // расстояние от центра
                    float alpha = dist <= StarCoreEnd ? 1f : Mathf.Exp(-k * (dist - StarCoreEnd)); // ядро или экспонента
                    if (dist > StarFadeStart) // в зоне конечного фейда
                        alpha *= Mathf.Clamp01((1f - dist) / (1f - StarFadeStart)); // линейно до нуля у канвы
                    pixels[iy * size + ix] = new Color32(255, 255, 255, (byte)(alpha * 255f)); // белый с альфой
                }
            }
            tex.SetPixels32(pixels); // залить
            tex.Apply(true, false); // применить
            return tex;
        }

        // кривая ядра дыры: плотный диск до HoleCoreSolid, короткий smoothstep-
        // спад к HoleCoreTailAlpha у края канвы — ровная сглаженная граница,
        // край канвы не читается как квадрат (тёмное ядро на тёмном фоне)
        private const float HoleCoreSolid = 0.94f; // радиус плотного диска
        private const float HoleCoreTailAlpha = 0.05f; // альфа хвоста у края

        public static Texture2D CreateHoleCoreTexture(int size) // тёмный диск дыры
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear }; // с мипами
            var pixels = new Color32[size * size]; // буфер
            float span = 1f - HoleCoreSolid; // ширина спада
            for (int iy = 0; iy < size; iy++) // строки
            {
                for (int ix = 0; ix < size; ix++) // столбцы
                {
                    float x = (ix + 0.5f) / size * 2f - 1f; // нормализованный X
                    float y = (iy + 0.5f) / size * 2f - 1f; // нормализованный Y
                    float dist = Mathf.Sqrt(x * x + y * y); // расстояние от центра
                    float alpha = dist <= HoleCoreSolid // в плотном диске
                        ? 1f // полная непрозрачность
                        : dist < 1f // в зоне спада
                            ? Mathf.SmoothStep(1f, HoleCoreTailAlpha, (dist - HoleCoreSolid) / span) // smoothstep к хвосту
                            : 0f; // за канвой диска альфы нет: SmoothStep клампит t и
                                  // без гейта залил бы углы канвы слабым «квадратом»
                    pixels[iy * size + ix] = new Color32(255, 255, 255, (byte)(alpha * 255f)); // белый с альфой (тёмное — тонировкой)
                }
            }
            tex.SetPixels32(pixels); // залить
            tex.Apply(true, false); // применить
            return tex;
        }

        public static Texture2D CreateSparkleTexture(int size) // искра с крестом лучей
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear }; // с мипами
            var pixels = new Color32[size * size]; // буфер
            for (int iy = 0; iy < size; iy++) // строки
            {
                for (int ix = 0; ix < size; ix++) // столбцы
                {
                    float x = (ix + 0.5f) / size * 2f - 1f; // нормализованный X
                    float y = (iy + 0.5f) / size * 2f - 1f; // нормализованный Y
                    float ax = Mathf.Abs(x); // модуль X
                    float ay = Mathf.Abs(y); // модуль Y

                    // ядро: компактная горячая точка с мягким краем
                    float dist = Mathf.Sqrt(x * x + y * y); // расстояние от центра
                    float core = Mathf.Clamp01((0.30f - dist) / 0.14f); // прогресс ядра
                    core = Mathf.SmoothStep(0f, 1f, core); // гладкое ядро

                    // лучи: тонкие сужающиеся крестовины
                    float armH = ay <= 0.05f ? Mathf.Clamp01(1f - ax) : 0f; // горизонтальный луч
                    float armV = ax <= 0.05f ? Mathf.Clamp01(1f - ay) : 0f; // вертикальный луч
                    float arm = Mathf.Max(armH, armV); // самый яркий луч
                    arm *= arm; // сужение к концам

                    float a = Mathf.Clamp01(Mathf.Max(core, arm * 0.9f)); // максимум ядра и лучей
                    pixels[iy * size + ix] = new Color32(255, 255, 255, (byte)(a * 255f)); // белый с альфой
                }
            }
            tex.SetPixels32(pixels); // залить
            tex.Apply(true, false); // применить
            return tex;
        }

        public static Texture2D CreateClusterTexture(int size, int seed) // скопление звёзд по сиду
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear }; // с мипами
            var rng = new System.Random(seed); // детерминированный рандом
            int count = 3 + rng.Next(3); // 3-5 звёзд в группе
            var xs = new float[count]; // координаты звёзд X
            var ys = new float[count]; // координаты звёзд Y
            var rs = new float[count]; // радиусы звёзд
            xs[0] = 0f; // главная — в центре
            ys[0] = 0f;
            rs[0] = 0.16f; // самая яркая — в центре
            for (int i = 1; i < count; i++) // остальные по кругу
            {
                float ang = (float)(rng.NextDouble() * System.Math.PI * 2.0); // случайный угол
                float dist = 0.30f + (float)rng.NextDouble() * 0.45f; // расстояние от центра
                xs[i] = Mathf.Cos(ang) * dist; // координата X
                ys[i] = Mathf.Sin(ang) * dist; // координата Y
                rs[i] = 0.06f + (float)rng.NextDouble() * 0.09f; // мелкий радиус
            }
            var pixels = new Color32[size * size]; // буфер
            for (int iy = 0; iy < size; iy++) // строки
            {
                for (int ix = 0; ix < size; ix++) // столбцы
                {
                    float x = (ix + 0.5f) / size * 2f - 1f; // нормализованный X
                    float y = (iy + 0.5f) / size * 2f - 1f; // нормализованный Y
                    float alpha = 0f; // суммарная альфа
                    for (int i = 0; i < count; i++) // вклад каждой звезды
                    {
                        // чёткая точка: плоский диск + узкий мягкий край
                        float dx = x - xs[i]; // смещение к звезде
                        float dy = y - ys[i];
                        float d = Mathf.Sqrt(dx * dx + dy * dy); // расстояние до звезды
                        float a = Mathf.Clamp01((rs[i] + 0.05f - d) / 0.05f); // диск с мягким краем
                        alpha = Mathf.Max(alpha, Mathf.Min(a, 1f)); // максимум по звёздам
                    }
                    pixels[iy * size + ix] = new Color32(255, 255, 255, (byte)(alpha * 255f)); // белый с альфой
                }
            }
            tex.SetPixels32(pixels); // залить
            tex.Apply(true, false); // применить
            return tex;
        }
    }
}
