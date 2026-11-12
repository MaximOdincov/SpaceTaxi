using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using SpaceTaxi.Core;

namespace SpaceTaxi.UI
{
    // Кнопка «из звёзд» (бриф E2/E3, хотфикс Н6): плашек и шрифта нет — прячу
    // сценические Frame/Background с плоской заливкой и TMP-Label; текст
    // выложен звёздами (MenuStarText по маскам 5x7). Звёздной рамки по
    // периметру больше НЕТ (Н6: кнопка = только звёздный текст).
    //
    // Цвет (Н6): все глифы кнопки базово ЧИСТО БЕЛЫЕ; при наведении —
    // такси-жёлтый, при уходе — обратно белый. Заголовок «SPACE TAXI» не
    // тронут (MenuStarTitle красит сам). Наведение: масштаб 1 → 1.06,
    // гало проявляется — как раньше.
    //
    // Хотфикс Н6 (кликабельность): после гашения Frame/Background/Label под
    // кнопкой не оставалось НИ ОДНОГО raycastTarget (звёзды MenuTwinkle
    // создаются с raycastTarget=false, гало тоже) — pointer-события вообще
    // не доходили до Button: клики падали сквозь кнопку в ClickCatcher
    // (яйцо летело вместо нажатия, hover не включался). Лечение:
    // прозрачный Image на весь прямоугольник кнопки с raycastTarget=true —
    // клики и hover ловятся по всей площади. Клик НЕ хукаем через UiSfx:
    // у сценических кнопок AudioSource.Play уже ПЕРВЫЙ слушатель в сцене
    // (клип ui_click), повторный хук дал бы двойной щелчок. onClick-биндинги
    // сцены (SceneFlowButtons) не тронуты.
    public sealed class StarButtonDecorator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float HoverScale = 1.06f;
        private const float HaloAlpha = 0.22f;
        private const float GlowSpeed = 0.18f; // сек на полное проявление гало
        private const float BoostMax = 0.9f;   // добавка к волне звёзд при наведении

        // Н6: глифы кнопок — белый базовый и жёлтый под наведение
        // (такси-жёлтый проекта, как у гало: 1, 0.831, 0.302)
        private static readonly Color GlyphWhite = Color.white;
        private static readonly Color GlyphYellow = new(1f, 0.831f, 0.302f);
        private static readonly Color[] GlyphPalette = { GlyphWhite };

        private RectTransform rect;
        private Button button;
        private Image halo;
        private MenuStarText starText;
        private float glow;         // текущая сила подсветки 0..1
        private bool hovered;
        private bool glyphsYellow;  // Н6: текущая перекраска глифов

        public void Build()
        {
            rect = (RectTransform)transform;
            button = GetComponent<Button>();

            BuildClickTarget();
            HidePlaques();
            BuildHalo();
            BuildLabel();
        }

        // хотфикс Н6: единственный raycast-таргет кнопки — невидимый Image на
        // всю площадь (первым ребёнком, под звёздами). Без него после гашения
        // плашек/лейбла клики проваливаются сквозь кнопку в ClickCatcher
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

        // бриф E2: плашки — прямые дети кнопки Frame/Background с плоской
        // заливкой (Image без спрайта); прячу объекты, кнопка держит звёзды
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

        // гало за кнопкой: в индекс 0 — под звёздами текста
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
        // разные размеры/поля), высота букв ~54px при кнопке 108px.
        // Н6: палитра глифов — чистый белый (без мяты/золота/голубого)
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
            starText.Build(rect, text.Trim(), GlyphPalette, maxWidthFactor: 0.86f, maxHeightFactor: 0.62f);
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

            // Н6: при наведении все глифы кнопки жёлтые, при уходе — белые;
            // погашенная «Продолжить» остаётся белой
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
