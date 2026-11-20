using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using SpaceTaxi.Core;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;
using SpaceTaxi.UI.PaperMap;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Карта уровня: лист, «нарисованный от руки» на старой бумаге. Строит иконки
    // тел, подписи, метки; зум, пан, клик-выбор тела (метка игрока). Наружу:
    // Waypoint + WaypointChanged, Set/Clear/ToggleWaypoint, SetView, TryHandleEscape.
    public class MapScreenBinder : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerDownHandler, IPointerClickHandler
    {
        [SerializeField] private TMP_FontAsset mapFont; // Excalifont-Regular SDF — назначен в сцене
        [SerializeField] private MapStyleConfig style;  // стиль карты; null → Resources

        // — свои надписи/контуры (Map Style Editor): режим клика по карте —
        public static bool AnnotationPlacementMode;     // левый клик ставит аннотацию, правый отменяет
        public static AnnotationKind PlacementKind = AnnotationKind.Label;

        // стиль карты: назначенный в сцене → Resources/MapStyle_Default → временные
        // дефолты. Кэшируется; правится окном Space Taxi → Map Style Editor
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

        // минимум — различимая точка (4 px контента ≈ 9 px экрана на минимальном зуме)
        private const float MinIconPx = 4f;

        private const float PickMinScreenPx = 14f; // мелкие тела кликаются в радиусе 14 экранных px

        // метка игрока для WaypointIndicator/MinimapBinder: ставится кликом по телу,
        // снимается повторным/правым кликом или Esc
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

        // Esc: при открытой карте сначала снимает метку игрока (карта остаётся),
        // без метки — закрывает карту (контракт с MapScreenController: true — Esc съеден)
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

        // карта построена и сейчас на экране
        public bool IsBuilt => built;
        internal LevelAsset Level => level; // подсистемам нужен уровень
        internal RectTransform MapRect => mapRect; // хром строит лист от корня карты
        internal TMP_FontAsset Font => font; // шрифт карты для подсистем
        private float builtWidth;
        private Camera canvasCam; // null для Screen Space Overlay — так и нужно утилитам

        private RectTransform beltsRoot;   // камни поясов астероидов
        private RectTransform bodiesRoot;  // иконки тел

        private bool relayoutPending;              // экран изменился — пересчитать лист

        // построенные тела уровня (ключ — DisplayName, совпадает с именем
        // GameObject): источник честного радиуса (Radius уже с PlanetSizeMul)
        // и игрового спрайта
        private readonly Dictionary<string, CelestialBody> gameBodies = new Dictionary<string, CelestialBody>();

        // подсистемы бумажной карты (Assets/Scripts/UI/PaperMap) — создаются в
        // Awake (CreateSubsystems); хост передаёт себя как контекст
        private PaperMapProjection view;
        private PaperMapChrome chrome; // хром листа (стол/бумага/легенда/линейка/кнопки/районы)
        private PaperMapLabels labels; // подписи тел (слоты/выноски/фейды)
        private PaperMapRings rings;   // кольца цели/метки, маркер ракеты, край мира, кометы
        private PaperMapAnnotations annotations; // слой аннотаций + эффекты смерти

        // шимы к подсистемам: зум/пан живёт в PaperMapProjection, лист/хром — в PaperMapChrome
        private float zoom => view.Zoom;
        private Vector2 zoomOffset => view.ZoomOffset;
        private float scale => view.Scale;
        private Vector2 worldCenter => view.WorldCenter;
        private bool suppressClick => view.SuppressClick;
        private RectTransform content => view.Content;     // зум-контейнер строит PaperMapChrome
        private Vector2 WorldToMap(Vector2 world) => view.WorldToMap(world); // иконки тел на карте
        private float playerHeading => rings.PlayerHeading;      // последний курс маркера
        private bool TryScreenToSheet(Vector2 screenPos, out Vector2 sheetLocal)
            => view.TryScreenToSheet(screenPos, out sheetLocal);
        private bool PressOnChrome(PointerEventData eventData)
            => chrome.PressedOnChrome(eventData); // хром-тест клика — PaperMapChrome

        // анимации карты: взрыв — кольцо+искры; стоянка — тень-подставка
        private HullSystem hull;

        private void Awake()
        {
            mapRect = (RectTransform)transform;
            Canvas canvas = GetComponentInParent<Canvas>();
            canvasCam = canvas != null ? canvas.worldCamera : null;

            // корень карты — на весь канвас (отступы под борткомп считает LayoutSheet)
            mapRect.anchorMin = Vector2.zero;
            mapRect.anchorMax = Vector2.one;
            mapRect.offsetMin = Vector2.zero;
            mapRect.offsetMax = Vector2.zero;

            // заготовку из сцены стираем целиком — лист рисуем кодом
            ClearChildren(transform);
            font = ResolveFont();
            CreateSubsystems(); // подсистемы бумажной карты
            view.CanvasCam = canvasCam; // инъекция: камера канваса (null = Screen Space Overlay)
            chrome.BuildChrome();
            DeathListener.Ensure(this);
        }

        // подсистемы бумажной карты (namespace SpaceTaxi.UI.PaperMap);
        // хост передаёт себя как контекст и связи соседей
        private void CreateSubsystems()
        {
            view = new PaperMapProjection(this);
            chrome = new PaperMapChrome(this, view);
            labels = new PaperMapLabels(this, view, chrome);
            rings = new PaperMapRings(this, view, labels);
            annotations = new PaperMapAnnotations(this, view, rings);
            view.SetPointerOverChrome(chrome.PointerOverChrome);
        }

        private void OnEnable()
        {
            BindHullDeath(); // подписка не ленивая — смерть до первого Update тоже ловится
            view.FocusOnPlayer(); // повторные открытия — карта сразу приближена к игроку
        }

        // шрифт: сцена (Excalifont) → ресурсы Excalifont → HUD → дефолт TMP;
        // все тексты карты идут через MakeLabel
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

            if (chrome.SyncOnboardPanel())
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
                chrome.RestackChrome();
                view.FocusOnPlayer(); // первый вход — сразу приближено к игроку
                labels.SyncLabelVisibility(); // FocusOnPlayer поднял зум — тиры подписей сразу по нему
                return;
            }

            if (relayoutPending)
            {
                relayoutPending = false;
                chrome.LayoutSheet();
                RebuildAll();
                return;
            }

            if (Mathf.Abs(mapRect.rect.width - builtWidth) > 2f)
            {
                RebuildAll();
                return;
            }

            view.UpdateZoomAnim();
            view.HandleWheel();
            labels.SyncLabelVisibility(); // decluttering: переразмещение подписей при смене тира зума
            labels.StepLabelFades();      // альфа-фейд на смене тира
            chrome.SyncRuler();           // линейка — только на крупных зумах
            chrome.SyncSectorFade();      // районы гаснут на приближении
            view.UpdateFollow();
            rings.UpdatePlayerMarker();
            rings.UpdateLandedRing();    // кольцо стоянки вокруг планеты
            rings.UpdateTargetRing();
            rings.UpdateWaypointVisual();
            rings.UpdateComets();
            annotations.UpdateAnnotations();
            annotations.UpdateMapEffects();
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

        private void OnDisable()
        {
            // карта закрылась — вид и кляксы гибелей сбрасываем, следующий вход
            // с чистого экрана
            view.OnMapDisabled(); // пан/зум — подсистема вида
            relayoutPending = false;
            annotations.OnMapDisabled(); // кляксы гибелей — подсистема аннотаций
        }

        // ————— содержимое листа —————

        private void BuildMap()
        {
            chrome.LayoutSheet(); // размеры окна уже известны — лист актуален

            // реальные тела уровня — радиус (уже с PlanetSizeMul) и игровой спрайт;
            // имя тела = имя GameObject (WorldBuilder). Заполняется до bbox —
            // честный fit считает игровые радиусы
            gameBodies.Clear();
            GravityField gravity = FindAnyObjectByType<GravityField>();
            if (gravity != null)
                foreach (CelestialBody gameBody in gravity.Bodies)
                    if (gameBody != null && !string.IsNullOrEmpty(gameBody.DisplayName))
                        gameBodies[gameBody.DisplayName] = gameBody;

            view.ComputeWorldMetrics(); // граница мира/bbox/масштаб/порог отдаления — подсистема вида
            chrome.ApplyPaperSize(); // бумага от мира (PaperMapChrome)

            // слои снизу вверх: районы → край мира → пояса → подписи → тела →
            // кометы → метка/цель. Иконки и подписи — чистая графика
            // (raycastTarget=false); слой подписей ниже иконок — подпись
            // читается поверх чужих, но не накрывает тела
            chrome.BuildSectorLayer(content);
            // туманная кромка — отдельный слой вне edgeRoot: дети edgeRoot
            // блокируют слоты подписей, а кромка накрывает весь лист
            rings.BuildEdgeLayers(content);
            beltsRoot = MakeRect("Belts", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            labels.CreateLabelsRoot(content);
            bodiesRoot = MakeRect("Bodies", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // чернильная клякса в origin мира (отметка центра, не линия)
            MakeImage("WorldCenter", content, PaperMapArt.InkDot(),
                WithAlpha(PaperMapArt.Ink, 0.5f), Vector2.one * 7f, false);

            chrome.BuildSectorLabels();
            rings.BuildCometZone();
            rings.BuildWorldEdge();
            chrome.BuildLegend(); // компактный ключ с честными значками — до PlaceAllLabels (блокирует слоты)
            BuildAnnotations(); // свои надписи/контуры (Map Style Editor) — поверх подписей
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type == BodyType.AsteroidBelt)
                    CreateBelt(body);
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type != BodyType.AsteroidBelt)
                    CreateBody(body);
            labels.BindEdgeRoot(rings.EdgeRoot); // слоты читают край мира (буи/«потеря связи»)
            labels.PlaceAllLabels(); // подписи блоками «иконка+имя» — после всех иконок

            // кометы под ореолами цели/метки: ореол не должен прятаться под пролетевшей кометой
            rings.CreateCometsLayer(content);

            rings.BuildWaypoint();
            rings.BuildTargetRing();
            rings.BuildPlayerMarker();

            // первый фокус на игроке — после built=true
            chrome.BuildRuler(); // линейка зависит от scale — известен только здесь
            chrome.SyncTitle();
        }

        // ————— пояса астероидов: камни map_asteroid по кольцу —————

        private void CreateBelt(CelestialBodyData body)
        {
            float diameter = Mathf.Max(12f, body.Radius * 2f * scale);
            Vector2 pos = WorldToMap(body.Position);
            labels.SetIconSize(body, diameter);

            // камни: один спрайт map_asteroid, детерминированный разброс по кольцу;
            // в игре камни летят в Radius ± BeltWidth/2 (AsteroidBeltSystem.Init) —
            // ширина кольца честная
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
                    // 2-8px степенным распределением — текстура полосы, а не галька
                    float size = 2f + Mathf.Pow((float)rng.NextDouble(), 2.2f) * 6f;
                    // камни — родный цвет арта
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
            => PaperMapUi.StableHash(text); // реализация в PaperMapUi

        // ————— тела: реальный игровой облик, честный размер —————

        private void CreateBody(CelestialBodyData body)
        {
            // диаметр px = 2 × игровой радиус × scale: радиус берём у построенного
            // тела (CelestialBody.Init умножил data.Radius на PlanetSizeMul/
            // VisualSizeMul) — размер тела на карте совпадает с игровым
            float d = Mathf.Max(MinIconPx, GameRadiusOf(body) * 2f * scale);
            // иконка дыры ×2 — без отдельного ореола
            if (body.Type == BodyType.BlackHole)
                d *= 2f;
            // картографические спрайты Art/map (услуги читаются с иконки), не
            // игровые арты планет; честный размер d не меняется
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

            // иконка — чистая графика: без рейкаста и кликов; цвет — родный цвет арта
            MakeImage("Icon", group, art, Color.white, size, false);
            labels.SetIconSize(body, d);
            // подпись строится в PlaceAllLabels — блоками «иконка+имя» по приоритету
        }

        // игровой радиус тела: у построенного тела Radius уже с множителем;
        // мир ещё не построен (карта открыта раньше) — зеркало логики Init по данным
        internal float GameRadiusOf(CelestialBodyData body) // читает подсистема вида
        {
            if (gameBodies.TryGetValue(body.DisplayName, out CelestialBody gameBody) && gameBody != null)
                return gameBody.Radius;
            float mul = body.Type == BodyType.Planet
                ? (body.VisualSizeMul > 0f ? body.VisualSizeMul : WorldSprites.PlanetSizeMul(body.DisplayName))
                : 1f;
            return body.Radius * mul;
        }

        // спрайт иконки — картографический (Art/map: map_planet/map_taxi_planet/
        // map_gas_station/map_repair/map_sun/map_black_hole; услуги читаются
        // с иконки); нет арта — игровой спрайт построенного тела
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

        private const float LabelFadeSeconds = PaperMapUi.LabelFadeSeconds; // общий темп фейдов — PaperMapUi

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

        // ————— свои надписи и контуры (Map Style Editor) —————

        private void BuildAnnotations()
            => annotations.BuildAnnotations(); // слой аннотаций — подсистема

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
        internal void PersistStyle() // пишет правки стиля в ассет
        {
#if UNITY_EDITOR
            if (style != null && UnityEditor.AssetDatabase.Contains(style))
            {
                UnityEditor.EditorUtility.SetDirty(style);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(style);
            }
#endif
        }

        // тело текущего шага активного заказа: пассажир до посадки, цель — с пассажиром
        internal int ActiveOrderTargetIndex() // читают подписи и кольца цели
        {
            var orders = session != null ? session.Orders : null;
            var active = orders != null ? orders.Active : null;
            return active == null
                ? -1
                : active.State == OrderState.Aboard ? active.DropoffIndex : active.PickupIndex;
        }

        internal Ship Ship => ship; // состояние корабля для маркера (ленивый поиск — в ShipPosition)
        internal HullSystem Hull => hull; // подсистеме смерти нужен hull

        internal Transform ShipPosition() // читает подсистема колец
        {
            if (ship == null)
                ship = FindAnyObjectByType<Ship>();
            BindHullDeath();
            return ship != null ? ship.transform : null;
        }

        // ориентация корпуса в мировых градусах — та же конвенция, что в
        // MinimapBinder.HullHeadingDeg: поворот тела + нос спрайта ShipController
        internal float HullHeadingDeg() // читает подсистема колец
        {
            if (ship == null)
                ship = FindAnyObjectByType<Ship>();
            if (ship == null || ship.Body == null)
                return playerHeading;
            ShipController controller = ship.GetComponent<ShipController>();
            float nose = controller != null ? controller.SpriteNoseAngle : 0f;
            return ship.Body.rotation + nose;
        }

        // подписка на смерть корабля не ленивая: OnEnable + полл в Update +
        // постоянный DeathListener (карта неактивна, пока закрыта). Переподписка
        // -=/+= идемпотентна — переживает domain reload
        private void BindHullDeath()
        {
            if (hull == null)
                hull = FindAnyObjectByType<HullSystem>();
            if (hull == null)
                return;
            hull.Died -= OnShipDied;
            hull.Died += OnShipDied;
        }

        private void OnShipDied()
            => annotations.OnShipDied(); // эффекты смерти — подсистема аннотаций

        // постоянный слушатель смерти: байндер неактивен, пока карта закрыта.
        // Живёт в DontDestroyOnLoad, самоуничтожается при смерти хоста (смена
        // сцены); переподписка идемпотентна
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
                    Destroy(gameObject); // хост уничтожен (смена сцены) — слушатель не нужен
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

        internal Vector2 PlayerWorldPosition() // читает подсистема вида
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

        // программный вид для тестов/оркестратора: зум и смещение контента
        public void SetView(float viewZoom, Vector2 sheetOffset)
            => view.SetView(viewZoom, sheetOffset);

        public void OnBeginDrag(PointerEventData eventData)
            => view.BeginDrag(eventData, PressOnChrome); // пан — подсистема вида

        public void OnDrag(PointerEventData eventData)
            => view.Drag(eventData); // пан — подсистема вида

        // клик по телу ставит/снимает метку (ToggleWaypoint), правный клик — снимает
        public void OnPointerDown(PointerEventData eventData)
            => view.PressReset(); // пан-состояние — подсистема вида

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

        // тело под курсором: ближайшее, у которого точка мира внутри честного
        // радиуса (мелкие — в радиусе PickMinScreenPx экрана). Пояса не кликабельны
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
            => view.EndDrag(eventData); // пан — подсистема вида

        private void RebuildAll()
        {
            ClearChildren(transform);
            chrome.Reset(); // хром — лист/бумага/секторы/легенда/линейка/кнопки
            beltsRoot = null;
            bodiesRoot = null;
            labels.Reset(); // подписи/размеры иконок — подсистема
            rings.Reset(); // кольца/маркеры/край мира/кометы — подсистема
            annotations.Reset(); // слой аннотаций + эффекты смерти — подсистема
            view.Reset(); // пан/зум/следование — подсистема вида
            chrome.BuildChrome();
            BuildMap();
            built = true;
            builtWidth = mapRect.rect.width;
            chrome.RestackChrome();
            view.FocusOnPlayer(); // после пересборки — вид к игроку (гвард built уже снят)
            labels.SyncLabelVisibility(); // тиры подписей по восстановленному зуму, без кадра-полукомплекта
        }

        // ————— хелперы —————

        private static void ClearChildren(Transform t)
            => PaperMapUi.ClearChildren(t); // реализация в PaperMapUi

        private static RectTransform MakeRect(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
            => PaperMapUi.MakeRect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax); // PaperMapUi

        private static RectTransform MakeImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size, bool raycast)
            => PaperMapUi.MakeImage(name, parent, sprite, color, size, raycast); // PaperMapUi

        private static Color WithAlpha(Color color, float alpha)
            => PaperMapUi.WithAlpha(color, alpha); // реализация в PaperMapUi

    }
}
