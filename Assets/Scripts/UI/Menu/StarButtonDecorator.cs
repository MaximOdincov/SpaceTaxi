using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using SpaceTaxi.Core;

namespace SpaceTaxi.UI
{
    // Кнопка «из звёзд» (бриф E2/E3): плашек и шрифта нет — прячу сценические
    // Frame/Background с плоской заливкой и TMP-Label, вместо них звёздная
    // рамка по периметру кнопки и текст, выложенный звёздами (MenuStarText
    // по маскам 5x7). Наведение: масштаб 1 → 1.06 (рамка и текст растут
    // вместе с кнопкой), гало проявляется, звёзды рамки и текста ярче.
    // Клик НЕ хукаем через UiSfx: у сценических кнопок звук уже повешен ПЕРВЫМ
    // слушателем (AudioSource.Play из инспектора, клип ui_click) — повторный хук
    // дал бы двойной щелчок (см. предупреждение в UiSfx.cs). Биндинги не тронуты.
    public sealed class StarButtonDecorator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float HoverScale = 1.06f;
        private const float HaloAlpha = 0.22f;
        private const float GlowSpeed = 0.18f; // сек на полное проявление гало
        private const float FrameStep = 46f;   // шаг звёзд рамки, px
        private const float FrameJitter = 3f;  // дрожание звезды рамки, px
        private const float CornerSizeMin = 14f;
        private const float CornerSizeMax = 18f;
        private const float EdgeSizeMin = 7f;
        private const float EdgeSizeMax = 12f;
        private const float BoostMax = 0.9f;   // добавка к волне звёзд при наведении

        private static readonly Color[] Palette =
        {
            new(0.659f, 1f, 0.796f),   // мятный
            new(1f, 0.95f, 0.8f),      // тёплый белый
            new(1f, 0.831f, 0.302f),   // такси-жёлтый
            new(0.80f, 0.88f, 1f)      // холодный голубой
        };

        private readonly List<MenuTwinkle.Star> stars = new();
        private RectTransform rect;
        private Button button;
        private Image halo;
        private MenuStarText starText;
        private float glow; // текущая сила подсветки 0..1
        private bool hovered;

        public void Build()
        {
            rect = (RectTransform)transform;
            button = GetComponent<Button>();

            HidePlaques();
            BuildHalo();
            BuildLabel();
            BuildFrame();
        }

        // бриф E2: плашки — прямые дети кнопки Frame/Background с плоской
        // заливкой (Image без спрайта); прячу объекты, кнопки держат звёзды
        private void HidePlaques()
        {
            for (int i = 0; i < rect.childCount; i++)
            {
                Transform child = rect.GetChild(i);
                if (child.name != "Frame" && child.name != "Background")
                    continue;
                if (child.TryGetComponent(out Image image) && image.sprite == null)
                    child.gameObject.SetActive(false);
            }
        }

        // гало за кнопкой: в индекс 0 — под звёздами рамки и текста
        private void BuildHalo()
        {
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
        }

        // бриф E3: текст кнопки — звёздами по маскам, TMP-Label гасится.
        // Надпись строится по центру КНОПКИ (не Label — у сценических Label
        // разные размеры/поля), высота букв ~54px при кнопке 108px
        private void BuildLabel()
        {
            string text = null;
            if (rect.Find("Label") is Transform label && label.TryGetComponent(out TMP_Text tmp))
            {
                text = tmp.text;
                tmp.enabled = false; // вместо шрифта — звёзды
            }
            if (string.IsNullOrWhiteSpace(text))
                text = gameObject.name; // страховка: имя кнопки

            starText = gameObject.AddComponent<MenuStarText>();
            starText.Build(rect, text.Trim(), Palette, maxWidthFactor: 0.86f, maxHeightFactor: 0.62f);
        }

        // звёздная рамка: звёзды по периметру с дрожанием, углы — крупными
        // блик-искрами; шаг ~46px даёт ~34 звезды на кнопку 679x108
        private void BuildFrame()
        {
            float halfWidth = rect.rect.width * 0.5f;
            float halfHeight = rect.rect.height * 0.5f;

            int columns = Mathf.Max(2, Mathf.RoundToInt(rect.rect.width / FrameStep));
            for (int i = 0; i <= columns; i++)
            {
                float x = Mathf.Lerp(-halfWidth, halfWidth, i / (float)columns);
                bool corner = i == 0 || i == columns;
                AddFrameStar(new Vector2(x, halfHeight), corner);
                AddFrameStar(new Vector2(x, -halfHeight), corner);
            }
            int rows = Mathf.Max(2, Mathf.RoundToInt(rect.rect.height / FrameStep));
            for (int i = 1; i < rows; i++) // углы уже поставлены горизонтальными рядами
            {
                float y = Mathf.Lerp(-halfHeight, halfHeight, i / (float)rows);
                AddFrameStar(new Vector2(-halfWidth, y), false);
                AddFrameStar(new Vector2(halfWidth, y), false);
            }
        }

        private void AddFrameStar(Vector2 position, bool corner)
        {
            bool sparkle = corner || Random.value < 0.3f;
            float size = corner
                ? Random.Range(CornerSizeMin, CornerSizeMax)
                : Random.Range(EdgeSizeMin, EdgeSizeMax);
            Color color = Palette[Random.Range(0, Palette.Length)];
            color.a *= Random.Range(0.55f, 0.9f);
            Vector2 jitter = new(Random.Range(-FrameJitter, FrameJitter), Random.Range(-FrameJitter, FrameJitter));
            MenuTwinkle.CreateStar(rect, position + jitter, size, color, 0.4f,
                Random.Range(1.2f, 3.0f), Random.Range(0f, Mathf.PI * 2f), sparkle, stars);
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
                star.Boost = glow * BoostMax;
                stars[i] = star;
            }
            MenuTwinkle.Step(stars, Time.unscaledTime);
            if (starText != null)
                starText.SetBoost(glow * BoostMax);
        }
    }
}
