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

namespace SpaceTaxi.UI
{
    // Экран станции [B] (§3.3): вкладки по услугам планеты — хаб МТО даёт все
    // четыре [МАГАЗИН][УЛУЧШЕНИЯ][ЗАПРАВКА][РЕМОНТ], обычная станция — по
    // флагам (заправка/мастерская). Заправка (Н-4): слайдер объёма, цена/ед и
    // запас визита — по тиру сети планеты (PricingConfig), оплата ровно за
    // взятое. Ремонт (Н-5): пакеты мелкий/средний/полный — доля недостающего
    // HP по базовой цене 1 кр/HP × множитель пакета; тир сети ремонта планеты
    // режет максимальный пакет и потолок визита, ограничения видны на карточках
    // заранее. Магазин (Н-3а): витрина — большой спрайт модели, статы, цена,
    // листание [←][→], сортировка по цене; базовые корабли всегда в наличии,
    // вариант покраски (ShipConfig.PaintVariants) ротирует ShopStockService
    // раз в 2 игровых часа, покупка запоминает показанный вариант.
    // Улучшения (§3.4) и гашение долга (§3.2) — на хабе отдельной вкладкой, на
    // обычной станции — внутри окна ремонта. Магазин (Н3.4): витрина крупнее —
    // спрайт 220×160, статы таблицей из 6 строк с полосками сравнения с текущим
    // кораблём (серая — текущий, цветная — модель: зелёная лучше, красная хуже,
    // справа %) и бейджем «ВЫ ЗДЕСЬ»; улучшения — предпросмотр «старое → новое».
    // Доступен ТОЛЬКО стоя
    // (State == Landed) на планете с услугами; мир не ставится на паузу (как
    // карта). Закрытие по B/Esc — ESC уступается по той же договорённости, что
    // и карта (NotifyStationOpened/Closed + EscJustClosed). Контроллера в
    // сценах нет — появляется сам (стиль DismissalScreenController), панель
    // строится кодом при первом открытии.
    public class StationScreenController : MonoBehaviour
    {
        private const int SortingOrder = 100; // поверх HUD/паузы, ниже увольнения (200)
        private const int SegmentCount = 10;  // как в HUD
        // подпись счётчика аварий (§3.2: увольнение после третьей непогашенной)
        private const int MaxUnpaidAccidents = 3;

        // витрина (Н3.4): карточка крупнее, статы — таблицей с полосками сравнения
        private const float ShowcaseSpriteW = 220f;
        private const float ShowcaseSpriteH = 160f;
        private const int StatCount = 6;      // Thrust, StrafeAcc, MaxV, FuelMax, HpMax, Retro
        private const int StatBrakeIndex = 5; // тормозной путь: меньше = лучше
        // тормозной путь = K / Retro — условные «отн.» единицы: Retro 98–310 → ~200–610
        private const float BrakeDistanceK = 60000f;

        // подписи статов витрины; индексы совпадают с CompareValue
        private static readonly string[] StatLabels =
        {
            "Тяга", "Манёвр", "Макс. скорость", "Бак", "Прочность", "Тормозной путь",
        };

        // шаблоны описания прибавки ОДНОГО уровня: проценты считаются из
        // UpgradeConfig (максимум / 3, шкала линейная) — числа не дублируются
        private const string EngineEffectFormat = "+{0}% тяги";
        private const string TankEffectFormat = "+{0}% бака, −{1}% расхода";
        private const string HullEffectFormat = "+{0}% корпуса, −{1}% жара";
        private const string ManeuverEffectFormat = "+{0}% боковой тяги";

        // цвета сегментов — как в HudBinder
        private static readonly Color SegmentOn = new Color(0.235f, 1f, 0.557f, 1f);
        private static readonly Color SegmentOff = new Color(0.059f, 0.2f, 0.125f, 0.824f);

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

        // ввод: действие Station мапы "UI" (binding B), привязка строками — стиль ShipController
        private InputActionAsset input;
        private InputAction stationAction;

        private TMP_Text stationTitle;
        private TMP_Text creditsValue;
        private TMP_Text fuelValue;
        private TMP_Text hullValue;
        private TMP_Text fuelStatus;
        private TMP_Text hullStatus;
        private Image[] fuelSegments;
        private Image[] hullSegments;
        private Button fuelButton;
        private TMP_Text fuelButtonLabel;
        // заправка (Н-4): слайдер объёма с живым предрасчётом и пресеты
        private TMP_Text fuelStockLine;  // «Топлива на станции: N ед · цена M кр/ед»
        private TMP_Text fuelOfferLabel; // «Заправить: X ед (Y кр)»
        private Slider fuelSlider;
        private Button fuelPreset10;
        private Button fuelPreset50;
        private Button fuelPresetAll;
        // ремонт (Н-5): три карточки пакетов вместо одной кнопки
        private TMP_Text repairTierLine; // «Мастерская: тир N — за визит не более X% починки»
        private RepairPackRefs[] repairPacks;
        // бюджет «за визит не более X%»: кап × недостающее на момент посадки;
        // покупки минусуют его, иначе повторные мелкие пакеты собирают 100%
        private Planet repairBudgetPlanet;
        private float repairBudgetRemaining;

        // окна-вкладки: видимость переключает SelectTab, состав — ApplyTabs
        private GameObject fuelWindow;
        private GameObject hullWindow;
        private GameObject upgradeWindow; // улучшения + гашение долга (§3.4/§3.2)
        private GameObject shopWindow;    // витрина магазина — только вкладка МАГАЗИН

        // вкладки услуг (§3.3): полный набор только у хаба МТО
        private enum StationTab { Shop, Upgrades, Fuel, Repair }

        private readonly Dictionary<StationTab, Button> tabButtons = new Dictionary<StationTab, Button>();
        private readonly Dictionary<StationTab, Image> tabBackgrounds = new Dictionary<StationTab, Image>();
        private readonly Dictionary<StationTab, TMP_Text> tabLabels = new Dictionary<StationTab, TMP_Text>();
        private StationTab activeTab = StationTab.Fuel;
        private Planet currentPlanet; // планета, под которую собраны вкладки

        // блоки магазина: улучшения/долг (П3.3), ангар (П3.2)
        private GameObject upgradesOwned;   // 4 строки линий (свой корабль)
        private TMP_Text upgradesRental;    // строка-пояснение (аренда всегда сток)
        private UpgradeRowRefs[] upgradeRows;
        private GameObject debtSection;     // виден только при долге > 0
        private TMP_Text debtLine;
        private Button repayButton;
        private TMP_Text repayLabel;
        // витрина (Н-3а): каталог из Resources сортируется по цене один раз,
        // листается стрелками; показанный вариант покраски ротирует
        // ShopStockService (раз в 2 игровых часа) — витрина меняется сама
        private ShipConfig[] shipCatalog;
        private int showcaseIndex;
        private int stockSlot;           // слот ротации на последней отрисовке
        private ShipConfig showcaseShip; // модель на витрине сейчас
        private Image showcaseSprite;
        private TMP_Text showcaseName;
        private TMP_Text showcasePaint;
        // Н3.4: таблица 6 статов с полосками сравнения с текущим кораблём
        private ShowStatRowRefs[] showcaseStatRows;
        private GameObject showcaseBadge; // «ВЫ ЗДЕСЬ» — витрина показывает текущую модель
        private TMP_Text showcaseStatus;
        private Button shopBuy;
        private TMP_Text shopBuyLabel;
        private TMP_Text shopStatus; // строка обратной связи покупки
        private ToastNotifier toast;

        public bool IsOpened => opened;

        public bool EscJustClosed { get; private set; }

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
                // экран завершения заказа модален (sortingOrder выше) — B ему не подчиняется
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

