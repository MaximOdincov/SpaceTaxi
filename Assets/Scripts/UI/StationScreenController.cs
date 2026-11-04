using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;
using SpaceTaxi.Physics;

namespace SpaceTaxi.UI
{
    // Экран станции [B]: платная заправка/ремонт (1 кр = 1 ед топлива / 1 HP,
    // план §3.1), магазин кораблей (П3.2, секция АНГАР), улучшения текущего
    // корабля (§3.4) и досрочное гашение долга (§3.2) — блоки секции магазина.
    // Доступен ТОЛЬКО стоя (State == Landed) на планете со станцией; мир не
    // ставится на паузу (как карта). Закрытие по B/Esc — ESC уступается по той
    // же договорённости, что и карта (NotifyStationOpened/Closed +
    // EscJustClosed). Контроллера в сценах нет — появляется сам (стиль
    // DismissalScreenController), панель строится кодом при первом открытии.
    public class StationScreenController : MonoBehaviour
    {
        private const int SortingOrder = 100; // поверх HUD/паузы, ниже увольнения (200)
        private const int SegmentCount = 10;  // как в HUD
        // подпись счётчика аварий (§3.2: увольнение после третьей непогашенной)
        private const int MaxUnpaidAccidents = 3;

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
        private TMP_Text serviceStatus;
        private TMP_Text fuelButtonLabel;
        private TMP_Text hullButtonLabel;
        private Image[] fuelSegments;
        private Image[] hullSegments;
        private Button fuelButton;
        private Button hullButton;

        // блоки магазина: улучшения/долг (П3.3), ангар (П3.2)
        private GameObject upgradesOwned;   // 4 строки линий (свой корабль)
        private TMP_Text upgradesRental;    // строка-пояснение (аренда всегда сток)
        private UpgradeRowRefs[] upgradeRows;
        private GameObject debtSection;     // виден только при долге > 0
        private TMP_Text debtLine;
        private Button repayButton;
        private TMP_Text repayLabel;
        // ангар: каталог читается из Resources один раз, карточки строятся
        // один раз при первом открытии — дальше только перечитываются
        private ShipConfig[] shipCatalog;
        private ShipCardRefs[] shipCards;
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
                && ship.LandedOn.HasStation;
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
            serviceStatus.text = "";
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

        // 1 кр = 1 ед топлива (план §3.1); при нехватке кредитов — частичная заправка
        private void Refuel()
        {
            if (!CanOpen() || session == null)
                return;
            int cost = FuelCost();
            if (cost <= 0)
                return;
            int units = Mathf.Min(cost, session.Wallet.Credits);
            if (units <= 0 || !session.Wallet.TrySpend(units))
                return;
            ship.Fuel += units; // сеттер шлёт FuelChanged — HUD и экран обновятся
            serviceStatus.text = $"Заправлено {units} ед за {units} кр"
                + (units < cost ? " — хватило только на часть" : "");
            landing.ReportServiced(ship.LandedOn.DisplayName);
        }

        // 1 кр = 1 HP (план §3.1); аналогично с частичным ремонтом
        private void Repair()
        {
            if (!CanOpen() || session == null)
                return;
            int cost = HullCost();
            if (cost <= 0)
                return;
            int units = Mathf.Min(cost, session.Wallet.Credits);
            if (units <= 0 || !session.Wallet.TrySpend(units))
                return;
            hull.Repair(units); // HpChanged дойдёт до HUD
            serviceStatus.text = $"Отремонтировано {units} HP за {units} кр"
                + (units < cost ? " — хватило только на часть" : "");
            landing.ReportServiced(ship.LandedOn.DisplayName);
        }

        // N = ceil(FuelMax − Fuel) — округление вверх: недостающая доля ед. тоже платная
        private int FuelCost() => Mathf.CeilToInt(ship.Eff.FuelMax - ship.Fuel);

        private int HullCost() => Mathf.CeilToInt(ship.Eff.HpMax - ship.Hp);

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

            stationTitle.text = "СТАНЦИЯ · " + ship.LandedOn.DisplayName.ToUpper();
            creditsValue.text = session.Wallet.Credits + " кр";

