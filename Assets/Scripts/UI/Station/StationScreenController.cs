using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceTaxi.Audio;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;
using SpaceTaxi.Physics;
using SpaceTaxi.World;
using UiKit;

namespace SpaceTaxi.UI
{
    // Экран станции (клавиша B): попап с вкладками МАГАЗИН / УЛУЧШЕНИЯ /
    // ЗАПРАВКА / РЕМОНТ. Открывается только стоя (Landed) на планете с
    // услугами, мир при этом НЕ ставится на паузу. Панель строится кодом при
    // первом открытии, дальше значения перечитываются по событиям
    // (деньги/топливо/корпус/парк). Секции магазина и улучшений вынесены в
    // отдельные классы, этот контроллер — каркас: вкладки, заправка, ремонт.
    public class StationScreenController : MonoBehaviour, IStationSectionHost
    {
        private const int SortingOrder = 100; // поверх HUD/паузы, ниже увольнения (200)
        private const int SegmentCount = 10;  // как в HUD
        // цвета сегментов и подсветка — StationUI (SegmentRow/SetSegments)

        private bool bound;
        private bool opened;
        private GameObject screen;
        private GameSession session;
        private Ship ship;
        private HullSystem hull;
        private LandingSystem landing;
        private PauseScreenController pauseController;
        private MapScreenController mapController;
        private OrderCompleteScreenController orderComplete; // модал завершения выше станции

        // ввод: действие Station из action map "UI" (клавиша B)
        private InputActionAsset input;
        private InputAction stationAction;

        private TMP_Text stationTitle;
        private TMP_Text creditsValue; // перечитывается по Wallet.CreditsChanged
        private TMP_Text fuelValue;
        private TMP_Text hullValue;
        private TMP_Text fuelStatus;
        private TMP_Text hullStatus;
        private Image[] fuelSegments;   // ячейки бака на всю ширину панели
        private RectTransform hpFill;   // заливка HP-бара ремонта
        private Button fuelButton;
        private TMP_Text fuelButtonLabel;
        // заправка: слайдер с живым предрасчётом, по умолчанию весь объём
        private TMP_Text fuelStockLine;  // «Топлива на станции: N ед · цена M Đ/ед»
        private TMP_Text fuelOfferLabel; // «Заправить: X ед · Đ Y»
        private Slider fuelSlider;
        // ремонт: одна кнопка «Починить» вместо пакетов
        private TMP_Text repairTierLine; // «Мастерская тир N · наценка ×K»
        private TMP_Text repairOffer;    // «Починка: +N HP · Đ M» или отказ по тиру корабля
        private Button repairButton;
        private TMP_Text repairButtonLabel;

        // окна-вкладки: видимость переключает SelectTab, состав — ApplyTabs
        private GameObject fuelWindow;
        private GameObject hullWindow;
        private GameObject upgradeWindow; // улучшения + гашение долга

        // полный набор вкладок только у хаба МТО
        private enum StationTab { Shop, Upgrades, Fuel, Repair }

        private readonly Dictionary<StationTab, Button> tabButtons = new Dictionary<StationTab, Button>();
        private readonly Dictionary<StationTab, Image> tabBackgrounds = new Dictionary<StationTab, Image>();
        private readonly Dictionary<StationTab, TMP_Text> tabLabels = new Dictionary<StationTab, TMP_Text>();
        private StationTab activeTab = StationTab.Fuel;
        private Planet currentPlanet; // планета, под которую собраны вкладки

        // секции экрана — обычные классы, строят UI в переданных родителях
        private StationUpgradesSection upgradesSection;
        private StationShowcaseSection showcaseSection;
        private ToastNotifier toast;

        public bool IsOpened => opened;

        public bool EscJustClosed { get; private set; }

        // IStationSectionHost: секции экрана ходят в сессию/борт и тосты через хоста
        public GameSession Session => session;
        public Ship Ship => ship;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // домен редактора может не перезагружаться между запусками — снимаем старую подписку
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Spawn();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Spawn();
        }

        private static void Spawn()
        {
            if (FindAnyObjectByType<StationScreenController>() != null)
                return;
            new GameObject("StationScreenController").AddComponent<StationScreenController>();
        }