            // ротация витрины (Н-3а): слот сменился — витрина перерисовывается
            // сама («за витриной интересно наблюдать»); проверка дешёвая — пара int
            if (opened && shopWindow != null && shopWindow.activeSelf)
            {
                int slot = ShopStockService.CurrentSlot(Time.time);
                if (slot != stockSlot)
                    RefreshShowcase(); // внутри обновит stockSlot
            }
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
            // П3.3: блоки магазина перечитываются сами (сессия одна на сцену,
            // отписок нет — стиль биндеров)
            session.Fleet.UpgradesChanged += OnShopChanged;
            session.Fleet.ShipChanged += OnShopChanged; // сменил корабль — блок перестроился
            session.Debt.DebtChanged += OnDebtChanged; // Action<int>: аргумент не нужен, блок перечитает сам
            bound = true;
        }

        // привязка из OnEnable и лениво из Update — кто раньше (как у ShipController)
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

        private bool CanOpen()
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

        // --- Заправка (Н-4): слайдер объёма, цена/ед и запас — по тиру сети планеты ---

        // конфиг цен: из OrderService (ассет сцены / Resources) или кодовый фолбэк
        private PricingConfig Pricing =>
            session != null && session.Orders != null ? session.Orders.Pricing : PricingConfig.Default;

        private int FuelTier => currentPlanet != null ? currentPlanet.FuelTier : PricingConfig.BaseFuelTier;

        private int RepairTier => currentPlanet != null ? currentPlanet.RepairTier : PricingConfig.BaseRepairTier;

        private int FuelAvailable()
        {
            if (ship == null || currentPlanet == null)
                return 0;
            return FuelNetwork.AvailableUnits(ship.Eff.FuelMax, ship.Fuel, FuelTier, Pricing);
        }

        // подтверждение: оплата ровно за выбранное (Wallet.TrySpend → Ship.Fuel → ReportServiced)
        private void ConfirmRefuel()
        {
            if (!CanOpen() || session == null || fuelSlider == null)
                return;
            int units = Mathf.RoundToInt(fuelSlider.value);
            int cost = FuelNetwork.CostOf(units, FuelTier, Pricing);
            if (units <= 0 || cost <= 0 || !session.Wallet.TrySpend(cost))
                return;
            ship.Fuel += units; // сеттер клампит и шлёт FuelChanged — HUD и экран обновятся
            fuelStatus.text = $"Заправлено {units} ед за {cost} кр";
            landing.ReportServiced(ship.LandedOn.DisplayName);
        }

        private void NudgeFuel(int delta) => SetFuelSelection(Mathf.RoundToInt(fuelSlider.value) + delta);

        // сдвиг выбора слайдера; onValueChanged сам обновит подписи
        private void SetFuelSelection(int units)
        {
            if (fuelSlider != null)
                fuelSlider.value = Mathf.Clamp(units, 0, FuelAvailable());
        }

        // запас сети и диапазон слайдера — при каждом Refresh: посадка, заправка
        // и улучшение бака меняют Eff.FuelMax/топливо
        private void UpdateFuelWindow()
        {
            int available = FuelAvailable();
            float perUnit = Pricing.GetFuelPricePerUnit(FuelTier);

            // 0 доступно: бак полон — заправка не нужна; иначе бак уже глубже запаса сети
            fuelStockLine.text = available > 0
                ? $"Топлива на станции: {available} ед · цена {perUnit:0.##} кр/ед"
                : ship.Fuel >= ship.Eff.FuelMax - 0.5f
                    ? "Бак полон — заправка не нужна"
                    : "Запас сети ниже текущего топлива";

            fuelSlider.minValue = 0f;
            fuelSlider.maxValue = Mathf.Max(1, available); // 0..0 слайдер не показывает
            fuelSlider.interactable = available > 0;
            fuelSlider.SetValueWithoutNotify(Mathf.Clamp(fuelSlider.value, 0f, available));

            bool hasStock = available > 0;
            fuelPreset10.interactable = hasStock;
            fuelPreset50.interactable = hasStock;
            fuelPresetAll.interactable = hasStock;

            UpdateFuelOffer();
        }

        // живой предрасчёт: подпись объёма и доступность подтверждения
        private void UpdateFuelOffer()
        {
            int available = FuelAvailable();
            int selected = Mathf.Clamp(Mathf.RoundToInt(fuelSlider.value), 0, available);
            int cost = FuelNetwork.CostOf(selected, FuelTier, Pricing);

            fuelOfferLabel.text = $"Заправить: {selected} ед ({cost} кр)";
            fuelButton.interactable = selected > 0 && session.Wallet.Credits >= cost;
            fuelButtonLabel.text = selected > 0
                ? $"ЗАПРАВИТЬ ({cost} кр)"
                : available > 0 ? "ВЫБЕРИТЕ ОБЪЁМ" : "ТОПЛИВА НЕТ";
        }

        // слайдер объёма: дорожка/заполнение/ручка собираются кодом (схема UGUI Slider)
        private static Slider BuildFuelSlider(Transform parent)
        {
            var go = new GameObject("FuelSlider", typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            var dim = OnboardComputerFactory.LabelColor;

            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(go.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)track.transform);
            track.GetComponent<Image>().color = new Color(dim.r, dim.g, dim.b, 0.14f);

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
            fill.GetComponent<Image>().color = new Color(dim.r, dim.g, dim.b, 0.7f);

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

        // пресеты объёма: сдвиг выбора с клампом в доступный запас
        private void BuildFuelPresets(Transform parent)
        {
            var row = OnboardComputerFactory.H("FuelPresets", parent);
            OnboardComputerFactory.FixedHeight(row.gameObject, 34f);
            fuelPreset10 = BuildPresetButton(row.transform, "+10 ед", () => NudgeFuel(10));
            fuelPreset50 = BuildPresetButton(row.transform, "+50 ед", () => NudgeFuel(50));
            fuelPresetAll = BuildPresetButton(row.transform, "ПОЛНЫЙ ДОСТУПНЫЙ", () => SetFuelSelection(FuelAvailable()));
        }

        private static Button BuildPresetButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = OnboardComputerFactory.Button(parent, label,
                new Color(OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g,
                    OnboardComputerFactory.LabelColor.b, 0.18f),
                OnboardComputerFactory.LabelColor, 16f);
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minHeight = 34f;
            le.preferredHeight = 34f;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }

        // --- Ремонт (Н-5): пакеты мелкий/средний/полный по тиру сети планеты ---

        private sealed class RepairPackRefs
        {
            public TMP_Text Info;    // «+25% · +18 HP» — эффективная доля пакета
            public TMP_Text Price;   // «15 кр»
            public Button Buy;
            public TMP_Text BuyLabel;
            public TMP_Text Note;    // причина блокировки (тир станции, деньги)
        }

