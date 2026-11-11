using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;

namespace SpaceTaxi.UI
{
    // Общая «жизнь» декоративных звёзд меню (бриф E): мерцание альфой по синусу.
    // Данные на структурах, без аллокаций в Update — используется слоем звёзд,
    // звёздным названием и декором кнопок. Спрайты переиспользуют кэш StubSprites
    // (мягкая точка и звезда с лучами) — 2 текстуры, uGUI батчит их в 1-2 draw call.
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

        // один шаг мерцания для списка звёзд; time — Time.unscaledTime вызывающего
        public static void Step(List<Star> stars, float time)
        {
            for (int i = 0; i < stars.Count; i++)
            {
                Star star = stars[i];
                float wave = Mathf.Sin(time * star.Speed + star.Phase) + star.Boost;
                float alpha = star.BaseColor.a / 255f * (1f + star.Amplitude * wave);
                byte a = (byte)(Mathf.Clamp01(alpha) * 255f);
                star.Image.color = new Color32(star.BaseColor.r, star.BaseColor.g, star.BaseColor.b, a);
            }
        }

        // Создаёт звёздку-Image (raycastTarget = false) с якорем в центре родителя
        // и регистрирует её в списке мерцания. sparkle — звезда с крестом-бликом,
        // иначе мягкая точка; core — доля радиуса с полной альфой (0 — полностью
        // мягкое пятно для туманностей, 0.45 — звезда с горячим ядром)
        public static Image CreateStar(RectTransform parent, Vector2 position, float size, Color color,
            float amplitude, float speed, float phase, bool sparkle, List<Star> into, float core = 0.45f)
        {
            var go = new GameObject("star", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(size, size);

            var image = go.AddComponent<Image>();
            image.sprite = sparkle ? StubSprites.StarSparkle(64f) : StubSprites.SoftDot(64f, core);
            image.color = color;
            image.raycastTarget = false;

            into.Add(new Star { Image = image, BaseColor = (Color32)color, Amplitude = amplitude, Speed = speed, Phase = phase });
            return image;
        }
    }
}