        private void Update()
        {
            BindSession();
            if (stationAction == null)
                BindInput();

            if (stationAction != null && stationAction.WasPressedThisFrame())
            {
                // экран завершения заказа модален — B ему не подчиняется
                if (orderComplete == null) orderComplete = FindAnyObjectByType<OrderCompleteScreenController>();
                if (orderComplete != null && orderComplete.IsOpened)
                    return;
                if (opened) Close();
                else TryOpen();
            }
            if (opened && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
                EscJustClosed = true;
            }

            // контекст потерян (взлёт, гибель, потеря корабля) — экран закрывается сам
            if (opened && !CanOpen())
                Close();

            // ротация витрины: слот сменился — витрина перерисовывается сама
            // (проверка дешёвая, можно звать каждый кадр)
            if (opened)
                showcaseSection?.UpdateRotation();
        }

        private void LateUpdate()
        {
            EscJustClosed = false;
        }

        private void OnEnable()
        {
            BindInput();
        }

        private void OnDisable()
        {
            if (input != null)
                input.FindActionMap("UI")?.Disable();
        }

        // лениво, пока сессия не соберётся (стиль биндеров)
        private void BindSession()
        {
            if (bound)
                return;
            session = FindAnyObjectByType<GameSession>();
            if (session == null || session.Ship == null || session.Wallet == null)
                return;
            ship = session.Ship;
            hull = ship.GetComponent<HullSystem>();
            landing = ship.GetComponent<LandingSystem>();
            // подписки одни за жизнь сессии: экран перечитывает значения сам
            ship.FuelChanged += OnShipValuesChanged;
            hull.HpChanged += OnShipValuesChanged;
            hull.Respawned += OnRespawned;
            session.Wallet.CreditsChanged += OnCreditsChanged;
            // блоки магазина перечитываются сами (сессия одна на сцену, отписки не нужны)
            session.Fleet.UpgradesChanged += OnShopChanged;
            session.Fleet.ShipChanged += OnShopChanged; // сменил корабль — блок перестроился
            session.Debt.DebtChanged += OnDebtChanged; // Action<int>: аргумент не нужен, блок перечитает сам
            bound = true;
        }

        // привязка из OnEnable и лениво из Update — кто раньше
        private void BindInput()
        {
            if (stationAction != null)
                return;
            if (input == null && session != null)
                input = session.Input;
            var map = input != null ? input.FindActionMap("UI") : null;
            if (map == null)
                return;
            stationAction = map.FindAction("Station");
            map.Enable();
        }

        // покупки только стоя на планете с услугами
        public bool CanOpen()
        {
            return ship != null
                && ship.State == ShipState.Landed
                && ship.LandedOn != null
                && (ship.LandedOn.HasFuel || ship.LandedOn.HasRepair);
        }

        private void TryOpen()
        {
            if (!CanOpen())
                return;
            if (mapController == null) mapController = FindAnyObjectByType<MapScreenController>();
            if (mapController != null && mapController.IsOpened)
                return; // карта открыта — станция поверх неё не нужна
            var complete = FindAnyObjectByType<OrderCompleteScreenController>();
            if (complete != null && complete.IsOpened)
                return; // модальный экран завершения — станция не должна лезть поверх

            if (screen == null)
                screen = BuildScreen();
            screen.SetActive(true);
            opened = true;
            fuelStatus.text = "";
            hullStatus.text = "";
            Refresh();
            // по умолчанию весь доступный объём — кнопка сразу «ЗАПРАВИТЬ ДО ПОЛНОГО»
            if (fuelSlider != null && FuelAvailable() > 0)
                fuelSlider.value = FuelAvailable();

            if (pauseController == null) pauseController = FindAnyObjectByType<PauseScreenController>();
            if (pauseController != null)
                pauseController.NotifyStationOpened();
        }

        private void Close()
        {
            if (!opened)
                return;
            opened = false;
            if (screen != null)
                screen.SetActive(false);

            if (pauseController == null) pauseController = FindAnyObjectByType<PauseScreenController>();
            if (pauseController != null)
                pauseController.NotifyStationClosed();
        }

        private void OnShipValuesChanged()
        {
            if (opened)
                Refresh();
        }

        private void OnCreditsChanged(int credits)
        {
            if (opened)
                Refresh();
        }

        private void OnRespawned()
        {
            if (opened)
                Close(); // респавн мог унести на другую планету
        }

        // --- Заправка: цена за единицу и запас — по тиру сети планеты ---

        // конфиг цен: из OrderService (ассет сцены / Resources) или кодовый фолбэк
        private PricingConfig Pricing =>
            session != null && session.Orders != null ? session.Orders.Pricing : PricingConfig.Default;

