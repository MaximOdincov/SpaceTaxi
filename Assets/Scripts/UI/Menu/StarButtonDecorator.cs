using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace SpaceTaxi.UI
{
    // Кнопка «из звёзд» (бриф E2/E3, хотфикс Н6): плашек и шрифта нет — прячу
    // сценические Frame/Background с плоской заливкой и TMP-Label; текст
    // выложен звёздами (MenuStarText по маскам 5x7). Звёздной рамки и
    // гало-полосы по периметру больше НЕТ (E8.2: наведение зажигает САМИ
    // ЗВЁЗДЫ — цвет и яркость искр; никаких подложек).
    //
    // Цвет (Н6 + E8.2): все глифы кнопки базово ЧИСТО БЕЛЫЕ; при наведении
    // плавно желтеют (твин ~0.15с внутри MenuStarText.SetGlyphColor) и
    // разгораются (boost к волне мерцания), при уходе — плавно обратно белый.
    // Заголовок «SPACE TAXI» не тронут (MenuStarTitle красит сам). Наведение:
    // масштаб 1 → 1.06. Неактивная кнопка («Продолжить» без сейва) не
    // желтеет вовсе — её звёзды гасит CanvasGroup от SceneFlowButtons
    // (DisabledAlpha 0.4 — тусклые белые).
    //
    // Хотфикс Н6 (кликабельность): после гашения Frame/Background/Label под
    // кнопкой не оставалось НИ ОДНОГО raycastTarget (звёзды MenuTwinkle
    // создаются с raycastTarget=false) — pointer-события вообще
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
        private const float GlowSpeed = 0.18f; // сек на разгон/затухание наведения (скейл + boost)
        private const float BoostMax = 0.9f;   // добавка к волне звёзд при наведении

        // 2026-11-15 (владелец: «Продолжить и Новая игра чуть меньше других»):
        // клетка у каждой кнопки подбиралась по ДЛИНЕ надписи — ПРОДОЛЖИТЬ 10.3,
        // НОВАЯ ИГРА 11.0, НАСТРОЙКИ 11.4, ВЫХОД 14.2 (вертикальный предел) —
        // заметный разброс. ЕДИНАЯ клетка 11.4 для всех кнопок: самая длинная
        // надпись ПРОДОЛЖИТЬ (59 колонок = 673 канвас-px) чуть шире невидимой
        // кнопки 638 — панель не обрезает (заливки скрыты), кликабельна вся
        // площадь кнопки. Ручка размера букв кнопок — только эта константа
        private const float UniformCell = 11.4f;

        // Н6: глифы кнопок — белый базовый и жёлтый под наведение
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

            // E8.2: при наведении загораются САМИ ЗВЁЗДЫ кнопки — цвет rgb
            // (белый→жёлтый, плавный твин внутри MenuStarText) + яркость
            // искр (boost к волне мерцания). Никаких ореолов-подложек.
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
