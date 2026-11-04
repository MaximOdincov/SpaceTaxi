using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Audio;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Бортовой компьютер: панель на автоматических layout-группах
    public class OnboardBinder : MonoBehaviour
    {
        private TMP_FontAsset font;

        private TMP_Text clock;
        private TMP_Text shipState1, shipState2, shipState3;
        private TMP_Text sysState2, sysState3;
        private Button abandonButton;

        private RectTransform orderArea; // контейнер зоны заказа
        private GameObject econSection;
        private TMP_Text econCounters;
        private TMP_Text moneyValue, ratingValue;

        [SerializeField] private Sprite cashIcon;
        [SerializeField] private Sprite ratingIcon;

        [Tooltip("Единый шрифт панели: применяется ко всем текстам (каркас и карточки). Пусто — шрифт часов")]
        [SerializeField] private TMP_FontAsset panelFont;

        [Header("Префабы карточек (пусто — собрать кодом)")]
        [SerializeField] private GameObject offerCardPrefab;
        [SerializeField] private GameObject activeOrderPrefab;
        private TMP_Text orderPay;
        private Button cancelButton;

        private Ship ship;
        private GravityField gravity;
        private OrderService orders;
        private HullSystem hull;
        private Wallet wallet;
        private DebtService debt;
        private TMP_Text debtValue;
        private float nextSlowUpdate;
        private bool bound;
        private readonly List<GameObject> orderAreaChildren = new List<GameObject>();

        private const float MoneyValueWidth = 110f;  // «1000 кр» шире заводских 64
        private const float DeadlineWarnSeconds = 15f; // остаток дедлайна, с которого строка краснеет

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
                // фолбэк для старой сцены: строим каркас кодом
                BindRefs(OnboardComputerFactory.Build(transform));
            }
            else
            {
                BindExisting();
            }
            EnsureDebtRow();
            WidenMoneyValue();
        }

        // шрифт панели: panelFont или шрифт часов
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
            // префабная кнопка: хук здесь, потому что фабрика её не строила
            UiSfx.Hook(abandonButton);
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

            GameObject row = activeOrderPrefab != null
                ? Instantiate(activeOrderPrefab, orderArea)
                : OnboardComputerFactory.BuildActiveOrderBlock();
            if (activeOrderPrefab == null)
                row.transform.SetParent(orderArea, false);
            orderAreaChildren.Add(row);
            StartCoroutine(FadeIn(row));

            var photo = row.transform.Find("Photo");
            var photoImage = photo != null ? photo.GetComponent<Image>() : null;
            if (photoImage != null)
            {
                // без портрета (арт А5) — цветная заглушка из палитры генератора
                if (offer.Passenger.Portrait != null)
                    photoImage.sprite = offer.Passenger.Portrait;
                photoImage.color = offer.Passenger.PortraitTint;
            }

            SetText(row, "Info/Name", offer.Passenger.DisplayName + " · " + offer.Passenger.Archetype);
            SetText(row, "Info/Route",
                orders.PlanetName(offer.PickupIndex).ToUpper() + " → " + orders.PlanetName(offer.DropoffIndex).ToUpper());
            // П5.3: вместо пожелания — текст задания, если архетип его дал
            PassengerTask task = offer.Task;
            string wish = task != null && task.Kind != PassengerTaskKind.None
                ? task.Text
                : PassengerProfile.PickPhrase(offer.Passenger.Wishes);
            SetText(row, "Info/Wish", string.IsNullOrEmpty(wish) ? "" : "«" + wish + "»");

            var cancel = OnboardComputerFactory.Button(orderArea, "ОТКАЗАТЬСЯ ОТ ЗАКАЗА",
                new Color(OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g, OnboardComputerFactory.LabelColor.b, 0.25f),
                OnboardComputerFactory.LabelColor, 17f);
            var cancelLe = cancel.AddComponent<LayoutElement>();
            cancelLe.minHeight = 32f;
            cancelLe.flexibleWidth = 1f;
            cancelButton = cancel.GetComponent<Button>();
            cancelButton.onClick.AddListener(() => orders.CancelActive());
            orderAreaChildren.Add(cancel);

            orderPay.text = Col("Счёт", orders.Deliveries + " доставок · " + orders.Failures + " провалов");
        }

        private static void SetText(GameObject root, string path, string value)
        {
            var t = root.transform.Find(path);
            if (t != null)
                t.GetComponent<TMP_Text>().text = value;
        }

        private readonly Dictionary<OrderOffer, GameObject> cardMap = new Dictionary<OrderOffer, GameObject>();

        private void BuildIncoming()
        {
            // ушедшие предложения — плавно растворяются
            var gone = new List<OrderOffer>();
            foreach (var pair in cardMap)
                if (!OfferExists(pair.Key))
                    gone.Add(pair.Key);
            foreach (OrderOffer offer in gone)
            {
                StartCoroutine(FadeOut(cardMap[offer]));
                cardMap.Remove(offer);
            }

            // новые появляются с анимацией, порядок как в списке
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
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.18f)
            {
                if (go == null) // уничтожен пересборкой
                    yield break;
                var group = go.GetComponent<CanvasGroup>();
                if (group == null)
                    group = go.AddComponent<CanvasGroup>();
                group.alpha = t;
                go.transform.localScale = Vector3.Lerp(Vector3.one * 0.95f, Vector3.one, t);
                yield return null;
            }
            if (go == null)
                yield break;
            var finalGroup = go.GetComponent<CanvasGroup>();
            if (finalGroup != null)
                finalGroup.alpha = 1f;
            go.transform.localScale = Vector3.one;
        }

        private System.Collections.IEnumerator FadeOut(GameObject go)
        {
            for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.15f)
            {
                if (go == null) // уже уничтожен пересборкой
                    yield break;
                var group = go.GetComponent<CanvasGroup>();
                if (group == null)
                    group = go.AddComponent<CanvasGroup>();
                group.alpha = t;
                yield return null;
            }
            if (go != null)
                Destroy(go);
        }

        // скроллбар справа; скрыт, когда контент помещается
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

        // карточка входящего: префаб или код, данные по именам узлов
        private GameObject BuildCard(OrderOffer offer)
        {
            GameObject card = offerCardPrefab != null
                ? Instantiate(offerCardPrefab, orderArea)
                : OnboardComputerFactory.BuildOfferCard();
            if (offerCardPrefab == null)
                card.transform.SetParent(orderArea, false);

            var avatar = card.transform.Find("Avatar");
            var avatarImage = avatar != null ? avatar.GetComponent<Image>() : null;
            if (avatarImage != null)
            {
                // без портрета (арт А5) — цветная заглушка из палитры генератора
                if (offer.Passenger.Portrait != null)
                    avatarImage.sprite = offer.Passenger.Portrait;
                avatarImage.color = offer.Passenger.PortraitTint;
            }

            SetText(card, "Info/Name", offer.Passenger.DisplayName + " · " + offer.Passenger.Archetype);
            SetText(card, "Info/Route",
                orders.PlanetName(offer.PickupIndex).ToUpper() + " → " + orders.PlanetName(offer.DropoffIndex).ToUpper()
                + "  ·  " + orders.RouteDistance(offer).ToString("0") + " юн");
            TMP_Text price = EnsurePriceNode(card);
            if (price != null)
                price.text = PriceTag(offer.Price);
            // П5.3: задание пассажира вместо пожелания; текст статичен — на карточке оффера таймер не нужен
            PassengerTask task = offer.Task;
            string wishPhrase = task != null && task.Kind != PassengerTaskKind.None
                ? task.Text
                : PassengerProfile.PickPhrase(offer.Passenger.Wishes);
            string wishPrefix = task != null && task.Kind != PassengerTaskKind.None ? "задание: " : "доп.: ";
            SetText(card, "Info/Wish", string.IsNullOrEmpty(wishPhrase) ? "" : wishPrefix + "«" + wishPhrase + "»");

            OrderOffer captured = offer;
            var accept = card.transform.Find("Buttons/Accept");
            if (accept != null)
            {
                Button acceptButton = accept.GetComponent<Button>();
                UiSfx.Hook(acceptButton); // префабная кнопка — фабрика не хукала
                acceptButton.onClick.AddListener(() => orders.Accept(captured));
            }
            var decline = card.transform.Find("Buttons/Decline");
            if (decline != null)
            {
                Button declineButton = decline.GetComponent<Button>();
                UiSfx.Hook(declineButton);
                declineButton.onClick.AddListener(() => orders.Decline(captured));
            }

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
                wallet = session.Wallet;
                wallet.CreditsChanged += ShowCredits;
                ShowCredits(wallet.Credits);
                debt = session.Debt;
                if (debt != null)
                {
                    debt.DebtChanged += RefreshDebt;
                    RefreshDebt(debt.Balance);
                }
                orders.OffersChanged += RefreshOrder;
                orders.ActiveOrderChanged += RefreshOrder;
                // слот рейтинга (§3.7): живёт событием, первичный показ при бинде
                if (session.Rating != null)
                {
                    session.Rating.RatingChanged += ShowRating;
                    ShowRating(session.Rating.Value);
                }
                if (abandonButton != null && hull != null)
                    abandonButton.onClick.AddListener(() => hull.Abandon("ОТКЛЮЧЁН ОПЕРАТОРОМ"));
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

            shipState1.text = Col("Корпус", Mathf.RoundToInt(ship.HpFraction * 100f) + "%");
            shipState2.text = Col("Топливо", Mathf.RoundToInt(ship.FuelFraction * 100f) + "%");
            shipState3.text = Col("Статус", ship.State == ShipState.Landed ? "посадка"
                : ship.Fuel <= 0f ? "дрейф без топлива" : "полёт");

            NearestBody(out string name, out float dist, out bool heat);
            sysState2.text = Col("Ближний объект", name + " (" + dist.ToString("0") + " юн)");
            sysState3.text = heat ? "⚠ жаровая зона" : "";

            econCounters.text = orders.Deliveries + " доставок · " + orders.Failures + " провалов";
            if (debt != null)
                RefreshDebt(debt.Balance); // страховка от пропущенного события
            abandonButton.gameObject.SetActive(ship.Fuel <= 0f);
            if (cancelButton != null && orders.Active != null)
                cancelButton.gameObject.SetActive(orders.Active.State == OrderState.Active
                    || ship.State == ShipState.Landed);

            var statusText = FindText(orderArea, "ActiveOrder/Info/OrderStatus");
            if (orders.Active != null && statusText != null)
                statusText.text = orders.Active.State == OrderState.Aboard
                    ? Col("Пассажир", "НА БОРТУ → " + orders.PlanetName(orders.Active.DropoffIndex).ToUpper()) + "  ·  " + PriceTag(orders.Active.Price)
                    : Col("Пассажир", "ожидает на " + orders.PlanetName(orders.Active.PickupIndex).ToUpper()) + "  ·  " + PriceTag(orders.Active.Price);

            // остаток дедлайна Торопыги (П5.3): строка задания, обновление раз в SlowUpdate
            var wishText = FindText(orderArea, "ActiveOrder/Info/Wish");
            if (orders.Active != null && wishText != null)
            {
                PassengerTask task = orders.Active.Task;
                if (task != null && task.Kind == PassengerTaskKind.Deadline)
                {
                    float remaining = task.DeadlineRemaining(Time.time);
                    string remain = remaining < DeadlineWarnSeconds
                        ? $"<color=#{ColorUtility.ToHtmlStringRGB(DangerColor)}>Осталось {remaining:0} с</color>"
                        : $"Осталось {remaining:0} с";
                    wishText.text = "«" + task.Text + "» · " + remain;
                }
                // прогресс ожидания Чудика (П8.1): посидели X/10 с
                else if (task != null && task.Kind == PassengerTaskKind.WaitOnPlanet)
                {
                    wishText.text = "«" + task.Text + "» · Посидели "
                        + Mathf.Min(task.WaitAccumulated, task.WaitSeconds).ToString("0")
                        + "/" + task.WaitSeconds.ToString("0") + " с";
                }
            }
        }

        // строка «Цена: N кр» — сумма акцентным цветом такси
        private static string PriceTag(int price)
        {
            return "Цена: <color=" + ColorHex(OnboardComputerFactory.TaxiColor) + ">" + price + " кр</color>";
        }

        private static string ColorHex(Color color)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(color);
        }

        // узел цены есть у каркасной карточки; в префабе OfferCard его нет — достраиваем рядом с Info/Route
        private static TMP_Text EnsurePriceNode(GameObject card)
        {
            Transform info = card.transform.Find("Info");
            if (info == null)
                return null;
            Transform price = info.Find("Price");
            if (price != null)
                return price.GetComponent<TMP_Text>();
            TMP_Text text = OnboardComputerFactory.Text(info.gameObject, "", 17f, OnboardComputerFactory.BrightColor, "Price");
            Transform route = info.Find("Route");
            text.transform.SetSiblingIndex(route != null ? route.GetSiblingIndex() + 1 : info.childCount);
            return text;
        }

        // кредит обновляется событием, без опроса в SlowUpdate
        private void ShowCredits(int credits)
        {
            if (moneyValue != null)
                moneyValue.text = credits + " кр";
        }

        // одна десятая всегда (даже 0.0); точка-разделитель, как в плане §3.7
        private void ShowRating(float value)
        {
            if (ratingValue != null)
                ratingValue.text = value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        // строка долга в секции // СМЕНА: каркас её не содержит, достраиваем кодом
        private void EnsureDebtRow()
        {
            if (debtValue != null)
                return;
            var econSection = transform.Find("Stack/EconSection");
            if (econSection == null)
                return;
            debtValue = OnboardComputerFactory.Text(econSection.gameObject, "", 20f, BrightColor, "DebtValue");
            OnboardComputerFactory.FixedHeight(debtValue.gameObject, 24f);
        }

        // при нуле строка пуста; долг — цветом тревоги фабрики
        private void RefreshDebt(int value)
        {
            if (debtValue == null)
                return;
            debtValue.text = value > 0
                ? $"<color=#{ColorUtility.ToHtmlStringRGB(OnboardComputerFactory.DangerColor)}>Долг: {value} кр</color>"
                : "";
        }

        // поле денег из фабрики/префаба узкое — суммы не влезают
        private void WidenMoneyValue()
        {
            if (moneyValue == null)
                return;
            var layout = moneyValue.gameObject.GetComponent<LayoutElement>();
            if (layout == null)
                return;
            layout.minWidth = MoneyValueWidth;
            layout.preferredWidth = MoneyValueWidth;
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
