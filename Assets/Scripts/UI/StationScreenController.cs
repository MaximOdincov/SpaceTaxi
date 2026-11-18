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
    // пакет (S6: потолок визита убран, ремонт ограничен ТОЛЬКО деньгами);
    // цена = HP × множитель пакета × наценка тира сети (RepairTierSurchargeMul).
    // Правило тиров мастерских: T1 берёт
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
    // процентов UpgradeConfig, цена «Đ» — по классу корабля
    // (UpgradeConfig.GetPrice, 22_BALANCE_REWORK §1.9), полоска уровня из 3
    // сегментов; только на СВОЙ корабль.
    // Доступен ТОЛЬКО стоя
    // (State == Landed) на планете с услугами; мир не ставится на паузу (как
    // карта). Закрытие по B/Esc — ESC уступается по той же договорённости, что
    // и карта (NotifyStationOpened/Closed + EscJustClosed). Контроллера в
    // сценах нет — появляется сам (стиль DismissalScreenController), панель
    // строится кодом при первом открытии.
    public class StationScreenController : MonoBehaviour, IStationSectionHost
    {
        private const int SortingOrder = 100; // поверх HUD/паузы, ниже увольнения (200)
        private const int SegmentCount = 10;  // как в HUD

        // цвета сегментов — палитра в HudPalette (Н12.4); используются
        // и секцией улучшений (полоски уровней), поэтому internal
        internal static readonly Color SegmentOn = HudPalette.SegmentOn;
        internal static readonly Color SegmentOff = HudPalette.SegmentOff;

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
        private TMP_Text repairTierLine; // «Мастерская тир N · наценка ×K»
        private TMP_Text repairOffer;    // «Починка: +N HP · Đ M» или отказ по тиру корабля
        private Button repairButton;
        private TMP_Text repairButtonLabel;

        // окна-вкладки: видимость переключает SelectTab, состав — ApplyTabs
        private GameObject fuelWindow;
        private GameObject hullWindow;
        private GameObject upgradeWindow; // улучшения + гашение долга (§3.4/§3.2)

        // вкладки услуг (§3.3): полный набор только у хаба МТО
        private enum StationTab { Shop, Upgrades, Fuel, Repair }

        private readonly Dictionary<StationTab, Button> tabButtons = new Dictionary<StationTab, Button>();
        private readonly Dictionary<StationTab, Image> tabBackgrounds = new Dictionary<StationTab, Image>();
        private readonly Dictionary<StationTab, TMP_Text> tabLabels = new Dictionary<StationTab, TMP_Text>();
        private StationTab activeTab = StationTab.Fuel;
        private Planet currentPlanet; // планета, под которую собраны вкладки

        // секции экрана (шаг 7c): улучшения/долг и витрина — plain-классы,
        // строят свой UI в переданных родителях (BuildScreen)
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

        // public: IStationSectionHost — покупки только стоя на планете с услугами
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
            fuelStatus.text = $"Заправлено {units} ед за {UiFormat.Currency} {UiFormat.FormatCredits(cost)}";
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
                : $"Заправить: {selected} ед · {UiFormat.Currency} {UiFormat.FormatCredits(cost)}";
            fuelButton.interactable = !FuelOutage && selected > 0 && session.Wallet.Credits >= cost;
            fuelButtonLabel.text = FuelOutage
                ? "ТОПЛИВА НЕТ"
                : selected <= 0 ? "ВЫБЕРИТЕ ОБЪЁМ"
                : selected >= available
                    ? $"ЗАПРАВИТЬ ДО ПОЛНОГО · {UiFormat.Currency} {UiFormat.FormatCredits(cost)}"
                    : $"ЗАПРАВИТЬ · {UiFormat.Currency} {UiFormat.FormatCredits(cost)}";
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
        // на экране отказ текстом борткомпа. Кнопка = полный пакет (S6: потолок
        // визита убран — ремонт ограничен ТОЛЬКО деньгами); цена = HP ×
        // множитель пакета × наценка тира сети (RepairTierSurchargeMul, С4) ---

        // тир корабля таксиста для правила тиров мастерских (FleetService.Current)
        private int ShipTier
        {
            get
            {
                var config = session != null && session.Fleet != null ? session.Fleet.Current : null;
                return config != null ? config.Tier : 2;
            }
        }

        // тир сети, круг тиров и предложение — при каждом Refresh:
        // ремонт, урон, деньги
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

            // полный пакет от текущего недостающего (S6: потолок визита убран —
            // ремонт ограничен ТОЛЬКО деньгами)
            float amount = RepairNetwork.RepairAmount(ship.Eff.HpMax, ship.Hp,
                RepairNetwork.PackCount - 1, tier, Pricing);
            int cost = RepairNetwork.CostOf(amount, RepairNetwork.PackCount - 1, tier, Pricing);

            repairOffer.text = $"Починка: +{Mathf.RoundToInt(amount)} HP · {UiFormat.Currency} {UiFormat.FormatCredits(cost)}";
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
                RepairNetwork.PackCount - 1, tier, Pricing);
            int cost = RepairNetwork.CostOf(amount, RepairNetwork.PackCount - 1, tier, Pricing);
            if (amount <= 0.01f || cost <= 0 || !session.Wallet.TrySpend(cost))
                return;
            hull.Repair(amount); // HpChanged дойдёт до HUD и до Refresh — экран пересчитается
            int pct = Mathf.RoundToInt(amount / missing * 100f);
            hullStatus.text = $"Отремонтировано на {pct}% (+{Mathf.RoundToInt(amount)} HP) за {UiFormat.Currency} {UiFormat.FormatCredits(cost)}";
            ShowToast($"Отремонтировано на {pct}%", tone: ToastTone.Danger); // Н6 U4: списание — красный
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
            }

            stationTitle.text = "СТАНЦИЯ · " + ship.LandedOn.DisplayName.ToUpper();
            creditsValue.text = UiFormat.Currency + " " + UiFormat.FormatCredits(session.Wallet.Credits);

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
            showcaseSection?.SetWindowVisible(tab == StationTab.Shop);

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
            // C2: панель шире (700 → 800, S3 — витрина крупнее): слева статы,
            // справа большая модель
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
            upgradesSection = new StationUpgradesSection(this);
            upgradesSection.Build(upgradeWindow.transform);

            // --- Вкладка МАГАЗИН: витрина (Н-3а) — большая модель на экране,
            // листание стрелками, покупка показанного варианта покраски ---
            showcaseSection = new StationShowcaseSection(this);
            showcaseSection.Build(panel.transform);

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

        // ширина fill-полоски по доле значения от максимума; internal —
        // вызывается секцией витрины (шаг 7c)
        internal static void SetFill(RectTransform fill, float fraction)
        {
            fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
        }

        // internal: вызывается секцией витрины (шаг 7c)
        internal static Button BuildServiceButton(Transform parent, string name, UnityEngine.Events.UnityAction onClick, out TMP_Text label)
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

        // магазинные перечитывания уходят в секции (шаг 7c):
        // улучшения/долг — StationUpgradesSection, витрина — StationShowcaseSection
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