        private int FuelTier => currentPlanet != null ? currentPlanet.FuelTier : PricingConfig.BaseFuelTier;

        // район станции для цены топлива (без планеты — базовый Near)
        private Sector FuelSector => currentPlanet != null ? currentPlanet.Sector : Sector.Near;

        // сеть станции пуста: заправка есть, топлива в ней нет
        private bool FuelOutage => currentPlanet != null && currentPlanet.FuelOutage;

        private int RepairTier => currentPlanet != null ? currentPlanet.RepairTier : PricingConfig.BaseRepairTier;

        private int FuelAvailable()
        {
            if (ship == null || currentPlanet == null || FuelOutage)
                return 0;
            return FuelNetwork.AvailableUnits(ship.Eff.FuelMax, ship.Fuel, FuelTier, Pricing);
        }

        // оплата ровно за выбранное: Wallet.TrySpend → Ship.Fuel → ReportServiced
        private void ConfirmRefuel()
        {
            if (!CanOpen() || session == null || fuelSlider == null || FuelOutage)
                return;
            int units = Mathf.RoundToInt(fuelSlider.value);
            int cost = FuelNetwork.CostOf(units, FuelTier, FuelSector, Pricing);
            if (units <= 0 || cost <= 0 || !session.Wallet.TrySpend(cost))
                return;
            ship.Fuel += units; // сеттер клампит и шлёт FuelChanged — HUD и экран обновятся
            fuelStatus.text = $"Заправлено {units} ед за {UiFormat.Currency} {UiFormat.FormatCredits(cost)}";
            landing.ReportServiced(ship.LandedOn.DisplayName);
        }

        // запас сети и диапазон слайдера — при каждом Refresh (бак меняется
        // заправкой и улучшениями)
        private void UpdateFuelWindow()
        {
            int available = FuelAvailable();
            float perUnit = FuelNetwork.PricePerUnit(FuelTier, FuelSector, Pricing);

            // 0 доступно: сеть пуста / бак полон / запас сети ниже текущего топлива
            fuelStockLine.text = FuelOutage
                ? "Сеть станции пуста — топлива нет"
                : available > 0
                    ? $"Топлива на станции: {available} ед · цена {perUnit:0.##} {UiFormat.Currency}/ед"
                    : ship.Fuel >= ship.Eff.FuelMax - 0.5f
                        ? "Бак полон — заправка не нужна"
                        : "Запас сети ниже текущего топлива";

            fuelSlider.minValue = 0f;
            fuelSlider.maxValue = Mathf.Max(1, available); // 0..0 слайдер не показывает
            fuelSlider.interactable = !FuelOutage && available > 0;
            fuelSlider.SetValueWithoutNotify(Mathf.Clamp(fuelSlider.value, 0f, available));

            UpdateFuelOffer();
        }

        // живой предрасчёт суммы при движении слайдера
        private void UpdateFuelOffer()
        {
            int available = FuelAvailable();
            int selected = FuelOutage
                ? 0
                : Mathf.Clamp(Mathf.RoundToInt(fuelSlider.value), 0, available);
            int cost = FuelNetwork.CostOf(selected, FuelTier, FuelSector, Pricing);

            fuelOfferLabel.text = FuelOutage
                ? "Заправка не работает"
                : $"Заправить: {selected} ед · {UiFormat.Currency} {UiFormat.FormatCredits(cost)}";
            fuelButton.interactable = !FuelOutage && selected > 0 && session.Wallet.Credits >= cost;
            fuelButtonLabel.text = FuelOutage
                ? "ТОПЛИВА НЕТ"
                : selected <= 0 ? "ВЫБЕРИТЕ ОБЪЁМ"
                : selected >= available
                    ? $"ЗАПРАВИТЬ ДО ПОЛНОГО · {UiFormat.Currency} {UiFormat.FormatCredits(cost)}"
                    : $"ЗАПРАВИТЬ · {UiFormat.Currency} {UiFormat.FormatCredits(cost)}";
        }

