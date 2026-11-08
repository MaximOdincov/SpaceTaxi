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
    // из OnboardRefs и событиям систем.
    public class OnboardBinder : MonoBehaviour
    {
        private TMP_Text clock;
        private TMP_Text moneyValue, ratingValue;
        private Image moneyIconImage, ratingIconImage;
        // анимация рейтинга: показанное значение догоняет целевое, цвет — динамика
        private float ratingShown = -1f;
        private bool ratingDropping;
        private GameObject activeSection;
        private RectTransform activeArea; // блок активного заказа + кнопка отмены
        private RectTransform orderArea;  // контент скролла входящих
        private TMP_Text econCounters, debtValue;
        private Button abandonButton;
        private Button cancelButton;
        private OrderOffer builtActiveOffer; // блок перестраивается только при смене заказа

        [SerializeField] private Sprite cashIcon;
        [SerializeField] private Sprite ratingIcon;

        [Tooltip("Единый шрифт панели: применяется ко всем текстам каркаса и карточек. Пусто — шрифт часов старого каркаса")]
        [SerializeField] private TMP_FontAsset panelFont;

        private Ship ship;
        private OrderService orders;
        private HullSystem hull;
        private Wallet wallet;
        private DebtService debt;
        private float nextSlowUpdate;
        private bool bound;
        private readonly List<GameObject> activeAreaChildren = new List<GameObject>();

        // плеер «ЭФИР»
        private Button playerPrev, playerPlay, playerNext;
        private Button radioMute;
        private GameSession session; // для районов карточек (уровень)
        private TMP_Text trackTitle;
        private RectTransform progressFill;
        private AudioManager audioManager;
        private bool musicBound;

        private const float DeadlineWarnSeconds = 15f; // остаток дедлайна, с которого строка краснеет
        // §3.2: увольнение после третьей непогашенной аварии (как в StationScreenController)
        private const int MaxUnpaidAccidents = 3;

        private static readonly Color LabelColor = new Color(0.357f, 0.788f, 0.561f);   // #5BC98F
        private static readonly Color DangerColor = new Color(1f, 0.478f, 0.361f);

        private void Awake()
        {
            ApplyPanelFont();
            // каркас всегда кодом: и для старых сцен, и поверх мокапа префаба
            BindRefs(OnboardComputerFactory.Build(transform, fontOverride: panelFont));
            BindIcons();
            SetupMusicPlayer();
        }

        private void OnDestroy()
        {
            // AudioManager переживает сцену — иначе событие пришло бы в разрушенный биндер
            if (musicBound && audioManager != null)
                audioManager.MusicStateChanged -= RefreshMusicPlayer;
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

        // иконки денег/рейтинга: спрайты сериализованы на биндере (в билде
        // AssetDatabase недоступен — фабрика грузит их только в редакторе)
        private void BindIcons()
        {
            if (cashIcon != null && moneyIconImage != null)
                moneyIconImage.sprite = cashIcon;
            if (ratingIcon != null && ratingIconImage != null)
                ratingIconImage.sprite = ratingIcon;
        }

        private void BindRefs(OnboardRefs refs)
        {
            clock = refs.Clock;
            moneyValue = refs.MoneyValue;
            ratingValue = refs.RatingValue;
            moneyIconImage = refs.MoneyIcon;
            ratingIconImage = refs.RatingIcon;
            activeSection = refs.ActiveSection;
            activeArea = refs.ActiveArea;
            orderArea = refs.OrderArea;
            econCounters = refs.EconCounters;
            debtValue = refs.DebtValue;
            session = FindAnyObjectByType<GameSession>();
            abandonButton = refs.AbandonButton;
            playerPrev = refs.PlayerPrev;
            playerPlay = refs.PlayerPlay;
            playerNext = refs.PlayerNext;
            radioMute = refs.RadioMute;
            trackTitle = refs.TrackTitle;
            progressFill = refs.ProgressFill;
        }

        private static void SetText(GameObject root, string path, string value)
        {
            var t = root.transform.Find(path);
            if (t != null)
                t.GetComponent<TMP_Text>().text = value;
        }

        // --- Зона заказа: активный блок + список входящих ---

        private void RefreshOrder()
        {
            bool hasActive = orders.Active != null;
            if (activeSection != null)
                activeSection.SetActive(hasActive);

            // блок активного заказа перестраивается только при смене заказа —
            // иначе каждое OffersChanged (протухание карточек) перезапускало бы фейд
            if (!hasActive || orders.Active != builtActiveOffer)
            {
                foreach (GameObject child in activeAreaChildren)
                    Destroy(child);
                activeAreaChildren.Clear();
                cancelButton = null;
                if (hasActive)
                    BuildActiveOrder();
                builtActiveOffer = hasActive ? orders.Active : null;
            }

            BuildIncoming();
            UpdateAcceptButtons();
        }

        private void BuildActiveOrder()
        {
            OrderOffer offer = orders.Active;

            GameObject row = OnboardComputerFactory.BuildActiveOrderBlock();
            row.transform.SetParent(activeArea, false);
            activeAreaChildren.Add(row);
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

            SetText(row, "Info/Name", offer.Passenger.DisplayName);
            SetText(row, "Info/Route",
                orders.PlanetName(offer.PickupIndex).ToUpper() + " → " + orders.PlanetName(offer.DropoffIndex).ToUpper()
                + "  ·  " + orders.RouteDistance(offer).ToString("0") + " юн");
            // П5.3: вместо пожелания — текст задания, если оно выпало;
            // таймер дедлайна дописывается в SlowUpdate
            PassengerTask task = offer.Task;
            string wish = task != null && task.Kind != PassengerTaskKind.None
                ? task.Text
                : PassengerProfile.PickPhrase(offer.Passenger.Wishes);
            SetText(row, "Info/Wish", string.IsNullOrEmpty(wish) ? "" : "«" + wish + "»");

            var cancel = OnboardComputerFactory.Button(activeArea, "ОТКАЗАТЬСЯ ОТ ЗАКАЗА",
                new Color(LabelColor.r, LabelColor.g, LabelColor.b, 0.25f), LabelColor, 15f);
            var cancelLe = cancel.AddComponent<LayoutElement>();
            cancelLe.minHeight = 30f;
            cancelLe.flexibleWidth = 1f;
            cancelButton = cancel.GetComponent<Button>();
            cancelButton.onClick.AddListener(() => orders.CancelActive());
            activeAreaChildren.Add(cancel);
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

        // входящие видны всегда; при активном заказе кнопка «взять» неактивна
        private void UpdateAcceptButtons()
        {
            foreach (GameObject card in cardMap.Values)
            {
                Transform accept = card.transform.Find("Accept");
                if (accept != null)
                    accept.GetComponent<Button>().interactable = orders.Active == null;
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

        // карточка входящего: [аватар | имя+цена / маршрут / дистанция | ВЗЯТЬ | НЕТ],
        // клик по карточке раскрывает детали (район, длина маршрута, задание)
        private GameObject BuildCard(OrderOffer offer)
        {
            GameObject card = OnboardComputerFactory.BuildOfferCard();
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

            SetText(card, "Info/NameRow/Name", offer.Passenger.DisplayName);
            var priceNode = card.transform.Find("Info/NameRow/Price");
            if (priceNode != null)
                priceNode.GetComponent<TMP_Text>().text = "¤ " + FormatCredits(offer.Price);

            SetText(card, "Info/Route",
                orders.PlanetName(offer.PickupIndex).ToUpper() + " → " + orders.PlanetName(offer.DropoffIndex).ToUpper());
            float toPassenger = orders.DistanceToPlanet(offer.PickupIndex, ship.transform.position);
            SetText(card, "Info/DistRow/Dist", "до пассажира: " + toPassenger.ToString("0") + " юн");

            // детали (скрыты до раскрытия): район, длина маршрута, задание
            PassengerTask task = offer.Task;
            string taskText = task != null && task.Kind != PassengerTaskKind.None
                ? "Задание: " + task.Text
                : "";
            string wishPhrase = task != null && task.Kind != PassengerTaskKind.None
                ? ""
                : PassengerProfile.PickPhrase(offer.Passenger.Wishes);
            SetText(card, "Info/Details/Sector", "Район: " + SectorName(offer.PickupIndex));
            SetText(card, "Info/Details/RouteLen", "Длина маршрута: " + orders.RouteDistance(offer).ToString("0") + " юн");
            SetText(card, "Info/Details/Task", string.IsNullOrEmpty(taskText) && !string.IsNullOrEmpty(wishPhrase)
                ? "Пожелание: «" + wishPhrase + "»"
                : taskText);

            OrderOffer captured = offer;
            var accept = card.transform.Find("Accept");
            if (accept != null)
                accept.GetComponent<Button>().onClick.AddListener(() => orders.Accept(captured));
            var decline = card.transform.Find("Decline");
            if (decline != null)
                decline.GetComponent<Button>().onClick.AddListener(() => orders.Decline(captured));

            // раскрытие: клик по карточке (кнопки внутри съедают свои клики сами)
            var cardButton = card.GetComponent<Button>();
            if (cardButton != null)
            {
                var details = card.transform.Find("Info/Details");
                var le = card.GetComponent<LayoutElement>();
                cardButton.onClick.AddListener(() =>
                {
                    bool opening = details != null && !details.gameObject.activeSelf;
                    if (details != null)
                        details.gameObject.SetActive(opening);
                    StartCoroutine(TweenCardHeight(le, opening ? 176f : 96f));
                });
            }
            return card;
        }

        // плавное изменение высоты карточки (раскрытие/сворачивание заказа)
        private System.Collections.IEnumerator TweenCardHeight(LayoutElement le, float target)
        {
            if (le == null)
                yield break;
            float start = le.minHeight;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.18f;
                le.minHeight = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
                yield return null;
            }
            le.minHeight = target;
        }

        // район планеты для карточки (сектора карты П1.3)
        private string SectorName(int index)
        {
            var level = session != null ? session.Level : null;
            if (level == null || index < 0 || index >= level.Bodies.Count)
                return "—";
            switch (level.Bodies[index].Sector)
            {
                case SpaceTaxi.World.Sector.Hub: return "хаб";
                case SpaceTaxi.World.Sector.Near: return "ближний";
                case SpaceTaxi.World.Sector.Mid1: return "средний-1";
                case SpaceTaxi.World.Sector.Mid2: return "средний-2";
                case SpaceTaxi.World.Sector.Far: return "дальний";
                default: return "—";
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

            AnimateRating();

            if (!bound)
            {
                var session = FindAnyObjectByType<GameSession>();
                ship = FindAnyObjectByType<Ship>();
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

            econCounters.text = orders.Deliveries + " доставок · " + orders.Failures + " провалов"
                + (debt != null ? " · аварий: " + debt.UnpaidAccidents + "/" + MaxUnpaidAccidents : "");
            if (debt != null)
                RefreshDebt(debt.Balance); // страховка от пропущенного события
            // прогресс трека плеера (П9.3): доля clip position/length, опрос в SlowUpdate
            if (progressFill != null && audioManager != null)
                progressFill.anchorMax = new Vector2(audioManager.MusicProgress, 1f);
            abandonButton.gameObject.SetActive(ship.Fuel <= 0f);
            if (cancelButton != null && orders.Active != null)
                cancelButton.gameObject.SetActive(orders.Active.State == OrderState.Active
                    || ship.State == ShipState.Landed);

            var statusText = FindText(activeArea, "ActiveOrder/Info/OrderStatus");
            if (orders.Active != null && statusText != null)
                statusText.text = orders.Active.State == OrderState.Aboard
                    ? Col("Пассажир", "НА БОРТУ → " + orders.PlanetName(orders.Active.DropoffIndex).ToUpper()) + "  ·  " + PriceTag(orders.Active.Price)
                    : Col("Пассажир", "ожидает на " + orders.PlanetName(orders.Active.PickupIndex).ToUpper()) + "  ·  " + PriceTag(orders.Active.Price);

            // остаток дедлайна Торопыги (П5.3): строка задания, обновление раз в SlowUpdate
            var wishText = FindText(activeArea, "ActiveOrder/Info/Wish");
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
                // прогресс ожидания на планете (П8.1): посидели X/10 с
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

        // кредит обновляется событием, без опроса в SlowUpdate
        private void ShowCredits(int credits)
        {
            if (moneyValue != null)
                moneyValue.text = "¤ " + FormatCredits(credits);
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
            ratingValue.text = ratingShown.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private void AnimateRating()
        {
            var s2 = FindAnyObjectByType<GameSession>();
            var orders = s2 != null ? s2.Orders : null;
            if (orders == null || s2.Rating == null || ratingValue == null)
                return;
            float target = s2.Rating.Value;
            if (ratingShown < 0f)
                ratingShown = target;
            if (Mathf.Approximately(ratingShown, target))
                return;
            ratingDropping = target < ratingShown;
            ratingShown = Mathf.MoveTowards(ratingShown, target, Time.unscaledDeltaTime * 3.5f);
            if (Mathf.Abs(target - ratingShown) < 0.05f)
                ratingShown = target;
            ratingValue.text = ratingShown.ToString("0.0", CultureInfo.InvariantCulture);
            ratingValue.color = ratingDropping
                ? OnboardComputerFactory.DangerColor
                : OnboardComputerFactory.RatingGreen;
            if (Mathf.Approximately(ratingShown, target))
                ratingValue.color = Color.white;
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

        // --- Плеер «ЭФИР» ---

        // узлы построены фабрикой (звуковой хук уже внутри Button); менеджер
        // живёт между сценами — разрешается на клике
        private void SetupMusicPlayer()
        {
            if (playerPrev != null)
                playerPrev.onClick.AddListener(() => AudioManager.Instance?.PlayPlaylistNext(-1));
            if (playerPlay != null)
                playerPlay.onClick.AddListener(() => AudioManager.Instance?.ToggleMusicPause());
            if (playerNext != null)
                playerNext.onClick.AddListener(() => AudioManager.Instance?.PlayPlaylistNext(1));
            if (radioMute != null)
                radioMute.onClick.AddListener(() => AudioManager.Instance?.ToggleMusicMuted());
        }

        // имя трека и глиф ▶/⏸ по состоянию менеджера; событие MusicStateChanged
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
            if (playerPlay != null)
            {
                TMP_Text glyph = playerPlay.GetComponentInChildren<TMP_Text>();
                if (glyph != null)
                    glyph.text = audioManager != null && audioManager.MusicPaused ? "||" : "▶";
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
