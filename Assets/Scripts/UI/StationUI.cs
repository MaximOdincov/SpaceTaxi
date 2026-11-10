using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceTaxi.UI
{
    // Ссылки на элементы хедера попапа станции (рамка С4)
    public sealed class StationHeaderRefs
    {
        public TMP_Text Title;   // «СТАНЦИЯ · ИМЯ ПЛАНЕТЫ»
        public TMP_Text Credits; // «Đ N» — контроллер перечитывает по Wallet.CreditsChanged
        public Button Close;     // крестик закрытия в правом верхнем углу
    }

    // Единый каркас попапа станции: кнопки с озвучкой и ColorTint, хедер с
    // кредитами и крестиком закрытия, секции «// …». Хелперы строят uGUI-узлы
    // кодом поверх примитивов OnboardComputerFactory (цвета, шрифт, шаблон
    // кнопки с первым хуком UiSfx — щелчок звучит раньше действий вызывающего).
    // Правило высоты текста 1.32 (NIGHT_LOG4, задача 1): TMP Ellipsis режет
    // текст и ПО ВЕРТИКАЛИ — узел с текстом обязан иметь высоту
    // ≥ fontSize × 1.32 (JetBrainsMono: 36pt ≈ 47.5, 26pt ≈ 34.3, 21pt ≈ 27.7,
    // 18pt ≈ 23.8), иначе строка «есть, но невидима». Воздух: spacing между
    // блоками ≥ 10-12, ничего не слеплено.
    public static class StationUI
    {
        // символ валюты в текстах станции; числа форматирует OnboardBinder.FormatCredits
        public const string Currency = "Đ";

        // --- Кнопка станции: фон ButtonColor фабрики, текст ярче (BrightColor),
        // hover/pressed через Button ColorTint (паттерн PlayerButton фабрики).
        // Слушатели вызывающего добавляются ПОСЛЕ — звук уже повешен фабрикой ---
        public static GameObject Button(Transform parent, string label, float fontSize, string name = null)
        {
            var go = OnboardComputerFactory.Button(parent, label,
                OnboardComputerFactory.ButtonColor, OnboardComputerFactory.BrightColor, fontSize, name);
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.fadeDuration = 0.1f;
            colors.highlightedColor = new Color(0.82f, 1f, 0.9f);
            colors.pressedColor = new Color(0.6f, 1f, 0.78f);
            button.colors = colors;
            return go;
        }

        // --- Хедер попапа: «СТАНЦИЯ · …» слева в стиле борткомпа (точка-линк,
        // BrightColor), кредиты «Đ N» справа (формат OnboardBinder.FormatCredits),
        // крестик закрытия в правом верхнем углу панели — клик закрывает попап
        // (Esc закрывает тем же Close() контроллера). Крестик вне лейаута,
        // поверх угла; строка резервирует справа 40px, тексты с ним не наезжают ---
        public static StationHeaderRefs PopupHeader(Transform panel, string title)
        {
            var refs = new StationHeaderRefs();
            var row = OnboardComputerFactory.H("TitleRow", panel);
            OnboardComputerFactory.FixedHeight(row.gameObject, 44f);
            row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(0, 40, 0, 0);

            var dot = OnboardComputerFactory.Text(row, "<voffset=0.35em>●</voffset>", 18f,
                OnboardComputerFactory.LabelColor, "LinkDot");
            OnboardComputerFactory.FixedWidth(dot.gameObject, 24f);
            dot.alignment = TextAlignmentOptions.Left;

            refs.Title = OnboardComputerFactory.Text(row, title, 26f, OnboardComputerFactory.BrightColor, "Title");
            // 36, не 34: строка 26pt ≈ 34.3 — Ellipsis при меньшей высоте режет заголовок
            OnboardComputerFactory.FixedHeight(refs.Title.gameObject, 36f);
            OnboardComputerFactory.Flexible(refs.Title.gameObject);
            refs.Title.alignment = TextAlignmentOptions.Left;

            refs.Credits = OnboardComputerFactory.Text(row, Currency + " 0", 26f,
                OnboardComputerFactory.TaxiColor, "Credits");
            OnboardComputerFactory.FixedHeight(refs.Credits.gameObject, 36f);
            OnboardComputerFactory.FixedWidth(refs.Credits.gameObject, 132f);
            refs.Credits.alignment = TextAlignmentOptions.Right;

            // крестик: квадрат 34×34 в правом верхнем углу панели, вне лейаута;
            // ignoreLayout обязателен — VerticalLayoutGroup перебил бы якоря
            bool crossGlyph = refs.Title.font != null && refs.Title.font.HasCharacter('×');
            var closeGo = OnboardComputerFactory.Button(panel, crossGlyph ? "×" : "X",
                OnboardComputerFactory.ButtonColor, OnboardComputerFactory.BrightColor, 22f, "CloseButton");
            var closeRect = (RectTransform)closeGo.transform;
            closeRect.anchorMin = Vector2.one;
            closeRect.anchorMax = Vector2.one;
            closeRect.pivot = Vector2.one;
            // дефолтный sizeDelta 100×100 от new RectTransform наезжает на хедер
            closeRect.sizeDelta = new Vector2(34f, 34f);
            closeRect.anchoredPosition = new Vector2(-4f, -4f);
            var closeLe = closeGo.AddComponent<LayoutElement>();
            closeLe.ignoreLayout = true;
            OnboardComputerFactory.FixedSize(closeGo, 34f, 34f);
            refs.Close = closeGo.GetComponent<Button>();
            return refs;
        }

        // --- Секция «// ЗАГОЛОВОК» + линия — единый стиль заголовков станции
        // (как секции борткомпа). Высота заголовка 28: строка 21pt ≈ 27.7 —
        // Ellipsis при меньшей высоте обрезал бы её по вертикали.
        // Spacing 10 — воздух между блоками секции ---
        public static GameObject Section(Transform parent, string title, string name)
        {
            var section = new GameObject(name, typeof(RectTransform));
            section.transform.SetParent(parent, false);
            var layout = section.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var heading = OnboardComputerFactory.Text(section, title, 21f, OnboardComputerFactory.LabelColor, "Heading");
            OnboardComputerFactory.FixedHeight(heading.gameObject, 28f);
            var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(section.transform, false);
            var lineImage = line.GetComponent<Image>();
            lineImage.color = new Color(
                OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g,
                OnboardComputerFactory.LabelColor.b, 0.4f);
            lineImage.raycastTarget = false;
            OnboardComputerFactory.FixedHeight(line, 2f);
            return section;
        }

        // --- Полоска-заливка (HP-бар ремонта): дорожка + fill; ширину ставит
        // контроллер по доле 0..1 (правый край fill двигается, как у
        // сравнительных полосок витрины) ---
        public static RectTransform FillBar(Transform parent, float height, Color fillColor)
        {
            var bar = new GameObject("FillBar", typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            var element = bar.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.minHeight = height;
            element.preferredHeight = height;

            var dim = OnboardComputerFactory.LabelColor;
            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(bar.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)track.transform);
            var trackImage = track.GetComponent<Image>();
            trackImage.color = new Color(dim.r, dim.g, dim.b, 0.12f);
            trackImage.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            var rect = (RectTransform)fill.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f); // правый край двигает доля заполнения
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var fillImage = fill.GetComponent<Image>();
            fillImage.color = fillColor;
            fillImage.raycastTarget = false;
            return rect;
        }
    }
}
