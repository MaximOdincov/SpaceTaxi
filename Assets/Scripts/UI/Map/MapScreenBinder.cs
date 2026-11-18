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

        // M7.2: относительная нормировка (RefIconPx/maxBodyRadius) убрана — иконка
        // = честный игровой диаметр 2 × Radius_game × scale; минимум — различимая
        // точка (4 px контента ≈ 9 px экрана на минимальном зуме; при 6 px
        // «минимальные» планеты уровня раздувались до 1.8× и отношение
        // «px карты / радиус мира» переставало быть единым)
        private const float MinIconPx = 4f;

        // M8.8: клик-выбор — различение клика от пана (минимальная версия из
        // истории Н5.18-Н6.8) + минимальный радиус попадания в тело
        private const float PickMinScreenPx = 14f; // мелкие тела кликаются в радиусе 14 экранных px

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
        internal LevelAsset Level => level; // Н11: подсистемам нужен уровень
        internal RectTransform MapRect => mapRect; // Н11: хром строит лист от корня карты
        internal TMP_FontAsset Font => font; // Н11: шрифт карты для подсистем
        private float builtWidth;
        private Camera canvasCam; // null для Screen Space Overlay — так и нужно утилитам

        private RectTransform beltsRoot;   // камни поясов астероидов
        private RectTransform bodiesRoot;  // иконки тел

        private bool relayoutPending;              // экран изменился — пересчитать лист

        // M7.2: построенные тела уровня (ключ — DisplayName, совпадает с именем
        // GameObject): источник честного радиуса (Radius уже с PlanetSizeMul)
        // и реального игрового спрайта
        private readonly Dictionary<string, CelestialBody> gameBodies = new Dictionary<string, CelestialBody>();

        // — Н11: подсистемы бумажной карты (Assets/Scripts/UI/PaperMap, та же сборка) —
        // создаются в Awake (CreateSubsystems); хост передаёт себя как контекст
        private PaperMapProjection view;
        private PaperMapChrome chrome; // Н11: хром листа (стол/бумага/легенда/линейка/кнопки/районы)
        private PaperMapLabels labels; // Н11: подписи тел (слоты/выноски/фейды)
        private PaperMapRings rings;   // Н11: кольца цели/метки, маркер ракеты, край мира, кометы
        private PaperMapAnnotations annotations; // Н11: слой аннотаций + эффекты смерти

        // — шимы к подсистемам вида/хрома: тела методов хоста остаются дословными,
        // состояние зума/пана живёт в PaperMapProjection, лист/хром — в PaperMapChrome —
        private float zoom => view.Zoom;
        private Vector2 zoomOffset => view.ZoomOffset;
        private float scale => view.Scale;
        private Vector2 worldCenter => view.WorldCenter;
        private bool suppressClick => view.SuppressClick;
        private RectTransform content => view.Content;     // Н11: зум-контейнер строит PaperMapChrome
        private Vector2 WorldToMap(Vector2 world) => view.WorldToMap(world); // Н11: иконки тел на карте
        private float playerHeading => rings.PlayerHeading;      // Н11: последний курс маркера
        private bool TryScreenToSheet(Vector2 screenPos, out Vector2 sheetLocal)
            => view.TryScreenToSheet(screenPos, out sheetLocal);
        private bool PressOnChrome(PointerEventData eventData)
            => chrome.PressedOnChrome(eventData); // Н11: хром-тест клика — PaperMapChrome

        // анимации карты (Н6.K7): взрыв — кольцо+искры; стоянка — тень-подставка
        private HullSystem hull;

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
            CreateSubsystems(); // Н11: подсистемы бумажной карты
            view.CanvasCam = canvasCam; // инъекция: камера канваса (null = Screen Space Overlay)
            chrome.BuildChrome();
            DeathListener.Ensure(this);
        }

        // Н11: подсистемы бумажной карты — та же сборка (без asmdef), namespace
        // SpaceTaxi.UI.PaperMap; хост передаёт себя как контекст и связи соседей
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
            BindHullDeath(); // M7.4: подписка не ленивая — смерть до первого Update тоже ловится
            view.FocusOnPlayer(); // повторные открытия — карта сразу приближена к игроку (Н5.21-8)
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
                view.FocusOnPlayer(); // Н5.21-8: первый вход — сразу приближено к игроку
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
            labels.StepLabelFades();      // M7.5: альфа-фейд на смене тира вместо мгновенного popping
            chrome.SyncRuler();           // M7.5: линейка — только на крупных зумах
            chrome.SyncSectorFade();      // M7.5: районы гаснут на приближении (скайрим-поведение)
            view.UpdateFollow();
            rings.UpdatePlayerMarker();
            rings.UpdateLandedRing();    // M7.3: кольцо стоянки вокруг планеты
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
            // карта закрылась — вид сбрасываем, следующий вход с чистого экрана;
            // кляксы гибелей и отложенный burst живут «до закрытия карты» (M7.4)
            view.OnMapDisabled(); // Н11: пан/зум — состояние подсистемы вида
            relayoutPending = false;
            annotations.OnMapDisabled(); // Н11: кляксы гибелей — подсистема аннотаций
        }

        // ————— каркас: стол, лист, бумага, экранное (вне зум-контейнера) —————

        // ————— содержимое листа —————

        private void BuildMap()
        {
            chrome.LayoutSheet(); // размеры окна уже известны — лист актуален

            // M7.2: реальные тела уровня — радиус (уже с PlanetSizeMul) и игровой
            // спрайт; имя тела = имя GameObject (WorldBuilder). Заполняется ДО
            // bbox: честный fit считает игровые радиусы (M8.3)
            gameBodies.Clear();
            GravityField gravity = FindAnyObjectByType<GravityField>();
            if (gravity != null)
                foreach (CelestialBody gameBody in gravity.Bodies)
                    if (gameBody != null && !string.IsNullOrEmpty(gameBody.DisplayName))
                        gameBodies[gameBody.DisplayName] = gameBody;

            view.ComputeWorldMetrics(); // Н11: граница мира/bbox/масштаб/порог отдаления — подсистема вида
            chrome.ApplyPaperSize(); // Н11: M8.3 — бумага от мира (PaperMapChrome)

            // рамка листа снята (владелец Н9.2: «убери бежевую рамку по краю»)

            // слои снизу вверх: районы → край мира → пояса → подписи → тела →
            // кометы → метка/цель (игрок — на листе, выше всех). Нарисованные
            // линии (рамки районов, орбиты, кольца дистанций, сетка, пунктирные
            // пути) убраны совсем — Н5.21-5. Н6.HF4: кликов по телам больше нет,
            // иконки и подписи — чистая графика (raycastTarget=false); подписи
            // живут при своих иконках (PlaceAllLabels), слой ниже иконок — чтобы
            // «отошедшая» подпись читалась поверх чужих, но не накрывала тела
            chrome.BuildSectorLayer(content);
            // M7.5: туман/обод — отдельный слой ВНЕ edgeRoot: CollectStaticChrome
            // собирает детей edgeRoot как блокирующие rects слотов подписей, а
            // кромка накрывает весь лист (2222×2222 px) — каждый слот «столкался»
            // с туманом и все подписи уходили в дальний слот с выноской-«палкой»
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
            chrome.BuildLegend(); // M7.5: компактный ключ с честными значками — до PlaceAllLabels (блокирует слоты)
            BuildAnnotations(); // свои надписи/контуры владельца (Map Style Editor) — поверх подписей
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type == BodyType.AsteroidBelt)
                    CreateBelt(body);
            foreach (CelestialBodyData body in level.Bodies)
                if (body.Type != BodyType.AsteroidBelt)
                    CreateBody(body);
            labels.BindEdgeRoot(rings.EdgeRoot); // Н11: слоты читают край мира (буи/«потеря связи»)
            labels.PlaceAllLabels(); // Н6.HF4: подписи блоками «иконка+имя» — после всех иконок

            // кометы под ореолами цели/метки: ореол не должен прятаться под пролетевшей кометой
            rings.CreateCometsLayer(content);

            rings.BuildWaypoint();
            rings.BuildTargetRing();
            rings.BuildPlayerMarker();

            // Н5.21-8 → Н6-fix: FocusOnPlayer вызывается ПОСЛЕ built=true (в Update —
            // внутри BuildMap гвард !built молча съедал приближение к игроку)
            chrome.BuildRuler(); // линейка зависит от scale — известен только здесь
            chrome.SyncTitle();
        }

        // ————— кометная зона / край мира —————

        // ————— пояса астероидов: камни map_asteroid по кольцу —————

        private void CreateBelt(CelestialBodyData body)
        {
            float diameter = Mathf.Max(12f, body.Radius * 2f * scale);
            Vector2 pos = WorldToMap(body.Position);
            labels.SetIconSize(body, diameter);

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
                    // ХОТФИКС владельца №2: камни пояса МЕЛЬЧЕ — «в реальности раз в
                    // 10 меньше» (прежде 6-28px); теперь 2-8px степенным распределением —
                    // текстура полосы, не галька
                    float size = 2f + Mathf.Pow((float)rng.NextDouble(), 2.2f) * 6f;
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
            => PaperMapUi.StableHash(text); // Н11: реализация в PaperMapUi

        // ————— тела: реальный игровой облик, честный размер (M7.2) —————

        private void CreateBody(CelestialBodyData body)
        {
            // диаметр px = 2 × игровой радиус × scale: игровой радиус берём у
            // построенного тела (CelestialBody.Init умножил data.Radius на
            // PlanetSizeMul/VisualSizeMul — жалоба «по карте влетишь, а в игре
            // пролетаешь мимо»). Относительная нормировка maxBodyRadius убрана:
            // отношение «px карты / радиус мира» одно для всех тел.
            float d = Mathf.Max(MinIconPx, GameRadiusOf(body) * 2f * scale);
            // v5.1 (владелец: «на карте просто увеличь иконку черной дыры, а не
            // рисуй ореол»): иконка дыры ×2 — фиолетовая зона-ореол убрана
            if (body.Type == BodyType.BlackHole)
                d *= 2f;
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
            labels.SetIconSize(body, d);
            // подпись строится в PlaceAllLabels — блоками «иконка+имя» по приоритету
        }

        // игровой радиус тела: у построенного тела Radius уже с множителем;
        // мир ещё не построен (карта открыта раньше) — зеркало логики Init по данным
        internal float GameRadiusOf(CelestialBodyData body) // Н11: читает подсистема вида
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

        private const float LabelFadeSeconds = PaperMapUi.LabelFadeSeconds; // Н11: общий темп фейдов — PaperMapUi

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

        // ————— свои надписи и контуры (Map Style Editor) —————

        private void BuildAnnotations()
            => annotations.BuildAnnotations(); // Н11: слой аннотаций — подсистема

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
        internal void PersistStyle() // Н11: пишет правки стиля в ассет (аннотации)
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
        internal int ActiveOrderTargetIndex() // Н11: читают подписи и кольца цели
        {
            var orders = session != null ? session.Orders : null;
            var active = orders != null ? orders.Active : null;
            return active == null
                ? -1
                : active.State == OrderState.Aboard ? active.DropoffIndex : active.PickupIndex;
        }

        internal Ship Ship => ship; // Н11: состояние корабля для маркера (ленивый поиск — в ShipPosition)
        internal HullSystem Hull => hull; // Н11: подсистеме смерти нужен hull
        // ————— игрок: ракета, видна всегда —————

        internal Transform ShipPosition() // Н11: читает подсистема колец
        {
            if (ship == null)
                ship = FindAnyObjectByType<Ship>();
            BindHullDeath();
            return ship != null ? ship.transform : null;
        }

        // ориентация корпуса в мировых градусах (M8.5: поза посадки маркера) —
        // та же конвенция, что в MinimapBinder.HullHeadingDeg: поворот тела +
        // нос спрайта ShipController
        internal float HullHeadingDeg() // Н11: читает подсистема колец
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

        private void OnShipDied()
            => annotations.OnShipDied(); // Н11: эффекты смерти — подсистема аннотаций

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

        internal Vector2 PlayerWorldPosition() // Н11: читает подсистема вида
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
        // (тело — в PaperMapProjection.SetView, Н11)
        public void SetView(float viewZoom, Vector2 sheetOffset)
            => view.SetView(viewZoom, sheetOffset);

        public void OnBeginDrag(PointerEventData eventData)
            => view.BeginDrag(eventData, PressOnChrome); // Н11: пан — подсистема вида

        public void OnDrag(PointerEventData eventData)
            => view.Drag(eventData); // Н11: пан — подсистема вида

        // M8.8: клик-выбор. Нажатие сбрасывает «перетащили» — клик снова жив;
        // клик по телу ставит/снимает метку (ToggleWaypoint), правый — снимает
        public void OnPointerDown(PointerEventData eventData)
            => view.PressReset(); // Н11: пан-состояние — подсистема вида

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
            => view.EndDrag(eventData); // Н11: пан — подсистема вида

        private void RebuildAll()
        {
            ClearChildren(transform);
            chrome.Reset(); // Н11: хром — лист/бумага/секторы/легенда/линейка/кнопки
            beltsRoot = null;
            bodiesRoot = null;
            labels.Reset(); // Н11: подписи/размеры иконок — подсистема
            rings.Reset(); // Н11: кольца/маркеры/край мира/кометы — подсистема
            annotations.Reset(); // Н11: слой аннотаций + эффекты смерти — подсистема
            view.Reset(); // Н11: пан/зум/следование — подсистема вида
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
            => PaperMapUi.ClearChildren(t); // Н11: реализация в PaperMapUi

        private static RectTransform MakeRect(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
            => PaperMapUi.MakeRect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax); // Н11: PaperMapUi

        private static RectTransform MakeImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size, bool raycast)
            => PaperMapUi.MakeImage(name, parent, sprite, color, size, raycast); // Н11: PaperMapUi

        private static Color WithAlpha(Color color, float alpha)
            => PaperMapUi.WithAlpha(color, alpha); // Н11: реализация в PaperMapUi

    }
}

// Н11-step1-papermapui-applied

// Н11-step2-projection-applied

// Н11-step4b1-applied

// Н11-step4b2-applied

// Н11-step5-rings-applied

// Н11-step6-annotations-applied
