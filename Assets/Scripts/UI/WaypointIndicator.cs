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
    // Н-8: waypoint с карты — в мире и на экране. Над помеченным телом янтарная
    // скобка-луч с подписью (форма/цвет отличны от жёлтого пульса TargetGlow);
    // если waypoint или цель заказа за кадром — стрелка-компас у края экрана
    // с дистанцией. Waypoint приходит по MapScreenBinder.WaypointChanged, цель
    // заказа — по ActiveOrderChanged/PassengerBoarded (при посадке за пассажиром
    // ActiveOrderChanged не фаерится). TargetGlow OrderService не трогаем.
    public sealed class WaypointIndicator : MonoBehaviour
    {
        private const float EdgePad = 88f;     // отступ стрелки от края, единиц канваса
        private const float OverlapGap = 110f; // минимальный зазор между стрелками

        private static readonly Color Amber = new Color(1f, 0.72f, 0.25f);
        private static readonly Color AmberText = new Color(1f, 0.86f, 0.55f);
        private static readonly Color TargetGreen = new Color(0.235f, 1f, 0.557f); // как HUD/кольцо посадки
        private static readonly Color TargetText = new Color(0.66f, 1f, 0.8f);

        private GameSession session;
        private OrderService orders;
        private MapScreenBinder binder;
        private GravityField gravity;
        private Ship ship;
        private TMP_FontAsset font;
        private bool ordersBound;

        // waypoint (метка с карты) — маркер в мире
        private CelestialBodyData waypoint;
        private CelestialBody markerBody;
        private string markerBodyName;
        private Transform markerRoot;
        private SpriteRenderer beam;
        private SpriteRenderer tickTop;
        private SpriteRenderer tickBottom;
        private TextMeshPro markerLabel;

        // цель активного заказа
        private int targetIndex = -1;

        // краевые стрелки
        private Canvas arrowCanvas;
        // кэш текста дистанции стрелок: строки в LateUpdate аллоцировать нельзя
        private float lastDistWValue = -1f;
        private string lastDistWText;
        private float lastDistTValue = -1f;
        private string lastDistTText;
        private RectTransform waypointArrow;
        private RectTransform targetArrow;
        private RectTransform waypointArrowTip;
        private RectTransform targetArrowTip;
        private TMP_Text waypointArrowDist;
        private TMP_Text targetArrowDist;

        // панели-контроллеры для гашения стрелок (кэш, стиль HudBinder.UpdateDim)
        private MapScreenController mapController;
        private OnboardComputerPanel onboard;
        private StationScreenController station;
        private PauseScreenController pause;

        private void Update()
        {
            if (!Bind())
                return;
            SyncMarker();
        }

        private void LateUpdate()
        {
            if (!ordersBound)
                return;
            UpdateMarkerPose();
            UpdateArrows();
        }

        private void OnDestroy()
        {
            // маркер и канвас — корневые объекты, живут только с компонентом
            if (markerRoot != null)
                Destroy(markerRoot.gameObject);
            if (arrowCanvas != null)
                Destroy(arrowCanvas.gameObject);
        }

        // ————— привязка —————

        private bool Bind()
        {
            if (!ordersBound)
            {
                session = FindAnyObjectByType<GameSession>();
                if (session == null || session.Orders == null || session.Level == null)
                    return false;
                orders = session.Orders;
                orders.ActiveOrderChanged += RefreshTargetIndex;
                orders.PassengerBoarded += _ => RefreshTargetIndex();
                RefreshTargetIndex();
                ordersBound = true;
            }

            if (binder == null)
            {
                // биндер лежит на неактивной карте — ищем с Include (договорённость Н-1)
                binder = FindAnyObjectByType<MapScreenBinder>(FindObjectsInactive.Include);
                if (binder != null)
                {
                    binder.WaypointChanged += OnWaypointChanged;
                    waypoint = binder.Waypoint; // метка могла быть поставлена до бинда
                }
            }

            if (gravity == null)
                gravity = FindAnyObjectByType<GravityField>();
            if (ship == null)
                ship = FindAnyObjectByType<Ship>();
            if (font == null)
                font = Resources.Load<TMP_FontAsset>("Fonts/TMP/Excalifont SDF")
                    ?? Resources.Load<TMP_FontAsset>("Excalifont SDF"); // цепочка шрифта карты, иначе дефолт TMP
            return true;
        }

        private void OnWaypointChanged()
        {
            waypoint = binder != null ? binder.Waypoint : null;
        }

        private void RefreshTargetIndex()
        {
            var active = orders != null ? orders.Active : null;
            targetIndex = active == null
                ? -1
                : active.State == OrderState.Aboard ? active.DropoffIndex : active.PickupIndex;
        }

        // ————— маркер в мире —————

        private void SyncMarker()
        {
            if (waypoint == null)
            {
                markerBodyName = null; // повторная метка на то же тело собирает маркер заново
                HideMarker();
                return;
            }

            if (markerBodyName != waypoint.DisplayName)
            {
                if (gravity == null)
                    return;
                CelestialBody body = ResolveBody(waypoint.DisplayName);
                if (body == null)
                    return; // тело ещё не собрано — попробуем в следующий кадр
                markerBody = body;
                markerBodyName = waypoint.DisplayName;
                EnsureMarker();
                PlaceMarker();
            }

            if (markerRoot != null && !markerRoot.gameObject.activeSelf)
                markerRoot.gameObject.SetActive(true);
        }

        private void HideMarker()
        {
            if (markerRoot != null && markerRoot.gameObject.activeSelf)
                markerRoot.gameObject.SetActive(false);
        }

        private CelestialBody ResolveBody(string displayName)
        {
            if (gravity == null || string.IsNullOrEmpty(displayName))
                return null;
            foreach (CelestialBody body in gravity.Bodies)
                if (body.DisplayName == displayName)
                    return body;
            return null;
        }

        private void EnsureMarker()
        {
            if (markerRoot != null)
                return;
            var go = new GameObject("WaypointMarker");
            markerRoot = go.transform;
            beam = MakePart("Beam");
            tickTop = MakePart("TickTop");
            tickTop.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            tickBottom = MakePart("TickBottom");
            tickBottom.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            var labelGo = new GameObject("Name", typeof(TextMeshPro));
            labelGo.transform.SetParent(markerRoot, false);
            markerLabel = labelGo.GetComponent<TextMeshPro>();
            if (font != null)
                markerLabel.font = font;
            markerLabel.fontSize = 96f; // 3D TMP: em ≈ fontSize/10 мировых юнитов
            markerLabel.alignment = TextAlignmentOptions.Center;
            markerLabel.overflowMode = TextOverflowModes.Overflow;
            markerLabel.rectTransform.sizeDelta = new Vector2(400f, 44f);
            labelGo.GetComponent<MeshRenderer>().sortingOrder = 9;
        }

        private SpriteRenderer MakePart(string name)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(markerRoot, false);
            SpriteRenderer part = go.GetComponent<SpriteRenderer>();
            part.sprite = StubSprites.Line(); // квадрат-заглушка: форму задаёт масштаб
            part.sortingOrder = 8;
            return part;
        }

        // скобка над верхней кромкой тела + подпись; размеры в юнитах мира
        private void PlaceMarker()
        {
            float gap = markerBody.Radius + 10f;
            const float len = 26f, width = 1.8f, tick = 9f;
            beam.transform.localPosition = Vector3.up * (gap + len * 0.5f);
            beam.transform.localScale = new Vector3(width / 16f, len / 16f, 1f); // Line(): 16 юнитов
            tickTop.transform.localPosition = Vector3.up * (gap + len);
            tickTop.transform.localScale = new Vector3(width / 16f, tick / 16f, 1f);
            tickBottom.transform.localPosition = Vector3.up * gap;
            tickBottom.transform.localScale = new Vector3(width / 16f, tick / 16f, 1f);
            markerLabel.text = waypoint.DisplayName.ToUpper();
            markerLabel.rectTransform.localPosition = Vector3.up * (gap + len + 11f);
        }

        // маркер без родителя: сам догоняет тело (кометы живут, спин телу не мешает)
        private void UpdateMarkerPose()
        {
            if (markerRoot == null || !markerRoot.gameObject.activeSelf || markerBody == null)
                return;
            Vector3 p = markerBody.transform.position;
            markerRoot.position = new Vector3(p.x, p.y, 0f);

            float t = Time.time * 2.4f;
            Color beamColor = new Color(Amber.r, Amber.g, Amber.b, 0.62f + 0.28f * Mathf.Sin(t));
            beam.color = beamColor;
            tickTop.color = beamColor;
            tickBottom.color = beamColor;
            markerLabel.color = new Color(AmberText.r, AmberText.g, AmberText.b, 0.72f + 0.18f * Mathf.Sin(t));
        }

        // ————— краевые стрелки —————

        private void UpdateArrows()
        {
            Camera cam = Camera.main;
            bool canvasOn = cam != null && !IsAnyPanelOpen();
            if (!canvasOn)
            {
                if (arrowCanvas != null && arrowCanvas.enabled)
                    arrowCanvas.enabled = false; // панель погасила — не считаем вхолостую
                return;
            }
            EnsureArrowUi();
            arrowCanvas.enabled = true;

            (Vector2? pos, float radius) wp = WaypointRef();
            (Vector2? pos, float radius) tp = TargetRef();
            bool showWaypoint = ComputeEdge(wp.pos, wp.radius, cam, out Vector2 posW, out float angleW);
            string distW = showWaypoint
                ? CachedDistance(ref lastDistWValue, ref lastDistWText, Vector2.Distance(ShipPos(), wp.pos.Value))
                : null;
            bool showTarget = ComputeEdge(tp.pos, tp.radius, cam, out Vector2 posT, out float angleT);
            string distT = showTarget
                ? CachedDistance(ref lastDistTValue, ref lastDistTText, Vector2.Distance(ShipPos(), tp.pos.Value))
                : null;

            // одно направление — расталкиваем по краю, чтобы не слипались
            if (showWaypoint && showTarget)
            {
                Vector2 d = posT - posW;
                float gap = d.magnitude;
                if (gap < OverlapGap)
                {
                    Vector2 sep = gap > 0.01f ? d / gap : new Vector2(-posW.y, posW.x).normalized;
                    float push = (OverlapGap - gap) * 0.5f + 2f;
                    posW -= sep * push;
                    posT += sep * push;
                }
            }

            ApplyArrow(waypointArrow, waypointArrowTip, waypointArrowDist, showWaypoint, posW, angleW, distW);
            ApplyArrow(targetArrow, targetArrowTip, targetArrowDist, showTarget, posT, angleT, distT);
        }

        private (Vector2? pos, float radius) WaypointRef()
        {
            if (waypoint == null)
                return (null, 0f);
            if (markerBodyName == waypoint.DisplayName && markerBody != null)
                return ((Vector2)markerBody.transform.position, markerBody.Radius);
            return (waypoint.Position, waypoint.Radius);
        }

        private (Vector2? pos, float radius) TargetRef()
        {
            LevelAsset level = session != null ? session.Level : null;
            if (targetIndex < 0 || level == null || targetIndex >= level.Bodies.Count)
                return (null, 0f);
            CelestialBodyData data = level.Bodies[targetIndex];
            CelestialBody body = ResolveBody(data.DisplayName);
            return body != null ? ((Vector2)body.transform.position, body.Radius) : (data.Position, data.Radius);
        }

        // кламп направления к прямоугольнику экрана с отступом; false — цель видна
        private bool ComputeEdge(Vector2? worldPoint, float radius, Camera cam, out Vector2 canvasPos, out float angle)
        {
            canvasPos = Vector2.zero;
            angle = 0f;
            if (worldPoint == null || cam == null)
                return false;

            Vector2 delta = worldPoint.Value - (Vector2)cam.transform.position;
            // тряска крутит камеру до ~±2.5° (CameraFollow) — канвас при этом
            // не вращается, поэтому дельту приводим к системе экрана заранее
            float roll = cam.transform.eulerAngles.z;
            if (roll > 0.01f || roll < -0.01f)
            {
                float rad = -roll * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);
                delta = new Vector2(delta.x * cos - delta.y * sin, delta.x * sin + delta.y * cos);
            }
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            // тело краем уже в кадре — стрелка не нужна
            float cx = Mathf.Clamp(delta.x, -halfW, halfW);
            float cy = Mathf.Clamp(delta.y, -halfH, halfH);
            float ex = delta.x - cx;
            float ey = delta.y - cy;
            if (ex * ex + ey * ey <= radius * radius)
                return false;

            // мир → единицы канваса: высота экрана = 2*orthographicSize юнитов мира
            float scale = arrowCanvas.scaleFactor;
            float unitsPerPx = 2f * halfH / Mathf.Max(1, Screen.height);
            Vector2 d = delta / (unitsPerPx * scale);
            float mx = Mathf.Max(Screen.width / scale * 0.5f - EdgePad, 1f);
            float my = Mathf.Max(Screen.height / scale * 0.5f - EdgePad, 1f);
            float tx = Mathf.Abs(d.x) > mx ? mx / Mathf.Abs(d.x) : float.MaxValue;
            float ty = Mathf.Abs(d.y) > my ? my / Mathf.Abs(d.y) : float.MaxValue;
            float t = Mathf.Min(tx, ty);
            if (t == float.MaxValue) // центр в кадре — отсечено проверкой выше
                return false;
            canvasPos = d * t;
            angle = Mathf.Atan2(canvasPos.y, canvasPos.x) * Mathf.Rad2Deg;
            return true;
        }

        private void ApplyArrow(RectTransform root, RectTransform tip, TMP_Text distLabel,
            bool show, Vector2 pos, float angle, string distText)
        {
            if (root == null)
                return;
            if (root.gameObject.activeSelf != show)
                root.gameObject.SetActive(show);
            if (!show)
                return;
            root.anchoredPosition = pos;
            tip.localRotation = Quaternion.Euler(0f, 0f, angle - 90f); // спрайт остриём вверх
            if (distLabel.text != distText)
                distLabel.text = distText;
        }

        private void EnsureArrowUi()
        {
            if (arrowCanvas != null)
                return;
            var go = new GameObject("WaypointArrows", typeof(Canvas), typeof(CanvasScaler));
            arrowCanvas = go.GetComponent<Canvas>();
            arrowCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            arrowCanvas.sortingOrder = 30; // поверх HUD, под станцией/модалами
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f); // как UI_Root
            scaler.matchWidthOrHeight = 0.5f;

            waypointArrow = MakeArrow("WaypointArrow", Amber, AmberText,
                out waypointArrowTip, out waypointArrowDist);
            targetArrow = MakeArrow("TargetArrow", TargetGreen, TargetText,
                out targetArrowTip, out targetArrowDist);
        }

        // без GraphicRaycaster: канвас ничего не перехватывает
        private RectTransform MakeArrow(string name, Color color, Color textColor,
            out RectTransform tip, out TMP_Text dist)
        {
            var rootGo = new GameObject(name, typeof(RectTransform));
            rootGo.transform.SetParent(arrowCanvas.transform, false);
            RectTransform root = (RectTransform)rootGo.transform;
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(150f, 116f);

            var tipGo = new GameObject("Tip", typeof(RectTransform), typeof(Image));
            tipGo.transform.SetParent(root, false);
            tip = (RectTransform)tipGo.transform;
            tip.sizeDelta = new Vector2(56f, 56f);
            tip.anchoredPosition = new Vector2(0f, 12f);
            Image tipImage = tipGo.GetComponent<Image>();
            tipImage.sprite = StubSprites.ShipTriangle();
            tipImage.color = color;
            tipImage.raycastTarget = false;

            var distGo = new GameObject("Dist", typeof(RectTransform), typeof(TextMeshProUGUI));
            distGo.transform.SetParent(root, false);
            dist = distGo.GetComponent<TextMeshProUGUI>();
            if (font != null)
                dist.font = font;
            dist.fontSize = 22f;
            dist.color = textColor;
            dist.alignment = TextAlignmentOptions.Center;
            dist.raycastTarget = false;
            RectTransform distRect = dist.rectTransform;
            distRect.anchorMin = new Vector2(0.5f, 0.5f);
            distRect.anchorMax = new Vector2(0.5f, 0.5f);
            distRect.sizeDelta = new Vector2(150f, 26f);
            distRect.anchoredPosition = new Vector2(0f, -32f);
            return root;
        }

        private bool IsAnyPanelOpen()
        {
            if (mapController == null) mapController = FindAnyObjectByType<MapScreenController>();
            if (onboard == null) onboard = FindAnyObjectByType<OnboardComputerPanel>();
            if (station == null) station = FindAnyObjectByType<StationScreenController>();
            if (pause == null) pause = FindAnyObjectByType<PauseScreenController>();
            return (mapController != null && mapController.IsOpened)
                || (onboard != null && onboard.IsShown)
                || (station != null && station.IsOpened)
                || (pause != null && pause.IsPaused);
        }

        private Vector2 ShipPos()
        {
            return ship != null ? (Vector2)ship.transform.position : Vector2.zero;
        }

        private static string FormatDistance(float d)
        {
            if (d < 1000f)
                return ((int)d).ToString();
            return (d / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        }

        // пересчёт текста только при изменении дистанции на ≥10 — без аллокаций в кадр
        private static string CachedDistance(ref float lastValue, ref string lastText, float d)
        {
            if (lastText == null || Mathf.Abs(d - lastValue) >= 10f)
            {
                lastValue = d;
                lastText = FormatDistance(d);
            }
            return lastText;
        }
    }
}
