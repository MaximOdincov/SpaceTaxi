using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Audio;

namespace SpaceTaxi.UI
{
    // Ссылки на построенные элементы
    public sealed class OnboardRefs
    {
        public TMP_Text Clock, MoneyValue, RatingValue;
        public GameObject DebtRow;         // строка долга под шапкой — скрыта при нуле
        public TMP_Text DebtValue;
        public GameObject ActiveSection;   // секция целиком — скрывается без активного заказа
        public RectTransform ActiveArea;   // карточка активного заказа (ведёт биндер)
        public RectTransform OrderArea;    // контент скролла входящих карточек (ведёт биндер)
        public GameObject OffersScroll;    // скролл входящих
        public Button AbandonButton;       // красная «ОТКЛЮЧИТЬ КОРАБЛЬ» в нижнем слоте
        public Button PlayerPrev, PlayerNext;
        public Button RadioMute;
        public TMP_Text TrackTitle;
        public RectTransform ProgressFill;
        public TMP_FontAsset Font;
    }

    // Строит каркас бортового компьютера: [СОСТОЯНИЕ: часы · кредиты · ★ рейтинг]
    // → [строка долга] → [активный заказ (всегда детальный)] → [входящие со скроллом:
    // карточки раскрываются IN PLACE] → [статистика] → [эфир] → [отключить корабль].
    // Высоты секций фиксированные, скролл только в списке входящих (паттерн
    // StationScreenController). Политика переполнения: все тексты однолинейные с
    // обрезкой «…», высоты узлов фиксированные — контент никогда не двигает layout
    // (исключения — «Пожелание клиента» в сводке: 2 строки). Из редактора результат
    // сериализуется в сцену; в рантайме биндер строит каркас этим же методом поверх
    // старого мокапа (Frame/Background сохраняются, остальное сносится).
    public static class OnboardComputerFactory
    {
        // используется и рантайм-карточками
        private static TMP_FontAsset currentFont;

        // --- Геометрия карточек (контракт с биндером: анимация раскрытия) ---
        // карточка имеет два взаимоисключающих контейнера: Compact и Expanded.
        // Раскрытие гасит Compact, включает Expanded и анимирует preferredHeight
        // карточки OfferCompactHeight ↔ OfferExpandedHeight; RectMask2D на карточке
        // срезает контент, пока анимация не отдала высоту — ничего не вылезает
        public const float CardSpacing = 4f;
        public const float AcceptHeight = 40f;
        public const float OfferCompactHeight = 124f;   // рамка 16 + Compact 108
        public const float DetailAvatarHeight = 176f;   // портрет на всю ширину
        public const float SummaryHeight = 170f;        // Имя 30 + Откуда/Куда/Маршрут 26×3 + Пожелание 54 + зазоры
        public const float ExpandedContentHeight = DetailAvatarHeight + CardSpacing + SummaryHeight + CardSpacing + AcceptHeight;
        public const float OfferExpandedHeight = 16f + ExpandedContentHeight;
        // активный заказ — тот же Expanded всегда (без строки статуса и тоггла)
        public const float ActiveSummaryHeight = SummaryHeight;
        public const float ActiveExpandedContentHeight = DetailAvatarHeight + CardSpacing + ActiveSummaryHeight + CardSpacing + AcceptHeight;
        public const float ActiveCardHeight = 16f + ActiveExpandedContentHeight;

        // приглушённая зелень рамки аватара: яркая LabelColor спорила с портретом
        public static readonly Color AvatarFrameColor = new Color(0.27f, 0.54f, 0.39f, 0.9f);

        public static readonly Color RatingGreen = new Color(0.357f, 0.9f, 0.56f);

        public static readonly Color LabelColor = new Color(0.357f, 0.788f, 0.561f);
        public static readonly Color BrightColor = new Color(0.659f, 1f, 0.796f);
        public static readonly Color TaxiColor = new Color(1f, 0.831f, 0.302f);
        public static readonly Color PanelColor = new Color(0.031f, 0.078f, 0.047f, 0.92f);
        public static readonly Color DangerColor = new Color(1f, 0.32f, 0.24f);
        public static readonly Color DangerDark = new Color(0.05f, 0.02f, 0.03f);
        // текст на ярких кнопках действия (жёлтая «взять», красная «отменить») — чёрный
        public static readonly Color ActionTextColor = Color.black;
        // «спокойный» красный долга: платится по графику или докатывается вниз
        public static readonly Color DebtCalmColor = new Color(0.86f, 0.46f, 0.42f);
        public static readonly Color ButtonColor = new Color(0.06f, 0.2f, 0.13f, 0.9f);

