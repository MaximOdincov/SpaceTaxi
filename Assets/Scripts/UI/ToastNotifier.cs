using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;

namespace SpaceTaxi.UI
{
    // Тосты событий: пассажир/доставка/провал/станция/потеря корабля.
    // Сам подписывается на GameSession.OrderService и системы корабля.
    public class ToastNotifier : MonoBehaviour
    {
        [Tooltip("Сколько секунд висит тост")]
        public float duration = 3.5f;

        private TMP_Text label;
        private CanvasGroup group;
        private float hideAt;
        private bool bound;

        private void Awake()
        {
            // Плашка рисуется кодом, стиль — 10_UI_DESIGN (панель/тёмный, текст яркий).
            var image = gameObject.AddComponent<Image>();
            image.color = new Color(0.031f, 0.078f, 0.047f, 0.92f);
            image.raycastTarget = false;

            group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(transform, false);
            label = textGo.GetComponent<TextMeshProUGUI>();
            label.fontSize = 22f;
            label.color = new Color(0.66f, 1f, 0.8f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(18f, 8f);
            rect.offsetMax = new Vector2(-18f, -8f);

            var self = (RectTransform)transform;
            self.sizeDelta = new Vector2(700f, 44f);

            gameObject.SetActive(false);
        }

        public void Show(string message)
        {
            gameObject.SetActive(true);
            label.text = message;
            group.alpha = 1f;
            hideAt = Time.unscaledTime + duration;
        }

        private void Update()
        {
            if (gameObject.activeSelf && Time.unscaledTime >= hideAt)
                gameObject.SetActive(false);

            if (bound)
                return;
            var session = FindAnyObjectByType<GameSession>();
            var ship = FindAnyObjectByType<Ship>();
            if (session == null || session.Orders == null || ship == null)
                return;

            var orders = session.Orders;
            orders.PassengerBoarded += offer => Show("Пассажир на борту: " + offer.Passenger.DisplayName
                + Prefix(PassengerProfile.PickPhrase(offer.Passenger.Greetings)));
            orders.OrderDelivered += offer => Show("Доставлено: " + offer.Passenger.DisplayName
                + Prefix(PassengerProfile.PickPhrase(offer.Passenger.Thanks)));
            orders.OrderFailed += offer => Show("Заказ провален"
                + Prefix(PassengerProfile.PickPhrase(offer.Passenger.Complaints)));

            var landing = ship.GetComponent<LandingSystem>();
            landing.StationServiced += station => Show($"Станция «{station}»: бак и корпус полны");

            var hull = ship.GetComponent<HullSystem>();
            hull.Respawned += () => Show("Респавн на планете старта");
            hull.ShipLost += reason => Show("КОРАБЛЬ ПОТЕРЯН — " + reason);

            bound = true;
        }

        private static string Prefix(string phrase)
        {
            return string.IsNullOrEmpty(phrase) ? "" : " — «" + phrase + "»";
        }
    }
}
