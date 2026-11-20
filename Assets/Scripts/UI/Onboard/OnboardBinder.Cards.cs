using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;

namespace SpaceTaxi.UI
{
    // Заказы: активная карточка + входящие в скролле; пути узлов карточек —
    // контракт с OnboardComputerFactory
    public partial class OnboardBinder
    {
        private GameObject activeSection;
        private RectTransform activeArea; // карточка активного заказа (всегда детальная)
        private RectTransform orderArea;  // контент скролла входящих
        private Button abandonButton;
        private OrderOffer builtActiveOffer; // карточка актива перестраивается только при смене заказа
        private GameObject activeCardGo;
        // раскрытие: входящая карточка разворачивается прямо в ленте скролла
        private OrderOffer expandedOffer;
        private readonly List<GameObject> activeAreaChildren = new List<GameObject>();
        // анимация раскрытия — одна на карточку, повторный клик не перезапускает
        private readonly Dictionary<GameObject, Coroutine> heightAnims = new Dictionary<GameObject, Coroutine>();
        private const float ExpandDuration = 0.22f; // как Slide панели, ease-out cubic
        private const float DeadlineWarnSeconds = 15f; // остаток дедлайна, с которого строка краснеет

        private static void SetText(GameObject root, string path, string value)
        {
            var t = root.transform.Find(path);
            if (t != null)
                t.GetComponent<TMP_Text>().text = value;
        }

        // лейбл + значение одной строкой (color-теги TMP; ColorHex уже с «#»)
        private static string StatLine(string label, string value)
        {
            return $"<color={ColorHex(OnboardComputerFactory.LabelColor)}>{label}</color> " +
                   $"<color={ColorHex(OnboardComputerFactory.BrightColor)}>{value}</color>";
        }

        private void RefreshOrder()
        {
            bool hasActive = orders.Active != null;

            if (activeSection != null)
                activeSection.SetActive(hasActive);

            // карточка актива перестраивается только при смене заказа — иначе
            // каждое OffersChanged (протухание) перезапускало бы анимацию
            if (!hasActive || orders.Active != builtActiveOffer)
            {
                foreach (GameObject child in activeAreaChildren)
                    Destroy(child);
                activeAreaChildren.Clear();
                activeCardGo = null;
                activeProgress = null; // строка выполнения гибнет вместе с карточкой
                activeStatus = null; // чип статуса гибнет вместе с карточкой
                if (hasActive)
                    BuildActiveOrder();
                builtActiveOffer = hasActive ? orders.Active : null;
            }

            BuildIncoming();
            UpdateAcceptButtons();
            RefreshCancelState();
        }

        private void ExpandOrder(OrderOffer offer)
        {
            if (offer == null || !OfferExists(offer))
                return;
            if (expandedOffer == offer)
            {
                CollapseExpansion(); // повторный клик по «−» сворачивает
                return;
            }

            if (expandedOffer != null)
                AnimateCard(expandedOffer, expand: false);
            expandedOffer = offer;
            AnimateCard(offer, expand: true);
            RefreshToggleGlyphs();
        }

        private void CollapseExpansion()
        {
            if (expandedOffer == null)
                return;
            AnimateCard(expandedOffer, expand: false);
            expandedOffer = null;
            RefreshToggleGlyphs();
        }

        // раскрытие на месте: Compact гасится, Expanded включается,
        // preferredHeight карточки анимируется между константами фабрики
        private void AnimateCard(OrderOffer offer, bool expand)
        {
            if (offer == null || !cardMap.TryGetValue(offer, out GameObject card) || card == null)
                return;
            Transform compact = card.transform.Find("Compact");
            Transform expanded = card.transform.Find("Expanded");
            if (compact == null || expanded == null)
                return;
            if (heightAnims.TryGetValue(card, out Coroutine running) && running != null)
                StopCoroutine(running);
            // контейнеры взаимоисключающие: переключение мгновенное, наезды исключены
            compact.gameObject.SetActive(!expand);
            expanded.gameObject.SetActive(expand);
            // без строки Wish раскрытая карточка короче — не оставляем пустоту
            float expandedTarget = OnboardComputerFactory.OfferExpandedHeight - (HasWishRow(offer) ? 0f : 56f);
            float target = expand ? expandedTarget : OnboardComputerFactory.OfferCompactHeight;
            heightAnims[card] = StartCoroutine(CardHeightRoutine(card, target));
        }

