using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Physics;

namespace SpaceTaxi.UI
{
    // Экран станции [B]: платная заправка/ремонт (1 кр = 1 ед топлива / 1 HP,
    // план §3.1). Доступен ТОЛЬКО стоя (State == Landed) на планете со станцией;
    // мир не ставится на паузу (как карта). Закрытие по B/Esc — ESC уступается
    // по той же договорённости, что и карта (NotifyStationOpened/Closed +
    // EscJustClosed). Секции будущего контента (магазин П3.2, улучшения/долг
    // П3.3) — коллапсируемый контейнер в layout, без заглушек. Контроллера в
    // сценах нет — появляется сам (стиль DismissalScreenController), панель
    // строится кодом при первом открытии.
    public class StationScreenController : MonoBehaviour
    {
        private const int SortingOrder = 100; // поверх HUD/паузы, ниже увольнения (200)
        private const int SegmentCount = 10;  // как в HUD

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

            // --- Закладка под будущие секции (магазин П3.2, улучшения/долг П3.3):
            // коллапсируемый контейнер в layout, без видимой заглушки ---
            var shop = new GameObject("ShopSection", typeof(RectTransform));
            shop.transform.SetParent(panel.transform, false);
            var shopLayout = shop.AddComponent<VerticalLayoutGroup>();
            shopLayout.spacing = 4f;
            shopLayout.childControlWidth = true;
            shopLayout.childControlHeight = true;
            shopLayout.childForceExpandWidth = true;
            shopLayout.childForceExpandHeight = false;
            shop.SetActive(false);

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
    }
}
