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

namespace SpaceTaxi.UI
{
    // Бортовой компьютер: панель на автоматических layout-группах. Каркас всегда
    // строится фабрикой (в рантайме — поверх старого мокапа префаба: Frame и
    // Background сохраняются, остальное сносится), живые данные — по ссылкам
    // из OnboardRefs и событиям систем. Входящие карточки раскрываются IN PLACE
    // (анимация высоты обёртки Detail), активный заказ живёт в закреплённой
    // секции и всегда детальный. Политика переполнения: тексты не меняют высоту
    // узлов, всё лишнее обрезается «…» на стороне фабрики.
    public class OnboardBinder : MonoBehaviour
    {
        private TMP_Text clock;
        private TMP_Text moneyValue, ratingValue;
        // «★ 3.0»: префикс пуст, если атлас не дал глиф звезды (сброс dynamic-атласа)
        private string ratingPrefix = "";
        // символ валюты проекта и глиф часов (есть не в каждом атласе — проверка при бинде)
        private const string Currency = "Đ";
        private bool clockGlyph;
        // анимация денег: показанное значение догоняет целевое (как рейтинг)
        private float moneyShown = -1f;
        private int moneyTarget;
        // строка долга: катится значением, цвет «дышит» — рост горячее погашения
        private GameObject debtRow;
        private TMP_Text debtValue;
        private float debtShown = -1f;
        private int debtTarget;
        private bool debtRising;
        // анимация рейтинга: показанное значение догоняет целевое, цвет — динамика
        private float ratingShown = -1f;
        private bool ratingDropping;
        private GameObject activeSection;
        private RectTransform activeArea; // карточка активного заказа (всегда детальная)
        private RectTransform orderArea;  // контент скролла входящих
        private Button abandonButton;
        private OrderOffer builtActiveOffer; // карточка актива перестраивается только при смене заказа
        private GameObject activeCardGo;
        // раскрытие: входящая карточка разворачивается прямо в ленте скролла
        private OrderOffer expandedOffer;

        [Tooltip("Единый шрифт панели: применяется ко всем текстам каркаса и карточек. Пусто — шрифт часов старого каркаса")]
        [SerializeField] private TMP_FontAsset panelFont;

        private Ship ship;
        private OrderService orders;
        private HullSystem hull;
        private Wallet wallet;
        private DebtService debt;
        private RatingService rating;      // для отписки от RatingChanged
        private GameSession cachedSession; // для анимации рейтинга — без поиска в кадре
        private float nextSlowUpdate;
        private bool bound;
        private readonly List<GameObject> activeAreaChildren = new List<GameObject>();

        // плеер «ЭФИР»
        private Button playerPrev, playerNext;
        private Button radioMute;
        private TMP_Text trackTitle;
        private RectTransform progressFill;
        private AudioManager audioManager;
        private bool musicBound;

        // анимации раскрытия карточек: одна на карточку, повторный клик не перезапускает
        private readonly Dictionary<GameObject, Coroutine> heightAnims = new Dictionary<GameObject, Coroutine>();
        private const float ExpandDuration = 0.22f; // как Slide панели, ease-out cubic

        private const float DeadlineWarnSeconds = 15f; // остаток дедлайна, с которого строка краснеет
        // §3.2: увольнение после третьей непогашенной аварии (как в StationScreenController)
        private const int MaxUnpaidAccidents = 3;

        private void Awake()
        {
            ApplyPanelFont();
            // каркас всегда кодом: и для старых сцен, и поверх мокапа префаба
            BindRefs(OnboardComputerFactory.Build(transform, fontOverride: panelFont));
            SetupMusicPlayer();
        }

        private void OnDestroy()
        {
            // AudioManager переживает сцену — иначе событие пришло бы в разрушенный биндер
            if (musicBound && audioManager != null)
                audioManager.MusicStateChanged -= RefreshMusicPlayer;
            // симметричная отписка от всего, на что подписались при бинде
            // (события в разрушенный биндер = NRE, проект уже ломался на этом)
            if (wallet != null)
                wallet.CreditsChanged -= ShowCredits;
            if (debt != null)
                debt.DebtChanged -= RefreshDebt;
            if (orders != null)
            {
                orders.OffersChanged -= RefreshOrder;
                orders.ActiveOrderChanged -= RefreshOrder;
            }
            if (rating != null)
                rating.RatingChanged -= ShowRating;
        }