        // слайдер собирается кодом по схеме UGUI Slider (трек + fill + ручка)
        private static Slider BuildFuelSlider(Transform parent)
        {
            var go = new GameObject("FuelSlider", typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            var dim = OnboardComputerFactory.LabelColor;

            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(go.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)track.transform);
            track.GetComponent<Image>().color = Ui.WithAlpha(dim, 0.14f);

            var fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = (RectTransform)fillArea.transform;
            fillAreaRect.anchorMin = new Vector2(0f, 0.3f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.7f);
            fillAreaRect.offsetMin = new Vector2(4f, 0f);
            fillAreaRect.offsetMax = new Vector2(-4f, 0f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)fill.transform);
            fill.GetComponent<Image>().color = Ui.WithAlpha(dim, 0.7f);

            var handleArea = new GameObject("HandleArea", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = (RectTransform)handleArea.transform;
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(8f, 0f);
            handleAreaRect.offsetMax = new Vector2(-8f, 0f);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = (RectTransform)handle.transform;
            handleRect.sizeDelta = new Vector2(12f, 0f); // ширину двигает сам Slider
            handle.GetComponent<Image>().color = OnboardComputerFactory.BrightColor;

            var slider = go.GetComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = (RectTransform)fill.transform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.wholeNumbers = true;
            return slider;
        }

        // ячейки бака (StationUI.SegmentRow): ряд сегментов на всю ширину панели

        // --- Ремонт: HP-бар + одна кнопка «Починить». Правило тиров мастерских:
        // T1 берёт корабли 1–2, T2 — 1–3, T3+ — 4–5 (RepairNetwork.ShipTierAccepted);
        // чужой корабль — отказ текстом. Ремонт — полный пакет от недостающего,
        // ограничение только деньгами ---

        // тир корабля для правила тиров мастерской (FleetService.Current)
        private int ShipTier
        {
            get
            {
                var config = session != null && session.Fleet != null ? session.Fleet.Current : null;
                return config != null ? config.Tier : 2;
            }
        }

        // предложение пересчитывается при каждом Refresh (ремонт, урон, деньги)
        private void UpdateRepairWindow()
        {
            int tier = RepairTier;
            float missing = RepairNetwork.MissingHp(ship.Eff.HpMax, ship.Hp);
            float surcharge = Pricing.GetRepairTierSurcharge(tier);

            repairTierLine.text = $"Мастерская тир {tier} · наценка ×{surcharge:0.##}";

            if (missing <= 0f)
            {
                repairOffer.text = "Корпус цел — ремонт не нужен";
                repairOffer.color = OnboardComputerFactory.LabelColor;
                repairButton.interactable = false;
                repairButtonLabel.text = "КОРПУС ЦЕЛ";
                hullStatus.text = "";
                return;
            }

            // корабль вне круга тиров станции — отказ
            if (!RepairNetwork.ShipTierAccepted(tier, ShipTier))
            {
                repairOffer.text = ShipTier < RepairNetwork.MinShipTier(tier)
                    ? $"Мастерская не берётся за корабли ниже тира {RepairNetwork.MinShipTier(tier)}"
                    : $"Корабли выше тира {RepairNetwork.MaxShipTier(tier)} здесь не обслуживаются";
                repairOffer.color = OnboardComputerFactory.DangerColor;
                repairButton.interactable = false;
                repairButtonLabel.text = "НЕ БЕРЁМСЯ";
                hullStatus.text = "";
                return;
            }

            // полный пакет от текущего недостающего; ограничение — только деньги
            float amount = RepairNetwork.RepairAmount(ship.Eff.HpMax, ship.Hp,
                RepairNetwork.PackCount - 1, tier, Pricing);
            int cost = RepairNetwork.CostOf(amount, RepairNetwork.PackCount - 1, tier, Pricing);

            repairOffer.text = $"Починка: +{Mathf.RoundToInt(amount)} HP · {UiFormat.Currency} {UiFormat.FormatCredits(cost)}";
            repairOffer.color = OnboardComputerFactory.BrightColor;
            bool canPay = session.Wallet.Credits >= cost;
            repairButton.interactable = canPay;
            repairButtonLabel.text = canPay ? "ПОЧИНИТЬ" : "МАЛО КРЕДИТОВ";
        }

