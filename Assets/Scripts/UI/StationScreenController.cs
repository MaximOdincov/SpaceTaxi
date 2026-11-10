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
    // флагам (заправка/мастерская). Каркас попапа — StationUI (рамка С4):
    // хедер «СТАНЦИЯ · …» с кредитами «Đ N» (обновляются по
    // Wallet.CreditsChanged) и крестик закрытия в углу (Esc закрывает тем же
    // Close()). Заправка (С1, механика Н-4 не тронута): ячейки бака на всю
    // ширину панели, слайдер объёма, сумма «Đ», ОДНА кнопка подтверждения —
    // по умолчанию выбран весь доступный объём («ЗАПРАВИТЬ ДО ПОЛНОГО»),
    // пресеты убраны. Ремонт (С4): HP-бар и ОДНА кнопка «Починить» — полный
    // пакет, зажатый потолком визита; цена = HP × множитель пакета × наценка
    // тира сети (RepairTierSurchargeMul). Правило тиров мастерских: T1 берёт
    // корабли 1–2, T2 — 1–3, T3+ — 4–5 (RepairNetwork.ShipTierAccepted),
    // чужой корабль — отказ с текстом борткомпа. Магазин (C2): слева таблица
    // статов — полоска рассматриваемой модели с риской (засечкой) твоего
    // текущего значения; отрезок между ними подкрашен (зелёный — модель
    // лучше, красный — хуже); ВСЕ статы «больше = лучше», торможение —
    // «Мощность торможения» по полю Retro (инверсий нет). Справа — большая
    // модель в ВЫБРАННОЙ расцветке (свотчи под таблицей, механика бывшего
    // FlipPaint; покупка берёт выбранный вариант), внизу — навигация
    // [←] ИМЯ · ТИР N [→] и подпись статуса владения («ВАШ»/«В АРЕНДЕ ·
    // СЕЙЧАС АКТИВЕН») вместо бейджа «ВЫ ЗДЕСЬ». Сортировка витрины по цене;
    // базовые корабли всегда в наличии, показанный вариант покраски
    // (ShipConfig.PaintVariants) ротирует ShopStockService раз в 2 игровых
    // часа. Улучшения (§3.4, C3): строки линий «УР N → УР N+1», прибавка из
    // процентов UpgradeConfig, цена «Đ» — по-линейные GetPrice(line,
    // level+1), полоска уровня из 3 сегментов; только на СВОЙ корабль.
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

        // витрина (C2): слева таблица статов с риской текущего значения, справа
        // большая модель в выбранной расцветке
        private const float ShowcaseSpriteW = 250f;
        private const float ShowcaseSpriteH = 200f;
        private const int StatCount = 6; // Thrust, MaxV, FuelMax, HpMax, Retro, StrafeAcc
        // ширины колонок строки стата: подпись — под «Мощность торможения»
        // (самая длинная строка), справа округлённое значение модели
        private const float StatLabelWidth = 165f;
        private const float StatValueWidth = 44f;
        // свотчи расцветок: 30px влезает при ≤5 вариантах, 26px при 6 (в колонке
        // статов под свотчи ~220px)
        private const float SwatchSize = 30f;
        private const float SwatchSizeSmall = 26f;

        // подписи статов витрины (C2); ВСЕ статы «больше = лучше»: тормозной
        // путь переименован в «Мощность торможения» (поле Retro, больше =
        // лучше). Индексы совпадают со StatValue/CurrentStatValue
        private static readonly string[] StatLabels =
        {
            "Тяга", "Макс. скорость", "Бак", "Прочность", "Мощность торможения", "Манёвренность",
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
        private TMP_Text creditsValue; // «Đ N» — рамка StationUI, перечитывается по Wallet.CreditsChanged
        private TMP_Text fuelValue;
        private TMP_Text hullValue;
        private TMP_Text fuelStatus;
        private TMP_Text hullStatus;
        private Image[] fuelSegments;   // ячейки бака на всю ширину панели (С1)
        private RectTransform hpFill;   // заливка HP-бара ремонта (С4)
        private Button fuelButton;
        private TMP_Text fuelButtonLabel;
        // заправка (С1): слайдер объёма с живым предрасчётом; пресеты убраны —
        // одна кнопка подтверждения (по умолчанию выбран весь доступный объём)
        private TMP_Text fuelStockLine;  // «Топлива на станции: N ед · цена M Đ/ед»
        private TMP_Text fuelOfferLabel; // «Заправить: X ед · Đ Y»
        private Slider fuelSlider;
        // ремонт (С4): одна кнопка «Починить» вместо трёх пакетов
        private TMP_Text repairTierLine; // «Мастерская тир N — за визит ≤X% · наценка ×K»
        private TMP_Text repairOffer;    // «Починка: +N HP · Đ M» или отказ по тиру корабля
        private Button repairButton;
        private TMP_Text repairButtonLabel;
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
        private int showcasePaintIndex; // покраска на витрине сейчас: 0 = заводская, 1..N — PaintVariants
        private int stockSlot;           // слот ротации на последней отрисовке
        private ShipConfig showcaseShip; // модель на витрине сейчас
        private Image showcaseSprite;
        private TMP_Text showcaseName;
        private TMP_Text showcasePaint;
        // C2: таблица 6 статов — полоска модели + риска текущего значения +
        // подкрашенный отрезок разницы
        private ShowStatRowRefs[] showcaseStatRows;
        private TMP_Text showcaseState;    // «ВАШ · СЕЙЧАС АКТИВЕН» / «В АРЕНДЕ …» — вместо бейджа
        private GameObject paintSwatchRow; // свотчи расцветок: выбор меняет большой спрайт
        private Image[] paintSwatchIcons;  // иконки свотчей по индексу покраски
        private ShipConfig swatchConfig;   // модель, под которую собраны свотчи
        private Button shopBuy;
        private TMP_Text shopBuyLabel;
        private TMP_Text shopStatus; // «Не хватает Đ N» при недостаче, иначе пусто
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
            // С1: по умолчанию выбран весь доступный объём — кнопка сразу
            // читается «ЗАПРАВИТЬ ДО ПОЛНОГО»; onValueChanged перерисует суммы
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

        // --- Заправка (Н-4): слайдер объёма, цена/ед и запас — по тиру сети планеты ---

        // конфиг цен: из OrderService (ассет сцены / Resources) или кодовый фолбэк
        private PricingConfig Pricing =>
            session != null && session.Orders != null ? session.Orders.Pricing : PricingConfig.Default;

        private int FuelTier => currentPlanet != null ? currentPlanet.FuelTier : PricingConfig.BaseFuelTier;

        // район станции для цены топлива (Н4.5): у экрана без планеты — базовый Near
        private Sector FuelSector => currentPlanet != null ? currentPlanet.Sector : Sector.Near;

        // сеть станции пуста (Н4.5): заправка есть, топлива в ней нет
        private bool FuelOutage => currentPlanet != null && currentPlanet.FuelOutage;

        private int RepairTier => currentPlanet != null ? currentPlanet.RepairTier : PricingConfig.BaseRepairTier;

        private int FuelAvailable()
        {
            if (ship == null || currentPlanet == null || FuelOutage)
                return 0;
            return FuelNetwork.AvailableUnits(ship.Eff.FuelMax, ship.Fuel, FuelTier, Pricing);
        }

        // подтверждение: оплата ровно за выбранное (Wallet.TrySpend → Ship.Fuel → ReportServiced);
        // выбор слайдера по умолчанию — весь доступный объём («до полного»)
        private void ConfirmRefuel()
        {
            if (!CanOpen() || session == null || fuelSlider == null || FuelOutage)
                return;
            int units = Mathf.RoundToInt(fuelSlider.value);
            int cost = FuelNetwork.CostOf(units, FuelTier, FuelSector, Pricing);
            if (units <= 0 || cost <= 0 || !session.Wallet.TrySpend(cost))
                return;
            ship.Fuel += units; // сеттер клампит и шлёт FuelChanged — HUD и экран обновятся
            fuelStatus.text = $"Заправлено {units} ед за {StationUI.Currency} {OnboardBinder.FormatCredits(cost)}";
            landing.ReportServiced(ship.LandedOn.DisplayName);
        }

        // запас сети и диапазон слайдера — при каждом Refresh: посадка, заправка
        // и улучшение бака меняют Eff.FuelMax/топливо
        private void UpdateFuelWindow()
        {
            int available = FuelAvailable();
            float perUnit = FuelNetwork.PricePerUnit(FuelTier, FuelSector, Pricing);

            // 0 доступно: сеть станции пуста (Н4.5), бак полон — заправка не
            // нужна, либо бак уже глубже запаса сети (тексты в стиле борткомпа)
            fuelStockLine.text = FuelOutage
                ? "Сеть станции пуста — топлива нет"
                : available > 0
                    ? $"Топлива на станции: {available} ед · цена {perUnit:0.##} {StationUI.Currency}/ед"
                    : ship.Fuel >= ship.Eff.FuelMax - 0.5f
                        ? "Бак полон — заправка не нужна"
                        : "Запас сети ниже текущего топлива";

            fuelSlider.minValue = 0f;
            fuelSlider.maxValue = Mathf.Max(1, available); // 0..0 слайдер не показывает
            fuelSlider.interactable = !FuelOutage && available > 0;
            fuelSlider.SetValueWithoutNotify(Mathf.Clamp(fuelSlider.value, 0f, available));

            UpdateFuelOffer();
        }

        // живой предрасчёт: сумма «Đ» и ОДНА кнопка (С1) — подтверждает выбор
        // слайдера; на полном выборе подпись «ЗАПРАВИТЬ ДО ПОЛНОГО»
        private void UpdateFuelOffer()
        {
            int available = FuelAvailable();
            int selected = FuelOutage
                ? 0
                : Mathf.Clamp(Mathf.RoundToInt(fuelSlider.value), 0, available);
            int cost = FuelNetwork.CostOf(selected, FuelTier, FuelSector, Pricing);

            fuelOfferLabel.text = FuelOutage
                ? "Заправка не работает"
                : $"Заправить: {selected} ед · {StationUI.Currency} {OnboardBinder.FormatCredits(cost)}";
            fuelButton.interactable = !FuelOutage && selected > 0 && session.Wallet.Credits >= cost;
            fuelButtonLabel.text = FuelOutage
                ? "ТОПЛИВА НЕТ"
                : selected <= 0 ? "ВЫБЕРИТЕ ОБЪЁМ"
                : selected >= available
                    ? $"ЗАПРАВИТЬ ДО ПОЛНОГО · {StationUI.Currency} {OnboardBinder.FormatCredits(cost)}"
                    : $"ЗАПРАВИТЬ · {StationUI.Currency} {OnboardBinder.FormatCredits(cost)}";
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

        // ячейки бака (С1): ряд сегментов НА ВСЮ ШИРИНУ панели — отдельная
        // строка секции, без подписи и чисел в том же ряду (те — над ячейками).
        // Цвета сегментов — как в HUD (SegmentOn/SegmentOff)
        private static Image[] BuildTankCells(Transform parent)
        {
            var row = new GameObject("TankCells", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            OnboardComputerFactory.FixedHeight(row.gameObject, 34f);

            var cells = new Image[SegmentCount];
            for (int i = 0; i < SegmentCount; i++)
            {
                var cell = new GameObject("Cell_" + (i + 1).ToString("00"), typeof(RectTransform), typeof(Image));
                cell.transform.SetParent(row.transform, false);
                cell.AddComponent<LayoutElement>().flexibleWidth = 1f;
                cells[i] = cell.GetComponent<Image>();
                cells[i].color = SegmentOff;
                cells[i].raycastTarget = false;
            }
            return cells;
        }

        // --- Ремонт (С4): HP-бар + ОДНА кнопка «Починить» вместо карточек
        // пакетов. Правило тиров мастерских (RepairNetwork.ShipTierAccepted):
        // T1 берёт корабли 1–2, T2 — 1–3, T3+ — 4–5; чужой корабль не чинится,
        // на экране отказ текстом борткомпа. Кнопка = полный пакет, зажатый
        // потолком визита (InitRepairBudget); цена = HP × множитель пакета ×
        // наценка тира сети (RepairTierSurchargeMul, С4) ---

        // тир корабля таксиста для правила тиров мастерских (FleetService.Current)
        private int ShipTier
        {
            get
            {
                var config = session != null && session.Fleet != null ? session.Fleet.Current : null;
                return config != null ? config.Tier : 2;
            }
        }

        // бюджет визита считается от недостающего HP на момент посадки и
        // уменьшается покупками — «за визит не более X%» выполняется ровно
        private void InitRepairBudget()
        {
            repairBudgetPlanet = currentPlanet;
            float cap = Pricing.GetRepairTierCap(RepairTier);
            repairBudgetRemaining = cap * RepairNetwork.MissingHp(ship.Eff.HpMax, ship.Hp);
        }

        // потолок станции, круг тиров и предложение — при каждом Refresh:
        // ремонт, урон, деньги
        private void UpdateRepairWindow()
        {
            int tier = RepairTier;
            float missing = RepairNetwork.MissingHp(ship.Eff.HpMax, ship.Hp);
            float cap = Pricing.GetRepairTierCap(tier);
            float surcharge = Pricing.GetRepairTierSurcharge(tier);

            repairTierLine.text = $"Мастерская тир {tier} — за визит не более {Mathf.RoundToInt(cap * 100f)}% починки · наценка ×{surcharge:0.##}";

            if (missing <= 0f)
            {
                repairOffer.text = "Корпус цел — ремонт не нужен";
                repairOffer.color = OnboardComputerFactory.LabelColor;
                repairButton.interactable = false;
                repairButtonLabel.text = "КОРПУС ЦЕЛ";
                hullStatus.text = "";
                return;
            }

            // С4: корабль вне круга тиров станции — отказ, ремонта не будет
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

            // полный пакет от текущего недостающего, зажатый остатком бюджета визита
            float amount = RepairNetwork.RepairAmount(ship.Eff.HpMax, ship.Hp,
                RepairNetwork.PackCount - 1, tier, Pricing, repairBudgetRemaining);
            int cost = RepairNetwork.CostOf(amount, RepairNetwork.PackCount - 1, tier, Pricing);

            if (amount <= 0.01f)
            {
                repairOffer.text = "Потолок визита исчерпан — приходите после нового урона";
                repairOffer.color = OnboardComputerFactory.LabelColor;
                repairButton.interactable = false;
                repairButtonLabel.text = "ЛИМИТ ВИЗИТА";
                hullStatus.text = "";
                return;
            }

            repairOffer.text = $"Починка: +{Mathf.RoundToInt(amount)} HP · {StationUI.Currency} {OnboardBinder.FormatCredits(cost)}";
            repairOffer.color = OnboardComputerFactory.BrightColor;
            bool canPay = session.Wallet.Credits >= cost;
            repairButton.interactable = canPay;
            repairButtonLabel.text = canPay ? "ПОЧИНИТЬ" : "МАЛО КРЕДИТОВ";
        }

        // подтверждение: существующий платёжный путь — TrySpend → HullSystem.Repair;
        // наценка тира сети внутри RepairNetwork.CostOf (базовая формула не тронута)
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
                RepairNetwork.PackCount - 1, tier, Pricing, repairBudgetRemaining);
            int cost = RepairNetwork.CostOf(amount, RepairNetwork.PackCount - 1, tier, Pricing);
            if (amount <= 0.01f || cost <= 0 || !session.Wallet.TrySpend(cost))
                return;
            hull.Repair(amount); // HpChanged дойдёт до HUD и до Refresh — экран пересчитается
            repairBudgetRemaining = Mathf.Max(0f, repairBudgetRemaining - amount);
            int pct = Mathf.RoundToInt(amount / missing * 100f);
            hullStatus.text = $"Отремонтировано на {pct}% (+{Mathf.RoundToInt(amount)} HP) за {StationUI.Currency} {OnboardBinder.FormatCredits(cost)}";
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
            creditsValue.text = StationUI.Currency + " " + OnboardBinder.FormatCredits(session.Wallet.Credits);

            UpdateGauge(fuelSegments, ship.FuelFraction);
            fuelValue.text = Mathf.RoundToInt(ship.Fuel) + "/" + Mathf.RoundToInt(ship.Eff.FuelMax);
            UpdateHpBar(ship.HpFraction);
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
            // C2: панель шире (640 → 700) — витрина: слева статы, справа большая модель
            panelRect.sizeDelta = new Vector2(700f, 0f); // высоту подберёт fitter
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

            // --- Шапка попапа (рамка С4): «СТАНЦИЯ · …» слева, кредиты «Đ N»
            // справа (Refresh перечитывает по Wallet.CreditsChanged), крестик
            // закрытия в правом верхнем углу — клик закрывает попап ---
            var header = StationUI.PopupHeader(panel.transform, "СТАНЦИЯ");
            stationTitle = header.Title;
            creditsValue = header.Credits;
            header.Close.onClick.AddListener(Close);

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

            // --- Окно заправки (вкладка ЗАПРАВКА, С1): ячейки бака на всю
            // ширину панели, слайдер объёма (механика Н-4 не тронута), сумма
            // «Đ», одна кнопка подтверждения ---
            fuelWindow = StationUI.Section(panel.transform, "// ТОПЛИВО", "FuelWindow");

            // шапка бака: метка слева, значение справа (правило 1.32: 17pt ≈ 22.4 → высота 24)
            var fuelHead = OnboardComputerFactory.H("FuelHead", fuelWindow.transform);
            OnboardComputerFactory.FixedHeight(fuelHead.gameObject, 24f);
            var fuelCaption = OnboardComputerFactory.Text(fuelHead, "БАК", 17f, OnboardComputerFactory.LabelColor, "Caption");
            OnboardComputerFactory.Flexible(fuelCaption.gameObject);
            fuelValue = OnboardComputerFactory.Text(fuelHead, "0/0", 17f, OnboardComputerFactory.BrightColor, "FuelValue");
            OnboardComputerFactory.FixedWidth(fuelValue.gameObject, 110f);
            fuelValue.alignment = TextAlignmentOptions.Right;

            fuelSegments = BuildTankCells(fuelWindow.transform);

            fuelStockLine = OnboardComputerFactory.Text(fuelWindow, "", 17f, OnboardComputerFactory.LabelColor, "StockLine");
            OnboardComputerFactory.FixedHeight(fuelStockLine.gameObject, 23f);
            fuelOfferLabel = OnboardComputerFactory.Text(fuelWindow, "", 18f, OnboardComputerFactory.BrightColor, "OfferLabel");
            OnboardComputerFactory.FixedHeight(fuelOfferLabel.gameObject, 24f);
            fuelSlider = BuildFuelSlider(fuelWindow.transform);
            OnboardComputerFactory.FixedHeight(fuelSlider.gameObject, 30f);
            fuelSlider.onValueChanged.AddListener(_ => UpdateFuelOffer());

            // ОДНА кнопка заправки (С1): пресеты убраны; каркасная кнопка —
            // яркий текст, ColorTint, щелчок озвучен каркасом. Подтверждает
            // выбор слайдера (по умолчанию — весь доступный объём)
            var fuelButtonGo = StationUI.Button(fuelWindow.transform, "—", 20f, "FuelButton");
            var fuelButtonLe = fuelButtonGo.AddComponent<LayoutElement>();
            fuelButtonLe.minHeight = 44f;
            fuelButtonLe.preferredHeight = 44f;
            fuelButtonLe.flexibleWidth = 1f;
            fuelButton = fuelButtonGo.GetComponent<Button>();
            fuelButton.onClick.AddListener(ConfirmRefuel);
            fuelButtonLabel = fuelButtonGo.GetComponentInChildren<TMP_Text>();

            fuelStatus = OnboardComputerFactory.Text(fuelWindow, "", 17f, OnboardComputerFactory.LabelColor, "Status");
            OnboardComputerFactory.FixedHeight(fuelStatus.gameObject, 23f);

            // --- Окно ремонта (вкладка РЕМОНТ, С4): HP-бар, цена «Đ», кнопка «Починить» ---
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

            var repairButtonGo = StationUI.Button(hullWindow.transform, "—", 20f, "RepairButton");
            var repairButtonLe = repairButtonGo.AddComponent<LayoutElement>();
            repairButtonLe.minHeight = 44f;
            repairButtonLe.preferredHeight = 44f;
            repairButtonLe.flexibleWidth = 1f;
            repairButton = repairButtonGo.GetComponent<Button>();
            repairButton.onClick.AddListener(ConfirmRepair);
            repairButtonLabel = repairButtonGo.GetComponentInChildren<TMP_Text>();

            hullStatus = OnboardComputerFactory.Text(hullWindow, "", 17f, OnboardComputerFactory.LabelColor, "Status");
            OnboardComputerFactory.FixedHeight(hullStatus.gameObject, 23f);

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
            // 22, не 20: строка 16pt ≈ 21.1 — Ellipsis при меньшей высоте режет по вертикали
            OnboardComputerFactory.FixedHeight(footer.gameObject, 22f);

            return root;
        }

        // HP-бар (С4): ширина заливки = доля корпуса; цвет — яркий зелень,
        // ниже четверти — тревожный красный (цвета борткомпа)
        private void UpdateHpBar(float fraction)
        {
            if (hpFill == null)
                return;
            SetFill(hpFill, fraction);
            var image = hpFill.GetComponent<Image>();
            if (image != null)
                image.color = fraction < 0.25f
                    ? OnboardComputerFactory.DangerColor
                    : OnboardComputerFactory.BrightColor;
        }

        private static Button BuildServiceButton(Transform parent, string name, UnityEngine.Events.UnityAction onClick, out TMP_Text label)
        {
            var go = StationUI.Button(parent, "—", 20f, name);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 44f;
            le.preferredHeight = 44f;
            le.flexibleWidth = 1f;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            label = go.GetComponentInChildren<TMP_Text>();
            return button;
        }

        // --- П3.3: блок улучшений и досрочного гашения долга ---

        private sealed class UpgradeRowRefs
        {
            public TMP_Text Level;   // «УР 1 → УР 2» / «УР 3 · МАКС»
            public TMP_Text Effect;  // «+8% тяги» … — прибавка следующего уровня
            public Image[] Segments; // полоска уровня: 3 сегмента, заполнение = уровень
            public Button Buy;
            public TMP_Text BuyLabel;
        }

        // строка таблицы статов витрины (C2): подпись | полоска модели с риской
        // текущего значения | значение модели. Полоска: нейтральный fill —
        // рассматриваемый корабль, подкрашенный отрезок — разница с твоим
        // (зелёный — рассматриваемый лучше, красный — хуже), янтарная риска —
        // твоё текущее значение поверх всех слоёв
        private sealed class ShowStatRowRefs
        {
            public TMP_Text Label;
            public TMP_Text Value;
            public RectTransform ValueFill; // доля рассматриваемого корабля (0..1)
            public RectTransform DeltaFill; // отрезок между твоим и рассматриваемым
            public Image DeltaImage;
            public RectTransform Tick;      // риска текущего значения
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

        // блок «Улучшения» — свой метод-строитель (как долг ниже и ангар П3.2).
        // C3: у каждой линии — «УР N → УР N+1», прибавка следующего уровня,
        // цена «Đ» по-линейная (UpgradeConfig.GetPrice), кнопка КУПИТЬ и
        // полоска уровня из 3 сегментов
        private void BuildUpgradesBlock(Transform shop)
        {
            var section = StationUI.Section(shop, "// УЛУЧШЕНИЯ", "UpgradesSection");

            // аренда: строка-пояснение без кнопок
            upgradesRental = OnboardComputerFactory.Text(section,
                "Аренда всегда сток — улучшения только на свой корабль", 17f,
                OnboardComputerFactory.LabelColor, "RentalNote");
            OnboardComputerFactory.FixedHeight(upgradesRental.gameObject, 24f);

            // свой корабль: по строке на линию улучшений
            upgradesOwned = new GameObject("OwnedRows", typeof(RectTransform));
            upgradesOwned.transform.SetParent(section.transform, false);
            var rows = upgradesOwned.AddComponent<VerticalLayoutGroup>();
            rows.spacing = 10f;
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
                rowLayout.spacing = 4f;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = true;
                rowLayout.childForceExpandHeight = false;

                var top = OnboardComputerFactory.H("Top", rowGo.transform);
                OnboardComputerFactory.FixedHeight(top.gameObject, 34f);
                top.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                var caption = OnboardComputerFactory.Text(top, names[i], 17f,
                    OnboardComputerFactory.BrightColor, "Caption");
                OnboardComputerFactory.FixedWidth(caption.gameObject, 130f);
                OnboardComputerFactory.FixedHeight(caption.gameObject, 22f);
                var refs = new UpgradeRowRefs();
                refs.Level = OnboardComputerFactory.Text(top, "", 17f, OnboardComputerFactory.LabelColor, "Level");
                OnboardComputerFactory.FixedWidth(refs.Level.gameObject, 120f);
                OnboardComputerFactory.FixedHeight(refs.Level.gameObject, 22f);
                var spacer = OnboardComputerFactory.Text(top, "", 15f, OnboardComputerFactory.LabelColor, "Spacer");
                OnboardComputerFactory.Flexible(spacer.gameObject);
                refs.Buy = BuildRowButton(top.transform, () => BuyUpgrade(line), out refs.BuyLabel);

                refs.Segments = BuildLevelSegments(rowGo.transform);
                refs.Effect = OnboardComputerFactory.Text(rowGo, "", 15f, OnboardComputerFactory.LabelColor, "Effect");
                OnboardComputerFactory.FixedHeight(refs.Effect.gameObject, 20f);
                upgradeRows[i] = refs;
            }

            RefreshUpgrades();
        }

        // полоска прогресса уровней (C3): 3 сегмента на всю ширину строки,
        // заполнение = уровень (0..3) — цвета сегментов как в HUD
        private static Image[] BuildLevelSegments(Transform parent)
        {
            var row = new GameObject("LevelSegments", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            OnboardComputerFactory.FixedHeight(row.gameObject, 10f);

            var segments = new Image[ShipStats.MaxLevel];
            for (int i = 0; i < ShipStats.MaxLevel; i++)
            {
                var cell = new GameObject("Seg_" + (i + 1), typeof(RectTransform), typeof(Image));
                cell.transform.SetParent(row.transform, false);
                cell.AddComponent<LayoutElement>().flexibleWidth = 1f;
                segments[i] = cell.GetComponent<Image>();
                segments[i].color = SegmentOff;
                segments[i].raycastTarget = false;
            }
            return segments;
        }

        // компактная кнопка строки улучшения: каркасная StationUI.Button
        // (фон ButtonColor, BrightColor, hover) — «КУПИТЬ · Đ N»
        private static Button BuildRowButton(Transform parent, UnityEngine.Events.UnityAction onClick, out TMP_Text label)
        {
            var go = StationUI.Button(parent, "—", 15f, "BuyButton");
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 150f;
            le.preferredWidth = 150f;
            le.minHeight = 32f;
            le.preferredHeight = 32f;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            label = go.GetComponentInChildren<TMP_Text>();
            return button;
        }

        // уровни/цены — из конфига (по-линейные GetPrice, C3); аренда всегда
        // сток, ур.3 — без кнопки. UpgradesChanged после покупки перечитывает всё
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
            for (int i = 0; i < upgradeRows.Length; i++)
            {
                var row = upgradeRows[i];
                int level = fleet.GetLevel(fleet.Current, (UpgradeLine)i);
                bool maxed = level >= ShipStats.MaxLevel;
                row.Level.text = maxed ? $"УР {level} · МАКС" : $"УР {level} → УР {level + 1}";
                row.Effect.text = maxed ? "Максимальный уровень" : NextLevelEffect(upgrades, (UpgradeLine)i);
                SetLevelSegments(row.Segments, level);
                // цена следующего уровня — GetPrice линии (level + 1), как в TryUpgrade
                int price = upgrades.GetPrice(i, level + 1);
                row.BuyLabel.text = maxed
                    ? "МАКС."
                    : $"КУПИТЬ · {StationUI.Currency} {OnboardBinder.FormatCredits(price)}";
                row.Buy.interactable = !maxed && price > 0 && session.Wallet.Credits >= price;
            }
        }

        // заполнение полоски уровня: lit = level сегментов из трёх
        private static void SetLevelSegments(Image[] segments, int level)
        {
            for (int i = 0; i < segments.Length; i++)
                segments[i].color = i < level ? SegmentOn : SegmentOff;
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
            debtSection = StationUI.Section(shop, "// ДОЛГ", "DebtSection");
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

        // каркас витрины (C2): слева таблица статов + свотчи расцветки, справа
        // большая модель в выбранной расцветке; внизу навигация [←] имя [→],
        // подпись статуса владения и кнопка покупки
        private void BuildShowcaseBlock(Transform parent)
        {
            shopWindow = StationUI.Section(parent, "// МАГАЗИН", "ShopWindow");

            var showcaseRow = OnboardComputerFactory.H("ShowcaseRow", shopWindow.transform);

            // --- левая колонка: 6 статов + выбор расцветки ---
            var statsColumn = new GameObject("StatsColumn", typeof(RectTransform));
            statsColumn.transform.SetParent(showcaseRow.transform, false);
            var statsLayout = statsColumn.AddComponent<VerticalLayoutGroup>();
            statsLayout.spacing = 6f;
            statsLayout.childControlWidth = true;
            statsLayout.childControlHeight = true;
            statsLayout.childForceExpandWidth = true;
            statsLayout.childForceExpandHeight = false;
            var statsElement = statsColumn.AddComponent<LayoutElement>();
            statsElement.flexibleWidth = 1f;

            showcaseStatRows = new ShowStatRowRefs[StatCount];
            for (int i = 0; i < StatCount; i++)
                showcaseStatRows[i] = BuildStatRow(statsColumn.transform, i, StatLabels[i]);

            // выбор расцветки: свотчи (заводская + PaintVariants); клик меняет
            // большой спрайт, покупка берёт выбранный вариант (механика бывшего
            // FlipPaint — индекс живёт в showcasePaintIndex)
            var paintRow = OnboardComputerFactory.H("PaintRow", statsColumn.transform);
            OnboardComputerFactory.FixedHeight(paintRow.gameObject, 34f);
            paintRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            var paintCaption = OnboardComputerFactory.Text(paintRow, "РАСКРАВКА", 15f,
                OnboardComputerFactory.LabelColor, "PaintCaption");
            OnboardComputerFactory.FixedWidth(paintCaption.gameObject, StatLabelWidth);
            paintSwatchRow = OnboardComputerFactory.H("Swatches", paintRow.transform);
            paintSwatchRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            OnboardComputerFactory.Flexible(paintSwatchRow);

            showcasePaint = OnboardComputerFactory.Text(statsColumn, "", 15f,
                OnboardComputerFactory.LabelColor, "PaintLine");
            OnboardComputerFactory.FixedHeight(showcasePaint.gameObject, 20f);

            // --- правая колонка: большая модель ---
            var spriteColumn = new GameObject("SpriteColumn", typeof(RectTransform));
            spriteColumn.transform.SetParent(showcaseRow.transform, false);
            var spriteColumnLayout = spriteColumn.AddComponent<VerticalLayoutGroup>();
            spriteColumnLayout.spacing = 6f;
            spriteColumnLayout.childControlWidth = true;
            spriteColumnLayout.childControlHeight = true;
            spriteColumnLayout.childForceExpandWidth = true;
            spriteColumnLayout.childForceExpandHeight = false;
            var spriteElement = spriteColumn.AddComponent<LayoutElement>();
            spriteElement.minWidth = ShowcaseSpriteW;
            spriteElement.preferredWidth = ShowcaseSpriteW;
            spriteElement.flexibleWidth = 0f;

            var box = new GameObject("SpriteBox", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(spriteColumn.transform, false);
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

            // --- навигация (C2): компактные стрелки-каркасные кнопки (40×40,
            // меньше прежних 48×160), название с тиром по центру; «ВЫ ЗДЕСЬ»
            // убрано — статус владения подписью под названием ---
            var navRow = OnboardComputerFactory.H("NavBar", shopWindow.transform);
            OnboardComputerFactory.FixedHeight(navRow.gameObject, 44f);
            navRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            BuildNavArrow(navRow.transform, "←", () => FlipShowcase(-1));
            showcaseName = OnboardComputerFactory.Text(navRow, "", 21f,
                OnboardComputerFactory.BrightColor, "ModelName");
            OnboardComputerFactory.Flexible(showcaseName.gameObject);
            // 36, не 28: строка 21pt ≈ 27.7 — Ellipsis при меньшей высоте режет заголовок
            OnboardComputerFactory.FixedHeight(showcaseName.gameObject, 36f);
            showcaseName.alignment = TextAlignmentOptions.Center;
            BuildNavArrow(navRow.transform, "→", () => FlipShowcase(1));

            showcaseState = OnboardComputerFactory.Text(shopWindow, "", 15f,
                OnboardComputerFactory.TaxiColor, "StateLine");
            OnboardComputerFactory.FixedHeight(showcaseState.gameObject, 20f);
            showcaseState.alignment = TextAlignmentOptions.Center;

            shopBuy = BuildServiceButton(shopWindow.transform, "ShopBuyButton", ConfirmShopAction, out shopBuyLabel);
            shopStatus = OnboardComputerFactory.Text(shopWindow, "", 17f, OnboardComputerFactory.TaxiColor, "Status");
            OnboardComputerFactory.FixedHeight(shopStatus.gameObject, 23f);

            Catalog(); // сортировка по цене — до первой отрисовки
            RefreshShowcase();
        }

        // строка таблицы статов (C2): подпись | полоска | значение модели.
        // Полоска: дорожка → нейтральный fill модели → подкрашенный отрезок
        // разницы → янтарная риска текущего значения (поверх всех слоёв)
        private ShowStatRowRefs BuildStatRow(Transform parent, int index, string label)
        {
            var rowGo = OnboardComputerFactory.H("Stat_" + index, parent);
            OnboardComputerFactory.FixedHeight(rowGo.gameObject, 22f);

            var refs = new ShowStatRowRefs();
            refs.Label = OnboardComputerFactory.Text(rowGo, label, 15f, OnboardComputerFactory.LabelColor, "Label");
            OnboardComputerFactory.FixedWidth(refs.Label.gameObject, StatLabelWidth);
            OnboardComputerFactory.FixedHeight(refs.Label.gameObject, 22f);
            refs.Label.alignment = TextAlignmentOptions.Left;

            var bar = new GameObject("Bar", typeof(RectTransform));
            bar.transform.SetParent(rowGo.transform, false);
            var barElement = bar.AddComponent<LayoutElement>();
            barElement.flexibleWidth = 1f;
            var dim = OnboardComputerFactory.LabelColor;
            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(bar.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)track.transform);
            var trackImage = track.GetComponent<Image>();
            trackImage.color = new Color(dim.r, dim.g, dim.b, 0.12f);
            trackImage.raycastTarget = false;

            refs.ValueFill = BuildBarFill(track.transform, new Color(dim.r, dim.g, dim.b, 0.45f), "ValueFill");
            refs.DeltaFill = BuildBarFill(track.transform, dim, "DeltaFill");
            refs.DeltaImage = refs.DeltaFill.GetComponent<Image>();
            refs.Tick = BuildTick(track.transform);

            refs.Value = OnboardComputerFactory.Text(rowGo, "", 14f, OnboardComputerFactory.LabelColor, "Value");
            OnboardComputerFactory.FixedWidth(refs.Value.gameObject, StatValueWidth);
            OnboardComputerFactory.FixedHeight(refs.Value.gameObject, 22f);
            refs.Value.alignment = TextAlignmentOptions.Right;
            return refs;
        }

        // отрезок полоски между двумя долями (0..1): риски/подкраска разницы
        private static void SetSegment(RectTransform fill, float from, float to)
        {
            float a = Mathf.Clamp01(Mathf.Min(from, to));
            float b = Mathf.Clamp01(Mathf.Max(from, to));
            fill.anchorMin = new Vector2(a, 0f);
            fill.anchorMax = new Vector2(b, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }

        // риска-засечка текущего значения (C2): вертикальная полоска 3px,
        // ширину стата показывает от всей дорожки
        private static RectTransform BuildTick(Transform track)
        {
            var tick = new GameObject("Tick", typeof(RectTransform), typeof(Image));
            tick.transform.SetParent(track, false);
            var image = tick.GetComponent<Image>();
            image.color = OnboardComputerFactory.TaxiColor;
            image.raycastTarget = false;
            return (RectTransform)tick.transform;
        }

        private static void SetTick(RectTransform tick, float fraction)
        {
            float x = Mathf.Clamp01(fraction);
            tick.anchorMin = new Vector2(x, 0f);
            tick.anchorMax = new Vector2(x, 1f);
            tick.sizeDelta = new Vector2(3f, 0f); // высоту дают якоря, ширина — 3px
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

        // стрелка навигации витрины (C2): компактная каркасная StationUI.Button
        // 40×40 — меньше прежних 48×160, звук уже хукнут каркасом
        private static void BuildNavArrow(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = StationUI.Button(parent, label, 18f, "NavArrow");
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 40f;
            le.preferredWidth = 40f;
            le.minHeight = 40f;
            le.preferredHeight = 40f;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
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

        // свотчи расцветки собираются под модель: заводская (индекс 0) +
        // PaintVariants; старые уничтожаются — число вариантов у моделей разное
        private void RebuildPaintSwatches(ShipConfig config)
        {
            if (paintSwatchRow == null || config == null)
                return;
            for (int i = paintSwatchRow.transform.childCount - 1; i >= 0; i--)
                Destroy(paintSwatchRow.transform.GetChild(i).gameObject);
            int count = config.PaintVariantCount + 1;
            paintSwatchIcons = new Image[count];
            float size = count > 5 ? SwatchSizeSmall : SwatchSize;
            for (int i = 0; i < count; i++)
                paintSwatchIcons[i] = BuildPaintSwatch(config, i, size);
            UpdatePaintSwatches();
        }

        // свотч: спрайт варианта (арта нет — затенение по тиру), ColorTint hover,
        // звук хуком каркаса ПЕРВЫМ, рамка-Outline только у выбранного
        private Image BuildPaintSwatch(ShipConfig config, int paintIndex, float size)
        {
            var go = new GameObject("Swatch_" + paintIndex, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(paintSwatchRow.transform, false);
            var icon = go.GetComponent<Image>();
            Sprite sprite = config.GetPaintSprite(paintIndex);
            if (sprite != null)
            {
                icon.sprite = sprite;
                icon.preserveAspect = true;
            }
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.85f, 0.7f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = size;
            le.preferredWidth = size;
            le.minHeight = size;
            le.preferredHeight = size;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.fadeDuration = 0.1f;
            colors.highlightedColor = new Color(0.82f, 1f, 0.9f);
            colors.pressedColor = new Color(0.6f, 1f, 0.78f);
            button.colors = colors;
            UiSfx.Hook(button); // каркас: хук первым, слушатели вызывающего после
            int captured = paintIndex;
            button.onClick.AddListener(() => SelectShowcasePaint(captured));
            return icon;
        }

        // выбор расцветки свотчем (механика бывшего FlipPaint): большой спрайт
        // перерисовывается, покупка берёт выбранный индекс
        private void SelectShowcasePaint(int paintIndex)
        {
            ShipConfig config = showcaseShip;
            if (config == null || shopWindow == null || session == null || session.Fleet == null)
                return;
            if (paintIndex < 0 || paintIndex > config.PaintVariantCount)
                return;
            showcasePaintIndex = paintIndex;
            ApplyShowcaseSprite(session.Fleet, config, paintIndex);
            UpdatePaintSwatches();
            showcasePaint.text = PaintLineText(config, paintIndex);
        }

        // подсветка выбранного свотча: рамка + полный цвет, невыбранные серее
        private void UpdatePaintSwatches()
        {
            ShipConfig config = swatchConfig;
            if (paintSwatchIcons == null || config == null)
                return;
            for (int i = 0; i < paintSwatchIcons.Length; i++)
            {
                bool selected = i == showcasePaintIndex;
                var image = paintSwatchIcons[i];
                Color baseColor = image.sprite != null ? Color.white : TierShade(config.Tier);
                image.color = selected
                    ? baseColor
                    : new Color(baseColor.r * 0.62f, baseColor.g * 0.62f, baseColor.b * 0.62f, baseColor.a);
                var outline = image.GetComponent<Outline>();
                if (outline != null)
                    outline.enabled = selected;
            }
        }

        // витрина перечитывается на каждый RefreshShop (деньги/парк) и при
        // смене слота ротации из Update — показанный вариант меняется сам.
        // Ручной выбор расцветки (свотч) переживает перечитывание денег/парка:
        // сбрасывается только сменой модели или слота ротации
        private void RefreshShowcase()
        {
            ShipConfig[] catalog = Catalog();
            if (shopWindow == null || catalog.Length == 0
                || session == null || session.Fleet == null)
                return;
            var fleet = session.Fleet;
            ShipConfig config = catalog[showcaseIndex];
            bool modelChanged = showcaseShip != config;
            showcaseShip = config;
            int slot = ShopStockService.CurrentSlot(Time.time);
            bool slotChanged = slot != stockSlot;
            stockSlot = slot;
            if (modelChanged || slotChanged)
                showcasePaintIndex = ShopStockService.DisplayPaintIndex(config, slot);
            if (swatchConfig != config)
            {
                swatchConfig = config;
                RebuildPaintSwatches(config);
            }
            ApplyShowcaseSprite(fleet, config, showcasePaintIndex);

            // название по центру навигации: «ЯЙЦО · ТИР 2»; статус владения —
            // подпись под названием вместо бейджа «ВЫ ЗДЕСЬ» (C2)
            showcaseName.text = config.DisplayName.ToUpper() + " · ТИР " + config.Tier;
            showcaseState.text = OwnershipLine(fleet, config);
            UpdateShowcaseStats(config);
            UpdatePaintSwatches();
            showcasePaint.text = PaintLineText(config, showcasePaintIndex);

            bool rental = config.IsRental;
            bool current = IsCurrent(fleet, config);
            bool owned = fleet.IsOwned(config);
            bool canBuy = !owned && !rental && config.Price > 0;
            bool canSit = !current && (owned || rental);
            bool affordable = session.Wallet.Credits >= config.Price;
            shopBuy.interactable = canBuy ? affordable : canSit;
            shopBuyLabel.text = canBuy
                ? $"КУПИТЬ · {StationUI.Currency} {OnboardBinder.FormatCredits(config.Price)}"
                : canSit ? "ПЕРЕСЕСТЬ"
                : current ? "СЕЙЧАС АКТИВЕН"
                : "НЕТ В ПРОДАЖЕ";
            // недостача кредитов: кнопка гаснет + статус с нехваткой (C2)
            shopStatus.text = canBuy && !affordable
                ? $"Не хватает {StationUI.Currency} {OnboardBinder.FormatCredits(config.Price - session.Wallet.Credits)}"
                : "";
        }

        // подпись статуса владения под названием (C2): честно и коротко
        private static string OwnershipLine(FleetService fleet, ShipConfig config)
        {
            bool current = IsCurrent(fleet, config);
            if (current)
                return fleet.IsOwned(config)
                    ? "ВАШ · СЕЙЧАС АКТИВЕН"
                    : "В АРЕНДЕ · СЕЙЧАС АКТИВЕН";
            if (fleet.IsOwned(config))
                return "ВАШ · В АНГАРЕ";
            return config.IsRental ? "АРЕНДА" : "";
        }

        // спрайт витрины: показанный вариант покраски или заводской; арта нет
        // вовсе — заглушка-треугольник с затенением по тиру; «в ангаре» — серым.
        // Один источник для полного рефреша и листания покраски
        private void ApplyShowcaseSprite(FleetService fleet, ShipConfig config, int paintIndex)
        {
            Sprite sprite = config.GetPaintSprite(paintIndex);
            bool current = IsCurrent(fleet, config);
            bool owned = fleet.IsOwned(config);
            showcaseSprite.sprite = sprite != null ? sprite : StubSprites.ShipTriangle();
            showcaseSprite.color = sprite == null ? TierShade(config.Tier)
                : owned && !current ? new Color(0.62f, 0.62f, 0.62f, 0.9f) // «в ангаре» — серым
                : Color.white;
        }

        // строка «Расцветка: …» под именем модели на витрине
        private static string PaintLineText(ShipConfig config, int paintIndex)
        {
            int variants = config.PaintVariantCount;
            return variants == 0 ? "Расцветка: заводская"
                : paintIndex == 0
                    ? $"Расцветка: заводская · ещё {variants} в ротации"
                    : $"Расцветка: вариант {paintIndex} из {variants} (ротация 2 игрочаса)";
        }

        // значение стата модели по индексам StatLabels. ВСЕ статы «больше =
        // лучше» (C2): Retro сравнивается напрямую — тормозной путь с его
        // инверсией убран, вместо него «Мощность торможения». Retro=0 —
        // глобальный фолбэк из PhysicsConfig, как в полёте
        private float StatValue(ShipConfig config, int index)
        {
            switch (index)
            {
                case 0: return config.Thrust;
                case 1: return config.MaxV;
                case 2: return config.FuelMax;
                case 3: return config.HpMax;
                case 4: return RetroOf(config);
                default: return config.StrafeAcc;
            }
        }

        // модельное торможение или глобальное (ShipStats.Compute использует 80
        // как последний фолбэк — то же число здесь); 0 не отдаём
        private float RetroOf(ShipConfig config)
        {
            if (config != null && config.Retro > 0f)
                return config.Retro;
            float global = session != null && session.Config != null ? session.Config.Retro : 80f;
            return global > 0f ? global : 80f;
        }

        // «твоё текущее значение» для риски (C2): эффективные статы живого
        // корабля — улучшения учтены (тяга/бак/прочность/манёвр растут с
        // уровнями); MaxV улучшениями не меняется — из конфига текущей модели
        private float CurrentStatValue(int index)
        {
            ShipConfig baseline = session != null && session.Fleet != null ? session.Fleet.Current : null;
            if (ship == null)
                return baseline != null ? StatValue(baseline, index) : 0f;
            switch (index)
            {
                case 0: return ship.Eff.Thrust;
                case 1: return baseline != null ? baseline.MaxV : 0f;
                case 2: return ship.Eff.FuelMax;
                case 3: return ship.Eff.HpMax;
                case 4: return ship.Eff.Retro;
                default: return ship.Eff.Strafe;
            }
        }

        // масштаб полоски — общий максимум стата по всему каталогу и твоим
        // текущим значениям (посчитан в коде, C2): полоски всех моделей
        // сопоставимы между собой, риска не выходит за дорожку
        private float StatScale(int index, float value, float current)
        {
            float max = Mathf.Max(value, current);
            ShipConfig[] catalog = Catalog();
            for (int c = 0; c < catalog.Length; c++)
                if (catalog[c] != null)
                    max = Mathf.Max(max, StatValue(catalog[c], index));
            return max > 0f ? max : 1f;
        }

        // таблица статов (C2): полоска — значение рассматриваемой модели,
        // риска — твоё текущее, отрезок между ними подкрашен: зелёный — модель
        // лучше, красный — хуже (все статы «больше = лучше», инверсий нет)
        private void UpdateShowcaseStats(ShipConfig config)
        {
            if (showcaseStatRows == null || config == null || session == null || session.Fleet == null)
                return;
            for (int i = 0; i < showcaseStatRows.Length; i++)
            {
                float value = StatValue(config, i);
                float current = CurrentStatValue(i);
                float max = StatScale(i, value, current);
                float valueFraction = Mathf.Clamp01(value / max);
                float currentFraction = Mathf.Clamp01(current / max);

                var row = showcaseStatRows[i];
                SetFill(row.ValueFill, valueFraction);
                SetSegment(row.DeltaFill, currentFraction, valueFraction);
                SetTick(row.Tick, currentFraction);

                float diff = value - current;
                bool better = diff > 0.001f;
                bool worse = diff < -0.001f;
                row.DeltaImage.color = better ? OnboardComputerFactory.BrightColor
                    : worse ? OnboardComputerFactory.DangerColor
                    : OnboardComputerFactory.LabelColor;
                row.Value.text = Mathf.RoundToInt(value).ToString();
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
            // покупка показанного варианта: индекс покраски берём с витрины
            // (листание переписывает showcasePaintIndex), иначе «показано X,
            // куплено Y»
            int paintIndex = showcasePaintIndex;
            if (!session.Fleet.TryBuy(config, session.Wallet, paintIndex))
                return;
            Sound.Play(SoundId.PurchaseCash); // П9.3: касса на покупку корабля
            ShowToast($"Куплен «{config.DisplayName}» (−{price} кр)", emphasized: true);
            // купленной модели «нет в продаже» — витрина листается дальше,
            // сама модель остаётся в списке серой с подписью «ВАШ · В АНГАРЕ»
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