        // шрифт панели: panelFont или шрифт часов стоящего каркаса — старого
        // (префаб) или нового (после редакторной перестройки)
        private void ApplyPanelFont()
        {
            if (panelFont == null)
            {
                var clockText = transform.Find("Stack/Header/TitleRow/Clock");
                if (clockText == null)
                    clockText = transform.Find("Stack/Header/Clock");
                if (clockText != null)
                    panelFont = clockText.GetComponent<TMP_Text>().font;
            }
        }

        private void BindRefs(OnboardRefs refs)
        {
            clock = refs.Clock;
            moneyValue = refs.MoneyValue;
            ratingValue = refs.RatingValue;
            debtRow = refs.DebtRow;
            debtValue = refs.DebtValue;
            activeSection = refs.ActiveSection;
            activeArea = refs.ActiveArea;
            orderArea = refs.OrderArea;
            abandonButton = refs.AbandonButton;
            playerPrev = refs.PlayerPrev;
            playerNext = refs.PlayerNext;
            radioMute = refs.RadioMute;
            trackTitle = refs.TrackTitle;
            progressFill = refs.ProgressFill;
            if (clock != null && clock.font != null)
                clockGlyph = clock.font.HasCharacter('⏲');
            if (refs.Font != null && refs.Font.HasCharacter('★'))
                ratingPrefix = "★ ";
        }

        private static void SetText(GameObject root, string path, string value)
        {
            var t = root.transform.Find(path);
            if (t != null)
                t.GetComponent<TMP_Text>().text = value;
        }

        // лейбл (приглушённый) + значение (яркое) одной строкой — единый стиль сводки;
        // ColorHex уже возвращает «#hex» — второй решётки после «color=» быть не должно
        private static string StatLine(string label, string value)
        {
            return $"<color={ColorHex(OnboardComputerFactory.LabelColor)}>{label}</color> " +
                   $"<color={ColorHex(OnboardComputerFactory.BrightColor)}>{value}</color>";
        }

        // --- Зона заказа: активный заказ + входящие карточки (in place) ---

