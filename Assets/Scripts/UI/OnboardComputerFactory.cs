using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceTaxi.UI
{
    // Ссылки на построенные элементы бортового компьютера.
    public sealed class OnboardRefs
    {
        public TMP_Text Clock, ShipState1, ShipState2, ShipState3;
        public TMP_Text SysState2, SysState3, EconCounters, OrderPay;
        public TMP_Text MoneyValue, RatingValue;
        public Button AbandonButton;
        public RectTransform OrderArea;
        public TMP_FontAsset Font;
    }

    // Строит каркас бортового компьютера на layout-группах. Используется
    // из редактора (Space Taxi -> Rebuild Onboard Computer) — результат
    // сериализуется в сцену и свободно правится руками; из рантайма —
    // только как фолбэк для старых сцен.
    public static class OnboardComputerFactory
    {
        // Шрифт каркаса; используется и рантайм-карточками.
        private static TMP_FontAsset currentFont;

        public static readonly Color LabelColor = new Color(0.357f, 0.788f, 0.561f);
        public static readonly Color BrightColor = new Color(0.659f, 1f, 0.796f);
        public static readonly Color TaxiColor = new Color(1f, 0.831f, 0.302f);
        public static readonly Color PanelColor = new Color(0.031f, 0.078f, 0.047f, 0.92f);
        public static readonly Color DangerColor = new Color(1f, 0.478f, 0.361f);

        public static OnboardRefs Build(Transform panel, bool destroyImmediate = false)
        {
            // Шрифт — у старого мокапа, если он ещё стоит.
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            var oldTitle = panel.Find("Title");
            if (oldTitle != null)
                font = oldTitle.GetComponent<TMP_Text>().font;
            currentFont = font;

            // Старовое содержимое сносим (кроме рамки и фона).
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
            rootLayout.padding = new RectOffset(24, 24, 8, 12);
            rootLayout.spacing = 8;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            var refs = new OnboardRefs { Font = font };

            // --- Шапка: два ряда ---
            var headerWrap = new GameObject("Header", typeof(RectTransform));
            headerWrap.transform.SetParent(root.transform, false);
            var wrapLayout = headerWrap.AddComponent<VerticalLayoutGroup>();
            wrapLayout.spacing = 2;
            wrapLayout.childControlWidth = true;
            wrapLayout.childControlHeight = true;
            wrapLayout.childForceExpandWidth = true;
            wrapLayout.childForceExpandHeight = false;

            var titleRow = H("TitleRow", headerWrap.transform);
            titleRow.AddComponent<LayoutElement>().minHeight = 34f;
            var dot = Text(titleRow, "<voffset=0.35em>●</voffset>", 18f, LabelColor, "LinkDot");
            dot.alignment = TextAlignmentOptions.Left;
            FixedWidth(dot.gameObject, 24f);
            var title = Text(titleRow, "БОРТОВОЙ КОМПЬЮТЕР", 24f, BrightColor, "Title");
            Flexible(title.gameObject);
            refs.Clock = Text(titleRow, "14:02", 28f, BrightColor, "Clock");
            FixedWidth(refs.Clock.gameObject, 92f);

            var statsRow = H("StatsRow", headerWrap.transform);
            var spacer = Text(statsRow, "", 18f, LabelColor, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            refs.MoneyValue = IconStat(statsRow.transform, "Assets/Sprites/cash.png", "MoneyValue");
            refs.RatingValue = IconStat(statsRow.transform, "Assets/Sprites/zvezda.png", "RatingValue");

            // --- СОСТОЯНИЕ ---
            var state = Section(root.transform, "// СОСТОЯНИЕ", "StateSection");
            refs.ShipState1 = Row(state, "ShipState1");
            refs.ShipState2 = Row(state, "ShipState2");
            refs.ShipState3 = Row(state, "ShipState3");
            refs.SysState2 = Row(state, "SysState2");
            refs.SysState3 = Row(state, "SysState3");
            var abandon = Button(state.transform, "ОТКЛЮЧИТЬ КОРАБЛЬ", DangerColor * 0.9f,
                new Color(0.05f, 0.02f, 0.03f), 20f, "AbandonButton");
            var abandonLe = abandon.AddComponent<LayoutElement>();
            abandonLe.minHeight = 38f;
            abandonLe.flexibleWidth = 1f;
            refs.AbandonButton = abandon.GetComponent<Button>();
            abandon.SetActive(false);

            // --- ЗАКАЗ (динамическая зона) ---
            var orderSection = Section(root.transform, "// ЗАКАЗ", "OrderSection");
            var orderContainer = new GameObject("OrderArea", typeof(RectTransform));
            orderContainer.transform.SetParent(orderSection.transform, false);
            var orderAreaLayout = orderContainer.AddComponent<VerticalLayoutGroup>();
            orderAreaLayout.spacing = 8;
            orderAreaLayout.childControlWidth = true;
            orderAreaLayout.childControlHeight = true;
            orderAreaLayout.childForceExpandWidth = true;
            orderAreaLayout.childForceExpandHeight = false;
            refs.OrderArea = (RectTransform)orderContainer.transform;
            refs.OrderPay = Text(orderSection, "", 19f, LabelColor, "OrderPay");
            FixedHeight(refs.OrderPay.gameObject, 24f);

            // --- СМЕНА ---
            var econ = Section(root.transform, "// СМЕНА", "EconSection");
            var econRow = H("EconRow", econ.transform);
            refs.EconCounters = Text(econRow, "0 доставок · 0 провалов", 20f, LabelColor, "EconCounters");
            refs.EconCounters.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // --- ЭФИР ---
            var player = Section(root.transform, "// ЭФИР", "PlayerSection");
            var playerRow = H("PlayerRow", player.transform);
            PlayerButton(playerRow.transform, "|<", "PlayerPrev");
            var station = Text(playerRow, "MTO RADIO", 22f, BrightColor, "Station");
            station.alignment = TextAlignmentOptions.Center;
            station.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            PlayerButton(playerRow.transform, ">|", "PlayerNext");
            var progress = new GameObject("Progress", typeof(RectTransform), typeof(Image));
            progress.transform.SetParent(player.transform, false);
            progress.GetComponent<Image>().color = new Color(0.06f, 0.2f, 0.13f);
            FixedHeight(progress, 4f);

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
            section.AddComponent<LayoutElement>().minHeight = 40f;

            var heading = Text(section, title, 21f, LabelColor, "Heading");
            FixedHeight(heading.gameObject, 26f);
            var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(section.transform, false);
            line.GetComponent<Image>().color = new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.4f);
            FixedHeight(line, 2f);
            return section;
        }

        private static TMP_Text Row(GameObject parent, string name)
        {
            var text = Text(parent, "—", 20f, BrightColor, name);
            FixedHeight(text.gameObject, 26f);
            return text;
        }

        private static TMP_Text IconStat(Transform parent, string iconPath, string name)
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

            var value = Text(stat, "0", 22f, BrightColor, "Value");
            FixedWidth(value.gameObject, 64f);
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

        private static GameObject PlayerButton(Transform parent, string glyph, string name)
        {
            var go = Button(parent, glyph, new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.22f), BrightColor, 15f, name);
            FixedSize(go, 26f, 26f);
            return go;
        }

        // --- Общие помощники (используются и рантаймом для карточек) ---

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


        // --- Шаблоны карточек заказов (уходят в префабы, редактируются руками) ---

        // Карточка входящего заказа: [аватар][тексты][кнопки].
        public static GameObject BuildOfferCard()
        {
            var card = new GameObject("OfferCard", typeof(RectTransform), typeof(Image));
            var layout = card.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 10;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            card.GetComponent<Image>().color = PanelColor;
            var le = card.AddComponent<LayoutElement>();
            le.minHeight = 108f;
            le.minWidth = 410f;
            le.preferredWidth = 410f;
            le.flexibleWidth = 0f;

            var avatar = new GameObject("Avatar", typeof(RectTransform), typeof(Image));
            avatar.transform.SetParent(card.transform, false);
            var avatarImage = avatar.GetComponent<Image>();
            avatarImage.preserveAspect = true;
            avatarImage.color = new Color(0.06f, 0.2f, 0.13f, 0.9f);
            FixedSize(avatar, 64f, 64f);

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

            TMP_Text name = Text(column, "Имя · архетип", 20f, BrightColor, "Name");
            TMP_Text route = Text(column, "ПЛАНЕТА А → ПЛАНЕТА Б · 0 юн", 17f, LabelColor, "Route");
            Text(column, "доп.: «пожелание»", 15f, LabelColor, "Wish");

            var buttons = new GameObject("Buttons", typeof(RectTransform));
            buttons.transform.SetParent(card.transform, false);
            var buttonsLayout = buttons.AddComponent<VerticalLayoutGroup>();
            buttonsLayout.spacing = 6;
            buttonsLayout.childControlWidth = true;
            buttonsLayout.childControlHeight = true;
            buttonsLayout.childForceExpandWidth = false;
            buttonsLayout.childForceExpandHeight = false;

            var accept = Button(buttons.transform, "✓", TaxiColor, new Color(0.04f, 0.06f, 0.1f), 26f, "Accept");
            FixedSize(accept, 44f, 44f);
            var decline = Button(buttons.transform, "✕", new Color(DangerColor.r, DangerColor.g, DangerColor.b, 0.3f), DangerColor, 26f, "Decline");
            FixedSize(decline, 44f, 44f);

            return card;
        }

        // Блок активного заказа: [фото][тексты].
        public static GameObject BuildActiveOrderBlock()
        {
            var row = new GameObject("ActiveOrder", typeof(RectTransform), typeof(Image));
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(10, 10, 10, 10);
            rowLayout.spacing = 10;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;
            row.GetComponent<Image>().color = PanelColor;
            row.AddComponent<LayoutElement>().minHeight = 120f;

            var photo = new GameObject("Photo", typeof(RectTransform), typeof(Image));
            photo.transform.SetParent(row.transform, false);
            var photoImage = photo.GetComponent<Image>();
            photoImage.preserveAspect = true;
            photoImage.color = new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.3f);
            FixedSize(photo, 96f, 96f);

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

            Text(column, "Имя · архетип", 24f, BrightColor, "Name");
            Text(column, "ПЛАНЕТА А → ПЛАНЕТА Б", 22f, TaxiColor, "Route");
            Text(column, "статус", 19f, LabelColor, "OrderStatus");
            Text(column, "«пожелание»", 17f, LabelColor, "Wish");

            return row;
        }

        public static void SetFont(TMP_FontAsset font) => currentFont = font;

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
