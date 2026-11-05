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
        public Image MoneyIcon, RatingIcon;
        public GameObject ActiveSection;   // секция целиком — скрывается без активного заказа
        public RectTransform ActiveArea;   // блок активного заказа + кнопка отмены (ведёт биндер)
        public RectTransform OrderArea;    // контент скролла входящих карточек (ведёт биндер)
        public TMP_Text EconCounters, DebtValue;
        public Button AbandonButton;
        public Button PlayerPrev, PlayerPlay, PlayerNext;
        public TMP_Text TrackTitle;
        public RectTransform ProgressFill;
        public TMP_FontAsset Font;
    }

    // Строит каркас бортового компьютера: [статус-строка] → [активный заказ] →
    // [входящие со скроллом] → [эфир-строка] → [система]. Высоты секций
    // фиксированные, скролл только в списке входящих (паттерн
    // StationScreenController). Из редактора результат сериализуется в сцену;
    // в рантайме биндер строит каркас этим же методом поверх старого мокапа
    // (Frame/Background сохраняются, остальное сносится).
    public static class OnboardComputerFactory
    {
        // используется и рантайм-карточками
        private static TMP_FontAsset currentFont;

        public const float MoneyValueWidth = 180f; // «999 999 кр» при 30 pt моноширинного

        private const string CashIconPath = "Assets/Sprites/cash.png";
        private const string RatingIconPath = "Assets/Sprites/zvezda.png";

        public static readonly Color LabelColor = new Color(0.357f, 0.788f, 0.561f);
        public static readonly Color BrightColor = new Color(0.659f, 1f, 0.796f);
        public static readonly Color TaxiColor = new Color(1f, 0.831f, 0.302f);
        public static readonly Color PanelColor = new Color(0.031f, 0.078f, 0.047f, 0.92f);
        public static readonly Color DangerColor = new Color(1f, 0.478f, 0.361f);

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

            // --- Статус-строка: часы | деньги (крупно) | рейтинг ---
            var header = H("Header", root.transform);
            FixedHeight(header, 44f);
            refs.Clock = Text(header, "14:02", 26f, BrightColor, "Clock");
            FixedWidth(refs.Clock.gameObject, 84f);
            refs.Clock.alignment = TextAlignmentOptions.Left;
            var spacer = Text(header, "", 18f, LabelColor, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.MoneyValue = IconStat(header.transform, CashIconPath, "MoneyValue",
                30f, MoneyValueWidth, TaxiColor, out refs.MoneyIcon);
            refs.MoneyValue.alignment = TextAlignmentOptions.Right;
            refs.RatingValue = IconStat(header.transform, RatingIconPath, "RatingValue",
                22f, 56f, BrightColor, out refs.RatingIcon);
            refs.RatingValue.alignment = TextAlignmentOptions.Left;

            // --- АКТИВНЫЙ ЗАКАЗ: блок и кнопку отмены строит биндер ---
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

            // --- ЭФИР: плеер одной строкой (несжимаемая секция) ---
            var player = new GameObject("PlayerSection", typeof(RectTransform));
            player.transform.SetParent(root.transform, false);
            var playerLayout = player.AddComponent<VerticalLayoutGroup>();
            playerLayout.spacing = 4;
            playerLayout.childControlWidth = true;
            playerLayout.childControlHeight = true;
            playerLayout.childForceExpandWidth = true;
            playerLayout.childForceExpandHeight = false;
            var playerRow = H("PlayerRow", player.transform);
            FixedHeight(playerRow, 30f);
            var radio = Text(playerRow, "ЭФИР", 15f, LabelColor, "RadioLabel");
            FixedWidth(radio.gameObject, 44f);
            radio.alignment = TextAlignmentOptions.Left;
            refs.PlayerPrev = PlayerButton(playerRow.transform, "|<", "PlayerPrev");
            refs.PlayerPlay = PlayerButton(playerRow.transform, "▶", "PlayerPlay"); // глиф ведёт биндер: ▶/||
            refs.TrackTitle = Text(playerRow, "МУЗЫКА · —", 19f, BrightColor, "Track");
            refs.TrackTitle.alignment = TextAlignmentOptions.Center;
            refs.TrackTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.PlayerNext = PlayerButton(playerRow.transform, ">|", "PlayerNext");
            var progress = new GameObject("Progress", typeof(RectTransform), typeof(Image));
            progress.transform.SetParent(player.transform, false);
            progress.GetComponent<Image>().color = new Color(0.06f, 0.2f, 0.13f);
            FixedHeight(progress, 4f);
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

            // --- СИСТЕМА: смены/аварии, долг, отключение ---
            var system = Section(root.transform, "// СИСТЕМА", "SystemSection");
            // 17pt: строка счётчиков с длинными числами не переносится в 448 px
            refs.EconCounters = Text(system, "", 17f, LabelColor, "EconCounters");
            FixedHeight(refs.EconCounters.gameObject, 22f);
            refs.EconCounters.alignment = TextAlignmentOptions.Left;
            refs.DebtValue = Text(system, "", 18f, BrightColor, "DebtValue");
            FixedHeight(refs.DebtValue.gameObject, 22f);
            var abandon = Button(system.transform, "ОТКЛЮЧИТЬ КОРАБЛЬ", DangerColor * 0.9f,
                new Color(0.05f, 0.02f, 0.03f), 18f, "AbandonButton");
            var abandonLe = abandon.AddComponent<LayoutElement>();
            abandonLe.minHeight = 38f;
            abandonLe.flexibleWidth = 1f;
            refs.AbandonButton = abandon.GetComponent<Button>();
            abandon.SetActive(false);

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
            FixedHeight(heading.gameObject, 26f);
            var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(section.transform, false);
            line.GetComponent<Image>().color = new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.4f);
            FixedHeight(line, 2f);
            return section;
        }

        private static TMP_Text IconStat(Transform parent, string iconPath, string name,
            float valueSize, float valueWidth, Color valueColor, out Image icon)
        {
            var stat = new GameObject(name, typeof(RectTransform));
            stat.transform.SetParent(parent, false);
            var layout = stat.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(stat.transform, false);
            var iconImage = iconGo.GetComponent<Image>();
            iconImage.sprite = LoadSprite(iconPath);
            iconImage.preserveAspect = true;
            iconImage.color = TaxiColor;
            FixedSize(iconGo, 24f, 24f);

            var value = Text(stat, "0", valueSize, valueColor, "Value");
            FixedWidth(value.gameObject, valueWidth);
            icon = iconImage;
            return value;
        }

        private static Sprite LoadSprite(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
#else
            return null;
#endif
        }

        private static Button PlayerButton(Transform parent, string glyph, string name)
        {
            var go = Button(parent, glyph, new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.22f), BrightColor, 15f, name);
            FixedSize(go, 26f, 26f);
            return go.GetComponent<Button>();
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

        public static TMP_Text Text(GameObject parent, string content, float size, Color color, string name = "Text")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent.transform, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = currentFont != null ? currentFont : TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.color = color;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        public static GameObject Button(Transform parent, string label, Color background, Color textColor, float size, string name = null)
        {
            var go = new GameObject(name ?? label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = background;
            var text = Text(go, label, size, textColor);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = true;
            // хук первым — щелчок звучит до обработчиков, добавленных вызывателем
            UiSfx.Hook(go.GetComponent<Button>());
            return go;
        }

        public static void FixedSize(GameObject go, float width, float height)
        {
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.minHeight = height;
            le.preferredWidth = width;
            le.preferredHeight = height;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
        }

        public static void FixedHeight(GameObject go, float height)
        {
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
        }

        public static void FixedWidth(GameObject go, float width)
        {
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.preferredWidth = width;
            le.flexibleWidth = 0f;
        }

        public static void Flexible(GameObject go)
        {
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        // --- Карточки заказов ---

        // Карточка входящего: 2 строки текста + кнопки справа.
        // [аватар | имя … цена / маршрут · задание | ✓ | ✕]
        public static GameObject BuildOfferCard()
        {
            var card = new GameObject("OfferCard", typeof(RectTransform), typeof(Image));
            var layout = card.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            card.GetComponent<Image>().color = PanelColor;
            var le = card.AddComponent<LayoutElement>();
            le.minHeight = 56f;
            le.flexibleWidth = 1f;
            ((RectTransform)card.transform).sizeDelta = new Vector2(440f, 62f); // стартовый размер для префаб-режима

            var avatar = new GameObject("Avatar", typeof(RectTransform), typeof(Image));
            avatar.transform.SetParent(card.transform, false);
            var avatarImage = avatar.GetComponent<Image>();
            avatarImage.preserveAspect = true;
            avatarImage.color = new Color(0.06f, 0.2f, 0.13f, 0.9f);
            FixedSize(avatar, 40f, 40f);

            var column = new GameObject("Info", typeof(RectTransform));
            column.transform.SetParent(card.transform, false);
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

            var nameRow = H("NameRow", column.transform);
            TMP_Text name = Text(nameRow, "Имя · архетип", 17f, BrightColor, "Name");
            Flexible(name.gameObject);
            name.alignment = TextAlignmentOptions.Left;
            TMP_Text price = Text(nameRow, "0 кр", 16f, TaxiColor, "Price");
            FixedWidth(price.gameObject, 88f);
            price.alignment = TextAlignmentOptions.Right;
            Text(column, "ПЛАНЕТА А → ПЛАНЕТА Б · 0 юн", 14f, LabelColor, "Route");

            var accept = Button(card.transform, "✓", TaxiColor, new Color(0.04f, 0.06f, 0.1f), 20f, "Accept");
            FixedSize(accept, 36f, 36f);
            var decline = Button(card.transform, "✕", new Color(DangerColor.r, DangerColor.g, DangerColor.b, 0.3f), DangerColor, 20f, "Decline");
            FixedSize(decline, 36f, 36f);

            return card;
        }

        // Блок активного заказа (крупный): [фото | имя / маршрут / статус / задание]
        public static GameObject BuildActiveOrderBlock()
        {
            var row = new GameObject("ActiveOrder", typeof(RectTransform), typeof(Image));
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(10, 10, 10, 10);
            rowLayout.spacing = 10;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            row.GetComponent<Image>().color = PanelColor;
            row.AddComponent<LayoutElement>().minHeight = 104f;
            ((RectTransform)row.transform).sizeDelta = new Vector2(440f, 116f); // стартовый размер для префаб-режима

            var photo = new GameObject("Photo", typeof(RectTransform), typeof(Image));
            photo.transform.SetParent(row.transform, false);
            var photoImage = photo.GetComponent<Image>();
            photoImage.preserveAspect = true;
            photoImage.color = new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.3f);
            FixedSize(photo, 64f, 64f);

            var column = new GameObject("Info", typeof(RectTransform));
            column.transform.SetParent(row.transform, false);
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

            Text(column, "Имя · архетип", 20f, BrightColor, "Name");
            Text(column, "ПЛАНЕТА А → ПЛАНЕТА Б", 18f, TaxiColor, "Route");
            Text(column, "статус", 15f, LabelColor, "OrderStatus");
            Text(column, "«пожелание»", 15f, LabelColor, "Wish");

            return row;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