            UpdateGauge(fuelSegments, ship.FuelFraction);
            fuelValue.text = Mathf.RoundToInt(ship.Fuel) + "/" + Mathf.RoundToInt(ship.Eff.FuelMax);
            UpdateGauge(hullSegments, ship.HpFraction);
            hullValue.text = Mathf.RoundToInt(ship.Hp) + "/" + Mathf.RoundToInt(ship.Eff.HpMax);

            int fuelCost = FuelCost();
            int hullCost = HullCost();
            bool solvent = session.Wallet.Credits > 0;
            fuelButtonLabel.text = fuelCost > 0 ? $"ЗАПРАВИТЬ ДО ПОЛНОГО ({fuelCost} кр)" : "БАК ПОЛОН";
            fuelButton.interactable = fuelCost > 0 && solvent;
            hullButtonLabel.text = hullCost > 0 ? $"РЕМОНТ ДО ПОЛНОГО ({hullCost} кр)" : "КОРПУС ЦЕЛ";
            hullButton.interactable = hullCost > 0 && solvent;

            RefreshShop();
        }

        private void UpdateGauge(Image[] segments, float fraction)
        {
            int lit = Mathf.CeilToInt(fraction * segments.Length);
            for (int i = 0; i < segments.Length; i++)
                segments[i].color = i < lit ? SegmentOn : SegmentOff;
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
            panelRect.sizeDelta = new Vector2(560f, 0f); // высоту подберёт fitter
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

            // --- Секция СЕРВИС ---
            var service = Section(panel.transform, "// СЕРВИС", "ServiceSection");
            var fuelGauge = BuildGauge(service, "ТОПЛИВО", "Fuel");
            fuelSegments = fuelGauge.Segments;
            fuelValue = fuelGauge.Value;
            var hullGauge = BuildGauge(service, "КОРПУС", "Hull");
            hullSegments = hullGauge.Segments;
            hullValue = hullGauge.Value;

            serviceStatus = OnboardComputerFactory.Text(service, "", 17f, OnboardComputerFactory.LabelColor, "Status");
            OnboardComputerFactory.FixedHeight(serviceStatus.gameObject, 22f);

            fuelButton = BuildServiceButton(service.transform, "FuelButton", Refuel, out fuelButtonLabel);
            hullButton = BuildServiceButton(service.transform, "HullButton", Repair, out hullButtonLabel);

            // --- Секция магазина (ShopSection): улучшения/долг (П3.3) и ангар
            // (П3.2) — каждый блок отдельный метод-строитель. Девять карточек
            // ангара не влезают в экран целиком — блок живёт в скролле ---
            var shopWrap = new GameObject("ShopScroll", typeof(RectTransform), typeof(ScrollRect));
            shopWrap.transform.SetParent(panel.transform, false);
            var wrapElement = shopWrap.AddComponent<LayoutElement>();
            // высота окна скролла от высоты экрана: остальное — сервис и шапка
            wrapElement.preferredHeight = Mathf.Clamp(Screen.height * 0.45f, 260f, 560f);
            wrapElement.flexibleHeight = 0f;
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewport.transform.SetParent(shopWrap.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)viewport.transform);
            // прозрачный фон — raycast-цель под курсором, иначе колесо мыши не скроллит
            viewport.GetComponent<Image>().color = Color.clear;