        private void RefreshOrder()
        {
            bool hasActive = orders.Active != null;

            if (activeSection != null)
                activeSection.SetActive(hasActive);

            // карточка активного заказа перестраивается только при смене заказа —
            // иначе каждое OffersChanged (протухание) перезапускало бы фейд
            if (!hasActive || orders.Active != builtActiveOffer)
            {
                foreach (GameObject child in activeAreaChildren)
                    Destroy(child);
                activeAreaChildren.Clear();
                activeCardGo = null;
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

        // разворот/сворот карточки in place: Compact гасится, Expanded включается,
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
            // цель по составу строк: без задания Wish скрыт — раскрытая короче,
            // иначе под кнопкой «Взять заказ» остаётся пустота
            bool hasTask = offer.Task != null && offer.Task.Kind != PassengerTaskKind.None;
            float expandedTarget = OnboardComputerFactory.OfferExpandedHeight - (hasTask ? 0f : 56f);
            float target = expand ? expandedTarget : OnboardComputerFactory.OfferCompactHeight;
            heightAnims[card] = StartCoroutine(CardHeightRoutine(card, target));
        }

        private System.Collections.IEnumerator CardHeightRoutine(GameObject card, float target)
        {
            var le = card != null ? card.GetComponent<LayoutElement>() : null;
            if (le == null)
                yield break;
            float from = le.preferredHeight;
            // ease-out cubic как Slide панели; проверка null — карточка могла протухнуть
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

            // тоггла нет: активный заказ всегда в детальном виде
            var cancel = card.transform.Find("Expanded/Cancel");
            if (cancel != null)
            {
                // деньги штрафа списывает GameSession на OrderCancelled (Wallet/DebtService)
                cancel.GetComponent<Button>().onClick.AddListener(() => orders.CancelActive());
                int penalty = OrderService.CancelPenalty(offer.Price);
                SetText(card.gameObject, "Expanded/Cancel/Text", "Отменить · −" + Currency + " " + penalty);
            }

            FillCard(card.transform, offer, activeMode: true);
        }

        // единое заполнение карточки (входящей или активной): аватары, верх, сводка
        private void FillCard(Transform card, OrderOffer offer, bool activeMode)
        {
            // аватары фиксированного размера (узкая колонка / полоса) — только
            // спрайт и тинт, геометрию не трогаем; картинка живёт в child Picture
            ApplyPortrait(card, "Compact/Avatar/Picture", offer.Passenger);
            ApplyPortrait(card, "Expanded/DetailAvatar/Picture", offer.Passenger);

            SetText(card.gameObject, "Compact/Info/Name", offer.Passenger.DisplayName);
            SetText(card.gameObject, "Compact/Info/Route",
                orders.PlanetName(offer.PickupIndex).ToUpper() + " → " + orders.PlanetName(offer.DropoffIndex).ToUpper());

            // строка статуса есть только у актива (узел не строится у входящих)
            var statusNode = card.Find("Expanded/Summary/Status");
            if (statusNode != null)
                statusNode.gameObject.SetActive(activeMode);

            // без задания — строка пожелания скрыта
            PassengerTask task = offer.Task;
            bool hasTask = task != null && task.Kind != PassengerTaskKind.None;
            var wishNode = card.Find("Expanded/Summary/Wish");
            if (wishNode != null)
                wishNode.gameObject.SetActive(hasTask);
            if (hasTask)
                SetText(card.gameObject, "Expanded/Summary/Wish", StatLine("Пожелание клиента:", task.Text));

            // высота активной карточки под фактический состав строк: без Wish
            // фиксированная высота оставляла пустоту под кнопкой отмены
            if (activeMode)
            {
                var cardLe = card.gameObject.GetComponent<LayoutElement>();
                if (cardLe != null)
                    cardLe.preferredHeight = hasTask
                        ? OnboardComputerFactory.ActiveCardHeight
                        : OnboardComputerFactory.ActiveCardHeight - 56f; // Wish 54 + зазор 2
            }

            RefreshLiveDistances(card, offer);
        }

        // портрет: спрайт и тинт; размер фиксирован фабрикой (узкая колонка / полоса)
        // спрайта (NpcPortrait отдаёт обрезанные по альфе спрайты — рамка без
        // мёртвых зон). Полосе на всю ширину (Expanded/DetailAvatar) ширину не трогаем
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

        // живые расстояния «от вас» в сводке — вызывается и при заполнении, и в SlowUpdate
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

        // отмена активного: с пассажиром на борту — только стоя (как в OrderService.CancelActive).
        // Лейбл штрафа обновляем живьём: цена восстановленного из сейва может
        // пересчитаться (старые сейвы цену не хранили)
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
                SetText(activeCardGo, "Expanded/Cancel/Text", "Отменить · −" + Currency + " " + penalty);
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

        // карточка входящего: Compact = [аватар | имя / маршрут / расстояние],
        // «+» в углу разворачивает Expanded (портрет, сводка, кнопка) в ленте скролла
        private GameObject BuildCard(OrderOffer offer)
        {
            GameObject card = OnboardComputerFactory.BuildOfferCard();
            card.transform.SetParent(orderArea, false);

            FillCard(card.transform, offer, activeMode: false);
            float toPassenger = orders.DistanceToPlanet(offer.PickupIndex, ship.transform.position);
            SetText(card, "Compact/Info/Dist",
                "Расстояние до пассажира: " + toPassenger.ToString("0") + " юн.");
            SetText(card, "Expanded/Accept/Text", "Взять заказ · " + Currency + " " + FormatCredits(offer.Price));

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

        // --- Живые данные ---

        private void Update()
        {
            // AudioManager живёт между сценами: биндим лениво, отписка в OnDestroy
            if (audioManager == null)
                audioManager = AudioManager.Instance;
            if (audioManager != null && !musicBound)
            {
                audioManager.MusicStateChanged += RefreshMusicPlayer;
                musicBound = true;
                RefreshMusicPlayer(); // музыка могла стартовать до бинда (SceneMusic)
            }

            AnimateMoney();
            AnimateRating();
            AnimateDebt();

            if (!bound)
            {
                var session = FindAnyObjectByType<GameSession>();
                ship = FindAnyObjectByType<Ship>();
                if (session == null || session.Orders == null || ship == null || ship.Config == null)
                    return;
                orders = session.Orders;
                hull = ship.GetComponent<HullSystem>();
                cachedSession = session;
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
                    rating = session.Rating;
                    rating.RatingChanged += ShowRating;
                    ShowRating(rating.Value);
                }
                // «ОТКЛЮЧИТЬ КОРАБЛЬ»: сразу штатный пайплайн потери (HullSystem →
                // респавн, DebtService.RegisterAccident за аренду, Fleet.NotifyShipLost)
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
            clock.text = (clockGlyph ? "⏲ " : "") + minutes / 60 + ":" + (minutes % 60).ToString("00");

            if (debt != null)
                RefreshDebt(debt.Balance); // страховка от пропущенного события
            // прогресс трека плеера (П9.3): доля clip position/length, опрос в SlowUpdate
            if (progressFill != null && audioManager != null)
                progressFill.anchorMax = new Vector2(audioManager.MusicProgress, 1f);

            // расстояния на компактных карточках — живые
            foreach (var pair in cardMap)
            {
                if (pair.Value == null)
                    continue;
                var dist = pair.Value.transform.Find("Compact/Info/Dist");
                if (dist != null && ship != null)
                {
                    float toPassenger = orders.DistanceToPlanet(pair.Key.PickupIndex, ship.transform.position);
                    dist.GetComponent<TMP_Text>().text =
                        "Расстояние до пассажира: " + toPassenger.ToString("0") + " юн.";
                }
            }

            // раскрытая входящая: живые расстояния сводки
            if (expandedOffer != null && cardMap.TryGetValue(expandedOffer, out GameObject expandedCard)
                && expandedCard != null && expandedCard.activeInHierarchy)
                RefreshLiveDistances(expandedCard.transform, expandedOffer);

            // активный заказ: сводка, статус, прогресс задания, доступность отмены
            if (orders.Active != null && activeCardGo != null && activeCardGo.activeInHierarchy)
            {
                RefreshLiveDistances(activeCardGo.transform, orders.Active);
                UpdateActiveWish();
                RefreshCancelState();
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
            if (task.Kind == PassengerTaskKind.WaitOnPlanet)
                return "Посидели " + Mathf.Min(task.WaitAccumulated, task.WaitSeconds).ToString("0")
                    + "/" + task.WaitSeconds.ToString("0") + " с";
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

        // --- СОСТОЯНИЕ: деньги, рейтинг, долг ---

        // кредит обновляется событием, значение плавно докатывается; до первого
        // события moneyShown == -1 (сентинел) — текст не пишем, не мигнув «Đ -1»
        private void ShowCredits(int credits)
        {
            moneyTarget = credits;
            if (moneyShown < 0f)
                moneyShown = credits;
            UpdateMoneyText();
        }

        private void AnimateMoney()
        {
            if (moneyValue == null || moneyShown < 0f || Mathf.Approximately(moneyShown, moneyTarget))
                return;
            float speed = Mathf.Max(80f, Mathf.Abs(moneyTarget - moneyShown) * 4f);
            moneyShown = Mathf.MoveTowards(moneyShown, moneyTarget, speed * Time.unscaledDeltaTime);
            if (Mathf.Abs(moneyTarget - moneyShown) < 0.5f)
                moneyShown = moneyTarget;
            UpdateMoneyText();
        }

        private void UpdateMoneyText()
        {
            if (moneyValue != null && moneyShown >= 0f)
                moneyValue.text = Currency + " " + FormatCredits(Mathf.RoundToInt(moneyShown));
        }

        // компактный формат: 10000 -> 10k, 1 400 000 -> 1.4m — размер строки предсказуем
        public static string FormatCredits(int value)
        {
            if (value >= 1_000_000)
                return (value / 1_000_000f).ToString("0.#", CultureInfo.InvariantCulture) + "m";
            if (value >= 1000)
                return (value / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return value.ToString();
        }

        // рейтинг догоняет целевое значение, цвет — динамика (красный/зелёный)
        private void ShowRating(float value)
        {
            if (ratingValue == null)
                return;
            if (ratingShown < 0f)
                ratingShown = value;
            ratingDropping = value < ratingShown - 0.001f;
            WriteRatingText();
        }

        private void AnimateRating()
        {
            // сессия кэшируется при бинде: тяжёлый поиск в каждом кадре не нужен
            var s2 = cachedSession != null ? cachedSession : FindAnyObjectByType<GameSession>();
            cachedSession = s2;
            var rating2 = s2 != null ? s2.Rating : null;
            if (rating2 == null || ratingValue == null)
                return;
            float target = rating2.Value;
            if (ratingShown < 0f)
                ratingShown = target;
            if (Mathf.Approximately(ratingShown, target))
                return;
            ratingDropping = target < ratingShown;
            // плавный glide: скорость пропорциональна дельте (лёгкий разгон и мягкий
            // докат), минимум не даёт хвосту «залипать» — виден перелист 3.0 → 4.5
            float delta = Mathf.Abs(target - ratingShown);
            float speed = Mathf.Max(delta * 4f, 0.4f);
            ratingShown = Mathf.MoveTowards(ratingShown, target, speed * Time.unscaledDeltaTime);
            if (Mathf.Abs(target - ratingShown) < 0.005f)
                ratingShown = target;
            WriteRatingText();
        }

        private void WriteRatingText()
        {
            if (ratingValue == null)
                return;
            ratingValue.text = ratingPrefix + ratingShown.ToString("0.0", CultureInfo.InvariantCulture);
            // в покое — зелёный (цвет рейтинга в шапке), в динамике — красный/зелёный
            ratingValue.color = ratingDropping
                ? OnboardComputerFactory.DangerColor
                : OnboardComputerFactory.RatingGreen;
        }

        // долг: строка скрыта при нуле (layout сжимается), значение катится,
        // цвет «дышит» — рост горячее, погашение спокойнее
        private void RefreshDebt(int value)
        {
            if (debtValue == null)
                return;
            if (debtShown < 0f)
                debtShown = value;
            debtRising = value > debtShown + 0.5f;
            debtTarget = value;
            if (debtRow != null)
                debtRow.SetActive(value > 0 || debtShown > 0.5f);
            UpdateDebtText();
        }

        private void AnimateDebt()
        {
            if (debtValue == null || debtShown < 0f || Mathf.Approximately(debtShown, debtTarget))
                return;
            float speed = Mathf.Max(40f, Mathf.Abs(debtTarget - debtShown) * 3f);
            debtShown = Mathf.MoveTowards(debtShown, debtTarget, speed * Time.unscaledDeltaTime);
            if (Mathf.Abs(debtTarget - debtShown) < 0.5f)
                debtShown = debtTarget;
            if (debtRow != null && debtTarget <= 0 && debtShown <= 0.5f)
                debtRow.SetActive(false); // докатка до нуля закончилась — строка уходит
            UpdateDebtText();
        }

        private void UpdateDebtText()
        {
            if (debtValue == null || debtShown < 0f)
                return;
            debtValue.text = "Текущий долг: " + Currency + " " + FormatCredits(Mathf.RoundToInt(debtShown));
            debtValue.color = debtRising && debtTarget > debtShown + 0.5f
                ? OnboardComputerFactory.DangerColor
                : OnboardComputerFactory.DebtCalmColor;
        }

        // --- Плеер «ЭФИР» ---

        // узлы построены фабрикой (звуковой хук уже внутри Button); менеджер
        // живёт между сценами — разрешается на клике
        private void SetupMusicPlayer()
        {
            if (playerPrev != null)
                playerPrev.onClick.AddListener(() => AudioManager.Instance?.PlayPlaylistNext(-1));
            if (playerNext != null)
                playerNext.onClick.AddListener(() => AudioManager.Instance?.PlayPlaylistNext(1));
            if (radioMute != null)
                radioMute.onClick.AddListener(() => AudioManager.Instance?.ToggleMusicMuted());
        }

        // имя станции и метка мьюта по состоянию менеджера; событие MusicStateChanged
        private void RefreshMusicPlayer()
        {
            if (trackTitle == null)
                return; // биндер разрушен, менеджер живёт между сценами
            if (radioMute != null)
            {
                TMP_Text muteLabel = radioMute.GetComponentInChildren<TMP_Text>();
                if (muteLabel != null)
                    muteLabel.text = audioManager != null && audioManager.MusicMuted ? "ЗВУК: ВЫКЛ" : "ЗВУК: ВКЛ";
            }
            if (audioManager == null || audioManager.MusicPlaylist.Count == 0)
            {
                trackTitle.text = "ЭФИР · треки не найдены";
            }
            else
            {
                trackTitle.text = "СТАНЦИЯ · " + StationName(audioManager.CurrentTrack);
            }
        }

        // у SoundEntry DisplayName нет — карта известных треков,
        // незнакомые — имя enum
        // названия радиостанций (SoundId станции -> имя в эфире)
        private static string StationName(SoundId id)
        {
            switch (id)
            {
                case SoundId.MusicFlight: return "Дорожная волна";
                case SoundId.MusicStation1: return "Космо-классика";
                case SoundId.MusicStation2: return "Ретро-волна";
                case SoundId.MusicStation3: return "Механика драйв";
                case SoundId.MusicStation4: return "Ночная частота";
                default: return "—";
            }
        }

        private static TMP_Text FindText(Transform parent, string name)
        {
            var found = parent.Find(name);
            return found != null ? found.GetComponent<TMP_Text>() : null;
        }
    }
}
