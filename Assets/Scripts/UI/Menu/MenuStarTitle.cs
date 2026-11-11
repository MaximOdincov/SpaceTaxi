using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace SpaceTaxi.UI
{
    // Звёздное название меню (бриф E.4). Задокументированный компромисс по брифу:
    // звёзды НЕ выкладываются по глифам букв — это мерцающая россыпь ПОВЕРХ площади
    // логотипа, а текст TMP сохранён 1:1 («SPACE TAXI», шрифт/цвет сцены не тронуты).
    // Дополнительно лёгкое мерцание альфой самого текста через CanvasGroup
    // (без пересборки mesh TMP — дешевле, чем трогать vertex color).
    public sealed class MenuStarTitle : MonoBehaviour
    {
        private const int SoftCount = 26;
        private const int SparkleCount = 9;

        private readonly List<MenuTwinkle.Star> stars = new();
        private CanvasGroup shimmer;

        // Строит россыпь в контейнере-двойнике rect логотипа, вставленном ПЕРЕД
        // логотипом в иерархии (рисуется за текстом, но в той же панели меню)
        public void Build(RectTransform logo)
        {
            var container = new GameObject("LogoStars", typeof(RectTransform));
            var rect = (RectTransform)container.transform;
            rect.SetParent(logo.parent, false);
            rect.anchorMin = logo.anchorMin;
            rect.anchorMax = logo.anchorMax;
            rect.anchoredPosition = logo.anchoredPosition;
            rect.sizeDelta = logo.sizeDelta;
            rect.pivot = logo.pivot;
            rect.localRotation = logo.localRotation;
            rect.localScale = logo.localScale;
            rect.SetSiblingIndex(logo.GetSiblingIndex()); // сразу перед текстом — звёзды за ним

            float halfWidth = rect.rect.width * 0.5f + 22f;
            float halfHeight = rect.rect.height * 0.5f + 8f;
            var palette = new[]
            {
                new Color(0.659f, 1f, 0.796f),    // мятный (цвет логотипа)
                new Color(1f, 0.94f, 0.78f),      // тёплый белый
                new Color(1f, 0.831f, 0.302f),    // такси-жёлтый
                new Color(0.80f, 0.88f, 1f)       // холодный голубой
            };

            for (int i = 0; i < SoftCount + SparkleCount; i++)
            {
                Vector2 position = new(Random.Range(-halfWidth, halfWidth), Random.Range(-halfHeight, halfHeight));
                bool sparkle = i >= SoftCount;
                float size = sparkle ? Random.Range(10f, 20f) : Random.Range(5f, 13f);
                Color color = palette[Random.Range(0, palette.Length)];
                color.a *= sparkle ? Random.Range(0.65f, 1f) : Random.Range(0.45f, 0.9f);
                MenuTwinkle.CreateStar(rect, position, size, color, 0.4f,
                    Random.Range(1.0f, 2.8f), Random.Range(0f, Mathf.PI * 2f), sparkle, stars);
            }

            // мерцание текста: CanvasGroup на логотипе (там его ещё нет — LogoText
            // это RectTransform + TMP), альфа колеблется мягко
            shimmer = logo.GetComponent<CanvasGroup>();
            if (shimmer == null)
                shimmer = logo.gameObject.AddComponent<CanvasGroup>();
        }

        private void Update()
        {
            float time = Time.unscaledTime;
            MenuTwinkle.Step(stars, time);
            if (shimmer != null)
                shimmer.alpha = 0.87f + 0.13f * Mathf.Sin(time * 1.6f);
        }
    }
}
