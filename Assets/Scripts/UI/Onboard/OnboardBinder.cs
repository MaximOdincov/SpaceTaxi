using TMPro;
using UnityEngine;
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
    // Секции класса — partial-файлы рядом: карточки заказов — OnboardBinder.Cards.cs,
    // деньги/рейтинг/долг/чип статуса/строка выполнения — OnboardBinder.Stats.cs,
    // плеер «ЭФИР» — OnboardBinder.Radio.cs.
    public partial class OnboardBinder : MonoBehaviour
    {
        private TMP_Text clock;
        // символ валюты проекта и глиф часов (есть не в каждом атласе — проверка при бинде)
        private bool clockGlyph;
        // Н10.9: «Đ»/FormatCredits/FormatClock переехали в UiFormat (Слой 1)
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
            // клик-хук при бинде каркаса (как плеер «ЭФИР»): сумма/доступность
            // читаются в момент клика, когда wallet/debt уже привязаны
            debtRepayButton = refs.DebtRepayButton;
            if (debtRepayButton != null)
                debtRepayButton.onClick.AddListener(RepayDebtNow);
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
            if (refs.Font != null && refs.Font.HasCharacter('✓'))
                doneGlyph = "✓";
            if (refs.Font != null && refs.Font.HasCharacter('✗'))
                failGlyph = "✗"; // иначе остаётся «×»
            if (refs.Font != null && refs.Font.HasCharacter('…'))
                ellipsis = "…"; // иначе остаётся "..."
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
                UpdateActiveStatusChip();
                UpdateActiveProgressRow();
                RefreshCancelState();
            }
        }
    }
}
