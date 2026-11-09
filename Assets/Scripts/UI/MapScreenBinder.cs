using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SpaceTaxi.Audio;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Карта (M) — тактический планшет в палитре борткомпа (OnboardComputerFactory):
    // тёмный экран, тонкие зелёные рамки, тела простыми фигурами (никакого
    // реализма — читаемость). Вид: автоподгон «весь мир» при открытии, ЛКМ-драг —
    // пан, колесо мыши / кнопки +− — зум (к колесу — к курсору, кнопки — к центру).
    // Клик по телу — инфо-панель: имя/тип/услуги/топливо (цена сети района или
    // «топлива нет» у сухих заправок Н4.5)/дистанция; повторный клик или клик мимо
    // закрывают. Подписи разводит ResolveLabels без наложений. Игрок — жёлтый
    // шеврон (Art/icons/Шеврон.png, перекраска белого как в WaypointIndicator):
    // размер не зависит от зума, за краем вида клампится к границе с указателем.
    // Цель заказа — жёлтое пульсирующее кольцо (стиль TargetRing радара).
    public class MapScreenBinder : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private TMP_FontAsset mapFont; // назначает тул Space Taxi/Fonts/Create TMP Asset

        // — вид: базовый масштаб = весь мир (bbox тел + поля вписан в экран), дальше зум —
        private const float MinViewZoom = 1f;      // из базового вида не отдаляемся
        private const float MaxViewZoom = 14f;     // от «весь мир» до одной планеты крупно
        private const float ClickZoom = 6.5f;      // клик по объекту поднимает вид до этого зума
        private const float ZoomDuration = 0.22f;
        private const float WheelStep = 1.2f;
        private const float ButtonZoomStep = 1.35f; // шаг кнопок +/−
        private const float ClickDragGuard = 6f;   // сдвиг больше этого — пан, а не выбор объекта
        private const float FitMargin = 1.10f;     // поля 10%: bbox тел занимает 90% экрана
        private const float LabelFontSize = 13f;   // кегль подписей тел
        private const float LabelHoverFont = 16f;
        private const float RouteDashPeriod = 15f; // шаг пунктира маршрута, экранных px
        private const int RouteDashMax = 80;
        private const float ViewInset = 18f;       // отступ клампа маркера игрока от края, px
        private const float RubberMargin = 1.15f;  // мягкий вынос экрана за край при перетаскивании

        // — waypoint для Н-8: метка игрока на объекте, свойство + событие —
        public CelestialBodyData Waypoint { get; private set; }
        public event Action WaypointChanged;

        public void SetWaypoint(CelestialBodyData body)
        {
            if (Waypoint == body)
                return;
            Waypoint = body;
            RefreshWaypointPin();
            WaypointChanged?.Invoke();
            RefreshMarkButton();
        }

        public void ClearWaypoint()
        {
            if (Waypoint == null)
                return;
            Waypoint = null;
            RefreshWaypointPin();
            WaypointChanged?.Invoke();
            RefreshMarkButton();
        }

        public void ToggleWaypoint(CelestialBodyData body)
        {
            if (Waypoint == body)
                ClearWaypoint();
            else
                SetWaypoint(body);
        }

        // Esc из контроллера: true — съеден панелью, false — карта закрывается целиком
        public bool TryHandleEscape()
        {
            if (selectedBody == null)
                return false;
            Deselect();
            return true;
        }

        // — палитра борткомпа (Н4.6.2): один источник стиля с OnboardComputerFactory —
        private static readonly Color Ink = new Color(OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g, OnboardComputerFactory.LabelColor.b);
        private static readonly Color InkBright = OnboardComputerFactory.BrightColor;
        private static readonly Color Taxi = OnboardComputerFactory.TaxiColor;
        private static readonly Color Danger = OnboardComputerFactory.DangerColor;
        private static readonly Color Panel = new Color(0.031f, 0.078f, 0.047f, 0.97f);
        private static readonly Color Night = new Color(0.008f, 0.022f, 0.013f, 0.99f);
        private static readonly Color ButtonBack = OnboardComputerFactory.ButtonColor;
        private static readonly Color StarColor = new Color(1f, 0.78f, 0.32f);
        private static readonly Color HoleRing = new Color(0.62f, 0.45f, 0.95f);
        private static readonly Color HoleCore = new Color(0.02f, 0.012f, 0.035f);
        private static readonly Color BeltColor = new Color(0.5f, 0.72f, 0.58f, 0.55f);
        private static readonly Color RepairColor = new Color(0.7f, 0.82f, 0.95f);
        private static readonly Color CometColor = Comet.CometColor;
        private static readonly Color WaypointColor = new Color(1f, 0.72f, 0.25f);

        // цвет тела по району (если автор не задал свой): различимые оттенки на тёмном
        private static readonly Vector3[] sectorColors =
        {
            new Vector3(0.56f, 0.86f, 0.64f), // Hub
            new Vector3(0.45f, 0.80f, 0.78f), // Near
            new Vector3(0.85f, 0.80f, 0.45f), // Mid1
            new Vector3(0.86f, 0.62f, 0.42f), // Mid2
            new Vector3(0.62f, 0.58f, 0.85f), // Far
        };

        private LevelAsset level;
        private GameSession session;
        private PricingConfig pricing; // цена топлива сети района (Н4.5)
        private HashSet<string> outageNames; // сухие заправки уровня — FuelNetwork.OutageStations
        private Ship ship;
        private GravityField gravity; // живые радиусы тел на hover и флаг FuelOutage (лениво)
        private RectTransform mapRect;
        private TMP_FontAsset font;
        private bool built;
        private float builtWidth;
        private Camera canvasCam; // null для Screen Space Overlay — так и нужно утилитам

        private RectTransform sheetRect;   // «экран планшета»: маска содержимого
        private RectTransform content;     // зум-контейнер: всё, что на планшете
        private GameObject noteRoot;       // инфо-панель объекта
        private RectTransform legendRoot;
        private RectTransform compassRoot;
        private RectTransform rulerRoot;
        private RectTransform zoomRoot;    // кнопки +/−/сброс

        private readonly Dictionary<CelestialBodyData, RectTransform> markers = new Dictionary<CelestialBodyData, RectTransform>();
        private readonly Dictionary<CelestialBodyData, float> bodySizes = new Dictionary<CelestialBodyData, float>();

        private RectTransform playerMarker;
        private RectTransform playerArrowRoot; // вращается по курсу; подпись «ВЫ» не крутится
        private RectTransform targetRing;
        private Image targetRingImage;
        private TMP_Text targetLabel; // «ЦЕЛЬ: <имя>» — видна с первого взгляда
        private RectTransform waypointPin;
        private int targetIndex = -1;
        private CelestialBodyData lastTargetBody; // чья подпись спрятана под «ЦЕЛЬ: …»
        private float playerHeading;

        // маршрут активного заказа: лёгкая пунктирная дуга корабль → пассажир/цель
        private RectTransform routeRoot;
        private readonly List<RectTransform> routeDashes = new List<RectTransform>();
        private readonly Vector2[] routePts = new Vector2[33];
        private readonly float[] routeCum = new float[33]; // накопленная длина ломаной
        private bool routeShown;

        // hover по телу: круги радиусов посадки/захвата + имя крупнее
        private RectTransform hoverCaptureRing;
        private RectTransform hoverLandingRing;
        private readonly Dictionary<CelestialBodyData, TMP_Text> bodyLabels = new Dictionary<CelestialBodyData, TMP_Text>();

        // подписи тел: раскладка без наложений
        private sealed class MapLabel
        {
            public RectTransform Rect;
            public float Width;   // оценка ширины текста, px экрана
            public Vector2 Below; // позиция под кругом
            public float AboveY;  // y позиции над кругом
        }
        private readonly List<MapLabel> mapLabels = new List<MapLabel>();

        // живые кометы (по ActiveComets CometSystem — как точки миникарты)
        private CometSystem cometSystem;
        private readonly Dictionary<Comet, RectTransform> cometIcons = new Dictionary<Comet, RectTransform>();
        private readonly Dictionary<Comet, Vector2> cometLastPos = new Dictionary<Comet, Vector2>();

        private Vector2 worldCenter; // центр карты = origin мира (граница HullSystem — от него)
        private float mapRadius;
        private float scale = 1f;

        private float zoom = 1f;
        private float zoomFrom = 1f;
        private float zoomTo = 1f;
        private float zoomT = 1f;
        private Vector2 zoomOffset;
        private Vector2 offFrom;
        private Vector2 offTo;
        private bool zoomAnim;

        // пан перетаскиванием: ЛКМ-драг по экрану; клик после драга не выбирает объект
        private bool dragging;
        private bool suppressClick;
        private float dragMoved;
        private Vector2 dragSheetPos;

        private CelestialBodyData selectedBody;
        private TMP_Text noteName;
        private TMP_Text noteType;
        private TMP_Text noteServices;
        private TMP_Text noteFuel;
        private TMP_Text noteDistance;
        private Button markButton;
        private Image markButtonImage;
        private TMP_Text markLabel;
        private float noteRefreshAt;

        private void Awake()
        {
            mapRect = (RectTransform)transform;
            Canvas canvas = GetComponentInParent<Canvas>();
            canvasCam = canvas != null ? canvas.worldCamera : null;

            TMP_FontAsset fallback = null;
            Transform title = transform.Find("Title");
            if (title != null)
                fallback = title.GetComponent<TMP_Text>().font;

            // заготовку из сцены стираем целиком — планшет рисуем кодом
            ClearChildren(transform);
            font = ResolveFont(fallback);
            BuildChrome();
        }

        // шрифт: тул/инспектор → JetBrains из HUD (как на радаре) → остаток заготовки
        private TMP_FontAsset ResolveFont(TMP_FontAsset fallback)
        {
            if (mapFont != null)
                return mapFont;
            HudBinder hud = FindAnyObjectByType<HudBinder>();
            if (hud != null)
            {
                Transform speed = hud.transform.Find("SpeedValue");
                TMP_Text text = speed != null ? speed.GetComponent<TMP_Text>() : null;
                if (text != null && text.font != null)
                    return text.font;
            }
            TMP_FontAsset res = Resources.Load<TMP_FontAsset>("Fonts/TMP/JetBrainsMono-Regular SDF");
            if (res != null)
                return res;
            res = Resources.Load<TMP_FontAsset>("Fonts/TMP/Excalifont SDF");
            if (res != null)
                return res;
            res = Resources.Load<TMP_FontAsset>("Excalifont SDF");
            return res != null ? res : fallback;
        }

        private void Update()
        {
            if (!built)
            {
                if (session == null)
                    session = FindAnyObjectByType<GameSession>();
                if (session == null || session.Level == null)
                    return;
                if (mapRect.rect.width < 100f) // ждём layout
                    return;
                level = session.Level;
                pricing = session.Orders != null ? session.Orders.Pricing : null;
                BuildMap();
                built = true;
                builtWidth = mapRect.rect.width;
                RestackChrome();
                return;
            }

            if (Mathf.Abs(mapRect.rect.width - builtWidth) > 2f)
            {
                RebuildAll();
                return;
            }

            UpdateZoomAnim();
            HandleWheel();
            UpdatePlayerMarker();
            UpdateTargetRing();
            UpdateRoute();
            UpdateComets();
            UpdateNoteDistance();
        }

        private void OnDisable()
        {
            // карта закрылась — вид и панель сбрасываем, следующий вход с чистого экрана
            selectedBody = null;
            if (noteRoot != null)
                noteRoot.SetActive(false);
            dragging = false;
            suppressClick = false;
            if (hoverCaptureRing != null)
                hoverCaptureRing.gameObject.SetActive(false);
            if (hoverLandingRing != null)
                hoverLandingRing.gameObject.SetActive(false);
            if (content != null)
                ApplyZoom(1f, Vector2.zero);
            zoomAnim = false;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        // ————— постройка каркаса (вне зум-контейнера) —————

        private void BuildChrome()
        {
            // тёмная подложка на весь экран; клик мимо планшета тоже снимает выбор
            RectTransform backdrop = MakeRect("Backdrop", mapRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image backImage = backdrop.gameObject.AddComponent<Image>();
            backImage.color = Night;
            backImage.raycastTarget = true;
            MapMarker backMarker = backdrop.gameObject.AddComponent<MapMarker>();
            backMarker.hoverEnabled = false;
            backMarker.Clicked += () =>
            {
                if (!suppressClick) // клик после пана — не «снять выбор»
                    Deselect();
            };

            // экран планшета: тёмно-зелёная панель + тонкая рамка
            sheetRect = MakeRect("Sheet", mapRect, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -8f));
            Image sheetImage = sheetRect.gameObject.AddComponent<Image>();
            sheetImage.color = Panel;
            sheetImage.raycastTarget = false; // клики сквозь экран уходят в Backdrop
            MakeFrameBars(sheetRect, WithAlpha(Ink, 0.55f), 2f);

            content = MakeRect("Content", sheetRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            content.gameObject.AddComponent<RectMask2D>(); // орбиты/пояса не вылезают за экран

            BuildInfoPanel();
            BuildLegend();
            BuildCompass();
            BuildRuler();
            BuildZoomButtons();
        }

        // рамка из четырёх полос (стиль OnboardComputerFactory.Frame)
        private static void MakeFrameBars(RectTransform parent, Color color, float thickness)
        {
            void Bar(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
            {
                RectTransform bar = MakeRect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
                Image img = bar.gameObject.AddComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
            }
            Bar("BorderTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -thickness), Vector2.zero);
            Bar("BorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, thickness));
            Bar("BorderLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(thickness, 0f));
            Bar("BorderRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-thickness, 0f), Vector2.zero);
        }

        // ————— постройка содержимого —————

        private void BuildMap()
        {
            // граница мира = кольцо буёв (HullSystem бросает «ПОТЕРЯНА СВЯЗЬ» от origin);
            // тела не должны вылезать, но подстрахуемся
            mapRadius = level.WorldRadius;
            float extX = 0f, extY = 0f; // bbox всех тел уровня — то, что обязано влезть
            foreach (CelestialBodyData body in level.Bodies)
            {
                float edge = body.Position.magnitude + body.Radius;
                if (edge > mapRadius)
                    mapRadius = edge + 100f;
                extX = Mathf.Max(extX, Mathf.Abs(body.Position.x) + body.Radius);
                extY = Mathf.Max(extY, Mathf.Abs(body.Position.y) + body.Radius);
            }
            if (level.CometAreaRadius > 0f) // зона комет — тоже часть содержимого
            {
                extX = Mathf.Max(extX, Mathf.Abs(level.CometAreaCenter.x) + level.CometAreaRadius);
                extY = Mathf.Max(extY, Mathf.Abs(level.CometAreaCenter.y) + level.CometAreaRadius);
            }
            if (extX <= 1f)
                extX = mapRadius;
            if (extY <= 1f)
                extY = mapRadius;
            worldCenter = Vector2.zero;

            // «весь мир на экране»: bbox тел уровня + поля 10% вписывается целиком
            float w = content.rect.width, h = content.rect.height;
            scale = Mathf.Min(w / (2f * extX * FitMargin), h / (2f * extY * FitMargin));
            scale = Mathf.Max(scale, 0.012f); // защита от вырожденно огромных уровней

            outageNames = FuelNetwork.OutageStations(level.Bodies, pricing); // детерминировано — совпадает с миром

            BuildGrid();
            BuildRouteLine();
            BuildSectors();
            BuildCometZone();
            BuildOrbits();
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type == BodyType.AsteroidBelt)
                    CreateBelt(body);
            BuildNetwork();
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type != BodyType.AsteroidBelt)
                    CreateBody(body);
            ResolveLabels();

            Sprite pinSprite = Diamond();
            waypointPin = MakeImage("WaypointPin", content, pinSprite, WaypointColor, new Vector2(16f, 16f), false);
            waypointPin.gameObject.SetActive(false);

            // hover-круги радиусов посадки/захвата — пунктир/кольцо поверх тел
            hoverCaptureRing = MakeImage("HoverCapture", content, DashRing(),
                WithAlpha(Ink, 0.5f), Vector2.zero, false);
            hoverCaptureRing.gameObject.SetActive(false);
            hoverLandingRing = MakeImage("HoverLanding", content, ThickRing(),
                WithAlpha(Taxi, 0.7f), Vector2.zero, false);
            hoverLandingRing.gameObject.SetActive(false);

            // цель заказа: жёлтое пульсирующее кольцо + подпись «ЦЕЛЬ: <имя>»
            targetRing = MakeRect("TargetRing", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            RectTransform ringImage = MakeImage("Ring", targetRing, ThickRing(), Taxi, Vector2.one * 46f, false);
            targetRingImage = ringImage.GetComponent<Image>();
            targetLabel = MakeLabel("TargetLabel", targetRing, "", 14f, Taxi);
            ((RectTransform)targetLabel.transform).sizeDelta = new Vector2(260f, 20f);
            targetLabel.fontStyle = FontStyles.Bold;
            targetRing.gameObject.SetActive(false);
            targetIndex = -1;

            // герой: жёлтый шеврон (арт Шеврон.png, перекраска как в WaypointIndicator)
            // + подпись «ВЫ»; размер не зависит от зума (UpdatePlayerMarker)
            playerMarker = MakeRect("PlayerMarker", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            playerMarker.sizeDelta = Vector2.one * 34f;
            playerArrowRoot = MakeRect("ArrowRoot", playerMarker, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Sprite chevArt = WaypointIndicator.ChevronSprite();
            MakeImage("Chevron", playerArrowRoot, chevArt != null ? chevArt : StubSprites.TargetChevron(),
                Taxi, Vector2.one * (chevArt != null ? 30f : 26f), false);

            RectTransform youChip = MakeImage("YouChip", playerMarker, null, ButtonBack, new Vector2(44f, 20f), false);
            youChip.anchoredPosition = new Vector2(0f, -30f);
            TMP_Text youLabel = MakeLabel("YouLabel", playerMarker, "ВЫ", 13f, Taxi);
            ((RectTransform)youLabel.transform).sizeDelta = new Vector2(44f, 20f);
            ((RectTransform)youLabel.transform).anchoredPosition = new Vector2(0f, -30f);
            youLabel.fontStyle = FontStyles.Bold;

            ApplyZoom(1f, Vector2.zero);
            RefreshWaypointPin();
        }

        // кольца дистанций от origin + крест направлений — мелкая сетка прибора
        private void BuildGrid()
        {
            MakeImage("GridCrossH", content, StubSprites.Line(), WithAlpha(Ink, 0.18f), new Vector2(46f, 1.6f), false);
            MakeImage("GridCrossV", content, StubSprites.Line(), WithAlpha(Ink, 0.18f), new Vector2(1.6f, 46f), false);
            MakeImage("GridCenter", content, StubSprites.Circle(64f), WithAlpha(Ink, 0.45f), Vector2.one * 4f, false);

            int[] steps = { 500, 1000, 2000, 5000, 10000, 20000, 50000 };
            int drawn = 0;
            foreach (int step in steps)
            {
                float px = step * scale;
                if (px < 70f || px > mapRadius * scale * 0.94f || drawn >= 4)
                    continue;
                RectTransform ring = MakeImage("GridRing_" + step, content, StubSprites.Ring(256),
                    WithAlpha(Ink, 0.10f), Vector2.one * (px * 2f), false);
                ring.anchoredPosition = Vector2.zero;
                drawn++;
            }
        }

        // районы: слабая заливка своим оттенком + рукописная подпись района
        private void BuildSectors()
        {
            var groups = new Dictionary<Sector, List<CelestialBodyData>>();
            foreach (CelestialBodyData body in level.Bodies)
            {
                if (body.Type == BodyType.AsteroidBelt || body.Type == BodyType.Comet)
                    continue;
                if (!groups.TryGetValue(body.Sector, out List<CelestialBodyData> list))
                    groups[body.Sector] = list = new List<CelestialBodyData>();
                list.Add(body);
            }

            foreach (var pair in groups)
            {
                Vector2 center = Vector2.zero;
                foreach (CelestialBodyData body in pair.Value)
                    center += body.Position;
                center /= pair.Value.Count;

                float radiusWorld = 350f; // минимум, чтобы заливка читалась
                foreach (CelestialBodyData body in pair.Value)
                    radiusWorld = Mathf.Max(radiusWorld, Vector2.Distance(center, body.Position) + 200f);

                Color tint = SectorColor(pair.Key);
                RectTransform blob = MakeImage("Sector_" + pair.Key, content, StubSprites.Circle(64f),
                    WithAlpha(tint, 0.045f), Vector2.one * radiusWorld * 2f * scale, false);
                blob.anchoredPosition = WorldToMap(center);

                TMP_Text label = MakeLabel("SectorLabel_" + pair.Key, content, SectorName(pair.Key), 16f,
                    WithAlpha(tint, 0.35f));
                ((RectTransform)label.transform).sizeDelta = new Vector2(240f, 24f);
                ((RectTransform)label.transform).anchoredPosition = WorldToMap(center) + Vector2.up * (radiusWorld * scale * 0.4f);
            }
        }

        private void BuildCometZone()
        {
            if (level.CometAreaRadius <= 0f) // хорды по всему миру — зону не рисуем
                return;
            float diameter = level.CometAreaRadius * 2f * scale;
            RectTransform ring = MakeImage("CometZone", content, DashRing(),
                WithAlpha(CometColor, 0.30f), Vector2.one * diameter, false);
            ring.anchoredPosition = WorldToMap(level.CometAreaCenter);

            RectTransform zoneIcon = MakeImage("CometZoneIcon", content, StubSprites.Circle(64f),
                WithAlpha(CometColor, 0.65f), Vector2.one * 9f, false);
            zoneIcon.anchoredPosition = WorldToMap(level.CometAreaCenter) + Vector2.down * (diameter * 0.5f - 16f);

            TMP_Text label = MakeLabel("CometZoneLabel", content, "КОМЕТНАЯ ЗОНА", 13f,
                WithAlpha(CometColor, 0.55f));
            ((RectTransform)label.transform).sizeDelta = new Vector2(220f, 20f);
            ((RectTransform)label.transform).anchoredPosition =
                WorldToMap(level.CometAreaCenter) + Vector2.up * (diameter * 0.5f + 14f);
        }

        // орбиты: пунктирные окружности вокруг звёзд, радиус = фактическая дистанция планет
        private void BuildOrbits()
        {
            var stars = new List<CelestialBodyData>();
            var planets = new List<CelestialBodyData>();
            foreach (CelestialBodyData body in level.Bodies)
            {
                if (body.Type == BodyType.Star)
                    stars.Add(body);
                else if (body.Type == BodyType.Planet)
                    planets.Add(body);
            }
            if (stars.Count == 0)
                return;

            var inkFaint = WithAlpha(Ink, 0.16f);
            foreach (CelestialBodyData star in stars)
            {
                var radii = new List<float>();
                foreach (CelestialBodyData planet in planets)
                {
                    if (NearestStar(planet, stars) != star)
                        continue;
                    float r = Vector2.Distance(planet.Position, star.Position);
                    if (r <= 1f || r > mapRadius)
                        continue;
                    bool duplicate = false;
                    foreach (float have in radii)
                        if (Mathf.Abs(have - r) < r * 0.04f)
                        {
                            duplicate = true;
                            break;
                        }
                    if (duplicate)
                        continue;
                    radii.Add(r);
                    RectTransform orbit = MakeImage("Orbit_" + star.DisplayName + radii.Count, content,
                        DashRing(), inkFaint, Vector2.one * r * 2f * scale, false);
                    orbit.anchoredPosition = WorldToMap(star.Position);
                }
            }
        }

        private CelestialBodyData NearestStar(CelestialBodyData body, List<CelestialBodyData> stars)
        {
            CelestialBodyData best = null;
            float bestDist = float.MaxValue;
            foreach (CelestialBodyData star in stars)
            {
                float d = Vector2.Distance(body.Position, star.Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = star;
                }
            }
            return best;
        }

        // сеть буёв по границе мира: пунктир + буи + «ПОТЕРЯ СВЯЗИ»
        private void BuildNetwork()
        {
            float netDiameter = mapRadius * 2f * scale;

            RectTransform ring = MakeImage("NetworkRing", content, DashRing(),
                WithAlpha(Ink, 0.5f), Vector2.one * netDiameter, false);
            ring.anchoredPosition = Vector2.zero;

            // сплошной обвод по границе мира — рамка «края системы»
            RectTransform frame = MakeImage("WorldFrame", content, ThickRing(),
                WithAlpha(Ink, 0.35f), Vector2.one * (netDiameter * 1.06f), false);
            frame.anchoredPosition = Vector2.zero;

            // буи: 12 ромбов по кругу
            Sprite buoySprite = Diamond();
            for (int i = 0; i < 12; i++)
            {
                float ang = i * 30f * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                RectTransform buoy = MakeImage("Buoy_" + i, content, buoySprite,
                    WithAlpha(InkBright, 0.75f), Vector2.one * 9f, false);
                buoy.anchoredPosition = dir * (mapRadius * scale);
            }

            var warnColor = WithAlpha(Danger, 0.75f);
            for (int i = 0; i < 4; i++) // диагонали — между буями, внутрь экрана помещаются
            {
                float ang = (45f + 90f * i) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                TMP_Text warn = MakeLabel("NoSignal_" + i, content, "ПОТЕРЯ СВЯЗИ", 12f, warnColor);
                ((RectTransform)warn.transform).sizeDelta = new Vector2(160f, 18f);
                ((RectTransform)warn.transform).anchoredPosition = dir * (mapRadius * scale + 22f);
            }
        }

        // ————— тела —————

        // тела простыми фигурами: планета — круг цвета тела с обводом, звезда — ядро
        // с 3 концентрическими ореолами, дыра — тёмный круг с ярким кольцом
        private void CreateBody(CelestialBodyData body)
        {
            bool star = body.Type == BodyType.Star;
            bool hole = body.Type == BodyType.BlackHole;
            float size = Mathf.Max(20f, body.Radius * 2f * scale * (star ? 1.6f : hole ? 1.0f : 1.9f));

            RectTransform group = MakeRect("Body_" + body.DisplayName, content, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            group.sizeDelta = Vector2.one * size;
            group.anchoredPosition = WorldToMap(body.Position);

            Image hit; // граф с raycast — по нему ловим клик/hover
            if (star) // ореол: 3 концентрических круга с падающей альфой
            {
                MakeImage("HaloOuter", group, StubSprites.Circle(64f), WithAlpha(StarColor, 0.08f), Vector2.one * size * 1.9f, false);
                MakeImage("HaloMid", group, StubSprites.Circle(64f), WithAlpha(StarColor, 0.15f), Vector2.one * size * 1.45f, false);
                MakeImage("HaloInner", group, StubSprites.Circle(64f), WithAlpha(StarColor, 0.28f), Vector2.one * size * 1.12f, false);
                RectTransform core = MakeImage("Core", group, StubSprites.Circle(64f), WithAlpha(StarColor, 0.95f), Vector2.one * size * 0.5f, true);
                hit = core.GetComponent<Image>();
            }
            else if (hole) // тёмный круг с ярким кольцом («горизонт»)
            {
                MakeImage("Halo", group, StubSprites.Circle(64f), WithAlpha(HoleRing, 0.12f), Vector2.one * size * 1.5f, false);
                MakeImage("Core", group, StubSprites.Circle(64f), HoleCore, Vector2.one * size * 0.8f, false);
                RectTransform ring = MakeImage("Ring", group, ThickRing(), HoleRing, Vector2.one * (size + 8f), true);
                hit = ring.GetComponent<Image>();
            }
            else // планета (и редкие кометные строки данных): круг цвета тела + обвод
            {
                Color c = BodyColor(body);
                MakeImage("Fill", group, StubSprites.SoftDot(64f, 0.5f), WithAlpha(c, 0.30f), Vector2.one * size * 0.86f, false);
                RectTransform ring = MakeImage("Ring", group, ThickRing(), WithAlpha(c, 0.95f), Vector2.one * size, true);
                hit = ring.GetComponent<Image>();
                if (body.IsHub) // хаб — жёлтый пунктир вокруг тела, виден с базового зума
                {
                    RectTransform hubRing = MakeImage("HubRing", group, DashRing(), WithAlpha(Taxi, 0.65f), Vector2.one * (size + 12f), false);
                    hubRing.anchoredPosition = Vector2.zero;
                }
            }

            var marker = group.gameObject.AddComponent<MapMarker>();
            marker.visual = hit;
            CelestialBodyData captured = body;
            marker.Clicked += () =>
            {
                if (suppressClick) // перетаскивание мимо объекта не открывает панель
                    return;
                if (selectedBody == captured)
                    Deselect(); // повторный клик закрывает
                else
                    SelectBody(captured);
            };
            marker.HoverEntered += () => ShowHoverRings(captured, true);
            marker.HoverExited += () => ShowHoverRings(captured, false);
            markers[body] = group;
            bodySizes[body] = size;

            // значки услуг — рядом с именем, под кругом
            int badges = CreateServiceBadges(group, body, size);
            float badgeDrop = badges > 0 ? 20f : 0f;

            // подпись всегда под кругом; точную раскладку разводит ResolveLabels
            Vector2 pos = WorldToMap(body.Position);
            TMP_Text label = MakeLabel("Label_" + body.DisplayName, content, body.DisplayName.ToUpper(), LabelFontSize,
                WithAlpha(InkBright, 0.85f));
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.sizeDelta = new Vector2(180f, 20f);
            labelRect.anchoredPosition = pos + Vector2.down * (size * 0.5f + 14f + badgeDrop);
            RegisterLabel(labelRect, body.DisplayName);
            bodyLabels[body] = label;
        }

        // значки услуг между кругом и именем: заправка (газ/ромб, жёлтый),
        // мастерская (крест); у сухой заправки — приглушённый значок с красным
        // перечёркиванием (Н4.5). Вернёт сколько нарисовано.
        private int CreateServiceBadges(RectTransform group, CelestialBodyData body, float size)
        {
            bool hub = body.IsHub;
            bool fuel = body.HasFuel || hub;
            bool repair = body.HasRepair || hub;
            if (!fuel && !repair)
                return 0;
            bool dry = fuel && OutageOf(body);

            const float d = 16f, gap = 5f;
            int count = (fuel ? 1 : 0) + (repair ? 1 : 0);
            float y = -(size * 0.5f + 12f);
            float x = -(count - 1) * (d + gap) * 0.5f;

            if (fuel)
            {
                Sprite gas = GasSprite();
                RectTransform badge = MakeImage("BadgeFuel", group, gas,
                    WithAlpha(Taxi, dry ? 0.32f : 0.95f), Vector2.one * d, false);
                badge.anchoredPosition = new Vector2(x, y);
                if (dry)
                {
                    // перечёркнутая заправка: красный штрих поверх приглушённого значка
                    RectTransform slash = MakeImage("Slash", badge, StubSprites.Line(),
                        WithAlpha(Danger, 0.9f), new Vector2(d * 1.35f, 2.6f), false);
                    slash.anchoredPosition = Vector2.zero;
                    slash.localRotation = Quaternion.Euler(0f, 0f, 45f);
                }
                x += d + gap;
            }
            if (repair)
            {
                RectTransform badge = MakeImage("BadgeRepair", group, CrossSprite(),
                    WithAlpha(RepairColor, 0.9f), Vector2.one * d, false);
                badge.anchoredPosition = new Vector2(x, y);
            }
            return count;
        }

        // пояс — пунктирное кольцо осевой линии; кликабелен (инфо-панель)
        private void CreateBelt(CelestialBodyData body)
        {
            float diameter = Mathf.Max(10f, body.Radius * 2f * scale);
            RectTransform ring = MakeImage("Belt_" + body.DisplayName, content, DashRing(),
                BeltColor, Vector2.one * diameter, true);
            ring.anchoredPosition = WorldToMap(body.Position);
            bodySizes[body] = diameter;

            var marker = ring.gameObject.AddComponent<MapMarker>();
            marker.visual = ring.GetComponent<Image>();
            CelestialBodyData captured = body;
            marker.Clicked += () =>
            {
                if (suppressClick)
                    return;
                if (selectedBody == captured)
                    Deselect();
                else
                    SelectBody(captured);
            };

            if (string.IsNullOrEmpty(body.DisplayName))
                return;
            string beltName = body.DisplayName.ToUpper();
            if (!beltName.Contains("ПОЯС"))
                beltName += " · ПОЯС";
            TMP_Text label = MakeLabel("Label_" + body.DisplayName, content, beltName, LabelFontSize,
                WithAlpha(InkBright, 0.85f));
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.sizeDelta = new Vector2(260f, 20f);
            labelRect.anchoredPosition = WorldToMap(body.Position) + Vector2.up * (diameter * 0.5f + 14f);
            RegisterLabel(labelRect, beltName); // AboveY = зеркальная позиция, у пояса — под кольцом
        }

        // цвет тела: авторский из данных, иначе оттенок района
        private Color BodyColor(CelestialBodyData body)
        {
            if (body.Visuals != null && body.Visuals.UseColor)
                return body.Visuals.Color;
            return SectorColor(body.Sector);
        }

        private static Color SectorColor(Sector sector)
        {
            Vector3 sc = sectorColors[(int)sector % sectorColors.Length];
            return new Color(sc.x, sc.y, sc.z);
        }

        // живое тело по имени — как в MinimapBinder.FindLiveBody
        private CelestialBody FindLiveBody(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
                return null;
            if (gravity == null)
                gravity = FindAnyObjectByType<GravityField>();
            if (gravity == null)
                return null;
            foreach (CelestialBody body in gravity.Bodies)
                if (body.DisplayName == displayName)
                    return body;
            return null;
        }

        // сеть станции пуста (Н4.5): у живого тела берём факт, иначе детерминированный
        // расчёт FuelNetwork (тот же, что в WorldBuilder) — карта строится до мира
        private bool OutageOf(CelestialBodyData body)
        {
            if (body.Type != BodyType.Planet || (!body.HasFuel && !body.IsHub))
                return false;
            CelestialBody live = FindLiveBody(body.DisplayName);
            if (live != null)
                return live.FuelOutage;
            return outageNames != null && outageNames.Contains(body.DisplayName);
        }

        // hover по телу: круги радиусов захвата/посадки + подпись крупнее
        private void ShowHoverRings(CelestialBodyData body, bool on)
        {
            if (hoverCaptureRing == null || hoverLandingRing == null || body == null)
                return;
            if (!on)
            {
                hoverCaptureRing.gameObject.SetActive(false);
                hoverLandingRing.gameObject.SetActive(false);
                if (bodyLabels.TryGetValue(body, out TMP_Text off))
                {
                    off.fontSize = LabelFontSize;
                    off.color = WithAlpha(InkBright, 0.85f);
                }
                return;
            }
            // радиусы берём у живого тела (там переопределения уровня + PhysicsConfig),
            // дата-фолбэк — только если тело ещё не заспавнено
            CelestialBody live = FindLiveBody(body.DisplayName);
            float capture = (live != null ? live.CaptureRing
                : body.CaptureRingRadius > 0f ? body.CaptureRingRadius : body.Radius * 3.3f + 30f) * 2f * scale;
            float landing = (live != null ? live.LandingRing
                : body.LandingRingRadius > 0f ? body.LandingRingRadius : body.Radius + 90f) * 2f * scale;
            hoverCaptureRing.sizeDelta = Vector2.one * capture;
            hoverCaptureRing.anchoredPosition = WorldToMap(body.Position);
            hoverLandingRing.sizeDelta = Vector2.one * landing;
            hoverLandingRing.anchoredPosition = WorldToMap(body.Position);
            hoverCaptureRing.gameObject.SetActive(true);
            hoverLandingRing.gameObject.SetActive(true);
            if (bodyLabels.TryGetValue(body, out TMP_Text label))
            {
                label.fontSize = LabelHoverFont;
                label.color = InkBright;
            }
        }

        // регистрация подписи для разводки без наложений (база — текущая позиция «под кругом»)
        private void RegisterLabel(RectTransform rect, string name)
        {
            var ml = new MapLabel
            {
                Rect = rect,
                Width = Mathf.Max(40f, (string.IsNullOrEmpty(name) ? 8f : name.Length) * LabelFontSize * 0.62f + 12f),
                Below = rect.anchoredPosition,
                AboveY = -rect.anchoredPosition.y // зеркально над кругом
            };
            mapLabels.Add(ml);
        }

        // разводим подписи: базово под кругом; занято — над кругом, дальше — шагом вниз/вверх
        private void ResolveLabels()
        {
            const float h = 20f, pad = 5f;
            var placed = new List<Rect>(mapLabels.Count);
            foreach (MapLabel ml in mapLabels)
            {
                float halfW = ml.Width * 0.5f + pad;
                var candidates = new[]
                {
                    ml.Below,
                    new Vector2(ml.Below.x, ml.AboveY),
                    ml.Below + Vector2.down * 24f,
                    new Vector2(ml.Below.x, ml.AboveY + 24f)
                };
                Vector2 chosen = ml.Below;
                foreach (Vector2 c in candidates)
                {
                    var r = new Rect(c.x - halfW, c.y - h * 0.5f - pad, ml.Width + pad * 2f, h + pad * 2f);
                    bool hit = false;
                    foreach (Rect p in placed)
                    {
                        if (r.Overlaps(p))
                        {
                            hit = true;
                            break;
                        }
                    }
                    if (!hit)
                    {
                        chosen = c;
                        break;
                    }
                }
                // даже fallback-позицию регистрируем, иначе плотный кластер наложится
                placed.Add(new Rect(chosen.x - halfW, chosen.y - h * 0.5f - pad, ml.Width + pad * 2f, h + pad * 2f));
                ml.Rect.anchoredPosition = chosen;
            }
        }

        // ————— легенда / компас / линейка / кнопки зума (вне зум-контейнера) —————

        private void BuildLegend()
        {
            legendRoot = MakeRect("Legend", mapRect, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            legendRoot.pivot = new Vector2(0f, 0f);
            legendRoot.anchoredPosition = new Vector2(24f, 22f);
            legendRoot.sizeDelta = new Vector2(216f, 232f);

            RectTransform bg = MakeRect("Bg", legendRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image bgImage = bg.gameObject.AddComponent<Image>();
            bgImage.color = Panel;
            bgImage.raycastTarget = true; // клики по легенде не снимают выбор
            MakeFrameBars(legendRoot, WithAlpha(Ink, 0.5f), 2f);

            float y = 216f; // ряды сверху вниз; pivot легенды (0,0) — y положительный
            LegendRow(ref y, StubSprites.Circle(64f), WithAlpha(Ink, 0.9f), "планета", 10f);
            LegendRow(ref y, StubSprites.Circle(64f), StarColor, "звезда", 11f);
            LegendRow(ref y, StubSprites.Circle(64f), HoleCore, "чёрная дыра", 11f, ThickRing(), HoleRing);
            LegendRow(ref y, StubSprites.Circle(64f), CometColor, "комета", 8f);
            LegendRow(ref y, DashRing(), BeltColor, "пояс астероидов", 15f);
            LegendRow(ref y, GasSprite(), Taxi, "заправка", 15f);
            LegendRow(ref y, GasSprite(), WithAlpha(Taxi, 0.32f), "топлива нет", 15f, StubSprites.Line(), WithAlpha(Danger, 0.9f), overlaySlash: true);
            LegendRow(ref y, CrossSprite(), RepairColor, "мастерская", 15f);
            LegendRow(ref y, Diamond(), Taxi, "хаб МТО", 15f);
            LegendRow(ref y, ThickRing(), Taxi, "цель заказа", 15f);
            LegendRow(ref y, PaperMapArt.RouteDash(), WithAlpha(Taxi, 0.8f), "маршрут заказа", 18f);
            LegendRow(ref y, WaypointIndicator.ChevronSprite() ?? StubSprites.TargetChevron(), Taxi, "вы здесь", 13f);
        }

        private void LegendRow(ref float y, Sprite icon, Color color, string text, float iconSize,
            Sprite overlay = null, Color overlayColor = default, bool overlaySlash = false)
        {
            RectTransform iconRect = MakeRect("I", legendRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            iconRect.sizeDelta = Vector2.one * 16f;
            iconRect.anchoredPosition = new Vector2(20f, y);
            MakeImage("G", iconRect, icon, color, Vector2.one * iconSize, false);
            if (overlay != null) // второй слой (перечёркнутая заправка, кольцо дыры)
            {
                RectTransform ov = MakeImage("O", iconRect, overlay, overlayColor, Vector2.one * iconSize, false);
                ov.anchoredPosition = Vector2.zero;
                if (overlaySlash)
                    ov.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }

            TMP_Text row = MakeLabel("R", legendRoot, text, 12f, WithAlpha(InkBright, 0.85f),
                TextAlignmentOptions.Left);
            ((RectTransform)row.transform).sizeDelta = new Vector2(170f, 16f);
            ((RectTransform)row.transform).anchoredPosition = new Vector2(34f, y);
            y -= 18f;
        }

        private void BuildCompass()
        {
            compassRoot = MakeRect("Compass", mapRect, Vector2.one, Vector2.one, Vector2.zero, Vector2.zero);
            compassRoot.anchoredPosition = new Vector2(-64f, -70f);
            compassRoot.sizeDelta = new Vector2(48f, 72f);
            var ink = WithAlpha(Ink, 0.8f);

            TMP_Text n = MakeLabel("N", compassRoot, "С", 16f, ink);
            ((RectTransform)n.transform).sizeDelta = new Vector2(24f, 22f);
            ((RectTransform)n.transform).anchoredPosition = new Vector2(0f, 20f);

            RectTransform needle = MakeRect("Needle", compassRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            needle.sizeDelta = new Vector2(3f, 34f);
            Image needleImage = needle.gameObject.AddComponent<Image>();
            needleImage.color = ink;
            needleImage.raycastTarget = false;

            RectTransform cross = MakeRect("Cross", compassRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            cross.sizeDelta = new Vector2(22f, 2f);
            Image crossImage = cross.gameObject.AddComponent<Image>();
            crossImage.color = WithAlpha(Ink, 0.35f);
            crossImage.raycastTarget = false;
        }

        private void BuildRuler()
        {
            // масштабная линейка: круглый шаг мира, влезающий в 150 px
            int[] steps = { 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000 };
            float step = steps[0];
            foreach (int candidate in steps)
            {
                if (candidate * scale <= 150f)
                    step = candidate;
            }
            float px = step * scale;

            rulerRoot = MakeRect("Ruler", mapRect, new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            rulerRoot.pivot = new Vector2(0.5f, 0f);
            rulerRoot.anchoredPosition = new Vector2(-28f - px * 0.5f, 22f);
            rulerRoot.sizeDelta = new Vector2(px + 8f, 34f);
            var ink = WithAlpha(Ink, 0.8f);

            RectTransform line = MakeRect("Line", rulerRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
            line.sizeDelta = new Vector2(px, 3f);
            Image lineImage = line.gameObject.AddComponent<Image>();
            lineImage.color = ink;
            lineImage.raycastTarget = false;

            foreach (float x in new[] { -px * 0.5f, px * 0.5f })
            {
                RectTransform tick = MakeRect("Tick", rulerRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
                tick.anchoredPosition = new Vector2(x, 4f);
                tick.sizeDelta = new Vector2(3f, 10f);
                Image tickImage = tick.gameObject.AddComponent<Image>();
                tickImage.color = ink;
                tickImage.raycastTarget = false;
            }

            TMP_Text label = MakeLabel("T", rulerRoot, step + " юн", 12f, ink);
            ((RectTransform)label.transform).sizeDelta = new Vector2(px + 40f, 18f);
            ((RectTransform)label.transform).anchoredPosition = new Vector2(0f, 12f);
        }

        // кнопки зума: +/− к центру экрана, «СБРОС» — вид «весь мир»
        private void BuildZoomButtons()
        {
            zoomRoot = MakeRect("ZoomButtons", mapRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
            zoomRoot.pivot = new Vector2(0.5f, 0f);
            zoomRoot.anchoredPosition = new Vector2(0f, 16f);
            zoomRoot.sizeDelta = new Vector2(196f, 40f);

            MakeChromeButton("BtnZoomIn", zoomRoot, new Vector2(44f, 40f), new Vector2(-76f, 0f), "+", 20f,
                ButtonBack, InkBright, () => ZoomStep(ButtonZoomStep));
            MakeChromeButton("BtnZoomOut", zoomRoot, new Vector2(44f, 40f), new Vector2(-26f, 0f), "-", 20f,
                ButtonBack, InkBright, () => ZoomStep(1f / ButtonZoomStep));
            MakeChromeButton("BtnZoomReset", zoomRoot, new Vector2(96f, 40f), new Vector2(50f, 0f), "СБРОС", 12f,
                ButtonBack, InkBright, () => AnimateZoom(1f, Vector2.zero));
        }

        // кнопка в стиле борткомпа: тёмная плашка + рамка-обводка + зелёный текст
        private Button MakeChromeButton(string name, RectTransform parent, Vector2 size, Vector2 pos,
            string label, float fontSize, Color back, Color fore, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;

            Image bg = go.GetComponent<Image>();
            bg.color = back;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.85f, 0.7f, 0.5f);
            outline.effectDistance = new Vector2(2f, -2f);

            TMP_Text text = MakeLabel("T", rect, label, fontSize, fore);
            RectTransform textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = bg; // tint-подсветка на hover/нажатие
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.fadeDuration = 0.1f;
            colors.highlightedColor = new Color(0.82f, 1f, 0.9f);
            colors.pressedColor = new Color(0.6f, 1f, 0.78f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());
            UiSfx.Hook(button);
            return button;
        }

        // ————— инфо-панель объекта —————

        private void BuildInfoPanel()
        {
            noteRoot = new GameObject("InfoPanel", typeof(RectTransform));
            noteRoot.transform.SetParent(mapRect, false);
            RectTransform note = (RectTransform)noteRoot.transform;
            note.anchorMin = new Vector2(1f, 0.5f);
            note.anchorMax = new Vector2(1f, 0.5f);
            note.pivot = new Vector2(1f, 0.5f);
            note.sizeDelta = new Vector2(300f, 340f);
            note.anchoredPosition = new Vector2(-16f, 8f);

            Image bg = noteRoot.AddComponent<Image>();
            bg.color = Panel;
            bg.raycastTarget = true; // клики по панели не уходят в планшет
            MakeFrameBars(note, WithAlpha(Ink, 0.6f), 2f);

            noteName = MakeLabel("Name", note, "", 20f, InkBright);
            ((RectTransform)noteName.transform).sizeDelta = new Vector2(268f, 30f);
            ((RectTransform)noteName.transform).anchoredPosition = new Vector2(0f, 130f);
            noteName.fontStyle = FontStyles.Bold;

            noteType = MakeLabel("Type", note, "", 12f, Ink);
            ((RectTransform)noteType.transform).sizeDelta = new Vector2(268f, 20f);
            ((RectTransform)noteType.transform).anchoredPosition = new Vector2(0f, 104f);

            noteServices = MakeLabel("Services", note, "", 13f, InkBright, TextAlignmentOptions.Top);
            ((RectTransform)noteServices.transform).sizeDelta = new Vector2(268f, 44f);
            ((RectTransform)noteServices.transform).anchoredPosition = new Vector2(0f, 69f);
            noteServices.textWrappingMode = TextWrappingModes.Normal; // до 2 строк, остальное — Overflow
            noteServices.overflowMode = TextOverflowModes.Overflow;

            noteFuel = MakeLabel("Fuel", note, "", 13f, Taxi);
            ((RectTransform)noteFuel.transform).sizeDelta = new Vector2(268f, 20f);
            ((RectTransform)noteFuel.transform).anchoredPosition = new Vector2(0f, 34f);

            noteDistance = MakeLabel("Distance", note, "", 13f, Taxi);
            ((RectTransform)noteDistance.transform).sizeDelta = new Vector2(268f, 20f);
            ((RectTransform)noteDistance.transform).anchoredPosition = new Vector2(0f, 12f);

            markButton = MakeChromeButton("BtnMark", note, new Vector2(140f, 42f), new Vector2(-74f, -20f),
                "ПОМЕТИТЬ", 14f, ButtonBack, InkBright, () =>
                {
                    if (selectedBody != null)
                        ToggleWaypoint(selectedBody);
                });
            markButtonImage = markButton.GetComponent<Image>();
            markLabel = markButton.GetComponentInChildren<TMP_Text>();

            MakeChromeButton("BtnHome", note, new Vector2(116f, 42f), new Vector2(72f, -20f),
                "К НАЧАЛУ", 13f, ButtonBack, InkBright, GoToStart);

            TMP_Text hint = MakeLabel("Hint", note, "M / ESC — ЗАКРЫТЬ КАРТУ", 10f, WithAlpha(Ink, 0.55f));
            ((RectTransform)hint.transform).sizeDelta = new Vector2(268f, 16f);
            ((RectTransform)hint.transform).anchoredPosition = new Vector2(0f, -148f);

            noteRoot.SetActive(false);
        }

        private void FillNote(CelestialBodyData body)
        {
            noteName.text = body.DisplayName.ToUpper();
            noteType.text = TypeName(body.Type) + " · " + SectorName(body.Sector);
            noteServices.text = ServicesLine(body);

            bool fuel = body.HasFuel || body.IsHub;
            noteFuel.gameObject.SetActive(fuel);
            if (fuel)
            {
                if (OutageOf(body))
                {
                    noteFuel.text = "ТОПЛИВА НЕТ";
                    noteFuel.color = Danger;
                }
                else
                {
                    float perUnit = FuelNetwork.PricePerUnit(body.FuelTier, body.Sector, pricing);
                    noteFuel.text = "ТОПЛИВО: Đ " + perUnit.ToString("0.##") + "/ед";
                    noteFuel.color = Taxi;
                }
            }

            noteRefreshAt = 0f; // дистанция сразу, без троттлинга прошлого кадра
            UpdateNoteDistance();
            RefreshMarkButton();
        }

        // что можно сделать на теле — из HasFuel/FuelOutage/HasRepair/IsHub; планеты
        // всегда принимают заказы (OrderService.RandomPlanetIndex — любой Planet)
        private static string ServicesLine(CelestialBodyData body)
        {
            if (body.IsHub)
                return "Хаб МТО: заправка, мастерская, магазин";
            if (body.HasFuel && body.HasRepair)
                return "Заправка · мастерская";
            if (body.HasFuel)
                return "Заправка";
            if (body.HasRepair)
                return "Мастерская";
            switch (body.Type)
            {
                case BodyType.Planet:
                    return "Приём и доставка заказов";
                case BodyType.AsteroidBelt:
                    return "Урон корпусу при пролёте";
                case BodyType.Star:
                    return "Услуг нет · обжигает";
                case BodyType.BlackHole:
                    return "Услуг нет · затягивает";
                default:
                    return "Услуг нет";
            }
        }

        private void UpdateNoteDistance()
        {
            if (selectedBody == null || noteDistance == null || Time.unscaledTime < noteRefreshAt)
                return;
            noteRefreshAt = Time.unscaledTime + 0.3f;
            Transform shipT = ShipPosition();
            float dist = shipT != null ? Vector2.Distance(shipT.position, selectedBody.Position) : 0f;
            noteDistance.text = "ДИСТАНЦИЯ: " + dist.ToString("0") + " юн";
        }

        private void RefreshMarkButton()
        {
            if (markLabel == null)
                return;
            bool marked = Waypoint != null && Waypoint == selectedBody;
            markLabel.text = marked ? "СНЯТЬ МЕТКУ" : "ПОМЕТИТЬ";
            if (markButtonImage != null) // отмеченный объект — жёлтая кнопка
                markButtonImage.color = marked ? Taxi : ButtonBack;
            if (markLabel != null)
                markLabel.color = marked ? Color.black : InkBright;
        }

        private void GoToStart()
        {
            // «К НАЧАЛУ»: панель на стартовую планету; вид подводится через EnsureVisible
            Deselect();
            if (level == null || level.StartPlanetIndex < 0 || level.StartPlanetIndex >= level.Bodies.Count)
                return;
            CelestialBodyData start = level.Bodies[level.StartPlanetIndex];
            if (start.Type == BodyType.Planet)
                SelectBody(start);
        }

        // ————— выбор и инфо-панель —————

        // публично: оркестратор/тесты открывают панель объекта без эмуляции мыши
        public void SelectBody(CelestialBodyData body)
        {
            selectedBody = body;
            noteRoot.SetActive(true);
            FillNote(body);
            EnsureVisible(WorldToMap(body.Position));
        }

        private void Deselect()
        {
            selectedBody = null;
            if (noteRoot != null)
                noteRoot.SetActive(false);
            // вид не сбрасываем — пан/зум остаются под контролем игрока
        }

        // клик по объекту: не рвём ручной вид, только гарантируем видимость
        private void EnsureVisible(Vector2 targetInContent)
        {
            float s = zoom < 1.4f ? ClickZoom : zoom;
            AnimateZoom(s, -targetInContent * s);
        }

        // ————— вид: зум колесом/кнопками + пан перетаскиванием —————

        // колесо: зум к курсору, точка под мышью остаётся на месте
        private void HandleWheel()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || content == null)
                return;
            Vector2 scroll = mouse.scroll.ReadValue();
            if (scroll.y == 0f)
                return;
            Vector2 pos = mouse.position.ReadValue();
            if (PointerOverChrome(pos))
                return;
            ZoomAt(pos, scroll.y > 0f ? WheelStep : 1f / WheelStep);
        }

        private void ZoomAt(Vector2 screenPos, float factor)
        {
            if (content == null || sheetRect == null || !TryScreenToSheet(screenPos, out Vector2 m))
                return;
            zoomAnim = false;
            float s = Mathf.Clamp(zoom * factor, MinViewZoom, MaxViewZoom);
            if (Mathf.Abs(s - zoom) < 0.0001f)
                return;
            Vector2 c = sheetRect.rect.center;
            Vector2 offset = (m - c) * (1f - s / zoom) + zoomOffset * (s / zoom);
            ApplyZoom(s, ClampZoomOffset(offset, s));
        }

        // кнопка +/−: зум к центру экрана (точка центра остаётся на месте)
        private void ZoomStep(float factor)
        {
            if (content == null)
                return;
            float s = Mathf.Clamp(zoom * factor, MinViewZoom, MaxViewZoom);
            if (Mathf.Abs(s - zoom) < 0.0001f)
                return;
            AnimateZoom(s, zoomOffset * (s / zoom));
        }

        // программный вид для тестов/оркестратора: зум и смещение контента
        public void SetView(float viewZoom, Vector2 sheetOffset)
        {
            if (!built || content == null)
                return;
            float s = Mathf.Clamp(viewZoom, MinViewZoom, MaxViewZoom);
            zoomAnim = false;
            ApplyZoom(s, ClampZoomOffset(sheetOffset, s));
        }

        private bool TryScreenToSheet(Vector2 screenPos, out Vector2 sheetLocal)
        {
            sheetLocal = Vector2.zero;
            return sheetRect != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(sheetRect, screenPos, canvasCam, out sheetLocal);
        }

        private bool PointerOverChrome(Vector2 screenPos)
        {
            return (noteRoot != null && ContainsScreen((RectTransform)noteRoot.transform, screenPos))
                || ContainsScreen(legendRoot, screenPos)
                || ContainsScreen(zoomRoot, screenPos);
        }

        private bool ContainsScreen(RectTransform rect, Vector2 screenPos)
        {
            return rect != null && rect.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, canvasCam);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            suppressClick = false; // новое нажатие — клик снова жив
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || PressOnChrome(eventData))
                return;
            dragging = true;
            dragMoved = 0f;
            zoomAnim = false; // рука перебивает анимацию зума
            TryScreenToSheet(eventData.position, out dragSheetPos);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || !TryScreenToSheet(eventData.position, out Vector2 pos))
                return;
            Vector2 delta = pos - dragSheetPos;
            dragMoved += delta.magnitude;
            dragSheetPos = pos;
            if (dragMoved > ClickDragGuard)
                suppressClick = true;
            // резиновый вынос за край; на отпускании экран плавно вернётся в границы мира
            ApplyZoom(zoom, ClampZoomOffset(zoomOffset + delta, zoom, RubberMargin));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging)
                return;
            dragging = false;
            AnimateZoom(zoom, zoomOffset); // плавное клампирование в границы мира
        }

        private bool PressOnChrome(PointerEventData eventData)
        {
            Transform pressed = eventData.pointerPress != null ? eventData.pointerPress.transform : null;
            return pressed != null
                && ((noteRoot != null && pressed.IsChildOf(noteRoot.transform))
                    || (legendRoot != null && pressed.IsChildOf(legendRoot.transform))
                    || (zoomRoot != null && pressed.IsChildOf(zoomRoot.transform)));
        }

        private void AnimateZoom(float targetScale, Vector2 targetOffset)
        {
            Vector2 clamped = ClampZoomOffset(targetOffset, targetScale);
            if (Mathf.Abs(targetScale - zoom) < 0.001f && (clamped - zoomOffset).sqrMagnitude < 1f)
            {
                zoomAnim = false;
                ApplyZoom(targetScale, clamped);
                return;
            }
            zoomFrom = zoom;
            offFrom = zoomOffset;
            zoomTo = targetScale;
            offTo = clamped;
            zoomT = 0f;
            zoomAnim = true;
        }

        private void UpdateZoomAnim()
        {
            if (!zoomAnim)
                return;
            zoomT = Mathf.Clamp01(zoomT + Time.unscaledDeltaTime / ZoomDuration);
            float k = Mathf.SmoothStep(0f, 1f, zoomT);
            ApplyZoom(Mathf.Lerp(zoomFrom, zoomTo, k), Vector2.Lerp(offFrom, offTo, k));
            if (zoomT >= 1f)
                zoomAnim = false;
        }

        private void ApplyZoom(float s, Vector2 offset)
        {
            zoom = s;
            zoomOffset = offset;
            if (content == null)
                return;
            content.localScale = Vector3.one * s;
            content.anchoredPosition = offset;
        }

        // зум/пан не выносят содержимое за экран; margin > 1 — резиновый вынос при перетаскивании
        private Vector2 ClampZoomOffset(Vector2 offset, float s, float margin = 1f)
        {
            if (content == null)
                return offset;
            if (s <= 1f)
                return Vector2.zero; // контент меньше экрана — всегда по центру
            Vector2 max = content.rect.size * (s - 1f) * 0.5f * margin;
            return Vector2.Max(-max, Vector2.Min(max, offset));
        }

        // ————— живой слой —————

        // игрок виден всегда: размер не зависит от зума, за краем вида маркер
        // клампится к границе и смотрит в сторону реальной позиции
        private void UpdatePlayerMarker()
        {
            Transform shipT = ShipPosition();
            if (shipT == null || playerMarker == null || sheetRect == null)
                return;

            if (ship != null && ship.State == ShipState.Flying && ship.Body != null
                && ship.Body.linearVelocity.sqrMagnitude > 25f)
            {
                Vector2 v = ship.Body.linearVelocity;
                playerHeading = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            }

            float inv = 1f / Mathf.Max(zoom, 0.001f);
            playerMarker.localScale = Vector3.one * inv; // контр-масштаб: размер на экране постоянен

            Vector2 pos = WorldToMap(shipT.position);
            Vector2 c = sheetRect.rect.center;
            Vector2 half = sheetRect.rect.size * 0.5f;
            float inset = ViewInset * inv;
            Vector2 min = (c - half - zoomOffset) * inv + new Vector2(inset, inset);
            Vector2 max = (c + half - zoomOffset) * inv - new Vector2(inset, inset);
            Vector2 clamped = new Vector2(Mathf.Clamp(pos.x, min.x, max.x), Mathf.Clamp(pos.y, min.y, max.y));
            playerMarker.anchoredPosition = clamped;

            bool offView = (pos - clamped).sqrMagnitude > 1f;
            float angle;
            if (offView) // за краем вида — шеврон указывает на реальную позицию
            {
                Vector2 d = pos - clamped;
                angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;
            }
            else
            {
                angle = playerHeading - 90f; // спрайт остриём вверх
            }
            if (playerArrowRoot != null)
                playerArrowRoot.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private Transform ShipPosition()
        {
            if (ship == null)
                ship = FindAnyObjectByType<Ship>();
            return ship != null ? ship.transform : null;
        }

        // цель активного заказа — жёлтое пульсирующее кольцо (стиль TargetRing радара)
        // + подпись «ЦЕЛЬ: <имя>»; собственная подпись тела-цели прячется
        private void UpdateTargetRing()
        {
            int index = ActiveOrderTargetIndex();
            if (index < 0 || index >= level.Bodies.Count)
            {
                targetRing.gameObject.SetActive(false);
                targetIndex = -1;
                RestoreTargetLabel();
                return;
            }

            CelestialBodyData body = level.Bodies[index];
            targetRing.gameObject.SetActive(true);
            if (targetIndex != index)
            {
                RestoreTargetLabel();
                targetIndex = index;
                lastTargetBody = body;
                if (bodyLabels.TryGetValue(body, out TMP_Text own))
                    own.enabled = false;
                float size = Mathf.Max(46f, IconSizeOf(body) * 2.0f);
                targetRingImage.rectTransform.sizeDelta = Vector2.one * size;
                ((RectTransform)targetLabel.transform).anchoredPosition = Vector2.up * (size * 0.5f + 14f);
                targetLabel.text = "ЦЕЛЬ: " + body.DisplayName.ToUpper();
            }
            targetRing.anchoredPosition = WorldToMap(body.Position);
            // пульс: кольцо дышит размером и альфой
            float t = Time.unscaledTime;
            targetRingImage.transform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(t * 4.6f));
            targetRingImage.color = WithAlpha(Taxi, 0.72f + 0.28f * Mathf.Sin(t * 3.1f));
        }

        private void RestoreTargetLabel()
        {
            if (lastTargetBody != null && bodyLabels.TryGetValue(lastTargetBody, out TMP_Text label))
                label.enabled = true;
            lastTargetBody = null;
        }

        // тело текущего шага активного заказа: пассажир до посадки, цель — с пассажиром
        private int ActiveOrderTargetIndex()
        {
            var orders = session != null ? session.Orders : null;
            var active = orders != null ? orders.Active : null;
            return active == null
                ? -1
                : active.State == OrderState.Aboard ? active.DropoffIndex : active.PickupIndex;
        }

        // ————— маршрут активного заказа: лёгкая пунктирная дуга корабль → цель —————

        private void BuildRouteLine()
        {
            routeRoot = MakeRect("RouteLine", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            routeRoot.SetAsFirstSibling(); // маршрут под всеми телами и секторами
        }

        private void UpdateRoute()
        {
            int index = ActiveOrderTargetIndex();
            Transform shipT = ShipPosition();
            if (routeRoot == null || index < 0 || index >= level.Bodies.Count || shipT == null)
            {
                HideRoute();
                return;
            }

            Vector2 a = WorldToMap(shipT.position);
            Vector2 b = WorldToMap(level.Bodies[index].Position);
            Vector2 span = b - a;
            float len = span.magnitude;
            // лёгкая дуга: квадратичная кривая с перпендикулярным прогибом ~12% длины
            Vector2 perp = len > 1f ? new Vector2(-span.y, span.x) / len : Vector2.right;
            Vector2 ctrl = (a + b) * 0.5f + perp * Mathf.Min(len * 0.12f, 260f) * (index % 2 == 0 ? 1f : -1f);
            float step = 1f / (routePts.Length - 1);
            float total = 0f;
            routePts[0] = a;
            routeCum[0] = 0f;
            for (int i = 1; i < routePts.Length; i++)
            {
                float t = i * step;
                float u = 1f - t;
                routePts[i] = u * u * a + 2f * u * t * ctrl + t * t * b;
                total += Vector2.Distance(routePts[i - 1], routePts[i]);
                routeCum[i] = total;
            }

            // плотность пунктира постоянна на экране: шаг и толщина делятся на зум
            float period = RouteDashPeriod / Mathf.Max(zoom, 0.001f);
            float dashLen = 9f / Mathf.Max(zoom, 0.001f);
            float dashThick = 2.6f / Mathf.Max(zoom, 0.001f);
            int count = Mathf.Clamp(Mathf.CeilToInt(total / period), 1, RouteDashMax);
            if (count == RouteDashMax)
                period = total / RouteDashMax;

            var taxiDash = WithAlpha(Taxi, 0.7f);
            routeShown = true;
            for (int i = 0; i < count; i++)
            {
                if (i >= routeDashes.Count) // пул растёт лениво, только пока карта открыта
                {
                    RectTransform dash = MakeImage("RouteDash_" + i, routeRoot, PaperMapArt.RouteDash(),
                        taxiDash, new Vector2(dashLen, dashThick), false);
                    routeDashes.Add(dash);
                }
                RectTransform d = routeDashes[i];
                d.gameObject.SetActive(true);
                d.sizeDelta = new Vector2(dashLen, dashThick);

                // равномерно по длине дуги: ищем сегмент с накопленной длиной
                float target = Mathf.Min((i + 0.5f) * period, total);
                int seg = 0;
                while (seg < routePts.Length - 2 && routeCum[seg + 1] < target)
                    seg++;
                float segLen = Mathf.Max(routeCum[seg + 1] - routeCum[seg], 0.0001f);
                float k = Mathf.Clamp01((target - routeCum[seg]) / segLen);
                d.anchoredPosition = Vector2.Lerp(routePts[seg], routePts[seg + 1], k);
                Vector2 dir = routePts[seg + 1] - routePts[seg];
                if (dir.sqrMagnitude > 0.0001f)
                    d.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            }
            for (int i = count; i < routeDashes.Count; i++)
                routeDashes[i].gameObject.SetActive(false);
        }

        private void HideRoute()
        {
            if (routeRoot == null || !routeShown)
                return;
            routeShown = false;
            foreach (RectTransform dash in routeDashes)
                dash.gameObject.SetActive(false);
        }

        // живые кометы — голубые точки с хвостом по курсу
        private void UpdateComets()
        {
            if (content == null)
                return;
            if (cometSystem == null)
                cometSystem = FindAnyObjectByType<CometSystem>();
            if (cometSystem == null)
                return;

            List<Comet> dead = null;
            foreach (KeyValuePair<Comet, RectTransform> kv in cometIcons)
            {
                if (kv.Key == null)
                {
                    (dead ??= new List<Comet>()).Add(kv.Key);
                    if (kv.Value != null)
                        Destroy(kv.Value.gameObject);
                    continue;
                }
                Vector2 world = kv.Key.transform.position;
                kv.Value.anchoredPosition = WorldToMap(world);
                Vector2 delta = world - cometLastPos[kv.Key];
                if (delta.sqrMagnitude > 0.01f)
                    kv.Value.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                cometLastPos[kv.Key] = world;
            }
            if (dead != null)
                foreach (Comet gone in dead)
                {
                    cometIcons.Remove(gone);
                    cometLastPos.Remove(gone);
                }

            foreach (Comet comet in cometSystem.ActiveComets)
            {
                if (comet == null || cometIcons.ContainsKey(comet))
                    continue;
                RectTransform icon = MakeImage("Comet_" + cometIcons.Count, content, StubSprites.Circle(64f),
                    WithAlpha(CometColor, 0.9f), Vector2.one * 8f, false);
                // хвост позади точки: иконка поворачивается по движению (+X = вперёд)
                RectTransform tail = MakeImage("CometTail", icon, StubSprites.Line(),
                    WithAlpha(CometColor, 0.55f), new Vector2(14f, 2.2f), false);
                tail.anchoredPosition = new Vector2(-10f, 0f);
                tail.SetAsFirstSibling();
                Vector2 world = comet.transform.position;
                icon.anchoredPosition = WorldToMap(world);
                cometIcons[comet] = icon;
                cometLastPos[comet] = world;
            }
        }

        private float IconSizeOf(CelestialBodyData body)
        {
            return bodySizes.TryGetValue(body, out float size) ? size : 20f;
        }

        private void RefreshWaypointPin()
        {
            if (waypointPin == null)
                return;
            if (Waypoint == null)
            {
                waypointPin.gameObject.SetActive(false);
                return;
            }
            waypointPin.gameObject.SetActive(true);
            waypointPin.anchoredPosition = WorldToMap(Waypoint.Position) + Vector2.up * (IconSizeOf(Waypoint) * 0.5f + 13f);
            waypointPin.SetAsLastSibling();
        }

        private void RestackChrome()
        {
            // экранное поверх планшета: легенда, компас, линейка, кнопки, панель
            if (legendRoot != null) legendRoot.SetAsLastSibling();
            if (compassRoot != null) compassRoot.SetAsLastSibling();
            if (rulerRoot != null) rulerRoot.SetAsLastSibling();
            if (zoomRoot != null) zoomRoot.SetAsLastSibling();
            if (noteRoot != null) noteRoot.transform.SetAsLastSibling();
        }

        private void RebuildAll()
        {
            ClearChildren(transform);
            sheetRect = null;
            content = null;
            noteRoot = null;
            legendRoot = null;
            compassRoot = null;
            rulerRoot = null;
            zoomRoot = null;
            markers.Clear();
            bodySizes.Clear();
            bodyLabels.Clear();
            mapLabels.Clear();
            cometIcons.Clear();
            cometLastPos.Clear();
            routeRoot = null;
            routeDashes.Clear();
            hoverCaptureRing = null;
            hoverLandingRing = null;
            playerMarker = null;
            playerArrowRoot = null;
            targetRing = null;
            targetRingImage = null;
            targetLabel = null;
            waypointPin = null;
            targetIndex = -1;
            lastTargetBody = null;
            routeShown = false;
            selectedBody = null;
            noteName = null;
            noteType = null;
            noteServices = null;
            noteFuel = null;
            noteDistance = null;
            markButton = null;
            markButtonImage = null;
            markLabel = null;
            dragging = false;
            suppressClick = false;
            zoomAnim = false;
            zoom = 1f;
            zoomOffset = Vector2.zero;
            BuildChrome();
            BuildMap();
            built = true;
            builtWidth = mapRect.rect.width;
            RestackChrome();
        }

        // ————— хелперы —————

        private Vector2 WorldToMap(Vector2 world)
        {
            return (world - worldCenter) * scale;
        }

        private static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Destroy(t.GetChild(i).gameObject);
        }

        private static RectTransform MakeRect(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        private static RectTransform MakeImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = raycast;
            RectTransform rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            return rect;
        }

        // все подписи карты одним шрифтом; высоты узлов ≥ fontSize*1.32 (урок ночи 4)
        private TMP_Text MakeLabel(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            if (font != null)
                label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        // — процедурные фигуры планшета (кэш в статике, паттерн MinimapBinder) —

        private static Sprite thickRingSprite;
        private static Sprite dashRingSprite;
        private static Sprite diamondSprite;
        private static Sprite crossSprite;
        private static Sprite gasSprite;

        // толстое кольцо: обводы планет, цель, мир, hover-посадка
        private static Sprite ThickRing()
        {
            if (thickRingSprite != null)
                return thickRingSprite;
            thickRingSprite = Sprite.Create(
                StubSprites.CreateMaskTexture(256, (x, y) =>
                {
                    float d = Mathf.Sqrt(x * x + y * y);
                    return d <= 1f && d >= 0.86f;
                }),
                new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f), 256f);
            return thickRingSprite;
        }

        // пунктирное кольцо: орбиты, пояса, сеть буёв, хаб, кометная зона
        private static Sprite DashRing()
        {
            if (dashRingSprite != null)
                return dashRingSprite;
            dashRingSprite = Sprite.Create(
                StubSprites.CreateMaskTexture(256, (x, y) =>
                {
                    float d = Mathf.Sqrt(x * x + y * y);
                    if (d > 1f || d < 0.92f)
                        return false;
                    float ang = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
                    return Mathf.Repeat(ang, 24f) < 13f; // штрих 13° / пробел 11°
                }),
                new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f), 256f);
            return dashRingSprite;
        }

        private static Sprite Diamond()
        {
            if (diamondSprite != null)
                return diamondSprite;
            diamondSprite = Sprite.Create(
                StubSprites.CreateMaskTexture(64, (x, y) => Mathf.Abs(x) + Mathf.Abs(y) <= 0.82f),
                new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
            return diamondSprite;
        }

        // мастерская: крест (плюс) простыми фигурами
        private static Sprite CrossSprite()
        {
            if (crossSprite != null)
                return crossSprite;
            crossSprite = Sprite.Create(
                StubSprites.CreateMaskTexture(64, (x, y) =>
                    Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) <= 0.78f
                    && (Mathf.Abs(x) <= 0.16f || Mathf.Abs(y) <= 0.16f)),
                new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
            return crossSprite;
        }

        // заправка: мини-иконка gas_icon.png (HUD), без неё — ромб
        private static Sprite GasSprite()
        {
            if (gasSprite == null)
                gasSprite = Resources.Load<Sprite>("Art/icons/gas_icon");
            return gasSprite != null ? gasSprite : Diamond();
        }

        private static string TypeName(BodyType type)
        {
            switch (type)
            {
                case BodyType.Star: return "ЗВЕЗДА";
                case BodyType.BlackHole: return "ЧЁРНАЯ ДЫРА";
                case BodyType.AsteroidBelt: return "ПОЯС АСТЕРОИДОВ";
                case BodyType.Comet: return "КОМЕТА";
                default: return "ПЛАНЕТА";
            }
        }

        private static string SectorName(Sector sector)
        {
            switch (sector)
            {
                case Sector.Hub: return "ХАБ";
                case Sector.Near: return "БЛИЖНИЙ";
                case Sector.Mid1: return "СРЕДНИЙ I";
                case Sector.Mid2: return "СРЕДНИЙ II";
                case Sector.Far: return "ДАЛЬНИЙ";
                default: return "";
            }
        }
    }
}