        // TrySpend → HullSystem.Repair; наценка тира сети — внутри RepairNetwork.CostOf
        private void ConfirmRepair()
        {
            if (!CanOpen() || session == null)
                return;
            int tier = RepairTier;
            if (!RepairNetwork.ShipTierAccepted(tier, ShipTier))
                return; // станция такие корабли не берёт — кнопка должна была быть серой
            float missing = RepairNetwork.MissingHp(ship.Eff.HpMax, ship.Hp);
            if (missing <= 0f)
                return;
            float amount = RepairNetwork.RepairAmount(ship.Eff.HpMax, ship.Hp,
                RepairNetwork.PackCount - 1, tier, Pricing);
            int cost = RepairNetwork.CostOf(amount, RepairNetwork.PackCount - 1, tier, Pricing);
            if (amount <= 0.01f || cost <= 0 || !session.Wallet.TrySpend(cost))
                return;
            hull.Repair(amount); // HpChanged дойдёт до HUD и до Refresh — экран пересчитается
            int pct = Mathf.RoundToInt(amount / missing * 100f);
            hullStatus.text = $"Отремонтировано на {pct}% (+{Mathf.RoundToInt(amount)} HP) за {UiFormat.Currency} {UiFormat.FormatCredits(cost)}";
            ShowToast($"Отремонтировано на {pct}%", tone: ToastTone.Danger); // списание — красный
            landing.ReportServiced(ship.LandedOn.DisplayName);
        }

        private void Refresh()
        {
            if (!opened || screen == null || ship == null || session == null)
                return;
            // гонка FixedUpdate vs Update: взлёт с открытым экраном —
            // закрываемся до любых разыменований LandedOn
            if (ship.LandedOn == null)
            {
                Close();
                return;
            }

            // другая планета — пересобрать состав вкладок под её услуги
            if (ship.LandedOn != currentPlanet)
            {
                currentPlanet = ship.LandedOn;
                ApplyTabs();
            }

            stationTitle.text = "СТАНЦИЯ · " + ship.LandedOn.DisplayName.ToUpper();
            creditsValue.text = UiFormat.Currency + " " + UiFormat.FormatCredits(session.Wallet.Credits);

            StationUI.SetSegments(fuelSegments, Mathf.CeilToInt(ship.FuelFraction * fuelSegments.Length));
            fuelValue.text = Mathf.RoundToInt(ship.Fuel) + "/" + Mathf.RoundToInt(ship.Eff.FuelMax);
            UpdateHpBar(ship.HpFraction);
            hullValue.text = Mathf.RoundToInt(ship.Hp) + "/" + Mathf.RoundToInt(ship.Eff.HpMax);

            UpdateFuelWindow();

            UpdateRepairWindow();

            RefreshShop();
        }

        // --- Вкладки ---

        // состав вкладок по услугам планеты; вызывается при смене планеты
        private void ApplyTabs()
        {
            SetTabVisible(StationTab.Shop, currentPlanet.IsHub);
            SetTabVisible(StationTab.Upgrades, currentPlanet.IsHub);
            SetTabVisible(StationTab.Fuel, currentPlanet.HasFuel);
            SetTabVisible(StationTab.Repair, currentPlanet.HasRepair);
            SelectTab(IsTabAvailable(activeTab) ? activeTab : DefaultTab());
        }

        private bool IsTabAvailable(StationTab tab)
        {
            switch (tab)
            {
                case StationTab.Shop:
                case StationTab.Upgrades:
                    return currentPlanet.IsHub;
                case StationTab.Fuel:
                    return currentPlanet.HasFuel;
                case StationTab.Repair:
                    return currentPlanet.HasRepair;
                default:
                    return false;
            }
        }

        // первая доступная услуга: заправка чаще всего и есть причина посадки
        private StationTab DefaultTab()
        {
            if (currentPlanet.HasFuel)
                return StationTab.Fuel;
            return currentPlanet.HasRepair ? StationTab.Repair : StationTab.Shop;
        }

        // видимость окон под активную вкладку; на обычной станции своей вкладки
        // улучшений нет — блок живёт в окне ремонта (на хабе окна ремонта не трогает)
        private void SelectTab(StationTab tab)
        {
            activeTab = tab;
            fuelWindow.SetActive(tab == StationTab.Fuel);
            hullWindow.SetActive(tab == StationTab.Repair);
            bool upgradesHere = tab == StationTab.Upgrades
                || (tab == StationTab.Repair && !currentPlanet.IsHub);
            upgradeWindow.SetActive(upgradesHere);
            showcaseSection?.SetWindowVisible(tab == StationTab.Shop);

            // подсветка активной вкладки: ярче фон и текст
            foreach (var pair in tabButtons)
            {
                bool active = pair.Key == tab;
                tabBackgrounds[pair.Key].color =
                    Ui.WithAlpha(OnboardComputerFactory.LabelColor, active ? 0.45f : 0.18f);
                tabLabels[pair.Key].color = active ? OnboardComputerFactory.BrightColor : OnboardComputerFactory.LabelColor;
            }
        }

