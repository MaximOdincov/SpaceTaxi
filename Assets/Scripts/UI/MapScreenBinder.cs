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
    // (M8.3: бесшовный тайл кропа Art/background.png, Image.Tiled) лежит ВНУТРИ
    // зум-контента и СЧИТАЕТСЯ ОТ МИРА: размер = 2×граница мира×поля, тайл = 512
    // мировых юнитов — плотность зерна постоянна в юнитах мира, зум крутит бумагу
    // вместе с телами честно (прежде зерно было привязано к листу и «приближало»
    // вместе с ним). Тёмный стол (Backdrop) начинается ПРАВЕЕ борткомпа — панель
    // остаётся яркой при открытой карте (Н6.K1). Декор for_fun и компас-роза
    // убраны (Н6.K2), легенда — без подложки, чернила прямо на бумаге (Н6.K3).
    // Н6.HF4 сносил клики по телам; M8.8 вернул МИНИМАЛЬНЫЙ клик-выбор (без
    // записок/зум-при-клике/чипа): клик по телу (отличение от пана — порог
    // сдвига 6 px) ставит/снимает СВОЮ метку — зелёная булавка + зелёный
    // пульсирующий ореол на карте (зелёный = метка игрока), червон на радаре
    // и скобка в мире живут от Waypoint API. Правый клик / Esc (при открытой
    // карте) метку снимают. Осталось: пан ЛКМ, зум колесом
    // и кнопками (кламп minViewZoom..MaxViewZoom), следование за ракетой,
    // маркеры заказов (жёлтая цель). Waypoint-контракт
    // (Waypoint/WaypointChanged/Set/Clear/ToggleWaypoint) — для
    // WaypointIndicator/MinimapBinder.
    // Подписи — блок «иконка+имя» (картографическая модель 8 слотов, Н6.HF4):
    // подпись живёт ПРИ своей иконке на фиксированном близком расстоянии,
    // порядок размещения хаб → тела по радиусу → пояса; занятые слоты
    // блокируются, при занятых всех 8 — шаг дальше + тонкая чернильная
    // выноска. Decluttering по зуму (Н6.HF4.1): подписи трёх тиров видимости —
    // хаб/звёзды/дыра/пояса всегда, планеты от 22 юн с contentScale ≥ 0.20,
    // мелкие с ≥ 0.32 (contentScale = zoom/MaxViewZoom); при пересечении порога
    // подписи переразмещаются — скрытые не занимают слоты у видимых, выноска
    // прячется вместе со своей подписью.
    // Иконки тел — чистая графика (raycastTarget=false), М7.2: честный размер —
    // диаметр px = 2 × игровой радиус × scale (игровой радиус = построенное тело
    // из GravityField: data.Radius × PlanetSizeMul/VisualSizeMul), спрайт —
    // реальный игровой (K10-подход), вписан контентом арта как в Init.
    // М7.1: цель заказа и метка — константный ЭКРАННЫЙ размер (counter-scale).
    // Весь текст карты — только Excalifont (mapFont из сцены, фолбэк —
    // Resources). Взрыв корабля — два расходящихся кольца с искрами и
    // чернильный крест «здесь я погиб» до закрытия карты (Н6.K7 + M7.4,
    // подписка не ленивая — ловит смерть и при закрытой карте), стоянка —
    // тень-подставка (Н6.K7) и чернильное кольцо вокруг планеты (M7.3).
    // Курсор системный всегда (Н6.K8). Отдаление
    // клампится — при минимальном зуме виден ВЕСЬ мир (≤ 100% диаметра, M8.3:
    // прежде было ≤ 62% и мир не влезал целиком; поля бумаги дают FitMargin).
    // M7.5 (скайрим-дисциплина, конспект R) + M8.1/M8.2/M8.7 (вкус владельца):
    // бумага (арт владельца) + чернила для текста/служебной графики. Тела —
    // реальные игровые спрайты в РОДНЫХ цветах, без отмывки и теней-копий;
    // ракета-маркер — родной цвет арта. Цель заказа — жёлтый полупрозрачный
    // пульс (жёлтый = цель); красный Alarm — только смерть (клякса/взрыв);
    // метка-waypoint — зелёные булавка+кольцо (M8.8, зелёный = метка игрока).
    // Край мира — мягкая туманная
    // кромка без рамки-обода (BuildWorldEdge), районы — крупные географические
    // надписи с разрядкой,
    // легенда — компактный ключ с честными значками построенных тел,
    // линейка — тоньше и только на крупных зумах (SyncRuler), подписи —
    // альфа-фейд 0.12с на смене тира (StepLabelFades).
    // Карта открывается ПРИБЛИЖЕННОЙ К ИГРОКУ (зум 9; нет корабля — планета
    // старта). Публичный контракт: Waypoint + WaypointChanged +
    // Set/Clear/ToggleWaypoint (WaypointIndicator, MinimapBinder), SetView и
    // Esc-договорённость TryHandleEscape (оркестратор, MapScreenController).
    // Вид: колесо — зум к курсору ×1.2 (весь мир ↔ одна планета), ЛКМ-драг —
    // пан (Н6.K5: WASD-пан убран, W больше не двигает карту — игра под картой
    // ловит тягу честно).
    public class MapScreenBinder : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerDownHandler, IPointerClickHandler
    {
        [SerializeField] private TMP_FontAsset mapFont; // Excalifont-Regular SDF — назначен в сцене
        [SerializeField] private MapStyleConfig style;  // стиль карты (ручки владельца); null → Resources

        // — свои надписи/контуры (Map Style Editor): режим клика по карте —
        public static bool AnnotationPlacementMode;     // левый клик ставит аннотацию, правый отменяет
        public static AnnotationKind PlacementKind = AnnotationKind.Label;

        // стиль карты: назначенный в сцене → Resources/MapStyle_Default → временные
        // дефолты (значения = константы Н8.13 — карта не меняется). Кэшируется;
        // правится окном Space Taxi → Map Style Editor
        public MapStyleConfig Style
        {
            get
            {
                if (style == null)
                    style = Resources.Load<MapStyleConfig>("MapStyle_Default");
                if (style == null)
                {
                    style = ScriptableObject.CreateInstance<MapStyleConfig>();
                    style.name = "MapStyle_RuntimeDefaults";
                }
                return style;
            }
        }

        private RectTransform annotationsRoot;
        private readonly List<AnnotationNode> annotationNodes = new List<AnnotationNode>();

        // аннотация на карте: рект для окна видимости + режим кегля
        private struct AnnotationNode
        {
            public RectTransform rect;
            public bool screenFixed;
        }

        // — вид: базовый масштаб = весь мир вписан в лист, дальше зум к курсору —
        // Н6.HF1 → M8.3: минимальный зум считается в BuildMap от реального окна
        // (поле minViewZoom) так, чтобы при полном отдалении по каждой оси было
        // видно ≤ 100% диаметра мира — «полный отъезд = весь мир + поля бумаги»
        // (прежде 0.62 поднимало порог зума ×1.77 и мир не влезал целиком)
        private const float MinZoomWorldFraction = 1.0f; // доля диаметра мира в окне при минимальном зуме
        // — визуальные ручки теперь в MapStyleConfig (Map Style Editor); свойства
        // читают стиль — точки вызова сохранены, дефолты = прежние константы —
        private float MaxViewZoom => Style.maxViewZoom;     // от максимального отъезда до одной планеты крупно
        private float InitialViewZoom => Style.initialViewZoom; // карта открывается приближенной к игроку
        private const float ZoomDuration = 0.22f;
        private const float WheelStep = 1.2f;
        private const float ButtonZoomStep = 1.35f;
        private const float FitMargin = 1.10f;     // bbox тел занимает 90% листа
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
        // M8.3: бумага — тайл ОТ МИРА (не от окна): зерно привязано к мировым юнитам
        private float PaperWorldMargin => Style.paperWorldMargin;  // радиус бумаги = радиус мира × (поля за туманом)
        private float WorldUnitsPerTile => Style.worldUnitsPerTile; // мировых юнитов на один тайл бумаги
        private const int PaperTilePx = 512;            // размер кропа-тайла в пикселях текстуры
        // M7.2: относительная нормировка (RefIconPx/maxBodyRadius) убрана — иконка
        // = честный игровой диаметр 2 × Radius_game × scale; минимум — различимая
        // точка (4 px контента ≈ 9 px экрана на минимальном зуме; при 6 px
        // «минимальные» планеты уровня раздувались до 1.8× и отношение
        // «px карты / радиус мира» переставало быть единым)
        private const float MinIconPx = 4f;

        // M7.1: маркеры цели/метки — константный ЭКРАННЫЙ размер (скайрим-урок):
        // рут в зум-контенте несёт counter-scale 1/zoom (как кометы), дети
        // заданы в экранных px и не растут при приближении
        private float TargetRingPx => Style.targetRingPx;
        private float TargetRingWidePx => Style.targetRingWidePx;
        // M8.8: своя метка — зелёный аналог жёлтой цели заказа (кольца того же
        // калибра) + булавка
        private float WaypointHaloPx => Style.waypointHaloPx;
        private float WaypointWidePx => Style.waypointWidePx;
        private float WaypointPinPx => Style.waypointPinPx;

        // M8.8: клик-выбор — различение клика от пана (минимальная версия из
        // истории Н5.18-Н6.8) + минимальный радиус попадания в тело
        private const float ClickDragGuard = 6f;   // сдвиг больше этого — пан, а не клик
        private const float PickMinScreenPx = 14f; // мелкие тела кликаются в радиусе 14 экранных px

        // M8.7: жёлтый полупрозрачный ореол/кольцо цели (жёлтый = цель заказа,
        // Н5.28; янтарный Accent отменён владельцем). Тот же тон, что в игре
        // (OrderService.UpdateTargetGlow)
        private Color TargetRingColor => Style.targetColor;

        // M8.8: своя метка — зелёный (зелёный = метка игрока), единый цвет с
        // червоном радара (MinimapBinder.WaypointColor) и скобкой в мире
        private Color WaypointMarkColor => Style.waypointColor;

        // «нос» арт-спрайтов при localRotation = 0, градусов от +X (замер по пикселям):
        // ракета летит остриём в правый-верхний угол, комета — головой в левый-низ
        private const float RocketNoseDeg = 55f;
        private const float CometHeadDeg = 226f;

        // — waypoint для WaypointIndicator/MinimapBinder (Н-8): свойство + событие.
        // M8.8: способ задания — клик по телу на карте (минимальный клик-выбор,
        // повторный клик/правый клик/Esc снимают)
        public CelestialBodyData Waypoint { get; private set; }
        public event Action WaypointChanged;

        public void SetWaypoint(CelestialBodyData body)
        {
            if (Waypoint == body)
                return;
            Waypoint = body;
            WaypointChanged?.Invoke();
        }

        public void ClearWaypoint()
        {
            if (Waypoint == null)
                return;
            Waypoint = null;
            WaypointChanged?.Invoke();
        }

        public void ToggleWaypoint(CelestialBodyData body)
        {
            if (Waypoint == body)
                ClearWaypoint();
            else
                SetWaypoint(body);
        }

        // Esc из контроллера: M8.8 — при открытой карте Esc сначала снимает
        // СВОЮ метку (карта остаётся открытой), без метки — Esc закрывает карту
        // целиком (контракт с MapScreenController: true — Esc съеден)
        public bool TryHandleEscape()
        {
            if (Waypoint == null)
                return false;
            ClearWaypoint();
            return true;
        }

        private LevelAsset level;
        private GameSession session;
        private Ship ship;
        private RectTransform mapRect;
        private TMP_FontAsset font;
        private bool built;

        // карта построена и сейчас на экране (Map Style Editor: статус «правки видны сразу»)
        public bool IsBuilt => built;
        private float builtWidth;
        private Camera canvasCam; // null для Screen Space Overlay — так и нужно утилитам

        private RectTransform sheetRect;   // лист бумаги: ровно окно карты минус борткомп
        private RectTransform viewport;    // фиксированное окно вида (маска): клип не движется с камерой
        private RectTransform paperRect;   // фон-бумага — ребёнок зум-контента: «один слой» с миром
        private RectTransform content;     // зум-контейнер: бумага + весь нарисованный мир
        private RectTransform sectorRoot;  // подписи районов
        private CanvasGroup sectorFade;    // M7.5: районы гаснут на приближении
        private RectTransform edgeFogRoot; // M7.5: туманная кромка — вне блокирующего хрома (гигантский rect)
        private RectTransform edgeRoot;    // буи границы мира, тревожные пометки
        private RectTransform beltsRoot;   // камни поясов астероидов
        private RectTransform bodiesRoot;  // иконки тел
        private RectTransform labelsRoot;  // подписи тел (в плоскости бумаги)
        private RectTransform cometsRoot;  // живые кометы
        private RectTransform backdropRect; // тёмный стол — ТОЛЬКО правее борткомпа (Н6.K1)
        private RectTransform legendRoot;
        private RectTransform rulerRoot;
        private CanvasGroup rulerFade;     // M7.5: плавный показ линейки на крупных зумах
        private RectTransform zoomRoot;
        private RectTransform titleRoot;

        private OnboardComputerPanel onboardPanel; // его зона вычитается из окна карты
        private bool onboardShown;                 // последнее известное состояние панели
        private bool relayoutPending;              // экран изменился — пересчитать лист

        private readonly Dictionary<CelestialBodyData, float> bodySizes = new Dictionary<CelestialBodyData, float>();
        // M7.2: построенные тела уровня (ключ — DisplayName, совпадает с именем
        // GameObject): источник честного радиуса (Radius уже с PlanetSizeMul)
        // и реального игрового спрайта
        private readonly Dictionary<string, CelestialBody> gameBodies = new Dictionary<string, CelestialBody>();

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
        private RectTransform waypointWide;      // второе, широкое кольцо (как у цели)
        private RectTransform waypointPin;
        private Image waypointHaloImage;         // кэш: пульс обновляется каждый кадр
        private Image waypointWideImage;
        private Image waypointPinImage;
        private int targetIndex = -1;
        private CelestialBodyData lastTargetBody; // чья подпись спрятана под «ЦЕЛЬ: …»
        private float playerHeading;

        // подписи тел (заполняются в PlaceAllLabels; читаются ореолом цели)
        private readonly Dictionary<CelestialBodyData, TMP_Text> bodyLabels = new Dictionary<CelestialBodyData, TMP_Text>();
        // выноски подписей — toggl вместе со своей подписью (decluttering, ореол цели)
        private readonly Dictionary<CelestialBodyData, RectTransform> labelLeaders = new Dictionary<CelestialBodyData, RectTransform>();
        private int labelTierState = -1; // максимальный видимый тир подписей при текущем зуме
        private float labelPlacedZoom = 1f; // зум, под который разложены подписи (M7.5)

        // M7.5: альфа-фейд подписей на смене тира — новые подписи проявляются,
        // исчезающие доигрывают призраками (0.12с), вместо мгновенного popping.
        // TMP_Text.alpha — это альфа цвета шрифта (множителя нет): фейд пишет
        // в цвет BaseColor.a * K
        private sealed class LabelFade
        {
            public TMP_Text Label;    // живая подпись или призрак
            public Color BaseColor;   // цвет при K=1 (у призрака — захваченный текущий)
            public Image Leader;      // выноска (может быть null)
            public Color LeaderColor; // цвет выноски при K=1
            public bool Ghost;        // призрак — уничтожить по завершении
            public float K;           // прогресс 0..1
            public float TargetK;
        }
        private readonly List<LabelFade> labelFades = new List<LabelFade>();

        // снимок подписи, исчезающей на новом тире, — после сноса рисуется призраком
        private sealed class GhostLabel
        {
            public string Text;
            public float FontSize;
            public Color Color; // захваченный цвет (альфа уже частично погашена фейдом)
            public TextAlignmentOptions Align;
            public Vector2 Position;
            public Vector2 Size;
            public bool HasLeader;
            public Vector2 LeaderPosition;
            public Vector2 LeaderSize;
            public Quaternion LeaderRotation;
            public Color LeaderColor;
        }

        // живые кометы (по ActiveComets CometSystem — как точки миникарты)
        private CometSystem cometSystem;
        private readonly Dictionary<Comet, RectTransform> cometIcons = new Dictionary<Comet, RectTransform>();
        private readonly Dictionary<Comet, Vector2> cometLastPos = new Dictionary<Comet, Vector2>();

        private Vector2 worldCenter; // центр карты = origin мира (граница HullSystem — от него)
        private float mapRadius;
        private float bodyEdgeRadius; // фактический край тел/кометной зоны — от него туман (M8.3)
        private float scale = 1f;

        private float zoom = 1f;
        private float minViewZoom = 1f; // нижний порог зума отдаления; считается в BuildMap (Н6.HF1)
        private float zoomFrom = 1f;
        private float zoomTo = 1f;
        private float zoomT = 1f;
        private Vector2 zoomOffset;
        private Vector2 offFrom;
        private Vector2 offTo;
        private bool zoomAnim;

        // пан: ЛКМ-драг по листу и столу; клик (сдвиг ≤ ClickDragGuard) —
        // клик-выбор метки (M8.8)
        private bool dragging;
        private bool suppressClick; // нажатие переросло в пан — кликом не считать
        private float dragMoved;
        private Vector2 dragSheetPos;

        // режим «следить за ракетой» (Н6.K6)
        private bool followMode;
        private Button followButton;
        private TMP_Text followLabel;

        // анимации карты (Н6.K7): взрыв — кольцо+искры; стоянка — тень-подставка
        private HullSystem hull;
        private RectTransform landedPad;
        private Image landedPadImage;
        private RectTransform landedRing;       // M7.3: чернильное кольцо «припаркованы здесь»
        private RectTransform landedRingNote;   // подпись под кольцом — константного экранного размера
        private CelestialBody landedRingBody;   // вокруг какого тела построено
        private sealed class MapBurst
        {
            public RectTransform Root;
            public Image Ring;
            public Image RingLate;              // второе кольцо, отстаёт на задержку (M7.4)
            public readonly List<Image> Sparks = new List<Image>();
            public readonly List<Vector2> SparkVel = new List<Vector2>();
            public float Age;
            public Vector2 World;               // мировая точка гибели — для кляксы после кольца
        }
        private readonly List<MapBurst> bursts = new List<MapBurst>();

        // M7.4: смерть при закрытой/недостроенной карте — burst доигрывает при
        // открытии, кляксы-кресты «здесь я погиб» живут до закрытия карты
        private const int BurstSparks = 18;     // искр на взрыв (было 10 — заметность)
        private Vector2? pendingDeathWorld;
        private const int MaxDeathMarks = 4;    // память листа: последние гибели
        private readonly List<Vector2> deathMarkWorlds = new List<Vector2>();
        private readonly List<RectTransform> deathMarkRects = new List<RectTransform>();

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
            DeathListener.Ensure(this);
        }

        private void OnEnable()
        {
            BindHullDeath(); // M7.4: подписка не ленивая — смерть до первого Update тоже ловится
            FocusOnPlayer(); // повторные открытия — карта сразу приближена к игроку (Н5.21-8)
        }

        // шрифт: сцена (Excalifont) → ресурсы Excalifont → HUD → дефолт TMP.
        // Н5.21-3: на карте только Excalifont — все тексты идут через MakeLabel.
        private TMP_FontAsset ResolveFont()
        {
            if (Style.fontOverride != null)
                return Style.fontOverride; // Map Style Editor: шрифт карты из стиля
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
            BindHullDeath(); // дешёвый полл: корабль появляется после карты (GameSession.Start)

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
                SyncLabelVisibility(); // FocusOnPlayer поднял зум — тиры подписей сразу по нему
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
            SyncLabelVisibility(); // decluttering: переразмещение подписей при смене тира зума
            StepLabelFades();      // M7.5: альфа-фейд на смене тира вместо мгновенного popping
            SyncRuler();           // M7.5: линейка — только на крупных зумах
            SyncSectorFade();      // M7.5: районы гаснут на приближении (скайрим-поведение)
            UpdateFollow();
            UpdatePlayerMarker();
            UpdateLandedRing();    // M7.3: кольцо стоянки вокруг планеты
            UpdateTargetRing();
            UpdateWaypointVisual();
            UpdateComets();
            UpdateAnnotations();
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
            // карта закрылась — вид сбрасываем, следующий вход с чистого экрана;
            // кляксы гибелей и отложенный burst живут «до закрытия карты» (M7.4)
            dragging = false;
            suppressClick = false;
            dragMoved = 0f;
            relayoutPending = false;
            if (content != null)
                ApplyZoom(1f, Vector2.zero);
            zoomAnim = false;
            foreach (RectTransform mark in deathMarkRects)
                if (mark != null)
                    Destroy(mark.gameObject);
            deathMarkRects.Clear();
            deathMarkWorlds.Clear();
            pendingDeathWorld = null;
        }

        // ————— каркас: стол, лист, бумага, экранное (вне зум-контейнера) —————

        private void BuildChrome()
        {
            // тёмный стол вокруг листа: ловит рейкаст, чтобы ЛКМ-драг панировал
            // карту и за пределами листа. Н6.K1: стол начинается ПРАВЕЕ борткомпа —
            // панель под картой остаётся яркой (затемнение только на области карты)
            backdropRect = MakeRect("Backdrop", mapRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image backImage = backdropRect.gameObject.AddComponent<Image>();
            backImage.color = PaperMapArt.Desk;
            backImage.raycastTarget = true;

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
            // M8.3: бесшовный тайл бумаги (кроп background.png) — Image.Tiled,
            // тайл = 512 мировых юнитов (пересчёт в BuildMap, когда известен scale).
            // Тайла нет — ровный цвет Paper прежним спрайтом/заливкой
            Sprite paperTile = PaperMapArt.PaperTile();
            paperImage.type = paperTile != null ? Image.Type.Tiled : Image.Type.Simple;
            paperImage.sprite = paperTile != null ? paperTile : PaperMapArt.SheetArt();
            paperImage.color = paperImage.sprite != null ? Color.white : PaperMapArt.Paper;
            paperImage.raycastTarget = false;
            paperRect.sizeDelta = Vector2.one * 1000f; // временно; BuildMap ставит размер от мира

            BuildTitle();
            BuildZoomButtons();
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
            // бумага с M8.3 не зависит от окна: её размер считает BuildMap от мира
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
            LayoutSheet(); // размеры окна уже известны — лист актуален

            // M7.2: реальные тела уровня — радиус (уже с PlanetSizeMul) и игровой
            // спрайт; имя тела = имя GameObject (WorldBuilder). Заполняется ДО
            // bbox: честный fit считает игровые радиусы (M8.3)
            gameBodies.Clear();
            GravityField gravity = FindAnyObjectByType<GravityField>();
            if (gravity != null)
                foreach (CelestialBody gameBody in gravity.Bodies)
                    if (gameBody != null && !string.IsNullOrEmpty(gameBody.DisplayName))
                        gameBodies[gameBody.DisplayName] = gameBody;

            // граница мира = кольцо буёв (HullSystem бросает «ПОТЕРЯНА СВЯЗЬ» от origin)
            mapRadius = level.WorldRadius;
            float extX = 0f, extY = 0f; // bbox всех тел уровня — то, что обязано влезть
            bodyEdgeRadius = 0f;        // фактический край тел — от него туман (M8.3)
            foreach (CelestialBodyData body in level.Bodies)
            {
                float gameRadius = GameRadiusOf(body);
                float edge = body.Position.magnitude + gameRadius;
                if (edge > mapRadius)
                    mapRadius = edge + 100f;
                bodyEdgeRadius = Mathf.Max(bodyEdgeRadius, edge);
                extX = Mathf.Max(extX, Mathf.Abs(body.Position.x) + gameRadius);
                extY = Mathf.Max(extY, Mathf.Abs(body.Position.y) + gameRadius);
            }
            if (level.CometAreaRadius > 0f) // зона комет — тоже часть содержимого
            {
                extX = Mathf.Max(extX, Mathf.Abs(level.CometAreaCenter.x) + level.CometAreaRadius);
                extY = Mathf.Max(extY, Mathf.Abs(level.CometAreaCenter.y) + level.CometAreaRadius);
                bodyEdgeRadius = Mathf.Max(bodyEdgeRadius, level.CometAreaCenter.magnitude + level.CometAreaRadius);
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

            // M8.3: бумага — от МИРА: диаметр мира × поля; тайл = WorldUnitsPerTile
            // мировых юнитов → tilePx = PaperTilePx / (PPU/refPPU × multiplier),
            // отсюда multiplier = PaperTilePx×refPPU / (PPU-спрайта×юнитов×scale)
            if (paperRect != null)
            {
                paperRect.sizeDelta = Vector2.one * (2f * mapRadius * scale * PaperWorldMargin);
                Image paperImage = paperRect.GetComponent<Image>();
                if (paperImage != null && paperImage.type == Image.Type.Tiled)
                {
                    Canvas canvas = paperImage.canvas;
                    float refPpu = canvas != null ? canvas.referencePixelsPerUnit : 100f;
                    paperImage.pixelsPerUnitMultiplier =
                        PaperTilePx * refPpu / (100f * WorldUnitsPerTile * scale);
                }
            }

            // Н6.HF1 → M8.3: порог отдаления — при минимальном зуме по каждой оси
            // видно ≤ MinZoomWorldFraction (1.0) диаметра мира: полный отъезд
            // показывает весь мир, поля бумаги дают FitMargin
            float minByWidth = w / (scale * 2f * extX * MinZoomWorldFraction);
            float minByHeight = h / (scale * 2f * extY * MinZoomWorldFraction);
            minViewZoom = Mathf.Min(MaxViewZoom, Mathf.Max(minByWidth, minByHeight));

            // рамка листа «от руки» — когда размер известен
            MakeSketchFrame(sheetRect, WithAlpha(PaperMapArt.Ink, 0.75f), 2.6f, 1042);

            // слои снизу вверх: районы → край мира → пояса → подписи → тела →
            // кометы → метка/цель (игрок — на листе, выше всех). Нарисованные
            // линии (рамки районов, орбиты, кольца дистанций, сетка, пунктирные
            // пути) убраны совсем — Н5.21-5. Н6.HF4: кликов по телам больше нет,
            // иконки и подписи — чистая графика (raycastTarget=false); подписи
            // живут при своих иконках (PlaceAllLabels), слой ниже иконок — чтобы
            // «отошедшая» подпись читалась поверх чужих, но не накрывала тела
            sectorRoot = MakeRect("Sectors", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            sectorFade = sectorRoot.gameObject.AddComponent<CanvasGroup>(); // M7.5: фейд районов по зуму
            sectorFade.blocksRaycasts = false;
            sectorFade.interactable = false;
            // M7.5: туман/обод — отдельный слой ВНЕ edgeRoot: CollectStaticChrome
            // собирает детей edgeRoot как блокирующие rects слотов подписей, а
            // кромка накрывает весь лист (2222×2222 px) — каждый слот «столкался»
            // с туманом и все подписи уходили в дальний слот с выноской-«палкой»
            edgeFogRoot = MakeRect("EdgeFogRoot", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            edgeRoot = MakeRect("WorldEdge", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            beltsRoot = MakeRect("Belts", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            labelsRoot = MakeRect("Labels", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            bodiesRoot = MakeRect("Bodies", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // чернильная клякса в origin мира (отметка центра, не линия)
            MakeImage("WorldCenter", content, PaperMapArt.InkDot(),
                WithAlpha(PaperMapArt.Ink, 0.5f), Vector2.one * 7f, false);

            BuildSectorLabels();
            BuildCometZone();
            BuildWorldEdge();
            BuildLegend(); // M7.5: компактный ключ с честными значками — до PlaceAllLabels (блокирует слоты)
            BuildAnnotations(); // свои надписи/контуры владельца (Map Style Editor) — поверх подписей
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type == BodyType.AsteroidBelt)
                    CreateBelt(body);
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type != BodyType.AsteroidBelt)
                    CreateBody(body);
            PlaceAllLabels(); // Н6.HF4: подписи блоками «иконка+имя» — после всех иконок

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
                bool titleOn = Style.titleVisible && !string.IsNullOrEmpty(Style.titleText);
                if (titleRoot.gameObject.activeSelf != titleOn)
                    titleRoot.gameObject.SetActive(titleOn);
                TMP_Text title = titleRoot.GetComponentInChildren<TMP_Text>();
                if (title != null)
                    title.text = "система " + level.LevelName + " · " + Style.titleText;
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
                if (pair.Key == Sector.Hub)
                    continue; // у хаба есть собственная подпись тела — район «ХАБ» дублировал её

                // кольцевой тест — только по ПЛАНЕТАМ сектора: звёзды периферии
                // входят в сектора (Утренняя/S1 … Горн/S4), их радиус 3950 раздувал
                // разброс и уводил подписку в bbox-ветку «верх bbox» — все районы
                // собирались наверху листа (две — за пределами экрана)
                float rMin = float.MaxValue, rMax = 0f;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                var planetsOnly = new List<CelestialBodyData>();
                foreach (CelestialBodyData body in pair.Value)
                {
                    Vector2 p = body.Position;
                    minX = Mathf.Min(minX, p.x);
                    maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y);
                    maxY = Mathf.Max(maxY, p.y);
                    if (body.Type != BodyType.Planet)
                        continue;
                    float r = p.magnitude;
                    rMin = Mathf.Min(rMin, r);
                    rMax = Mathf.Max(rMax, r);
                    planetsOnly.Add(body);
                }

                // крупная географическая надпись района (M7.5): разрядка + низкая
                // альфа — «география», а не UI-элемент.
                // ХОТФИКС владельца: после круглого редизайна M8.4 сектор = КОНЦЕНТРИЧЕСКОЕ
                // КОЛЬЦО планет — подпись НА кольце (средний радиус планет) под своим
                // углом (вразлёт по компасу, середина дуги между планетами); позиция
                // живёт в мировых координатах и обновляется по зуму в SyncSectorFade
                // (sectorRoot — незум-слой). Географический сектор (разброс радиусов
                // планет велик) — прежняя логика bbox
                TMP_Text label = MakeLabel("SectorLabel_" + pair.Key, sectorRoot, SectorName(pair.Key), Style.districtFontSize,
                    WithAlpha(Style.districtColor, 0.35f));
                RectTransform labelRect = (RectTransform)label.transform;
                labelRect.sizeDelta = new Vector2(560f, 56f);

                bool isRing = planetsOnly.Count > 0 && rMax - rMin < 260f;
                if (isRing)
                {
                    // подпись НА кольце — контент-пространство (пан/зум везут её сами)
                    Vector2 world = new Vector2(Mathf.Cos(SectorLabelAngle((int)pair.Key) * Mathf.Deg2Rad),
                        Mathf.Sin(SectorLabelAngle((int)pair.Key) * Mathf.Deg2Rad)) * rMean(planetsOnly);
                    labelRect.anchoredPosition = WorldToMap(world);
                }
                else
                {
                    float xShift = ((int)pair.Key % 2 == 0 ? -1f : 1f) * (maxX - minX) * 0.20f;
                    labelRect.anchoredPosition = WorldToMap(new Vector2((minX + maxX) * 0.5f + xShift, maxY)) + Vector2.up * 34f;
                }
                labelRect.localRotation = Quaternion.Euler(0f, 0f, (Hash01((int)pair.Key * 7 + 3) - 0.5f) * 3f);
                label.characterSpacing = 16f;
                sectorTexts.Add(label); // кегль — константный экранный (контр-скейл в SyncSectorFade)
            }
        }

        private readonly List<TMP_Text> sectorTexts = new List<TMP_Text>();

        // углы районных подписей на кольцах — вразлёт по компасу (лево/право-верх/низ),
        // каждый угол — середина дуги между планетами своего кольца
        private static float SectorLabelAngle(int sector)
        {
            switch ((Sector)sector)
            {
                case Sector.Near: return 165f;  // лево, чуть выше горизонта
                case Sector.Mid1: return 75f;   // верх-право
                case Sector.Mid2: return 255f;  // низ-лево
                case Sector.Far: return 345f;   // право, чуть ниже горизонта
                default: return 75f + 90f * (sector % 4); // неизвестный сектор — по кругу
            }
        }

        private static float rMean(List<CelestialBodyData> bodies)
        {
            float sum = 0f;
            foreach (CelestialBodyData body in bodies)
                sum += body.Position.magnitude;
            return sum / Mathf.Max(1, bodies.Count);
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
        private static RectTransform MakeSegment(RectTransform parent, Vector2 a, Vector2 b, Color color, float thickness)
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
            return rect;
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
                WithAlpha(PaperMapArt.InkSoft, 0.8f));
            ((RectTransform)label.transform).sizeDelta = new Vector2(240f, 22f);
            ((RectTransform)label.transform).anchoredPosition = pos + Vector2.up * (level.CometAreaRadius * scale + 16f);
        }

        // граница мира (M7.5): туманная кромка — чернильный крап, нарастающий за
        // границей («дальше бумаги нет»), буи-звёздочки и пометки «за кругом —
        // потеря связи» (красный больше не здесь — только смерть).
        // M8.2: рамка-обод по краю удалена (вкус владельца), туман — мягкий:
        // растворение начинается глубже (0.65×R) и тянется шире — без резкой
        // внутренней границы
        private void BuildWorldEdge()
        {
            float borderPx = mapRadius * scale;

            // крап-туман: растворение начинается от ФАКТИЧЕСКОГО края тел
            // (bodyEdgeRadius, M8.3 — прежде 0.65×mapRadius, что при плотной
            // компоновке с WorldRadius 4600 накрыло бы Дальний район) и густеет
            // за кольцом буёв; живёт в edgeFogRoot — не блокирует слоты
            // подписей (см. BuildMap). Максимум с прежним поведением: у старого
            // мира 0.65×6000 ≈ 3936 — край тел, вид не меняется
            float fogInnerPx = Mathf.Max(bodyEdgeRadius, mapRadius * Style.fogInnerFrac) * scale;
            RectTransform fog = MakeImage("EdgeFog", edgeFogRoot, PaperMapArt.EdgeFog(),
                WithAlpha(PaperMapArt.Ink, 0.75f),
                Vector2.one * (fogInnerPx * 2f / PaperMapArt.EdgeFogBorderFrac), false);
            fog.anchoredPosition = Vector2.zero;
            fog.SetAsFirstSibling();

            Sprite star = PaperMapArt.BuoyStar();
            for (int i = 0; i < 12; i++)
            {
                float ang = i * 30f * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                RectTransform buoy = MakeImage("Buoy_" + i, edgeRoot, star,
                    WithAlpha(PaperMapArt.Ink, 0.7f), Vector2.one * 11f, false);
                buoy.anchoredPosition = dir * borderPx;
                buoy.localRotation = Quaternion.Euler(0f, 0f, i * 17f);
            }

            var warnColor = WithAlpha(PaperMapArt.InkSoft, 0.75f);
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

        // ————— пояса астероидов: камни map_asteroid по кольцу —————

        private void CreateBelt(CelestialBodyData body)
        {
            float diameter = Mathf.Max(12f, body.Radius * 2f * scale);
            Vector2 pos = WorldToMap(body.Position);
            bodySizes[body] = diameter;

            // камни: ОДИН спрайт map_asteroid — детерминированный разброс по кольцу.
            // M7.2: ширина кольца честная — в игре камни летят в Radius ± BeltWidth/2
            // (AsteroidBeltSystem.Init), карта больше не расширяет пояс ×1.7.
            // Кликовая зона пояса снесена (Н6.HF4): карта без кликов по телам;
            // подпись строится в PlaceAllLabels
            Sprite rock = PaperMapArt.MapSprite("map_asteroid");
            if (rock != null)
            {
                var rng = new System.Random(StableHash(body.DisplayName));
                int count = Mathf.Clamp(body.BeltAsteroidCount / 3, 9, 14);
                for (int i = 0; i < count; i++)
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float rr = (body.Radius + (float)(rng.NextDouble() * 2.0 - 1.0) * body.BeltWidth * 0.5f) * scale;
                    Vector2 p = pos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
                    // ХОТФИКС владельца: камни пояса с настоящим разбросом габаритов —
                    // степенное распределение (мало крупных, много мелких); прежде
                    // 9-18px читались «одного размера»
                    float size = 6f + Mathf.Pow((float)rng.NextDouble(), 2.2f) * 22f;
                    // M8.1: камни — родной цвет арта (отмывка снята по вкусу владельца)
                    RectTransform stone = MakeImage("Rock_" + i, beltsRoot, rock,
                        Color.white, Vector2.one * size, false);
                    stone.anchoredPosition = p;
                    stone.localRotation = Quaternion.Euler(0f, 0f, (float)rng.NextDouble() * 360f);
                    if (rng.NextDouble() < 0.5)
                        stone.localScale = new Vector3(-1f, 1f, 1f); // зеркалим — меньше повтора
                }
            }
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

        // ————— тела: реальный игровой облик, честный размер (M7.2) —————

        private void CreateBody(CelestialBodyData body)
        {
            // диаметр px = 2 × игровой радиус × scale: игровой радиус берём у
            // построенного тела (CelestialBody.Init умножил data.Radius на
            // PlanetSizeMul/VisualSizeMul — жалоба «по карте влетишь, а в игре
            // пролетаешь мимо»). Относительная нормировка maxBodyRadius убрана:
            // отношение «px карты / радиус мира» одно для всех тел.
            float d = Mathf.Max(MinIconPx, GameRadiusOf(body) * 2f * scale);
            // ХОТФИКС владельца (день после Н8): на карте — КАРТОГРАФИЧЕСКИЕ спрайты
            // Art/map (услуги читаются с иконки), не игровые арты планет; честный
            // размер (d) остаётся прежним — меняется только текстура
            Sprite art = MapSpriteOf(body);
            // вписываем КОНТЕНТ арта (альфа-границы, в мировых юнитах спрайта),
            // как вписывает тело CelestialBody.Init — видимый шар карты = шар игры
            Vector2 spr = art != null ? art.bounds.size : Vector2.one;
            Vector2 artContent = art != null ? SpriteArt.ContentSize(art) : Vector2.one;
            float contentMax = Mathf.Max(artContent.x, artContent.y);
            if (contentMax <= 0.0001f)
                contentMax = Mathf.Max(Mathf.Max(spr.x, spr.y), 0.0001f);
            Vector2 size = Vector2.Scale(spr, Vector2.one * (d / contentMax)); // видимая сторона = d

            RectTransform group = MakeRect("Body_" + body.DisplayName, bodiesRoot, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            group.sizeDelta = size;
            group.anchoredPosition = WorldToMap(body.Position);

            // иконка — чистая графика: без рейкаста, hover и кликов (Н6.HF4);
            // цвет — родной цвет арта (M8.1: чернильная отмывка снята по вкусу владельца)
            MakeImage("Icon", group, art, Color.white, size, false);
            bodySizes[body] = d;
            // подпись строится в PlaceAllLabels — блоками «иконка+имя» по приоритету
        }

        // игровой радиус тела: у построенного тела Radius уже с множителем;
        // мир ещё не построен (карта открыта раньше) — зеркало логики Init по данным
        private float GameRadiusOf(CelestialBodyData body)
        {
            if (gameBodies.TryGetValue(body.DisplayName, out CelestialBody gameBody) && gameBody != null)
                return gameBody.Radius;
            float mul = body.Type == BodyType.Planet
                ? (body.VisualSizeMul > 0f ? body.VisualSizeMul : WorldSprites.PlanetSizeMul(body.DisplayName))
                : 1f;
            return body.Radius * mul;
        }

        // спрайт иконки тела на карте — КАРТОГРАФИЧЕСКИЙ (Art/map: map_planet/
        // map_taxi_planet/map_gas_station/map_repair/map_sun/map_black_hole;
        // услуги читаются с иконки). Хотфикс владельца после Н8: прежде здесь был
        // игровой спрайт планеты. Картографии нет (арт удалён/не найден) —
        // прежний источник, игровой спрайт построенного тела
        private Sprite MapSpriteOf(CelestialBodyData body)
        {
            Sprite map = BodySprite(body);
            if (map != null)
                return map;
            if (gameBodies.TryGetValue(body.DisplayName, out CelestialBody gameBody) && gameBody != null)
            {
                SpriteRenderer renderer = gameBody.GetComponent<SpriteRenderer>();
                if (renderer != null && renderer.sprite != null)
                    return renderer.sprite;
            }
            return null;
        }

        private static void CollectRects(RectTransform root, List<Rect> into)
        {
            if (root == null)
                return;
            foreach (Transform child in root)
                if (child is RectTransform rect)
                    into.Add(CenteredRect(rect.anchoredPosition, rect.sizeDelta));
        }

        // ————— подписи «блок иконка+имя» (Н6.HF4): 8 близких слотов вокруг иконки —————
        // Подпись живёт ПРИ иконке на фиксированном близком расстоянии — «далеко от
        // своей планеты» невозможно по построению: иконка всегда на своём мировом
        // месте, двигается только подпись. Порядок размещения: хаб, затем тела по
        // убыванию радиуса, пояса последними; уже размещённые подписи занимают
        // слоты и блокируют их для следующих. Кандидат свободен, если его rect
        // (preferred-размер текста) не задевает чужие подписи, чужие иконки и
        // статичный хром (районы, край мира, легенда, заголовок). Если заняты все
        // 8 близких слотов — один шаг дальше в направлении с минимальной коллизией
        // и тонкая чернильная выноска от края иконки до подписи (не более одной на
        // тело); если и там коллизия — наименее коллизирующий слот (best effort).

        private const float LabelSlotGap = 3f;      // зазор края подписи от края иконки, px контента
        private const float LeaderThickness = 1.6f; // толщина чернильной выноски

        // — decluttering по зуму (Н6.HF4.1) — с ХОТФИКСА владельца («не у всех планет
        // есть надписи») тиры видимости для тел ВЫКЛЮЧЕНЫ (LabelTierOf всегда 0):
        // подпись у каждого тела на любом зуме. Пороги contentScale остались
        // триггерами ПЕРЕРАЗМЕЩЕНИЯ подписей при пересечении (дрейф разложенных) —
        // contentScale = zoom / MaxViewZoom (1.0 — максимальное приближение,
        // полный отъезд ≈ 0.16); скрытые подписи не блокируют слоты видимым
        private const float LabelTier1ZoomFraction = 0.20f; // порог переразмещения
        private const float LabelTier2ZoomFraction = 0.32f; // порог переразмещения
        private const float LabelFadeSeconds = 0.12f;       // альфа-фейд подписей на смене тира (M7.5)

        // M7.5: подписи тел — константного ЭКРАННОГО размера (скайрим-урок, как
        // маркеры M7.1): контентный кегль = LabelScreenFontSize / zoom, иначе
        // на приближении имена разрастаются (16px × zoom9 = 144px — «куча мала»).
        // Солвер не тронут: работает с тем же кеглем через GetPreferredValues;
        // при дрейфе зума подписи переразмещаются (SyncLabelVisibility)
        // ХОТФИКС владельца (день после Н8): 15px мелковато — искать планету по
        // названию тяжело; 18px на любом зуме (константный экранный кегль M7.5)
        private float LabelScreenFontSize => Style.labelFontSize;
        private const float LabelRezoomGrow = 1.20f;  // зум вырос на 20% — переразместить (кегль вырос)
        private const float LabelRezoomShrink = 0.55f; // зум упал сильнее — переразместить (кегль отстал)

        // M7.5: линейка — тонкая, чернила, только на крупных зумах
        private float RulerMinZoomFraction => Style.rulerMinZoomFraction; // видна при contentScale ≥

        // M7.5: районы — «география» листа, видна издалека; на приближении гаснут
        private float SectorFadeOutFraction => Style.districtFadeOutFraction; // исчезли при contentScale ≥

        // размещает подписи всех тел и поясов — вызывается один раз в BuildMap
        // ПОСЛЕ построения всех иконок: их rects известны сразу
        private void PlaceAllLabels()
        {
            // переразмещение при смене тира зума: старые подписи и выноски сносятся;
            // исчезающие на новом тире доигрывают призраками (M7.5)
            List<GhostLabel> ghosts = CaptureGhostLabels();
            ClearChildren(labelsRoot);
            bodyLabels.Clear();
            labelLeaders.Clear();

            var chrome = CollectStaticChrome();
            var iconRects = new Dictionary<CelestialBodyData, Rect>(level.Bodies.Count);
            foreach (CelestialBodyData body in level.Bodies)
            {
                float half = PlacementIconHalf(body);
                iconRects[body] = CenteredRect(WorldToMap(body.Position), Vector2.one * (half * 2f));
            }

            var placed = new List<Rect>(); // блокеры — только подписи, видимые на текущем тире
            foreach (CelestialBodyData body in LabelPlacementOrder())
            {
                string text = LabelText(body);
                if (string.IsNullOrEmpty(text))
                    continue; // безымянное тело не подписываем
                bool visible = LabelVisible(body);
                TMP_Text label = MakeLabel("Label_" + body.DisplayName, labelsRoot, text,
                    LabelScreenFontSize / Mathf.Max(zoom, 0.001f), // M7.5: константный экранный размер
                    body.Type == BodyType.AsteroidBelt ? WithAlpha(PaperMapArt.Ink, 0.85f) : WithAlpha(PaperMapArt.Ink, 0.95f));
                label.raycastTarget = false; // подпись не ловит клики — чистая графика
                Vector2 labelSize = label.GetPreferredValues();
                RectTransform labelRect = (RectTransform)label.transform;
                labelRect.sizeDelta = labelSize;
                bodyLabels[body] = label;

                Vector2 center = WorldToMap(body.Position);
                float iconHalf = PlacementIconHalf(body);
                Vector2 labelHalf = labelSize * 0.5f;

                int slot = -1;
                for (int i = 0; i < 8 && slot < 0; i++) // низ → верх → право → лево → диагонали
                {
                    Rect candidate = CenteredRect(center + SlotOffset(i, iconHalf, LabelSlotGap, labelHalf), labelSize);
                    if (PlacementCollisions(candidate, body, iconRects, placed, chrome) == 0)
                        slot = i;
                }

                RectTransform leader = null;
                if (slot >= 0)
                {
                    labelRect.anchoredPosition = center + SlotOffset(slot, iconHalf, LabelSlotGap, labelHalf);
                }
                else // все близкие слоты заняты — шаг дальше + линия-выноска
                {
                    float farGap = labelHalf.y + 6f;
                    int best = 0, bestCollisions = int.MaxValue;
                    Rect bestRect = Rect.zero;
                    for (int i = 0; i < 8; i++)
                    {
                        Rect candidate = CenteredRect(center + SlotOffset(i, iconHalf, farGap, labelHalf), labelSize);
                        int collisions = PlacementCollisions(candidate, body, iconRects, placed, chrome);
                        if (collisions < bestCollisions)
                        {
                            best = i;
                            bestCollisions = collisions;
                            bestRect = candidate;
                        }
                    }
                    labelRect.anchoredPosition = bestRect.center;
                    leader = MakeLeaderLine(best, center, iconHalf, bestRect); // не более одной на тело
                    labelLeaders[body] = leader;
                }

                // decluttering (M7.5): вместо мгновенного enabled — альфа-фейд;
                // скрытая подпись позицию держит (при появлении не «переезжает»
                // с нуля), но слоты видимым не занимает.
                // ВАЖНО: TMP_Text.alpha пишет прямо в color.a — полный цвет
                // захватываем ДО гашения, иначе фейд гонит ноль к нулю
                Color fullColor = label.color;
                label.alpha = 0f; // старт фейда: видимая проявится к 1, скрытая остаётся прозрачной
                if (leader != null)
                    leader.gameObject.SetActive(visible);
                if (visible)
                {
                    placed.Add(CenteredRect(labelRect.anchoredPosition, labelSize));
                    labelFades.Add(new LabelFade
                    {
                        Label = label,
                        BaseColor = fullColor,
                        Leader = leader != null ? leader.GetComponent<Image>() : null,
                        LeaderColor = WithAlpha(PaperMapArt.Ink, 0.5f),
                        K = 0f,
                        TargetK = 1f
                    });
                }
            }

            SpawnGhostLabels(ghosts);
            labelTierState = LabelTierState(); // состояние синхронно с выполненным размещением
            labelPlacedZoom = zoom;            // кегль разложен под этот зум (M7.5)
        }

        // снимок подписей, исчезающих на новом тире (старое состояние живо в
        // bodyLabels), — после сноса листа они доигрывают призраками
        private List<GhostLabel> CaptureGhostLabels()
        {
            List<GhostLabel> ghosts = null;
            if (bodyLabels.Count == 0)
                return ghosts;
            foreach (var pair in bodyLabels)
            {
                TMP_Text label = pair.Value;
                if (label == null || LabelVisible(pair.Key) || label.alpha <= 0.02f)
                    continue;
                RectTransform rect = (RectTransform)label.transform;
                var ghost = new GhostLabel
                {
                    Text = label.text,
                    FontSize = label.fontSize,
                    Color = label.color,
                    Align = label.alignment,
                    Position = rect.anchoredPosition,
                    Size = rect.sizeDelta
                };
                if (labelLeaders.TryGetValue(pair.Key, out RectTransform leader) && leader != null
                    && leader.gameObject.activeSelf)
                {
                    Image image = leader.GetComponent<Image>();
                    ghost.HasLeader = true;
                    ghost.LeaderPosition = leader.anchoredPosition;
                    ghost.LeaderSize = leader.sizeDelta;
                    ghost.LeaderRotation = leader.localRotation;
                    ghost.LeaderColor = image != null ? image.color : WithAlpha(PaperMapArt.Ink, 0.5f);
                }
                (ghosts ??= new List<GhostLabel>()).Add(ghost);
            }
            return ghosts;
        }

        // призраки: копии исчезнувших подписей (и выносок) — гаснут за 0.12с и умирают
        private void SpawnGhostLabels(List<GhostLabel> ghosts)
        {
            if (ghosts == null)
                return;
            foreach (GhostLabel ghost in ghosts)
            {
                TMP_Text label = MakeLabel("LabelGhost", labelsRoot, ghost.Text, ghost.FontSize, ghost.Color, ghost.Align);
                RectTransform rect = (RectTransform)label.transform;
                rect.sizeDelta = ghost.Size;
                rect.anchoredPosition = ghost.Position;

                Image leaderImage = null;
                if (ghost.HasLeader)
                {
                    var go = new GameObject("LeaderGhost", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(labelsRoot, false);
                    RectTransform leaderRect = (RectTransform)go.transform;
                    leaderRect.anchorMin = leaderRect.anchorMax = new Vector2(0.5f, 0.5f);
                    leaderRect.anchoredPosition = ghost.LeaderPosition;
                    leaderRect.sizeDelta = ghost.LeaderSize;
                    leaderRect.localRotation = ghost.LeaderRotation;
                    leaderImage = go.GetComponent<Image>();
                    leaderImage.sprite = StubSprites.Line();
                    leaderImage.color = ghost.LeaderColor;
                    leaderImage.raycastTarget = false;
                }

                labelFades.Add(new LabelFade
                {
                    Label = label,
                    BaseColor = ghost.Color,
                    Leader = leaderImage,
                    LeaderColor = ghost.LeaderColor,
                    K = 1f,
                    TargetK = 0f,
                    Ghost = true
                });
            }
        }

        // шаг фейда: живые подписи проявляются к 1, призраки гаснут и умирают
        private void StepLabelFades()
        {
            if (labelFades.Count == 0)
                return;
            float step = Time.unscaledDeltaTime / LabelFadeSeconds;
            for (int i = labelFades.Count - 1; i >= 0; i--)
            {
                LabelFade fade = labelFades[i];
                if (fade.Label == null) // пережил пересборку карты — просто забыть
                {
                    labelFades.RemoveAt(i);
                    continue;
                }
                fade.K = Mathf.MoveTowards(fade.K, fade.TargetK, step);
                var color = fade.BaseColor;
                color.a *= fade.K;
                fade.Label.color = color;
                if (fade.Leader != null)
                {
                    var leaderColor = fade.LeaderColor;
                    leaderColor.a *= fade.K;
                    fade.Leader.color = leaderColor;
                }
                if (fade.K == fade.TargetK)
                {
                    if (fade.Ghost)
                        Destroy(fade.Label.gameObject);
                    labelFades.RemoveAt(i);
                }
            }
        }

        // статичный хром блок-листа: край мира (буи, «потеря связи», кометная
        // зона) — в координатах контента; легенда и заголовок лежат на листе —
        // их rect переводится в координаты контента.
        // M7.5: районы в хром НЕ собираются — их 560×56-rects в контент-px
        // накрывали почти весь лист (вид на zoom9 ≈ 100×61 px) и гнали все
        // подписи в дальние слоты с выносками-«палками». Район — фоновый
        // водяной знак (alpha ~0.35): мелкая подпись поверх него читается
        private List<Rect> CollectStaticChrome()
        {
            var chrome = new List<Rect>();
            CollectRects(edgeRoot, chrome);
            AddSheetChrome(legendRoot, chrome);
            AddSheetChrome(titleRoot, chrome);
            return chrome;
        }

        // rect экранных детей листа в координатах контента: при сборке зум=1 и
        // смещение=0, а центры листа и контента совпадают — локальные координаты
        // листа и контента тождественны
        private void AddSheetChrome(RectTransform child, List<Rect> into)
        {
            if (child == null || sheetRect == null)
                return;
            Rect parentRect = sheetRect.rect;
            Vector2 pivot = child.pivot;
            Vector2 anchorRef = new Vector2(
                parentRect.xMin + Mathf.Lerp(child.anchorMin.x, child.anchorMax.x, pivot.x) * parentRect.width,
                parentRect.yMin + Mathf.Lerp(child.anchorMin.y, child.anchorMax.y, pivot.y) * parentRect.height);
            Vector2 center = anchorRef + child.anchoredPosition
                + Vector2.Scale(Vector2.one * 0.5f - pivot, child.rect.size);
            into.Add(CenteredRect(center, child.rect.size));
        }

        // половина «иконки» для слотов и коллизий: у пояса — кольцо камней
        // (честная ширина, как в игре ±BeltWidth/2), у остальных — иконка
        private float PlacementIconHalf(CelestialBodyData body)
        {
            return body.Type == BodyType.AsteroidBelt
                ? (body.Radius + body.BeltWidth * 0.5f) * scale
                : IconSizeOf(body) * 0.5f;
        }

        // ХОТФИКС владельца («не у всех планет есть надписи»): decluttering тиров
        // для тел выключен — подпись теперь у КАЖДОГО тела на любом зуме (кегль
        // константный экранный, солвер разводит с выносками; на полном отъезде
        // прежде планеты молчали до приближения). Механика переразмещения при
        // зуме (дрейф разложенных подписей) осталась
        private static int LabelTierOf(CelestialBodyData body)
        {
            return 0;
        }

        // нормированный масштаб контента на экране → максимальный видимый тир:
        // полный отъезд показывает только тир 0, приближение открывает 1-й и 2-й
        private int LabelTierState()
        {
            float contentScale = zoom / MaxViewZoom;
            if (contentScale >= LabelTier2ZoomFraction)
                return 2;
            return contentScale >= LabelTier1ZoomFraction ? 1 : 0;
        }

        // видима ли подпись при текущем тире зума
        private bool LabelVisible(CelestialBodyData body)
        {
            return LabelTierOf(body) <= labelTierState;
        }

        // decluttering: при пересечении порога тира подписи переразмещаются —
        // дешёвый полл ловит любую смену зума (колесо, кнопки, анимация,
        // следование); M7.5: переразмещение и при дрейфе зума от разложенного —
        // кегль подписей константного экранного размера (LabelScreenFontSize/zoom)
        private void SyncLabelVisibility()
        {
            if (labelsRoot == null)
                return;
            int state = LabelTierState();
            bool rezoom = zoom > labelPlacedZoom * LabelRezoomGrow || zoom < labelPlacedZoom * LabelRezoomShrink;
            if (state == labelTierState && !rezoom)
                return;
            labelTierState = state;
            PlaceAllLabels();
            // ореол цели в этом же кадре перечитает свежие подписи и снова
            // спрячет подпись своей цели (targetIndex сброшен — re-init)
            targetIndex = -1;
            lastTargetBody = null;
        }

        // порядок размещения: хаб первым, затем тела по убыванию радиуса,
        // пояса последними (внутри группы — порядок уровня)
        private IEnumerable<CelestialBodyData> LabelPlacementOrder()
        {
            CelestialBodyData hub = null;
            var planets = new List<CelestialBodyData>();
            var belts = new List<CelestialBodyData>();
            foreach (CelestialBodyData body in level.Bodies)
            {
                if (body.Type == BodyType.AsteroidBelt)
                    belts.Add(body);
                else if (body.IsHub && hub == null)
                    hub = body;
                else
                    planets.Add(body);
            }
            planets.Sort((a, b) => b.Radius.CompareTo(a.Radius));
            if (hub != null)
                yield return hub;
            foreach (CelestialBodyData body in planets)
                yield return body;
            foreach (CelestialBodyData body in belts)
                yield return body;
        }

        // текст подписи: у пояса — «пояс Имя»; безымянные не подписываются
        private static string LabelText(CelestialBodyData body)
        {
            if (string.IsNullOrEmpty(body.DisplayName))
                return "";
            if (body.Type != BodyType.AsteroidBelt)
                return body.DisplayName;
            string name = body.DisplayName;
            return name.ToLower().Contains("пояс") ? name : "пояс " + name;
        }

        // центр подписи относительно центра иконки: слоты 0 низ (дефолт), 1 верх,
        // 2 право, 3 лево (по центру иконки), 4..7 — диагонали низ-право,
        // низ-лево, верх-право, верх-лево
        private static Vector2 SlotOffset(int slot, float iconHalf, float gap, Vector2 labelHalf)
        {
            float sx = 0f, sy = -1f;
            switch (slot)
            {
                case 1: sy = 1f; break;
                case 2: sx = 1f; sy = 0f; break;
                case 3: sx = -1f; sy = 0f; break;
                case 4: sx = 1f; break;
                case 5: sx = -1f; break;
                case 6: sx = 1f; sy = 1f; break;
                case 7: sx = -1f; sy = 1f; break;
            }
            return new Vector2(sx * (iconHalf + gap + labelHalf.x), sy * (iconHalf + gap + labelHalf.y));
        }

        // число препятствий под кандидатом: чужие подписи, чужие иконки, хром.
        // Свою иконку кандидат не накрывает по построению слота — не считаем
        private static int PlacementCollisions(Rect candidate, CelestialBodyData self,
            Dictionary<CelestialBodyData, Rect> iconRects, List<Rect> placedLabels, List<Rect> chrome)
        {
            int count = 0;
            foreach (Rect rect in placedLabels)
                if (candidate.Overlaps(rect))
                    count++;
            foreach (var pair in iconRects)
                if (pair.Key != self && candidate.Overlaps(pair.Value))
                    count++;
            foreach (Rect rect in chrome)
                if (candidate.Overlaps(rect))
                    count++;
            return count;
        }

        // тонкая чернильная линия-выноска от края иконки до края подписи —
        // функциональный элемент: связывает отошедшую подпись с телом.
        // Rect возвращается вызывающему: toggl вместе со своей подписью
        private RectTransform MakeLeaderLine(int slot, Vector2 center, float iconHalf, Rect labelRect)
        {
            const float diag = 0.70710678f;
            Vector2 a, b;
            switch (slot)
            {
                case 1: a = center + Vector2.up * iconHalf; b = new Vector2(center.x, labelRect.yMin); break;
                case 2: a = center + Vector2.right * iconHalf; b = new Vector2(labelRect.xMin, center.y); break;
                case 3: a = center + Vector2.left * iconHalf; b = new Vector2(labelRect.xMax, center.y); break;
                case 4: a = center + new Vector2(diag, -diag) * iconHalf; b = new Vector2(labelRect.xMin, labelRect.yMax); break;
                case 5: a = center + new Vector2(-diag, -diag) * iconHalf; b = new Vector2(labelRect.xMax, labelRect.yMax); break;
                case 6: a = center + new Vector2(diag, diag) * iconHalf; b = new Vector2(labelRect.xMin, labelRect.yMin); break;
                case 7: a = center + new Vector2(-diag, diag) * iconHalf; b = new Vector2(labelRect.xMax, labelRect.yMin); break;
                default: a = center + Vector2.down * iconHalf; b = new Vector2(center.x, labelRect.yMax); break;
            }
            return MakeSegment(labelsRoot, a, b, WithAlpha(PaperMapArt.Ink, 0.5f), LeaderThickness);
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

        // ————— метка (waypoint) и цель заказа —————

        private void BuildWaypoint()
        {
            // M7.1: константный экранный размер — рут несёт counter-scale 1/zoom,
            // дети заданы в экранных px и больше не растут от IconSizeOf×zoom.
            // M8.8: своя метка ЗЕЛЁНАЯ (зелёный = метка игрока) — ореол-кольца
            // того же калибра, что у жёлтой цели заказа, булавка над кольцом
            waypointRoot = MakeRect("WaypointRoot", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            waypointWide = MakeImage("Wide", waypointRoot, PaperMapArt.TargetRing(),
                WithAlpha(WaypointMarkColor, 0.5f), Vector2.one * WaypointWidePx, false);
            waypointHalo = MakeImage("Halo", waypointRoot, PaperMapArt.TargetRing(),
                WaypointMarkColor, Vector2.one * WaypointHaloPx, false);
            waypointPin = MakeImage("Pin", waypointRoot, PaperMapArt.Pin(),
                WaypointMarkColor, Vector2.one * WaypointPinPx, false);
            waypointPin.anchoredPosition = Vector2.up * (WaypointHaloPx * 0.5f + 16f);
            waypointHaloImage = waypointHalo.GetComponent<Image>(); // кэш для кадра
            waypointWideImage = waypointWide.GetComponent<Image>();
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
            waypointRoot.localScale = Vector3.one / Mathf.Max(zoom, 0.001f); // экранный px постоянны

            // ХОТФИКС владельца: кольца метки должны «зафиксироваться» — прежде
            // альфы пульсовали В ПРОТИВОФАЗЕ (гасло 64px разгоралось, когда
            // широкое 90px гасло, и наоборот) — зелёное пятно дышало видимым
            // размером 64↔90px. Теперь оба кольца пульсируют альфой В ФАЗЕ
            // (широкое всегда тусклее в постоянной пропорции) — пятно не меняет
            // видимый диаметр; экранный размер колец постоянен (counter-scale)
            float t = Time.unscaledTime;
            float pulse = 0.72f + 0.22f * Mathf.Sin(t * 3.4f);
            if (waypointHaloImage != null)
                waypointHaloImage.color = WithAlpha(WaypointMarkColor, pulse);
            if (waypointWideImage != null)
                waypointWideImage.color = WithAlpha(WaypointMarkColor, pulse * 0.55f);
            if (waypointPinImage != null)
                waypointPinImage.color = WithAlpha(WaypointMarkColor, 0.75f + 0.25f * Mathf.Sin(t * 2.8f + 1f));
        }

        private void BuildTargetRing()
        {
            // M7.1: константный экранный размер (руту ставит counter-scale в Update);
            // M8.7: единственный насыщенный цвет карты — жёлтый ореол цели
            targetRing = MakeRect("TargetRing", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            targetRingPulse = MakeImage("Ring", targetRing, PaperMapArt.TargetRing(),
                TargetRingColor, Vector2.one * TargetRingPx, false);
            targetRingWide = MakeImage("RingWide", targetRing, PaperMapArt.TargetRing(),
                WithAlpha(TargetRingColor, 0.7f), Vector2.one * TargetRingWidePx, false);
            targetRingPulseImage = targetRingPulse.GetComponent<Image>(); // кэш для кадра
            targetRingWideImage = targetRingWide.GetComponent<Image>();
            targetLabel = MakeLabel("TargetLabel", targetRing, "", 16f, TargetRingColor);
            ((RectTransform)targetLabel.transform).sizeDelta = new Vector2(280f, 24f);
            ((RectTransform)targetLabel.transform).anchoredPosition = Vector2.up * (TargetRingPx * 0.5f + 18f);
            targetLabel.characterSpacing = 2f;
            targetRing.gameObject.SetActive(false);
            targetIndex = -1;
        }

        // честный диаметр иконки на карте (px контента) — читают слоты подписей
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
                {
                    own.enabled = false;
                    if (labelLeaders.TryGetValue(body, out RectTransform leader) && leader != null)
                        leader.gameObject.SetActive(false); // выноска прячется вместе с подписью
                }
                targetLabel.text = "ЦЕЛЬ: " + body.DisplayName.ToUpper();
            }
            targetRing.anchoredPosition = WorldToMap(body.Position);
            targetRing.localScale = Vector3.one / Mathf.Max(zoom, 0.001f); // M7.1: экранные px постоянны
            // пульс: кольца дышат размером и альфой
            float t = Time.unscaledTime;
            targetRingPulse.localScale = Vector3.one * (1f + 0.09f * Mathf.Sin(t * 4.2f));
            if (targetRingPulseImage != null)
                targetRingPulseImage.color = WithAlpha(TargetRingColor, 0.62f + 0.3f * Mathf.Sin(t * 3.4f));
            if (targetRingWideImage != null)
                targetRingWideImage.color = WithAlpha(TargetRingColor, 0.35f + 0.25f * Mathf.Sin(t * 3.4f + Mathf.PI));
        }

        // ————— свои надписи и контуры (Map Style Editor) —————

        // слой аннотаций поверх подписей: клики не ест (raycastTarget=false),
        // пан/зум работают сквозь него как по листу
        private void BuildAnnotations()
        {
            if (annotationsRoot != null)
                Destroy(annotationsRoot.gameObject);
            annotationNodes.Clear();
            annotationsRoot = MakeRect("Annotations", content, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            annotationsRoot.SetAsLastSibling();
            var list = Style.annotations;
            for (int i = 0; i < list.Count; i++)
                annotationNodes.Add(BuildAnnotation(list[i], i));
            UpdateAnnotations();
        }

        private AnnotationNode BuildAnnotation(MapAnnotation ann, int index)
        {
            string name = "Ann_" + index;
            Vector2 pos = WorldToMap(ann.worldPosition);
            var node = new AnnotationNode();
            switch (ann.kind)
            {
                case AnnotationKind.Label:
                {
                    TMP_Text label = MakeLabel(name, annotationsRoot, ann.text, ann.fontSize, ann.color);
                    if (ann.screenFixedSize) // экранный кегль: контентный = кегль/zoom
                        label.fontSize = ann.fontSize / Mathf.Max(zoom, 0.001f);
                    RectTransform rect = (RectTransform)label.transform;
                    rect.anchoredPosition = pos;
                    node.rect = rect;
                    node.screenFixed = ann.screenFixedSize;
                    break;
                }
                case AnnotationKind.Ring:
                {
                    // рисованный карандашный круг — тот же спрайт, что у колец меток;
                    // живёт в масштабе мира (географический контур района)
                    RectTransform ring = MakeImage(name, annotationsRoot, PaperMapArt.TargetRing(), ann.color,
                        Vector2.one * (Mathf.Max(1f, ann.radius) * 2f * scale), false);
                    ring.anchoredPosition = pos;
                    node.rect = ring;
                    break;
                }
                case AnnotationKind.Circle:
                {
                    RectTransform fill = MakeImage(name, annotationsRoot, StubSprites.SoftDot(64f, 0f), ann.color,
                        Vector2.one * (Mathf.Max(1f, ann.radius) * 2f * scale), false);
                    fill.anchoredPosition = pos;
                    node.rect = fill;
                    break;
                }
                case AnnotationKind.Rect:
                {
                    // рамка из чернильных линий (толщина в экранных px); группа —
                    // точка мира, линии в её локальных координатах
                    RectTransform group = MakeRect(name, annotationsRoot, new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                    group.anchoredPosition = pos;
                    float halfW = Mathf.Max(1f, ann.radius) * scale;
                    float halfH = Mathf.Max(1f, ann.height) * scale;
                    Color c = ann.color;
                    MakeSegment(group, new Vector2(-halfW, halfH), new Vector2(halfW, halfH), c, ann.thicknessPx);
                    MakeSegment(group, new Vector2(-halfW, -halfH), new Vector2(halfW, -halfH), c, ann.thicknessPx);
                    MakeSegment(group, new Vector2(-halfW, -halfH), new Vector2(-halfW, halfH), c, ann.thicknessPx);
                    MakeSegment(group, new Vector2(halfW, -halfH), new Vector2(halfW, halfH), c, ann.thicknessPx);
                    node.rect = group;
                    break;
                }
            }
            return node;
        }

        // окно видимости по зуму + экранный кегль fixed-надписей — каждый кадр
        private void UpdateAnnotations()
        {
            if (annotationsRoot == null)
                return;
            var list = Style.annotations;
            if (list.Count != annotationNodes.Count)
            {
                BuildAnnotations(); // правка из окна (добавили/удалили) — слой рассинхронился
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                MapAnnotation ann = list[i];
                RectTransform rect = annotationNodes[i].rect;
                if (rect == null)
                    continue;
                bool visible = (ann.minZoom <= 0f || zoom >= ann.minZoom - 0.001f)
                    && (ann.maxZoom <= 0f || zoom <= ann.maxZoom + 0.001f);
                if (rect.gameObject.activeSelf != visible)
                    rect.gameObject.SetActive(visible);
                if (annotationNodes[i].screenFixed && visible)
                {
                    TMP_Text label = rect.GetComponent<TMP_Text>();
                    if (label != null)
                    {
                        float target = ann.fontSize / Mathf.Max(zoom, 0.001f);
                        if (!Mathf.Approximately(label.fontSize, target))
                            label.fontSize = target;
                    }
                }
            }
        }

        // поставить аннотацию в точку мира (клик из Map Style Editor / API):
        // дописывает в стиль, перерисовывает слой; ассет сохраняется — аннотации
        // переживают выход из плея
        public MapAnnotation AddAnnotationAtWorld(Vector2 world, AnnotationKind kind, string text = null)
        {
            var ann = new MapAnnotation { kind = kind, worldPosition = world, text = !string.IsNullOrEmpty(text) ? text : DefaultAnnotationText(kind) };
            if (kind == AnnotationKind.Rect)
            {
                ann.radius = 320f;
                ann.height = 220f;
            }
            Style.annotations.Add(ann);
            PersistStyle();
            if (built)
                BuildAnnotations();
            return ann;
        }

        // удалить последнюю аннотацию (кнопка окна)
        public bool RemoveLastAnnotation()
        {
            var list = Style.annotations;
            if (list.Count == 0)
                return false;
            list.RemoveAt(list.Count - 1);
            PersistStyle();
            if (built)
                BuildAnnotations();
            return true;
        }

        // применить стиль, правленный в Map Style Editor: пересборка листа
        // (не построена — применится при следующем открытии карты)
        public void RefreshMapStyle()
        {
            if (built)
                RebuildAll();
        }

        private static string DefaultAnnotationText(AnnotationKind kind)
        {
            switch (kind)
            {
                case AnnotationKind.Ring: return "контур";
                case AnnotationKind.Circle: return "зона";
                case AnnotationKind.Rect: return "рамка";
                default: return "метка";
            }
        }

        // правки стиля из плея пишутся в ассет — аннотации/ручки переживают выход
        private void PersistStyle()
        {
#if UNITY_EDITOR
            if (style != null && UnityEditor.AssetDatabase.Contains(style))
            {
                UnityEditor.EditorUtility.SetDirty(style);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(style);
            }
#endif
        }

        private void RestoreTargetLabel()
        {
            if (lastTargetBody != null && bodyLabels.TryGetValue(lastTargetBody, out TMP_Text label))
            {
                // M7.5: подписи живут через альфу — вернуть текущее фейд-состояние
                // (актуальная альфа уже записана в цвет: живым фейдом или стартовым гашением)
                label.enabled = true;
                float alpha = label.color.a;
                foreach (LabelFade fade in labelFades)
                    if (fade.Label == label && !fade.Ghost)
                    {
                        alpha = fade.BaseColor.a * fade.K;
                        break;
                    }
                label.alpha = alpha;
                if (labelLeaders.TryGetValue(lastTargetBody, out RectTransform leader) && leader != null)
                    leader.gameObject.SetActive(alpha > 0.01f); // выноска toggl вместе с подписью
            }
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
                // M8.1: родной цвет арта ракеты — «вы здесь» держат размер,
                // подпись и движение маркера
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

            if (ship != null && ship.State == ShipState.Landed)
            {
                // M8.5: поза посадки — нос маркера по ориентации КОРПУСА
                // (SpriteNoseAngle, как на радаре Н7 G7.2), а не по последнему
                // вектору полёта: прежде ракета на карте «падала» носом вниз
                playerHeading = HullHeadingDeg();
            }
            else if (ship != null && ship.State == ShipState.Flying && ship.Body != null
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
            Vector2 clamped = SheetClampView(sheetPos); // кламп внутрь окна вида
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

        // M7.3: фиксация при посадке — чернильное кольцо «вы припаркованы здесь»
        // вокруг планеты стоянки (в content-координатах: «территория», скейлится
        // зумом честно, как иконка). Пересоздаётся на смену LandedOn; стоялка
        // читается поллом — событие взлёта не существует
        private void UpdateLandedRing()
        {
            if (content == null)
                return;
            CelestialBody parkedOn = ship != null && ship.State == ShipState.Landed
                ? ship.LandedOn
                : null;
            if (parkedOn == null)
            {
                if (landedRing != null && landedRing.gameObject.activeSelf)
                    landedRing.gameObject.SetActive(false);
                landedRingBody = null;
                return;
            }

            if (landedRing == null)
            {
                landedRing = MakeRect("LandedRing", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                Image ring = landedRing.gameObject.AddComponent<Image>();
                ring.sprite = PaperMapArt.TargetRing(); // кольцо «от руки»
                ring.color = WithAlpha(PaperMapArt.Ink, 0.85f); // M7.5: чернила, без второго цвета «ВЫ»
                ring.raycastTarget = false;
                TMP_Text note = MakeLabel("ParkNote", landedRing, "припаркованы", 14f,
                    WithAlpha(PaperMapArt.Ink, 0.9f));
                landedRingNote = (RectTransform)note.transform;
                landedRingNote.sizeDelta = new Vector2(150f, 20f);
            }

            // кольцо — «территория» (честно скейлится зумом, как иконка планеты),
            // подпись — константный экранный размер (counter-scale, как маркеры M7.1)
            if (landedRingNote != null)
                landedRingNote.localScale = Vector3.one / Mathf.Max(zoom, 0.001f);

            if (landedRingBody != parkedOn) // новая стоянка — перерисовать кольцо по её радиусу
            {
                landedRingBody = parkedOn;
                landedRing.gameObject.SetActive(true);
                float d = parkedOn.Radius * 2f * scale + 9f; // честный диаметр + 8-10px зазор
                Image ring = landedRing.GetComponent<Image>();
                if (ring != null)
                    ring.rectTransform.sizeDelta = Vector2.one * d;
                if (landedRingNote != null)
                    landedRingNote.anchoredPosition = Vector2.down * (d * 0.5f + 12f);
            }
            landedRing.anchoredPosition = WorldToMap(parkedOn.transform.position);
        }

        private Transform ShipPosition()
        {
            if (ship == null)
                ship = FindAnyObjectByType<Ship>();
            BindHullDeath();
            return ship != null ? ship.transform : null;
        }

        // ориентация корпуса в мировых градусах (M8.5: поза посадки маркера) —
        // та же конвенция, что в MinimapBinder.HullHeadingDeg: поворот тела +
        // нос спрайта ShipController
        private float HullHeadingDeg()
        {
            if (ship == null)
                ship = FindAnyObjectByType<Ship>();
            if (ship == null || ship.Body == null)
                return playerHeading;
            ShipController controller = ship.GetComponent<ShipController>();
            float nose = controller != null ? controller.SpriteNoseAngle : 0f;
            return ship.Body.rotation + nose;
        }

        // взрыв на карте (Н6.K7 + M7.4): подписка на смерть корабля — НЕ ленивая:
        // вешается в OnEnable, поллится в Update и дублируется постоянным
        // DeathListener (карта-то неактивна, пока закрыта). -=+ += идемпотентны:
        // domain reload в пле стирает delegate-списки событий (не сериализуются),
        // ранний early-return после этого молча терял смерть (найдено приёмкой Н7)
        private void BindHullDeath()
        {
            if (hull == null)
                hull = FindAnyObjectByType<HullSystem>();
            if (hull == null)
                return;
            hull.Died -= OnShipDied;
            hull.Died += OnShipDied;
        }

        // точка гибели: корпус ещё на месте (телепорт респавна — через 0.6с).
        // Карта открыта — burst сразу; закрыта/не построена — burst доиграет при
        // открытии, клякса-крест «здесь я погиб» появляется в любом случае
        private void OnShipDied()
        {
            Vector2 world = hull != null && hull.transform != null
                ? (Vector2)hull.transform.position
                : ship != null ? (Vector2)ship.transform.position : worldCenter;
            AddDeathMark(world);
            if (!built || sheetRect == null || !gameObject.activeInHierarchy)
            {
                pendingDeathWorld = world;
                return;
            }
            SpawnDeathBurst(world);
        }

        // два расходящихся кольца + искры в точке гибели; позиция клампится как у
        // ракеты — за ViewInset точка рисуется у края вида, а не прячется
        private void SpawnDeathBurst(Vector2 world)
        {
            if (playerMarker == null || sheetRect == null)
                return;
            Vector2 center = SheetClampView(zoomOffset + zoom * WorldToMap(world));
            var burst = new MapBurst
            {
                Root = MakeRect("MapBurst", sheetRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero),
                World = world
            };
            burst.Root.anchoredPosition = center;
            burst.Root.SetAsLastSibling();

            burst.Ring = MakeImage("Ring", burst.Root, PaperMapArt.TargetRing(),
                WithAlpha(PaperMapArt.Alarm, 0.9f), Vector2.one * 14f, false).GetComponent<Image>();
            burst.RingLate = MakeImage("RingLate", burst.Root, PaperMapArt.TargetRing(),
                WithAlpha(PaperMapArt.Alarm, 0.75f), Vector2.one * 10f, false).GetComponent<Image>();
            burst.RingLate.gameObject.SetActive(false); // родится с задержкой RingLateDelay
            var rng = new System.Random(StableHash("burst") + (int)(Time.unscaledTime * 1000f));
            for (int i = 0; i < BurstSparks; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float speed = 70f + (float)rng.NextDouble() * 150f;
                Image spark = MakeImage("Spark_" + i, burst.Root,
                    i % 3 == 0 ? PaperMapArt.InkDot() : PaperMapArt.MapSprite("map_asteroid"),
                    WithAlpha(PaperMapArt.Alarm, 0.9f), Vector2.one * (5f + (float)rng.NextDouble() * 6f), false)
                    .GetComponent<Image>();
                burst.Sparks.Add(spark);
                burst.SparkVel.Add(new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * speed);
            }
            bursts.Add(burst);
        }

        // кадр жизни эффектов (M7.4): заметнее — 1.2с, кольцо до 180px, второе
        // кольцо с задержкой 0.2с, 18 искр; в конце — клякса-крест до закрытия карты
        private void UpdateMapEffects()
        {
            const float Duration = 1.2f;
            const float RingMaxPx = 180f;
            const float LateDelay = 0.2f;
            if (pendingDeathWorld.HasValue) // смерть при закрытой карте — burst при открытии
            {
                SpawnDeathBurst(pendingDeathWorld.Value);
                pendingDeathWorld = null;
            }
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
                if (burst.RingLate != null)
                {
                    bool lateOn = burst.Age >= LateDelay;
                    if (burst.RingLate.gameObject.activeSelf != lateOn)
                        burst.RingLate.gameObject.SetActive(lateOn);
                    if (lateOn)
                    {
                        float k2 = Mathf.Clamp01((burst.Age - LateDelay) / Duration);
                        burst.RingLate.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(10f, RingMaxPx * 0.7f, Mathf.SmoothStep(0f, 1f, k2));
                        var c = burst.RingLate.color;
                        c.a = 0.75f * (1f - k2);
                        burst.RingLate.color = c;
                    }
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
            SyncDeathMarks(); // кляксы живы до закрытия карты, переживают пересборку
        }

        // ————— кляксы «здесь я погиб» (M7.4) —————

        private void AddDeathMark(Vector2 world)
        {
            deathMarkWorlds.Add(world);
            deathMarkRects.Add(null);
            while (deathMarkWorlds.Count > MaxDeathMarks)
            {
                deathMarkWorlds.RemoveAt(0);
                if (deathMarkRects[0] != null)
                    Destroy(deathMarkRects[0].gameObject);
                deathMarkRects.RemoveAt(0);
            }
        }

        // чернильная клякса + крест в точке гибели (Alarm, до закрытия карты);
        // позиция клампится как у ракеты и пересчитывается при пан/зуме
        private void SyncDeathMarks()
        {
            if (sheetRect == null)
                return;
            for (int i = 0; i < deathMarkWorlds.Count; i++)
            {
                if (i >= deathMarkRects.Count)
                    deathMarkRects.Add(null);
                RectTransform rect = deathMarkRects[i];
                if (rect == null) // пересборка карты стёрла — перерисовать
                    deathMarkRects[i] = rect = MakeDeathMark();
                rect.anchoredPosition = SheetClampView(zoomOffset + zoom * WorldToMap(deathMarkWorlds[i]));
            }
        }

        private RectTransform MakeDeathMark()
        {
            RectTransform root = MakeRect("DeathMark", sheetRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            root.SetAsLastSibling();
            var ink = WithAlpha(PaperMapArt.Alarm, 0.9f);
            MakeImage("Blot", root, PaperMapArt.InkDot(), WithAlpha(PaperMapArt.Alarm, 0.8f), Vector2.one * 26f, false);
            MakeSegment(root, new Vector2(-8f, -8f), new Vector2(8f, 8f), ink, 2f);
            MakeSegment(root, new Vector2(-8f, 8f), new Vector2(8f, -8f), ink, 2f);
            return root;
        }

        // кламп точки вида внутрь окна листа — как у маркера ракеты: точка за
        // ViewInset рисуется у края, «кламп не прячет событие»
        private Vector2 SheetClampView(Vector2 sheetPos)
        {
            Vector2 half = sheetRect.rect.size * 0.5f - new Vector2(ContentMargin, ContentMargin);
            return new Vector2(
                Mathf.Clamp(sheetPos.x, -half.x + ViewInset, half.x - ViewInset),
                Mathf.Clamp(sheetPos.y, -half.y + ViewInset, half.y - ViewInset));
        }

        // M7.4: постоянный слушатель смерти — сам байндер неактивен, пока карта
        // закрыта, и не словил бы смерть до первого открытия карты. Живёт в
        // DontDestroyOnLoad, самоуничтожается при смерти владельца (смена сцены);
        // переподписка идемпотентна — переживает domain reload в пле
        private sealed class DeathListener : MonoBehaviour
        {
            private MapScreenBinder owner;
            private HullSystem hull;

            public static void Ensure(MapScreenBinder binder)
            {
                DeathListener listener = FindAnyObjectByType<DeathListener>();
                if (listener == null)
                {
                    var go = new GameObject("MapDeathListener");
                    DontDestroyOnLoad(go);
                    listener = go.AddComponent<DeathListener>();
                }
                listener.owner = binder;
            }

            private void Update()
            {
                if (owner == null)
                {
                    Destroy(gameObject); // владелец умер (смена сцены) — слушатель не нужен
                    return;
                }
                if (hull == null)
                    hull = FindAnyObjectByType<HullSystem>();
                if (hull == null)
                    return;
                hull.Died -= owner.OnShipDied;
                hull.Died += owner.OnShipDied;
            }

            private void OnDestroy()
            {
                if (hull != null && owner != null)
                    hull.Died -= owner.OnShipDied; // симметричная отписка (конвенция проекта)
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
                // M8.1: арту кометы — родной цвет; процедурный фолбэк (белая
                // маска) тонируется чернилами, иначе не виден на бумаге
                bool hasArt = art != null;
                if (!hasArt)
                    art = PaperMapArt.CometIcon();
                Color cometColor = hasArt ? Color.white : WithAlpha(PaperMapArt.Ink, 0.62f);
                RectTransform icon = MakeImage("Comet_" + cometIcons.Count, cometsRoot, art,
                    cometColor, Vector2.one * 30f, false);
                Vector2 world = comet.transform.position;
                icon.anchoredPosition = WorldToMap(world);
                icon.localScale = Vector3.one * inv;
                cometIcons[comet] = icon;
                cometLastPos[comet] = world;
            }
        }

        private static Rect CenteredRect(Vector2 center, Vector2 size)
        {
            return new Rect(center - size * 0.5f, size);
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

        // ————— вид: зум колесом/кнопками + пан драгом —————

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
            float s = Mathf.Clamp(zoom * factor, minViewZoom, MaxViewZoom);
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
            float s = Mathf.Clamp(zoom * factor, minViewZoom, MaxViewZoom);
            if (Mathf.Abs(s - zoom) < 0.0001f)
                return;
            AnimateZoom(s, zoomOffset * (s / zoom));
        }

        // программный вид для тестов/оркестратора: зум и смещение контента
        public void SetView(float viewZoom, Vector2 sheetOffset)
        {
            if (!built || content == null)
                return;
            float s = Mathf.Clamp(viewZoom, minViewZoom, MaxViewZoom);
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
                if (image != null) // включённый режим — кнопка затемнена чернилами, текст бумагой
                    image.color = followMode ? WithAlpha(PaperMapArt.Ink, 0.42f) : PaperMapArt.PaperDark;
            }
            if (followLabel != null)
                followLabel.color = followMode ? PaperMapArt.Paper : PaperMapArt.Ink;
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
            return ContainsScreen(legendRoot, screenPos)
                || ContainsScreen(zoomRoot, screenPos);
        }

        private bool ContainsScreen(RectTransform rect, Vector2 screenPos)
        {
            return rect != null && rect.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, canvasCam);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || PressOnChrome(eventData))
                return;
            dragging = true;
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
            if (dragMoved > ClickDragGuard)
                suppressClick = true; // перетащили — это пан, не клик-выбор (M8.8)
            dragSheetPos = pos;
            // резиновый вынос за край; на отпускании лист плавно вернётся в границы мира
            ApplyZoom(zoom, ClampZoomOffset(zoomOffset + delta, zoom, RubberMargin));
        }

        // M8.8: клик-выбор. Нажатие сбрасывает «перетащили» — клик снова жив;
        // клик по телу ставит/снимает метку (ToggleWaypoint), правый — снимает
        public void OnPointerDown(PointerEventData eventData)
        {
            suppressClick = false;
            dragMoved = 0f;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (suppressClick || !built)
                return;
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                if (AnnotationPlacementMode)
                {
                    AnnotationPlacementMode = false; // правый клик отменяет режим размещения
                    return;
                }
                ClearWaypoint();
                return;
            }
            if (eventData.button == PointerEventData.InputButton.Left
                && !PressOnChrome(eventData)
                && TryScreenToSheet(eventData.position, out Vector2 sheetPos))
            {
                // режим из Map Style Editor: клик по листу ставит аннотацию в точку мира
                if (AnnotationPlacementMode)
                {
                    Vector2 contentPos = (sheetPos - zoomOffset) / Mathf.Max(zoom, 0.001f);
                    Vector2 world = contentPos / Mathf.Max(scale, 0.0001f) + worldCenter;
                    AddAnnotationAtWorld(world, PlacementKind);
                    AnnotationPlacementMode = false;
                    return;
                }
                if (PickBodyAt(eventData.position, out CelestialBodyData body))
                    ToggleWaypoint(body);
            }
        }

        // тело под курсором (минимальный вариант из истории Н5.18-Н6.8, без
        // записок/чипа/зум-при-клике): ближайшее тело, у которого точка мира
        // внутри честного радиуса (мелкие — в радиусе PickMinScreenPx экрана).
        // Пояса не кликабельны: метка ставится на точку, у пояса она бессмысленна
        private bool PickBodyAt(Vector2 screenPos, out CelestialBodyData picked)
        {
            picked = null;
            if (!TryScreenToSheet(screenPos, out Vector2 sheetPos))
                return false;
            Vector2 contentPos = (sheetPos - zoomOffset) / Mathf.Max(zoom, 0.001f);
            Vector2 world = contentPos / Mathf.Max(scale, 0.0001f) + worldCenter;
            float pickMinWorld = PickMinScreenPx / Mathf.Max(zoom * scale, 0.0001f);
            float best = float.MaxValue;
            foreach (CelestialBodyData body in level.Bodies)
            {
                if (body.Type == BodyType.AsteroidBelt)
                    continue;
                float hitR = Mathf.Max(GameRadiusOf(body), pickMinWorld);
                float d = Vector2.Distance(world, body.Position);
                if (d <= hitR && d < best)
                {
                    best = d;
                    picked = body;
                }
            }
            return picked != null;
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
                && ((legendRoot != null && pressed.IsChildOf(legendRoot.transform))
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

        // легенда (M7.5): компактный ключ в одном углу — нацарапан ПРЯМО на бумаге,
        // без подложки и рамки (Н6.K3). Значки — те же источники, что рисуют карту:
        // реальные спрайты построенных тел (M7.2), иначе типовой картографический арт;
        // строки «заправка/мастерская» сняты — услуги видны с реального арта планет
        private void BuildLegend()
        {
            legendRoot = MakeRect("Legend", sheetRect, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            legendRoot.pivot = new Vector2(0f, 0f);
            legendRoot.anchoredPosition = new Vector2(18f, 18f);
            legendRoot.sizeDelta = Style.legendSize;
            legendRoot.gameObject.SetActive(Style.legendVisible); // выключена — слоты подписей освобождаются

            int y = 0;
            // ХОТФИКС владельца: значки легенды = те же картографические арты, что
            // рисуют тела на карте (MapSpriteOf), — легенда больше не показывает
            // игровые спрайты планет
            LegendRow(0f, ref y, PaperMapArt.MapSprite("map_planet"), Color.white, "планета");
            LegendRow(0f, ref y, PaperMapArt.MapSprite("map_taxi_planet"), Color.white, "хаб МТО");
            LegendRow(0f, ref y, PaperMapArt.MapSprite("map_sun"), Color.white, "звезда");
            LegendRow(0f, ref y, PaperMapArt.MapSprite("map_black_hole"), Color.white, "чёрная дыра");
            LegendRow(0f, ref y, PaperMapArt.MapSprite("map_asteroid"), Color.white, "пояс");

            // маркеры: жёлтый — только цель заказа; зелёный — своя метка (M8.8)
            y = 0;
            LegendRow(175f, ref y, PaperMapArt.MapSprite("map_cometa"),
                Color.white, "комета");
            LegendRow(175f, ref y, PaperMapArt.TargetRing(), TargetRingColor, "цель заказа", 22f);
            LegendRow(175f, ref y, PaperMapArt.Pin(), WaypointMarkColor, "своя метка", 18f);
            Sprite rocket = PaperMapArt.MapSprite("map_rocket");
            LegendRow(175f, ref y, rocket, Color.white, "вы здесь", 20f);
        }

        private void LegendRow(float x, ref int row, Sprite icon, Color color, string text, float iconSize = 18f)
        {
            float y = 138f - row * 30f;
            RectTransform iconRect = MakeRect("I", legendRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            iconRect.sizeDelta = Vector2.one * 24f;
            iconRect.anchoredPosition = new Vector2(x + 12f, y);
            MakeImage("G", iconRect, icon, color, Vector2.one * iconSize, false);
            if (icon == null)
                MakeImage("F", iconRect, StubSprites.Circle(64f), WithAlpha(PaperMapArt.Ink, 0.7f), Vector2.one * 14f, false);

            TMP_Text rowLabel = MakeLabel("R", legendRoot, text, 16f, WithAlpha(PaperMapArt.Ink, 0.9f),
                TextAlignmentOptions.Left);
            ((RectTransform)rowLabel.transform).sizeDelta = new Vector2(130f, 22f);
            ((RectTransform)rowLabel.transform).anchorMin = new Vector2(0f, 0f);
            ((RectTransform)rowLabel.transform).anchorMax = new Vector2(0f, 0f);
            ((RectTransform)rowLabel.transform).pivot = new Vector2(0f, 0.5f);
            ((RectTransform)rowLabel.transform).anchoredPosition = new Vector2(x + 30f, y);
            row++;
        }

        private void BuildRuler()
        {
            // масштабная линейка (M7.5: тоньше, чернила, видна только на крупных
            // зумах — на отъезде она не нужна): круглый шаг мира, влезающий в 150 px
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
            rulerFade = rulerRoot.gameObject.AddComponent<CanvasGroup>();
            rulerFade.blocksRaycasts = false;
            rulerFade.interactable = false;
            var ink = WithAlpha(PaperMapArt.Ink, 0.62f);

            RectTransform line = MakeRect("Line", rulerRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
            line.sizeDelta = new Vector2(px, 2f);
            Image lineImage = line.gameObject.AddComponent<Image>();
            lineImage.color = ink;
            lineImage.raycastTarget = false;

            foreach (float x in new[] { -px * 0.5f, px * 0.5f })
            {
                RectTransform tick = MakeRect("Tick", rulerRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
                tick.anchoredPosition = new Vector2(x, 4f);
                tick.sizeDelta = new Vector2(2f, 8f);
                Image tickImage = tick.gameObject.AddComponent<Image>();
                tickImage.color = ink;
                tickImage.raycastTarget = false;
            }

            TMP_Text label = MakeLabel("T", rulerRoot, step + " юн", 14f, WithAlpha(PaperMapArt.Ink, 0.62f));
            ((RectTransform)label.transform).sizeDelta = new Vector2(px + 60f, 20f);
            ((RectTransform)label.transform).anchoredPosition = new Vector2(0f, 14f);
        }

        // M7.5: линейка привязана к зуму — плавный фейд ~0.12с, на отъезде выключена
        private void SyncRuler()
        {
            if (rulerRoot == null || rulerFade == null)
                return;
            float target = zoom / MaxViewZoom >= RulerMinZoomFraction ? 1f : 0f;
            float next = Mathf.MoveTowards(rulerFade.alpha, target, Time.unscaledDeltaTime / LabelFadeSeconds);
            if (next != rulerFade.alpha)
            {
                rulerFade.alpha = next;
                bool active = next > 0.001f;
                if (rulerRoot.gameObject.activeSelf != active)
                    rulerRoot.gameObject.SetActive(active);
            }
        }

        // M7.5: районы — имена областей читаются на обзоре, на приближении гаснут
        private void SyncSectorFade()
        {
            if (sectorRoot == null || sectorFade == null)
                return;
            float contentScale = zoom / MaxViewZoom;
            float target = Mathf.Clamp01((SectorFadeOutFraction - contentScale) / 0.20f);
            float next = Mathf.MoveTowards(sectorFade.alpha, target, Time.unscaledDeltaTime / LabelFadeSeconds);
            if (next != sectorFade.alpha)
            {
                sectorFade.alpha = next;
                bool active = next > 0.001f;
                if (sectorRoot.gameObject.activeSelf != active)
                    sectorRoot.gameObject.SetActive(active);
            }

            // районы в контент-пространстве: кегль — константный экранный
            // (контентный = Style.districtFontSize / zoom), как у подписей тел
            float districtSize = Style.districtFontSize / Mathf.Max(zoom, 0.001f);
            foreach (TMP_Text text in sectorTexts)
                if (text != null && !Mathf.Approximately(text.fontSize, districtSize))
                    text.fontSize = districtSize;
        }

        // кнопки зума: +/− к центру листа, «сброс» — максимальный отъезд
        // (Н6.HF1: вид «весь мир» больше не существует), «следить» — фиксация
        // камеры на ракете (Н5.21-7: крупнее; Н6.K6: кнопка следования)
        private void BuildZoomButtons()
        {
            zoomRoot = MakeRect("ZoomButtons", sheetRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
            zoomRoot.pivot = new Vector2(0.5f, 0f);
            zoomRoot.anchoredPosition = new Vector2(0f, 16f);
            zoomRoot.sizeDelta = new Vector2(480f, 64f);
            zoomRoot.localScale = Vector3.one * Style.zoomButtonScale; // ручка размера кнопок зума

            MakePaperButton("BtnZoomIn", zoomRoot, new Vector2(52f, 48f), new Vector2(-168f, 8f), "+", 26f,
                () => ZoomStep(ButtonZoomStep), 701);
            MakePaperButton("BtnZoomOut", zoomRoot, new Vector2(52f, 48f), new Vector2(-104f, 8f), "-", 26f,
                () => ZoomStep(1f / ButtonZoomStep), 702);
            MakePaperButton("BtnZoomReset", zoomRoot, new Vector2(124f, 48f), new Vector2(-10f, 8f), "сброс", 20f,
                () => AnimateZoom(minViewZoom, Vector2.zero), 703); // Н6.HF1: сброс = максимальный отъезд, не «весь мир»
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
            // экранное поверх листа: заголовок, легенда, линейка, кнопки
            if (legendRoot != null) legendRoot.SetAsLastSibling();
            if (titleRoot != null) titleRoot.SetAsLastSibling();
            if (rulerRoot != null) rulerRoot.SetAsLastSibling();
            if (zoomRoot != null) zoomRoot.SetAsLastSibling();
        }

        private void RebuildAll()
        {
            ClearChildren(transform);
            sheetRect = null;
            viewport = null;
            paperRect = null;
            content = null;
            sectorRoot = null;
            sectorFade = null;
            sectorTexts.Clear();
            edgeFogRoot = null;
            edgeRoot = null;
            beltsRoot = null;
            bodiesRoot = null;
            labelsRoot = null;
            cometsRoot = null;
            legendRoot = null;
            rulerRoot = null;
            zoomRoot = null;
            titleRoot = null;
            bodySizes.Clear();
            bodyLabels.Clear();
            labelLeaders.Clear();
            labelTierState = -1;
            labelFades.Clear(); // M7.5: фейд-состояния пересборки не переживают
            rulerFade = null;
            cometIcons.Clear();
            cometLastPos.Clear();
            playerMarker = null;
            playerArrowRoot = null;
            landedPad = null;
            landedPadImage = null;
            landedRing = null;
            landedRingNote = null;
            landedRingBody = null;
            deathMarkRects.Clear(); // мировые точки гибелей живут — перерисуются на новом листе
            targetRing = null;
            targetRingPulse = null;
            targetRingWide = null;
            targetRingPulseImage = null;
            targetRingWideImage = null;
            targetLabel = null;
            annotationsRoot = null;
            annotationNodes.Clear();
            waypointRoot = null;
            waypointHalo = null;
            waypointWide = null;
            waypointPin = null;
            waypointHaloImage = null;
            waypointWideImage = null;
            waypointPinImage = null;
            targetIndex = -1;
            lastTargetBody = null;
            followButton = null;
            followLabel = null;
            followMode = false;
            bursts.Clear();
            dragging = false;
            suppressClick = false;
            dragMoved = 0f;
            zoomAnim = false;
            zoom = 1f;
            zoomOffset = Vector2.zero;
            BuildChrome();
            BuildMap();
            built = true;
            builtWidth = mapRect.rect.width;
            RestackChrome();
            FocusOnPlayer(); // после пересборки — вид к игроку (гвард built уже снят)
            SyncLabelVisibility(); // тиры подписей по восстановленному зуму, без кадра-полукомплекта
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
