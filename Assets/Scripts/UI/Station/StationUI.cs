using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UiKit;

namespace SpaceTaxi.UI
{
    // Ссылки на элементы шапки попапа станции
    public sealed class StationHeaderRefs
    {
        public TMP_Text Title;   // «СТАНЦИЯ · ИМЯ ПЛАНЕТЫ»
        public TMP_Text Credits; // «Đ N» — контроллер перечитывает по Wallet.CreditsChanged
        public Button Close;     // крестик закрытия в правом верхнем углу
    }

    // Каркас попапа станции: кнопки с hover, шапка с кредитами и крестиком,
    // секции «// ЗАГОЛОВОК», полоски, сегменты, сервисные кнопки.
    // Правило высот: TMP Ellipsis режет текст и по вертикали — высота узла
    // с текстом должна быть ≥ fontSize × 1.32 (26pt → 36, 21pt → 28),
    // иначе строка «есть, но невидима»
    public static class StationUI
    {

        // кнопка станции: hover/pressed через ColorTint; звук уже повешен
        // фабрикой — слушатели вызывающего добавляются после
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

        // шапка: точка + название слева, кредиты справа, крестик в углу.
        // Крестик — вне лейаута (строка резервирует справа 40px под него)
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
            // 36: 26pt ≈ 34.3 — запас против вертикального Ellipsis
            OnboardComputerFactory.FixedHeight(refs.Title.gameObject, 36f);
            OnboardComputerFactory.Flexible(refs.Title.gameObject);
            refs.Title.alignment = TextAlignmentOptions.Left;

            refs.Credits = OnboardComputerFactory.Text(row, UiFormat.Currency + " 0", 26f,
                OnboardComputerFactory.TaxiColor, "Credits");
            OnboardComputerFactory.FixedHeight(refs.Credits.gameObject, 36f);
            OnboardComputerFactory.FixedWidth(refs.Credits.gameObject, 132f);
            refs.Credits.alignment = TextAlignmentOptions.Right;

            // крестик 34×34 в правом верхнем углу; ignoreLayout обязателен —
            // иначе VerticalLayoutGroup перебил бы ручные якоря
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

        // секция «// ЗАГОЛОВОК» + линия (стиль секций борткомпа)
        public static GameObject Section(Transform parent, string title, string name)
        {
            var section = OnboardComputerFactory.V(name, parent, 10f);
            var heading = OnboardComputerFactory.Text(section, title, 21f, OnboardComputerFactory.LabelColor, "Heading");
            OnboardComputerFactory.FixedHeight(heading.gameObject, 28f);
            var line = Ui.Image(section.transform, null, UiTheme.Divider, "Line");
            OnboardComputerFactory.FixedHeight(line.gameObject, 2f);
            return section;
        }

        // --- Полоска-заливка (HP-бар): дорожка + fill; ширину ставит SetFill ---
        public static RectTransform FillBar(Transform parent, float height, Color fillColor)
        {
            var bar = new GameObject("FillBar", typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            var element = bar.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.minHeight = height;
            element.preferredHeight = height;

            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(bar.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)track.transform);
            var trackImage = track.GetComponent<Image>();
            trackImage.color = Ui.WithAlpha(OnboardComputerFactory.LabelColor, 0.12f);
            trackImage.raycastTarget = false;
            return FillChild(track.transform, fillColor);
        }

        // заливка-доля внутри дорожки: левый край закреплён, правый двигает
        // SetFill (доля 0..1). Общая для HP-бара и полосок статов витрины
        public static RectTransform FillChild(Transform track, Color color, string name = "Fill")
        {
            var fill = new GameObject(name, typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track, false);
            var rect = (RectTransform)fill.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f); // правый край двигает доля заполнения
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var fillImage = fill.GetComponent<Image>();
            fillImage.color = color;
            fillImage.raycastTarget = false;
            return rect;
        }

        // ширина fill-полоски по доле 0..1
        public static void SetFill(RectTransform fill, float fraction)
        {
            fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
        }

        // --- Сегментные полоски: бак станции (10 ячеек) и уровни улучшений (3);
        // цвета — как в HUD (HudPalette) ---
        public static readonly Color SegmentOn = HudPalette.SegmentOn;
        public static readonly Color SegmentOff = HudPalette.SegmentOff;

        // ряд из count равных ячеек на всю ширину родителя; старт — все погашены
        public static Image[] SegmentRow(Transform parent, string name, int count, float height)
        {
            var row = OnboardComputerFactory.H(name, parent, 4f);
            OnboardComputerFactory.FixedHeight(row, height);
            var cells = new Image[count];
            for (int i = 0; i < count; i++)
            {
                var cell = new GameObject("Cell_" + (i + 1).ToString("00"), typeof(RectTransform), typeof(Image));
                cell.transform.SetParent(row.transform, false);
                cell.AddComponent<LayoutElement>().flexibleWidth = 1f;
                cells[i] = cell.GetComponent<Image>();
                cells[i].color = SegmentOff;
                cells[i].raycastTarget = false;
            }
            return cells;
        }

        // подсветка: lit ячеек слева горят, остальные погашены
        public static void SetSegments(Image[] segments, int lit)
        {
            for (int i = 0; i < segments.Length; i++)
                segments[i].color = i < lit ? SegmentOn : SegmentOff;
        }

        // сервисная кнопка на всю ширину (заправка/ремонт/покупка); подпись
        // ставит и меняет вызывающий через GetComponentInChildren<TMP_Text>()
        public static Button ServiceButton(Transform parent, string name, UnityEngine.Events.UnityAction onClick,
            float height = 44f)
        {
            var go = Button(parent, "—", 20f, name);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleWidth = 1f;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }
    }
}