        public static OnboardRefs Build(Transform panel, bool destroyImmediate = false, TMP_FontAsset fontOverride = null)
        {
            // шрифт: явный override от биндера → старый мокап-титул → дефолт TMP
            TMP_FontAsset font = fontOverride;
            if (font == null)
            {
                var oldTitle = panel.Find("Title");
                if (oldTitle != null)
                    font = oldTitle.GetComponent<TMP_Text>().font;
            }
            if (font == null)
                font = TMP_Settings.defaultFontAsset;
            currentFont = font;

            // старое содержимое сносим (кроме рамки и фона)
            for (int i = panel.childCount - 1; i >= 0; i--)
            {
                string n = panel.GetChild(i).name;
                if (n != "Frame" && n != "Background")
                {
                    if (destroyImmediate)
                        Object.DestroyImmediate(panel.GetChild(i).gameObject);
                    else
                        Object.Destroy(panel.GetChild(i).gameObject);
                }
            }

            var root = new GameObject("Stack", typeof(RectTransform));
            root.transform.SetParent(panel, false);
            Stretch((RectTransform)root.transform);
            var rootLayout = root.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(16, 16, 10, 10);
            rootLayout.spacing = 8;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            var refs = new OnboardRefs { Font = font };

            // --- СОСТОЯНИЕ: время у левого края · деньги строго по центру панели · ★ рейтинг у правого края ---
            // якоря вручную (ignoreLayout): равные трети дают центр «своей» ячейки,
            // а не панели; деньги ставим якорем в середину ширины Header
            var header = H("Header", root.transform);
            FixedHeight(header, 44f);
            var headerRect = (RectTransform)header.transform;
            // глиф «⏲» есть не в каждом атласе: без него — просто время
            bool clockGlyph = font.HasCharacter('⏲');
            refs.Clock = Text(header, clockGlyph ? "⏲ 15:30" : "15:30", 36f, BrightColor, "Clock");
            AnchorHeaderEdge(refs.Clock.transform as RectTransform, isLeft: true);
            refs.Clock.alignment = TextAlignmentOptions.Left;
            // деньги без иконки: символ валюты прямо в тексте (Đ 3.5k)
            refs.MoneyValue = Text(header, "Đ 0", 36f, BrightColor, "MoneyValue");
            AnchorHeaderCenter(refs.MoneyValue.transform as RectTransform);
            refs.MoneyValue.alignment = TextAlignmentOptions.Center;
            // рейтинг — текст «★ 3.0»: глиф звезды испечён в атласы JetBrainsMono;
            // без глифа (сброс dynamic-атласа в билде) биндер пишет просто «3.0»
            bool starGlyph = font.HasCharacter('★');
            refs.RatingValue = Text(header, starGlyph ? "★ 3.0" : "3.0", 36f, RatingGreen, "RatingValue");
            AnchorHeaderEdge(refs.RatingValue.transform as RectTransform, isLeft: false);
            refs.RatingValue.alignment = TextAlignmentOptions.Right;

            // --- Строка долга: пустая → скрыта, секции ниже поднимаются ---
            refs.DebtValue = Text(root, "Текущий долг: Đ 0", 22f, DebtCalmColor, "DebtRow");
            FixedHeight(refs.DebtValue.gameObject, 30f);
            refs.DebtRow = refs.DebtValue.gameObject;
            refs.DebtRow.SetActive(false);

            // --- АКТИВНЫЙ ЗАКАЗ: всегда в детальном виде, строит биндер ---
            var active = Section(root.transform, "// АКТИВНЫЙ ЗАКАЗ", "ActiveSection");
            refs.ActiveSection = active;
            var activeArea = new GameObject("ActiveArea", typeof(RectTransform));
            activeArea.transform.SetParent(active.transform, false);
            var activeLayout = activeArea.AddComponent<VerticalLayoutGroup>();
            activeLayout.spacing = 4;
            activeLayout.childControlWidth = true;
            activeLayout.childControlHeight = true;
            activeLayout.childForceExpandWidth = true;
            activeLayout.childForceExpandHeight = false;
            refs.ActiveArea = (RectTransform)activeArea.transform;

            // --- ВХОДЯЩИЕ ЗАКАЗЫ: карточки биндер кладёт в скролл ---
            var incoming = Section(root.transform, "// ВХОДЯЩИЕ ЗАКАЗЫ", "IncomingSection");
            var scrollWrap = new GameObject("OffersScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollWrap.transform.SetParent(incoming.transform, false);
            var wrapElement = scrollWrap.AddComponent<LayoutElement>();
            wrapElement.flexibleHeight = 1f; // всё свободное место между секциями
            wrapElement.minHeight = 120f;
            refs.OffersScroll = scrollWrap;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewport.transform.SetParent(scrollWrap.transform, false);
            Stretch((RectTransform)viewport.transform);
            // прозрачный фон — raycast-цель под курсором, иначе колесо мыши не скроллит
            viewport.GetComponent<Image>().color = Color.clear;

            var content = new GameObject("OrderArea", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = (RectTransform)content.transform;
            // контент растёт вниз от верхней кромки вьюпорта — стандартная схема ScrollRect
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            var contentLayout = content.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 6;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollWrap.GetComponent<ScrollRect>();
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            refs.OrderArea = contentRect;

            // --- ЭФИР: заголовок секции, плеер, прогресс, мьют ---
            var radio = Section(root.transform, "// ЭФИР", "RadioSection");
            var playerRow = H("PlayerRow", radio.transform);
            FixedHeight(playerRow, 44f);
            var playerRowLayout = playerRow.GetComponent<HorizontalLayoutGroup>();
            playerRowLayout.childForceExpandWidth = false; // кнопки строго 44×44, трек — flexible
            // паузу убрали по просьбе владельца: радио всегда играет, мьют ниже плеера
            refs.PlayerPrev = PlayerButton(playerRow.transform, "|<", "PlayerPrev");
            refs.TrackTitle = Text(playerRow, "—", 20f, BrightColor, "Track");
            refs.TrackTitle.alignment = TextAlignmentOptions.Center;
            refs.TrackTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.PlayerNext = PlayerButton(playerRow.transform, ">|", "PlayerNext");
            var progress = new GameObject("Progress", typeof(RectTransform), typeof(Image));
            progress.transform.SetParent(radio.transform, false);
            progress.GetComponent<Image>().color = new Color(0.06f, 0.2f, 0.13f);
            FixedHeight(progress, 6f);
            // заливка прогресса: ширина = доля clip position/length (ведёт OnboardBinder)
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(progress.transform, false);
            Image fillImage = fill.GetComponent<Image>();
            fillImage.color = BrightColor;
            fillImage.raycastTarget = false;
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f); // правый край двигает биндер
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            refs.ProgressFill = fillRect;
            var muteGo = Button(radio.transform, "ЗВУК: ВКЛ", ButtonColor, BrightColor, 15f, "RadioMute");
            var muteLe = muteGo.AddComponent<LayoutElement>();
            muteLe.minHeight = 34f;
            muteLe.flexibleWidth = 1f;
            refs.RadioMute = muteGo.GetComponent<Button>();

            // --- ОТКЛЮЧИТЬ КОРАБЛЬ: тревожная красная, клик сразу запускает
            // штатный пайплайн потери (HullSystem.Abandon → респавн/долг/парк)
            var abandon = Button(root.transform, "ОТКЛЮЧИТЬ КОРАБЛЬ", DangerColor * 0.9f,
                DangerDark, 18f, "AbandonButton");
            var abandonLe = abandon.AddComponent<LayoutElement>();
            abandonLe.minHeight = 40f;
            abandonLe.flexibleWidth = 1f;
            refs.AbandonButton = abandon.GetComponent<Button>();

            return refs;
        }

        // --- Элементы каркаса ---

        private static GameObject Section(Transform parent, string title, string name)
        {
            var section = new GameObject(name, typeof(RectTransform));
            section.transform.SetParent(parent, false);
            var layout = section.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var heading = Text(section, title, 21f, LabelColor, "Heading");
            // 28, не 26: строка 21pt ≈ 27.7 — Ellipsis при меньшей высоте
            // обрезает заголовок до нуля видимых символов
            FixedHeight(heading.gameObject, 28f);
            var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(section.transform, false);
            line.GetComponent<Image>().color = new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.4f);
            FixedHeight(line, 2f);
            return section;
        }