        // ряд из трёх карточек: состав и цены пакетов — PricingConfig
        private void BuildRepairPacks(Transform parent)
        {
            var row = new GameObject("PackRow", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true; // карточки одной высоты — по самой высокой

            string[] names = { "МЕЛКИЙ", "СРЕДНИЙ", "ПОЛНЫЙ" };
            repairPacks = new RepairPackRefs[RepairNetwork.PackCount];
            for (int i = 0; i < RepairNetwork.PackCount; i++)
            {
                int packIndex = i; // замыкание: иначе все кнопки поймают PackCount
                repairPacks[i] = BuildRepairCard(row.transform, packIndex, names[i]);
            }
        }

        private RepairPackRefs BuildRepairCard(Transform parent, int packIndex, string title)
        {
            var card = new GameObject("Pack_" + (packIndex + 1), typeof(RectTransform), typeof(Image));
            card.transform.SetParent(parent, false);
            card.GetComponent<Image>().color = new Color(
                OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g,
                OnboardComputerFactory.LabelColor.b, 0.14f);
            var cardLayout = card.AddComponent<VerticalLayoutGroup>();
            cardLayout.padding = new RectOffset(8, 8, 8, 8);
            cardLayout.spacing = 3f;
            cardLayout.childControlWidth = true;
            cardLayout.childControlHeight = true;
            cardLayout.childForceExpandWidth = true;
            cardLayout.childForceExpandHeight = false;
            var cardElement = card.AddComponent<LayoutElement>();
            cardElement.flexibleWidth = 1f;
            cardElement.minWidth = 150f;

            var refs = new RepairPackRefs();
            var heading = OnboardComputerFactory.Text(card, title, 16f, OnboardComputerFactory.LabelColor, "Title");
            OnboardComputerFactory.FixedHeight(heading.gameObject, 20f);
            refs.Info = OnboardComputerFactory.Text(card, "—", 18f, OnboardComputerFactory.BrightColor, "Info");
            OnboardComputerFactory.FixedHeight(refs.Info.gameObject, 24f);
            refs.Price = OnboardComputerFactory.Text(card, "—", 18f, OnboardComputerFactory.TaxiColor, "Price");
            OnboardComputerFactory.FixedHeight(refs.Price.gameObject, 24f);

            var buttonGo = OnboardComputerFactory.Button(card.transform, "—",
                new Color(OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g,
                    OnboardComputerFactory.LabelColor.b, 0.22f),
                OnboardComputerFactory.BrightColor, 16f, "BuyButton");
            var le = buttonGo.AddComponent<LayoutElement>();
            le.minHeight = 34f;
            le.preferredHeight = 34f;
            refs.Buy = buttonGo.GetComponent<Button>();
            refs.Buy.onClick.AddListener(() => ConfirmRepair(packIndex));
            refs.BuyLabel = buttonGo.GetComponentInChildren<TMP_Text>();

            refs.Note = OnboardComputerFactory.Text(card, "", 14f, OnboardComputerFactory.LabelColor, "Note");
            OnboardComputerFactory.FixedHeight(refs.Note.gameObject, 18f);
            return refs;
        }

        // бюджет визита считается от недостающего HP на момент посадки и
        // уменьшается покупками — «за визит не более X%» выполняется ровно
        private void InitRepairBudget()
        {
            repairBudgetPlanet = currentPlanet;
            float cap = Pricing.GetRepairTierCap(RepairTier);
            repairBudgetRemaining = cap * RepairNetwork.MissingHp(ship.Eff.HpMax, ship.Hp);
        }

        // потолок станции и карточки — при каждом Refresh: ремонт, урон, деньги
        private void UpdateRepairWindow()
        {
            int tier = RepairTier;
            float missing = RepairNetwork.MissingHp(ship.Eff.HpMax, ship.Hp);
            float cap = Pricing.GetRepairTierCap(tier);

            repairTierLine.text = missing <= 0f
                ? "Корпус цел — ремонт не нужен"
                : $"Мастерская: тир {tier} — за визит не более {Mathf.RoundToInt(cap * 100f)}% починки";

            int maxPack = RepairNetwork.MaxPackIndex(tier);
            int credits = session.Wallet.Credits;
            for (int i = 0; i < repairPacks.Length; i++)
            {
                var card = repairPacks[i];
                if (missing <= 0f)
                {
                    card.Info.text = "—";
                    card.Price.text = "—";
                    card.Buy.interactable = false;
                    card.BuyLabel.text = "КОРПУС ЦЕЛ";
                    card.Note.text = "";
                    continue;
                }

                bool tierOk = i <= maxPack;
                float amount;
                if (tierOk)
                {
                    // пакет от текущего недостающего, зажатый остатком бюджета визита
                    amount = RepairNetwork.RepairAmount(ship.Eff.HpMax, ship.Hp, i, tier,
                        Pricing, repairBudgetRemaining);
                }
                else
                {
                    // пакет вне тира не предлагается — на карточке его номинал,
                    // чтобы было видно, что именно станция отказывается делать
                    amount = missing * Pricing.GetRepairPackFraction(i);
                }
                float fraction = amount / missing;
                int cost = RepairNetwork.CostOf(amount, i, Pricing);

                card.Info.text = $"+{Mathf.RoundToInt(fraction * 100f)}% · +{Mathf.RoundToInt(amount)} HP";
                card.Price.text = cost + " кр";

                if (!tierOk)
                {
                    card.Buy.interactable = false;
                    card.BuyLabel.text = "НЕ ВОЗЬМЁМСЯ";
                    card.Note.text = "не тот уровень станции";
                    card.Note.color = OnboardComputerFactory.DangerColor;
                }
                else if (amount <= 0.01f)
                {
                    card.Buy.interactable = false;
                    card.BuyLabel.text = "ЛИМИТ ВИЗИТА";
                    card.Note.text = "потолок станции исчерпан";
                    card.Note.color = OnboardComputerFactory.LabelColor;
                }
                else if (credits < cost)
                {
                    card.Buy.interactable = false;
                    card.BuyLabel.text = "МАЛО КРЕДИТОВ";
                    card.Note.text = "нужно " + cost + " кр";
                    card.Note.color = OnboardComputerFactory.LabelColor;
                }
                else
                {
                    card.Buy.interactable = true;
                    card.BuyLabel.text = "ПОЧИНИТЬ";
                    card.Note.text = "";
                }
            }
        }

        // подтверждение пакета: существующий платёжный путь — TrySpend → HullSystem.Repair
        private void ConfirmRepair(int packIndex)
        {
            if (!CanOpen() || session == null)
                return;
            if (packIndex > RepairNetwork.MaxPackIndex(RepairTier))
                return; // станция такой пакет не даёт — кнопка должна была быть серой
            float missing = RepairNetwork.MissingHp(ship.Eff.HpMax, ship.Hp);
            if (missing <= 0f)
                return;
            float amount = RepairNetwork.RepairAmount(ship.Eff.HpMax, ship.Hp, packIndex, RepairTier,
                Pricing, repairBudgetRemaining);
            int cost = RepairNetwork.CostOf(amount, packIndex, Pricing);
            if (amount <= 0.01f || cost <= 0 || !session.Wallet.TrySpend(cost))
                return;
            hull.Repair(amount); // HpChanged дойдёт до HUD и до Refresh — карточки пересчитаются
            repairBudgetRemaining = Mathf.Max(0f, repairBudgetRemaining - amount);
            int pct = Mathf.RoundToInt(amount / missing * 100f);
            hullStatus.text = $"Отремонтировано на {pct}% (+{Mathf.RoundToInt(amount)} HP) за {cost} кр";
            ShowToast($"Отремонтировано на {pct}%");
            landing.ReportServiced(ship.LandedOn.DisplayName);
        }

        private void Refresh()
        {
            if (!opened || screen == null || ship == null || session == null)
                return;
            // Взлёт с открытым экраном (гонка FixedUpdate vs Update):
            // LandedOn уже null — закрываемся до любых разыменований
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
                InitRepairBudget();
            }
            else if (repairBudgetPlanet != currentPlanet)
            {
                // экран пересоздан в рамках того же визита — бюджет тот же
                InitRepairBudget();
            }

            stationTitle.text = "СТАНЦИЯ · " + ship.LandedOn.DisplayName.ToUpper();
            creditsValue.text = session.Wallet.Credits + " кр";

            UpdateGauge(fuelSegments, ship.FuelFraction);
            fuelValue.text = Mathf.RoundToInt(ship.Fuel) + "/" + Mathf.RoundToInt(ship.Eff.FuelMax);
            UpdateGauge(hullSegments, ship.HpFraction);
            hullValue.text = Mathf.RoundToInt(ship.Hp) + "/" + Mathf.RoundToInt(ship.Eff.HpMax);

            UpdateFuelWindow();

            UpdateRepairWindow();

            RefreshShop();
        }

        private void UpdateGauge(Image[] segments, float fraction)
        {
            int lit = Mathf.CeilToInt(fraction * segments.Length);
            for (int i = 0; i < segments.Length; i++)
                segments[i].color = i < lit ? SegmentOn : SegmentOff;
        }

        // --- Вкладки услуг (§3.3) ---

        // состав вкладок по услугам планеты: хаб — все четыре, обычная станция —
        // по флагам; вызывается при смене планеты (из Refresh)
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
            if (shopWindow != null)
                shopWindow.SetActive(tab == StationTab.Shop);

