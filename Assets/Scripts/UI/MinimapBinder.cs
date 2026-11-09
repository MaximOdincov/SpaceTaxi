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
    // Н-12: круглый радар-прибор. Проекция «от корабля», ориентация «север
    // вверх» (Н3.2): центр прибора = позиция корабля каждый кадр (радар «плывёт»
    // вместе с ним), смещение тел = (тело − корабль) БЕЗ поворота на курс —
    // мировая +Y всегда вверх, развороты корабля картину не вращают; сам корабль
    // — стрелка в центре, крутится по носу. Фиксированный масштаб —
    // RadarsWorldRadius юнитов мира от корабля до обода. Обычные тела за охватом
    // скрываются; важные (цель заказа, waypoint, живые кометы, предупреждение)
    // клампятся к ободу приглушённой точкой «за краем». Кольца дистанций — от
    // корабля, буи связи — точки границы мира, попавшие в охват. Корабль —
    // крупная яркая стрелка с кольцом-подложкой, читается первым. Дальше —
    // стиль «прибора»: тёмный фон, зелёные линии, свечение — полупрозрачные
    // спрайты без шейдеров. Вся геометрия строится кодом, сцена не трогается.
    public class MinimapBinder : MonoBehaviour
    {
        private const float SweepSpeedDeg = 72f;  // скорость луча развёртки
        private const int BuoyCount = 12;         // буи связи по границе мира
        private const float RebuildWidthEps = 2f; // пересборка при заметной смене ширины

        // сколько юнитов мира видно от корабля до обода радара. 850 — обзор ночи
        // Н3.2 (было 600: тесно), владелец подкрутит под баланс.
        private const float RadarsWorldRadius = 850f;
        private const float MarkAlpha = 0.85f;    // точки тел приглушены — корабль читается первым
        private const float OffEdgeAlpha = 0.55f; // вид «за краем охвата»: тусклая точка на ободе
        private const float RimInset = 8f;        // отступ маркеров от обода при клампе
        private const float CometTailPx = 11f;    // длина хвоста кометы на радаре, px

        // кольца дистанций от корабля, юниты мира (метка «С» — север — у верхнего обода)
        private static readonly float[] RingDistances = { 200f, 400f, 600f, 800f };

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
        private static readonly Color TargetColor = new Color(0.235f, 1f, 0.557f);      // как HUD/посадочное кольцо
        private static readonly Color WaypointColor = new Color(1f, 0.72f, 0.25f);      // как waypoint в мире (Н-8)
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
        }

        private struct BeltMark
        {
            public CelestialBodyData Data;
            public RectTransform Ring;
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
        private ShipController controller;
        private CometSystem cometSystem;
        private TMP_FontAsset font;

        private bool built;
        private float builtWidth;
        private float pxPerUnit;       // фиксированный масштаб: px обода на юнит мира
        private float radarRadiusPx;   // радиус прибора в пикселях (по builtWidth)
        private float worldEdgeRadius; // граница мира — на ней стоят буи связи
        private Vector2 shipPos;       // позиция корабля в этом кадре (центр проекции)
        private float sweepAngle;
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
        private RectTransform targetRing;
        private Image targetRingImage;
        private RectTransform waypointMark;
        private Image waypointImage;
        private RectTransform cometWarn;

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

            UpdateSweep();
            UpdateBelts();
            UpdateMarks();
            UpdateBuoys();
            UpdateShipArrow();
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
            if (controller == null)
                controller = FindAnyObjectByType<ShipController>();
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
            targetRing = null;
            targetRingImage = null;
            waypointMark = null;
            waypointImage = null;
            cometWarn = null;
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

            // масштаб «от корабля»: обод = RadarsWorldRadius юнитов мира
            radarRadiusPx = radius;
            pxPerUnit = radius / RadarsWorldRadius;
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

            // крест направлений относительно корабля + точка под стрелкой
            Color cross = WithAlpha(GridColor, 0.12f);
            MakeImg("CrossH", mask, StubSprites.Line(), cross, new Vector2(diameter, 1.6f));
            MakeImg("CrossV", mask, StubSprites.Line(), cross, new Vector2(1.6f, diameter));
            MakeImg("CenterDot", mask, StubSprites.Circle(64f), WithAlpha(GridColor, 0.5f), Vector2.one * 3f);

            // развёртка: широкий тусклый сектор + узкий яркий (чисто визуал)
            sweep = MakeNode("Sweep", mask, Vector2.one * diameter);
            MakeImg("Wide", sweep, SweepWide(), WithAlpha(GridColor, 0.10f), Vector2.one * diameter);
            MakeImg("Narrow", sweep, SweepNarrow(), WithAlpha(GridColor, 0.22f), Vector2.one * diameter);

            // кольца дистанций от корабля: 200/400/600/800 (Н3.2, обзор 850) —
            // подписи внутри кольца сверху; метка 800 не влезает между кольцом и
            // ободом, поэтому подписаны только 200/400/600
            for (int k = 0; k < RingDistances.Length; k++)
            {
                float rPx = RingDistances[k] * pxPerUnit;
                if (rPx >= radius)
                    break; // охват перекручен — кольцо за ободом не рисуем
                MakeImg("Ring_" + (int)RingDistances[k], mask, RingSprite(),
                    WithAlpha(GridColor, 0.16f + 0.06f * k), Vector2.one * (rPx * 2f));
                // метка «С» живёт у самого обода — внешнему кольцу подпись не даём
                if (k < RingDistances.Length - 1)
                {
                    TMP_Text label = MakeLabel("Dist_" + (int)RingDistances[k], mask,
                        FormatDistance(RingDistances[k]), WithAlpha(GridColor, 0.55f));
                    RectTransform labelRect = (RectTransform)label.transform;
                    labelRect.sizeDelta = new Vector2(70f, 14f);
                    labelRect.anchoredPosition = Vector2.up * (rPx - 8f);
                }
            }

            // «север вверх»: мировая +Y всегда вверх — метка «С» у верхнего обода
            TMP_Text north = MakeLabel("North", mask, "С", WithAlpha(GridColor, 0.8f));
            RectTransform northRect = (RectTransform)north.transform;
            northRect.sizeDelta = new Vector2(18f, 14f);
            northRect.anchoredPosition = Vector2.up * (radius - 11f);

            // пояса астероидов: пунктирное кольцо по осевой линии, крутится как в мире
            RectTransform beltsRoot = MakeNode("Belts", mask, Vector2.zero);
            if (level != null)
                foreach (CelestialBodyData data in level.Bodies)
                {
                    if (data.Type != BodyType.AsteroidBelt)
                        continue;
                    float d = Mathf.Max(14f, data.Radius * 2f * pxPerUnit);
                    RectTransform ring = MakeImg("Belt_" + data.DisplayName, beltsRoot, DashRing(), BeltColor, Vector2.one * d);
                    belts.Add(new BeltMark { Data = data, Ring = ring });
                }

            bodiesRoot = MakeNode("Bodies", mask, Vector2.zero);
            foreach (CelestialBody body in gravity.Bodies)
                marks.Add(new BodyMark { Body = body, Root = BuildBodyMark(body) });

            targetRing = MakeImg("TargetRing", mask, ThickRing(), TargetColor, Vector2.one * 16f);
            targetRingImage = targetRing.GetComponent<Image>();
            targetRing.gameObject.SetActive(false);

            waypointMark = MakeImg("WaypointMark", mask, Diamond(), WaypointColor, Vector2.one * 12f);
            waypointImage = waypointMark.GetComponent<Image>();
            waypointMark.gameObject.SetActive(false);

            cometWarn = MakeImg("CometWarn", mask, StubSprites.ShipTriangle(), CometWarnColor, Vector2.one * 13f);
            cometWarn.gameObject.SetActive(false);

            // корабль: в центре прибора, самый крупный и яркий маркер —
            // ореол + кольцо-подложка + стрелка, читается раньше всех точек
            shipArrow = MakeNode("ShipArrow", mask, Vector2.zero);
            MakeImg("Glow", shipArrow, StubSprites.Circle(64f), WithAlpha(ShipColor, 0.22f), Vector2.one * 44f);
            MakeImg("Backing", shipArrow, ThickRing(), WithAlpha(ShipColor, 0.9f), Vector2.one * 30f);
            MakeImg("Arrow", shipArrow, StubSprites.ShipTriangle(), ShipColor, Vector2.one * 20f);
        }

        // Н3.2: тела — кольца по фактическому радиусу (в масштабе радара), тип
        // читается формой и цветом: планета — зелёное кольцо, звезда — крупное
        // тёплое охра-кольцо, дыра — тёмное кольцо с точкой в центре
        private RectTransform BuildBodyMark(CelestialBody body)
        {
            RectTransform root = MakeNode("Body_" + body.DisplayName, bodiesRoot, Vector2.zero);
            float trueD = body.Radius * 2f * pxPerUnit; // диаметр тела в пикселях прибора

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
            sweepAngle = (sweepAngle + SweepSpeedDeg * Time.unscaledDeltaTime) % 360f;
            sweep.localRotation = Quaternion.Euler(0f, 0f, -sweepAngle); // по часовой, как классический радар
        }

        private void UpdateBelts()
        {
            beltAngle += beltSpinDeg * Time.unscaledDeltaTime;
            for (int i = 0; i < belts.Count; i++)
            {
                belts[i].Ring.anchoredPosition = WorldToRadar(belts[i].Data.Position);
                belts[i].Ring.localRotation = Quaternion.Euler(0f, 0f, beltAngle);
            }
        }

        // обычные тела за охватом радара скрываются — на приборе только то,
        // что реально рядом с кораблём
        private void UpdateMarks()
        {
            for (int i = 0; i < marks.Count; i++)
            {
                Vector2 p = WorldToRadar(marks[i].Body.transform.position);
                bool visible = p.magnitude <= radarRadiusPx;
                if (marks[i].Root.gameObject.activeSelf != visible)
                    marks[i].Root.gameObject.SetActive(visible);
                if (visible)
                    marks[i].Root.anchoredPosition = p;
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

        private void UpdateShipArrow()
        {
            // корабль всегда в центре (позиция не меняется), вращается только нос:
            // спрайт остриём вверх, мировое направление = поворот корпуса + смещение носа спрайта
            float nose = ship.transform.eulerAngles.z + (controller != null ? controller.SpriteNoseAngle : 0f);
            shipArrow.localRotation = Quaternion.Euler(0f, 0f, nose - 90f);
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
            float a = Mathf.Sin(Time.unscaledTime * 5.5f) > 0f ? 1f : 0.3f; // мигание
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
            Vector2 p = ClampToRim(WorldToRadar(pos) + Vector2.up * 10f, out bool clamped); // над точкой тела
            waypointMark.anchoredPosition = p;
            if (waypointOnRim != clamped)
            {
                waypointOnRim = clamped;
                waypointMark.sizeDelta = Vector2.one * (clamped ? 9f : 12f);
            }
            float a = 0.65f + 0.25f * Mathf.Sin(Time.unscaledTime * 2.4f); // пульс как у маркера в мире (Н-8)
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
            float angle = Mathf.Atan2(cometWarnDirection.y, cometWarnDirection.x) * Mathf.Rad2Deg;
            cometWarn.localRotation = Quaternion.Euler(0f, 0f, angle - 90f); // спрайт остриём вверх
        }

        // хвост кометы: короткая линия назад по вектору скорости; за охватом
        // комета — просто точка на ободе, хвост прячется
        private static void UpdateCometTail(Comet comet, CometDot dot, bool clamped)
        {
            Vector2 dir = comet.FlightDirection;
            bool show = !clamped && dir.sqrMagnitude > 1e-6f;
            if (dot.Tail.gameObject.activeSelf != show)
                dot.Tail.gameObject.SetActive(show);
            if (!show)
                return;
            dot.Tail.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            dot.Tail.anchoredPosition = -dir * (CometTailPx * 0.5f);
        }

        // ————— хелперы —————

        // мир → радар: центр прибора = позиция корабля в этом кадре (shipPos),
        // масштаб фиксированный — радар «плывёт» вместе с кораблём. Север вверх
        // (Н3.2): смещение БЕЗ поворота на курс корабля — развороты не вращают
        // картину, мировая +Y всегда вверх
        private Vector2 WorldToRadar(Vector2 world) => (world - shipPos) * pxPerUnit;

        // важные цели за охватом клампятся к ободу: точка на краю прибора,
        // сама рисуется приглушённой (см. OffEdgeAlpha у вызывающих)
        private Vector2 ClampToRim(Vector2 radarPos, out bool clamped)
        {
            float edge = radarRadiusPx - RimInset;
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