        // --- Карточки заказов ---

        // Пути узлов (контракт для OnboardBinder):
        //   Compact/Avatar, Compact/Info/Name, Compact/Info/Route, Compact/Info/Dist,
        //   Expanded/DetailAvatar, Expanded/Summary/{Name,From,To,RouteLen,Wish},
        //   Expanded/Accept, ExpandToggle
        // Карточка входящего: Compact = [аватар по аспекту | имя / маршрут / расстояние],
        // Expanded = портрет на всю ширину + сводка + «Взять заказ». Контейнеры
        // взаимоисключающие; раскрытие анимирует preferredHeight карточки.
        public static GameObject BuildOfferCard()
        {
            var card = new GameObject("OfferCard", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = CardSpacing;
            // высоты детей фиксированные (LE preferred): во время анимации сумма детей
            // больше карточки — маска срезает, ничего не наезжает и не сплющивается
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            card.GetComponent<Image>().color = PanelColor;
            Frame(card);
            var cardLe = card.AddComponent<LayoutElement>();
            cardLe.minHeight = 0f;
            cardLe.preferredHeight = OfferCompactHeight; // биндер анимирует до OfferExpandedHeight
            cardLe.flexibleHeight = 0f;
            cardLe.flexibleWidth = 1f;
            ((RectTransform)card.transform).sizeDelta = new Vector2(440f, OfferCompactHeight); // стартовый размер для префаб-режима

            BuildCompactRow(card.transform);

            BuildExpandedBlock(card.transform, SummaryHeight, false, "Accept", "Взять заказ · Đ 0", TaxiColor, bold: true, visible: false);

            // «+»/«−»: аккуратный квадратик 34×34 в правом верхнем углу поверх рамки,
            // позиция не зависит от текстов и состояния
            var expand = Button(card.transform, "+", ButtonColor, BrightColor, 18f, "ExpandToggle", framed: false);
            FixedSize(expand, 34f, 34f);
            var expandRect = (RectTransform)expand.transform;
            expandRect.anchorMin = Vector2.one;
            expandRect.anchorMax = Vector2.one;
            expandRect.pivot = Vector2.one;
            // ignoreLayout отключает лейаут-контроль — размер руками, иначе виснет
            // дефолтный sizeDelta 100×100 от new RectTransform и кнопка наездает на карточку
            expandRect.sizeDelta = new Vector2(34f, 34f);
            expandRect.anchoredPosition = new Vector2(-4f, -4f);
            expand.GetComponent<LayoutElement>().ignoreLayout = true;

            return card;
        }

        // Пути узлов (контракт для OnboardBinder):
        //   Expanded/DetailAvatar, Expanded/Summary/{Name,From,To,RouteLen,Status,Wish}, Cancel
        // Карточка активного заказа: ТОТ ЖЕ Expanded всегда (компакта и тоггла нет),
        // внизу — красная «Отменить · −Đ N» со штрафом (сумму ставит биндер).
        public static GameObject BuildActiveOrderCard()
        {
            var card = new GameObject("ActiveOrder", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = CardSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            card.GetComponent<Image>().color = PanelColor;
            Frame(card);
            var cardLe = card.AddComponent<LayoutElement>();
            cardLe.minHeight = 0f;
            cardLe.preferredHeight = ActiveCardHeight;
            cardLe.flexibleHeight = 0f;
            cardLe.flexibleWidth = 1f;
            ((RectTransform)card.transform).sizeDelta = new Vector2(440f, ActiveCardHeight);

            BuildExpandedBlock(card.transform, ActiveSummaryHeight, false, "Cancel", "Отменить · −Đ 0", DangerColor * 0.9f, bold: false, visible: true);

            return card;
        }

        // компактный контейнер: узкий аватар в рамке слева + текстовая колонка
        private static void BuildCompactRow(Transform parent)
        {
            var compact = H("Compact", parent);
            FixedHeight(compact, 108f);
            // childForceExpandWidth у H() растянул бы аватар на пол-ряда вопреки
            // preferred-ширине — Info(flexible=1) сам забирает всё лишнее
            compact.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;

            var avatar = new GameObject("Avatar", typeof(RectTransform));
            avatar.transform.SetParent(compact.transform, false);
            RectBorder(avatar, 4f, AvatarFrameColor); // Outline на портретном Image рисует 4 призрака фигуры
            // фиксированный размер: любой портрет в одинаковой узкой колонке;
            // картинка — child с отступом от рамки, фигура не прижата и не срезана
            FixedWidth(avatar, 84f);
            FixedHeight(avatar, 108f);
            var picture = new GameObject("Picture", typeof(RectTransform), typeof(Image));
            picture.transform.SetParent(avatar.transform, false);
            var pictureImage = picture.GetComponent<Image>();
            pictureImage.preserveAspect = true;
            pictureImage.color = new Color(0.06f, 0.2f, 0.13f, 0.9f);
            var pictureRect = (RectTransform)picture.transform;
            pictureRect.anchorMin = Vector2.zero;
            pictureRect.anchorMax = Vector2.one;
            pictureRect.offsetMin = new Vector2(7f, 7f);
            pictureRect.offsetMax = new Vector2(-7f, -7f);

            var column = new GameObject("Info", typeof(RectTransform));
            column.transform.SetParent(compact.transform, false);
            var columnLayout = column.AddComponent<VerticalLayoutGroup>();
            columnLayout.spacing = 2;
            columnLayout.childControlWidth = true;
            columnLayout.childControlHeight = true;
            columnLayout.childForceExpandWidth = true;
            columnLayout.childForceExpandHeight = false;
            var columnLe = column.AddComponent<LayoutElement>();
            columnLe.flexibleWidth = 1f;
            columnLe.minWidth = 100f;
            columnLe.preferredWidth = 1f;

            var name = Text(column, "Имя", 23f, BrightColor, "Name");
            // 31, не 28: строка 23pt ≈ 30.4 — Ellipsis при меньшей высоте
            // обрезает имя пассажира до нуля видимых символов
            FixedHeight(name.gameObject, 31f);
            name.alignment = TextAlignmentOptions.Left;
            var route = Text(column, "ПЛАНЕТА А → ПЛАНЕТА Б", 16f, LabelColor, "Route");
            FixedHeight(route.gameObject, 22f);
            route.alignment = TextAlignmentOptions.Left;
            var dist = Text(column, "Расстояние до пассажира: 0 юн.", 16f, LabelColor, "Dist");
            FixedHeight(dist.gameObject, 22f);
            dist.alignment = TextAlignmentOptions.Left;
        }

        // раскрытый контейнер: портрет на всю ширину в рамке + сводка + кнопка действия
        private static void BuildExpandedBlock(Transform parent, float summaryHeight, bool withStatus,
            string actionName, string actionLabel, Color actionBackground, bool bold, bool visible)
        {
            var expanded = new GameObject("Expanded", typeof(RectTransform));
            expanded.transform.SetParent(parent, false);
            var layout = expanded.AddComponent<VerticalLayoutGroup>();
            layout.spacing = CardSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var expandedLe = expanded.AddComponent<LayoutElement>();
            expandedLe.flexibleWidth = 1f;
            expanded.SetActive(visible); // карточка входящего стартует свёрнутой, активная — раскрытой

            var avatar = new GameObject("DetailAvatar", typeof(RectTransform));
            avatar.transform.SetParent(expanded.transform, false);
            RectBorder(avatar, 4f, AvatarFrameColor); // Outline на портретном Image рисует 4 призрака фигуры
            var picture = new GameObject("Picture", typeof(RectTransform), typeof(Image));
            picture.transform.SetParent(avatar.transform, false);
            var pictureImage = picture.GetComponent<Image>();
            pictureImage.preserveAspect = true;
            pictureImage.color = new Color(0.06f, 0.2f, 0.13f, 0.9f);
            var pictureRect = (RectTransform)picture.transform;
            pictureRect.anchorMin = Vector2.zero;
            pictureRect.anchorMax = Vector2.one;
            pictureRect.offsetMin = new Vector2(7f, 7f);
            pictureRect.offsetMax = new Vector2(-7f, -7f);
            var avatarLe = avatar.AddComponent<LayoutElement>();
            avatarLe.flexibleWidth = 1f; // полоса на всю ширину карточки; фигура по центру (preserveAspect)
            avatarLe.minHeight = DetailAvatarHeight;
            avatarLe.preferredHeight = DetailAvatarHeight;
            avatarLe.flexibleHeight = 0f;

            // сводка: строки фиксированной высоты, лейблы LabelColor / значения Bright — биндер
            var summary = new GameObject("Summary", typeof(RectTransform));
            summary.transform.SetParent(expanded.transform, false);
            var summaryLayout = summary.AddComponent<VerticalLayoutGroup>();
            summaryLayout.spacing = 2;
            summaryLayout.childControlWidth = true;
            summaryLayout.childControlHeight = true;
            summaryLayout.childForceExpandWidth = true;
            summaryLayout.childForceExpandHeight = false;
            var summaryLe = summary.AddComponent<LayoutElement>();
            summaryLe.flexibleWidth = 1f;
            summaryLe.minWidth = 100f;
            summaryLe.preferredWidth = 1f;

            var name = Text(summary, "Имя: —", 19f, BrightColor, "Name");
            FixedHeight(name.gameObject, 30f);
            name.alignment = TextAlignmentOptions.Left;
            var from = Text(summary, "Откуда: —", 17f, BrightColor, "From");
            FixedHeight(from.gameObject, 26f);
            from.alignment = TextAlignmentOptions.Left;
            var to = Text(summary, "Куда: —", 17f, BrightColor, "To");
            FixedHeight(to.gameObject, 26f);
            to.alignment = TextAlignmentOptions.Left;
            var routeLen = Text(summary, "Примерная длина маршрута: 0 юн", 17f, BrightColor, "RouteLen");
            FixedHeight(routeLen.gameObject, 26f);
            routeLen.alignment = TextAlignmentOptions.Left;
            if (withStatus)
            {
                var status = Text(summary, "статус", 17f, BrightColor, "Status");
                FixedHeight(status.gameObject, 26f);
                status.alignment = TextAlignmentOptions.Left;
            }
            // «Пожелание клиента»: единственное исключение из политики — wrap
            // ровно на 2 строки фиксированной высоты, дальше «…»
            var wish = Text(summary, "Пожелание клиента: —", 17f, BrightColor, "Wish");
            FixedHeight(wish.gameObject, 54f);
            wish.alignment = TextAlignmentOptions.Left;
            wish.textWrappingMode = TextWrappingModes.Normal;

            var action = Button(expanded.transform, actionLabel, actionBackground, ActionTextColor, 17f, actionName);
            var actionLe = action.AddComponent<LayoutElement>();
            actionLe.minHeight = AcceptHeight;
            actionLe.flexibleWidth = 1f;
            if (bold)
            {
                var actionText = action.GetComponentInChildren<TMP_Text>();
                if (actionText != null)
                    actionText.fontStyle = FontStyles.Bold;
            }
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // --- Общие помощники ---

        public static GameObject H(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;
            return go;
        }

        // зелёная рамка по контуру узла (карточка/аватар) — UI.Outline, цвет лейблов
        public static void Frame(GameObject go, float thickness = 2f)
        {
            var outline = go.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = LabelColor;
            outline.effectDistance = new Vector2(thickness, -thickness);
        }

        // рамка прямоугольником из 4 тонких полосок — для узлов с прозрачной
        // картинкой: UI.Outline продублировал бы сам портрет 4 раза со смещением
        public static void RectBorder(GameObject go, float thickness = 2f, Color? color = null)
        {
            var parent = (RectTransform)go.transform;
            Color frameColor = color ?? LabelColor;
            void Bar(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
            {
                var bar = new GameObject(name, typeof(RectTransform), typeof(Image));
                bar.transform.SetParent(parent, false);
                var img = bar.GetComponent<Image>();
                img.color = frameColor;
                img.raycastTarget = false;
                var rt = (RectTransform)bar.transform;
                rt.anchorMin = anchorMin;
                rt.anchorMax = anchorMax;
                rt.offsetMin = offsetMin;
                rt.offsetMax = offsetMax;
            }
            Bar("BorderTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -thickness), new Vector2(0f, 0f));
            Bar("BorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, thickness));
            Bar("BorderLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(thickness, 0f));
            Bar("BorderRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-thickness, 0f), new Vector2(0f, 0f));
        }

        // элемент шапки, прижатый к левому/правому краю строки; вне лейаута.
        // LayoutElement обязателен: без него ignoreLayout не на чем ставить,
        // HorizontalLayoutGroup (childControlWidth) перебивает ручные якоря
        // и «центральные» деньги уезжают, а края схлопываются.
        // Высота 48, а не 44: строка 36pt JetBrainsMono имеет preferredHeight
        // ≈ 47.5 — при меньшем прямоугольнике Ellipsis-переполнение обрезает
        // текст ПО ВЕРТИКАЛИ до нуля символов (шапка «есть, но невидима»)
        public static void AnchorHeaderEdge(RectTransform rect, bool isLeft)
        {
            float edge = isLeft ? 0f : 1f;
            rect.anchorMin = new Vector2(edge, 0.5f);
            rect.anchorMax = new Vector2(edge, 0.5f);
            rect.pivot = new Vector2(edge, 0.5f);
            rect.sizeDelta = new Vector2(240f, 48f);
            rect.anchoredPosition = new Vector2(isLeft ? 12f : -12f, 0f);
            Le(rect.gameObject).ignoreLayout = true;
        }

        // деньги: ровно по центру ширины панели, независимо от соседних текстов
        public static void AnchorHeaderCenter(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(260f, 48f);
            rect.anchoredPosition = Vector2.zero;
            Le(rect.gameObject).ignoreLayout = true;
        }

        // однолинейный текст с обрезкой «…»: базовая политика панели.
        // ВАЖНО: Ellipsis режет и по вертикали — узел с этим текстом обязан
        // иметь высоту ≥ preferredHeight строки (36pt ≈ 47.5, 21pt ≈ 27.7,
        // 23pt ≈ 30.4), иначе текст «есть, но невидим» (ноль видимых символов)
        public static TMP_Text Text(GameObject parent, string content, float size, Color color, string name = "Text")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent.transform, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = currentFont != null ? currentFont : TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.color = color;
            text.text = content;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        public static GameObject Button(Transform parent, string label, Color background, Color textColor, float size,
            string name = null, bool framed = true)
        {
            var go = new GameObject(name ?? label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var bg = go.GetComponent<Image>();
            bg.color = background;
            // рамка и светлый верх — силуэт кнопки читается сразу; мелким тогглам не нужна
            if (framed)
            {
                var outline = go.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = new Color(0.55f, 0.85f, 0.7f, 0.55f);
                outline.effectDistance = new Vector2(2f, -2f);
            }
            var text = Text(go, label, size, textColor);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = true;
            // хук первым — щелчок звучит до обработчиков, добавленных вызывателем
            UiSfx.Hook(go.GetComponent<Button>());
            return go;
        }

        // кнопки плеера: без масштабирования при нажатии — только цветовой отклик
        private static Button PlayerButton(Transform parent, string glyph, string name)
        {
            var go = Button(parent, glyph, new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.22f), BrightColor, 18f, name);
            FixedSize(go, 44f, 44f);
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.fadeDuration = 0.1f;
            colors.highlightedColor = new Color(0.82f, 1f, 0.9f);
            colors.pressedColor = new Color(0.6f, 1f, 0.78f);
            button.colors = colors;
            return button;
        }

        // get-or-add: один LayoutElement на узел (Flexible + Fixed* поверх него)
        private static LayoutElement Le(GameObject go)
        {
            var le = go.GetComponent<LayoutElement>();
            return le != null ? le : go.AddComponent<LayoutElement>();
        }

        public static void FixedSize(GameObject go, float width, float height)
        {
            var le = Le(go);
            le.minWidth = width;
            le.minHeight = height;
            le.preferredWidth = width;
            le.preferredHeight = height;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
        }

        public static void FixedHeight(GameObject go, float height)
        {
            var le = Le(go);
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
        }

        public static void FixedWidth(GameObject go, float width)
        {
            var le = Le(go);
            le.minWidth = width;
            le.preferredWidth = width;
            le.flexibleWidth = 0f;
        }

        public static void Flexible(GameObject go)
        {
            Le(go).flexibleWidth = 1f;
        }
    }
}
