using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Бортовой компьютер: панель строится кодом на автоматических layout-группах
    // (VerticalLayoutGroup/HorizontalLayoutGroup + LayoutElement). Дети живут
    // только внутри родителей — ничего не налезает, новые секции добавляются
    // в стек и всё сдвигается само. Вешается на OnboardComputer.
    public class OnboardBinder : MonoBehaviour
    {
        private TMP_FontAsset font;

        private TMP_Text clock;
        private TMP_Text shipState1, shipState2, shipState3;
        private TMP_Text sysState2, sysState3;
        private Button abandonButton;

        private RectTransform orderArea;      // контейнер зоны заказа (переключается)
        private GameObject econSection;
        private TMP_Text econCounters;
        private TMP_Text orderPay;
        private Button cancelButton;

        private Ship ship;
        private GravityField gravity;
        private OrderService orders;
        private HullSystem hull;
        private float nextSlowUpdate;
        private bool bound;
        private readonly List<GameObject> orderAreaChildren = new List<GameObject>();

        private static readonly Color LabelColor = new Color(0.357f, 0.788f, 0.561f);   // #5BC98F
        private static readonly Color BrightColor = new Color(0.659f, 1f, 0.796f);      // #A8FFCB
        private static readonly Color TaxiColor = new Color(1f, 0.831f, 0.302f);        // #FFD44D
        private static readonly Color PanelColor = new Color(0.031f, 0.078f, 0.047f, 0.92f);
        private static readonly Color DangerColor = new Color(1f, 0.478f, 0.361f);

        private void Awake()
        {
            // Шрифт забираем у старого мокапа до сноса.
            var oldTitle = transform.Find("Title");
            font = oldTitle != null ? oldTitle.GetComponent<TMP_Text>().font : TMP_Settings.defaultFontAsset;

            // Мокап сносим: панель пересобираем на layout-ах.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                string n = transform.GetChild(i).name;
                if (n != "Frame" && n != "Background")
                    Destroy(transform.GetChild(i).gameObject);
            }

            BuildSkeleton();
        }

        // --- Каркас панели: вертикальный стек секций ---

        private void BuildSkeleton()
        {
            var root = new GameObject("Stack", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            Stretch((RectTransform)root.transform);
            var rootLayout = root.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(24, 24, 8, 12);
            rootLayout.spacing = 8;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;
            root.AddComponent<LayoutElement>().flexibleHeight = 0f;

            // Шапка: связь + название + часы.
            var header = H("Header", root.transform);
            var dot = Text(header, "<voffset=0.35em>●</voffset>", 18f, LabelColor);
            dot.alignment = TextAlignmentOptions.Left;
            FixedWidth(dot.gameObject, 24f);
            var title = Text(header, "БОРТОВОЙ КОМПЬЮТЕР", 24f, BrightColor);
            Flexible(title.gameObject);
            clock = Text(header, "14:02", 30f, BrightColor);
            FixedWidth(clock.gameObject, 96f);

            // Секция состояния.
            var state = Section(root.transform, "// СОСТОЯНИЕ", "StateSection");
            shipState1 = Row(state, "—");
            shipState2 = Row(state, "—");
            shipState3 = Row(state, "—");
            sysState2 = Row(state, "—");
            sysState3 = Row(state, "");
            Abandon(state.transform);

            // Секция заказа: контейнер переключается (текущий / входящие).
            var orderSection = Section(root.transform, "// ЗАКАЗ", "OrderSection");
            var orderSectionLayout = orderSection.AddComponent<LayoutElement>();
            orderSectionLayout.minHeight = 280f;
            orderSectionLayout.flexibleHeight = 1f;

            // OrderArea — сама layout-группа: дети (скролл/карточки/кнопки)
            // без неё остаются дефолтных 100x100 в центре.
            var orderContainer = new GameObject("OrderArea", typeof(RectTransform));
            orderContainer.transform.SetParent(orderSection.transform, false);
            var orderAreaLayout = orderContainer.AddComponent<VerticalLayoutGroup>();
            orderAreaLayout.spacing = 8;
            orderAreaLayout.childControlWidth = true;
            orderAreaLayout.childControlHeight = true;
            orderAreaLayout.childForceExpandWidth = true;
            orderAreaLayout.childForceExpandHeight = false;
            orderArea = (RectTransform)orderContainer.transform;
            orderArea.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            orderPay = Text(orderSection, "", 19f, LabelColor);
            FixedHeight(orderPay.gameObject, 24f);

            // Экономика (видна не всегда).
            econSection = Section(root.transform, "// СМЕНА", "EconSection");
            var econRow = H("EconRow", econSection.transform);
            var money = Text(econRow, "● 0", 24f, TaxiColor);
            FixedWidth(money.gameObject, 110f);
            var rating = Text(econRow, "★ 0.0", 24f, TaxiColor);
            FixedWidth(rating.gameObject, 120f);
            econCounters = Text(econRow, "0 доставок · 0 провалов", 18f, LabelColor);
            econCounters.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Плеер — внизу стека.
            var player = Section(root.transform, "// ЭФИР", "PlayerSection");
            var playerRow = H("PlayerRow", player.transform);
            PlayerButton(playerRow.transform, "|<");
            var station = Text(playerRow, "MTO RADIO", 22f, BrightColor);
            station.alignment = TextAlignmentOptions.Center;
            station.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            PlayerButton(playerRow.transform, ">|");
            var progress = new GameObject("Progress", typeof(RectTransform), typeof(Image));
            progress.transform.SetParent(player.transform, false);
            progress.GetComponent<Image>().color = new Color(0.06f, 0.2f, 0.13f);
            FixedHeight(progress, 4f);
        }

        private GameObject Section(Transform parent, string title, string name)
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

            var heading = Text(section, title, 21f, LabelColor);
            FixedHeight(heading.gameObject, 26f);
            var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(section.transform, false);
            line.GetComponent<Image>().color = new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.4f);
            FixedHeight(line, 2f);
            return section;
        }

        private void Abandon(Transform parent)
        {
            var go = Button(parent, "ОТКЛЮЧИТЬ КОРАБЛЬ", DangerColor * 0.9f,
                new Color(0.05f, 0.02f, 0.03f), 20f);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 38f;
            le.flexibleWidth = 1f;
            abandonButton = go.GetComponent<Button>();
            abandonButton.onClick.AddListener(() => hull.Abandon("КОРАБЛЬ ОТКЛЮЧЁН"));
            go.SetActive(false);
        }

        private TMP_Text Row(GameObject parent, string content)
        {
            var text = Text(parent, content, 20f, BrightColor);
            FixedHeight(text.gameObject, 26f);
            return text;
        }

        // --- Зона заказа: активный или список входящих ---

        private void RefreshOrder()
        {
            foreach (GameObject child in orderAreaChildren)
                Destroy(child);
            orderAreaChildren.Clear();

            bool hasActive = orders.Active != null;
            orderPay.gameObject.SetActive(hasActive);
            cancelButton = null;

            if (hasActive)
                BuildActiveOrder();
            else
                BuildIncoming();
        }

        private void BuildActiveOrder()
        {
            OrderOffer offer = orders.Active;

            var row = new GameObject("ActiveOrder", typeof(RectTransform), typeof(Image));
            row.transform.SetParent(orderArea, false);
            orderAreaChildren.Add(row);
            row.GetComponent<Image>().color = PanelColor;
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(10, 10, 10, 10);
            rowLayout.spacing = 10;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;

            // Фото пассажира в рамке.
            var photo = new GameObject("Photo", typeof(RectTransform), typeof(Image));
            photo.transform.SetParent(row.transform, false);
            var photoImage = photo.GetComponent<Image>();
            photoImage.preserveAspect = true;
            photoImage.color = new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.3f);
            if (offer.Passenger.Portrait != null)
                photoImage.sprite = offer.Passenger.Portrait;
            FixedSize(photo, 96f, 96f);

            // Тексты заказа.
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
            columnLe.preferredWidth = 1f; // текст не распирает карточку — перенос строк

            TMP_Text name = Text(column, offer.Passenger.DisplayName + " · " + offer.Passenger.Archetype, 24f, BrightColor);
            FixedHeight(name.gameObject, 30f);
            TMP_Text route = Text(column,
                orders.PlanetName(offer.PickupIndex).ToUpper() + " → " + orders.PlanetName(offer.DropoffIndex).ToUpper(),
                22f, TaxiColor);
            FixedHeight(route.gameObject, 28f);
            var status = Text(column, "...", 19f, LabelColor);
            FixedHeight(status.gameObject, 24f);
            status.name = "OrderStatus";

            string wish = PassengerProfile.PickPhrase(offer.Passenger.Wishes);
            if (!string.IsNullOrEmpty(wish))
            {
                var wishText = Text(column, "«" + wish + "»", 17f, LabelColor);
                wishText.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            }

            // Отказ: без пассажира — всегда, с пассажиром — только на планете.
            var cancel = Button(orderArea, "ОТКАЗАТЬСЯ ОТ ЗАКАЗА",
                new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.25f), LabelColor, 17f);
            var cancelLe = cancel.AddComponent<LayoutElement>();
            cancelLe.minHeight = 32f;
            cancelLe.flexibleWidth = 1f;
            cancelButton = cancel.GetComponent<Button>();
            cancelButton.onClick.AddListener(() => orders.CancelActive());
            orderAreaChildren.Add(cancel);

            orderPay.text = Col("Счёт:", orders.Deliveries + " доставок · " + orders.Failures + " провалов");
        }

        private void BuildIncoming()
        {
            // Скролл: viewport + content на VerticalLayoutGroup + ContentSizeFitter.
            var scrollGo = new GameObject("IncomingScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGo.transform.SetParent(orderArea, false);
            orderAreaChildren.Add(scrollGo);
            scrollGo.AddComponent<LayoutElement>().flexibleHeight = 1f;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; // без прокрутки в пустоту
            scroll.scrollSensitivity = 30f;
            scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            incomingScroll = scroll;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRect = (RectTransform)viewportGo.transform;
            Stretch(viewportRect);
            viewportRect.offsetMax = new Vector2(-14f, 0f); // место под полоску
            viewportGo.GetComponent<Image>().color = Color.white;
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;
            scroll.viewport = (RectTransform)viewportGo.transform;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            var contentLayout = contentGo.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 10;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = false;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            CreateScrollbar(scrollGo, scroll);

            foreach (OrderOffer offer in orders.Offers)
            {
                GameObject card = BuildCard(offer);
                card.transform.SetParent(content, false);
            }
        }

        // Полоска прокрутки справа; скрыта, когда контент помещается целиком.
        private ScrollRect incomingScroll;
        private GameObject scrollbarGo;

        private void CreateScrollbar(GameObject scrollObject, ScrollRect scroll)
        {
            var barGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            barGo.transform.SetParent(scrollObject.transform, false);
            var barRect = (RectTransform)barGo.transform;
            barRect.anchorMin = new Vector2(1f, 0f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 0.5f);
            barRect.sizeDelta = new Vector2(8f, 0f);
            barGo.GetComponent<Image>().color = new Color(0.06f, 0.2f, 0.13f, 0.9f);

            var bar = barGo.GetComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;

            var sliding = new GameObject("SlidingArea", typeof(RectTransform));
            sliding.transform.SetParent(barGo.transform, false);
            var slidingRect = (RectTransform)sliding.transform;
            Stretch(slidingRect);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(sliding.transform, false);
            Stretch((RectTransform)handle.transform);
            handle.GetComponent<Image>().color = LabelColor;
            bar.handleRect = (RectTransform)handle.transform;
            bar.targetGraphic = handle.GetComponent<Image>();

            scroll.verticalScrollbar = bar;
            scrollbarGo = barGo;
        }

        // Карточка: [аватар] [имя/маршрут/условие] [кнопки] — всё внутри контейнеров.
        private GameObject BuildCard(OrderOffer offer)
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
            var cardLe = card.AddComponent<LayoutElement>();
            cardLe.minHeight = 108f;
            cardLe.minWidth = 410f;
            cardLe.preferredWidth = 410f;
            cardLe.flexibleWidth = 0f;

            // Аватар в рамке.
            var avatar = new GameObject("Avatar", typeof(RectTransform), typeof(Image));
            avatar.transform.SetParent(card.transform, false);
            var avatarImage = avatar.GetComponent<Image>();
            avatarImage.preserveAspect = true;
            if (offer.Passenger.Portrait != null)
            {
                avatarImage.sprite = offer.Passenger.Portrait;
                avatarImage.color = offer.Passenger.PortraitTint;
            }
            else
            {
                avatarImage.color = new Color(0.06f, 0.2f, 0.13f, 0.9f);
            }
            FixedSize(avatar, 64f, 64f);

            // Тексты.
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
            columnLe.preferredWidth = 1f; // текст не распирает карточку — перенос строк

            TMP_Text name = Text(column, offer.Passenger.DisplayName + " · " + offer.Passenger.Archetype, 20f, BrightColor);
            TMP_Text route = Text(column,
                orders.PlanetName(offer.PickupIndex).ToUpper() + " → " + orders.PlanetName(offer.DropoffIndex).ToUpper()
                + "  ·  " + orders.RouteDistance(offer).ToString("0") + " юн", 17f, LabelColor);
            string wishPhrase = PassengerProfile.PickPhrase(offer.Passenger.Wishes);
            if (!string.IsNullOrEmpty(wishPhrase))
            {
                var wish = Text(column, "доп.: «" + wishPhrase + "»", 15f, LabelColor);
                wish.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            }

            // Кнопки-иконки в ряд: ✓ принять, ✕ отклонить — компактно, без пустого низа.
            var buttons = new GameObject("Buttons", typeof(RectTransform));
            buttons.transform.SetParent(card.transform, false);
            var buttonsLayout = buttons.AddComponent<HorizontalLayoutGroup>();
            buttonsLayout.spacing = 6;
            buttonsLayout.childControlWidth = true;
            buttonsLayout.childControlHeight = true;
            buttonsLayout.childForceExpandWidth = false;
            buttonsLayout.childForceExpandHeight = false;
            OrderOffer captured = offer;
            var accept = Button(buttons.transform, "✓", TaxiColor, new Color(0.04f, 0.06f, 0.1f), 26f);
            FixedSize(accept, 44f, 44f);
            accept.GetComponent<Button>().onClick.AddListener(() => orders.Accept(captured));
            var decline = Button(buttons.transform, "✕",
                new Color(DangerColor.r, DangerColor.g, DangerColor.b, 0.3f), DangerColor, 26f);
            FixedSize(decline, 44f, 44f);
            decline.GetComponent<Button>().onClick.AddListener(() => orders.Decline(captured));

            return card;
        }

        // --- Живые данные ---

        private void Update()
        {
            if (!bound)
            {
                var session = FindAnyObjectByType<GameSession>();
                ship = FindAnyObjectByType<Ship>();
                gravity = FindAnyObjectByType<GravityField>();
                if (session == null || session.Orders == null || ship == null || ship.Config == null)
                    return;
                orders = session.Orders;
                hull = ship.GetComponent<HullSystem>();
                orders.OffersChanged += RefreshOrder;
                orders.ActiveOrderChanged += RefreshOrder;
                RefreshOrder();
                bound = true;
            }

            if (Time.unscaledTime >= nextSlowUpdate)
            {
                nextSlowUpdate = Time.unscaledTime + 0.25f;
                SlowUpdate();
            }
        }

        private void SlowUpdate()
        {
            int minutes = 14 * 60 + 2 + (int)Time.time / 3;
            clock.text = minutes / 60 + ":" + (minutes % 60).ToString("00");

            shipState1.text = Col("Корпус:", Mathf.RoundToInt(ship.HpFraction * 100f) + "%");
            shipState2.text = Col("Топливо:", Mathf.RoundToInt(ship.FuelFraction * 100f) + "%");
            shipState3.text = Col("Статус:", ship.State == ShipState.Landed ? "посадка"
                : ship.Fuel <= 0f ? "дрейф без топлива" : "полёт");

            NearestBody(out string name, out float dist, out bool heat);
            sysState2.text = Col("Ближний объект:", name + " (" + dist.ToString("0") + " юн)");
            sysState3.text = heat ? "⚠ жаровая зона" : "";

            econCounters.text = orders.Deliveries + " доставок · " + orders.Failures + " провалов";
            abandonButton.gameObject.SetActive(ship.Fuel <= 0f);
            if (cancelButton != null && orders.Active != null)
                cancelButton.gameObject.SetActive(orders.Active.State == OrderState.Active
                    || ship.State == ShipState.Landed);

            if (incomingScroll != null && scrollbarGo != null)
                scrollbarGo.SetActive(incomingScroll.content.rect.height > incomingScroll.viewport.rect.height + 1f);

            var statusText = FindText(orderArea, "OrderStatus");
            if (orders.Active != null && statusText != null)
                statusText.text = orders.Active.State == OrderState.Aboard
                    ? Col("Пассажир", "НА БОРТУ → " + orders.PlanetName(orders.Active.DropoffIndex).ToUpper())
                    : Col("Пассажир", "ожидает на " + orders.PlanetName(orders.Active.PickupIndex).ToUpper());
        }

        private void NearestBody(out string name, out float dist, out bool heat)
        {
            name = "—";
            dist = 0f;
            heat = false;
            float best = float.MaxValue;
            Vector2 pos = ship.transform.position;
            foreach (CelestialBody body in gravity.Bodies)
            {
                float d = Vector2.Distance(body.transform.position, pos) - body.Radius;
                if (d < best)
                {
                    best = d;
                    name = body.DisplayName;
                }
                if (body is Star star && star.IsInHeatZone(pos))
                    heat = true;
            }
            dist = Mathf.Max(0f, best);
        }

        // --- Помощники построения ---

        private static string Col(string label, string value)
        {
            return $"<color=#{ColorUtility.ToHtmlStringRGB(LabelColor)}>{label}:</color> {value}";
        }

        private GameObject H(string name, Transform parent)
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

        private GameObject PlayerButton(Transform parent, string glyph)
        {
            var go = Button(parent, glyph, new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.22f), BrightColor, 20f);
            FixedSize(go, 34f, 34f);
            return go;
        }

        private TMP_Text Text(GameObject parent, string content, float size, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent.transform, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        private GameObject Button(Transform parent, string label, Color background, Color textColor, float size)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = background;
            var text = Text(go, label, size, textColor);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = true;
            return go;
        }

        private static void FixedSize(GameObject go, float width, float height)
        {
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.minHeight = height;
            le.preferredWidth = width;
            le.preferredHeight = height;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
        }

        private static void FixedHeight(GameObject go, float height)
        {
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
        }

        private static void FixedWidth(GameObject go, float width)
        {
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.preferredWidth = width;
            le.flexibleWidth = 0f;
        }

        private static void Flexible(GameObject go)
        {
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static TMP_Text FindText(RectTransform parent, string name)
        {
            var found = parent.Find(name);
            return found != null ? found.GetComponent<TMP_Text>() : null;
        }
    }
}
