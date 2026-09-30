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
        private TMP_Text moneyValue, ratingValue;

        [SerializeField] private Sprite cashIcon;
        [SerializeField] private Sprite ratingIcon;

        [Tooltip("Единый шрифт панели: применяется ко всем текстам (каркас и карточки). Пусто — шрифт часов")]
        [SerializeField] private TMP_FontAsset panelFont;
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
            ApplyPanelFont();
            if (transform.Find("Stack") == null)
            {
                // Фолбэк для старой сцены: строим каркас кодом.
                BindRefs(OnboardComputerFactory.Build(transform));
            }
            else
            {
                BindExisting();
            }
        }

        // Поиск готовых (построенных редактором) элементов по именам.
        // Единый шрифт панели: поле panelFont, иначе текущий шрифт часов.
        private void ApplyPanelFont()
        {
            if (panelFont == null)
            {
                var clockText = transform.Find("Stack/Header/TitleRow/Clock");
                if (clockText != null)
                    panelFont = clockText.GetComponent<TMP_Text>().font;
            }
            if (panelFont == null)
                return;
            OnboardComputerFactory.SetFont(panelFont);
            foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
                text.font = panelFont;
        }

        private void BindExisting()
        {
            clock = FindT("Stack/Header/TitleRow/Clock");
            shipState1 = FindT("Stack/StateSection/ShipState1");
            shipState2 = FindT("Stack/StateSection/ShipState2");
            shipState3 = FindT("Stack/StateSection/ShipState3");
            sysState2 = FindT("Stack/StateSection/SysState2");
            sysState3 = FindT("Stack/StateSection/SysState3");
            econCounters = FindT("Stack/EconSection/EconRow/EconCounters");
            orderPay = FindT("Stack/OrderSection/OrderPay");
            moneyValue = FindT("Stack/Header/StatsRow/MoneyValue/Value");
            ratingValue = FindT("Stack/Header/StatsRow/RatingValue/Value");
            var abandon = transform.Find("Stack/StateSection/AbandonButton");
            abandonButton = abandon != null ? abandon.GetComponent<Button>() : null;
            var area = transform.Find("Stack/OrderSection/OrderArea");
            orderArea = (RectTransform)area;
            if (clock != null)
                OnboardComputerFactory.SetFont(clock.font);
        }

        private void BindRefs(OnboardRefs refs)
        {
            clock = refs.Clock;
            shipState1 = refs.ShipState1;
            shipState2 = refs.ShipState2;
            shipState3 = refs.ShipState3;
            sysState2 = refs.SysState2;
            sysState3 = refs.SysState3;
            econCounters = refs.EconCounters;
            orderPay = refs.OrderPay;
            moneyValue = refs.MoneyValue;
            ratingValue = refs.RatingValue;
            abandonButton = refs.AbandonButton;
            orderArea = refs.OrderArea;
        }

        private TMP_Text FindT(string path)
        {
            var t = transform.Find(path);
            return t != null ? t.GetComponent<TMP_Text>() : null;
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
            {
                foreach (var card in cardMap.Values)
                    Destroy(card);
                cardMap.Clear();
                BuildActiveOrder();
            }
            else
                BuildIncoming();
        }

        private void BuildActiveOrder()
        {
            OrderOffer offer = orders.Active;

            var row = new GameObject("ActiveOrder", typeof(RectTransform), typeof(Image));
            row.transform.SetParent(orderArea, false);
            orderAreaChildren.Add(row);
            StartCoroutine(FadeIn(row));
            row.GetComponent<Image>().color = OnboardComputerFactory.PanelColor;
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
            photoImage.color = new Color(OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g, OnboardComputerFactory.LabelColor.b, 0.3f);
            if (offer.Passenger.Portrait != null)
                photoImage.sprite = offer.Passenger.Portrait;
            OnboardComputerFactory.FixedSize(photo, 96f, 96f);

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

            TMP_Text name = OnboardComputerFactory.Text(column, offer.Passenger.DisplayName + " · " + offer.Passenger.Archetype, 24f, OnboardComputerFactory.BrightColor);
            OnboardComputerFactory.FixedHeight(name.gameObject, 30f);
            TMP_Text route = OnboardComputerFactory.Text(column,
                orders.PlanetName(offer.PickupIndex).ToUpper() + " → " + orders.PlanetName(offer.DropoffIndex).ToUpper(),
                22f, OnboardComputerFactory.TaxiColor);
            OnboardComputerFactory.FixedHeight(route.gameObject, 28f);
            var status = OnboardComputerFactory.Text(column, "...", 19f, OnboardComputerFactory.LabelColor);
            OnboardComputerFactory.FixedHeight(status.gameObject, 24f);
            status.name = "OrderStatus";

            string wish = PassengerProfile.PickPhrase(offer.Passenger.Wishes);
            if (!string.IsNullOrEmpty(wish))
            {
                var wishText = OnboardComputerFactory.Text(column, "«" + wish + "»", 17f, OnboardComputerFactory.LabelColor);
                wishText.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            }

            // Отказ: без пассажира — всегда, с пассажиром — только на планете.
            var cancel = OnboardComputerFactory.Button(orderArea, "ОТКАЗАТЬСЯ ОТ ЗАКАЗА",
                new Color(OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g, OnboardComputerFactory.LabelColor.b, 0.25f), OnboardComputerFactory.LabelColor, 17f);
            var cancelLe = cancel.AddComponent<LayoutElement>();
            cancelLe.minHeight = 32f;
            cancelLe.flexibleWidth = 1f;
            cancelButton = cancel.GetComponent<Button>();
            cancelButton.onClick.AddListener(() => orders.CancelActive());
            orderAreaChildren.Add(cancel);

            orderPay.text = Col("Счёт:", orders.Deliveries + " доставок · " + orders.Failures + " провалов");
        }

        private readonly Dictionary<OrderOffer, GameObject> cardMap = new Dictionary<OrderOffer, GameObject>();

        private void BuildIncoming()
        {
            // Ушедшие предложения — плавно растворяются.
            var gone = new List<OrderOffer>();
            foreach (var pair in cardMap)
                if (!OfferExists(pair.Key))
                    gone.Add(pair.Key);
            foreach (OrderOffer offer in gone)
            {
                StartCoroutine(FadeOut(cardMap[offer]));
                cardMap.Remove(offer);
            }

            // Новые появляются с анимацией; порядок — как в списке.
            int index = 0;
            foreach (OrderOffer offer in orders.Offers)
            {
                if (!cardMap.TryGetValue(offer, out GameObject card))
                {
                    card = BuildCard(offer);
                    card.transform.SetParent(orderArea, false);
                    cardMap[offer] = card;
                    StartCoroutine(FadeIn(card));
                }
                card.transform.SetSiblingIndex(index);
                index++;
            }
        }

        private bool OfferExists(OrderOffer offer)
        {
            foreach (OrderOffer existing in orders.Offers)
                if (existing == offer)
                    return true;
            return false;
        }

        private System.Collections.IEnumerator FadeIn(GameObject go)
        {
            var group = go.GetComponent<CanvasGroup>();
            if (group == null)
                group = go.AddComponent<CanvasGroup>();
            go.transform.localScale = Vector3.one * 0.95f;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.18f)
            {
                group.alpha = t;
                go.transform.localScale = Vector3.Lerp(Vector3.one * 0.95f, Vector3.one, t);
                yield return null;
            }
            group.alpha = 1f;
            go.transform.localScale = Vector3.one;
        }

        private System.Collections.IEnumerator FadeOut(GameObject go)
        {
            var group = go.GetComponent<CanvasGroup>();
            if (group == null)
                group = go.AddComponent<CanvasGroup>();
            for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.15f)
            {
                group.alpha = t;
                yield return null;
            }
            Destroy(go);
        }

        // Полоска прокрутки справа; скрыта, когда контент помещается целиком.
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
            OnboardComputerFactory.Stretch(slidingRect);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(sliding.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)handle.transform);
            handle.GetComponent<Image>().color = OnboardComputerFactory.LabelColor;
            bar.handleRect = (RectTransform)handle.transform;
            bar.targetGraphic = handle.GetComponent<Image>();

            scroll.verticalScrollbar = bar;
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
            card.GetComponent<Image>().color = OnboardComputerFactory.PanelColor;
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
            OnboardComputerFactory.FixedSize(avatar, 64f, 64f);

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

            TMP_Text name = OnboardComputerFactory.Text(column, offer.Passenger.DisplayName + " · " + offer.Passenger.Archetype, 20f, OnboardComputerFactory.BrightColor);
            TMP_Text route = OnboardComputerFactory.Text(column,
                orders.PlanetName(offer.PickupIndex).ToUpper() + " → " + orders.PlanetName(offer.DropoffIndex).ToUpper()
                + "  ·  " + orders.RouteDistance(offer).ToString("0") + " юн", 17f, OnboardComputerFactory.LabelColor);
            string wishPhrase = PassengerProfile.PickPhrase(offer.Passenger.Wishes);
            if (!string.IsNullOrEmpty(wishPhrase))
            {
                var wish = OnboardComputerFactory.Text(column, "доп.: «" + wishPhrase + "»", 15f, OnboardComputerFactory.LabelColor);
                wish.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            }

            // Кнопки-иконки в ряд: ✓ принять, ✕ отклонить — компактно, без пустого низа.
            var buttons = new GameObject("Buttons", typeof(RectTransform));
            buttons.transform.SetParent(card.transform, false);
            var buttonsLayout = buttons.AddComponent<VerticalLayoutGroup>();
            buttonsLayout.spacing = 6;
            buttonsLayout.childControlWidth = true;
            buttonsLayout.childControlHeight = true;
            buttonsLayout.childForceExpandWidth = false;
            buttonsLayout.childForceExpandHeight = false;
            OrderOffer captured = offer;
            var accept = OnboardComputerFactory.Button(buttons.transform, "✓", OnboardComputerFactory.TaxiColor, new Color(0.04f, 0.06f, 0.1f), 26f);
            OnboardComputerFactory.FixedSize(accept, 44f, 44f);
            accept.GetComponent<Button>().onClick.AddListener(() => orders.Accept(captured));
            var decline = OnboardComputerFactory.Button(buttons.transform, "✕",
                new Color(OnboardComputerFactory.DangerColor.r, OnboardComputerFactory.DangerColor.g, OnboardComputerFactory.DangerColor.b, 0.3f), OnboardComputerFactory.DangerColor, 26f);
            OnboardComputerFactory.FixedSize(decline, 44f, 44f);
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

        private static string Col(string label, string value)
        {
            return $"<color=#{ColorUtility.ToHtmlStringRGB(OnboardComputerFactory.LabelColor)}>{label}:</color> {value}";
        }
        private static TMP_Text FindText(RectTransform parent, string name)
        {
            var found = parent.Find(name);
            return found != null ? found.GetComponent<TMP_Text>() : null;
        }
    }
}
