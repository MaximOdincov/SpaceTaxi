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
    // Бортовой компьютер: живое СОСТОЯНИЕ, текущий заказ, входящие карточки
    // с кнопками [ПРИНЯТЬ] и кнопка «ОТКЛЮЧИТЬ КОРАБЛЬ» при пустом баке.
    // Вешается на OnboardComputer, элементы находит по именам из сцены.
    public class OnboardBinder : MonoBehaviour
    {
        private TMP_Text clock;
        private TMP_Text shipState1, shipState2, shipState3;
        private TMP_Text sysState2, sysState2Dist, sysState3;
        private TMP_Text orderTitle, orderName, orderRoute, orderTimer, orderPay;
        private RectTransform wishFrame;
        private TMP_Text wishText;
        private RectTransform clientPhoto;
        private Image clientPhotoImage;
        private TMP_Text clientPhotoLabel;
        private RectTransform divAir;

        private Ship ship;
        private GravityField gravity;
        private OrderService orders;
        private HullSystem hull;
        private GameObject abandonButton;
        private RectTransform incomingRoot;
        private float nextSlowUpdate;
        private readonly List<GameObject> cards = new List<GameObject>();

        private static readonly Color LabelColor = new Color(0.357f, 0.788f, 0.561f);   // #5BC98F
        private static readonly Color BrightColor = new Color(0.659f, 1f, 0.796f);      // #A8FFCB
        private static readonly Color TaxiColor = new Color(1f, 0.831f, 0.302f);        // #FFD44D
        private static readonly Color PanelColor = new Color(0.031f, 0.078f, 0.047f, 0.92f);

        private void Awake()
        {
            Transform t = transform;
            clock = t.Find("Clock").GetComponent<TMP_Text>();
            shipState1 = t.Find("ShipState1").GetComponent<TMP_Text>();
            shipState2 = t.Find("ShipState2").GetComponent<TMP_Text>();
            shipState3 = t.Find("ShipState3").GetComponent<TMP_Text>();
            sysState2 = t.Find("SysState2").GetComponent<TMP_Text>();
            sysState2Dist = t.Find("SysState2Dist").GetComponent<TMP_Text>();
            sysState3 = t.Find("SysState3").GetComponent<TMP_Text>();
            orderTitle = t.Find("OrderTitle").GetComponent<TMP_Text>();
            orderName = t.Find("OrderName").GetComponent<TMP_Text>();
            orderRoute = t.Find("OrderRoute").GetComponent<TMP_Text>();
            orderTimer = t.Find("OrderTimer").GetComponent<TMP_Text>();
            orderPay = t.Find("OrderPay").GetComponent<TMP_Text>();
            wishFrame = t.Find("WishFrame").GetComponent<RectTransform>();
            wishText = wishFrame.Find("Text").GetComponent<TMP_Text>();
            clientPhoto = t.Find("ClientPhoto").GetComponent<RectTransform>();
            clientPhotoImage = t.Find("ClientPhoto/Bg").GetComponent<Image>();
            clientPhotoLabel = t.Find("ClientPhoto/Label").GetComponent<TMP_Text>();
            divAir = t.Find("DivAir").GetComponent<RectTransform>();
        }

        private void Update()
        {
            if (orders == null)
            {
                var session = FindAnyObjectByType<GameSession>();
                ship = FindAnyObjectByType<Ship>();
                gravity = FindAnyObjectByType<GravityField>();
                if (session == null || session.Orders == null || ship == null || ship.Config == null)
                    return;
                orders = session.Orders;
                hull = ship.GetComponent<HullSystem>();
                orders.OffersChanged += RebuildIncoming;
                orders.ActiveOrderChanged += RefreshOrder;
                RebuildIncoming();
                RefreshOrder();
                CreateAbandonButton();
            }

            if (Time.unscaledTime >= nextSlowUpdate)
            {
                nextSlowUpdate = Time.unscaledTime + 0.25f;
                SlowUpdate();
            }
        }

        // Медленные обновления: часы, статусы, ближайший объект.
        private void SlowUpdate()
        {
            int seconds = (int)Time.time; // время смены с 14:02
            int minutes = 14 * 60 + 2 + seconds / 3; // 1 реальная секунда = 3 игровых
            clock.text = minutes / 60 + ":" + (minutes % 60).ToString("00");

            shipState1.text = Col("Целостность корпуса:", Mathf.RoundToInt(ship.HpFraction * 100f) + "%");
            shipState2.text = Col("Запас топлива:", Mathf.RoundToInt(ship.FuelFraction * 100f) + "%");

            string status = ship.State == ShipState.Landed ? "посадка"
                : ship.Fuel <= 0f ? "дрейф без топлива"
                : "полёт";
            shipState3.text = Col("Статус:", status);

            NearestBody(out string name, out float dist, out bool heat);
            sysState2.text = Col("Ближайший объект:", name);
            sysState2Dist.text = "(" + dist.ToString("0") + " юн)";
            sysState3.text = heat ? "⚠ жаровая зона" : "";

            if (abandonButton != null)
                abandonButton.SetActive(ship.Fuel <= 0f);
        }

        private static string Col(string label, string value)
        {
            return $"<color=#{ColorUtility.ToHtmlStringRGB(LabelColor)}>{label}:</color> {value}";
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

        // --- Активный заказ / входящие ---

        private void RefreshOrder()
        {
            bool hasActive = orders.Active != null;
            orderTitle.text = hasActive ? "// ТЕКУЩИЙ ЗАКАЗ" : "// ВХОДЯЩИЕ ЗАКАЗЫ";

            orderName.gameObject.SetActive(hasActive);
            orderRoute.gameObject.SetActive(hasActive);
            wishFrame.gameObject.SetActive(hasActive);
            orderTimer.gameObject.SetActive(hasActive);
            orderPay.gameObject.SetActive(hasActive);
            clientPhoto.gameObject.SetActive(hasActive);

            if (!hasActive)
            {
                orderPay.gameObject.SetActive(true); // счётчик под заголовком входящих
                orderPay.text = Col("Доставлено:", orders.Deliveries + " · " + Col("Провалы:", orders.Failures.ToString()));
                return;
            }

            OrderOffer offer = orders.Active;
            orderName.text = offer.Passenger.DisplayName;
            orderRoute.text = orders.PlanetName(offer.PickupIndex).ToUpper() + " → " + orders.PlanetName(offer.DropoffIndex).ToUpper();
            string wish = PassengerProfile.PickPhrase(offer.Passenger.Wishes);
            wishText.text = string.IsNullOrEmpty(wish) ? "" : "Сообщение от клиента: «" + wish + "»";
            orderTimer.text = Col("Статус:", offer.State == OrderState.Aboard
                ? "пассажир НА БОРТУ → " + orders.PlanetName(offer.DropoffIndex).ToUpper()
                : "пассажир ОЖИДАЕТ на " + orders.PlanetName(offer.PickupIndex).ToUpper());
            orderPay.text = Col("Счёт:", orders.Deliveries + " доставлено · " + orders.Failures + " провалов");

            // Портрет: спрайт профиля с оттенком или заглушка «ФОТО».
            if (offer.Passenger.Portrait != null)
            {
                clientPhotoImage.sprite = offer.Passenger.Portrait;
                clientPhotoImage.color = offer.Passenger.PortraitTint;
                clientPhotoLabel.text = "";
            }
            else
            {
                clientPhotoImage.sprite = null;
                clientPhotoImage.color = new Color(0.03f, 0.07f, 0.05f, 1f);
                clientPhotoLabel.text = "ФОТО";
            }
        }

        private void RebuildIncoming()
        {
            foreach (GameObject card in cards)
                Destroy(card);
            cards.Clear();
            if (incomingRoot == null)
                CreateIncomingRoot();
            if (orders.Active != null)
                return; // активный заказ — карточки не показываем

            TMP_FontAsset font = orderName.font;
            // Зона карточек: от заголовка вниз до разделителя (координаты top-якорные).
            float topY = orderTitle.rectTransform.anchoredPosition.y - 56f;
            float available = topY - divAir.anchoredPosition.y; // оба отрицательные, сверху вниз
            int maxFit = Mathf.Max(1, (int)(available / 114f));
            int index = 0;
            foreach (OrderOffer offer in orders.Offers)
            {
                if (index >= maxFit)
                    break;
                GameObject card = CreateCard(offer, font);
                ((RectTransform)card.transform).anchoredPosition = new Vector2(0f, topY - index * 114f);
                cards.Add(card);
                index++;
            }
        }

        private void CreateIncomingRoot()
        {
            incomingRoot = new GameObject("Incoming", typeof(RectTransform)).GetComponent<RectTransform>();
            incomingRoot.SetParent(transform, false);
            // Растянуть на всю панель — дети с top-якорями считают от её верха.
            incomingRoot.anchorMin = Vector2.zero;
            incomingRoot.anchorMax = Vector2.one;
            incomingRoot.offsetMin = Vector2.zero;
            incomingRoot.offsetMax = Vector2.zero;
        }

        // Карточка входящего заказа: имя/архетип, маршрут, пожелание, [ПРИНЯТЬ].
        private GameObject CreateCard(OrderOffer offer, TMP_FontAsset font)
        {
            var card = new GameObject("OfferCard", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(incomingRoot, false);
            var rect = (RectTransform)card.transform;
            // Якоря сверху: позиция задаётся вниз от верха панели.
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(432f, 108f);
            card.GetComponent<Image>().color = PanelColor;

            TMP_Text name = MakeText(card.transform, font, 22f, BrightColor);
            name.text = offer.Passenger.DisplayName + " · " + offer.Passenger.Archetype;
            Place(name.rectTransform, new Vector2(-116f, -6f), new Vector2(-10f, -34f));

            TMP_Text route = MakeText(card.transform, font, 18f, LabelColor);
            route.text = orders.PlanetName(offer.PickupIndex).ToUpper() + " → " + orders.PlanetName(offer.DropoffIndex).ToUpper();
            Place(route.rectTransform, new Vector2(-116f, -38f), new Vector2(-10f, -64f));

            TMP_Text wish = MakeText(card.transform, font, 15f, LabelColor);
            string wishPhrase = PassengerProfile.PickPhrase(offer.Passenger.Wishes);
            wish.text = string.IsNullOrEmpty(wishPhrase) ? "" : "«" + wishPhrase + "»";
            Place(wish.rectTransform, new Vector2(-116f, -68f), new Vector2(-10f, -104f));

            // Кнопка [ПРИНЯТЬ].
            var buttonGo = new GameObject("Accept", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(card.transform, false);
            var buttonRect = (RectTransform)buttonGo.transform;
            buttonRect.sizeDelta = new Vector2(104f, 40f);
            buttonRect.anchorMin = new Vector2(1f, 1f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.anchoredPosition = new Vector2(-10f, -8f);
            buttonGo.GetComponent<Image>().color = TaxiColor;

            TMP_Text buttonText = MakeText(buttonGo.transform, font, 18f, new Color(0.043f, 0.055f, 0.1f));
            buttonText.text = "ПРИНЯТЬ";
            buttonText.alignment = TextAlignmentOptions.Center;
            Place(buttonText.rectTransform, Vector2.zero, Vector2.zero, true);

            OrderOffer captured = offer;
            buttonGo.GetComponent<Button>().onClick.AddListener(() => orders.Accept(captured));
            return card;
        }

        private void CreateAbandonButton()
        {
            abandonButton = new GameObject("AbandonButton", typeof(RectTransform), typeof(Image), typeof(Button));
            abandonButton.transform.SetParent(transform, false);
            var rect = (RectTransform)abandonButton.transform;
            rect.sizeDelta = new Vector2(432f, 40f);
            rect.anchoredPosition = new Vector2(0f, sysState3.rectTransform.anchoredPosition.y - 36f);
            abandonButton.GetComponent<Image>().color = new Color(1f, 0.478f, 0.361f, 0.9f);

            TMP_Text text = MakeText(abandonButton.transform, orderName.font, 20f, new Color(0.05f, 0.02f, 0.03f));
            text.text = "ОТКЛЮЧИТЬ КОРАБЛЬ";
            text.alignment = TextAlignmentOptions.Center;
            Place(text.rectTransform, Vector2.zero, Vector2.zero, true);

            abandonButton.GetComponent<Button>().onClick.AddListener(() => hull.Abandon("КОРАБЛЬ ОТКЛЮЧЁН"));
            abandonButton.SetActive(false);
        }

        private static TMP_Text MakeText(Transform parent, TMP_FontAsset font, float size, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max, bool stretch = false)
        {
            if (stretch)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                return;
            }
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.offsetMin = min;
            rect.offsetMax = max;
        }
    }
}