        private System.Collections.IEnumerator CardHeightRoutine(GameObject card, float target)
        {
            var le = card != null ? card.GetComponent<LayoutElement>() : null;
            if (le == null)
                yield break;
            float from = le.preferredHeight;
            // ease-out cubic; null-проверки — карточка могла протухнуть
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / ExpandDuration)
            {
                if (card == null || le == null)
                    yield break;
                float k = Mathf.Clamp01(t);
                float eased = 1f - Mathf.Pow(1f - k, 3f);
                le.preferredHeight = Mathf.Lerp(from, target, eased);
                yield return null;
            }
            if (card == null || le == null)
                yield break;
            le.preferredHeight = target;
            heightAnims.Remove(card);
        }

        private void BuildActiveOrder()
        {
            OrderOffer offer = orders.Active;

            GameObject card = OnboardComputerFactory.BuildActiveOrderCard();
            card.transform.SetParent(activeArea, false);
            activeAreaChildren.Add(card);
            activeCardGo = card;
            StartCoroutine(FadeIn(card));

            // тоггла нет: активный заказ всегда детальный
            var cancel = card.transform.Find("Expanded/Cancel");
            if (cancel != null)
            {
                // штраф списывает GameSession на OrderCancelled
                cancel.GetComponent<Button>().onClick.AddListener(() => orders.CancelActive());
                int penalty = OrderService.CancelPenalty(offer.Price);
                SetText(card.gameObject, "Expanded/Cancel/Text", "Отменить · −" + UiFormat.Currency + " " + penalty);
            }

            FillCard(card.transform, offer, activeMode: true);
            activeStatus = FindText(card.transform, "Expanded/Summary/Status");
            UpdateActiveStatusChip(); // реальный текст вместо заглушки «статус» с первого кадра
            BuildActiveProgressRow();
        }

        // заполнение карточки (входящей или активной): аватары и тексты
        private void FillCard(Transform card, OrderOffer offer, bool activeMode)
        {
            // только спрайт и тинт — геометрию задаёт фабрика
            ApplyPortrait(card, "Compact/Avatar/Picture", offer.Passenger);
            ApplyPortrait(card, "Expanded/DetailAvatar/Picture", offer.Passenger);

            SetText(card.gameObject, "Compact/Info/Name", offer.Passenger.DisplayName);
            SetText(card.gameObject, "Compact/Info/Route",
                orders.PlanetName(offer.PickupIndex).ToUpper() + " → " + orders.PlanetName(offer.DropoffIndex).ToUpper());

            // строка статуса есть только у актива (узел не строится у входящих)
            var statusNode = card.Find("Expanded/Summary/Status");
            if (statusNode != null)
                statusNode.gameObject.SetActive(activeMode);

            // строка Wish: текст задания или реплика пассажира из пула
            // (PassengerWish звать один раз — внутри случайный выбор)
            PassengerTask task = offer.Task;
            bool hasTask = task != null && task.Kind != PassengerTaskKind.None;
            string wishRaw = hasTask ? null : PassengerWish(offer);
            string wishValue = hasTask ? task.Text : (wishRaw != null ? wishRaw + " " + NoTaskMark() : null);
            var wishNode = card.Find("Expanded/Summary/Wish");
            if (wishNode != null)
                wishNode.gameObject.SetActive(!string.IsNullOrEmpty(wishValue));
            if (!string.IsNullOrEmpty(wishValue))
                SetText(card.gameObject, "Expanded/Summary/Wish", StatLine("Пожелание клиента:", wishValue));

            // высота активной карточки — под фактический состав строк
            // (без Wish фиксированная высота оставляла пустоту)
            if (activeMode)
            {
                var cardLe = card.gameObject.GetComponent<LayoutElement>();
                if (cardLe != null)
                    cardLe.preferredHeight = HasWishRow(offer)
                        ? OnboardComputerFactory.ActiveCardHeight
                        : OnboardComputerFactory.ActiveCardHeight - 56f; // Wish 54 + зазор 2
            }

            RefreshLiveDistances(card, offer);
        }

