using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SpaceTaxi.Core;

namespace SpaceTaxi.UI
{
    // Кнопка «из звёзд» (бриф E.5): за кнопкой гало-подсветка, по площадке —
    // мерцающие звёздки-орнамент. Наведение: масштаб 1 → 1.05, гало проявляется,
    // звёздки ярче и быстрее (MenuTwinkle.Star.Boost). Яркость самой кнопки при
    // наведении усиливает родной ColorTint кнопки (HighlightedColor в сцене) —
    // свой цвет мы не трогаем, чтобы не спорить с переходом.
    // Клик НЕ хукаем через UiSfx: у сценических кнопок звук уже повешен ПЕРВЫМ
    // слушателем (AudioSource.Play из инспектора, клип ui_click) — повторный хук
    // дал бы двойной щелчок (см. предупреждение в UiSfx.cs). Биндинги не тронуты.
    public sealed class StarButtonDecorator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float HoverScale = 1.05f;
        private const float HaloAlpha = 0.22f;
        private const float GlowSpeed = 0.18f;   // сек на полное проявление гало
        private const int StarCount = 7;

        private readonly List<MenuTwinkle.Star> stars = new();
        private RectTransform rect;
        private Button button;
        private Image halo;
        private float glow; // текущая сила подсветки 0..1
        private bool hovered;

        public void Build()
        {
            rect = (RectTransform)transform;
            button = GetComponent<Button>();

            // гало за кнопкой: в индекс 0 — под Frame/Background/Label
            var haloGo = new GameObject("StarHalo", typeof(RectTransform), typeof(Image));
            var haloRect = (RectTransform)haloGo.transform;
            haloRect.SetParent(rect, false);
            haloRect.anchorMin = Vector2.zero;
            haloRect.anchorMax = Vector2.one;
            haloRect.offsetMin = new Vector2(-70f, -55f); // растяжка с запасом за края кнопки
            haloRect.offsetMax = new Vector2(70f, 55f);
            halo = haloGo.GetComponent<Image>();
            halo.sprite = StubSprites.SoftDot(64f, 0f); // большое мягкое пятно
            halo.color = new Color(1f, 0.831f, 0.302f, 0f); // такси-жёлтый, прозрачен до наведения
            halo.raycastTarget = false;
            halo.transform.SetSiblingIndex(0);

            // звёздки-орнамент по площадке кнопки (поверх фона кнопки)
            float halfWidth = rect.rect.width * 0.5f * 0.92f;
            float halfHeight = rect.rect.height * 0.5f * 0.8f;
            var palette = new[]
            {
                new Color(0.659f, 1f, 0.796f),
                new Color(1f, 0.95f, 0.8f),
                new Color(1f, 0.831f, 0.302f)
            };
            for (int i = 0; i < StarCount; i++)
            {
                Vector2 position = new(Random.Range(-halfWidth, halfWidth), Random.Range(-halfHeight, halfHeight));
                bool sparkle = Random.value < 0.5f;
                Color color = palette[Random.Range(0, palette.Length)];
                color.a *= Random.Range(0.5f, 0.85f);
                MenuTwinkle.CreateStar(rect, position, sparkle ? Random.Range(9f, 15f) : Random.Range(6f, 11f),
                    color, 0.35f, Random.Range(1.2f, 3.0f), Random.Range(0f, Mathf.PI * 2f), sparkle, stars);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) => hovered = true;

        public void OnPointerExit(PointerEventData eventData) => hovered = false;

        private void Update()
        {
            // погашенная «Продолжить» не подсвечивается
            bool target = hovered && (button == null || button.interactable);
            glow = Mathf.MoveTowards(glow, target ? 1f : 0f, Time.unscaledDeltaTime / GlowSpeed);

            rect.localScale = Vector3.one * (1f + (HoverScale - 1f) * glow);
            Color c = halo.color;
            c.a = HaloAlpha * glow;
            halo.color = c;

            for (int i = 0; i < stars.Count; i++)
            {
                MenuTwinkle.Star star = stars[i];
                star.Boost = glow * 0.9f;
                stars[i] = star;
            }
            MenuTwinkle.Step(stars, Time.unscaledTime);
        }
    }
}
