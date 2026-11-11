using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SpaceTaxi.Audio;
using SpaceTaxi.Core;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Карта (M) — «нарисованная от руки на старой жёлтой бумаге» (Н5.F + фиксы Н5.21 + Н6.K).
    // Лист ровно вписан в окно карты минус зона бортового компьютера (панель 480 px
    // слева; TAB убирает панель — лист каждый раз перекладывается). Бумага
    // (Art/background.png) лежит ВНУТРИ зум-контента: пан двигает бумагу, зум
    // зумит её вместе с телами, запас ×3 от окна — края бумаги не видны.
    // Тёмный стол (Backdrop) начинается ПРАВЕЕ борткомпа — панель остаётся яркой
    // при открытой карте (Н6.K1). Декор for_fun и компас-роза убраны (Н6.K2),
    // легенда — без подложки, чернила прямо на бумаге (Н6.K3).
    // Записки тел (PaperMapLore) нацарапаны ПРЯМО на карте возле тел: издалека
    // почти не видны, крупным планом читаются; клик по телу приближает и
    // подсвечивает его записку (Н6.K4); уголок-записка снесён, «пометить» —
    // маленькая чернильная кнопка у верхнего края листа.
    // Нарисованные линии убраны (рамки районов, орбиты, кольца дистанций, сетка,
    // пунктирные пути, кольца мира/поясов/хаба) — остались иконки тел (диаметр
    // пропорционален радиусу, максимум уровня = опорный), подписи «на бумаге»
    // у своих планет (панятся и зумятся с контентом, без разводок), камни поясов,
    // кометы, буи, ореолы цели/метки и ракета игрока (видна всегда: размер не
    // зависит от зума, за краем вида клампится с указателем; +30% и режим
    // «следить за ракетой» — Н6.K6). Весь текст карты — только Excalifont
    // (mapFont из сцены, фолбэк — Resources). Взрыв корабля — расходящееся
    // кольцо с искрами в точке ракеты, стоянка — мягкая тень-подставка (Н6.K7).
    // Курсор системный всегда (Н6.K8); зоны клика подписей = размер текста,
    // пояс ловит клик по кольцу, не по квадрату (Н6.K9). Карта открывается
    // ПРИБЛИЖЕННОЙ К ИГРОКУ (зум 9; нет корабля — планета старта). Публичный
    // контракт сохранён: Waypoint + WaypointChanged + Set/Clear/ToggleWaypoint
    // (WaypointIndicator, MinimapBinder), SetView/SelectBody (оркестратор),
    // Esc-договорённость с MapScreenController. Вид: колесо — зум к курсору
    // ×1.2 (весь мир ↔ одна планета), ЛКМ-драг — пан (Н6.K5: WASD-пан убран,
    // W больше не двигает карту — игра под картой ловит тягу честно).
    public class MapScreenBinder : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private TMP_FontAsset mapFont; // Excalifont-Regular SDF — назначен в сцене

        // — вид: базовый масштаб = весь мир вписан в лист, дальше зум к курсору —
        private const float MinViewZoom = 1f;      // из базового вида не отдаляемся
        private const float MaxViewZoom = 14f;     // от «весь мир» до одной планеты крупно
        private const float InitialViewZoom = 9f;  // карта открывается приближенной к игроку
        private const float ClickZoom = 8f;        // клик по телу — сильный зум (видны записи)
        private const float ZoomDuration = 0.22f;
        private const float WheelStep = 1.2f;
        private const float ButtonZoomStep = 1.35f;
        private const float ClickDragGuard = 6f;   // сдвиг больше этого — пан, а не выбор
        private const float FitMargin = 1.10f;     // bbox тел занимает 90% листа
        private const float LabelFontSize = 16f;   // кегль подписей тел (Excalifont, на бумаге)
        private const float LabelHoverFont = 20f;
        private const float ViewInset = 76f;       // отступ клампа ракеты от края листа, px (ракета 64 + подпись «ВЫ» внизу)
        private const float RubberMargin = 1.15f;  // мягкий вынос за край при перетаскивании

        // режим «следить за ракетой» (Н6.K6): камера плавно держит ракету в центре.
        // Пан и выбор тела сбрасывают фиксацию НАВСЕГДА (до повторного клика кнопки) —
        // выбрано вместо «пауза на время перетаскивания»: предсказуемо, без рывка
        // возврата после отпускания
        private const float FollowLerpSpeed = 5f;  // скорость догоняющего лерпа, 1/с (экспоненциальное сглаживание)
        private const float RocketMarkerPx = 64f;  // маркер ракеты (Н6.K6: 48 → +33%)

        // — лист и бортовой компьютер (Н5.21) —
        private const float SheetMargin = 12f;          // поля листа от краёв доступной области
        private const float ContentMargin = 26f;        // окно вида внутри листа (маска клипует всё внутри)
        private const float OnboardGap = 8f;           // зазор листа к краю борткомпа           // зазор между борткомпом и листом
        private const float OnboardFallbackWidth = 480f; // ширина панели борткомпа (OnboardComputer.prefab)
        private const float PaperOversize = 3f;         // запас бумаги за краями окна (×3 от окна)
        private const float RefIconPx = 64f;            // диаметр иконки тела с наибольшим радиусом уровня
        private const float MinIconPx = 16f;            // минимальный читаемый диаметр иконки

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
        private Ship ship;
        private GravityField gravity; // живые радиусы тел на hover (лениво)
        private RectTransform mapRect;
        private TMP_FontAsset font;
        private bool built;
        private float builtWidth;
        private Camera canvasCam; // null для Screen Space Overlay — так и нужно утилитам

        private RectTransform sheetRect;   // лист бумаги: ровно окно карты минус борткомп
        private RectTransform viewport;    // фиксированное окно вида (маска): клип не движется с камерой
        private RectTransform paperRect;   // фон-бумага — ребёнок зум-контента: «один слой» с миром
        private RectTransform content;     // зум-контейнер: бумага + весь нарисованный мир
        private RectTransform sectorRoot;  // подписи районов
        private RectTransform edgeRoot;    // буи границы мира, тревожные пометки
        private RectTransform beltsRoot;   // камни поясов астероидов
        private RectTransform bodiesRoot;  // иконки тел
        private RectTransform labelsRoot;  // подписи тел (в плоскости бумаги)
        private RectTransform notesRoot;   // записки тел «на полях карты» (Н6.K4)
        private RectTransform cometsRoot;  // живые кометы
        private RectTransform backdropRect; // тёмный стол — ТОЛЬКО правее борткомпа (Н6.K1)
        private RectTransform legendRoot;
        private RectTransform rulerRoot;
        private RectTransform zoomRoot;
        private RectTransform titleRoot;

        private OnboardComputerPanel onboardPanel; // его зона вычитается из окна карты
        private bool onboardShown;                 // последнее известное состояние панели
        private bool relayoutPending;              // экран изменился — пересчитать лист

        private readonly Dictionary<CelestialBodyData, RectTransform> markers = new Dictionary<CelestialBodyData, RectTransform>();
        private readonly Dictionary<CelestialBodyData, float> bodySizes = new Dictionary<CelestialBodyData, float>();
        private float maxBodyRadius; // опорный радиус для нормировки иконок (Н5.21-6)

        private RectTransform playerMarker;      // на sheetRect (вне зум-контента): RectMask2D не режет, размер постоянен
        private RectTransform playerArrowRoot; // вращается по курсу; подпись «ВЫ» не крутится
        private RectTransform targetRing;
        private RectTransform targetRingPulse;   // жёлтый пульс: сплошное кольцо
        private RectTransform targetRingWide;    // и второе — видно издалека
        private Image targetRingPulseImage;      // кэш: пульс обновляется каждый кадр
        private Image targetRingWideImage;
        private TMP_Text targetLabel;
        private RectTransform waypointRoot;      // зелёный ореол метки + булавка
        private RectTransform waypointHalo;
        private RectTransform waypointPin;
        private Image waypointHaloImage;         // кэш: пульс обновляется каждый кадр
        private Image waypointPinImage;
        private int targetIndex = -1;
        private CelestialBodyData lastTargetBody; // чья подпись спрятана под «ЦЕЛЬ: …»
        private float playerHeading;

        // hover по телу: круги радиусов посадки/захвата + имя крупнее
        private RectTransform hoverCaptureRing;
        private RectTransform hoverLandingRing;
        private readonly Dictionary<CelestialBodyData, TMP_Text> bodyLabels = new Dictionary<CelestialBodyData, TMP_Text>();

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
        private RectTransform markButtonRoot;  // маленькая чернильная кнопка «пометить» вместо уголка (Н6.K4)
        private Button markButton;
        private Image markButtonImage;
        private TMP_Text markLabel;

        // записки «на полях»: текст возле каждого тела, подсветка выбранного (Н6.K4)
        private readonly Dictionary<CelestialBodyData, TMP_Text> bodyNotes = new Dictionary<CelestialBodyData, TMP_Text>();

        // режим «следить за ракетой» (Н6.K6)
        private bool followMode;
        private Button followButton;
        private TMP_Text followLabel;

        // анимации карты (Н6.K7): взрыв — кольцо+искры; стоянка — тень-подставка
        private HullSystem hull;
        private RectTransform landedPad;
        private Image landedPadImage;
        private sealed class MapBurst
        {
            public RectTransform Root;
            public Image Ring;
            public readonly List<Image> Sparks = new List<Image>();
            public readonly List<Vector2> SparkVel = new List<Vector2>();
            public float Age;
        }
        private readonly List<MapBurst> bursts = new List<MapBurst>();

        private void Awake()
        {
            mapRect = (RectTransform)transform;
            Canvas canvas = GetComponentInParent<Canvas>();
            canvasCam = canvas != null ? canvas.worldCamera : null;

            // корень карты — на весь канвас: сценическая заготовка несла старые
            // отступы «под борткомп», из-за чего резерв ширины панели в LayoutSheet
            // складывался с ними и карта не доставала до борткомпа (Н5.26)
            mapRect.anchorMin = Vector2.zero;
            mapRect.anchorMax = Vector2.one;
            mapRect.offsetMin = Vector2.zero;
            mapRect.offsetMax = Vector2.zero;

            // заготовку из сцены стираем целиком — лист рисуем кодом
            ClearChildren(transform);
            font = ResolveFont();
            BuildChrome();
        }

        private void OnEnable()
        {
            FocusOnPlayer(); // повторные открытия — карта сразу приближена к игроку (Н5.21-8)
        }

        // шрифт: сцена (Excalifont) → ресурсы Excalifont → HUD → дефолт TMP.
        // Н5.21-3: на карте только Excalifont — все тексты идут через MakeLabel.
        private TMP_FontAsset ResolveFont()
        {
            if (mapFont != null)
                return mapFont;
            TMP_FontAsset res = Resources.Load<TMP_FontAsset>("Excalifont-Regular SDF");
            if (res == null)
                res = Resources.Load<TMP_FontAsset>("Excalifont SDF");
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
            if (SyncOnboardPanel())
            {
                RebuildAll(); // лист переложен — контент пересобрать под новую область
                return;
            }

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
                FocusOnPlayer(); // Н5.21-8: первый вход — сразу приближено к игроку
                return;
            }

            if (relayoutPending)
            {
                relayoutPending = false;
                LayoutSheet();
                RebuildAll();
                return;
            }

            if (Mathf.Abs(mapRect.rect.width - builtWidth) > 2f)
            {
                RebuildAll();
                return;
            }

            UpdateZoomAnim();
            HandleWheel();
            UpdateFollow();
            UpdatePlayerMarker();
            UpdateTargetRing();
            UpdateWaypointVisual();
            UpdateComets();
            UpdateMapEffects();
        }

        private void OnDestroy()
        {
            // симметричная отписка (конвенция проекта)
            if (hull != null)
                hull.Died -= OnShipDied;
        }

        // экран изменился — лист и контент пересчитываются в следующем кадре
        private void OnRectTransformDimensionsChange()
        {
            if (built && mapRect != null)
                relayoutPending = true;
        }

        // борткомп выезжает/убирается по TAB — окно карты меняется, лист перекладываем
        private bool SyncOnboardPanel()
        {
            if (onboardPanel == null)
                onboardPanel = FindAnyObjectByType<OnboardComputerPanel>();
            if (onboardPanel == null || onboardPanel.IsShown == onboardShown)
                return false;
            onboardShown = onboardPanel.IsShown;
            LayoutSheet();
            return built;
        }

        private void OnDisable()
        {
            // карта закрылась — вид и панель сбрасываем, следующий вход с чистого экрана
            selectedBody = null;
            if (markButtonRoot != null)
                markButtonRoot.gameObject.SetActive(false);
            dragging = false;
            suppressClick = false;
            relayoutPending = false;
            if (hoverCaptureRing != null)
                hoverCaptureRing.gameObject.SetActive(false);
            if (hoverLandingRing != null)
                hoverLandingRing.gameObject.SetActive(false);
            if (content != null)
                ApplyZoom(1f, Vector2.zero);
            zoomAnim = false;
        }

        // ————— каркас: стол, лист, бумага, экранное (вне зум-контейнера) —————

        private void BuildChrome()
        {
            // тёмный стол вокруг листа; клик мимо листа тоже снимает выбор.
            // Н6.K1: стол начинается ПРАВЕЕ борткомпа — панель под картой остаётся
            // яркой (затемнение допустимо только на области карты)
            backdropRect = MakeRect("Backdrop", mapRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image backImage = backdropRect.gameObject.AddComponent<Image>();
            backImage.color = PaperMapArt.Desk;
            backImage.raycastTarget = true;
            MapMarker backMarker = backdropRect.gameObject.AddComponent<MapMarker>();
            backMarker.hoverEnabled = false;
            backMarker.Clicked += () =>
            {
                if (!suppressClick) // клик после пана — не «снять выбор»
                    Deselect();
            };

            // лист жёлтой бумаги: ровно окно карты минус зона борткомпа (Н5.21-1)
            sheetRect = MakeRect("Sheet", mapRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image sheetImage = sheetRect.gameObject.AddComponent<Image>();
            sheetImage.color = PaperMapArt.Paper; // подложка; текстура бумаги панится в PaperClip
            sheetImage.raycastTarget = false; // клики сквозь лист уходят в Backdrop
            LayoutSheet();

            // «один слой» (Н5.26): фиксированное окно вида с маской, внутри — единственный
            // зум-контейнер; бумага и весь мир — его дети. Камера (пан/зум) двигает
            // контейнер целиком; клип стоит на месте, ничто не выезжает за лист.
            viewport = MakeRect("Viewport", sheetRect, Vector2.zero, Vector2.one,
                new Vector2(ContentMargin, ContentMargin), new Vector2(-ContentMargin, -ContentMargin));
            viewport.gameObject.AddComponent<RectMask2D>(); // окно вида не движется с камерой

            content = MakeRect("MapRoot", viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            paperRect = MakeRect("Paper", content, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
            Image paperImage = paperRect.gameObject.AddComponent<Image>();
            Sprite paperArt = PaperMapArt.SheetArt();
            paperImage.sprite = paperArt;
            paperImage.color = paperArt != null ? Color.white : PaperMapArt.Paper;
            paperImage.raycastTarget = false;
            paperRect.sizeDelta = sheetRect.rect.size * PaperOversize; // панится/зумится вместе с контентом

            BuildTitle();
            BuildLegend();
            BuildZoomButtons();
            BuildMarkButton();
        }

        // Н5.21-1: лист = доступная область окна карты минус зона борткомпа.
        // Пересчитывается при смене экрана (OnRectTransformDimensionsChange)
        // и при TAB-переключении панели (SyncOnboardPanel).
        private void LayoutSheet()
        {
            if (sheetRect == null || mapRect == null)
                return;
            sheetRect.offsetMin = new Vector2(ReservedLeftInset() + OnboardGap, SheetMargin);
            sheetRect.offsetMax = new Vector2(-SheetMargin, -SheetMargin);
            if (paperRect != null) // запас бумаги от текущего окна
                paperRect.sizeDelta = sheetRect.rect.size * PaperOversize;
            // Н6.K1: стол — от края панели (минус кольцо-поле листа) и правее;
            // полоса борткомпа остаётся вне затемнения
            if (backdropRect != null)
            {
                backdropRect.offsetMin = new Vector2(ReservedLeftInset() - SheetMargin, 0f);
                backdropRect.offsetMax = Vector2.zero;
            }
        }

        // ширина зоны борткомпа слева (панель — корень префаба OnboardComputer, 480 px)
        private float ReservedLeftInset()
        {
            if (onboardPanel == null)
                onboardPanel = FindAnyObjectByType<OnboardComputerPanel>();
            if (onboardPanel == null || !onboardPanel.IsShown)
                return 0f;
            RectTransform panelRect = onboardPanel.transform as RectTransform;
            float width = panelRect != null ? panelRect.rect.width : OnboardFallbackWidth;
            return width > 1f ? width : OnboardFallbackWidth;
        }

        // ————— содержимое листа —————

        private void BuildMap()
        {
            LayoutSheet(); // размеры окна уже известны — лист и бумага актуальны

            // граница мира = кольцо буёв (HullSystem бросает «ПОТЕРЯНА СВЯЗЬ» от origin)
            mapRadius = level.WorldRadius;
            float extX = 0f, extY = 0f; // bbox всех тел уровня — то, что обязано влезть
            maxBodyRadius = 0f;         // опорный радиус для нормировки иконок
            foreach (CelestialBodyData body in level.Bodies)
            {
                float edge = body.Position.magnitude + body.Radius;
                if (edge > mapRadius)
                    mapRadius = edge + 100f;
                extX = Mathf.Max(extX, Mathf.Abs(body.Position.x) + body.Radius);
                extY = Mathf.Max(extY, Mathf.Abs(body.Position.y) + body.Radius);
                if (body.Type != BodyType.AsteroidBelt && body.Radius > maxBodyRadius)
                    maxBodyRadius = body.Radius;
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
            if (maxBodyRadius <= 0f)
                maxBodyRadius = 1f;
            worldCenter = Vector2.zero;

            // «весь мир на листе»: bbox тел уровня + поля 10% вписывается целиком
            float w = content.rect.width, h = content.rect.height;
            scale = Mathf.Min(w / (2f * extX * FitMargin), h / (2f * extY * FitMargin));
            scale = Mathf.Max(scale, 0.012f);

            // рамка листа «от руки» — когда размер известен
            MakeSketchFrame(sheetRect, WithAlpha(PaperMapArt.Ink, 0.75f), 2.6f, 1042);

            // слои снизу вверх: районы → край мира → пояса → декор → тела →
            // подписи → hover → кометы → метка/цель (игрок — на листе, выше всех).
            // Нарисованные линии (рамки районов, орбиты, кольца дистанций, сетка,
            // пунктирные пути) убраны совсем — Н5.21-5.
            sectorRoot = MakeRect("Sectors", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            edgeRoot = MakeRect("WorldEdge", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            beltsRoot = MakeRect("Belts", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            bodiesRoot = MakeRect("Bodies", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            labelsRoot = MakeRect("Labels", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // чернильная клякса в origin мира (отметка центра, не линия)
            MakeImage("WorldCenter", content, PaperMapArt.InkDot(),
                WithAlpha(PaperMapArt.Ink, 0.5f), Vector2.one * 7f, false);

            BuildSectorLabels();
            BuildCometZone();
            BuildWorldEdge();
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type == BodyType.AsteroidBelt)
                    CreateBelt(body);
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type != BodyType.AsteroidBelt)
                    CreateBody(body);
            BuildBodyNotes(); // Н6.K4: записки «на полях» после тел и подписей

            hoverCaptureRing = MakeImage("HoverCapture", content, PaperMapArt.TargetRing(),
                WithAlpha(PaperMapArt.Graphite, 0.7f), Vector2.zero, false);
            hoverCaptureRing.gameObject.SetActive(false);
            hoverLandingRing = MakeImage("HoverLanding", content, PaperMapArt.TargetRing(),
                WithAlpha(PaperMapArt.GreenPencil, 0.75f), Vector2.zero, false);
            hoverLandingRing.gameObject.SetActive(false);

            // кометы под ореолами цели/метки: ореол не должен прятаться под пролетевшей кометой
            cometsRoot = MakeRect("Comets", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            BuildWaypoint();
            BuildTargetRing();
            BuildPlayerMarker();

            // Н5.21-8 → Н6-fix: FocusOnPlayer вызывается ПОСЛЕ built=true (в Update —
            // внутри BuildMap гвард !built молча съедал приближение к игроку)
            BuildRuler(); // линейка зависит от scale — известен только здесь
            if (titleRoot != null)
            {
                TMP_Text title = titleRoot.GetComponentInChildren<TMP_Text>();
                if (title != null)
                    title.text = "система " + level.LevelName + " · маршрутный лист";
            }
        }

        // ————— районы: крупные рукописные подписи (рамки-линии убраны, Н5.21-5) —————

        private void BuildSectorLabels()
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
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (CelestialBodyData body in pair.Value)
                {
                    Vector2 p = body.Position;
                    minX = Mathf.Min(minX, p.x);
                    maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y);
                    maxY = Mathf.Max(maxY, p.y);
                }

                // крупная рукописная подпись района над группой тел, чуть с наклоном
                TMP_Text label = MakeLabel("SectorLabel_" + pair.Key, sectorRoot, SectorName(pair.Key), 24f,
                    WithAlpha(PaperMapArt.Ink, 0.55f));
                RectTransform labelRect = (RectTransform)label.transform;
                labelRect.sizeDelta = new Vector2(360f, 34f);
                labelRect.anchoredPosition = WorldToMap(new Vector2((minX + maxX) * 0.5f, maxY)) + Vector2.up * 26f;
                labelRect.localRotation = Quaternion.Euler(0f, 0f, (Hash01((int)pair.Key * 7 + 3) - 0.5f) * 4f);
                label.characterSpacing = 5f;
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
            img.raycastTarget = false; // штрихи рамок не едят клики (иначе рамка района блокирует «клик мимо»)
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

        // ————— кометная зона / край мира —————

        private void BuildCometZone()
        {
            if (level.CometAreaRadius <= 0f) // хорды по всему миру — зону не подписываем
                return;
            Vector2 pos = WorldToMap(level.CometAreaCenter);
            TMP_Text label = MakeLabel("CometZoneLabel", edgeRoot, "кометная зона", 16f,
                WithAlpha(PaperMapArt.CometPencil, 0.95f));
            ((RectTransform)label.transform).sizeDelta = new Vector2(240f, 22f);
            ((RectTransform)label.transform).anchoredPosition = pos + Vector2.up * (level.CometAreaRadius * scale + 16f);
        }

        // граница мира: буи-звёздочки + две тревожные пометки «от руки»
        // (пунктирное кольцо и штриховка снаружи убраны — линии, Н5.21-5)
        private void BuildWorldEdge()
        {
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

            var warnColor = WithAlpha(PaperMapArt.RedPencil, 0.8f);
            for (int i = 0; i < 2; i++)
            {
                float ang = (45f + 180f * i) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                TMP_Text warn = MakeLabel("NoSignal_" + i, edgeRoot, "за кругом — потеря связи", 15f, warnColor);
                ((RectTransform)warn.transform).sizeDelta = new Vector2(280f, 22f);
                ((RectTransform)warn.transform).anchoredPosition = dir * (mapRadius * scale + 26f);
                ((RectTransform)warn.transform).localRotation = Quaternion.Euler(0f, 0f, i == 0 ? -2.5f : 2.5f);
            }
        }

        // ————— пояса астероидов: камни map_asteroid по кольцу, клик-зона без линии —————

        private void CreateBelt(CelestialBodyData body)
        {
            float diameter = Mathf.Max(12f, body.Radius * 2f * scale);
            Vector2 pos = WorldToMap(body.Position);
            bodySizes[body] = diameter;

            // Н6.K9: клик-зона пояса — невидимые точки по кольцу, не огромный
            // квадрат по диаметру. Пунктирная осевая линия-путь убрана (Н5.21-5),
            // кликабельность сохранена.
            CelestialBodyData captured = body;
            for (int i = 0; i < 16; i++)
            {
                float ang = i / 16f * Mathf.PI * 2f;
                RectTransform dot = MakeRect("BeltHit_" + body.DisplayName + "_" + i, beltsRoot,
                    Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
                dot.sizeDelta = Vector2.one * 34f;
                dot.anchoredPosition = pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (diameter * 0.5f);
                Image dotImage = dot.gameObject.AddComponent<Image>();
                dotImage.color = new Color(0f, 0f, 0f, 0f);
                dotImage.raycastTarget = true;
                var marker = dot.gameObject.AddComponent<MapMarker>();
                marker.hoverEnabled = false; // точки невидимы — hover-эффект не нужен
                marker.Clicked += () =>
                {
                    if (suppressClick)
                        return;
                    if (selectedBody == captured)
                        Deselect();
                    else
                        SelectBody(captured);
                };
            }

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
                }
            }

            if (string.IsNullOrEmpty(body.DisplayName))
                return;
            string beltName = body.DisplayName;
            if (!beltName.ToLower().Contains("пояс"))
                beltName = "пояс " + beltName;
            // подпись «на бумаге»: чуть ниже и правее кольца пояса (Н5.21-4)
            TMP_Text label = MakeLabel("Label_" + body.DisplayName, labelsRoot, beltName, LabelFontSize,
                WithAlpha(PaperMapArt.Ink, 0.85f));
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.sizeDelta = new Vector2(240f, 28f);
            labelRect.anchoredPosition = pos + new Vector2(diameter * 0.5f + 10f, -(diameter * 0.5f + 15f));
            bodyLabels[body] = label;
            MakeLabelSelectable(label, body); // Н6.K9: зона = размер текста
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

        // ————— тела: арт владельца, диаметр пропорционален радиусу —————

        private void CreateBody(CelestialBodyData body)
        {
            Sprite art = BodySprite(body);
            // Н5.21-6: диаметр иконки пропорционален data.Radius; тело с наибольшим
            // радиусом уровня = опорный размер (RefIconPx), минимум — MinIconPx.
            // Звезда/дыра/станции нормируются тем же максимумом — относительные
            // размеры строги и внутри типа, и между типами.
            float ratio = maxBodyRadius > 0f ? body.Radius / maxBodyRadius : 1f;
            float d = Mathf.Max(MinIconPx, RefIconPx * ratio);
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
            // пунктирное кольцо вокруг хаба убрано — линия (Н5.21-5); хаб читается
            // по своей иконке (map_taxi_planet) и легенде

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

            // подпись «написана на бумаге» (Н5.21-4): чуть ниже и правее иконки,
            // живёт в системе координат контента — панится и зумится вместе с ней.
            // Никаких разводок и подставок «над иконкой» — просто у своей планеты.
            TMP_Text label = MakeLabel("Label_" + body.DisplayName, labelsRoot, body.DisplayName, LabelFontSize,
                WithAlpha(PaperMapArt.Ink, 0.95f));
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.sizeDelta = new Vector2(220f, 28f); // высота ≥ fontSize×1.32 и для hover-кегля
            labelRect.anchoredPosition = pos + new Vector2(size.x * 0.5f + 10f, -(size.y * 0.5f + 15f));
            bodyLabels[body] = label;
            MakeLabelSelectable(label, body); // Н6.K9: зона = размер текста
        }

        // Н6.K9: клик/hover-зона подписи = размер текста + 5-6px. TMP включается
        // в рейкаст, рект сжимается до preferred-размера (был широкий квадрат-планка),
        // hover ведёт себя как у иконки — те же кольца радиусов и увеличение
        private void MakeLabelSelectable(TMP_Text label, CelestialBodyData body)
        {
            if (label == null)
                return;
            RectTransform rect = (RectTransform)label.transform;
            rect.sizeDelta = label.GetPreferredValues() + new Vector2(6f, 5f);
            label.raycastTarget = true;
            var marker = label.gameObject.AddComponent<MapMarker>();
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
            marker.HoverEntered += () => ShowHoverRings(captured, true);
            marker.HoverExited += () => ShowHoverRings(captured, false);
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

        // hover по телу: круги радиусов захвата/посадки (сплошные карандашные
        // кольца-ореолы, не пунктирные линии) + подпись крупнее
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
            waypointHaloImage = waypointHalo.GetComponent<Image>(); // кэш для кадра
            waypointPinImage = waypointPin.GetComponent<Image>();
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
            if (waypointHaloImage != null)
                waypointHaloImage.color = WithAlpha(PaperMapArt.GreenPencil, pulse);
            if (waypointPinImage != null)
                waypointPinImage.color = WithAlpha(PaperMapArt.GreenPencil, 0.65f + 0.35f * Mathf.Sin(t * 2.8f + 1f));
        }

        private void BuildTargetRing()
        {
            targetRing = MakeRect("TargetRing", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            targetRingPulse = MakeImage("Ring", targetRing, PaperMapArt.TargetRing(),
                PaperMapArt.YellowPencil, Vector2.one * 46f, false);
            targetRingWide = MakeImage("RingWide", targetRing, PaperMapArt.TargetRing(),
                WithAlpha(PaperMapArt.YellowPencil, 0.7f), Vector2.one * 70f, false);
            targetRingPulseImage = targetRingPulse.GetComponent<Image>(); // кэш для кадра
            targetRingWideImage = targetRingWide.GetComponent<Image>();
            targetLabel = MakeLabel("TargetLabel", targetRing, "", 16f, PaperMapArt.YellowPencil);
            ((RectTransform)targetLabel.transform).sizeDelta = new Vector2(280f, 24f);
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
                ((RectTransform)targetLabel.transform).anchoredPosition = Vector2.up * (size * 0.5f + 18f);
                targetLabel.text = "ЦЕЛЬ: " + body.DisplayName.ToUpper();
            }
            targetRing.anchoredPosition = WorldToMap(body.Position);
            // пульс: кольца дышат размером и альфой
            float t = Time.unscaledTime;
            targetRingPulse.localScale = Vector3.one * (1f + 0.09f * Mathf.Sin(t * 4.2f));
            if (targetRingPulseImage != null)
                targetRingPulseImage.color = WithAlpha(PaperMapArt.YellowPencil, 0.62f + 0.3f * Mathf.Sin(t * 3.4f));
            if (targetRingWideImage != null)
                targetRingWideImage.color = WithAlpha(PaperMapArt.YellowPencil, 0.35f + 0.25f * Mathf.Sin(t * 3.4f + Mathf.PI));
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
            // маркер живёт на ЛИСТЕ, не в зум-контенте: RectMask2D контента (инсет 26 px
            // от края листа) резал ракету и подпись «ВЫ» у краёв; позицию считаем сами
            playerMarker = MakeRect("PlayerMarker", sheetRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            playerMarker.SetAsLastSibling(); // поверх рамки листа, под экранным (легенда/записка)
            playerMarker.sizeDelta = Vector2.one * RocketMarkerPx;

            // тень-подставка на стоянке (Н6.K7): мягкое чернильное пятно под ракетой,
            // когда корабль Landed — скромный индикатор «припаркован»
            landedPad = MakeImage("LandedPad", playerMarker, PaperMapArt.InkDot(),
                WithAlpha(PaperMapArt.Ink, 0f), new Vector2(RocketMarkerPx * 1.7f, RocketMarkerPx * 0.55f), false);
            landedPad.SetAsFirstSibling();

            playerArrowRoot = MakeRect("ArrowRoot", playerMarker, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            Sprite rocket = PaperMapArt.MapSprite("map_rocket");
            Vector2 size;
            if (rocket != null)
            {
                // Н6.K6: ракета крупнее (+33% к прежним 36×48)
                size = rocket.rect.height > rocket.rect.width
                    ? new Vector2(48f, RocketMarkerPx)
                    : new Vector2(RocketMarkerPx, 48f);
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
                MakeImage("Rocket", playerArrowRoot, chev, PaperMapArt.Ink, Vector2.one * 40f, false);
            }

            TMP_Text youLabel = MakeLabel("YouLabel", playerMarker, "ВЫ", 18f, PaperMapArt.Ink);
            ((RectTransform)youLabel.transform).sizeDelta = new Vector2(60f, 26f);
            ((RectTransform)youLabel.transform).anchoredPosition = new Vector2(0f, -47f);
        }

        // ракета видна всегда: размер не зависит от зума (маркер вне зум-контента),
        // за краем вида маркер клампится к листу и смотрит в сторону реальной позиции
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

            // тень-подставка: корабль стоит на планете (Н6.K7) — мягкий пульс альфы
            if (landedPadImage == null && landedPad != null)
                landedPadImage = landedPad.GetComponent<Image>();
            if (landedPadImage != null)
            {
                bool parked = ship != null && ship.State == ShipState.Landed;
                float alpha = parked ? 0.20f + 0.08f * Mathf.Sin(Time.unscaledTime * 2.2f) : 0f;
                Color c = landedPadImage.color;
                c.a = alpha;
                landedPadImage.color = c;
                landedPad.gameObject.SetActive(parked);
            }

            // содержимое листа рисуется в sheet-координатах как zoomOffset + zoom * p,
            // маркер — напрямую в sheet-координатах: контр-масштаб не нужен
            Vector2 sheetPos = zoomOffset + zoom * WorldToMap(shipT.position);
            Vector2 half = sheetRect.rect.size * 0.5f - new Vector2(ContentMargin, ContentMargin); // кламп внутрь окна вида
            Vector2 clamped = new Vector2(
                Mathf.Clamp(sheetPos.x, -half.x + ViewInset, half.x - ViewInset),
                Mathf.Clamp(sheetPos.y, -half.y + ViewInset, half.y - ViewInset));
            playerMarker.anchoredPosition = clamped;

            bool offView = (sheetPos - clamped).sqrMagnitude > 1f;
            float angle;
            if (offView) // за краем вида — ракета указывает на реальную позицию
            {
                Vector2 d = sheetPos - clamped;
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
            BindHullDeath();
            return ship != null ? ship.transform : null;
        }

        // взрыв на карте (Н6.K7): подписка на смерть корабля; ленивая — корабль
        // спавнится после карты. Симметричная отписка в OnDestroy
        private void BindHullDeath()
        {
            if (hull != null)
                return;
            hull = FindAnyObjectByType<HullSystem>();
            if (hull != null)
                hull.Died += OnShipDied;
        }

        // расширяющееся кольцо + искры в точке ракеты (маркер всегда виден на листе)
        private void OnShipDied()
        {
            if (playerMarker == null || sheetRect == null)
                return;
            Vector2 center = playerMarker.anchoredPosition;
            var burst = new MapBurst { Root = MakeRect("MapBurst", sheetRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero) };
            burst.Root.anchoredPosition = center;
            burst.Root.SetAsLastSibling();

            burst.Ring = MakeImage("Ring", burst.Root, PaperMapArt.TargetRing(),
                WithAlpha(PaperMapArt.RedPencil, 0.9f), Vector2.one * 14f, false).GetComponent<Image>();
            var rng = new System.Random(StableHash("burst") + (int)(Time.unscaledTime * 1000f));
            for (int i = 0; i < 10; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float speed = 70f + (float)rng.NextDouble() * 110f;
                Image spark = MakeImage("Spark_" + i, burst.Root,
                    i % 3 == 0 ? PaperMapArt.InkDot() : PaperMapArt.MapSprite("map_asteroid"),
                    WithAlpha(PaperMapArt.RedPencil, 0.9f), Vector2.one * (5f + (float)rng.NextDouble() * 6f), false)
                    .GetComponent<Image>();
                burst.Sparks.Add(spark);
                burst.SparkVel.Add(new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * speed);
            }
            bursts.Add(burst);
        }

        // кадр жизни эффектов: кольцо растёт и бледнеет, искры разлетаются
        private void UpdateMapEffects()
        {
            const float Duration = 0.65f;
            const float RingMaxPx = 110f;
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                MapBurst burst = bursts[i];
                if (burst.Root == null) // пережил пересборку карты — просто забываем
                {
                    bursts.RemoveAt(i);
                    continue;
                }
                burst.Age += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(burst.Age / Duration);
                if (burst.Ring != null)
                {
                    burst.Ring.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(14f, RingMaxPx, Mathf.SmoothStep(0f, 1f, k));
                    var c = burst.Ring.color;
                    c.a = 0.9f * (1f - k);
                    burst.Ring.color = c;
                }
                for (int s = 0; s < burst.Sparks.Count; s++)
                {
                    Image spark = burst.Sparks[s];
                    if (spark == null)
                        continue;
                    spark.rectTransform.anchoredPosition += burst.SparkVel[s] * Time.unscaledDeltaTime;
                    var c = spark.color;
                    c.a = 0.9f * (1f - k);
                    spark.color = c;
                }
                if (k >= 1f)
                {
                    Destroy(burst.Root.gameObject);
                    bursts.RemoveAt(i);
                }
            }
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

        // ————— записки: нацарапаны прямо на карте возле тел (Н6.K4) —————
        // Старый «уголок листа» (панель-подложка) снесён. Записка — текст в
        // координатах контента: панится и зумится вместе с миром, издалека
        // (мелкий кегль + бледные чернила) почти не читается, крупным планом
        // читается. Место выбирается перебором направлений так, чтобы записку
        // не накрывала ни одна планета и ни одна подпись.

        private const float NoteFontSize = 10f;      // базовый кегль записки (в контенте)
        private const float NoteWidth = 170f;        // ширина колонки записки, px контента
        private const float NoteAlpha = 0.48f;       // бледные чернила: издалека не читается

        // маленькая чернильная кнопка «пометить» — единственное, что осталось
        // от уголка-записки (waypoint API нельзя терять)
        private void BuildMarkButton()
        {
            markButton = MakePaperButton("BtnNoteMark", sheetRect, new Vector2(158f, 38f),
                new Vector2(-80f, -18f), "пометить", 14f, () =>
                {
                    if (selectedBody != null)
                        ToggleWaypoint(selectedBody);
                }, 901);
            markButtonImage = markButton.GetComponent<Image>();
            markLabel = markButton.GetComponentInChildren<TMP_Text>();
            markButtonRoot = (RectTransform)markButton.transform;
            markButtonRoot.anchorMin = new Vector2(1f, 1f);
            markButtonRoot.anchorMax = new Vector2(1f, 1f);
            markButtonRoot.pivot = new Vector2(1f, 1f);
            markButtonRoot.anchoredPosition = new Vector2(-14f, -14f);
            markButtonRoot.localRotation = Quaternion.Euler(0f, 0f, -1f); // чуть криво — «от руки»
            markButtonRoot.gameObject.SetActive(false);
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

        private void BuildBodyNotes()
        {
            notesRoot = MakeRect("Notes", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            bodyNotes.Clear();

            // что уже «нарисовано» на бумаге и не должно накрывать записки:
            // иконки тел (у пояса — всё кольцо), подписи тел/поясов, районы, кометы
            var blocked = new List<Rect>();
            foreach (CelestialBodyData body in level.Bodies)
                blocked.Add(CenteredRect(WorldToMap(body.Position), Vector2.one * IconSizeOf(body)));
            foreach (var pair in bodyLabels)
                blocked.Add(CenteredRect(((RectTransform)pair.Value.transform).anchoredPosition,
                    ((RectTransform)pair.Value.transform).sizeDelta));
            foreach (RectTransform sector in sectorRoot.GetComponentsInChildren<RectTransform>(false))
                if (sector != sectorRoot)
                    blocked.Add(CenteredRect(sector.anchoredPosition, sector.sizeDelta));
            Rect cometRect = Rect.zero;
            bool cometBlocked = false;
            if (level.CometAreaRadius > 0f)
            {
                cometRect = CenteredRect(WorldToMap(level.CometAreaCenter) + Vector2.up * (level.CometAreaRadius * scale + 16f),
                    new Vector2(240f, 22f));
                cometBlocked = true;
            }

            foreach (CelestialBodyData body in level.Bodies)
            {
                if (body.Type == BodyType.Comet)
                    continue;
                if (!bodyLabels.ContainsKey(body) && body.Type != BodyType.AsteroidBelt)
                    continue; // подпись строится для всех не-поясов; страховка
                TMP_Text note = MakeLabel("Note_" + body.DisplayName, notesRoot,
                    string.Join("\n", PaperMapLore.For(body)), NoteFontSize, WithAlpha(PaperMapArt.Ink, NoteAlpha));
                note.textWrappingMode = TextWrappingModes.Normal;
                note.overflowMode = TextOverflowModes.Overflow;
                RectTransform rect = (RectTransform)note.transform;
                rect.sizeDelta = new Vector2(NoteWidth, note.preferredHeight + 6f);
                rect.localRotation = Quaternion.Euler(0f, 0f, (Hash01(StableHash(body.DisplayName)) - 0.5f) * 7f);

                Vector2 center = WorldToMap(body.Position);
                float iconHalf = IconSizeOf(body) * 0.5f;
                Rect noteRect = Rect.zero;
                bool placed = false;
                for (int dirIndex = 0; dirIndex < 8 && !placed; dirIndex++)
                {
                    float ang = dirIndex * 45f * Mathf.Deg2Rad;
                    var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    for (int step = 0; step < 4 && !placed; step++)
                    {
                        float dist = iconHalf + 16f + step * 18f;
                        Vector2 c = center + dir * dist + new Vector2(dir.x > 0.1f ? NoteWidth * 0.5f : dir.x < -0.1f ? -NoteWidth * 0.5f : 0f, 0f);
                        Rect candidate = CenteredRect(c, rect.sizeDelta);
                        if (OverlapsAny(candidate, blocked) || (cometBlocked && candidate.Overlaps(cometRect))
                            || NoteCollides(candidate, bodyNotes.Keys))
                            continue;
                        if (candidate.Overlaps(CenteredRect(center, Vector2.one * (iconHalf * 2f + 10f))))
                            continue;
                        noteRect = candidate;
                        placed = true;
                    }
                }
                if (!placed) // всё занято — справа снизу как есть (не должно случаться)
                    noteRect = CenteredRect(center + new Vector2(iconHalf + NoteWidth * 0.5f + 14f, -iconHalf - 20f), rect.sizeDelta);

                rect.anchoredPosition = noteRect.center;
                bodyNotes[body] = note;
                blocked.Add(noteRect);
            }
        }

        private static Rect CenteredRect(Vector2 center, Vector2 size)
        {
            return new Rect(center - size * 0.5f, size);
        }

        private static bool OverlapsAny(Rect rect, List<Rect> rects)
        {
            foreach (Rect other in rects)
                if (rect.Overlaps(other))
                    return true;
            return false;
        }

        // записки уже размещённых тел: у тех, что ещё не размещались, rect неизвестен —
        // столкновение проверяем только по фактически стоящим запискам
        private bool NoteCollides(Rect candidate, ICollection<CelestialBodyData> placed)
        {
            foreach (CelestialBodyData body in placed)
            {
                RectTransform rect = bodyNotes[body] != null ? (RectTransform)bodyNotes[body].transform : null;
                if (rect != null && candidate.Overlaps(CenteredRect(rect.anchoredPosition, rect.sizeDelta)))
                    return true;
            }
            return false;
        }

        // выбор тела: записка подсвечивается чернилами (клик по планете «открывает»
        // её записи на карте), у верхнего края появляется кнопка «пометить»
        private void HighlightSelectedNote()
        {
            foreach (var pair in bodyNotes)
            {
                bool on = pair.Key == selectedBody;
                pair.Value.color = on ? WithAlpha(PaperMapArt.Ink, 0.95f) : WithAlpha(PaperMapArt.Ink, NoteAlpha);
                pair.Value.fontSize = on ? NoteFontSize * 1.3f : NoteFontSize;
            }
        }

        // ————— стартовый вид: приближено к игроку (Н5.21-8) —————

        // карта открывается сфокусированной на позиции игрока (Ship); корабль
        // недоступен — планета старта; зум InitialViewZoom: окрестности игрока,
        // ближайшие планеты и подписи читаются
        private void FocusOnPlayer()
        {
            if (!built || content == null || level == null)
                return;
            Vector2 mapPos = WorldToMap(PlayerWorldPosition());
            ApplyZoom(InitialViewZoom, ClampZoomOffset(-mapPos * InitialViewZoom, InitialViewZoom));
        }

        private Vector2 PlayerWorldPosition()
        {
            Transform shipT = ShipPosition();
            if (shipT != null)
                return shipT.position;
            if (level != null && level.StartPlanetIndex >= 0 && level.StartPlanetIndex < level.Bodies.Count)
            {
                CelestialBodyData start = level.Bodies[level.StartPlanetIndex];
                if (start != null)
                    return start.Position;
            }
            return worldCenter;
        }

        // ————— выбор и записки —————

        // публично: оркестратор/тесты открывают записки объекта без эмуляции мыши
        public void SelectBody(CelestialBodyData body)
        {
            if (body == null) // внешний вызов с null — честно закрываем записку
            {
                Deselect();
                return;
            }
            selectedBody = body;
            HighlightSelectedNote();
            if (markButtonRoot != null)
                markButtonRoot.gameObject.SetActive(true);
            RefreshMarkButton();
            EnsureVisible(WorldToMap(body.Position));
        }

        private void Deselect()
        {
            selectedBody = null;
            HighlightSelectedNote();
            if (markButtonRoot != null)
                markButtonRoot.gameObject.SetActive(false);
            // вид не сбрасываем — пан/зум остаются под контролем игрока
        }

        // клик по объекту: приближаем к телу; это ручная смена вида — фиксация
        // на ракете сбрасывается (Н6.K6)
        private void EnsureVisible(Vector2 targetInContent)
        {
            followMode = false;
            RefreshFollowButton();
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

        // Н6.K5: WASD-пан убран — клавиши больше не двигают карту, игра под
        // картой ловит их без конфликта. Пан — только мышью (ЛКМ-драг).

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

        // ————— режим «следить за ракетой» (Н6.K6) —————

        private void ToggleFollow()
        {
            followMode = !followMode;
            RefreshFollowButton();
        }

        private void RefreshFollowButton()
        {
            if (followLabel != null)
                followLabel.text = followMode ? "следить: вкл" : "следить: выкл";
            if (followButton != null)
            {
                var image = followButton.GetComponent<Image>();
                if (image != null) // включённый режим — зелёный карандаш
                    image.color = followMode ? WithAlpha(PaperMapArt.GreenPencil, 0.30f) : PaperMapArt.PaperDark;
            }
            if (followLabel != null)
                followLabel.color = followMode ? PaperMapArt.GreenPencil : PaperMapArt.Ink;
        }

        // камера плавно (экспоненциальное сглаживание, без телепорта) держит
        // ракету в центре; колесо/кнопки зума живут вместе с фиксацией
        private void UpdateFollow()
        {
            if (!followMode || content == null)
                return;
            Vector2 target = ClampZoomOffset(-WorldToMap(PlayerWorldPosition()) * zoom, zoom);
            float k = 1f - Mathf.Exp(-FollowLerpSpeed * Time.unscaledDeltaTime);
            zoomAnim = false; // фиксация главнее остаточной анимации зума
            ApplyZoom(zoom, Vector2.Lerp(zoomOffset, target, k));
        }

        private bool TryScreenToSheet(Vector2 screenPos, out Vector2 sheetLocal)
        {
            sheetLocal = Vector2.zero;
            return sheetRect != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(sheetRect, screenPos, canvasCam, out sheetLocal);
        }

        private bool PointerOverChrome(Vector2 screenPos)
        {
            return ContainsScreen(markButtonRoot, screenPos)
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
            // Н6.K6: пан — ручное управление видом; фиксация на ракете выключается
            // до повторного клика кнопки (без «дёргающего» возврата)
            followMode = false;
            RefreshFollowButton();
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
                && ((markButtonRoot != null && pressed.IsChildOf(markButtonRoot))
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
            content.anchoredPosition = offset; // бумага — ребёнок контента: двигается одним слоем (Н5.26)
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

        // ————— экранное: заголовок, легенда, линейка, кнопки —————
        // (Н6.K2: компас-роза map_for_fun_1 убрана вместе с остальным декором)

        private void BuildTitle()
        {
            titleRoot = MakeRect("Title", sheetRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            titleRoot.anchoredPosition = new Vector2(0f, -18f);
            titleRoot.sizeDelta = new Vector2(880f, 40f);
            titleRoot.localRotation = Quaternion.Euler(0f, 0f, 0.4f);
            TMP_Text title = MakeLabel("T", titleRoot, "маршрутный лист", 26f, WithAlpha(PaperMapArt.Ink, 0.7f));
            title.characterSpacing = 3f;
        }

        // легенда: нацарапана ПРЯМО на бумаге — без подложки и рамки (Н6.K3),
        // значки и текст чернилами в свободном углу (низ-лево: в новой раскладке
        // мира H6-K0 юго-западный квадрант пуст — легенда ничего не накрывает)
        private void BuildLegend()
        {
            legendRoot = MakeRect("Legend", sheetRect, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            legendRoot.pivot = new Vector2(0f, 0f);
            legendRoot.anchoredPosition = new Vector2(18f, 18f);
            legendRoot.sizeDelta = new Vector2(490f, 280f);

            int y = 0;
            LegendRow(18f, ref y, PaperMapArt.MapSprite("map_planet"), Color.white, "планета");
            LegendRow(18f, ref y, PaperMapArt.MapSprite("map_gas_station"), Color.white, "заправка");
            LegendRow(18f, ref y, PaperMapArt.MapSprite("map_repair"), Color.white, "мастерская");
            LegendRow(18f, ref y, PaperMapArt.MapSprite("map_taxi_planet"), Color.white, "хаб МТО");
            LegendRow(18f, ref y, PaperMapArt.MapSprite("map_sun"), Color.white, "звезда");
            LegendRow(18f, ref y, PaperMapArt.MapSprite("map_black_hole"), Color.white, "чёрная дыра");

            y = 0;
            LegendRow(254f, ref y, PaperMapArt.MapSprite("map_asteroid"), Color.white, "пояс");
            LegendRow(254f, ref y, PaperMapArt.MapSprite("map_cometa"), Color.white, "комета");
            LegendRow(254f, ref y, PaperMapArt.TargetRing(), PaperMapArt.YellowPencil, "цель заказа", 26f);
            LegendRow(254f, ref y, PaperMapArt.TargetRing(), PaperMapArt.GreenPencil, "своя метка", 26f);
            Sprite rocket = PaperMapArt.MapSprite("map_rocket");
            LegendRow(254f, ref y, rocket, Color.white, "вы здесь", 28f);
        }

        private void LegendRow(float x, ref int row, Sprite icon, Color color, string text, float iconSize = 30f)
        {
            float y = 250f - row * 40f;
            RectTransform iconRect = MakeRect("I", legendRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            iconRect.sizeDelta = Vector2.one * 34f;
            iconRect.anchoredPosition = new Vector2(x + 17f, y);
            MakeImage("G", iconRect, icon, color, Vector2.one * iconSize, false);
            if (icon == null)
                MakeImage("F", iconRect, StubSprites.Circle(64f), WithAlpha(PaperMapArt.Ink, 0.7f), Vector2.one * 22f, false);

            TMP_Text rowLabel = MakeLabel("R", legendRoot, text, 24f, WithAlpha(PaperMapArt.Ink, 0.9f),
                TextAlignmentOptions.Left);
            ((RectTransform)rowLabel.transform).sizeDelta = new Vector2(190f, 34f);
            ((RectTransform)rowLabel.transform).anchorMin = new Vector2(0f, 0f);
            ((RectTransform)rowLabel.transform).anchorMax = new Vector2(0f, 0f);
            ((RectTransform)rowLabel.transform).pivot = new Vector2(0f, 0.5f);
            ((RectTransform)rowLabel.transform).anchoredPosition = new Vector2(x + 44f, y);
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

            rulerRoot = MakeRect("Ruler", sheetRect, new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            rulerRoot.pivot = new Vector2(0.5f, 0f);
            rulerRoot.anchoredPosition = new Vector2(-28f - px * 0.5f, 30f);
            rulerRoot.sizeDelta = new Vector2(px + 8f, 44f);
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

            TMP_Text label = MakeLabel("T", rulerRoot, step + " юн", 18f, ink);
            ((RectTransform)label.transform).sizeDelta = new Vector2(px + 60f, 26f);
            ((RectTransform)label.transform).anchoredPosition = new Vector2(0f, 14f);
        }

        // кнопки зума: +/− к центру листа, «сброс» — вид «весь мир», «следить» —
        // фиксация камеры на ракете (Н5.21-7: крупнее; Н6.K6: кнопка следования)
        private void BuildZoomButtons()
        {
            zoomRoot = MakeRect("ZoomButtons", sheetRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
            zoomRoot.pivot = new Vector2(0.5f, 0f);
            zoomRoot.anchoredPosition = new Vector2(0f, 16f);
            zoomRoot.sizeDelta = new Vector2(480f, 64f);

            MakePaperButton("BtnZoomIn", zoomRoot, new Vector2(52f, 48f), new Vector2(-168f, 8f), "+", 26f,
                () => ZoomStep(ButtonZoomStep), 701);
            MakePaperButton("BtnZoomOut", zoomRoot, new Vector2(52f, 48f), new Vector2(-104f, 8f), "-", 26f,
                () => ZoomStep(1f / ButtonZoomStep), 702);
            MakePaperButton("BtnZoomReset", zoomRoot, new Vector2(124f, 48f), new Vector2(-10f, 8f), "сброс", 20f,
                () => AnimateZoom(1f, Vector2.zero), 703);
            followButton = MakePaperButton("BtnFollow", zoomRoot, new Vector2(150f, 48f), new Vector2(130f, 8f),
                "следить: выкл", 15f, ToggleFollow, 704);
            followLabel = followButton.GetComponentInChildren<TMP_Text>();
            RefreshFollowButton();
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
            // экранное поверх листа: заголовок, легенда, линейка, кнопки, «пометить»
            if (legendRoot != null) legendRoot.SetAsLastSibling();
            if (titleRoot != null) titleRoot.SetAsLastSibling();
            if (rulerRoot != null) rulerRoot.SetAsLastSibling();
            if (zoomRoot != null) zoomRoot.SetAsLastSibling();
            if (markButtonRoot != null) markButtonRoot.SetAsLastSibling();
        }

        private void RebuildAll()
        {
            ClearChildren(transform);
            sheetRect = null;
            viewport = null;
            paperRect = null;
            content = null;
            sectorRoot = null;
            edgeRoot = null;
            beltsRoot = null;
            bodiesRoot = null;
            labelsRoot = null;
            notesRoot = null;
            cometsRoot = null;
            legendRoot = null;
            rulerRoot = null;
            zoomRoot = null;
            titleRoot = null;
            markers.Clear();
            bodySizes.Clear();
            bodyLabels.Clear();
            bodyNotes.Clear();
            cometIcons.Clear();
            cometLastPos.Clear();
            hoverCaptureRing = null;
            hoverLandingRing = null;
            playerMarker = null;
            playerArrowRoot = null;
            landedPad = null;
            landedPadImage = null;
            targetRing = null;
            targetRingPulse = null;
            targetRingWide = null;
            targetRingPulseImage = null;
            targetRingWideImage = null;
            targetLabel = null;
            waypointRoot = null;
            waypointHalo = null;
            waypointPin = null;
            waypointHaloImage = null;
            waypointPinImage = null;
            targetIndex = -1;
            lastTargetBody = null;
            selectedBody = null;
            markButtonRoot = null;
            markButton = null;
            markButtonImage = null;
            markLabel = null;
            followButton = null;
            followLabel = null;
            followMode = false;
            bursts.Clear();
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
            FocusOnPlayer(); // после пересборки — вид к игроку (гвард built уже снят)
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

        // все подписи карты одним рукописным шрифтом (Н5.21-3 — только Excalifont);
        // высоты узлов ≥ fontSize*1.32 (урок ночи 4)
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