            var shop = new GameObject("ShopSection", typeof(RectTransform));
            shop.transform.SetParent(viewport.transform, false);
            var shopRect = (RectTransform)shop.transform;
            // контент растёт вниз от верхней кромки вьюпорта — стандартная схема ScrollRect
            shopRect.anchorMin = new Vector2(0f, 1f);
            shopRect.anchorMax = new Vector2(1f, 1f);
            shopRect.pivot = new Vector2(0.5f, 1f);
            shopRect.offsetMin = Vector2.zero;
            shopRect.offsetMax = Vector2.zero;
            var shopLayout = shop.AddComponent<VerticalLayoutGroup>();
            shopLayout.spacing = 12f;
            shopLayout.childControlWidth = true;
            shopLayout.childControlHeight = true;
            shopLayout.childForceExpandWidth = true;
            shopLayout.childForceExpandHeight = false;
            shop.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = shopWrap.GetComponent<ScrollRect>();
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = shopRect;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            BuildUpgradesBlock(shop.transform);
            BuildDebtBlock(shop.transform);
            BuildHangarBlock(shop.transform);
            shop.SetActive(true); // в контейнере теперь есть содержимое

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
            public Button Buy;
            public TMP_Text BuyLabel;
        }

        private void RefreshShop()
        {
            RefreshUpgrades();
            RefreshDebt();
            RefreshHangar();
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
                // цена следующего уровня — PricesPerLevel[текущий уровень], как в TryUpgrade
                int price = prices != null && level < prices.Length ? prices[level] : 0;
                row.BuyLabel.text = maxed ? "МАКС." : $"КУПИТЬ ({price} КР)";
                row.Buy.interactable = !maxed && price > 0 && session.Wallet.Credits >= price;
            }
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
            fleet.TryUpgrade(fleet.Current, line, session.Wallet);
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
            ShowToast("Долг погашен", emphasized: true);
        }

        // --- П3.2: блок «Ангар» — каталог моделей, покупка и пересадка ---

        private sealed class ShipCardRefs
        {
            public ShipConfig Config;
            public Image Thumb;
            public TMP_Text Title;
            public TMP_Text Status;
            public TMP_Text Stats;
            public Button Buy;
            public TMP_Text BuyLabel;
            public Button Sit;
            public TMP_Text SitLabel;
        }

        // каталог читается из Resources один раз; порядок — по тиру (при равенстве
        // по цене и имени, чтобы список не плясал)
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
                int byTier = a.Tier.CompareTo(b.Tier);
                if (byTier != 0)
                    return byTier;
                int byPrice = a.Price.CompareTo(b.Price);
                return byPrice != 0 ? byPrice : string.CompareOrdinal(a.DisplayName, b.DisplayName);
            });
            shipCatalog = configs;
            return shipCatalog;
        }

        private void BuildHangarBlock(Transform shop)
        {
            var section = Section(shop, "// АНГАР", "HangarSection");
            var catalog = Catalog();
            shipCards = new ShipCardRefs[catalog.Length];
            for (int i = 0; i < catalog.Length; i++)
                shipCards[i] = BuildShipCard(section.transform, catalog[i]);
            RefreshHangar();
        }

        // карточка модели: [миниатюра | название/статус | кнопка] + строка статов
        private ShipCardRefs BuildShipCard(Transform parent, ShipConfig config)
        {
            var card = new GameObject("Card_" + config.Id, typeof(RectTransform), typeof(Image));
            card.transform.SetParent(parent, false);
            card.GetComponent<Image>().color = OnboardComputerFactory.PanelColor;
            var cardLayout = card.AddComponent<VerticalLayoutGroup>();
            cardLayout.padding = new RectOffset(10, 10, 8, 8);
            cardLayout.spacing = 4f;
            cardLayout.childControlWidth = true;
            cardLayout.childControlHeight = true;
            cardLayout.childForceExpandWidth = true;
            cardLayout.childForceExpandHeight = false;

            var refs = new ShipCardRefs { Config = config };

            var top = OnboardComputerFactory.H("Top", card.transform);
            // миниатюра — слот под спрайт корабля: пока треугольник-заглушка
            // с затенением по тиру, реальная графика придёт с артом А1/А2
            var thumbGo = new GameObject("Thumb", typeof(RectTransform), typeof(Image));
            thumbGo.transform.SetParent(top.transform, false);
            refs.Thumb = thumbGo.GetComponent<Image>();
            refs.Thumb.sprite = StubSprites.ShipTriangle();
            refs.Thumb.preserveAspect = true;
            OnboardComputerFactory.FixedSize(thumbGo, 40f, 40f);

            var info = new GameObject("Info", typeof(RectTransform));
            info.transform.SetParent(top.transform, false);
            var infoLayout = info.AddComponent<VerticalLayoutGroup>();
            infoLayout.spacing = 2f;
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = true;
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = false;
            var infoElement = info.AddComponent<LayoutElement>();
            infoElement.flexibleWidth = 1f;
            infoElement.minWidth = 100f;

            refs.Title = OnboardComputerFactory.Text(info, config.DisplayName, 18f, OnboardComputerFactory.BrightColor, "Title");
            refs.Status = OnboardComputerFactory.Text(info, "", 15f, OnboardComputerFactory.LabelColor, "Status");

            refs.Buy = BuildRowButton(top.transform, () => BuyShip(config), out refs.BuyLabel);
            refs.Sit = BuildRowButton(top.transform, () => SitIn(config), out refs.SitLabel);

            refs.Stats = OnboardComputerFactory.Text(card, "", 14f, OnboardComputerFactory.LabelColor, "Stats");
            OnboardComputerFactory.FixedHeight(refs.Stats.gameObject, 20f);
            return refs;
        }

        // «Макс» — потолок скорости MaxV (тяга/манёвр/бак/HP — из ShipConfig)
        private static string ShipStatsLine(ShipConfig config)
        {
            return "Тяга " + Mathf.RoundToInt(config.Thrust)
                + " · Манёвр " + Mathf.RoundToInt(config.StrafeAcc)
                + " · Бак " + Mathf.RoundToInt(config.FuelMax)
                + " · HP " + Mathf.RoundToInt(config.HpMax)
                + " · Макс " + Mathf.RoundToInt(config.MaxV);
        }

        // статусы/кнопки перечитываются на каждый RefreshShop: смену корабля
        // приносит ShipChanged, доступность покупки по деньгам — CreditsChanged
        private void RefreshHangar()
        {
            if (shipCards == null || session == null || session.Fleet == null)
                return;
            var fleet = session.Fleet;
            int credits = session.Wallet.Credits;
            foreach (var card in shipCards)
            {
                var config = card.Config;
                bool rental = config.IsRental;
                bool owned = fleet.IsOwned(config);
                bool current = IsCurrent(fleet, config);

                card.Title.text = config.DisplayName + " · Т" + config.Tier;
                card.Thumb.color = TierShade(config.Tier);
                card.Stats.text = ShipStatsLine(config);
                card.Status.text = current ? "В полёте (текущий)"
                    : owned ? "В ангаре"
                    : rental ? "Аренда"
                    : config.Price + " кр";

                bool canBuy = !owned && !rental && config.Price > 0;
                card.Buy.gameObject.SetActive(canBuy);
                if (canBuy)
                {
                    card.BuyLabel.text = $"КУПИТЬ ({config.Price} КР)";
                    card.Buy.interactable = credits >= config.Price;
                }

                bool canSit = !current && (owned || rental);
                card.Sit.gameObject.SetActive(canSit);
                if (canSit)
                    card.SitLabel.text = "СЕСТЬ В";
            }
        }

        // «Яйцо» из фолбэка (инстанс в памяти) и ассет egg_rental — одна модель:
        // до первой пересадки Current не совпадает по ссылке с карточкой аренды
        private static bool IsCurrent(FleetService fleet, ShipConfig config)
        {
            if (fleet.Current == config)
                return true;
            return config != null && config.IsRental
                && fleet.Current != null && fleet.Current.IsRental;
        }

        // затенение миниатюры по тиру: Т1 — приглушённая, Т5 — яркая
        private static Color TierShade(int tier)
        {
            float t = Mathf.Clamp01((tier - 1) / 4f);
            var dim = OnboardComputerFactory.LabelColor;
            dim.a = 0.4f;
            return Color.Lerp(dim, OnboardComputerFactory.BrightColor, t);
        }

        // правила покупки — в FleetService.TryBuy: не-аренда, Price > 0, не куплен;
        // после оплаты TryBuy сразу пересаживает (полные HP/бак), ShipChanged
        // перерисует карточки сам
        private void BuyShip(ShipConfig config)
        {
            if (session == null || session.Fleet == null || config == null || !CanOpen())
                return;
            int price = config.Price;
            if (!session.Fleet.TryBuy(config, session.Wallet))
                return;
            ShowToast($"Куплен «{config.DisplayName}» (−{price} кр)", emphasized: true);
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