        private void SetTabVisible(StationTab tab, bool visible)
        {
            if (tabButtons.TryGetValue(tab, out var button) && button.gameObject.activeSelf != visible)
                button.gameObject.SetActive(visible);
        }

        // кнопка вкладки в стиле сервисных кнопок, ширина равная (flexible)
        private void AddTabButton(StationTab tab, Transform parent, string title)
        {
            var go = OnboardComputerFactory.Button(parent, title,
                Ui.WithAlpha(OnboardComputerFactory.LabelColor, 0.18f),
                OnboardComputerFactory.LabelColor, 18f, "Tab_" + tab);
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minHeight = 40f;
            le.preferredHeight = 40f;
            tabButtons[tab] = go.GetComponent<Button>();
            tabBackgrounds[tab] = go.GetComponent<Image>();
            tabLabels[tab] = go.GetComponentInChildren<TMP_Text>();
            tabButtons[tab].onClick.AddListener(() => SelectTab(tab));
        }

        // каркас экрана строится кодом при первом открытии
        private GameObject BuildScreen()
        {
            var root = new GameObject("StationScreen", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            // масштаб как у канваса сцены (1920x1080) — иначе на других
            // разрешениях станция расходится с HUD
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(root.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)background.transform);
            background.GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.015f, 0.85f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            // панель 800: слева статы витрины, справа большая модель
            panelRect.sizeDelta = new Vector2(800f, 0f); // высоту подберёт fitter
            panel.GetComponent<Image>().color = OnboardComputerFactory.PanelColor;
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 18);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // шапка: название, кредиты (перечитывает Refresh), крестик закрытия
            var header = StationUI.PopupHeader(panel.transform, "СТАНЦИЯ");
            stationTitle = header.Title;
            creditsValue = header.Credits;
            header.Close.onClick.AddListener(Close);

            // все четыре вкладки; видимость под планету ставит ApplyTabs
            var tabRow = OnboardComputerFactory.H("TabRow", panel.transform);
            tabRow.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            OnboardComputerFactory.FixedHeight(tabRow, 40f);
            AddTabButton(StationTab.Shop, tabRow.transform, "МАГАЗИН");
            AddTabButton(StationTab.Upgrades, tabRow.transform, "УЛУЧШЕНИЯ");
            AddTabButton(StationTab.Fuel, tabRow.transform, "ЗАПРАВКА");
            AddTabButton(StationTab.Repair, tabRow.transform, "РЕМОНТ");

            // --- Окно заправки: ячейки бака, запас, слайдер, кнопка ---
            fuelWindow = StationUI.Section(panel.transform, "// ТОПЛИВО", "FuelWindow");

            // шапка бака: метка слева, значение справа (17pt → высота 24)
            var fuelHead = OnboardComputerFactory.H("FuelHead", fuelWindow.transform);
            OnboardComputerFactory.FixedHeight(fuelHead.gameObject, 24f);
            var fuelCaption = OnboardComputerFactory.Text(fuelHead, "БАК", 17f, OnboardComputerFactory.LabelColor, "Caption");
            OnboardComputerFactory.Flexible(fuelCaption.gameObject);
            fuelValue = OnboardComputerFactory.Text(fuelHead, "0/0", 17f, OnboardComputerFactory.BrightColor, "FuelValue");
            OnboardComputerFactory.FixedWidth(fuelValue.gameObject, 110f);
            fuelValue.alignment = TextAlignmentOptions.Right;

            fuelSegments = StationUI.SegmentRow(fuelWindow.transform, "TankCells", SegmentCount, 34f);

            fuelStockLine = OnboardComputerFactory.Text(fuelWindow, "", 17f, OnboardComputerFactory.LabelColor, "StockLine");
            OnboardComputerFactory.FixedHeight(fuelStockLine.gameObject, 23f);
            fuelOfferLabel = OnboardComputerFactory.Text(fuelWindow, "", 18f, OnboardComputerFactory.BrightColor, "OfferLabel");
            OnboardComputerFactory.FixedHeight(fuelOfferLabel.gameObject, 24f);
            fuelSlider = BuildFuelSlider(fuelWindow.transform);
            OnboardComputerFactory.FixedHeight(fuelSlider.gameObject, 30f);
            fuelSlider.onValueChanged.AddListener(_ => UpdateFuelOffer());

