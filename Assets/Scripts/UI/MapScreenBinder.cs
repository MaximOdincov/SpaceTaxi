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
    // Карта (M) — «нарисованная от руки на старой жёлтой бумаге» (Н5.F). На тёмном
    // столе лежит лист (Art/background.png), на нём чернилами и карандашами —
    // система в масштабе мира: тела — арт владельца (Art/map: планета, заправка,
    // мастерская, хаб-такси, солнце, чёрная дыра), пояс — ОДИН спрайт
    // map_asteroid с поворотами/размерами по пунктирной осевой, кометы —
    // map_cometa по живым CometSystem.ActiveComets, игрок — map_rocket (виден
    // всегда: размер не зависит от зума, за краем вида клампится с указателем).
    // Районы — выпуклые рамки «от руки» с крупными подписями (БЛИЖНИЙ,
    // СРЕДНИЙ I…). Клик по телу — сильный зум + «уголок листа» с рукописными
    // записями (PaperMapLore: радиус, район, услуги, цена топлива, дистанция,
    // лорные факты); сухие заправки и перечёркивания убраны — outage отключён
    // (FuelOutageChancePct = 0). Цель заказа — жёлтый пульсирующий ореол, своя
    // метка — зелёный (булавка + ореол на теле). Публичный контракт сохранён:
    // Waypoint + WaypointChanged + Set/Clear/ToggleWaypoint (WaypointIndicator,
    // MinimapBinder), SetView/SelectBody (оркестратор), Esc-договорённость с
    // MapScreenController. Вид: колесо — зум к курсору ×1.2 (весь мир ↔ одна
    // планета), ЛКМ-драг и WASD — пан (игра под картой живёт: W на карте =
    // тяга кораблю, конфликт известен — см. отчёт Н5.F). Подписи разводит
    // ResolveLabels с учётом иконок тел, поясов, декора и рамок районов.
    public class MapScreenBinder : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private TMP_FontAsset mapFont; // Excalifont-Regular SDF — назначен в сцене

        // — вид: базовый масштаб = весь мир вписан в лист, дальше зум к курсору —
        private const float MinViewZoom = 1f;      // из базового вида не отдаляемся
        private const float MaxViewZoom = 14f;     // от «весь мир» до одной планеты крупно
        private const float ClickZoom = 8f;        // клик по телу — сильный зум (видны записи)
        private const float ZoomDuration = 0.22f;
        private const float WheelStep = 1.2f;
        private const float ButtonZoomStep = 1.35f;
        private const float ClickDragGuard = 6f;   // сдвиг больше этого — пан, а не выбор
        private const float FitMargin = 1.10f;     // bbox тел занимает 90% листа
        private const float LabelFontSize = 14f;   // кегль подписей тел (Excalifont)
        private const float LabelHoverFont = 17f;
        private const float RouteDashPeriod = 15f; // шаг пунктира маршрута, экранных px
        private const int RouteDashMax = 80;
        private const float ViewInset = 18f;       // отступ клампа ракеты от края, px
        private const float RubberMargin = 1.15f;  // мягкий вынос за край при перетаскивании
        private const float MapPanSpeed = 640f;    // WASD-пан, экранных px/с (делится на зум)

        // «нос» арт-спрайтов при localRotation = 0, градусов от +X (замер по пикселям):
        // ракета летит остриём в правый-верхний угол, комета — головой в левый-низ
        private const float RocketNoseDeg = 55f;
        private const float CometHeadDeg = 226f;

        // — waypoint для WaypointIndicator/MinimapBinder (Н-8): свойство + событие —
        public CelestialBodyData Waypoint { get; private set; }
        public event Action WaypointChanged;

        public void SetWaypoint(CelestialBodyData body)
        {
            if (Waypoint == body)
                return;
            Waypoint = body;
            WaypointChanged?.Invoke();
            RefreshMarkButton();
        }

        public void ClearWaypoint()
        {
            if (Waypoint == null)
                return;
            Waypoint = null;
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

        private LevelAsset level;
        private GameSession session;
        private PricingConfig pricing; // цена топлива сети района
        private Ship ship;
        private GravityField gravity; // живые радиусы тел на hover (лениво)
        private RectTransform mapRect;
        private TMP_FontAsset font;
        private bool built;
        private float builtWidth;
        private Camera canvasCam; // null для Screen Space Overlay — так и нужно утилитам

        private RectTransform sheetRect;   // лист бумаги
        private RectTransform content;     // зум-контейнер: всё нарисованное на листе
        private RectTransform sectorRoot;  // рамки и подписи районов
        private RectTransform orbitsRoot;  // пунктирные орбиты планет
        private RectTransform edgeRoot;    // граница мира, буи, штриховка
        private RectTransform beltsRoot;   // пояса астероидов
        private RectTransform decoRoot;    // безделушки на свободных местах
        private RectTransform bodiesRoot;  // иконки тел
        private RectTransform labelsRoot;  // подписи тел/поясов (над иконками)
        private RectTransform cometsRoot;  // живые кометы
        private GameObject noteRoot;       // «уголок листа» с записями
        private RectTransform legendRoot;
        private RectTransform compassRoot;
        private RectTransform rulerRoot;
        private RectTransform zoomRoot;
        private RectTransform titleRoot;

        private readonly Dictionary<CelestialBodyData, RectTransform> markers = new Dictionary<CelestialBodyData, RectTransform>();
        private readonly Dictionary<CelestialBodyData, float> bodySizes = new Dictionary<CelestialBodyData, float>();

        private RectTransform playerMarker;
        private RectTransform playerArrowRoot; // вращается по курсу; подпись «ВЫ» не крутится
        private RectTransform targetRing;
        private RectTransform targetRingPulse;   // жёлтый пульс: сплошное кольцо
        private RectTransform targetRingWide;    // и второе, пунктирное — видно издалека
        private TMP_Text targetLabel;
        private RectTransform waypointRoot;      // зелёный ореол метки + булавка
        private RectTransform waypointHalo;
        private RectTransform waypointPin;
        private int targetIndex = -1;
        private CelestialBodyData lastTargetBody; // чья подпись спрятана под «ЦЕЛЬ: …»
        private float playerHeading;

        // маршрут активного заказа: лёгкая пунктирная дуга корабль → пассажир/цель
        private RectTransform routeRoot;
        private readonly List<RectTransform> routeDashes = new List<RectTransform>();
        private readonly Vector2[] routePts = new Vector2[33];
        private readonly float[] routeCum = new float[33];
        private bool routeShown;

        // hover по телу: круги радиусов посадки/захвата + имя крупнее
        private RectTransform hoverCaptureRing;
        private RectTransform hoverLandingRing;
        private readonly Dictionary<CelestialBodyData, TMP_Text> bodyLabels = new Dictionary<CelestialBodyData, TMP_Text>();

        // подписи тел: раскладка без наложений
        private sealed class MapLabel
        {
            public RectTransform Rect;
            public float Width;   // точная ширина текста (GetPreferredValues), px
            public Vector2 Below; // позиция под иконкой
            public float AboveY;  // y позиции над иконкой
        }
        private readonly List<MapLabel> mapLabels = new List<MapLabel>();
        // препятствия для подписей: иконки тел, камни поясов, декор, подписи районов
        private readonly List<Rect> labelObstacles = new List<Rect>();

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

        // пан: ЛКМ-драг и WASD; клик после драга не выбирает объект
        private bool dragging;
        private bool suppressClick;
        private float dragMoved;
        private Vector2 dragSheetPos;

        private CelestialBodyData selectedBody;
        private TMP_Text noteName;
        private TMP_Text noteSubtitle;
        private TMP_Text noteServices;
        private TMP_Text noteFuel;
        private TMP_Text noteRadius;
        private TMP_Text noteDistance;
        private TMP_Text noteFacts;
        private Button markButton;
        private Image markButtonImage;
        private TMP_Text markLabel;
        private float noteRefreshAt;

        private void Awake()
        {
            mapRect = (RectTransform)transform;
            Canvas canvas = GetComponentInParent<Canvas>();
            canvasCam = canvas != null ? canvas.worldCamera : null;

            // заготовку из сцены стираем целиком — лист рисуем кодом
            ClearChildren(transform);
            font = ResolveFont();
            BuildChrome();
        }

        // шрифт: сцена (Excalifont) → старый ресурс → HUD → дефолт TMP
        private TMP_FontAsset ResolveFont()
        {
            if (mapFont != null)
                return mapFont;
            TMP_FontAsset res = Resources.Load<TMP_FontAsset>("Excalifont SDF");
            if (res != null)
                return res;
            HudBinder hud = FindAnyObjectByType<HudBinder>();
            if (hud != null)
            {
                Transform speed = hud.transform.Find("SpeedValue");
                TMP_Text text = speed != null ? speed.GetComponent<TMP_Text>() : null;
                if (text != null && text.font != null)
                    return text.font;
            }
            return Resources.Load<TMP_FontAsset>("Fonts/TMP/JetBrainsMono-Regular SDF");
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
            UpdateWASDPan();
            UpdatePlayerMarker();
            UpdateTargetRing();
            UpdateWaypointVisual();
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

        // ————— каркас: стол, лист, экранное (вне зум-контейнера) —————

        private void BuildChrome()
        {
            // тёмный стол вокруг листа; клик мимо листа тоже снимает выбор
            RectTransform backdrop = MakeRect("Backdrop", mapRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image backImage = backdrop.gameObject.AddComponent<Image>();
            backImage.color = PaperMapArt.Desk;
            backImage.raycastTarget = true;
            MapMarker backMarker = backdrop.gameObject.AddComponent<MapMarker>();
            backMarker.hoverEnabled = false;
            backMarker.Clicked += () =>
            {
                if (!suppressClick) // клик после пана — не «снять выбор»
                    Deselect();
            };

            // лист жёлтой бумаги
            sheetRect = MakeRect("Sheet", mapRect, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -10f));
            Image sheetImage = sheetRect.gameObject.AddComponent<Image>();
            Sprite paper = PaperMapArt.SheetArt();
            sheetImage.sprite = paper;
            sheetImage.color = paper != null ? Color.white : PaperMapArt.Paper;
            sheetImage.raycastTarget = false; // клики сквозь лист уходят в Backdrop

            // карта живёт внутри листа с полями: не залезает на рукописную рамку
            content = MakeRect("MapRoot", sheetRect, Vector2.zero, Vector2.one, new Vector2(26f, 26f), new Vector2(-26f, -26f));
            content.gameObject.AddComponent<RectMask2D>(); // орбиты/пояса не вылезают за лист

            BuildCompass();
            BuildTitle();
            BuildLegend();
            BuildZoomButtons();
            BuildNotePanel();
        }

        // ————— содержимое листа —————

        private void BuildMap()
        {
            // граница мира = кольцо буёв (HullSystem бросает «ПОТЕРЯНА СВЯЗЬ» от origin)
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

            // «весь мир на листе»: bbox тел уровня + поля 10% вписывается целиком
            float w = content.rect.width, h = content.rect.height;
            scale = Mathf.Min(w / (2f * extX * FitMargin), h / (2f * extY * FitMargin));
            scale = Mathf.Max(scale, 0.012f);

            // рамка листа «от руки» — когда размер известен
            MakeSketchFrame(sheetRect, WithAlpha(PaperMapArt.Ink, 0.75f), 2.6f, 1042);

            // слои снизу вверх: маршрут → районы → орбиты → край мира → пояса →
            // декор → тела → подписи → hover → метка/цель → кометы → игрок
            routeRoot = MakeRect("RouteLine", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            routeRoot.SetAsFirstSibling();
            sectorRoot = MakeRect("Sectors", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            orbitsRoot = MakeRect("Orbits", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            edgeRoot = MakeRect("WorldEdge", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            beltsRoot = MakeRect("Belts", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            decoRoot = MakeRect("Deco", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            bodiesRoot = MakeRect("Bodies", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            labelsRoot = MakeRect("Labels", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            BuildGrid();
            BuildSectorFrames();
            BuildOrbits();
            BuildCometZone();
            BuildWorldEdge();
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type == BodyType.AsteroidBelt)
                    CreateBelt(body);
            BuildDeco();
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type != BodyType.AsteroidBelt)
                    CreateBody(body);
            ResolveLabels();

            hoverCaptureRing = MakeImage("HoverCapture", content, PaperMapArt.OrbitRing(),
                WithAlpha(PaperMapArt.Graphite, 0.7f), Vector2.zero, false);
            hoverCaptureRing.gameObject.SetActive(false);
            hoverLandingRing = MakeImage("HoverLanding", content, PaperMapArt.TargetRing(),
                WithAlpha(PaperMapArt.GreenPencil, 0.75f), Vector2.zero, false);
            hoverLandingRing.gameObject.SetActive(false);

            BuildWaypoint();
            BuildTargetRing();
            BuildPlayerMarker();

            cometsRoot = MakeRect("Comets", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            ApplyZoom(1f, Vector2.zero);
            BuildRuler(); // линейка зависит от scale — известен только здесь
            if (titleRoot != null)
            {
                TMP_Text title = titleRoot.GetComponentInChildren<TMP_Text>();
                if (title != null)
                    title.text = "система " + level.LevelName + " · маршрутный лист";
            }
        }

        // кольца дистанций от origin — карандашная разметка для сверки в полёте
        private void BuildGrid()
        {
            MakeImage("GridCenter", content, PaperMapArt.InkDot(),
                WithAlpha(PaperMapArt.Ink, 0.5f), Vector2.one * 7f, false);

            int[] steps = { 500, 1000, 2000, 5000, 10000, 20000, 50000 };
            int drawn = 0;
            foreach (int step in steps)
            {
                float px = step * scale;
                if (px < 70f || px > mapRadius * scale * 0.98f || drawn >= 4)
                    continue;
                RectTransform ring = MakeImage("GridRing_" + step, orbitsRoot, PaperMapArt.OrbitRing(),
                    WithAlpha(PaperMapArt.Ink, 0.13f), Vector2.one * (px * 2f), false);
                ring.anchoredPosition = Vector2.zero;
                drawn++;
            }
        }

        // ————— районы: выпуклые рамки «от руки» + крупные подписи —————

        private void BuildSectorFrames()
        {
            var groups = new Dictionary<Sector, List<CelestialBodyData>>();
            foreach (CelestialBodyData body in level.Bodies)
            {
                if (body.Type == BodyType.Comet)
                    continue; // живые строки данных; пояса участвуют центром
                if (!groups.TryGetValue(body.Sector, out List<CelestialBodyData> list))
                    groups[body.Sector] = list = new List<CelestialBodyData>();
                list.Add(body);
            }

            foreach (var pair in groups)
            {
                var pts = new List<Vector2>(pair.Value.Count);
                Vector2 center = Vector2.zero;
                float maxRadius = 60f;
                foreach (CelestialBodyData body in pair.Value)
                {
                    pts.Add(body.Position);
                    center += body.Position;
                    maxRadius = Mathf.Max(maxRadius, body.Radius);
                }
                center /= pair.Value.Count;
                float pad = maxRadius + 130f; // рамка не прижимается к телам

                Vector2 labelPos;
                var hull = ConvexHull(pts);
                if (hull.Count < 3)
                {
                    // одинокое тело (хаб) или отрезок — рамка по bbox
                    float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                    foreach (Vector2 p in pts)
                    {
                        minX = Mathf.Min(minX, p.x - pad);
                        maxX = Mathf.Max(maxX, p.x + pad);
                        minY = Mathf.Min(minY, p.y - pad);
                        maxY = Mathf.Max(maxY, p.y + pad);
                    }
                    hull = new List<Vector2>
                    {
                        new Vector2(minX, minY), new Vector2(maxX, minY),
                        new Vector2(maxX, maxY), new Vector2(minX, maxY)
                    };
                    labelPos = WorldToMap(new Vector2((minX + maxX) * 0.5f, maxY)) + Vector2.up * 22f;
                }
                else
                {
                    // расширяю вершины от центра района — тела остаются внутри с запасом
                    for (int i = 0; i < hull.Count; i++)
                    {
                        Vector2 dir = hull[i] - center;
                        hull[i] += dir.normalized * pad;
                    }
                    Vector2 top = hull[0];
                    foreach (Vector2 p in hull)
                        if (p.y > top.y)
                            top = p;
                    labelPos = WorldToMap(top) + Vector2.up * 24f;
                }

                var mapped = new List<Vector2>(hull.Count);
                foreach (Vector2 p in hull)
                    mapped.Add(WorldToMap(p));
                DrawSketchPolygon(sectorRoot, mapped, WithAlpha(PaperMapArt.Ink, 0.5f), 2.4f, 31 + (int)pair.Key * 17);

                // крупная рукописная подпись района, чуть с наклоном — как на старых картах
                TMP_Text label = MakeLabel("SectorLabel_" + pair.Key, sectorRoot, SectorName(pair.Key), 21f,
                    WithAlpha(PaperMapArt.Ink, 0.55f));
                RectTransform labelRect = (RectTransform)label.transform;
                labelRect.sizeDelta = new Vector2(340f, 30f);
                labelRect.anchoredPosition = labelPos;
                labelRect.localRotation = Quaternion.Euler(0f, 0f, (Hash01((int)pair.Key * 7 + 3) - 0.5f) * 4f);
                label.characterSpacing = 5f;
                labelObstacles.Add(RectCentered(labelPos, new Vector2(280f, 30f)));
            }
        }

        // замкнутый пунктир «от руки»: по ребрам — дэши с дрожью перпендикуляра
        private static void DrawSketchPolygon(RectTransform parent, List<Vector2> pts, Color color, float thickness, int seed)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % pts.Count];
                float len = Vector2.Distance(a, b);
                if (len < 2f)
                    continue;
                Vector2 dir = (b - a) / len;
                Vector2 perp = new Vector2(-dir.y, dir.x);
                int dashes = Mathf.Max(2, Mathf.RoundToInt(len / 26f));
                for (int j = 0; j < dashes; j++)
                {
                    float t0 = (j + 0.08f) / dashes;
                    float t1 = (j + 0.60f) / dashes; // дэш ~52% шага
                    Vector2 da = a + dir * (len * t0) + perp * (Wob(rng) * 3.0f);
                    Vector2 db = a + dir * (len * t1) + perp * (Wob(rng) * 3.0f);
                    MakeSegment(parent, da, db, color, thickness);
                }
            }
        }

        // рамка «от руки»: отрезки с дрожью руки по периметру + кляксы-углы
        private static void MakeSketchFrame(RectTransform parent, Color color, float thickness, int seed)
        {
            Rect r = parent.rect;
            var rng = new System.Random(seed);
            float cx = r.center.x, cy = r.center.y;
            Edge(new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax)); // верх
            Edge(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin)); // низ
            Edge(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax)); // лево
            Edge(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax)); // право

            void Edge(Vector2 a, Vector2 b)
            {
                Vector2 dir = (b - a).normalized;
                Vector2 perp = new Vector2(-dir.y, dir.x);
                int count = Mathf.Max(3, Mathf.RoundToInt((b - a).magnitude / 110f));
                Vector2 prev = a;
                for (int i = 1; i <= count; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)count);
                    if (i < count)
                        p += perp * (Wob(rng) * 3.5f); // дрожь стыка
                    MakeSegment(parent, prev, p, color, thickness);
                    prev = p;
                }
            }

            // кляксы-уголки: линия «выведена» за угол
            foreach (Vector2 c in new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin),
                     new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax) })
            {
                var go = new GameObject("Corner", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                RectTransform rect = (RectTransform)go.transform;
                rect.sizeDelta = Vector2.one * 10f;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(c.x - cx, c.y - cy);
                Image img = go.AddComponent<Image>();
                img.sprite = PaperMapArt.InkDot();
                img.color = color;
                img.raycastTarget = false;
            }
        }

        // отрезок чернил: капсула StubSprites.Line между двумя точками
        private static void MakeSegment(RectTransform parent, Vector2 a, Vector2 b, Color color, float thickness)
        {
            var go = new GameObject("Ink", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.sprite = StubSprites.Line();
            img.color = color;
            img.raycastTarget = false;
            RectTransform rect = (RectTransform)go.transform;
            Vector2 mid = (a + b) * 0.5f;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2((b - a).magnitude + thickness * 0.9f, thickness);
            rect.anchoredPosition = mid - (Vector2)parent.rect.center;
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        }

        private static float Wob(System.Random rng)
        {
            return (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        // детерминированный [0..1] по целому — наклоны подписей держатся между открытиями
        private static float Hash01(int seed)
        {
            uint h = (uint)seed ^ 0x9E3779B9u;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }

        // выпуклая оболочка (монотонная цепочка) — рамка района без самопересечений
        private static List<Vector2> ConvexHull(List<Vector2> points)
        {
            if (points.Count <= 2)
                return new List<Vector2>(points);
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>(2 * points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], points[i]) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(points[i]);
            }
            int lower = hull.Count + 1;
            for (int i = points.Count - 2; i >= 0; i--)
            {
                while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], points[i]) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(points[i]);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }

        // ————— орбиты / кометная зона / край мира —————

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

            var inkFaint = WithAlpha(PaperMapArt.Ink, 0.20f);
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
                    RectTransform orbit = MakeImage("Orbit_" + star.DisplayName + radii.Count, orbitsRoot,
                        PaperMapArt.OrbitRing(), inkFaint, Vector2.one * r * 2f * scale, false);
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

        private void BuildCometZone()
        {
            if (level.CometAreaRadius <= 0f) // хорды по всему миру — зону не рисуем
                return;
            float diameter = level.CometAreaRadius * 2f * scale;
            RectTransform ring = MakeImage("CometZone", edgeRoot, PaperMapArt.NetworkRing(),
                WithAlpha(PaperMapArt.CometPencil, 0.65f), Vector2.one * diameter, false);
            ring.anchoredPosition = WorldToMap(level.CometAreaCenter);

            Vector2 pos = WorldToMap(level.CometAreaCenter);
            TMP_Text label = MakeLabel("CometZoneLabel", edgeRoot, "кометная зона", 13f,
                WithAlpha(PaperMapArt.CometPencil, 0.95f));
            ((RectTransform)label.transform).sizeDelta = new Vector2(220f, 20f);
            ((RectTransform)label.transform).anchoredPosition = pos + Vector2.up * (diameter * 0.5f + 14f);
            labelObstacles.Add(RectCentered(pos + Vector2.up * (diameter * 0.5f + 14f), new Vector2(140f, 20f)));
        }

        // граница мира: рукописный пунктир + штриховка снаружи + буи-звёздочки
        private void BuildWorldEdge()
        {
            float netDiameter = mapRadius * 2f * scale;

            RectTransform ring = MakeImage("NetworkRing", edgeRoot, PaperMapArt.NetworkRing(),
                WithAlpha(PaperMapArt.Ink, 0.6f), Vector2.one * netDiameter, false);
            ring.anchoredPosition = Vector2.zero;

            RectTransform hatch = MakeImage("EdgeHatch", edgeRoot, PaperMapArt.HatchRing(),
                WithAlpha(PaperMapArt.Ink, 0.28f), Vector2.one * (netDiameter * 1.07f), false);
            hatch.anchoredPosition = Vector2.zero;

            Sprite star = PaperMapArt.BuoyStar();
            for (int i = 0; i < 12; i++)
            {
                float ang = i * 30f * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                RectTransform buoy = MakeImage("Buoy_" + i, edgeRoot, star,
                    WithAlpha(PaperMapArt.Ink, 0.7f), Vector2.one * 11f, false);
                buoy.anchoredPosition = dir * (mapRadius * scale);
                buoy.localRotation = Quaternion.Euler(0f, 0f, i * 17f);
            }

            // две тревожные пометки «от руки» — не стакаются (NE и SW)
            var warnColor = WithAlpha(PaperMapArt.RedPencil, 0.8f);
            for (int i = 0; i < 2; i++)
            {
                float ang = (45f + 180f * i) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                TMP_Text warn = MakeLabel("NoSignal_" + i, edgeRoot, "за кругом — потеря связи", 12f, warnColor);
                ((RectTransform)warn.transform).sizeDelta = new Vector2(240f, 18f);
                ((RectTransform)warn.transform).anchoredPosition = dir * (mapRadius * scale + 24f);
                ((RectTransform)warn.transform).localRotation = Quaternion.Euler(0f, 0f, i == 0 ? -2.5f : 2.5f);
            }
        }

        // ————— пояса астероидов: один спрайт, повороты, размеры, пунктирный путь —————

        private void CreateBelt(CelestialBodyData body)
        {
            float diameter = Mathf.Max(12f, body.Radius * 2f * scale);
            Vector2 pos = WorldToMap(body.Position);

            // пунктирная осевая линия — путь пояса (кликабельна)
            RectTransform ring = MakeImage("BeltPath_" + body.DisplayName, beltsRoot, PaperMapArt.BeltRing(),
                WithAlpha(PaperMapArt.BeltPencil, 0.85f), Vector2.one * diameter, true);
            ring.anchoredPosition = pos;
            bodySizes[body] = diameter;

            // камни: ОДИН спрайт map_asteroid — детерминированный разброс по кольцу
            Sprite rock = PaperMapArt.MapSprite("map_asteroid");
            if (rock != null)
            {
                var rng = new System.Random(StableHash(body.DisplayName));
                int count = Mathf.Clamp(body.BeltAsteroidCount / 3, 9, 14);
                for (int i = 0; i < count; i++)
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float rr = (body.Radius + (float)(rng.NextDouble() * 2.0 - 1.0) * body.BeltWidth * 0.85f) * scale;
                    Vector2 p = pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
                    float size = 9f + (float)rng.NextDouble() * 9f;
                    RectTransform stone = MakeImage("Rock_" + i, beltsRoot, rock, Color.white, Vector2.one * size, false);
                    stone.anchoredPosition = p;
                    stone.localRotation = Quaternion.Euler(0f, 0f, (float)rng.NextDouble() * 360f);
                    if (rng.NextDouble() < 0.5)
                        stone.localScale = new Vector3(-1f, 1f, 1f); // зеркалим — меньше повтора
                    labelObstacles.Add(RectCentered(p, Vector2.one * size));
                }
            }

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
            string beltName = body.DisplayName;
            if (!beltName.ToLower().Contains("пояс"))
                beltName = "пояс " + beltName;
            TMP_Text label = MakeLabel("Label_" + body.DisplayName, labelsRoot, beltName, LabelFontSize,
                WithAlpha(PaperMapArt.Ink, 0.85f));
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.sizeDelta = new Vector2(240f, 20f);
            labelRect.anchoredPosition = pos + Vector2.up * (diameter * 0.5f + 14f);
            RegisterLabel(labelRect, label, labelRect.anchoredPosition);
            bodyLabels[body] = label;
        }

        private static int StableHash(string text)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (char c in text)
                {
                    h ^= c;
                    h *= 16777619u;
                }
                return (int)h;
            }
        }

        // ————— тела: арт владельца с чернильной тенью-«наклейкой» —————

        private void CreateBody(CelestialBodyData body)
        {
            bool star = body.Type == BodyType.Star;
            bool hole = body.Type == BodyType.BlackHole;
            Sprite art = BodySprite(body);
            float d = Mathf.Max(star || hole ? 26f : 20f, body.Radius * 2f * scale * (star ? 1.55f : hole ? 1.35f : 1.5f));
            Vector2 spr = art != null ? new Vector2(art.rect.width, art.rect.height) : Vector2.one;
            float fit = d / Mathf.Max(spr.x, spr.y);
            Vector2 size = Vector2.Scale(spr, Vector2.one * fit); // большая сторона = d

            RectTransform group = MakeRect("Body_" + body.DisplayName, bodiesRoot, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            group.sizeDelta = size;
            Vector2 pos = WorldToMap(body.Position);
            group.anchoredPosition = pos;

            // тень со сдвигом — иконка «приклеена» к бумаге
            RectTransform shadow = MakeImage("Shadow", group, art, WithAlpha(PaperMapArt.Ink, 0.22f), size, false);
            shadow.anchoredPosition = new Vector2(2.5f, -2.5f);
            shadow.SetAsFirstSibling();

            RectTransform icon = MakeImage("Icon", group, art, Color.white, size, true);
            Image iconImage = icon.GetComponent<Image>();

            if (body.IsHub) // хаб — ржавно-карандашный пунктир вокруг, виден с базового зума
            {
                RectTransform hubRing = MakeImage("HubRing", group, PaperMapArt.OrbitRing(),
                    WithAlpha(PaperMapArt.Rust, 0.65f), Vector2.one * (d + 16f), false);
                hubRing.anchoredPosition = Vector2.zero;
            }

            var marker = group.gameObject.AddComponent<MapMarker>();
            marker.visual = iconImage;
            CelestialBodyData captured = body;
            marker.Clicked += () =>
            {
                if (suppressClick) // перетаскивание мимо объекта не открывает записки
                    return;
                if (selectedBody == captured)
                    Deselect(); // повторный клик закрывает
                else
                    SelectBody(captured);
            };
            marker.HoverEntered += () => ShowHoverRings(captured, true);
            marker.HoverExited += () => ShowHoverRings(captured, false);
            markers[body] = group;
            bodySizes[body] = d;

            labelObstacles.Add(RectCentered(pos, size * 0.92f));

            // подпись под иконкой; точную раскладку разводит ResolveLabels
            TMP_Text label = MakeLabel("Label_" + body.DisplayName, labelsRoot, body.DisplayName, LabelFontSize,
                WithAlpha(PaperMapArt.Ink, 0.95f));
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.sizeDelta = new Vector2(200f, 20f);
            labelRect.anchoredPosition = pos + Vector2.down * (size.y * 0.5f + 13f);
            RegisterLabel(labelRect, label, labelRect.anchoredPosition);
            bodyLabels[body] = label;
        }

        // спрайт тела: хаб — планета-такси, заправка/мастерская — «планеты со
        // станцией» (услуги читаются с иконки, отдельные значки не нужны)
        private static Sprite BodySprite(CelestialBodyData body)
        {
            switch (body.Type)
            {
                case BodyType.Star:
                    return PaperMapArt.MapSprite("map_sun");
                case BodyType.BlackHole:
                    return PaperMapArt.MapSprite("map_black_hole");
                default:
                    if (body.IsHub)
                        return PaperMapArt.MapSprite("map_taxi_planet");
                    if (body.HasFuel)
                        return PaperMapArt.MapSprite("map_gas_station");
                    if (body.HasRepair)
                        return PaperMapArt.MapSprite("map_repair");
                    return PaperMapArt.MapSprite("map_planet");
            }
        }

        // безделушки на свободных местах: детерминированные точки, далёкие от тел и поясов
        private void BuildDeco()
        {
            Sprite fun = PaperMapArt.MapSprite("map_for_fun_2");
            if (fun == null)
                return;
            var candidates = new[]
            {
                new Vector2(3300f, 2400f), new Vector2(-2400f, -2600f), new Vector2(3800f, 800f),
                new Vector2(-1400f, 1600f), new Vector2(2400f, -2800f), new Vector2(-3000f, 800f)
            };
            int placedCount = 0;
            foreach (Vector2 c in candidates)
            {
                if (!FreeSpot(c, 500f))
                    continue;
                RectTransform stamp = MakeImage("Deco_" + placedCount, decoRoot, fun,
                    WithAlpha(PaperMapArt.Ink, 0.55f), Vector2.one * 46f, false);
                stamp.anchoredPosition = WorldToMap(c);
                stamp.localRotation = Quaternion.Euler(0f, 0f, (Hash01(77 + placedCount) - 0.5f) * 60f);
                labelObstacles.Add(RectCentered(stamp.anchoredPosition, Vector2.one * 50f));
                if (++placedCount >= 2)
                    break;
            }
        }

        private bool FreeSpot(Vector2 p, float clear)
        {
            foreach (CelestialBodyData body in level.Bodies)
            {
                if (body.Type == BodyType.AsteroidBelt)
                {
                    if (Vector2.Distance(p, body.Position) < body.Radius + body.BeltWidth + clear)
                        return false;
                }
                else if (Vector2.Distance(p, body.Position) < clear + body.Radius)
                {
                    return false;
                }
            }
            return true;
        }

        // ————— подписи: раскладка без наложений —————

        // регистрация подписи: ширина по GetPreferredValues (точная для Excalifont)
        private void RegisterLabel(RectTransform rect, TMP_Text label, Vector2 basePos)
        {
            float prefW = label.GetPreferredValues().x;
            var ml = new MapLabel
            {
                Rect = rect,
                Width = Mathf.Clamp(prefW, 30f, 300f),
                Below = basePos,
                AboveY = -basePos.y // зеркально над иконкой
            };
            mapLabels.Add(ml);
        }

        // разводим подписи: базово под иконкой; занято — над, дальше, сбоку.
        // Препятствия: иконки тел, камни поясов, декор, подписи районов.
        private void ResolveLabels()
        {
            const float h = 20f, pad = 4f;
            mapLabels.Sort((a, b) => b.Below.y.CompareTo(a.Below.y)); // сверху вниз — стабильная раскладка
            var placed = new List<Rect>(labelObstacles);
            foreach (MapLabel ml in mapLabels)
            {
                float halfW = ml.Width * 0.5f + pad;

                Rect RectAt(Vector2 c) => new Rect(c.x - halfW, c.y - h * 0.5f - pad, ml.Width + pad * 2f, h + pad * 2f);
                var candidates = new[]
                {
                    ml.Below,
                    new Vector2(ml.Below.x, ml.AboveY),
                    ml.Below + Vector2.down * 22f,
                    new Vector2(ml.Below.x, ml.AboveY + 22f),
                    ml.Below + Vector2.down * 44f,
                    new Vector2(ml.Below.x, ml.AboveY + 44f),
                    ml.Below + Vector2.left * (halfW + 20f),
                    ml.Below + Vector2.right * (halfW + 20f)
                };
                Vector2 chosen = ml.Below;
                foreach (Vector2 c in candidates)
                {
                    Rect r = RectAt(c);
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
                placed.Add(RectAt(chosen));
                ml.Rect.anchoredPosition = chosen;
            }
        }

        private static Rect RectCentered(Vector2 c, Vector2 size)
        {
            return new Rect(c.x - size.x * 0.5f, c.y - size.y * 0.5f, size.x, size.y);
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
                    off.color = WithAlpha(PaperMapArt.Ink, 0.95f);
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
                label.color = PaperMapArt.Ink;
            }
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

        // ————— метка (waypoint) и цель заказа —————

        private void BuildWaypoint()
        {
            waypointRoot = MakeRect("WaypointRoot", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            waypointHalo = MakeImage("Halo", waypointRoot, PaperMapArt.TargetRing(),
                PaperMapArt.GreenPencil, Vector2.one * 40f, false);
            waypointPin = MakeImage("Pin", waypointRoot, PaperMapArt.Pin(),
                PaperMapArt.GreenPencil, Vector2.one * 20f, false);
            waypointRoot.gameObject.SetActive(false);
        }

        // зелёный пульс на теле метки + булавка над ним
        private void UpdateWaypointVisual()
        {
            if (waypointRoot == null)
                return;
            if (Waypoint == null)
            {
                if (waypointRoot.gameObject.activeSelf)
                    waypointRoot.gameObject.SetActive(false);
                return;
            }
            if (!waypointRoot.gameObject.activeSelf)
                waypointRoot.gameObject.SetActive(true);

            waypointRoot.anchoredPosition = WorldToMap(Waypoint.Position);
            float d = IconSizeOf(Waypoint);
            waypointHalo.sizeDelta = Vector2.one * (d + 24f);
            waypointPin.anchoredPosition = Vector2.up * (d * 0.5f + 16f);

            float t = Time.unscaledTime;
            float pulse = 0.5f + 0.3f * Mathf.Sin(t * 2.8f);
            waypointHalo.GetComponent<Image>().color = WithAlpha(PaperMapArt.GreenPencil, pulse);
            waypointPin.GetComponent<Image>().color = WithAlpha(PaperMapArt.GreenPencil, 0.65f + 0.35f * Mathf.Sin(t * 2.8f + 1f));
        }

        private void BuildTargetRing()
        {
            targetRing = MakeRect("TargetRing", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            targetRingPulse = MakeImage("Ring", targetRing, PaperMapArt.TargetRing(),
                PaperMapArt.YellowPencil, Vector2.one * 46f, false);
            targetRingWide = MakeImage("RingWide", targetRing, PaperMapArt.OrbitRing(),
                WithAlpha(PaperMapArt.YellowPencil, 0.7f), Vector2.one * 70f, false);
            targetLabel = MakeLabel("TargetLabel", targetRing, "", 15f, PaperMapArt.YellowPencil);
            ((RectTransform)targetLabel.transform).sizeDelta = new Vector2(260f, 20f);
            targetLabel.characterSpacing = 2f;
            targetRing.gameObject.SetActive(false);
            targetIndex = -1;
        }

        private float IconSizeOf(CelestialBodyData body)
        {
            return bodySizes.TryGetValue(body, out float size) ? size : 22f;
        }

        // цель активного заказа — жёлтый пульс (заметный издалека) + «цель: имя»
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
                float size = Mathf.Max(52f, IconSizeOf(body) + 30f);
                targetRingPulse.sizeDelta = Vector2.one * size;
                targetRingWide.sizeDelta = Vector2.one * (size + 26f);
                ((RectTransform)targetLabel.transform).anchoredPosition = Vector2.up * (size * 0.5f + 16f);
                targetLabel.text = "ЦЕЛЬ: " + body.DisplayName.ToUpper();
            }
            targetRing.anchoredPosition = WorldToMap(body.Position);
            // пульс: кольца дышат размером и альфой
            float t = Time.unscaledTime;
            targetRingPulse.localScale = Vector3.one * (1f + 0.09f * Mathf.Sin(t * 4.2f));
            targetRingPulse.GetComponent<Image>().color = WithAlpha(PaperMapArt.YellowPencil, 0.62f + 0.3f * Mathf.Sin(t * 3.4f));
            targetRingWide.GetComponent<Image>().color = WithAlpha(PaperMapArt.YellowPencil, 0.35f + 0.25f * Mathf.Sin(t * 3.4f + Mathf.PI));
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

        // ————— игрок: ракета, видна всегда —————

        private void BuildPlayerMarker()
        {
            playerMarker = MakeRect("PlayerMarker", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            playerMarker.sizeDelta = Vector2.one * 48f;
            playerArrowRoot = MakeRect("ArrowRoot", playerMarker, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            Sprite rocket = PaperMapArt.MapSprite("map_rocket");
            Vector2 size;
            if (rocket != null)
            {
                size = rocket.rect.height > rocket.rect.width
                    ? new Vector2(36f, 48f)
                    : new Vector2(48f, 36f);
                RectTransform shadow = MakeImage("Shadow", playerArrowRoot, rocket, WithAlpha(PaperMapArt.Ink, 0.25f), size, false);
                shadow.anchoredPosition = new Vector2(2f, -2f);
                shadow.SetAsFirstSibling();
                MakeImage("Rocket", playerArrowRoot, rocket, Color.white, size, false);
            }
            else
            {
                // арт не найден — шеврон в чернилах
                Sprite chev = WaypointIndicator.ChevronSprite() != null
                    ? WaypointIndicator.ChevronSprite()
                    : StubSprites.TargetChevron();
                MakeImage("Rocket", playerArrowRoot, chev, PaperMapArt.Ink, Vector2.one * 30f, false);
            }

            TMP_Text youLabel = MakeLabel("YouLabel", playerMarker, "ВЫ", 12f, PaperMapArt.Ink);
            ((RectTransform)youLabel.transform).sizeDelta = new Vector2(40f, 16f);
            ((RectTransform)youLabel.transform).anchoredPosition = new Vector2(0f, -31f);
        }

        // ракета видна всегда: размер не зависит от зума, за краем вида маркер
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
            if (offView) // за краем вида — ракета указывает на реальную позицию
            {
                Vector2 d = pos - clamped;
                angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - RocketNoseDeg;
            }
            else
            {
                angle = playerHeading - RocketNoseDeg; // нос арт-ракеты смотрит в 55°
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

        // ————— маршрут активного заказа: лёгкая пунктирная дуга корабль → цель —————

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

            var routeColor = WithAlpha(PaperMapArt.YellowPencil, 0.75f);
            routeShown = true;
            for (int i = 0; i < count; i++)
            {
                if (i >= routeDashes.Count) // пул растёт лениво, только пока карта открыта
                {
                    RectTransform dash = MakeImage("RouteDash_" + i, routeRoot, PaperMapArt.RouteDash(),
                        routeColor, new Vector2(dashLen, dashThick), false);
                    routeDashes.Add(dash);
                }
                RectTransform d = routeDashes[i];
                d.gameObject.SetActive(true);
                d.sizeDelta = new Vector2(dashLen, dashThick);
                d.GetComponent<Image>().color = routeColor;

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

        // ————— живые кометы: map_cometa по курсу —————

        private void UpdateComets()
        {
            if (cometsRoot == null)
                return;
            if (cometSystem == null)
                cometSystem = FindAnyObjectByType<CometSystem>();
            if (cometSystem == null)
                return;

            float inv = 1f / Mathf.Max(zoom, 0.001f);
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
                kv.Value.localScale = Vector3.one * inv; // размер на экране постоянен
                Vector2 delta = world - cometLastPos[kv.Key];
                if (delta.sqrMagnitude > 0.01f)
                    kv.Value.localRotation = Quaternion.Euler(0f, 0f,
                        Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - CometHeadDeg);
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
                Sprite art = PaperMapArt.MapSprite("map_cometa");
                if (art == null)
                    art = PaperMapArt.CometIcon();
                RectTransform icon = MakeImage("Comet_" + cometIcons.Count, cometsRoot, art, Color.white, Vector2.one * 30f, false);
                Vector2 world = comet.transform.position;
                icon.anchoredPosition = WorldToMap(world);
                icon.localScale = Vector3.one * inv;
                cometIcons[comet] = icon;
                cometLastPos[comet] = world;
            }
        }

        // ————— записки: «уголок листа» —————

        private void BuildNotePanel()
        {
            noteRoot = new GameObject("NotePanel", typeof(RectTransform));
            noteRoot.transform.SetParent(mapRect, false);
            RectTransform note = (RectTransform)noteRoot.transform;
            note.anchorMin = new Vector2(1f, 1f);
            note.anchorMax = new Vector2(1f, 1f);
            note.pivot = new Vector2(1f, 1f);
            note.sizeDelta = new Vector2(344f, 476f);
            note.anchoredPosition = new Vector2(-14f, -14f);
            note.localRotation = Quaternion.Euler(0f, 0f, -1.2f); // клочок наклеен чуть криво

            Image bg = noteRoot.AddComponent<Image>();
            bg.color = WithAlpha(PaperMapArt.Paper, 0.98f); // чуть светлее листа — «наклеен»
            bg.raycastTarget = true; // клики по записке не уходят в планшет
            MakeSketchFrame(note, WithAlpha(PaperMapArt.Ink, 0.85f), 2.2f, 77);

            // строки — слева сверху вниз (якорь 0,1, точка x=18)
            noteName = NoteRow("Name", note, 34f, 24f, PaperMapArt.Ink);
            noteSubtitle = NoteRow("Subtitle", note, 64f, 12f, PaperMapArt.Graphite);
            MakeDivider(note, 82f);
            noteServices = NoteRow("Services", note, 104f, 14f, PaperMapArt.Ink);
            noteFuel = NoteRow("Fuel", note, 148f, 13f, PaperMapArt.YellowPencil);
            noteRadius = NoteRow("Radius", note, 172f, 12f, PaperMapArt.Graphite);
            noteDistance = NoteRow("Distance", note, 196f, 13f, PaperMapArt.Graphite);
            MakeDivider(note, 218f);
            TMP_Text factsHeader = NoteRow("FactsHeader", note, 240f, 11f, WithAlpha(PaperMapArt.Graphite, 0.9f));
            factsHeader.text = "заметки на полях:";
            noteFacts = NoteRow("Facts", note, 262f, 13f, WithAlpha(PaperMapArt.Ink, 0.92f));
            ((RectTransform)noteFacts.transform).sizeDelta = new Vector2(308f, 128f);

            // кнопки: метка (зелёная, когда стоит) и «к началу»
            // (якорь — центр записки: локальный центр (−172, −238) при pivot (1,1))
            markButton = MakePaperButton("BtnMark", note, new Vector2(150f, 42f), new Vector2(-84f, -182f),
                "пометить", 14f, () =>
                {
                    if (selectedBody != null)
                        ToggleWaypoint(selectedBody);
                }, 901);
            markButtonImage = markButton.GetComponent<Image>();
            markLabel = markButton.GetComponentInChildren<TMP_Text>();

            MakePaperButton("BtnHome", note, new Vector2(108f, 42f), new Vector2(100f, -182f),
                "к началу", 13f, GoToStart, 902);

            TMP_Text hint = NoteRow("Hint", note, 456f, 10f, WithAlpha(PaperMapArt.Graphite, 0.8f), TextAlignmentOptions.Center);
            hint.text = "M / ESC — сложить карту";

            noteRoot.SetActive(false);
        }

        // строка записок: левый край, отступ 18, ширина 308
        private TMP_Text NoteRow(string name, RectTransform parent, float y, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            TMP_Text t = MakeLabel(name, parent, "", size, color, align);
            RectTransform r = (RectTransform)t.transform;
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(18f, -y);
            r.sizeDelta = new Vector2(308f, size * 1.5f);
            t.textWrappingMode = TextWrappingModes.Normal; // записи могут занять 2 строки
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        // волнистый разделитель — карандашный штрих
        private static void MakeDivider(RectTransform parent, float y)
        {
            RectTransform div = MakeImage("Divider", parent, PaperMapArt.RouteDash(),
                WithAlpha(PaperMapArt.Ink, 0.45f), new Vector2(300f, 5f), false);
            div.anchorMin = new Vector2(0f, 1f);
            div.anchorMax = new Vector2(0f, 1f);
            div.pivot = new Vector2(0.5f, 0.5f);
            div.anchoredPosition = new Vector2(172f, -y);
        }

        private void FillNote(CelestialBodyData body)
        {
            noteName.text = body.DisplayName;
            noteSubtitle.text = TypeName(body.Type).ToLower() + " · район " + SectorName(body.Sector).ToLower()
                + (body.IsHub ? " · хаб" : "");
            noteServices.text = ServicesLine(body);

            bool fuel = body.HasFuel || body.IsHub;
            noteFuel.gameObject.SetActive(fuel);
            if (fuel)
            {
                float perUnit = FuelNetwork.PricePerUnit(body.FuelTier, body.Sector, pricing);
                noteFuel.text = "топливо: Đ " + perUnit.ToString("0.##") + "/ед — сеть работает";
            }

            noteRadius.text = "радиус: " + body.Radius.ToString("0") + " юн";
            noteRefreshAt = 0f; // дистанция сразу, без троттлинга прошлого кадра
            UpdateNoteDistance();
            RefreshMarkButton();
            string[] facts = PaperMapLore.For(body);
            noteFacts.text = "- " + string.Join("\n- ", facts);
        }

        // что можно сделать на теле — из HasFuel/HasRepair/IsHub; рукописным стилем
        private static string ServicesLine(CelestialBodyData body)
        {
            if (body.IsHub)
                return "хаб МТО: заправка, мастерская, магазин";
            if (body.HasFuel && body.HasRepair)
                return "заправка · мастерская";
            if (body.HasFuel)
                return "заправка";
            if (body.HasRepair)
                return "мастерская";
            switch (body.Type)
            {
                case BodyType.Planet:
                    return "принимает и отдаёт заказы";
                case BodyType.AsteroidBelt:
                    return "урон корпусу при пролёте";
                case BodyType.Star:
                    return "услуг нет · обжигает";
                case BodyType.BlackHole:
                    return "услуг нет · затягивает";
                default:
                    return "услуг нет";
            }
        }

        private void UpdateNoteDistance()
        {
            if (selectedBody == null || noteDistance == null || Time.unscaledTime < noteRefreshAt)
                return;
            noteRefreshAt = Time.unscaledTime + 0.3f;
            Transform shipT = ShipPosition();
            float dist = shipT != null ? Vector2.Distance(shipT.position, selectedBody.Position) : 0f;
            noteDistance.text = "от вас: " + dist.ToString("0") + " юн";
        }

        private void RefreshMarkButton()
        {
            if (markLabel == null)
                return;
            bool marked = Waypoint != null && Waypoint == selectedBody;
            markLabel.text = marked ? "снять метку" : "пометить";
            if (markButtonImage != null) // отмеченный объект — зелёный карандаш
                markButtonImage.color = marked ? WithAlpha(PaperMapArt.GreenPencil, 0.30f) : PaperMapArt.PaperDark;
            markLabel.color = marked ? PaperMapArt.GreenPencil : PaperMapArt.Ink;
        }

        private void GoToStart()
        {
            // «к началу»: записки на стартовую планету; вид подводится через EnsureVisible
            Deselect();
            if (level == null || level.StartPlanetIndex < 0 || level.StartPlanetIndex >= level.Bodies.Count)
                return;
            CelestialBodyData start = level.Bodies[level.StartPlanetIndex];
            if (start.Type == BodyType.Planet)
                SelectBody(start);
        }

        // ————— выбор и записки —————

        // публично: оркестратор/тесты открывают записки объекта без эмуляции мыши
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

        // ————— вид: зум колесом/кнопками + пан драгом и WASD —————

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

        // WASD-пан (требование Н5.F). Игра под картой живёт: W — ещё и тяга
        // кораблю (договорённость владельца; конфликт задокументирован)
        private void UpdateWASDPan()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || content == null)
                return;
            Vector2 dir = Vector2.zero;
            if (kb.aKey.isPressed) dir.x -= 1f;
            if (kb.dKey.isPressed) dir.x += 1f;
            if (kb.sKey.isPressed) dir.y -= 1f;
            if (kb.wKey.isPressed) dir.y += 1f;
            if (dir == Vector2.zero)
                return;
            zoomAnim = false;
            float step = MapPanSpeed * Time.unscaledDeltaTime / Mathf.Max(zoom, 0.001f);
            ApplyZoom(zoom, ClampZoomOffset(zoomOffset + dir.normalized * step, zoom));
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
            // резиновый вынос за край; на отпускании лист плавно вернётся в границы мира
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

        // ————— экранное: компас, заголовок, легенда, линейка, кнопки —————

        // компас — роза ветров владельца (map_for_fun_1) с «С»
        private void BuildCompass()
        {
            compassRoot = MakeRect("Compass", mapRect, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            compassRoot.anchoredPosition = new Vector2(66f, -80f);
            compassRoot.sizeDelta = new Vector2(76f, 84f);

            Sprite rose = PaperMapArt.MapSprite("map_for_fun_1");
            RectTransform roseRect = MakeImage("Rose", compassRoot, rose, WithAlpha(PaperMapArt.Ink, 0.85f), Vector2.one * 62f, false);
            roseRect.anchoredPosition = new Vector2(0f, -7f);
            roseRect.localRotation = Quaternion.Euler(0f, 0f, 8f);

            TMP_Text n = MakeLabel("N", compassRoot, "С", 15f, WithAlpha(PaperMapArt.Ink, 0.85f));
            ((RectTransform)n.transform).sizeDelta = new Vector2(24f, 20f);
            ((RectTransform)n.transform).anchoredPosition = new Vector2(0f, 29f);
        }

        private void BuildTitle()
        {
            titleRoot = MakeRect("Title", mapRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            titleRoot.anchoredPosition = new Vector2(0f, -18f);
            titleRoot.sizeDelta = new Vector2(620f, 30f);
            titleRoot.localRotation = Quaternion.Euler(0f, 0f, 0.4f);
            TMP_Text title = MakeLabel("T", titleRoot, "маршрутный лист", 17f, WithAlpha(PaperMapArt.Ink, 0.7f));
            title.characterSpacing = 3f;
        }

        // легенда-клочок: две колонки, только значки владельца
        private void BuildLegend()
        {
            legendRoot = MakeRect("Legend", mapRect, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            legendRoot.pivot = new Vector2(0f, 0f);
            legendRoot.anchoredPosition = new Vector2(18f, 18f);
            legendRoot.sizeDelta = new Vector2(296f, 140f);

            RectTransform bg = MakeRect("Bg", legendRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image bgImage = bg.gameObject.AddComponent<Image>();
            bgImage.color = WithAlpha(PaperMapArt.Paper, 0.96f);
            bgImage.raycastTarget = true; // клики по легенде не снимают выбор
            MakeSketchFrame(legendRoot, WithAlpha(PaperMapArt.Ink, 0.8f), 2.2f, 555);

            int y = 0;
            LegendRow(18f, ref y, PaperMapArt.MapSprite("map_planet"), Color.white, "планета");
            LegendRow(18f, ref y, PaperMapArt.MapSprite("map_gas_station"), Color.white, "заправка");
            LegendRow(18f, ref y, PaperMapArt.MapSprite("map_repair"), Color.white, "мастерская");
            LegendRow(18f, ref y, PaperMapArt.MapSprite("map_taxi_planet"), Color.white, "хаб МТО");
            LegendRow(18f, ref y, PaperMapArt.MapSprite("map_sun"), Color.white, "звезда");
            LegendRow(18f, ref y, PaperMapArt.MapSprite("map_black_hole"), Color.white, "чёрная дыра");

            y = 0;
            LegendRow(158f, ref y, PaperMapArt.MapSprite("map_asteroid"), Color.white, "пояс");
            LegendRow(158f, ref y, PaperMapArt.MapSprite("map_cometa"), Color.white, "комета");
            LegendRow(158f, ref y, PaperMapArt.TargetRing(), PaperMapArt.YellowPencil, "цель заказа", 16f);
            LegendRow(158f, ref y, PaperMapArt.TargetRing(), PaperMapArt.GreenPencil, "своя метка", 16f);
            Sprite rocket = PaperMapArt.MapSprite("map_rocket");
            LegendRow(158f, ref y, rocket, Color.white, "вы здесь",
                rocket != null ? 15f : 13f);
        }

        private void LegendRow(float x, ref int row, Sprite icon, Color color, string text, float iconSize = 17f)
        {
            float y = 122f - row * 21f;
            RectTransform iconRect = MakeRect("I", legendRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            iconRect.sizeDelta = Vector2.one * 18f;
            iconRect.anchoredPosition = new Vector2(x + 9f, y);
            MakeImage("G", iconRect, icon, color, Vector2.one * iconSize, false);
            if (icon == null)
                MakeImage("F", iconRect, StubSprites.Circle(64f), WithAlpha(PaperMapArt.Ink, 0.7f), Vector2.one * 12f, false);

            TMP_Text rowLabel = MakeLabel("R", legendRoot, text, 12f, WithAlpha(PaperMapArt.Ink, 0.9f),
                TextAlignmentOptions.Left);
            ((RectTransform)rowLabel.transform).sizeDelta = new Vector2(110f, 16f);
            ((RectTransform)rowLabel.transform).anchorMin = new Vector2(0f, 0f);
            ((RectTransform)rowLabel.transform).anchorMax = new Vector2(0f, 0f);
            ((RectTransform)rowLabel.transform).pivot = new Vector2(0f, 0.5f);
            ((RectTransform)rowLabel.transform).anchoredPosition = new Vector2(x + 24f, y);
            row++;
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
            rulerRoot.anchoredPosition = new Vector2(-28f - px * 0.5f, 24f);
            rulerRoot.sizeDelta = new Vector2(px + 8f, 34f);
            var ink = WithAlpha(PaperMapArt.Ink, 0.8f);

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

        // кнопки зума: +/− к центру листа, «сброс» — вид «весь мир»
        private void BuildZoomButtons()
        {
            zoomRoot = MakeRect("ZoomButtons", mapRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
            zoomRoot.pivot = new Vector2(0.5f, 0f);
            zoomRoot.anchoredPosition = new Vector2(0f, 16f);
            zoomRoot.sizeDelta = new Vector2(200f, 44f);

            MakePaperButton("BtnZoomIn", zoomRoot, new Vector2(44f, 40f), new Vector2(-76f, 0f), "+", 20f,
                () => ZoomStep(ButtonZoomStep), 701);
            MakePaperButton("BtnZoomOut", zoomRoot, new Vector2(44f, 40f), new Vector2(-26f, 0f), "-", 20f,
                () => ZoomStep(1f / ButtonZoomStep), 702);
            MakePaperButton("BtnZoomReset", zoomRoot, new Vector2(96f, 40f), new Vector2(52f, 0f), "сброс", 13f,
                () => AnimateZoom(1f, Vector2.zero), 703);
        }

        // кнопка-клочок: бумага + рамка «от руки» + чернильный текст
        private Button MakePaperButton(string name, RectTransform parent, Vector2 size, Vector2 pos,
            string label, float fontSize, Action onClick, int seed)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;

            Image bg = go.GetComponent<Image>();
            bg.color = PaperMapArt.PaperDark;
            bg.raycastTarget = true;
            MakeSketchFrame(rect, WithAlpha(PaperMapArt.Ink, 0.9f), 2f, seed);

            TMP_Text text = MakeLabel("T", rect, label, fontSize, PaperMapArt.Ink);
            RectTransform textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = bg; // tint-подсветка на hover/нажатие
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.fadeDuration = 0.1f;
            colors.highlightedColor = new Color(0.93f, 0.84f, 0.60f);
            colors.pressedColor = new Color(0.72f, 0.62f, 0.42f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());
            UiSfx.Hook(button);
            return button;
        }

        private void RestackChrome()
        {
            // экранное поверх листа: компас, заголовок, легенда, линейка, кнопки, записки
            if (legendRoot != null) legendRoot.SetAsLastSibling();
            if (compassRoot != null) compassRoot.SetAsLastSibling();
            if (titleRoot != null) titleRoot.SetAsLastSibling();
            if (rulerRoot != null) rulerRoot.SetAsLastSibling();
            if (zoomRoot != null) zoomRoot.SetAsLastSibling();
            if (noteRoot != null) noteRoot.transform.SetAsLastSibling();
        }

        private void RebuildAll()
        {
            ClearChildren(transform);
            sheetRect = null;
            content = null;
            sectorRoot = null;
            orbitsRoot = null;
            edgeRoot = null;
            beltsRoot = null;
            decoRoot = null;
            bodiesRoot = null;
            labelsRoot = null;
            cometsRoot = null;
            noteRoot = null;
            legendRoot = null;
            compassRoot = null;
            rulerRoot = null;
            zoomRoot = null;
            titleRoot = null;
            markers.Clear();
            bodySizes.Clear();
            bodyLabels.Clear();
            mapLabels.Clear();
            labelObstacles.Clear();
            cometIcons.Clear();
            cometLastPos.Clear();
            routeRoot = null;
            routeDashes.Clear();
            hoverCaptureRing = null;
            hoverLandingRing = null;
            playerMarker = null;
            playerArrowRoot = null;
            targetRing = null;
            targetRingPulse = null;
            targetRingWide = null;
            targetLabel = null;
            waypointRoot = null;
            waypointHalo = null;
            waypointPin = null;
            targetIndex = -1;
            lastTargetBody = null;
            routeShown = false;
            selectedBody = null;
            noteName = null;
            noteSubtitle = null;
            noteServices = null;
            noteFuel = null;
            noteRadius = null;
            noteDistance = null;
            noteFacts = null;
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

        // все подписи карты одним рукописным шрифтом; высоты узлов ≥ fontSize*1.32 (урок ночи 4)
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
