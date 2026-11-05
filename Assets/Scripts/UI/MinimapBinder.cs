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
    // Н-12: круглый радар-прибор вместо ближней мини-карты. Весь мир в масштабе
    // WorldRadius (навигационный прибор): кольца дистанций с подписями, крест
    // направлений, луч развёртки (чисто визуал), тела по типам, станции цветом
    // топливной сети (тир из Н-4), мигающая цель заказа, waypoint с большой
    // карты (Н-1), кометы и предупреждение о входе. Корабль — стрелка по носу.
    // Стиль «прибора»: тёмный фон, зелёные линии, свечение — полупрозрачные
    // спрайты без шейдеров. Вся геометрия строится кодом, сцена не трогается.
    public class MinimapBinder : MonoBehaviour
    {
        private const float SweepSpeedDeg = 72f;  // скорость луча развёртки
        private const int BuoyCount = 12;         // буи связи по границе мира
        private const float RebuildWidthEps = 2f; // пересборка при заметной смене ширины

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
        private static readonly Color ShipColor = new Color(0.8f, 1f, 0.87f);
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

        private struct CometDot
        {
            public Comet Comet;
            public RectTransform Dot;
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
        private float pxPerUnit;
        private float worldRadius;  // фактический охват: граница мира с запасом по телам/поясам
        private float sweepAngle;
        private float beltAngle;
        private float beltSpinDeg = 0.12f * Mathf.Rad2Deg;
        private float cometWarnUntil;
        private Vector2 cometWarnEntry;
        private Vector2 cometWarnDirection;
        private bool cometsBound;

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
        private readonly List<CometDot> cometDots = new List<CometDot>();

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

            UpdateSweep();
            UpdateBelts();
            UpdateMarks();
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
            marks.Clear();
            belts.Clear();
            cometDots.Clear();
        }

        private void Build(float diameter)
        {
            float radius = diameter * 0.5f;

            // охват: граница мира (порог HullSystem) с запасом по телам и поясам
            float worldR = level != null ? level.WorldRadius : 2500f;
            foreach (CelestialBody body in gravity.Bodies)
            {
                float edge = ((Vector2)body.transform.position).magnitude + body.Radius;
                if (edge > worldR)
                    worldR = edge + 60f;
            }
            if (level != null)
                foreach (CelestialBodyData data in level.Bodies)
                {
                    if (data.Type != BodyType.AsteroidBelt)
                        continue;
                    float edge = data.Position.magnitude + data.Radius + Mathf.Max(0f, data.BeltWidth) * 0.5f;
                    if (edge > worldR)
                        worldR = edge + 60f;
                }
            worldRadius = worldR;
            pxPerUnit = radius / worldRadius;

            AsteroidBeltSystem beltSystem = FindAnyObjectByType<AsteroidBeltSystem>();
            beltSpinDeg = (beltSystem != null ? beltSystem.BeltOrbitSpeed : 0.12f) * Mathf.Rad2Deg;

            chrome = MakeStretch("Radar", (RectTransform)transform);

            Color bezel = WithAlpha(GridColor, 0.9f);
            MakeImg("BezelGlow", chrome, RingSprite(), WithAlpha(GridColor, 0.13f), Vector2.one * (diameter * 1.09f));
            MakeImg("Bezel", chrome, RingSprite(), bezel, Vector2.one * diameter);

            // буи связи — точки по границе мира (та же сеть, что на карте Н-1)
            for (int i = 0; i < BuoyCount; i++)
            {
                float ang = i * 360f / BuoyCount * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                RectTransform buoy = MakeImg("Buoy_" + i, chrome, StubSprites.Circle(64f), BuoyColor, Vector2.one * 5f);
                buoy.anchoredPosition = dir * radius;
            }

            // маска-круг: вся начинка прибора живёт внутри обода
            RectTransform mask = MakeStretch("Mask", chrome);
            Image maskImage = mask.gameObject.AddComponent<Image>();
            maskImage.sprite = StubSprites.Circle(diameter);
            maskImage.raycastTarget = false;
            Mask maskComponent = mask.gameObject.AddComponent<Mask>();
            maskComponent.showMaskGraphic = false;

            MakeStretchImg("Background", mask, StubSprites.Circle(diameter), PanelColor);

            // крест направлений + точка начала координат мира
            Color cross = WithAlpha(GridColor, 0.12f);
            MakeImg("CrossH", mask, StubSprites.Line(), cross, new Vector2(diameter, 1.6f));
            MakeImg("CrossV", mask, StubSprites.Line(), cross, new Vector2(1.6f, diameter));
            MakeImg("CenterDot", mask, StubSprites.Circle(64f), WithAlpha(GridColor, 0.5f), Vector2.one * 3f);

            // развёртка: широкий тусклый сектор + узкий яркий (чисто визуал)
            sweep = MakeNode("Sweep", mask, Vector2.one * diameter);
            MakeImg("Wide", sweep, SweepWide(), WithAlpha(GridColor, 0.10f), Vector2.one * diameter);
            MakeImg("Narrow", sweep, SweepNarrow(), WithAlpha(GridColor, 0.22f), Vector2.one * diameter);

            // кольца дистанций: 2 внутренних + граница мира (обод) — подписи мелко
            for (int k = 1; k <= 3; k++)
            {
                float rPx = radius * k / 3f;
                if (k < 3)
                    MakeImg("Ring_" + k, mask, RingSprite(),
                        WithAlpha(GridColor, 0.22f + 0.14f * (k - 1)), Vector2.one * (rPx * 2f));
                float dist = Mathf.RoundToInt(worldRadius * k / 3f / 10f) * 10f;
                TMP_Text label = MakeLabel("Dist_" + k, mask, FormatDistance(dist), WithAlpha(GridColor, 0.55f));
                RectTransform labelRect = (RectTransform)label.transform;
                labelRect.sizeDelta = new Vector2(70f, 14f);
                labelRect.anchoredPosition = Vector2.up * (k < 3 ? rPx + 7f : radius - 10f);
            }

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

            shipArrow = MakeNode("ShipArrow", mask, Vector2.zero);
            MakeImg("Glow", shipArrow, StubSprites.Circle(64f), WithAlpha(ShipColor, 0.16f), Vector2.one * 24f);
            MakeImg("Arrow", shipArrow, StubSprites.ShipTriangle(), ShipColor, Vector2.one * 13f);
        }

        private RectTransform BuildBodyMark(CelestialBody body)
        {
            RectTransform root = MakeNode("Body_" + body.DisplayName, bodiesRoot, Vector2.zero);
            float dot = Mathf.Clamp(body.Radius * 2f * pxPerUnit, 5f, 9f);

            switch (body.Type)
            {
                case BodyType.Star:
                    Halo(root, Mathf.Max(dot * 2.1f, 13f), StarColor, 0.16f);
                    Dot(root, Mathf.Max(dot, 6f), StarColor);
                    break;
                case BodyType.BlackHole:
                    // тёмное ядро + фиолетовый обвод «горизонта событий», как на карте
                    Halo(root, Mathf.Max(dot * 2.6f, 16f), HoleHaloColor, 1f);
                    RingMark(root, Mathf.Max(dot * 1.5f, 10f), WithAlpha(HoleHaloColor, 0.55f));
                    Dot(root, Mathf.Max(dot * 0.5f, 4f), HoleCoreColor);
                    break;
                default:
                    BuildStationMark(root, body, dot);
                    break;
            }
            return root;
        }

        // станция = точка цвета топливной сети (тир задан — цвет по тиру, иначе
        // жёлтая); мастерская без заправки — стальная; хаб — двойное кольцо
        private void BuildStationMark(RectTransform root, CelestialBody body, float dot)
        {
            if (body.IsHub)
            {
                Color c = FuelTierColor(body.FuelTier);
                Halo(root, 20f, c, 0.16f);
                RingMark(root, 17f, WithAlpha(c, 0.35f));
                RingMark(root, 13f, WithAlpha(c, 0.85f));
                Dot(root, 7f, c);
            }
            else if (body.HasFuel)
            {
                Color c = FuelTierColor(body.FuelTier);
                Halo(root, 13f, c, 0.15f);
                RingMark(root, 11f, WithAlpha(c, 0.7f));
                Dot(root, 6f, c);
            }
            else if (body.HasRepair)
            {
                Halo(root, 13f, RepairColor, 0.12f);
                RingMark(root, 11f, WithAlpha(RepairColor, 0.6f));
                Dot(root, 6f, RepairColor);
            }
            else
            {
                Halo(root, dot * 2.2f, PlanetColor, 0.12f);
                Dot(root, dot, PlanetColor);
            }
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

        private void UpdateMarks()
        {
            for (int i = 0; i < marks.Count; i++)
                marks[i].Root.anchoredPosition = WorldToRadar(marks[i].Body.transform.position);
        }

        private void UpdateShipArrow()
        {
            shipArrow.anchoredPosition = WorldToRadar(ship.transform.position);
            // нос: спрайт остриём вверх, мировое направление = поворот корпуса + смещение носа спрайта
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
            targetRing.anchoredPosition = WorldToRadar(pos);
            float a = Mathf.Sin(Time.unscaledTime * 5.5f) > 0f ? 1f : 0.3f; // мигание
            targetRingImage.color = WithAlpha(TargetColor, a);
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
            waypointMark.anchoredPosition = WorldToRadar(pos) + Vector2.up * 10f; // над точкой тела
            float a = 0.65f + 0.25f * Mathf.Sin(Time.unscaledTime * 2.4f); // пульс как у маркера в мире (Н-8)
            waypointImage.color = WithAlpha(WaypointColor, a);
        }

        private void UpdateComets()
        {
            if (cometSystem == null)
                return;

            // живые кометы — голубые точки по ActiveComets; мёртвые записи чистим
            for (int i = cometDots.Count - 1; i >= 0; i--)
            {
                if (cometDots[i].Comet == null)
                {
                    if (cometDots[i].Dot != null)
                        Destroy(cometDots[i].Dot.gameObject);
                    cometDots.RemoveAt(i);
                }
                else
                {
                    cometDots[i].Dot.anchoredPosition = WorldToRadar(cometDots[i].Comet.transform.position);
                }
            }
            foreach (Comet comet in cometSystem.ActiveComets)
            {
                if (comet == null || cometDots.Exists(d => d.Comet == comet))
                    continue;
                RectTransform dot = MakeImg("CometDot", bodiesRoot, StubSprites.Circle(64f), Comet.CometColor, Vector2.one * 5f);
                cometDots.Add(new CometDot { Comet = comet, Dot = dot });
            }

            // предупреждение: остриё по направлению входа, кламп к краю радара;
            // таймер — страховка на случай пропущенного CometSpawned
            bool showWarn = Time.time < cometWarnUntil;
            if (cometWarn.gameObject.activeSelf != showWarn)
                cometWarn.gameObject.SetActive(showWarn);
            if (!showWarn)
                return;
            Vector2 offset = WorldToRadar(cometWarnEntry);
            float edge = ((RectTransform)transform).rect.width * 0.5f - 8f;
            if (offset.magnitude > edge)
                offset = offset.normalized * edge;
            cometWarn.anchoredPosition = offset;
            float angle = Mathf.Atan2(cometWarnDirection.y, cometWarnDirection.x) * Mathf.Rad2Deg;
            cometWarn.localRotation = Quaternion.Euler(0f, 0f, angle - 90f); // спрайт остриём вверх
        }

        // ————— хелперы —————

        // мир → радар: центр прибора = начало координат мира (от него же граница HullSystem)
        private Vector2 WorldToRadar(Vector2 world) => world * pxPerUnit;

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

        // точка тела
        private static void Dot(RectTransform parent, float size, Color color)
        {
            MakeImg("Dot", parent, StubSprites.Circle(64f), color, Vector2.one * size);
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