            // кнопка заправки: подтверждает выбор слайдера
            fuelButton = StationUI.ServiceButton(fuelWindow.transform, "FuelButton", ConfirmRefuel);
            fuelButtonLabel = fuelButton.GetComponentInChildren<TMP_Text>();

            fuelStatus = OnboardComputerFactory.Text(fuelWindow, "", 17f, OnboardComputerFactory.LabelColor, "Status");
            OnboardComputerFactory.FixedHeight(fuelStatus.gameObject, 23f);

            // --- Окно ремонта: HP-бар, цена, кнопка ---
            hullWindow = StationUI.Section(panel.transform, "// РЕМОНТ", "HullWindow");

            var hullHead = OnboardComputerFactory.H("HullHead", hullWindow.transform);
            OnboardComputerFactory.FixedHeight(hullHead.gameObject, 24f);
            var hullCaption = OnboardComputerFactory.Text(hullHead, "КОРПУС", 17f, OnboardComputerFactory.LabelColor, "Caption");
            OnboardComputerFactory.Flexible(hullCaption.gameObject);
            hullValue = OnboardComputerFactory.Text(hullHead, "0/0", 17f, OnboardComputerFactory.BrightColor, "HullValue");
            OnboardComputerFactory.FixedWidth(hullValue.gameObject, 110f);
            hullValue.alignment = TextAlignmentOptions.Right;

            hpFill = StationUI.FillBar(hullWindow.transform, 14f, OnboardComputerFactory.BrightColor);

            repairTierLine = OnboardComputerFactory.Text(hullWindow, "", 17f, OnboardComputerFactory.LabelColor, "TierLine");
            OnboardComputerFactory.FixedHeight(repairTierLine.gameObject, 23f);
            repairOffer = OnboardComputerFactory.Text(hullWindow, "", 18f, OnboardComputerFactory.BrightColor, "OfferLine");
            OnboardComputerFactory.FixedHeight(repairOffer.gameObject, 24f);

            repairButton = StationUI.ServiceButton(hullWindow.transform, "RepairButton", ConfirmRepair);
            repairButtonLabel = repairButton.GetComponentInChildren<TMP_Text>();

            hullStatus = OnboardComputerFactory.Text(hullWindow, "", 17f, OnboardComputerFactory.LabelColor, "Status");
            OnboardComputerFactory.FixedHeight(hullStatus.gameObject, 23f);

            // окно улучшений (+ блок долга); на не-хабе показывается внутри окна ремонта
            upgradeWindow = OnboardComputerFactory.V("UpgradeWindow", panel.transform, 12f);
            upgradesSection = new StationUpgradesSection(this);
            upgradesSection.Build(upgradeWindow.transform);

            // вкладка МАГАЗИН — витрина (StationShowcaseSection)
            showcaseSection = new StationShowcaseSection(this);
            showcaseSection.Build(panel.transform);

            var footer = OnboardComputerFactory.Text(panel, "B / ESC — закрыть", 16f, OnboardComputerFactory.LabelColor, "Footer");
            // 22: строка 16pt ≈ 21.1 — запас против вертикального Ellipsis
            OnboardComputerFactory.FixedHeight(footer.gameObject, 22f);

            return root;
        }

        // HP-бар: заливка = доля корпуса; ниже четверти — тревожный красный
        private void UpdateHpBar(float fraction)
        {
            if (hpFill == null)
                return;
            StationUI.SetFill(hpFill, fraction);
            var image = hpFill.GetComponent<Image>();
            if (image != null)
                image.color = fraction < 0.25f
                    ? OnboardComputerFactory.DangerColor
                    : OnboardComputerFactory.BrightColor;
        }

        // перечитывание секций магазина (улучшения/долг + витрина)
        private void RefreshShop()
        {
            upgradesSection?.Refresh();
            showcaseSection?.Refresh();
        }

        private void OnShopChanged()
        {
            if (opened)
                RefreshShop();
        }

        // DebtChanged приходит с новым балансом (Action<int>) — аргумент не нужен
        private void OnDebtChanged(int balance)
        {
            OnShopChanged();
        }

        public void ShowToast(string message, bool emphasized = false, ToastTone tone = ToastTone.Neutral)
        {
            if (toast == null)
                toast = FindAnyObjectByType<ToastNotifier>();
            if (toast != null)
                toast.Show(message, emphasized, tone);
        }
    }
}
