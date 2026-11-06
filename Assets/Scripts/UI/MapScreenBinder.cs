using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Карта «экспедиционная бумага»: лист на тёмной подложке, карандашные тела,
    // орбиты, сектора, сеть буёв по границе мира. Видом управляет рука: ЛКМ-драг — пан,
    // колесо мыши — зум к курсору; клик по объекту открывает записку.
    // Арт берётся из Resources/Map (PaperMapArt), без него — процедурный фолбэк.
    public class MapScreenBinder : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private TMP_FontAsset mapFont; // назначает тул Space Taxi/Fonts/Create TMP Asset

        // — вид: базовый масштаб подбирается по фактическим телам уровня —
        private const float MinViewZoom = 0.5f;
        private const float MaxViewZoom = 3f;
        private const float ClickZoom = 1.8f;    // клик по объекту поднимает вид до этого зума
        private const float ZoomDuration = 0.25f;
        private const float WheelStep = 1.15f;
        private const float ClickDragGuard = 6f; // сдвиг больше этого — пан, а не выбор объекта
        private const float RubberMargin = 1.15f; // мягкий вынос листа за край при перетаскивании
        private const float MinNeighborPx = 50f;  // целевой зазор между ближайшими телами в базовом виде

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

        // Esc из контроллера: true — съеден запиской/зумом, false — карта закрывается целиком
        public bool TryHandleEscape()
        {
            if (selectedBody == null)
                return false;
            Deselect();
            return true;
        }

        private LevelAsset level;
        private GameSession session;
        private Ship ship;
        private RectTransform mapRect;
        private TMP_FontAsset font;
        private bool built;
        private float builtWidth;
        private Camera canvasCam; // null для Screen Space Overlay — так и нужно утилитам

        private RectTransform sheetRect;
        private RectTransform content; // зум-контейнер: всё, что на листе
        private GameObject noteRoot;
        private RectTransform legendRoot;
        private RectTransform compassRoot;
        private RectTransform rulerRoot;

        private readonly Dictionary<CelestialBodyData, RectTransform> markers = new Dictionary<CelestialBodyData, RectTransform>();
        private readonly Dictionary<CelestialBodyData, RectTransform> icons = new Dictionary<CelestialBodyData, RectTransform>();
        private readonly Dictionary<CelestialBodyData, float> bodySizes = new Dictionary<CelestialBodyData, float>();

        private RectTransform playerMarker;
        private RectTransform playerArrowRoot; // вращается по курсу; обёртка с подписью не крутится
        private RectTransform targetRing;
        private Image targetRingImage;
        private TMP_Text targetNameLabel;
        private TMP_Text targetTagLabel;
        private RectTransform waypointPin;
        private int targetIndex = -1;
        private float playerHeading;

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

        // пан перетаскиванием: ЛКМ-драг по листу; клик после драга не выбирает объект
        private bool dragging;
        private bool suppressClick;
        private float dragMoved;
        private Vector2 dragSheetPos;

        private CelestialBodyData selectedBody;
        private TMP_Text noteName;
        private TMP_Text noteType;
        private TMP_Text noteServices;
        private TMP_Text noteDistance;
        private TMP_Text markLabel;
        private Button markButton;
        private float noteRefreshAt;

        private static readonly Vector3[] sectorColors =
        {
            new Vector3(0.55f, 0.44f, 0.20f), // Hub
            new Vector3(0.66f, 0.52f, 0.28f), // Near
            new Vector3(0.63f, 0.44f, 0.26f), // Mid1
            new Vector3(0.58f, 0.38f, 0.28f), // Mid2
            new Vector3(0.46f, 0.36f, 0.40f), // Far
        };

        // hand-drawn вайб: детерминированный наклон/микросмещение по имени — сид держится между открытиями
        private static readonly Dictionary<string, Vector3> jitterCache = new Dictionary<string, Vector3>();

        private static int SeedOf(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;
            uint h = 2166136261u;
            foreach (char c in name)
                h = (h ^ c) * 16777619u;
            return (int)(h & 0x7fffffff);
        }

        private static Vector3 JitterOf(string name)
        {
            if (string.IsNullOrEmpty(name))
                return Vector3.zero;
            if (jitterCache.TryGetValue(name, out Vector3 v))
                return v;
            uint h = (uint)SeedOf(name);
            float tilt = ((h >> 8) % 5u) - 2f;        // −2..2°
            float dx = (((h >> 12) % 7u) - 3f) * 0.8f; // ±2.4 px
            float dy = (((h >> 16) % 7u) - 3f) * 0.8f;
            v = new Vector3(tilt, dx, dy);
            jitterCache[name] = v;
            return v;
        }

        private void Awake()
        {
            mapRect = (RectTransform)transform;
            Canvas canvas = GetComponentInParent<Canvas>();
            canvasCam = canvas != null ? canvas.worldCamera : null;

            TMP_FontAsset fallback = null;
            Transform title = transform.Find("Title");
            if (title != null)
                fallback = title.GetComponent<TMP_Text>().font;

            // заготовку из сцены стираем целиком — лист рисуем кодом
            ClearChildren(transform);
            font = ResolveFont(fallback);
            BuildChrome();
        }

        // шрифт: тул/инспектор → Resources → остаток от заготовки сцены
        private TMP_FontAsset ResolveFont(TMP_FontAsset fallback)
        {
            if (mapFont != null)
                return mapFont;
            TMP_FontAsset res = Resources.Load<TMP_FontAsset>("Fonts/TMP/Excalifont SDF");
            if (res == null)
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
            UpdateComets();
            UpdateNoteDistance();
        }

        private void OnDisable()
        {
            // карта закрылась — вид и записку сбрасываем, следующий вход с чистого листа
            selectedBody = null;
            if (noteRoot != null)
                noteRoot.SetActive(false);
            dragging = false;
            suppressClick = false;
            if (content != null)
                ApplyZoom(1f, Vector2.zero);
            zoomAnim = false;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        // ————— постройка —————

        private void BuildChrome()
        {
            // тёмная подложка на весь экран карты; клик мимо листа тоже снимает выбор
            RectTransform backdrop = MakeRect("Backdrop", mapRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image backImage = backdrop.gameObject.AddComponent<Image>();
            backImage.color = PaperMapArt.Night;
            backImage.raycastTarget = true;
            MapMarker backMarker = backdrop.gameObject.AddComponent<MapMarker>();
            backMarker.hoverEnabled = false;
            backMarker.Clicked += () =>
            {
                if (!suppressClick) // клик после пана — не «снять выбор»
                    Deselect();
            };

            // лист: арт-бумага или тёплый жёлтый цвет с мягким процедурным краем
            Sprite paper = PaperMapArt.Sheet();
            sheetRect = MakeRect("Sheet", mapRect, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -8f));
            Image sheetImage = sheetRect.gameObject.AddComponent<Image>();
            sheetImage.sprite = paper;
            sheetImage.type = paper != null ? Image.Type.Simple : Image.Type.Sliced;
            sheetImage.color = paper != null ? Color.white : PaperMapArt.Paper;
            sheetImage.raycastTarget = false; // клики сквозь лист уходят в Backdrop

            content = MakeRect("Content", sheetRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            content.gameObject.AddComponent<RectMask2D>(); // орбиты/пояса не вылезают за лист

            BuildNote();
            BuildLegend();
            BuildCompass();
            BuildRuler();
        }

        private void BuildMap()
        {
            // граница мира = кольцо буёв (HullSystem бросает «ПОТЕРЯНА СВЯЗЬ» от origin);
            // тела не должны вылезать, но подстрахуемся
            mapRadius = level.WorldRadius;
            float bodyExtent = 0f;
            foreach (CelestialBodyData body in level.Bodies)
            {
                float edge = body.Position.magnitude + body.Radius;
                if (edge > mapRadius)
                    mapRadius = edge + 100f;
                if (edge > bodyExtent)
                    bodyExtent = edge;
            }
            if (bodyExtent <= 0f)
                bodyExtent = mapRadius;
            worldCenter = Vector2.zero;

            // базовый масштаб: вмещаем тела уровня, но не даём соседям слиться.
            // BetaMap01: минимальный зазор ~276 юн → 50/276 ≈ 0.18 — соседи ≥ ~57 px
            // между центрами, карта заметно больше вьюпорта (пан обязателен)
            float fitScale = Mathf.Min(mapRect.rect.width, mapRect.rect.height) / (2f * bodyExtent * 1.06f);
            float minGap = MinNeighborGap();
            scale = fitScale;
            if (minGap > 1f)
                scale = Mathf.Max(scale, MinNeighborPx / minGap);
            scale = Mathf.Min(scale, fitScale * 4f); // защита от вырожденно близких тел

            BuildSectors();
            BuildCometZone();
            BuildOrbits();
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type == BodyType.AsteroidBelt)
                    CreateBelt(body);
            BuildNetwork();
            for (int i = 0; i < level.Bodies.Count; i++)
                if (level.Bodies[i].Type != BodyType.AsteroidBelt)
                    CreateBody(level.Bodies[i], i);

            Sprite pinSprite = PaperMapArt.Icon(8) ?? PaperMapArt.Stamp(3);
            waypointPin = MakeImage("WaypointPin", content,
                pinSprite ?? PaperMapArt.Pin(),
                pinSprite != null ? Color.white : PaperMapArt.Rust, new Vector2(26f, 26f), false);
            waypointPin.gameObject.SetActive(false);

            // цель заказа: пульсирующее кольцо + имя планеты + пометка «ЦЕЛЬ»
            targetRing = MakeRect("TargetRing", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            RectTransform ringImage = MakeImage("Ring", targetRing, PaperMapArt.TargetRing(),
                new Color(PaperMapArt.Rust.r, PaperMapArt.Rust.g, PaperMapArt.Rust.b, 0.9f),
                Vector2.one * 46f, false);
            targetRingImage = ringImage.GetComponent<Image>();
            targetNameLabel = MakeLabel("TargetName", targetRing, "", 15f, PaperMapArt.Ink);
            ((RectTransform)targetNameLabel.transform).sizeDelta = new Vector2(200f, 20f);
            targetNameLabel.fontStyle = FontStyles.Bold;
            targetTagLabel = MakeLabel("TargetTag", targetRing, "ЦЕЛЬ", 14f, PaperMapArt.Rust);
            ((RectTransform)targetTagLabel.transform).sizeDelta = new Vector2(160f, 18f);
            targetTagLabel.fontStyle = FontStyles.Bold;
            targetRing.gameObject.SetActive(false);
            targetIndex = -1;

            // герой: крупная карандашная стрелка с обводом + подпись «ВЫ»
            playerMarker = MakeRect("PlayerMarker", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            playerMarker.sizeDelta = Vector2.one * 34f;
            playerArrowRoot = MakeRect("ArrowRoot", playerMarker, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Sprite shipArt = PaperMapArt.Icon(6);
            if (shipArt != null)
            {
                MakeImage("Icon", playerArrowRoot, shipArt, Color.white, Vector2.one * 34f, false);
            }
            else
            {
                // бумажное пятно под стрелкой — читается на любой чернильной линии
                RectTransform under = MakeImage("Under", playerArrowRoot, StubSprites.Circle(64f),
                    new Color(PaperMapArt.Paper.r, PaperMapArt.Paper.g, PaperMapArt.Paper.b, 0.92f), Vector2.one * 46f, false);
                under.anchoredPosition = Vector2.zero;
                RectTransform outline = MakeImage("Outline", playerArrowRoot, StubSprites.ShipTriangle(),
                    PaperMapArt.Ink, Vector2.one * 40f, false);
                outline.anchoredPosition = new Vector2(1.5f, -1.5f); // второй проход карандаша со сдвигом
                MakeImage("Arrow", playerArrowRoot, StubSprites.ShipTriangle(), PaperMapArt.HeroPencil, Vector2.one * 33f, false);
            }

            RectTransform youChip = MakeImage("YouChip", playerMarker, PaperMapArt.InkFrame(),
                PaperMapArt.Paper, new Vector2(54f, 36f), false);
            youChip.GetComponent<Image>().type = Image.Type.Sliced;
            youChip.anchoredPosition = new Vector2(0f, -40f);
            TMP_Text youLabel = MakeLabel("YouLabel", playerMarker, "ВЫ", 15f, PaperMapArt.HeroPencil);
            ((RectTransform)youLabel.transform).sizeDelta = new Vector2(54f, 20f);
            ((RectTransform)youLabel.transform).anchoredPosition = new Vector2(0f, -40f);
            youLabel.fontStyle = FontStyles.Bold;

            ApplyZoom(1f, Vector2.zero);
            RefreshWaypointPin();
        }

        // минимальный зазор между телами (без поясов и комет) — по фактическим позициям уровня
        private float MinNeighborGap()
        {
            var solid = new List<CelestialBodyData>();
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type != BodyType.AsteroidBelt && body.Type != BodyType.Comet)
                    solid.Add(body);
            float gap = float.MaxValue;
            for (int i = 0; i < solid.Count; i++)
                for (int j = i + 1; j < solid.Count; j++)
                {
                    float d = Vector2.Distance(solid[i].Position, solid[j].Position)
                              - solid[i].Radius - solid[j].Radius;
                    if (d > 0f && d < gap)
                        gap = d;
                }
            return gap;
        }

        // районы: тёплая клякса (спрайт-болванка × масштаб) + рукописная подпись
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

                float radiusWorld = 350f; // минимум, чтобы клякса читалась
                foreach (CelestialBodyData body in pair.Value)
                    radiusWorld = Mathf.Max(radiusWorld, Vector2.Distance(center, body.Position) + 200f);

                Vector3 sc = sectorColors[(int)pair.Key % sectorColors.Length];
                var tint = new Color(sc.x, sc.y, sc.z, 0.06f);
                RectTransform blob = MakeImage("Sector_" + pair.Key, content, StubSprites.Circle(64f),
                    tint, Vector2.one * radiusWorld * 2f * scale, false);
                blob.anchoredPosition = WorldToMap(center);

                TMP_Text label = MakeLabel("SectorLabel_" + pair.Key, content, SectorName(pair.Key), 20f,
                    new Color(sc.x, sc.y, sc.z, 0.45f));
                ((RectTransform)label.transform).sizeDelta = new Vector2(240f, 26f);
                ((RectTransform)label.transform).anchoredPosition = WorldToMap(center) + Vector2.up * (radiusWorld * scale * 0.4f);
            }
        }

        private void BuildCometZone()
        {
            if (level.CometAreaRadius <= 0f) // хорды по всему миру — зону не рисуем
                return;
            float diameter = level.CometAreaRadius * 2f * scale;
            RectTransform ring = MakeImage("CometZone", content, PaperMapArt.NetworkRing(),
                new Color(PaperMapArt.Rust.r, PaperMapArt.Rust.g, PaperMapArt.Rust.b, 0.30f),
                Vector2.one * diameter, false);
            ring.anchoredPosition = WorldToMap(level.CometAreaCenter);

            // значок кометы в зоне — зона видна с первого взгляда
            RectTransform zoneIcon = MakeImage("CometZoneIcon", content, PaperMapArt.CometIcon(),
                new Color(PaperMapArt.CometPencil.r, PaperMapArt.CometPencil.g, PaperMapArt.CometPencil.b, 0.85f),
                Vector2.one * 38f, false);
            zoneIcon.anchoredPosition = WorldToMap(level.CometAreaCenter) + Vector2.down * (diameter * 0.5f - 30f);

            TMP_Text label = MakeLabel("CometZoneLabel", content, "КОМЕТНАЯ ЗОНА", 15f,
                new Color(PaperMapArt.Rust.r, PaperMapArt.Rust.g, PaperMapArt.Rust.b, 0.75f));
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

            var inkFaint = new Color(PaperMapArt.Ink.r, PaperMapArt.Ink.g, PaperMapArt.Ink.b, 0.10f);
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

        // сеть буёв по границе мира: штриховка снаружи + пунктир + буи + «ПОТЕРЯ СВЯЗИ»
        private void BuildNetwork()
        {
            float netDiameter = mapRadius * 2f * scale;

            RectTransform hatch = MakeImage("OutsideHatch", content, PaperMapArt.HatchRing(),
                new Color(PaperMapArt.Rust.r, PaperMapArt.Rust.g, PaperMapArt.Rust.b, 0.10f),
                Vector2.one * netDiameter * 1.16f, false);
            hatch.anchoredPosition = Vector2.zero;

            RectTransform ring = MakeImage("NetworkRing", content, PaperMapArt.NetworkRing(),
                new Color(PaperMapArt.Ink.r, PaperMapArt.Ink.g, PaperMapArt.Ink.b, 0.55f),
                Vector2.one * netDiameter, false);
            ring.anchoredPosition = Vector2.zero;

            // буи: 12 по кругу; иконка арта или карандашная точка, чуть повёрнутая рукой
            Sprite buoySprite = PaperMapArt.Icon(5);
            for (int i = 0; i < 12; i++)
            {
                float ang = i * 30f * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                RectTransform buoy = MakeImage("Buoy_" + i, content, buoySprite ?? StubSprites.Circle(64f),
                    buoySprite != null ? Color.white : PaperMapArt.Ink, Vector2.one * 11f, false);
                buoy.anchoredPosition = dir * (mapRadius * scale);
                buoy.localRotation = Quaternion.Euler(0f, 0f, JitterOf("Buoy" + i).x);
                if (buoySprite == null)
                {
                    RectTransform dot = MakeImage("BuoyDot_" + i, buoy, StubSprites.Circle(64f),
                        PaperMapArt.Paper, Vector2.one * 4.5f, false);
                    dot.anchoredPosition = Vector2.zero;
                }
            }

            var warnColor = new Color(PaperMapArt.Rust.r, PaperMapArt.Rust.g, PaperMapArt.Rust.b, 0.85f);
            for (int i = 0; i < 4; i++) // диагонали — между буями, внутрь листа помещаются
            {
                float ang = (45f + 90f * i) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                TMP_Text warn = MakeLabel("NoSignal_" + i, content, "ПОТЕРЯ СВЯЗИ", 13f, warnColor);
                ((RectTransform)warn.transform).sizeDelta = new Vector2(160f, 16f);
                ((RectTransform)warn.transform).anchoredPosition = dir * (mapRadius * scale + 22f);
            }
        }

        private void CreateBody(CelestialBodyData body, int index)
        {
            bool star = body.Type == BodyType.Star;
            bool hole = body.Type == BodyType.BlackHole;
            float size = Mathf.Max(20f, body.Radius * 2f * scale * (star ? 1.6f : hole ? 1.0f : 1.9f));
            int seed = SeedOf(body.DisplayName);

            Vector3 jitter = JitterOf(body.DisplayName);
            RectTransform group = MakeRect("Body_" + body.DisplayName, content, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            group.sizeDelta = Vector2.one * size;
            group.anchoredPosition = WorldToMap(body.Position);

            if (star) // тёплый карандашный ореол звезды
            {
                RectTransform halo = MakeImage("Halo", group, StubSprites.Circle(64f),
                    new Color(PaperMapArt.StarPencil.r, PaperMapArt.StarPencil.g, PaperMapArt.StarPencil.b, 0.16f),
                    Vector2.one * size * 1.9f, false);
                halo.anchoredPosition = Vector2.zero;
            }

            Sprite artSprite = PaperMapArt.Icon(hole ? 2 : star ? 1 : 0);
            Sprite iconSprite = artSprite;
            Color iconColor;
            if (iconSprite == null) // процедурный карандаш по сиду тела — дрожь руки стабильна
            {
                if (hole)
                    iconSprite = PaperMapArt.HoleSketch(seed);
                else if (star)
                    iconSprite = PaperMapArt.StarSketch(seed);
                else if (body.Type == BodyType.Comet)
                    iconSprite = PaperMapArt.CometIcon();
                else
                    iconSprite = PaperMapArt.PlanetPencil(seed);
                iconColor = body.Type == BodyType.Comet ? PaperMapArt.CometPencil : PencilColor(body, star, hole);
            }
            else
            {
                iconColor = Color.white;
            }
            RectTransform icon = MakeImage("Icon", group, iconSprite, iconColor, Vector2.one * size, true);
            icon.anchoredPosition = new Vector2(jitter.y, jitter.z);
            icon.localRotation = Quaternion.Euler(0f, 0f, jitter.x);

            if (hole) // фиолетовый обвод — «горизонт событий»
            {
                RectTransform edge = MakeImage("Edge", group, PaperMapArt.TargetRing(),
                    new Color(0.45f, 0.32f, 0.55f, 0.6f), Vector2.one * (size + 10f), false);
                edge.anchoredPosition = Vector2.zero;
            }

            // услуги Н-3: без арта — буквы в кружке (З заправка / М мастерская / Х хаб)
            int badge = 0;
            if (body.IsHub)
                CreateBadge(group, "Х", 17f, new Vector2(size * 0.5f + 14f, size * 0.22f), PaperMapArt.Rust);
            else
            {
                if (body.HasFuel)
                    CreateBadge(group, "З", 15f, new Vector2(size * 0.5f + 12f + badge++ * 18f, size * 0.22f), PaperMapArt.Ink);
                if (body.HasRepair)
                    CreateBadge(group, "М", 15f, new Vector2(size * 0.5f + 12f + badge++ * 18f, size * 0.22f), PaperMapArt.Ink);
            }

            var marker = group.gameObject.AddComponent<MapMarker>();
            marker.visual = icon.GetComponent<Image>();
            CelestialBodyData captured = body;
            marker.Clicked += () =>
            {
                if (!suppressClick) // перетаскивание мимо объекта не открывает записку
                    SelectBody(captured);
            };
            markers[body] = group;
            icons[body] = icon;
            bodySizes[body] = size;

            // подпись сверху/снизу по чётности индекса — у близких пар не сталкивается
            bool below = index % 2 == 0;
            TMP_Text label = MakeLabel("Label_" + body.DisplayName, content, body.DisplayName.ToUpper(), 15f,
                new Color(PaperMapArt.Ink.r, PaperMapArt.Ink.g, PaperMapArt.Ink.b, 0.8f));
            ((RectTransform)label.transform).sizeDelta = new Vector2(160f, 18f);
            ((RectTransform)label.transform).anchoredPosition =
                WorldToMap(body.Position) + (below ? Vector2.down : Vector2.up) * (size * 0.5f + 14f);
        }

        // пояс — штриховая каракуля-дуга осевой линии
        private void CreateBelt(CelestialBodyData body)
        {
            float diameter = Mathf.Max(8f, body.Radius * 2f * scale);
            RectTransform ring = MakeImage("Belt_" + body.DisplayName, content, PaperMapArt.BeltRing(),
                PaperMapArt.BeltPencil, Vector2.one * diameter, false);
            ring.anchoredPosition = WorldToMap(body.Position);
            bodySizes[body] = diameter;

            if (string.IsNullOrEmpty(body.DisplayName))
                return;
            string beltName = body.DisplayName.ToUpper();
            if (!beltName.Contains("ПОЯС"))
                beltName += " · ПОЯС";
            TMP_Text label = MakeLabel("Label_" + body.DisplayName, content, beltName, 15f,
                new Color(PaperMapArt.Ink.r, PaperMapArt.Ink.g, PaperMapArt.Ink.b, 0.8f));
            ((RectTransform)label.transform).sizeDelta = new Vector2(260f, 18f);
            ((RectTransform)label.transform).anchoredPosition = WorldToMap(body.Position) + Vector2.up * (diameter * 0.5f + 14f);
        }

        private void CreateBadge(RectTransform group, string letter, float diameter, Vector2 pos, Color letterColor)
        {
            RectTransform circle = MakeImage("Badge_" + letter, group, StubSprites.Circle(64f),
                PaperMapArt.PaperDark, Vector2.one * diameter, false);
            circle.anchoredPosition = pos;
            TMP_Text text = MakeLabel("T", circle, letter, diameter * 0.62f, letterColor);
            ((RectTransform)text.transform).sizeDelta = Vector2.one * diameter;
            ((RectTransform)text.transform).anchoredPosition = Vector2.zero;
        }

        private Color PencilColor(CelestialBodyData body, bool star, bool hole)
        {
            if (star)
                return PaperMapArt.StarPencil;
            if (hole)
                return PaperMapArt.HolePencil;
            if (body.Visuals != null && body.Visuals.UseColor)
                return body.Visuals.Color; // автор уровня попросил свой цвет
            Vector3 sc = sectorColors[(int)body.Sector % sectorColors.Length];
            return Color.Lerp(PaperMapArt.Graphite, new Color(sc.x, sc.y, sc.z), 0.5f); // графит + цветной карандаш сектора
        }

        // ————— легенда / компас / линейка (вне зум-контейнера) —————

        private void BuildLegend()
        {
            legendRoot = MakeRect("Legend", mapRect, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            legendRoot.pivot = new Vector2(0f, 0f);
            legendRoot.anchoredPosition = new Vector2(24f, 22f);
            legendRoot.sizeDelta = new Vector2(206f, 214f);

            RectTransform bg = MakeRect("Bg", legendRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image bgImage = bg.gameObject.AddComponent<Image>();
            bgImage.sprite = PaperMapArt.Note();
            bgImage.color = bgImage.sprite != null ? new Color(1f, 1f, 1f, 0.97f) : PaperMapArt.Paper;
            bgImage.raycastTarget = true; // клики по легенде не снимают выбор
            RectTransform frame = MakeRect("Frame", legendRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.sprite = PaperMapArt.InkFrame();
            frameImage.type = Image.Type.Sliced;
            frameImage.color = new Color(PaperMapArt.Ink.r, PaperMapArt.Ink.g, PaperMapArt.Ink.b, 0.55f);
            frameImage.raycastTarget = false;

            float y = 198f; // ряды сверху вниз; pivot легенды (0,0) — y положительный
            LegendRow(ref y, null, PaperMapArt.Graphite, "планета");
            LegendRow(ref y, null, PaperMapArt.StarPencil, "звезда");
            LegendRow(ref y, null, PaperMapArt.HolePencil, "чёрная дыра");
            LegendRow(ref y, null, PaperMapArt.CometPencil, "комета", icon: PaperMapArt.CometIcon());
            LegendRow(ref y, null, PaperMapArt.BeltPencil, "пояс астероидов", icon: PaperMapArt.BeltRing());
            LegendRow(ref y, "З", PaperMapArt.PaperDark, "заправка");
            LegendRow(ref y, "М", PaperMapArt.PaperDark, "мастерская");
            LegendRow(ref y, "Х", PaperMapArt.PaperDark, "хаб МТО");
            LegendRow(ref y, null, PaperMapArt.Ink, "буй связи", dashed: true);
            LegendRow(ref y, null, PaperMapArt.Rust, "цель заказа", ring: true);
            LegendRow(ref y, null, PaperMapArt.HeroPencil, "вы здесь", arrow: true);
        }

        private void LegendRow(ref float y, string badge, Color color, string text, bool dashed = false, bool ring = false,
            bool arrow = false, Sprite icon = null)
        {
            RectTransform iconRect = MakeRect("I", legendRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            iconRect.sizeDelta = Vector2.one * 14f;
            iconRect.anchoredPosition = new Vector2(18f, y);
            Image img = iconRect.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            if (badge != null)
            {
                img.sprite = StubSprites.Circle(64f);
                img.color = color;
                TMP_Text letter = MakeLabel("L", iconRect, badge, 10f, PaperMapArt.Ink);
                ((RectTransform)letter.transform).sizeDelta = Vector2.one * 14f;
                ((RectTransform)letter.transform).anchoredPosition = Vector2.zero;
            }
            else if (dashed)
            {
                img.sprite = PaperMapArt.NetworkRing();
                img.color = new Color(color.r, color.g, color.b, 0.8f);
            }
            else if (ring)
            {
                img.sprite = PaperMapArt.TargetRing();
                img.color = color;
            }
            else if (arrow)
            {
                img.sprite = StubSprites.ShipTriangle();
                img.color = color;
            }
            else if (icon != null)
            {
                img.sprite = icon;
                img.color = color;
            }
            else
            {
                img.sprite = StubSprites.Circle(64f);
                img.color = color;
                iconRect.sizeDelta = Vector2.one * 10f;
            }

            TMP_Text row = MakeLabel("R", legendRoot, text, 13f, new Color(PaperMapArt.Ink.r, PaperMapArt.Ink.g, PaperMapArt.Ink.b, 0.85f),
                TextAlignmentOptions.Left);
            ((RectTransform)row.transform).sizeDelta = new Vector2(160f, 16f);
            ((RectTransform)row.transform).anchoredPosition = new Vector2(32f, y);
            y -= 17f;
        }

        private void BuildCompass()
        {
            compassRoot = MakeRect("Compass", mapRect, Vector2.one, Vector2.one, Vector2.zero, Vector2.zero);
            compassRoot.anchoredPosition = new Vector2(-64f, -70f);
            compassRoot.sizeDelta = new Vector2(48f, 72f);
            var ink = new Color(PaperMapArt.Ink.r, PaperMapArt.Ink.g, PaperMapArt.Ink.b, 0.8f);

            TMP_Text n = MakeLabel("N", compassRoot, "С", 17f, ink);
            ((RectTransform)n.transform).sizeDelta = new Vector2(24f, 20f);
            ((RectTransform)n.transform).anchoredPosition = new Vector2(0f, 20f);

            RectTransform needle = MakeRect("Needle", compassRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            needle.sizeDelta = new Vector2(3f, 34f);
            Image needleImage = needle.gameObject.AddComponent<Image>();
            needleImage.color = ink;
            needleImage.raycastTarget = false;

            RectTransform cross = MakeRect("Cross", compassRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            cross.sizeDelta = new Vector2(22f, 2f);
            Image crossImage = cross.gameObject.AddComponent<Image>();
            crossImage.color = new Color(ink.r, ink.g, ink.b, 0.35f);
            crossImage.raycastTarget = false;
        }

        private void BuildRuler()
        {
            // масштабная линейка: круглый шаг мира, влезающий в 150 px
            int[] steps = { 100, 200, 500, 1000, 2000, 5000, 10000 };
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
            var ink = new Color(PaperMapArt.Ink.r, PaperMapArt.Ink.g, PaperMapArt.Ink.b, 0.8f);

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
            ((RectTransform)label.transform).sizeDelta = new Vector2(px + 40f, 16f);
            ((RectTransform)label.transform).anchoredPosition = new Vector2(0f, 12f);
        }

        // ————— записка (боковой листочек) —————

        private void BuildNote()
        {
            noteRoot = new GameObject("Note", typeof(RectTransform));
            noteRoot.transform.SetParent(mapRect, false);
            RectTransform note = (RectTransform)noteRoot.transform;
            note.anchorMin = new Vector2(1f, 0.5f);
            note.anchorMax = new Vector2(1f, 0.5f);
            note.pivot = new Vector2(1f, 0.5f);
            note.sizeDelta = new Vector2(304f, 396f);
            note.anchoredPosition = new Vector2(-16f, 8f);
            note.localEulerAngles = new Vector3(0f, 0f, -1.5f); // листочек чуть перекошен

            Image bg = noteRoot.AddComponent<Image>();
            bg.sprite = PaperMapArt.Note();
            bg.type = bg.sprite != null ? Image.Type.Simple : Image.Type.Sliced;
            bg.color = bg.sprite != null ? Color.white : PaperMapArt.Paper;
            bg.raycastTarget = true; // клики по записке не уходят в лист

            RectTransform frame = MakeRect("Frame", note, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.sprite = PaperMapArt.InkFrame();
            frameImage.type = Image.Type.Sliced;
            frameImage.color = new Color(PaperMapArt.Ink.r, PaperMapArt.Ink.g, PaperMapArt.Ink.b, 0.6f);
            frameImage.raycastTarget = false;

            noteName = MakeLabel("Name", note, "", 22f, PaperMapArt.Ink, TextAlignmentOptions.Top);
            ((RectTransform)noteName.transform).sizeDelta = new Vector2(270f, 34f);
            ((RectTransform)noteName.transform).anchoredPosition = new Vector2(0f, -44f);

            noteType = MakeLabel("Type", note, "", 13f, new Color(PaperMapArt.Ink.r, PaperMapArt.Ink.g, PaperMapArt.Ink.b, 0.6f));
            ((RectTransform)noteType.transform).sizeDelta = new Vector2(270f, 18f);
            ((RectTransform)noteType.transform).anchoredPosition = new Vector2(0f, -68f);

            noteServices = MakeLabel("Services", note, "", 14f, PaperMapArt.Ink, TextAlignmentOptions.Top);
            ((RectTransform)noteServices.transform).sizeDelta = new Vector2(270f, 40f);
            ((RectTransform)noteServices.transform).anchoredPosition = new Vector2(0f, -104f);

            noteDistance = MakeLabel("Distance", note, "", 14f, PaperMapArt.Rust, TextAlignmentOptions.Top);
            ((RectTransform)noteDistance.transform).sizeDelta = new Vector2(270f, 20f);
            ((RectTransform)noteDistance.transform).anchoredPosition = new Vector2(0f, -136f);

            markButton = CreateButton("BtnMark", note, new Vector2(136f, 40f), new Vector2(-72f, -172f),
                PaperMapArt.Rust, PaperMapArt.Paper);
            markButton.onClick.AddListener(() =>
            {
                if (selectedBody != null)
                    ToggleWaypoint(selectedBody);
            });
            markLabel = markButton.GetComponentInChildren<TMP_Text>();

            Button homeButton = CreateButton("BtnHome", note, new Vector2(120f, 40f), new Vector2(66f, -172f),
                PaperMapArt.PaperDark, PaperMapArt.Ink);
            homeButton.onClick.AddListener(GoToStart);

            noteRoot.SetActive(false);
        }

        private Button CreateButton(string name, RectTransform parent, Vector2 size, Vector2 pos, Color back, Color fore)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;

            Image bg = go.GetComponent<Image>();
            bg.sprite = PaperMapArt.InkFrame();
            bg.type = Image.Type.Sliced;
            bg.color = back;

            TMP_Text text = MakeLabel("T", rect, "", 15f, fore);
            RectTransform textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = bg; // tint-подсветка рамки на hover/нажатие
            return button;
        }

        private void FillNote(CelestialBodyData body)
        {
            noteName.text = body.DisplayName.ToUpper();
            noteType.text = TypeName(body.Type) + " · " + SectorName(body.Sector);
            noteServices.text = ServicesLine(body);
            noteRefreshAt = 0f; // дистанция сразу, без троттлинга прошлого кадра
            UpdateNoteDistance();
            RefreshMarkButton();
        }

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
            return "услуг нет";
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
        }

        private void GoToStart()
        {
            // «К НАЧАЛУ»: записка на стартовую планету; вид подводится через EnsureVisible
            Deselect();
            if (level == null || level.StartPlanetIndex < 0 || level.StartPlanetIndex >= level.Bodies.Count)
                return;
            CelestialBodyData start = level.Bodies[level.StartPlanetIndex];
            if (start.Type == BodyType.Planet)
                SelectBody(start);
        }

        // ————— выбор и записка —————

        private void SelectBody(CelestialBodyData body)
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

        // ————— вид: зум колесом + пан перетаскиванием —————

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

        private bool TryScreenToSheet(Vector2 screenPos, out Vector2 sheetLocal)
        {
            sheetLocal = Vector2.zero;
            return sheetRect != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(sheetRect, screenPos, canvasCam, out sheetLocal);
        }

        private bool PointerOverChrome(Vector2 screenPos)
        {
            return (noteRoot != null && ContainsScreen((RectTransform)noteRoot.transform, screenPos))
                || ContainsScreen(legendRoot, screenPos);
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
                    || (legendRoot != null && pressed.IsChildOf(legendRoot.transform)));
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

        // зум/пан не выносят содержимое за лист; margin > 1 — резиновый вынос при перетаскивании
        private Vector2 ClampZoomOffset(Vector2 offset, float s, float margin = 1f)
        {
            if (content == null)
                return offset;
            if (s <= 1f)
                return Vector2.zero; // контент меньше листа — всегда по центру
            Vector2 max = content.rect.size * (s - 1f) * 0.5f * margin;
            return Vector2.Max(-max, Vector2.Min(max, offset));
        }

        // ————— живой слой —————

        private void UpdatePlayerMarker()
        {
            Transform shipT = ShipPosition();
            if (shipT == null || playerMarker == null)
                return;
            playerMarker.anchoredPosition = WorldToMap(shipT.position);

            if (ship != null && ship.State == ShipState.Flying && ship.Body != null
                && ship.Body.linearVelocity.sqrMagnitude > 25f)
            {
                Vector2 v = ship.Body.linearVelocity;
                playerHeading = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            }
            if (playerArrowRoot != null)
                playerArrowRoot.localRotation = Quaternion.Euler(0f, 0f, playerHeading);
        }

        private Transform ShipPosition()
        {
            if (ship == null)
                ship = FindAnyObjectByType<Ship>();
            return ship != null ? ship.transform : null;
        }

        // цель активного заказа — пульсирующий карандашный обвод + имя + «ЦЕЛЬ»
        private void UpdateTargetRing()
        {
            var orders = session != null ? session.Orders : null;
            var active = orders != null ? orders.Active : null;
            int index = active == null
                ? -1
                : active.State == OrderState.Aboard ? active.DropoffIndex : active.PickupIndex;

            if (index < 0 || index >= level.Bodies.Count)
            {
                targetRing.gameObject.SetActive(false);
                targetIndex = -1;
                return;
            }

            CelestialBodyData body = level.Bodies[index];
            targetRing.gameObject.SetActive(true);
            if (targetIndex != index)
            {
                targetIndex = index;
                float size = Mathf.Max(46f, IconSizeOf(body) * 2.0f);
                targetRingImage.rectTransform.sizeDelta = Vector2.one * size;
                ((RectTransform)targetNameLabel.transform).anchoredPosition = Vector2.up * (size * 0.5f + 16f);
                ((RectTransform)targetTagLabel.transform).anchoredPosition = Vector2.up * (size * 0.5f + 36f);
                targetNameLabel.text = body.DisplayName.ToUpper();
            }
            targetRing.anchoredPosition = WorldToMap(body.Position);
            // пульс: кольцо дышит, подписи стоят на месте
            targetRingImage.transform.localScale = Vector3.one * (1f + 0.07f * Mathf.Sin(Time.unscaledTime * 4.6f));
        }

        // живые кометы — карандашные значки с хвостом по курсу
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
                RectTransform icon = MakeImage("Comet_" + cometIcons.Count, content, PaperMapArt.CometIcon(),
                    PaperMapArt.CometPencil, Vector2.one * 36f, false);
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
            // экранное поверх карты: легенда, компас, линейка, записка
            if (legendRoot != null) legendRoot.SetAsLastSibling();
            if (compassRoot != null) compassRoot.SetAsLastSibling();
            if (rulerRoot != null) rulerRoot.SetAsLastSibling();
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
            markers.Clear();
            icons.Clear();
            bodySizes.Clear();
            cometIcons.Clear();
            cometLastPos.Clear();
            playerMarker = null;
            playerArrowRoot = null;
            targetRing = null;
            targetRingImage = null;
            targetNameLabel = null;
            targetTagLabel = null;
            waypointPin = null;
            targetIndex = -1;
            selectedBody = null;
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

        // все подписи карты — шрифтом экспедиции (Excalifont), иначе дефолт TMP
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
            label.raycastTarget = false;
            return label;
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