        // есть ли строка Wish: единый предикат для высоты карточек
        // (AnimateCard и FillCard), чтобы состав строк и высота не разъезжались
        private static bool HasWishRow(OrderOffer offer)
        {
            if (offer == null)
                return false;
            if (offer.Task != null && offer.Task.Kind != PassengerTaskKind.None)
                return !string.IsNullOrEmpty(offer.Task.Text);
            return !string.IsNullOrEmpty(PassengerWish(offer));
        }

        // приглушённая пометка для реплик без задания
        private static string NoTaskMark()
        {
            return "<color=" + ColorHex(OnboardComputerFactory.LabelColor) + ">(без задания)</color>";
        }

        // реплика пассажира (пул Wishes); null — строки нет. Пустая фраза
        // заменяется первой непустой — узел и высота согласованы
        private static string PassengerWish(OrderOffer offer)
        {
            if (offer == null || offer.Passenger == null)
                return null;
            string picked = PassengerProfile.PickPhrase(offer.Passenger.Wishes);
            if (!string.IsNullOrEmpty(picked))
                return picked;
            string[] wishes = offer.Passenger.Wishes;
            if (wishes == null)
                return null;
            foreach (string phrase in wishes)
                if (!string.IsNullOrEmpty(phrase))
                    return phrase;
            return null;
        }

        // портрет: спрайт и тинт; размер фиксирован фабрикой
        private static void ApplyPortrait(Transform card, string path, PassengerProfile passenger)
        {
            var node = card.Find(path);
            var image = node != null ? node.GetComponent<Image>() : null;
            if (image == null)
                return;
            if (passenger.Portrait != null)
                image.sprite = passenger.Portrait;
            image.color = passenger.PortraitTint;
        }

        // расстояния «от вас» в сводке; вызывается при заполнении и в SlowUpdate
        private void RefreshLiveDistances(Transform card, OrderOffer offer)
        {
            if (card == null || offer == null || ship == null)
                return;
            float fromDist = orders.DistanceToPlanet(offer.PickupIndex, ship.transform.position);
            float toDist = orders.DistanceToPlanet(offer.DropoffIndex, ship.transform.position);
            SetText(card.gameObject, "Expanded/Summary/Name", StatLine("Имя:", offer.Passenger.DisplayName));
            SetText(card.gameObject, "Expanded/Summary/From",
                StatLine("Откуда:", orders.PlanetName(offer.PickupIndex).ToUpper() + $" ({fromDist:0} юн от вас)"));
            SetText(card.gameObject, "Expanded/Summary/To",
                StatLine("Куда:", orders.PlanetName(offer.DropoffIndex).ToUpper() + $" ({toDist:0} юн от вас)"));
            SetText(card.gameObject, "Expanded/Summary/RouteLen",
                StatLine("Примерная длина маршрута:", orders.RouteDistance(offer).ToString("0") + " юн"));
        }

        private readonly Dictionary<OrderOffer, GameObject> cardMap = new Dictionary<OrderOffer, GameObject>();