            // подсветка активной вкладки: ярче фон и текст
            foreach (var pair in tabButtons)
            {
                bool active = pair.Key == tab;
                var dim = OnboardComputerFactory.LabelColor;
                tabBackgrounds[pair.Key].color = new Color(dim.r, dim.g, dim.b, active ? 0.45f : 0.18f);
                tabLabels[pair.Key].color = active ? OnboardComputerFactory.BrightColor : OnboardComputerFactory.LabelColor;
            }
        }

        private void SetTabVisible(StationTab tab, bool visible)
        {
            if (tabButtons.TryGetValue(tab, out var button) && button.gameObject.activeSelf != visible)
                button.gameObject.SetActive(visible);
        }

        // кнопка вкладки в стиле BuildServiceButton, ширина равная (flexible)
        private void AddTabButton(StationTab tab, Transform parent, string title)
        {
            var go = OnboardComputerFactory.Button(parent, title,
                new Color(OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g,
                    OnboardComputerFactory.LabelColor.b, 0.18f),
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

        // каркас экрана строится кодом при первом открытии (стиль OnboardComputerFactory)
        private GameObject BuildScreen()
        {
            var root = new GameObject("StationScreen", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            // Масштаб как у сценического канваса (1920x1080, match 0.5) — иначе
            // на не-1080p разрешениях станция визуально расходится с HUD
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
            // Н3.4: панель шире (560 → 640) — витрине нужна таблица статов с полосками
            panelRect.sizeDelta = new Vector2(640f, 0f); // высоту подберёт fitter
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

            // --- Шапка: точка + заголовок + кредиты ---
            var titleRow = OnboardComputerFactory.H("TitleRow", panel.transform);
            var dot = OnboardComputerFactory.Text(titleRow, "<voffset=0.35em>●</voffset>", 18f, OnboardComputerFactory.LabelColor, "LinkDot");
            OnboardComputerFactory.FixedWidth(dot.gameObject, 24f);
            stationTitle = OnboardComputerFactory.Text(titleRow, "СТАНЦИЯ", 26f, OnboardComputerFactory.BrightColor, "Title");
            OnboardComputerFactory.Flexible(stationTitle.gameObject);
            creditsValue = OnboardComputerFactory.Text(titleRow, "0 кр", 24f, OnboardComputerFactory.TaxiColor, "Credits");
            OnboardComputerFactory.FixedWidth(creditsValue.gameObject, 120f);

            // --- Вкладки услуг (§3.3): кнопки создаём все четыре, видимость
            // под флаги планеты ставит ApplyTabs при каждом открытии ---
            var tabRow = new GameObject("TabRow", typeof(RectTransform));
            tabRow.transform.SetParent(panel.transform, false);
            var tabLayout = tabRow.AddComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = 8f;
            tabLayout.childControlWidth = true;
            tabLayout.childControlHeight = true;
            tabLayout.childForceExpandWidth = true;
            tabLayout.childForceExpandHeight = false;
            var tabElement = tabRow.AddComponent<LayoutElement>();
            tabElement.minHeight = 40f;
            tabElement.preferredHeight = 40f;
            AddTabButton(StationTab.Shop, tabRow.transform, "МАГАЗИН");
            AddTabButton(StationTab.Upgrades, tabRow.transform, "УЛУЧШЕНИЯ");
            AddTabButton(StationTab.Fuel, tabRow.transform, "ЗАПРАВКА");
            AddTabButton(StationTab.Repair, tabRow.transform, "РЕМОНТ");

            // --- Окно заправки (вкладка ЗАПРАВКА): слайдер объёма (Н-4) ---
            fuelWindow = Section(panel.transform, "// ЗАПРАВКА", "FuelWindow");
            var fuelGauge = BuildGauge(fuelWindow, "ТОПЛИВО", "Fuel");
            fuelSegments = fuelGauge.Segments;
            fuelValue = fuelGauge.Value;
            fuelStockLine = OnboardComputerFactory.Text(fuelWindow, "", 17f, OnboardComputerFactory.LabelColor, "StockLine");
            OnboardComputerFactory.FixedHeight(fuelStockLine.gameObject, 22f);
            fuelOfferLabel = OnboardComputerFactory.Text(fuelWindow, "", 18f, OnboardComputerFactory.BrightColor, "OfferLabel");
            OnboardComputerFactory.FixedHeight(fuelOfferLabel.gameObject, 24f);
            fuelSlider = BuildFuelSlider(fuelWindow.transform);
            OnboardComputerFactory.FixedHeight(fuelSlider.gameObject, 30f);
            fuelSlider.onValueChanged.AddListener(_ => UpdateFuelOffer());
            BuildFuelPresets(fuelWindow.transform);
            fuelButton = BuildServiceButton(fuelWindow.transform, "FuelButton", ConfirmRefuel, out fuelButtonLabel);
            fuelStatus = OnboardComputerFactory.Text(fuelWindow, "", 17f, OnboardComputerFactory.LabelColor, "Status");
            OnboardComputerFactory.FixedHeight(fuelStatus.gameObject, 22f);

            // --- Окно ремонта (вкладка РЕМОНТ): пакеты (Н-5) ---
            hullWindow = Section(panel.transform, "// РЕМОНТ", "HullWindow");
            var hullGauge = BuildGauge(hullWindow, "КОРПУС", "Hull");
            hullSegments = hullGauge.Segments;
            hullValue = hullGauge.Value;
            repairTierLine = OnboardComputerFactory.Text(hullWindow, "", 17f, OnboardComputerFactory.LabelColor, "TierLine");
            OnboardComputerFactory.FixedHeight(repairTierLine.gameObject, 22f);
            BuildRepairPacks(hullWindow.transform);
            hullStatus = OnboardComputerFactory.Text(hullWindow, "", 17f, OnboardComputerFactory.LabelColor, "Status");
            OnboardComputerFactory.FixedHeight(hullStatus.gameObject, 22f);

            // --- Окно улучшений: вкладка УЛУЧШЕНИЯ (хаб) или контент окна
            // РЕМОНТА (обычная станция); гашение долга живёт здесь же (§3.2) ---
            upgradeWindow = new GameObject("UpgradeWindow", typeof(RectTransform));
            upgradeWindow.transform.SetParent(panel.transform, false);
            var upgradeLayout = upgradeWindow.AddComponent<VerticalLayoutGroup>();
            upgradeLayout.spacing = 12f;
            upgradeLayout.childControlWidth = true;
            upgradeLayout.childControlHeight = true;
            upgradeLayout.childForceExpandWidth = true;
            upgradeLayout.childForceExpandHeight = false;
            BuildUpgradesBlock(upgradeWindow.transform);
            BuildDebtBlock(upgradeWindow.transform);

            // --- Вкладка МАГАЗИН: витрина (Н-3а) — большая модель на экране,
            // листание стрелками, покупка показанного варианта покраски ---
            BuildShowcaseBlock(panel.transform);

            var footer = OnboardComputerFactory.Text(panel, "B / ESC — закрыть", 16f, OnboardComputerFactory.LabelColor, "Footer");
            OnboardComputerFactory.FixedHeight(footer.gameObject, 20f);

            return root;
        }

        // секция в стиле фабрики борткомпа: заголовок + линия
        private static GameObject Section(Transform parent, string title, string name)
        {
            var section = new GameObject(name, typeof(RectTransform));
            section.transform.SetParent(parent, false);
            var layout = section.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var heading = OnboardComputerFactory.Text(section, title, 20f, OnboardComputerFactory.LabelColor, "Heading");
            OnboardComputerFactory.FixedHeight(heading.gameObject, 26f);
            var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(section.transform, false);
            line.GetComponent<Image>().color = new Color(
                OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g, OnboardComputerFactory.LabelColor.b, 0.4f);
            OnboardComputerFactory.FixedHeight(line, 2f);
            return section;
        }

        private sealed class GaugeRefs
        {
            public Image[] Segments = new Image[SegmentCount];
            public TMP_Text Value;
        }

        // строка «метка | сегменты | числа» — как в HUD
        private static GaugeRefs BuildGauge(GameObject parent, string label, string name)
        {
            var row = OnboardComputerFactory.H(name + "Row", parent.transform);
            var caption = OnboardComputerFactory.Text(row, label, 18f, OnboardComputerFactory.LabelColor, "Caption");
            OnboardComputerFactory.FixedWidth(caption.gameObject, 96f);

            var track = new GameObject(name + "Track", typeof(RectTransform));
            track.transform.SetParent(row.transform, false);
            var trackLayout = track.AddComponent<HorizontalLayoutGroup>();
            trackLayout.spacing = 3f;
            trackLayout.childControlWidth = true;
            trackLayout.childControlHeight = true;
            trackLayout.childForceExpandWidth = true;
            trackLayout.childForceExpandHeight = true;
            OnboardComputerFactory.Flexible(track);

            var gauge = new GaugeRefs();
            for (int i = 0; i < SegmentCount; i++)
            {
                var seg = new GameObject(name + "Seg_" + (i + 1).ToString("00"), typeof(RectTransform), typeof(Image));
                seg.transform.SetParent(track.transform, false);
                seg.AddComponent<LayoutElement>().flexibleWidth = 1f;
                gauge.Segments[i] = seg.GetComponent<Image>();
                gauge.Segments[i].color = SegmentOff;
            }

            gauge.Value = OnboardComputerFactory.Text(row, "0/0", 20f, OnboardComputerFactory.BrightColor, name + "Value");
            OnboardComputerFactory.FixedWidth(gauge.Value.gameObject, 100f);
            return gauge;
        }

        private static Button BuildServiceButton(Transform parent, string name, UnityEngine.Events.UnityAction onClick, out TMP_Text label)
        {
            var go = OnboardComputerFactory.Button(parent, "—",
                new Color(OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g,
                    OnboardComputerFactory.LabelColor.b, 0.22f),
                OnboardComputerFactory.BrightColor, 20f, name);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 44f;
            le.preferredHeight = 44f;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            label = go.GetComponentInChildren<TMP_Text>();
            return button;
        }

        // --- П3.3: блок улучшений и досрочного гашения долга ---

        private sealed class UpgradeRowRefs
        {
            public TMP_Text Level;
            public TMP_Text Effect;
            // Н3.4: предпросмотр «Двигатель 140 → 161 (+15%)» + полоска-прирост
            public TMP_Text Preview;
            public CompareBarRefs PreviewBar;
            public Button Buy;
            public TMP_Text BuyLabel;
        }

        // полоска сравнения (Н3.4): дорожка + два fill-дочки; ширины двигает
        // SetFill по долям значений (цветной fill перекрывает серый)
        private sealed class CompareBarRefs
        {
            public RectTransform CurrentFill; // серая — текущий корабль / старое значение
            public RectTransform ValueFill;   // цветная — модель / новый уровень
            public Image ValueImage;
        }

        // строка таблицы статов витрины: подпись | полоска | процент отличия
        private sealed class ShowStatRowRefs
        {
            public TMP_Text Label;
            public TMP_Text Percent;
            public CompareBarRefs Bar;
        }

        private void RefreshShop()
        {
            RefreshUpgrades();
            RefreshDebt();
            RefreshShowcase();
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

        // блок «Улучшения» — свой метод-строитель (как долг ниже и ангар П3.2)
        private void BuildUpgradesBlock(Transform shop)
        {
            var section = Section(shop, "// УЛУЧШЕНИЯ", "UpgradesSection");

            // аренда: строка-пояснение без кнопок
            upgradesRental = OnboardComputerFactory.Text(section,
                "Аренда всегда сток — улучшения только на свой корабль", 17f,
                OnboardComputerFactory.LabelColor, "RentalNote");
            OnboardComputerFactory.FixedHeight(upgradesRental.gameObject, 24f);

            // свой корабль: по строке на линию улучшений
            upgradesOwned = new GameObject("OwnedRows", typeof(RectTransform));
            upgradesOwned.transform.SetParent(section.transform, false);
            var rows = upgradesOwned.AddComponent<VerticalLayoutGroup>();
            rows.spacing = 6f;
            rows.childControlWidth = true;
            rows.childControlHeight = true;
            rows.childForceExpandWidth = true;
            rows.childForceExpandHeight = false;

            var upgrades = session.Fleet.Upgrades; // GameSession в Awake ставит ассет или фолбэк
            string[] names =
            {
                upgrades.EngineName, upgrades.TankName, upgrades.HullName, upgrades.ManeuverName,
            };
            upgradeRows = new UpgradeRowRefs[ShipStats.LineCount];
            for (int i = 0; i < ShipStats.LineCount; i++)
            {
                var line = (UpgradeLine)i;
                var rowGo = new GameObject("Row_" + (i + 1), typeof(RectTransform));
                rowGo.transform.SetParent(upgradesOwned.transform, false);
                var rowLayout = rowGo.AddComponent<VerticalLayoutGroup>();
                rowLayout.spacing = 2f;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = true;
                rowLayout.childForceExpandHeight = false;

                var top = OnboardComputerFactory.H("Top", rowGo.transform);
                var caption = OnboardComputerFactory.Text(top, names[i], 17f, OnboardComputerFactory.BrightColor, "Caption");
                OnboardComputerFactory.FixedWidth(caption.gameObject, 120f);
                var refs = new UpgradeRowRefs();
                refs.Level = OnboardComputerFactory.Text(top, "ур. 0/3", 17f, OnboardComputerFactory.LabelColor, "Level");
                OnboardComputerFactory.FixedWidth(refs.Level.gameObject, 76f);
                var spacer = OnboardComputerFactory.Text(top, "", 15f, OnboardComputerFactory.LabelColor, "Spacer");
                OnboardComputerFactory.Flexible(spacer.gameObject);
                refs.Buy = BuildRowButton(top.transform, () => BuyUpgrade(line), out refs.BuyLabel);

                refs.Effect = OnboardComputerFactory.Text(rowGo, "", 15f, OnboardComputerFactory.LabelColor, "Effect");
                OnboardComputerFactory.FixedHeight(refs.Effect.gameObject, 20f);
                // предпросмотр эффекта до покупки (Н3.4): числа даст ShipStats.Compute
                refs.Preview = OnboardComputerFactory.Text(rowGo, "", 15f, OnboardComputerFactory.BrightColor, "Preview");
                OnboardComputerFactory.FixedHeight(refs.Preview.gameObject, 20f);
                refs.PreviewBar = BuildCompareBar(rowGo.transform, 8f);
                upgradeRows[i] = refs;
            }

            RefreshUpgrades();
        }

        // компактная кнопка строки улучшения (стиль BuildServiceButton)
        private static Button BuildRowButton(Transform parent, UnityEngine.Events.UnityAction onClick, out TMP_Text label)
        {
            var go = OnboardComputerFactory.Button(parent, "—",
                new Color(OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g,
                    OnboardComputerFactory.LabelColor.b, 0.22f),
                OnboardComputerFactory.BrightColor, 15f, "BuyButton");
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 150f;
            le.preferredWidth = 150f;
            le.minHeight = 32f;
            le.preferredHeight = 32f;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            label = go.GetComponentInChildren<TMP_Text>();
            return button;
        }

        // уровни/цены — из конфига; аренда всегда сток, ур.3 — без кнопки
        private void RefreshUpgrades()
        {
            if (upgradeRows == null || session == null || session.Fleet == null)
                return;
            var fleet = session.Fleet;
            bool owned = fleet.IsOwned(fleet.Current);
            upgradesRental.gameObject.SetActive(!owned);
            upgradesOwned.SetActive(owned);
            if (!owned)
                return;
            var upgrades = fleet.Upgrades;
            if (upgrades == null)
                return;
            int[] prices = upgrades.PricesPerLevel;
            for (int i = 0; i < upgradeRows.Length; i++)
            {
                var row = upgradeRows[i];
                int level = fleet.GetLevel(fleet.Current, (UpgradeLine)i);
                bool maxed = level >= ShipStats.MaxLevel;
                row.Level.text = $"ур. {level}/{ShipStats.MaxLevel}";
                row.Effect.text = maxed ? "макс. уровень" : NextLevelEffect(upgrades, (UpgradeLine)i);
                UpdateUpgradePreview(row, upgrades, (UpgradeLine)i, level, maxed);
                // цена следующего уровня — PricesPerLevel[текущий уровень], как в TryUpgrade
                int price = prices != null && level < prices.Length ? prices[level] : 0;
                row.BuyLabel.text = maxed ? "МАКС." : $"КУПИТЬ ({price} КР)";
                row.Buy.interactable = !maxed && price > 0 && session.Wallet.Credits >= price;
            }
        }

        // предпросмотр «старое → новое» (Н3.4): ShipStats.Compute с текущими
        // уровнями и со следующим по линии. Прирост чистого дохода из конфига
        // честно не выводится (цены заказов зависят от тира/маршрута, не от
        // улучшений) — окупаемость не выдумываем, показываем только прирост %
        private void UpdateUpgradePreview(UpgradeRowRefs row, UpgradeConfig upgrades,
            UpgradeLine line, int level, bool maxed)
        {
            if (row.Preview == null)
                return;
            var fleet = session.Fleet;
            var config = fleet.Current;
            var physics = session.Config;
            int[] levels = new int[ShipStats.LineCount];
            for (int i = 0; i < ShipStats.LineCount; i++)
                levels[i] = fleet.GetLevel(config, (UpgradeLine)i);
            var before = ShipStats.Compute(physics, config, levels, upgrades);
            levels[(int)line] = Mathf.Min(level + 1, ShipStats.MaxLevel);
            var after = ShipStats.Compute(physics, config, levels, upgrades);

            string label;
            float oldV, newV;
            switch (line)
            {
                case UpgradeLine.Engine:
                    label = upgrades.EngineName;
                    oldV = before.Thrust;
                    newV = after.Thrust;
                    break;
                case UpgradeLine.Tank:
                    label = upgrades.TankName;
                    oldV = before.FuelMax;
                    newV = after.FuelMax;
                    break;
                case UpgradeLine.Hull:
                    label = upgrades.HullName;
                    oldV = before.HpMax;
                    newV = after.HpMax;
                    break;
                default:
                    label = upgrades.ManeuverName;
                    oldV = before.Strafe;
                    newV = after.Strafe;
                    break;
            }

            if (maxed)
            {
                row.Preview.text = $"{label} {Mathf.RoundToInt(newV)} — макс.";
                SetFill(row.PreviewBar.CurrentFill, 1f);
                SetFill(row.PreviewBar.ValueFill, 1f);
                return;
            }
            float pct = oldV > 0f ? (newV - oldV) / oldV * 100f : 0f;
            row.Preview.text = $"{label} {Mathf.RoundToInt(oldV)} → {Mathf.RoundToInt(newV)} ({PercentText(pct)})";
            // полоска-прирост: серый — старое, цветной — новое (до верха дорожки)
            float top = Mathf.Max(newV, 0.01f);
            SetFill(row.PreviewBar.CurrentFill, oldV / top);
            SetFill(row.PreviewBar.ValueFill, 1f);
        }

        // прибавка следующего уровня: максимум из конфига / 3 — шкала линейная
        private static string NextLevelEffect(UpgradeConfig upgrades, UpgradeLine line)
        {
            float third = 1f / ShipStats.MaxLevel;
            switch (line)
            {
                case UpgradeLine.Engine:
                    return string.Format(EngineEffectFormat, Mathf.RoundToInt(upgrades.EngineThrustPct * third));
                case UpgradeLine.Tank:
                    return string.Format(TankEffectFormat,
                        Mathf.RoundToInt(upgrades.TankCapacityPct * third),
                        Mathf.RoundToInt(upgrades.TankDrainRedPct * third));
                case UpgradeLine.Hull:
                    return string.Format(HullEffectFormat,
                        Mathf.RoundToInt(upgrades.HullHpPct * third),
                        Mathf.RoundToInt(upgrades.HullHeatRedPct * third));
                case UpgradeLine.Maneuver:
                    return string.Format(ManeuverEffectFormat, Mathf.RoundToInt(upgrades.ManeuverStrafePct * third));
                default:
                    return "";
            }
        }

        // покупка улучшения: только свой корабль; эффект применит сам TryUpgrade
        // (Ship.Eff пересчитывается, без лечения/заправки)
        private void BuyUpgrade(UpgradeLine line)
        {
            if (session == null || session.Fleet == null || !CanOpen())
                return;
            var fleet = session.Fleet;
            if (!fleet.IsOwned(fleet.Current))
                return; // аренда всегда сток
            if (fleet.TryUpgrade(fleet.Current, line, session.Wallet))
                Sound.Play(SoundId.PurchaseCash); // П9.3: касса на покупку улучшения
        }

        // блок «Долг» — свой метод-строитель
        private void BuildDebtBlock(Transform shop)
        {
            debtSection = Section(shop, "// ДОЛГ", "DebtSection");
            debtLine = OnboardComputerFactory.Text(debtSection, "", 18f, OnboardComputerFactory.DangerColor, "DebtLine");
            OnboardComputerFactory.FixedHeight(debtLine.gameObject, 24f);

            var go = OnboardComputerFactory.Button(debtSection.transform, "—",
                new Color(OnboardComputerFactory.DangerColor.r, OnboardComputerFactory.DangerColor.g,
                    OnboardComputerFactory.DangerColor.b, 0.22f),
                OnboardComputerFactory.DangerColor, 20f, "RepayButton");
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 44f;
            le.preferredHeight = 44f;
            repayButton = go.GetComponent<Button>();
            repayButton.onClick.AddListener(RepayDebt);
            repayLabel = go.GetComponentInChildren<TMP_Text>();

            RefreshDebt(); // Balance == 0 → секция скрыта сразу
        }

        // виден только при Balance > 0; кнопка активна при достатке кредитов
        private void RefreshDebt()
        {
            if (debtSection == null || session == null || session.Debt == null)
                return;
            int balance = session.Debt.Balance;
            debtSection.SetActive(balance > 0);
            if (balance <= 0)
                return;
            debtLine.text = $"Долг: {balance} кр · Аварий: {session.Debt.UnpaidAccidents}/{MaxUnpaidAccidents}";
            repayLabel.text = $"ПОГАСИТЬ ДОСРОЧНО ({balance} КР)";
            repayButton.interactable = session.Wallet.Credits >= balance;
        }

        // §3.2: досрочное гашение — единовременно полной суммой
        private void RepayDebt()
        {
            if (session == null || session.Debt == null)
                return;
            int balance = session.Debt.Balance;
            if (balance <= 0 || !session.Wallet.TrySpend(balance))
                return;
            session.Debt.Repay(balance); // DebtChanged скроет блок, счётчик аварий обнулится сам
            Sound.Play(SoundId.DebtTone); // П9.3: тон расчёта по долгу
            ShowToast("Долг погашен", emphasized: true);
        }

        // --- Н-3а: витрина магазина — одна модель на экране, листание,
        // ротация покрасок, покупка показанного варианта ---

        // каталог читается из Resources один раз; порядок витрины — по цене
        // (при равенстве по тиру и имени, чтобы список не плясал)
        private ShipConfig[] Catalog()
        {
            if (shipCatalog != null)
                return shipCatalog;
            var configs = Resources.LoadAll<ShipConfig>("Ships");
            if (configs == null || configs.Length == 0)
                Debug.LogWarning("[Station] Каталог кораблей пуст — Assets/ScriptableObjects/Resources/Ships");
            if (configs == null)
                return Array.Empty<ShipConfig>(); // guard: Sort ниже разыменовал бы null
            Array.Sort(configs, (a, b) =>
            {
                int byPrice = a.Price.CompareTo(b.Price);
                if (byPrice != 0)
                    return byPrice;
                int byTier = a.Tier.CompareTo(b.Tier);
                return byTier != 0 ? byTier : string.CompareOrdinal(a.DisplayName, b.DisplayName);
            });
            shipCatalog = configs;
            return shipCatalog;
        }

        // каркас витрины (Н3.4): [←] спрайт [→] над именем с бейджем «ВЫ ЗДЕСЬ»,
        // таблицей 6 статов с полосками сравнения, ценой и кнопкой
        private void BuildShowcaseBlock(Transform parent)
        {
            shopWindow = Section(parent, "// МАГАЗИН", "ShopWindow");

            var showcaseRow = OnboardComputerFactory.H("ShowcaseRow", shopWindow.transform);
            BuildArrowButton(showcaseRow.transform, "←", () => FlipShowcase(-1));
            var box = new GameObject("SpriteBox", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(showcaseRow.transform, false);
            showcaseSprite = box.GetComponent<Image>();
            showcaseSprite.color = new Color(
                OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g,
                OnboardComputerFactory.LabelColor.b, 0.08f);
            showcaseSprite.preserveAspect = true;
            // рамка как у кнопок фабрики — карточка читается как один блок
            var frame = box.AddComponent<Outline>();
            frame.effectColor = new Color(0.55f, 0.85f, 0.7f, 0.35f);
            frame.effectDistance = new Vector2(2f, -2f);
            OnboardComputerFactory.FixedSize(box, ShowcaseSpriteW, ShowcaseSpriteH);
            BuildArrowButton(showcaseRow.transform, "→", () => FlipShowcase(1));

            var nameRow = OnboardComputerFactory.H("NameRow", shopWindow.transform);
            OnboardComputerFactory.FixedHeight(nameRow.gameObject, 28f);
            showcaseName = OnboardComputerFactory.Text(nameRow, "", 22f, OnboardComputerFactory.BrightColor, "ModelName");
            OnboardComputerFactory.Flexible(showcaseName.gameObject);
            showcaseBadge = BuildBadge(nameRow.transform, "ВЫ ЗДЕСЬ");
            showcaseBadge.SetActive(false);

            showcasePaint = OnboardComputerFactory.Text(shopWindow, "", 16f, OnboardComputerFactory.LabelColor, "PaintLine");
            OnboardComputerFactory.FixedHeight(showcasePaint.gameObject, 20f);

            // таблица статов: подпись | полоска сравнения | % отличия от текущего
            var statsTable = new GameObject("StatsTable", typeof(RectTransform));
            statsTable.transform.SetParent(shopWindow.transform, false);
            var tableLayout = statsTable.AddComponent<VerticalLayoutGroup>();
            tableLayout.spacing = 2f;
            tableLayout.childControlWidth = true;
            tableLayout.childControlHeight = true;
            tableLayout.childForceExpandWidth = true;
            tableLayout.childForceExpandHeight = false;

            showcaseStatRows = new ShowStatRowRefs[StatCount];
            for (int i = 0; i < StatCount; i++)
            {
                var rowGo = new GameObject("Stat_" + (i + 1), typeof(RectTransform));
                rowGo.transform.SetParent(statsTable.transform, false);
                var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 8f;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = true;
                rowLayout.childForceExpandHeight = false;
                rowLayout.childAlignment = TextAnchor.MiddleLeft;
                var rowElement = rowGo.AddComponent<LayoutElement>();
                rowElement.minHeight = 20f;
                rowElement.preferredHeight = 20f;

                var refs = new ShowStatRowRefs();
                refs.Label = OnboardComputerFactory.Text(rowGo, StatLabels[i], 15f, OnboardComputerFactory.LabelColor, "Label");
                OnboardComputerFactory.FixedWidth(refs.Label.gameObject, 132f);
                OnboardComputerFactory.FixedHeight(refs.Label.gameObject, 20f);
                refs.Bar = BuildCompareBar(rowGo.transform, 12f);
                refs.Percent = OnboardComputerFactory.Text(rowGo, "0%", 15f, OnboardComputerFactory.LabelColor, "Percent");
                OnboardComputerFactory.FixedWidth(refs.Percent.gameObject, 64f);
                OnboardComputerFactory.FixedHeight(refs.Percent.gameObject, 20f);
                refs.Percent.alignment = TextAlignmentOptions.Right;
                showcaseStatRows[i] = refs;
            }

            showcaseStatus = OnboardComputerFactory.Text(shopWindow, "", 19f, OnboardComputerFactory.TaxiColor, "PriceLine");
            OnboardComputerFactory.FixedHeight(showcaseStatus.gameObject, 24f);
            shopBuy = BuildServiceButton(shopWindow.transform, "ShopBuyButton", ConfirmShopAction, out shopBuyLabel);
            shopStatus = OnboardComputerFactory.Text(shopWindow, "", 17f, OnboardComputerFactory.LabelColor, "Status");
            OnboardComputerFactory.FixedHeight(shopStatus.gameObject, 22f);

            Catalog(); // сортировка по цене — до первой отрисовки
            RefreshShowcase();
        }

        // бейдж «ВЫ ЗДЕСЬ»: рамка в стиле кнопок фабрики, янтарный — цвет такси
        private static GameObject BuildBadge(Transform parent, string label)
        {
            var go = new GameObject("HereBadge", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var taxi = OnboardComputerFactory.TaxiColor;
            go.GetComponent<Image>().color = new Color(taxi.r, taxi.g, taxi.b, 0.22f);
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(taxi.r, taxi.g, taxi.b, 0.6f);
            outline.effectDistance = new Vector2(2f, -2f);
            var text = OnboardComputerFactory.Text(go, label, 15f, taxi, "BadgeText");
            OnboardComputerFactory.Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            OnboardComputerFactory.FixedSize(go, 104f, 24f);
            return go;
        }

        // полоска сравнения: дорожка + серый fill (текущее/старое) + цветной
        // fill (модель/новое); ширины ставит SetFill по долям значений
        private static CompareBarRefs BuildCompareBar(Transform parent, float height)
        {
            var bar = new GameObject("Bar", typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            var element = bar.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.minHeight = height;
            element.preferredHeight = height;

            var dim = OnboardComputerFactory.LabelColor;
            var refs = new CompareBarRefs();

            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(bar.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)track.transform);
            var trackImage = track.GetComponent<Image>();
            trackImage.color = new Color(dim.r, dim.g, dim.b, 0.1f);
            trackImage.raycastTarget = false;

            refs.CurrentFill = BuildBarFill(track.transform, new Color(dim.r, dim.g, dim.b, 0.45f), "CurrentFill");
            refs.ValueFill = BuildBarFill(track.transform, OnboardComputerFactory.BrightColor, "ValueFill");
            refs.ValueImage = refs.ValueFill.GetComponent<Image>();
            return refs;
        }

        // fill-дочка: левый край закреплён, правый двигает SetFill (доля 0..1)
        private static RectTransform BuildBarFill(Transform parent, Color color, string name)
        {
            var fill = new GameObject(name, typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(parent, false);
            var rect = (RectTransform)fill.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = fill.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        // ширина fill-полоски по доле значения от максимума
        private static void SetFill(RectTransform fill, float fraction)
        {
            fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
        }

        // «+18%» / «−7%» / «0%» — минус типографский, как в подписях эффектов
        private static string PercentText(float pct)
        {
            int rounded = Mathf.RoundToInt(pct);
            if (rounded == 0)
                return "0%";
            return (rounded > 0 ? "+" : "−") + Mathf.Abs(rounded) + "%";
        }

        // стрелка листания витрины [←]/[→]
        private void BuildArrowButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = OnboardComputerFactory.Button(parent, label,
                new Color(OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g,
                    OnboardComputerFactory.LabelColor.b, 0.18f),
                OnboardComputerFactory.LabelColor, 26f, "Arrow");
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 48f;
            le.preferredWidth = 48f;
            le.minHeight = ShowcaseSpriteH;
            le.preferredHeight = ShowcaseSpriteH;
            go.GetComponent<Button>().onClick.AddListener(onClick);
        }

        private void FlipShowcase(int delta)
        {
            int count = Catalog().Length;
            if (count == 0)
                return;
            showcaseIndex = (showcaseIndex + delta + count) % count;
            RefreshShowcase();
        }

        // витрина перечитывается на каждый RefreshShop (деньги/парк) и при
        // смене слота ротации из Update — вариант покраски меняется сам
        private void RefreshShowcase()
        {
            ShipConfig[] catalog = Catalog();
            if (shopWindow == null || catalog.Length == 0
                || session == null || session.Fleet == null)
                return;
            var fleet = session.Fleet;
            var config = catalog[showcaseIndex];
            showcaseShip = config;
            int slot = ShopStockService.CurrentSlot(Time.time);
            stockSlot = slot;
            int paintIndex = ShopStockService.DisplayPaintIndex(config, slot);

            // спрайт: показанный вариант покраски (ротация) или заводской;
            // арта нет вовсе — заглушка-треугольник с затенением по тиру
            Sprite sprite = config.GetPaintSprite(paintIndex);
            bool current = IsCurrent(fleet, config);
            bool owned = fleet.IsOwned(config);
            showcaseSprite.sprite = sprite != null ? sprite : StubSprites.ShipTriangle();
            showcaseSprite.color = sprite == null ? TierShade(config.Tier)
                : owned && !current ? new Color(0.62f, 0.62f, 0.62f, 0.9f) // «в ангаре» — серым
                : Color.white;

            bool rental = config.IsRental;
            showcaseName.text = config.DisplayName + " · Т" + config.Tier;
            if (showcaseBadge != null)
                showcaseBadge.SetActive(current);
            UpdateShowcaseStats(config);
            int variants = config.PaintVariantCount;
            showcasePaint.text = variants == 0 ? "Расцветка: заводская"
                : paintIndex == 0
                    ? $"Расцветка: заводская · ещё {variants} в ротации"
                    : $"Расцветка: вариант {paintIndex} из {variants} (ротация 2 игрочаса)";
            showcaseStatus.text = current ? "В полёте (текущий)"
                : owned ? "В ангаре"
                : rental ? "Аренда"
                : config.Price + " кр";

            bool canBuy = !owned && !rental && config.Price > 0;
            bool canSit = !current && (owned || rental);
            shopBuy.interactable = (canBuy && session.Wallet.Credits >= config.Price) || canSit;
            shopBuyLabel.text = canBuy
                ? $"КУПИТЬ ({config.Price} КР)"
                : canSit ? "СЕСТЬ В"
                : current ? "В ПОЛЁТЕ"
                : "НЕТ В ПРОДАЖЕ";
        }

        // сравниваемая величина стата (Н3.4): индексы 0..4 — «больше = лучше»,
        // 5 — тормозной путь K/Retro («меньше = лучше», ед. не указываем —
        // «отн.»). Retro=0 — глобальный фолбэк из PhysicsConfig, как в полёте
        private float CompareValue(ShipConfig config, int index)
        {
            switch (index)
            {
                case 0: return config.Thrust;
                case 1: return config.StrafeAcc;
                case 2: return config.MaxV;
                case 3: return config.FuelMax;
                case 4: return config.HpMax;
                default: return BrakeDistanceK / RetroOf(config);
            }
        }

        // модельное торможение или глобальное (ShipStats.Compute использует 80
        // как последний фолбэк — то же число здесь); 0 не отдаём — в знаменателе
        private float RetroOf(ShipConfig config)
        {
            if (config != null && config.Retro > 0f)
                return config.Retro;
            float global = session != null && session.Config != null ? session.Config.Retro : 80f;
            return global > 0f ? global : 80f;
        }

        // таблица статов с полосками сравнения (Н3.4): серая полоса — текущий
        // корабль, цветная — модель на витрине (зелёная — лучше, красная —
        // хуже), справа % отличия. Ширина = значение / максимум по каталогу
        // и текущему кораблю, чтобы полоски всех моделей были сопоставимы
        private void UpdateShowcaseStats(ShipConfig config)
        {
            if (showcaseStatRows == null || config == null)
                return;
            ShipConfig[] catalog = Catalog();
            ShipConfig baseline = session.Fleet.Current;
            for (int i = 0; i < showcaseStatRows.Length; i++)
            {
                float value = CompareValue(config, i);
                float current = baseline != null ? CompareValue(baseline, i) : 0f;
                float max = current;
                for (int c = 0; c < catalog.Length; c++)
                {
                    if (catalog[c] == null)
                        continue;
                    max = Mathf.Max(max, CompareValue(catalog[c], i));
                }

                var row = showcaseStatRows[i];
                SetFill(row.Bar.CurrentFill, max > 0f ? current / max : 0f);
                SetFill(row.Bar.ValueFill, max > 0f ? value / max : 0f);

                float diff = value - current;
                // тормозной путь — обратная величина: короче = лучше
                bool better = i == StatBrakeIndex ? diff < 0f : diff > 0f;
                bool worse = i == StatBrakeIndex ? diff > 0f : diff < 0f;
                Color color = better ? OnboardComputerFactory.BrightColor
                    : worse ? OnboardComputerFactory.DangerColor
                    : OnboardComputerFactory.LabelColor;
                row.Bar.ValueImage.color = color;

                float pct = current > 0f ? diff / current * 100f : 0f;
                row.Percent.text = PercentText(pct);
                row.Percent.color = color;
            }
        }

        // «Яйцо» из фолбэка (инстанс в памяти) и ассет egg_rental — одна модель:
        // до первой пересадки Current не совпадает по ссылке с витриной аренды
        private static bool IsCurrent(FleetService fleet, ShipConfig config)
        {
            if (fleet.Current == config)
                return true;
            return config != null && config.IsRental
                && fleet.Current != null && fleet.Current.IsRental;
        }

        // затенение заглушки по тиру: Т1 — приглушённая, Т5 — яркая
        private static Color TierShade(int tier)
        {
            float t = Mathf.Clamp01((tier - 1) / 4f);
            var dim = OnboardComputerFactory.LabelColor;
            dim.a = 0.4f;
            return Color.Lerp(dim, OnboardComputerFactory.BrightColor, t);
        }

        // единственная кнопка витрины: покупка показанного варианта или
        // пересадка в свой/арендный корабль. Правила покупки — в
        // FleetService.TryBuy: не-аренда, Price > 0, не куплен; после оплаты
        // TryBuy сразу пересаживает (полные HP/бак), ShipChanged перерисует сам
        private void ConfirmShopAction()
        {
            if (session == null || session.Fleet == null || showcaseShip == null || !CanOpen())
                return;
            var config = showcaseShip;
            if (session.Fleet.IsOwned(config) || config.IsRental)
            {
                SitIn(config);
                return;
            }
            int price = config.Price;
            if (price <= 0)
                return; // «НЕТ В ПРОДАЖЕ» — кнопка серая, но поток проверяет сам
            // покупка в показанном варианте: индекс витрины = индекс покраски
            int paintIndex = ShopStockService.DisplayPaintIndex(config, ShopStockService.CurrentSlot(Time.time));
            if (!session.Fleet.TryBuy(config, session.Wallet, paintIndex))
                return;
            Sound.Play(SoundId.PurchaseCash); // П9.3: касса на покупку корабля
            ShowToast($"Куплен «{config.DisplayName}» (−{price} кр)", emphasized: true);
            shopStatus.text = paintIndex == 0
                ? $"Куплен «{config.DisplayName}» в заводской расцветке"
                : $"Куплен «{config.DisplayName}», покраска вариант {paintIndex}";
            // купленной модели «нет в продаже» — витрина листается дальше,
            // сама модель остаётся в списке серой с меткой «В ангаре»
            FlipShowcase(1);
        }

        private void SitIn(ShipConfig config)
        {
            if (session == null || session.Fleet == null || config == null || !CanOpen())
                return;
            if (IsCurrent(session.Fleet, config))
                return; // уже за штурвалом — кнопка и не должна была быть видна
            session.Fleet.SwitchTo(config);
            ShowToast($"Пересел в «{config.DisplayName}»");
        }

        private void ShowToast(string message, bool emphasized = false)
        {
            if (toast == null)
                toast = FindAnyObjectByType<ToastNotifier>();
            if (toast != null)
                toast.Show(message, emphasized);
        }
    }
}
