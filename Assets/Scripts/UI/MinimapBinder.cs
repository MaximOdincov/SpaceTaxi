using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Н-12/G-15/Н6: круглый радар-прибор. Проекция «от корабля», ориентация
    // north-up (Н6 M1, решение владельца вместо heading-up Н5.22): мир НЕ
    // вращается — север всегда вверх, кольца/крест/сетка/подпись «С» стоят
    // статично, цель/waypoint — по мировым координатам без поворота. Иконка
    // корабля в центре поворачивается по курсу (Н6 M2): нос = вектор скорости,
    // на месте/в посадке — ориентация корпуса (Н7 G7.2, вес непрерывен по
    // скорости); базовая ориентация арта — нос на +45° (Н6 M3,
    // PNG-анализ for_mini). Масштаб НЕлинейный по дистанции:
    // r = R * (d / maxRange)^rangeExponent — экспонента < 1 растягивает
    // ближнюю зону (объекты рядом крупнее) и сжимает дальнюю (обзор
    // отдалённее линейного); рисуются ВСЕ тела — за охватом кламп к ободу
    // с приглушением и уменьшением (без хаотичных исчезновений/мерцаний).
    // Важные маркеры: цель заказа — жёлтый ореол (TaxiColor, как HUD/карта),
    // waypoint — зелёный ромб (как своя метка на карте, G-15), живые кометы
    // и предупреждение — кламп к ободу. Корабль — иконка Art/icons/for_mini.png
    // с кольцом-подложкой. Дальше — стиль «прибора»: тёмный фон, зелёные
    // линии, свечение — полупрозрачные спрайты без шейдеров. Вся геометрия
    // строится кодом, сцена не трогается.
    public class MinimapBinder : MonoBehaviour
    {
        private const int BuoyCount = 12;         // буи связи по границе мира
        private const float RebuildWidthEps = 2f; // пересборка при заметной смене ширины

        // G-15: обзор радара (юнитов мира до обода) и показатель нелинейности.
        // Радиус маркера на приборе: r = R * (d / maxRange)^rangeExponent.
        // Экспонента < 1 раздвигает ближнюю зону (рядом — крупнее) и сжимает
        // дальнюю — видно отдалённее прежних 850 линейных юнитов. Владелец
        // подкрутит под баланс в Inspector.
        [SerializeField] private float maxRange = 1400f;
        [SerializeField] private float rangeExponent = 0.6f;

        private const float MarkAlpha = 0.85f;    // точки тел приглушены — корабль читается первым
        private const float OffEdgeAlpha = 0.5f;  // вид «за краем охвата»: тусклее и мельче
        private const float ClampedScale = 0.72f; // уменьшение маркеров, клампнутых к ободу
        private const float NearScaleMul = 1.25f; // тела в ближней зоне рисуются крупнее (G-15)
        private const float FarScaleMul = 0.85f;  // …и мельче на краю охвата
        private const float RimInset = 8f;        // отступ маркеров от обода при клампе
        private const float WaypointRimInset = 15f; // червон: запас под полудиагональ+пульс (хотфикс «обрезается»)
        private const float CometTailPx = 11f;    // длина хвоста кометы на радаре, px
        // Н6 M3: ар for_mini смотрит носом в правый верх — PNG-анализ: главная
        // ось силуэта +45° (Unity, от +X). Поворот иконки = курс − этот офсет.
        private const float ShipIconNoseDeg = 45f;
        private const float ShipIconHoldSpeed = 0.5f; // ниже — корабль «стоит»: курс из ориентации корпуса
        private const float ShipIconBlendSpeed = 12f; // выше — иконка целиком по вектору скорости (Н7 G7.2)
        private const float IconTurnRate = 10f;       // сглаживание поворота иконки, 1/с

        // кольца дистанций от корабля, юниты мира. Подобраны под нелинейную карту
        // (maxRange 1400, exp 0.6): на приборе лежат примерно на 26/44/63/82% R —
        // равномерный ряд без слипания у обода
        private static readonly float[] RingDistances = { 150f, 350f, 650f, 1000f };

        // — палитра «прибора» —
        private static readonly Color PanelColor = new Color(0.015f, 0.06f, 0.035f, 0.85f);
        private static readonly Color GridColor = new Color(0.35f, 0.85f, 0.55f);
        private static readonly Color PlanetColor = new Color(0.6f, 0.85f, 0.68f);
        private static readonly Color StarColor = new Color(1f, 0.78f, 0.32f);
        private static readonly Color HoleCoreColor = new Color(0.05f, 0.04f, 0.09f);
        private static readonly Color HoleHaloColor = new Color(0.55f, 0.35f, 0.85f, 0.3f);
        private static readonly Color BeltColor = new Color(0.5f, 0.72f, 0.58f, 0.45f);
        private static readonly Color RepairColor = new Color(0.7f, 0.82f, 0.95f);
        private static readonly Color StationNoTierColor = new Color(1f, 0.88f, 0.35f); // тир не задан
        // G-15: цель заказа — жёлтая (TaxiColor, как HUD/карта/стрелка), своя
        // метка (waypoint) — зелёная, единый цвет с маркером в мире (Н-8/G-15)
        private static readonly Color TargetColor = OnboardComputerFactory.TaxiColor;
        private static readonly Color WaypointColor = WaypointIndicator.WaypointGreen;
        private static readonly Color ShipColor = new Color(0.93f, 1f, 0.96f);          // корабль — самый яркий маркер прибора
        private static readonly Color CometWarnColor = new Color(1f, 0.42f, 0.36f);
        private static readonly Color BuoyColor = new Color(0.45f, 0.85f, 0.6f, 0.9f);

        // тир топливной сети (Н-4): 1 — бедная/дешёвая … 4 — люкс (полный бак)
        private static readonly Color[] FuelTierColors =
        {
            new Color(0.35f, 0.8f, 1f),
            new Color(0.42f, 0.95f, 0.6f),
            new Color(1f, 0.66f, 0.3f),
            new Color(1f, 0.45f, 0.42f),
        };

        private struct BodyMark
        {
            public CelestialBody Body;
            public RectTransform Root;
            public CanvasGroup Group; // приглушение «за краем охвата» одним альфой
        }

        private struct BeltMark
        {
            public CelestialBodyData Data;
            public RectTransform Ring;
            public CanvasGroup Group;
        }

        private struct BuoyMark
        {
            public Vector2 Dir;       // единичное направление к точке на границе мира
            public RectTransform Root;
        }

        private struct CometDot
        {
            public RectTransform Root;
            public Image Img;
            public RectTransform Tail; // линия-хвост по вектору скорости (Comet.FlightDirection)
        }

        private GameSession session;
        private LevelAsset level;
        private OrderService orders;
        private MapScreenBinder mapBinder;
        private GravityField gravity;
        private Ship ship;
        private ShipController controller; // кэш для SpriteNoseAngle (Н7 G7.2)
        private CometSystem cometSystem;
        private TMP_FontAsset font;

        private bool built;
        private float builtWidth;
        private float radarRadiusPx;   // радиус прибора в пикселях (по builtWidth)
        private float worldEdgeRadius; // граница мира — на ней стоят буи связи
        private float shipIconDeg = 45f;       // текущий поворот иконки корабля (старт: нос вверх)
        private float shipIconNoseOffset;      // нос арта в системе спрайта: 45° (for_mini) или 0° (заглушка)
        private Vector2 shipPos;       // позиция корабля в этом кадре (центр проекции)
        private float beltAngle;
        private float beltSpinDeg = 0.12f * Mathf.Rad2Deg;
        private float cometWarnUntil;
        private Vector2 cometWarnEntry;
        private Vector2 cometWarnDirection;
        private bool cometsBound;
        private bool targetOnRim;      // размер цели меняется только при смене «за краем»
        private bool waypointOnRim;

        private RectTransform chrome;  // весь прибор, пересобирается при смене размера
        private RectTransform sweep;
        private RectTransform bodiesRoot;
        private RectTransform shipArrow;
        private RectTransform shipIconRect; // иконка корабля — крутится по курсу (Н6 M2)
        private RectTransform targetRing;
        private Image targetRingImage;
        private RectTransform waypointMark;
        private Image waypointImage;
        private RectTransform cometWarn;
        private RectTransform crossRoot;   // крест мировых осей — стоит статично (north-up, Н6 M1)
        private RectTransform northLabel;  // «С» — статично наверху обода: север всегда вверх

        private readonly List<BodyMark> marks = new List<BodyMark>();
        private readonly List<BeltMark> belts = new List<BeltMark>();
        private readonly List<BuoyMark> buoys = new List<BuoyMark>();
        // словарь, а не List+Exists: поиск точки кометы — O(1) без аллокаций в кадр
        private readonly Dictionary<Comet, CometDot> cometDots = new Dictionary<Comet, CometDot>();

        // — процедурные спрайты прибора (UI рисует по sizeDelta, PPU не важен) —
        private static Sprite thickRingSprite;   // кольцо потолще: цель, станции, дыра
        private static Sprite diamondSprite;     // waypoint-метка
        private static Sprite dashRingSprite;    // пояс астероидов: пунктирная дуга
        private static Sprite sweepWideSprite;   // развёртка: широкий тусклый сектор
        private static Sprite sweepNarrowSprite; // развёртка: узкий яркий сектор
        private static Sprite shipIconSprite;    // иконка игрока (G-15)

        private void Awake()
        {
            // статичную заготовку сцены убираем целиком — радар строится кодом
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }

        private void Update()
        {
            if (!TryBind())
                return;
            float width = ((RectTransform)transform).rect.width;
            if (width < 50f)
                return; // ждём layout канваса
            if (!built || Mathf.Abs(width - builtWidth) > RebuildWidthEps)
            {
                ClearChrome();
                Build(width);
                builtWidth = width;
                built = true;
            }

            shipPos = ship.transform.position; // центр проекции в этом кадре
            UpdateShipIconDeg();

            UpdateSweep();
            UpdateBelts();
            UpdateMarks();
            UpdateBuoys();
            UpdateTargetRing();
            UpdateWaypointMark();
            UpdateComets();
        }

        // ————— привязка (лениво, как у остальных биндеров) —————

        private bool TryBind()
        {
            if (session == null)
                session = FindAnyObjectByType<GameSession>();
            if (session == null)
                return false;
            if (level == null)
                level = session.Level;
            if (gravity == null)
                gravity = FindAnyObjectByType<GravityField>();
            if (gravity == null || gravity.Bodies.Count == 0)
                return false; // мир собирается в WorldBuilder'е в Start
            if (ship == null)
                ship = FindAnyObjectByType<Ship>();
            if (ship == null)
                return false; // проекция центрируется на корабле — без него рисовать нечего
            if (font == null)
                font = ResolveFont();
            if (orders == null && session.Orders != null)
                orders = session.Orders;
            if (mapBinder == null)
                mapBinder = FindAnyObjectByType<MapScreenBinder>(FindObjectsInactive.Include); // Н-1: живёт на неактивной карте
            BindComets();
            return true;
        }

        private void BindComets()
        {
            if (cometsBound)
                return;
            cometSystem = FindAnyObjectByType<CometSystem>();
            if (cometSystem == null)
                return; // CometSystem ставится WorldBuilder'ом в Start — подождём
            cometSystem.CometIncoming += OnCometIncoming;
            cometSystem.CometSpawned += OnCometSpawned;
            cometsBound = true;
        }

        // шрифт прибора: JetBrains из HUD (как цифры скорости), иначе цепочка карты
        private TMP_FontAsset ResolveFont()
        {
            HudBinder hud = FindAnyObjectByType<HudBinder>();
            if (hud != null)
            {
                Transform speed = hud.transform.Find("SpeedValue");
                if (speed != null)
                {
                    TMP_Text text = speed.GetComponent<TMP_Text>();
                    if (text != null && text.font != null)
                        return text.font;
                }
            }
            return Resources.Load<TMP_FontAsset>("Fonts/TMP/Excalifont SDF")
                ?? Resources.Load<TMP_FontAsset>("Excalifont SDF");
        }

        private void OnCometIncoming(Vector2 entry, Vector2 direction)
        {
            cometWarnEntry = entry;
            cometWarnDirection = direction;
            cometWarnUntil = Time.time + (cometSystem != null ? cometSystem.WarningSeconds : 20f);
        }

        private void OnCometSpawned()
        {
            cometWarnUntil = 0f;
        }

        // ————— постройка —————

        private void ClearChrome()
        {
            if (chrome != null)
                Destroy(chrome.gameObject);
            chrome = null;
            sweep = null;
            bodiesRoot = null;
            shipArrow = null;
            shipIconRect = null;
            targetRing = null;
            targetRingImage = null;
            waypointMark = null;
            waypointImage = null;
            cometWarn = null;
            crossRoot = null;
            northLabel = null;
            targetOnRim = false;
            waypointOnRim = false;
            marks.Clear();
            belts.Clear();
            buoys.Clear();
            cometDots.Clear();
        }

        private void Build(float diameter)
        {
            float radius = diameter * 0.5f;

            // масштаб «от корабля»: обод = maxRange юнитов мира, радиус внутри —
            // нелинейная карта RadarDistPx (G-15)
            radarRadiusPx = radius;
            worldEdgeRadius = level != null ? level.WorldRadius : 2500f;

            AsteroidBeltSystem beltSystem = FindAnyObjectByType<AsteroidBeltSystem>();
            beltSpinDeg = (beltSystem != null ? beltSystem.BeltOrbitSpeed : 0.12f) * Mathf.Rad2Deg;

            chrome = MakeStretch("Radar", (RectTransform)transform);

            Color bezel = WithAlpha(GridColor, 0.9f);
            MakeImg("BezelGlow", chrome, RingSprite(), WithAlpha(GridColor, 0.13f), Vector2.one * (diameter * 1.09f));
            MakeImg("Bezel", chrome, RingSprite(), bezel, Vector2.one * diameter);

            // буи связи — точки по границе мира (та же сеть, что на карте Н-1);
            // стоят на месте в мире, поэтому видны только когда попали в охват радара
            for (int i = 0; i < BuoyCount; i++)
            {
                float ang = i * 360f / BuoyCount * Mathf.Deg2Rad;
                RectTransform buoy = MakeImg("Buoy_" + i, chrome, StubSprites.Circle(64f), BuoyColor, Vector2.one * 5f);
                buoy.gameObject.SetActive(false);
                buoys.Add(new BuoyMark { Dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)), Root = buoy });
            }

            // маска-круг: вся начинка прибора живёт внутри обода
            RectTransform mask = MakeStretch("Mask", chrome);
            Image maskImage = mask.gameObject.AddComponent<Image>();
            maskImage.sprite = StubSprites.Circle(diameter);
            maskImage.raycastTarget = false;
            Mask maskComponent = mask.gameObject.AddComponent<Mask>();
            maskComponent.showMaskGraphic = false;

            MakeStretchImg("Background", mask, StubSprites.Circle(diameter), PanelColor);

            // крест мировых осей: north-up (Н6 M1) — стоит статично (вертикаль =
            // север), точка под иконкой корабля — отдельно
            Color cross = WithAlpha(GridColor, 0.12f);
            crossRoot = MakeNode("WorldCross", mask, Vector2.zero);
            MakeImg("CrossH", crossRoot, StubSprites.Line(), cross, new Vector2(diameter, 1.6f));
            MakeImg("CrossV", crossRoot, StubSprites.Line(), cross, new Vector2(1.6f, diameter));
            MakeImg("CenterDot", mask, StubSprites.Circle(64f), WithAlpha(GridColor, 0.5f), Vector2.one * 3f);

            // развёртка: широкий тусклый сектор + узкий яркий (чисто визуал)
            sweep = MakeNode("Sweep", mask, Vector2.one * diameter);
            MakeImg("Wide", sweep, SweepWide(), WithAlpha(GridColor, 0.10f), Vector2.one * diameter);
            MakeImg("Narrow", sweep, SweepNarrow(), WithAlpha(GridColor, 0.22f), Vector2.one * diameter);

            // кольца дистанций от корабля: радиус каждого — по нелинейной карте
            // (G-15); подписи внутри кольца «сверху» — при heading-up это сторона
            // курса, что удобно; внешнему кольцу подпись не даём (не влезает до обода)
            for (int k = 0; k < RingDistances.Length; k++)
            {
                float rPx = RadarDistPx(RingDistances[k]);
                if (rPx >= radius)
                    break; // охват перекручен — кольцо за ободом не рисуем
                MakeImg("Ring_" + (int)RingDistances[k], mask, RingSprite(),
                    WithAlpha(GridColor, 0.16f + 0.06f * k), Vector2.one * (rPx * 2f));
                if (k < RingDistances.Length - 1)
                {
                    TMP_Text label = MakeLabel("Dist_" + (int)RingDistances[k], mask,
                        FormatDistance(RingDistances[k]), WithAlpha(GridColor, 0.55f));
                    RectTransform labelRect = (RectTransform)label.transform;
                    labelRect.sizeDelta = new Vector2(70f, 14f);
                    labelRect.anchoredPosition = Vector2.up * (rPx - 8f);
                }
            }

            // метка «С» (север) — статично наверху обода: north-up (Н6 M1)
            TMP_Text north = MakeLabel("North", mask, "С", WithAlpha(GridColor, 0.8f));
            northLabel = (RectTransform)north.transform;
            northLabel.sizeDelta = new Vector2(18f, 14f);
            northLabel.anchoredPosition = Vector2.up * (radarRadiusPx - 11f);

            // пояса астероидов: пунктирное кольцо по осевой линии, крутится как в
            // мире; видимый диаметр зависит от дистанции (нелинейная карта) —
            // считается каждый кадр в UpdateBelts
            RectTransform beltsRoot = MakeNode("Belts", mask, Vector2.zero);
            if (level != null)
                foreach (CelestialBodyData data in level.Bodies)
                {
                    if (data.Type != BodyType.AsteroidBelt)
                        continue;
                    RectTransform ring = MakeImg("Belt_" + data.DisplayName, beltsRoot, DashRing(), BeltColor, Vector2.one * 14f);
                    CanvasGroup group = ring.gameObject.AddComponent<CanvasGroup>();
                    group.blocksRaycasts = false;
                    belts.Add(new BeltMark { Data = data, Ring = ring, Group = group });
                }

            bodiesRoot = MakeNode("Bodies", mask, Vector2.zero);
            foreach (CelestialBody body in gravity.Bodies)
                marks.Add(new BodyMark { Body = body, Root = BuildBodyMark(body, out CanvasGroup group), Group = group });

            targetRing = MakeImg("TargetRing", mask, ThickRing(), TargetColor, Vector2.one * 16f);
            targetRingImage = targetRing.GetComponent<Image>();
            targetRing.gameObject.SetActive(false);

            waypointMark = MakeImg("WaypointMark", mask, Diamond(), WaypointColor, Vector2.one * 12f);
            waypointImage = waypointMark.GetComponent<Image>();
            waypointMark.gameObject.SetActive(false);

            cometWarn = MakeImg("CometWarn", mask, StubSprites.ShipTriangle(), CometWarnColor, Vector2.one * 13f);
            cometWarn.gameObject.SetActive(false);

            // корабль: в центре прибора, самый крупный и яркий маркер — ореол +
            // кольцо-подложка + иконка for_mini.png (G-15); north-up: мир не
            // вращается, курс выражен поворотом самой иконки каждый кадр (Н6 M2)
            shipArrow = MakeNode("ShipArrow", mask, Vector2.zero);
            MakeImg("Glow", shipArrow, StubSprites.Circle(64f), WithAlpha(ShipColor, 0.22f), Vector2.one * 44f);
            MakeImg("Backing", shipArrow, ThickRing(), WithAlpha(ShipColor, 0.9f), Vector2.one * 30f);
            Sprite icon = ShipIcon();
            RectTransform iconRect = MakeImg("Icon", shipArrow, icon != null ? icon : StubSprites.ShipTriangle(),
                ShipColor, Vector2.one * 26f);
            // базовая ориентация: артовый нос на +45° (Н6 M3), заглушка
            // ShipTriangle остриём вправо (0°); до первого вектора скорости
            // иконка носом вверх
            shipIconNoseOffset = icon != null ? ShipIconNoseDeg : 0f;
            shipIconRect = iconRect;
            shipIconDeg = 90f - shipIconNoseOffset;
            iconRect.localRotation = Quaternion.Euler(0f, 0f, shipIconDeg);
        }

        // Н3.2/G-15: тела — кольца по фактическому радиусу (в номинальном масштабе
        // прибора; ближе к кораблю маркер дополнительно крупнее — см. UpdateMarks),
        // тип читается формой и цветом: планета — зелёное кольцо, звезда — крупное
        // тёплое охра-кольцо, дыра — тёмное кольцо с точкой в центре
        private RectTransform BuildBodyMark(CelestialBody body, out CanvasGroup group)
        {
            RectTransform root = MakeNode("Body_" + body.DisplayName, bodiesRoot, Vector2.zero);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            float trueD = body.Radius * 2f * UnitPx(); // диаметр тела в пикселях прибора

            switch (body.Type)
            {
                case BodyType.Star:
                {
                    float d = Mathf.Clamp(trueD * 1.35f, 18f, 56f); // крупнее любой планеты
                    Halo(root, d * 1.6f, StarColor, 0.15f);
                    RingMark(root, d, WithAlpha(StarColor, 0.95f));
                    Dot(root, Mathf.Max(5f, d * 0.3f), StarColor);
                    break;
                }
                case BodyType.BlackHole:
                {
                    // тёмное кольцо + точка в центре; тусклый фиолетовый ореол —
                    // силуэт «горизонта» читается на тёмном фоне прибора
                    float d = Mathf.Clamp(trueD * 1.2f, 14f, 50f);
                    Halo(root, d * 1.5f, HoleHaloColor, 0.22f);
                    RingMark(root, d, HoleCoreColor);
                    Dot(root, Mathf.Max(4f, d * 0.28f), HoleHaloColor);
                    break;
                }
                default:
                {
                    float d = Mathf.Clamp(trueD, 8f, 44f);
                    Halo(root, d * 1.25f, PlanetColor, 0.1f);
                    RingMark(root, d, WithAlpha(PlanetColor, 0.9f));
                    BuildStationDot(root, body, d);
                    break;
                }
            }
            return root;
        }

        // сервисы станции — цветная точка внутри зелёного кольца планеты (тип
        // тела остаётся зелёным кольцом, сеть услуг не теряется): хаб — точка +
        // внутреннее кольцо цвета топливного тира, заправка — точка тира,
        // мастерская без заправки — стальная точка
        private static void BuildStationDot(RectTransform root, CelestialBody body, float ringD)
        {
            if (body.IsHub)
            {
                Color c = FuelTierColor(body.FuelTier);
                RingMark(root, Mathf.Max(ringD - 6f, 6f), WithAlpha(c, 0.5f));
                Dot(root, 5f, c);
            }
            else if (body.HasFuel)
            {
                Dot(root, 5f, FuelTierColor(body.FuelTier));
            }
            else if (body.HasRepair)
            {
                Dot(root, 5f, RepairColor);
            }
            // прочие планеты — чистое зелёное кольцо
        }

        // ————— покадровое обновление —————

        private void UpdateSweep()
        {
            if (sweep == null)
                return;
            // Н6 хотфикс: сектор не «ищет» по кругу, а смотрит по носу корабля —
            // тот же сглаженный курс, что у иконки (shipIconDeg = курс − нос арта).
            // Арт сектора «остриём вверх» (интринсик 90°), у иконки нос скомпенсирован
            // на 45° — доворот сектора = shipIconDeg − 45; стоит корабль — сектор
            // держит последний курс, как и иконка.
            sweep.localRotation = Quaternion.Euler(0f, 0f, shipIconDeg - 45f);
        }

        private void UpdateBelts()
        {
            beltAngle += beltSpinDeg * Time.unscaledDeltaTime;
            for (int i = 0; i < belts.Count; i++)
            {
                CelestialBodyData data = belts[i].Data;
                belts[i].Ring.anchoredPosition = WorldToRadar(data.Position);
                // видимый диаметр пояса — по нелинейной карте: прирост радиуса от
                // центра пояса наружу (в ближней зоне пояс крупнее, вдали — жмётся)
                float d = Mathf.Min(Vector2.Distance(data.Position, shipPos), maxRange);
                float span = RadarDistPx(Mathf.Min(d + data.Radius, maxRange)) - RadarDistPx(d);
                belts[i].Ring.sizeDelta = Vector2.one * Mathf.Max(2f * span, 12f);
                belts[i].Ring.localRotation = Quaternion.Euler(0f, 0f, beltAngle);
                belts[i].Group.alpha = Vector2.Distance(data.Position, shipPos) > maxRange ? OffEdgeAlpha : 1f; // за охватом — приглушён
            }
        }

        // G-15: рисуем ВСЕ тела всегда — за охватом кламп к ободу с приглушением
        // (CanvasGroup) и уменьшением, никаких хаотичных исчезновений; в ближней
        // зоне маркеры крупнее (пара Near/FarScaleMul поверх нелинейной карты)
        private void UpdateMarks()
        {
            for (int i = 0; i < marks.Count; i++)
            {
                CelestialBody body = marks[i].Body;
                Vector2 p = ClampToRim(WorldToRadar(body.transform.position), out bool clamped);
                marks[i].Root.anchoredPosition = p;
                float t = Mathf.Min(Vector2.Distance(body.transform.position, shipPos) / Mathf.Max(maxRange, 1f), 1f);
                marks[i].Root.localScale = Vector3.one * (Mathf.Lerp(NearScaleMul, FarScaleMul, t) * (clamped ? ClampedScale : 1f));
                float a = clamped ? OffEdgeAlpha : 1f;
                if (!Mathf.Approximately(marks[i].Group.alpha, a))
                    marks[i].Group.alpha = a;
            }
        }

        private void UpdateBuoys()
        {
            for (int i = 0; i < buoys.Count; i++)
            {
                Vector2 p = WorldToRadar(buoys[i].Dir * worldEdgeRadius);
                bool visible = p.magnitude <= radarRadiusPx - RimInset;
                if (buoys[i].Root.gameObject.activeSelf != visible)
                    buoys[i].Root.gameObject.SetActive(visible);
                if (visible)
                    buoys[i].Root.anchoredPosition = p;
            }
        }

        // Н6 M2/M3 + Н7 G7.2: north-up — иконка корабля крутится по курсу с
        // лёгким сглаживанием. При движении нос = вектор скорости (rot =
        // atan2(vel) − нос_арта); при развороте на месте/посадке вектор не
        // отражает разворот корпуса, поэтому вес источника непрерывен по
        // скорости: t = clamp((|v| − hold)/(blend − hold)), курс = LerpAngle(
        // курс корпуса, курс из вектора, t) — без скачка на пересечении порога.
        // Курс корпуса = body.rotation + SpriteNoseAngle (та же конвенция, что
        // в ShipController.UpdateHeading; headingDir у контроллера приватен) —
        // сектор радара после Н6.8 читает тот же сглаженный shipIconDeg,
        // UpdateSweep не менялся.
        private void UpdateShipIconDeg()
        {
            if (shipIconRect == null)
                return;
            Vector2 velocity = ship.Body != null ? ship.Body.linearVelocity : Vector2.zero;
            float speed = velocity.magnitude;
            float velocityDeg = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - shipIconNoseOffset;
            float hullDeg = HullHeadingDeg() - shipIconNoseOffset;
            float t = Mathf.Clamp01(
                (speed - ShipIconHoldSpeed) / (ShipIconBlendSpeed - ShipIconHoldSpeed));
            float target = Mathf.LerpAngle(hullDeg, velocityDeg, t);
            shipIconDeg = Mathf.LerpAngle(shipIconDeg, target,
                1f - Mathf.Exp(-IconTurnRate * Time.unscaledDeltaTime));
            shipIconRect.localRotation = Quaternion.Euler(0f, 0f, shipIconDeg);
        }

        // ориентация корпуса в мировых градусах; без физики — текущий
        // сглаженный курс (иконка стоит на месте, не дёргается)
        private float HullHeadingDeg()
        {
            if (ship.Body == null)
                return shipIconDeg + shipIconNoseOffset;
            if (controller == null)
                controller = ship.GetComponent<ShipController>();
            float nose = controller != null ? controller.SpriteNoseAngle : 0f;
            return ship.Body.rotation + nose;
        }

        // цель активного заказа: индекс как в WaypointIndicator — при посадке за
        // пассажиром ActiveOrderChanged не фаерится, поэтому читаем Active каждый кадр
        private void UpdateTargetRing()
        {
            OrderOffer active = orders != null ? orders.Active : null;
            int index = active == null
                ? -1
                : active.State == OrderState.Aboard ? active.DropoffIndex : active.PickupIndex;

            if (index < 0 || level == null || index >= level.Bodies.Count)
            {
                if (targetRing.gameObject.activeSelf)
                    targetRing.gameObject.SetActive(false);
                return;
            }

            CelestialBodyData data = level.Bodies[index];
            CelestialBody live = FindLiveBody(data.DisplayName);
            Vector2 pos = live != null ? (Vector2)live.transform.position : data.Position;

            targetRing.gameObject.SetActive(true);
            Vector2 p = ClampToRim(WorldToRadar(pos), out bool clamped);
            targetRing.anchoredPosition = p;
            if (targetOnRim != clamped)
            {
                targetOnRim = clamped;
                targetRing.sizeDelta = Vector2.one * (clamped ? 10f : 16f); // за охватом — точка на ободе
            }
            float a = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 5.5f); // ровный пульс — без резких миганий
            targetRingImage.color = WithAlpha(TargetColor, a * (clamped ? OffEdgeAlpha : 1f));
        }

        private void UpdateWaypointMark()
        {
            CelestialBodyData waypoint = mapBinder != null ? mapBinder.Waypoint : null;
            if (waypoint == null)
            {
                if (waypointMark.gameObject.activeSelf)
                    waypointMark.gameObject.SetActive(false);
                return;
            }

            CelestialBody live = FindLiveBody(waypoint.DisplayName);
            Vector2 pos = live != null ? (Vector2)live.transform.position : waypoint.Position;

            waypointMark.gameObject.SetActive(true);
            // ХОТФИКС владельца («червон обрезается»): кламп с запасом под полудиагональ
            // ромба (12px → кончик 8.5px) + пульс — прежде RimInset 8 срезал кончики
            // маской радара при клампе к ободу
            Vector2 p = ClampToRim(WorldToRadar(pos) + Vector2.up * 10f, out bool clamped, WaypointRimInset);
            waypointMark.anchoredPosition = p;
            if (waypointOnRim != clamped)
            {
                waypointOnRim = clamped;
                waypointMark.sizeDelta = Vector2.one * (clamped ? 8f : 10f); // хотфикс: иконка меньше
            }
            float a = 0.72f + 0.18f * Mathf.Sin(Time.unscaledTime * 2.4f); // лёгкий пульс, как у метки в мире (Н-8/G-15)
            waypointImage.color = WithAlpha(WaypointColor, a * (clamped ? OffEdgeAlpha : 1f));
        }

        private void UpdateComets()
        {
            if (cometSystem == null)
                return;

            // живые кометы — голубые точки по ActiveComets (важные: кламп к ободу);
            // мёртвые записи чистим
            List<Comet> dead = null;
            foreach (KeyValuePair<Comet, CometDot> kv in cometDots)
            {
                if (kv.Key == null)
                {
                    (dead ??= new List<Comet>()).Add(kv.Key);
                    if (kv.Value.Root != null)
                        Destroy(kv.Value.Root.gameObject);
                }
                else
                {
                    Vector2 p = ClampToRim(WorldToRadar(kv.Key.transform.position), out bool clamped);
                    kv.Value.Root.anchoredPosition = p;
                    kv.Value.Img.color = WithAlpha(Comet.CometColor, clamped ? OffEdgeAlpha : MarkAlpha);
                    UpdateCometTail(kv.Key, kv.Value, clamped);
                }
            }
            if (dead != null)
                foreach (Comet comet in dead)
                    cometDots.Remove(comet);
            foreach (Comet comet in cometSystem.ActiveComets)
            {
                if (comet == null || cometDots.ContainsKey(comet))
                    continue;
                RectTransform dot = MakeImg("CometDot", bodiesRoot, StubSprites.Circle(64f), WithAlpha(Comet.CometColor, MarkAlpha), Vector2.one * 5f);
                // хвост позади точки: линия по вектору скорости, рисуется под точкой
                RectTransform tail = MakeImg("CometTail", dot, StubSprites.Line(),
                    WithAlpha(Comet.CometColor, 0.6f), new Vector2(CometTailPx, 2.2f));
                tail.SetAsFirstSibling();
                cometDots.Add(comet, new CometDot { Root = dot, Img = dot.GetComponent<Image>(), Tail = tail });
            }

            // предупреждение: остриё по направлению входа, кламп к краю радара;
            // таймер — страховка на случай пропущенного CometSpawned
            bool showWarn = Time.time < cometWarnUntil;
            if (cometWarn.gameObject.activeSelf != showWarn)
                cometWarn.gameObject.SetActive(showWarn);
            if (!showWarn)
                return;
            cometWarn.anchoredPosition = ClampToRim(WorldToRadar(cometWarnEntry), out _);
            // north-up (Н6 M1): направление в мире = направление на приборе
            float angle = Mathf.Atan2(cometWarnDirection.y, cometWarnDirection.x) * Mathf.Rad2Deg;
            cometWarn.localRotation = Quaternion.Euler(0f, 0f, angle - 90f); // спрайт остриём вверх
        }

        // хвост кометы: короткая линия назад по вектору скорости (north-up —
        // мир не поворачивается, Н6 M1); за охватом комета — просто точка на
        // ободе, хвост прячется
        private void UpdateCometTail(Comet comet, CometDot dot, bool clamped)
        {
            Vector2 dir = comet.FlightDirection;
            bool show = !clamped && dir.sqrMagnitude > 1e-6f;
            if (dot.Tail.gameObject.activeSelf != show)
                dot.Tail.gameObject.SetActive(show);
            if (!show)
                return;
            dot.Tail.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            dot.Tail.anchoredPosition = -dir.normalized * (CometTailPx * 0.5f);
        }

        // ————— хелперы —————

        // мир → радар: центр прибора = позиция корабля в этом кадре (shipPos),
        // радар «плывёт» вместе с ним. north-up (Н6 M1): смещение НЕ поворачивается —
        // мир фиксирован, север всегда вверх; радиус маппится нелинейно,
        // за maxRange — обод
        private Vector2 WorldToRadar(Vector2 world)
        {
            Vector2 o = world - shipPos;
            float d = o.magnitude;
            if (d < 1e-4f)
                return Vector2.zero;
            return o / d * RadarDistPx(Mathf.Min(d, maxRange));
        }

        // нелинейная карта дистанции (G-15): r = R * (d / maxRange)^rangeExponent —
        // ближняя зона растянута, дальняя сжата; maxRange защищён от нуля
        private float RadarDistPx(float dist) => radarRadiusPx * Mathf.Pow(dist / Mathf.Max(maxRange, 1f), rangeExponent);

        // номинальный масштаб для собственных размеров тел (px на юнит мира)
        private float UnitPx() => radarRadiusPx / maxRange;

        // важные цели за охватом клампятся к ободу: точка на краю прибора,
        // сама рисуется приглушённой (см. OffEdgeAlpha у вызывающих)
        private Vector2 ClampToRim(Vector2 radarPos, out bool clamped, float inset = RimInset)
        {
            float edge = radarRadiusPx - inset;
            if (radarPos.magnitude <= edge)
            {
                clamped = false;
                return radarPos;
            }
            clamped = true;
            return radarPos.sqrMagnitude > 1e-6f ? radarPos.normalized * edge : new Vector2(0f, edge);
        }

        private CelestialBody FindLiveBody(string displayName)
        {
            if (string.IsNullOrEmpty(displayName) || gravity == null)
                return null;
            foreach (CelestialBody body in gravity.Bodies)
                if (body.DisplayName == displayName)
                    return body;
            return null;
        }

        private static Color FuelTierColor(int tier)
        {
            if (tier >= 1 && tier <= FuelTierColors.Length)
                return FuelTierColors[tier - 1];
            return StationNoTierColor; // тир 0 — сеть не задана, базовая жёлтая
        }

        private static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        private static string FormatDistance(float d)
        {
            if (d < 1000f)
                return ((int)d).ToString(CultureInfo.InvariantCulture);
            return (d / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        }

        private static RectTransform MakeStretch(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static RectTransform MakeStretchImg(string name, RectTransform parent, Sprite sprite, Color color)
        {
            RectTransform rect = MakeStretch(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static RectTransform MakeNode(string name, RectTransform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform MakeImg(string name, RectTransform parent, Sprite sprite, Color color, Vector2 size)
        {
            RectTransform rect = MakeNode(name, parent, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        // точка тела — чуть приглушена, чтобы корабль читался первым
        private static void Dot(RectTransform parent, float size, Color color)
        {
            MakeImg("Dot", parent, StubSprites.Circle(64f), WithAlpha(color, MarkAlpha), Vector2.one * size);
        }

        // лёгкое свечение — полупрозрачный ореол за основной точкой
        private static void Halo(RectTransform parent, float size, Color color, float alpha)
        {
            MakeImg("Halo", parent, StubSprites.Circle(64f), WithAlpha(color, alpha), Vector2.one * size);
        }

        private static void RingMark(RectTransform parent, float size, Color color)
        {
            MakeImg("Ring", parent, ThickRing(), color, Vector2.one * size);
        }

        private TMP_Text MakeLabel(string name, RectTransform parent, string text, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            if (font != null)
                label.font = font;
            label.text = text;
            label.fontSize = 11f;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        private static Sprite RingSprite() => StubSprites.Ring(64f); // тонкое кольцо — обод и дистанции

        // G-15: иконка игрока — Art/icons/for_mini.png (белая ракета, красится
        // цветом прибора). Текстура импортирована в Multiple — берём единственный
        // суб-спрайт через LoadAll; фолбэк — процедурная стрелка StubSprites
        private static Sprite ShipIcon()
        {
            if (shipIconSprite == null)
            {
                Sprite[] frames = Resources.LoadAll<Sprite>("Art/icons/for_mini");
                if (frames != null && frames.Length > 0)
                    shipIconSprite = frames[0];
            }
            return shipIconSprite;
        }

        private static Sprite ThickRing()
        {
            if (thickRingSprite == null)
                thickRingSprite = Sprite.Create(
                    StubSprites.CreateMaskTexture(256, (x, y) =>
                    {
                        float d = Mathf.Sqrt(x * x + y * y);
                        return d <= 1f && d >= 0.86f;
                    }),
                    new Rect(0f, 0f, 256f, 256f), new Vector2(0.5f, 0.5f), 256f);
            return thickRingSprite;
        }

        private static Sprite Diamond()
        {
            if (diamondSprite == null)
                diamondSprite = Sprite.Create(
                    StubSprites.CreateMaskTexture(64, (x, y) => Mathf.Abs(x) + Mathf.Abs(y) <= 0.82f),
                    new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
            return diamondSprite;
        }

        private static Sprite DashRing()
        {
            if (dashRingSprite == null)
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

        private static Sprite SweepWide()
        {
            if (sweepWideSprite == null)
                sweepWideSprite = SectorSprite(16f);
            return sweepWideSprite;
        }

        private static Sprite SweepNarrow()
        {
            if (sweepNarrowSprite == null)
                sweepNarrowSprite = SectorSprite(6f);
            return sweepNarrowSprite;
        }

        // сектор остриём вверх (90°), радиус с запасом против зубчатого края текстуры
        private static Sprite SectorSprite(float halfDeg)
        {
            return Sprite.Create(
                StubSprites.CreateMaskTexture(128, (x, y) =>
                {
                    float d = Mathf.Sqrt(x * x + y * y);
                    if (d > 0.985f)
                        return false;
                    float ang = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
                    return Mathf.Abs(Mathf.DeltaAngle(ang, 90f)) <= halfDeg;
                }),
                new Rect(0f, 0f, 128f, 128f), new Vector2(0.5f, 0.5f), 128f);
        }
    }
}