        private void BuildIncoming()
        {
            // ушедшие предложения — плавно растворяются; раскрытая перед этим сворачивается
            var gone = new List<OrderOffer>();
            foreach (var pair in cardMap)
                if (!OfferExists(pair.Key))
                    gone.Add(pair.Key);
            foreach (OrderOffer offer in gone)
            {
                GameObject card = cardMap[offer];
                if (offer == expandedOffer)
                {
                    expandedOffer = null;
                    AnimateCard(offer, expand: false); // протух пока раскрыт — плавный сворот
                    RefreshToggleGlyphs();
                }
                if (heightAnims.TryGetValue(card, out Coroutine fading) && fading != null)
                    StopCoroutine(fading);
                heightAnims.Remove(card);
                StartCoroutine(FadeOut(card));
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

        // «Взять заказ» недоступна при активном заказе — кнопки сереют
        private void UpdateAcceptButtons()
        {
            foreach (GameObject card in cardMap.Values)
            {
                Transform accept = card.transform.Find("Expanded/Accept");
                if (accept != null)
                    accept.GetComponent<Button>().interactable = orders.Active == null;
            }
        }

        // с пассажиром на борту отменить можно только стоя; лейбл штрафа
        // обновляем живьём (цена могла пересчитаться)
        private void RefreshCancelState()
        {
            if (activeCardGo == null || orders.Active == null || ship == null)
                return;
            Transform cancel = activeCardGo.transform.Find("Expanded/Cancel");
            if (cancel != null)
            {
                cancel.GetComponent<Button>().interactable = orders.Active.State == OrderState.Active
                    || ship.State == ShipState.Landed;
                int penalty = OrderService.CancelPenalty(orders.Active.Price);
                SetText(activeCardGo, "Expanded/Cancel/Text", "Отменить · −" + UiFormat.Currency + " " + penalty);
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

        // карточка входящего: «+» в углу разворачивает Expanded в ленте скролла
        private GameObject BuildCard(OrderOffer offer)
        {
            GameObject card = OnboardComputerFactory.BuildOfferCard();
            card.transform.SetParent(orderArea, false);

            FillCard(card.transform, offer, activeMode: false);
            float toPassenger = orders.DistanceToPlanet(offer.PickupIndex, ship.transform.position);
            SetText(card, "Compact/Info/Dist",
                "Расстояние до пассажира: " + toPassenger.ToString("0") + " юн.");
            SetText(card, "Expanded/Accept/Text", "Взять заказ · " + UiFormat.Currency + " " + UiFormat.FormatCredits(offer.Price));

            OrderOffer captured = offer;
            var accept = card.transform.Find("Expanded/Accept");
            if (accept != null)
                accept.GetComponent<Button>().onClick.AddListener(() => orders.Accept(captured));
            var expand = card.transform.Find("ExpandToggle");
            if (expand != null)
                expand.GetComponent<Button>().onClick.AddListener(() => ExpandOrder(captured));

            return card;
        }

        // глифы «+»/«−» по состоянию раскрытия
        private void RefreshToggleGlyphs()
        {
            foreach (var pair in cardMap)
            {
                if (pair.Value == null)
                    continue;
                var expand = pair.Value.transform.Find("ExpandToggle");
                if (expand != null)
                    expand.GetComponentInChildren<TMP_Text>().text = pair.Key == expandedOffer ? "−" : "+";
            }
        }

        // прогресс задания для строки пожелания (дедлайн/ожидание), null — показывать без прогресса
        private string TaskProgress(PassengerTask task)
        {
            if (task == null || task.Kind == PassengerTaskKind.None)
                return null;
            if (task.Kind == PassengerTaskKind.Deadline)
            {
                float remaining = task.DeadlineRemaining(Time.time);
                return remaining < DeadlineWarnSeconds
                    ? $"<color={ColorHex(OnboardComputerFactory.DangerColor)}>Осталось {remaining:0} с</color>"
                    : $"Осталось {remaining:0} с";
            }
            // избегания (жар/пояс): счёт показываем только после первого замечания
            if ((task.Kind == PassengerTaskKind.NoHeat || task.Kind == PassengerTaskKind.BeltAvoid)
                && task.Strikes > 0)
            {
                int limit = task.Kind == PassengerTaskKind.NoHeat
                    ? PassengerTasks.NoHeatStrikeLimit
                    : PassengerTasks.BeltStrikeLimit;
                return $"<color={ColorHex(OnboardComputerFactory.DangerColor)}>Замечаний: {task.Strikes}/{limit}</color>";
            }
            return null;
        }

        // задание активного заказа в сводке с живым прогрессом
        private void UpdateActiveWish()
        {
            if (activeCardGo == null || orders.Active == null)
                return;
            var wishText = FindText(activeCardGo.transform, "Expanded/Summary/Wish");
            if (wishText == null || !wishText.gameObject.activeSelf)
                return;
            PassengerTask task = orders.Active.Task;
            if (task == null || task.Kind == PassengerTaskKind.None)
                return;
            string progress = TaskProgress(task);
            wishText.text = StatLine("Пожелание клиента:", task.Text + (progress != null ? " · " + progress : ""));
        }

        private static string ColorHex(Color color)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(color);
        }

        private static TMP_Text FindText(Transform parent, string name)
        {
            var found = parent.Find(name);
            return found != null ? found.GetComponent<TMP_Text>() : null;
        }
    }
}
