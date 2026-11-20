using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace SpaceTaxi.UI
{
    // Кнопка «из звёзд»: сценические плашки и TMP-Label гасятся, текст
    // выкладывается звёздами (MenuStarText). Наведение — масштаб 1 → 1.06 и
    // плавное пожелтение звёзд; неактивная кнопка не желтеет (CanvasGroup).
    public sealed class StarButtonDecorator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float HoverScale = 1.06f;
        private const float GlowSpeed = 0.18f; // сек на разгон/затухание наведения (скейл + boost)
        private const float BoostMax = 0.9f;   // добавка к волне звёзд при наведении

        // ЕДИНАЯ клетка всех кнопок (автоподбор по длине надписи давал бы
        // разброс 10.3-14.2). Самая длинная надпись ПРОДОЛЖИТЬ (59 колонок)
        // чуть шире невидимой кнопки — панель не обрезает, кликабельна вся
        // площадь. Ручка размера букв кнопок — только эта константа
        private const float UniformCell = 11.4f;

        // глифы кнопок — белый базовый и жёлтый под наведение
        // (такси-жёлтый проекта: 1, 0.831, 0.302)
        private static readonly Color GlyphWhite = Color.white;
        private static readonly Color GlyphYellow = new(1f, 0.831f, 0.302f);
        private static readonly Color[] GlyphPalette = { GlyphWhite };

        private RectTransform rect;
        private Button button;
        private MenuStarText starText;
        private float glow;         // текущая сила наведения 0..1 (скейл + boost)
        private bool hovered;
        private bool glyphsYellow;  // текущая перекраска глифов

        public void Build()
        {
            rect = (RectTransform)transform;
            button = GetComponent<Button>();

            BuildClickTarget();
            HidePlaques();
            BuildLabel();
        }

        // единственный raycast-таргет кнопки — невидимый Image на всю площадь
        // (первым ребёнком, под звёздами): звёзды создаются с raycastTarget =
        // false, без этого Image клики проваливаются сквозь кнопку в ClickCatcher.
        // Звук клика не хукаем: у сценических кнопок AudioSource.Play уже
        // слушает клик в сцене, повторный хук дал бы двойной щелчок
        private void BuildClickTarget()
        {
            var clickGo = new GameObject("ClickTarget", typeof(RectTransform), typeof(Image));
            clickGo.transform.SetParent(rect, false);
            var clickRect = (RectTransform)clickGo.transform;
            clickRect.anchorMin = Vector2.zero;
            clickRect.anchorMax = Vector2.one;
            clickRect.offsetMin = Vector2.zero;
            clickRect.offsetMax = Vector2.zero;
            Image clickImage = clickGo.GetComponent<Image>();
            clickImage.color = new Color(0f, 0f, 0f, 0f); // невидим
            clickImage.raycastTarget = true;
        }

        // плашки — прямые дети кнопки Frame/Background с плоской заливкой
        // (Image без спрайта); прячу объекты, кнопка держит звёзды
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

        // текст кнопки — звёздами по маскам, TMP-Label гасится. Надпись
        // строится по центру КНОПКИ (не Label — у сценических Label разные
        // размеры/поля), высота букв ~54px при кнопке 108px. Палитра глифов —
        // чистый белый
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
            // Клетка едина для всех кнопок (UniformCell) — надписи разной длины
            // набираются ОДНИМ кеглем; ширина/высота ректа при override не
            // участвуют (кнопка невидима, панель не обрезает)
            starText.Build(rect, text.Trim(), GlyphPalette, maxWidthFactor: 0.95f, maxHeightFactor: 0.92f,
                cellOverride: UniformCell);
        }

        public void OnPointerEnter(PointerEventData eventData) => hovered = true;

        public void OnPointerExit(PointerEventData eventData) => hovered = false;

        private void Update()
        {
            // погашенная «Продолжить» не желтеет и не разгорается (её звёзды
            // гасит CanvasGroup от SceneFlowButtons)
            bool target = hovered && (button == null || button.interactable);
            glow = Mathf.MoveTowards(glow, target ? 1f : 0f, Time.unscaledDeltaTime / GlowSpeed);

            rect.localScale = Vector3.one * (1f + (HoverScale - 1f) * glow);

            // при наведении загораются сами звёзды кнопки: цвет rgb
            // (белый→жёлтый, твин внутри MenuStarText) + яркость искр
            // (boost к волне мерцания)
            if (starText != null && glyphsYellow != target)
            {
                glyphsYellow = target;
                starText.SetGlyphColor(target ? GlyphYellow : GlyphWhite);
            }
            if (starText != null)
                starText.SetBoost(glow * BoostMax);
        }
    }
}
