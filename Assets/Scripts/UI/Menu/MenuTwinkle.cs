using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;

namespace SpaceTaxi.UI
{
    // Общая «жизнь» декоративных звёзд меню (бриф E): мерцание альфой по синусу.
    // Данные на структурах, без аллокаций в Update — используется слоем звёзд,
    // звёздным названием и декором кнопок. Спрайты переиспользуют кэш StubSprites
    // (мягкая точка и звезда с лучами), точки глифов текста — свой кэш
    // DiamondSparkle (E7.1) — 3 текстуры, uGUI батчит их в 2-3 draw call.

    // Форма точки-звезды в глифах текста (E7.1): Diamond — 4-лучевая «искра»
    // (ромб с вогнутыми сторонами), Circle — прежняя мягкая точка. Выбор —
    // константа GlyphStyle в MenuStarText; фон и маски 5x7 не зависят от стиля.
    public enum StarGlyphStyle
    {
        Circle,
        Diamond
    }

    public static class MenuTwinkle
    {
        public struct Star
        {
            public Image Image;
            public Color32 BaseColor;
            public float Amplitude; // размах мерцания вокруг базовой альфы (0..0.5)
            public float Speed;     // рад/с
            public float Phase;
            public float Boost;     // добавка к волне при наведении (кнопки), 0..1
        }

        // один шаг мерцания для списка звёзд; time — Time.unscaledTime
        // вызывающего; extraBoost — общая добавка к волне списка (наведение
        // на звёздный текст), поверх индивидуального star.Boost
        public static void Step(List<Star> stars, float time, float extraBoost = 0f)
        {
            for (int i = 0; i < stars.Count; i++)
            {
                Star star = stars[i];
                float wave = Mathf.Sin(time * star.Speed + star.Phase) + star.Boost + extraBoost;
                float alpha = star.BaseColor.a / 255f * (1f + star.Amplitude * wave);
                byte a = (byte)(Mathf.Clamp01(alpha) * 255f);
                star.Image.color = new Color32(star.BaseColor.r, star.BaseColor.g, star.BaseColor.b, a);
            }
        }

        // Создаёт звёздку-Image (raycastTarget = false) с якорем в центре родителя
        // и регистрирует её в списке мерцания. sparkle — звезда с крестом-бликом,
        // иначе мягкая точка; core — доля радиуса с полной альфой (0 — полностью
        // мягкое пятно для туманностей, 0.45 — звезда с горячим ядром).
        // baseSprite (E7.1) — замена мягкой точки для обычных звёзд (не искр):
        // так MenuStarText подставляет ромбовидную «искру» глифов
        public static Image CreateStar(RectTransform parent, Vector2 position, float size, Color color,
            float amplitude, float speed, float phase, bool sparkle, List<Star> into, float core = 0.45f,
            Sprite baseSprite = null)
        {
            var go = new GameObject("star", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(size, size);

            var image = go.AddComponent<Image>();
            if (sparkle)
                image.sprite = StubSprites.StarSparkle(64f);
            else if (baseSprite != null)
                image.sprite = baseSprite;
            else
                image.sprite = StubSprites.SoftDot(64f, core);
            image.color = color;
            image.raycastTarget = false;

            into.Add(new Star { Image = image, BaseColor = (Color32)color, Amplitude = amplitude, Speed = speed, Phase = phase });
            return image;
        }

        private static readonly Dictionary<int, Sprite> diamonds = new Dictionary<int, Sprite>();

        // E7.1: 4-лучевая «искра» для точек глифов текста — ромб с вогнутыми
        // сторонами (|x|^p + |y|^p <= 1 при p < 1) плюс горячее ядро: буквы
        // читаются плотнее и чётче, чем из мягких кружков. fatness — полнота
        // лучей (меньше — тоньше и острее), core — радиус ядра (доля полуширины).
        // Текстура строится один раз, кэш по паре параметров.
        public static Sprite DiamondSparkle(float fatness, float core)
        {
            int key = Mathf.RoundToInt(fatness * 100f) * 1000 + Mathf.RoundToInt(core * 100f);
            if (!diamonds.TryGetValue(key, out Sprite sprite) || sprite == null)
            {
                var tex = CreateDiamondTexture(64, fatness, core);
                sprite = Sprite.Create(tex, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
                diamonds[key] = sprite;
            }
            return sprite;
        }

        private static Texture2D CreateDiamondTexture(int size, float fatness, float core)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear };
            var pixels = new Color32[size * size];
            for (int iy = 0; iy < size; iy++)
            {
                for (int ix = 0; ix < size; ix++)
                {
                    float x = (ix + 0.5f) / size * 2f - 1f;
                    float y = (iy + 0.5f) / size * 2f - 1f;

                    // тело «искры»: вогнутые стороны суперэллипса с p < 1,
                    // мягкий край шириной 0.24 нормированной полуширины
                    float f = Mathf.Pow(Mathf.Abs(x), fatness) + Mathf.Pow(Mathf.Abs(y), fatness);
                    float body = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - f) / 0.24f));

                    // горячее ядро — плотная середина точки: без него буквы
                    // «пушистятся» и перестают читаться как буквы
                    float dist = Mathf.Sqrt(x * x + y * y);
                    float center = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((core - dist) / 0.14f));

                    float a = Mathf.Max(body, center);
                    pixels[iy * size + ix] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.Apply(true, false);
            return tex;
        }
    }
}
