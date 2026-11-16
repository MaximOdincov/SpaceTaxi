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
    // Н-8/G-15: waypoint с карты — в мире и на экране. Над помеченным телом
    // зелёная компактная скобка-луч с подписью (цвет — своя метка, как на
    // карте и радаре; форма отлична от жёлтого пульса TargetGlow цели заказа);
    // если waypoint или цель заказа за кадром — стрелка-компас у края экрана
    // с дистанцией (зелёная — своя метка, жёлтая — цель заказа). Waypoint
    // приходит по MapScreenBinder.WaypointChanged, цель заказа — по
    // ActiveOrderChanged/PassengerBoarded (при посадке за пассажиром
    // ActiveOrderChanged не фаерится). TargetGlow OrderService не трогаем.
    public sealed class WaypointIndicator : MonoBehaviour
    {
        private const float EdgePad = 88f;     // отступ стрелки от края, единиц канваса
        private const float OverlapGap = 110f; // минимальный зазор между стрелками

        // M8.9: габарит скобки метки НАД телом (геометрия PlaceMarker):
        // зазор Radius+10 + луч 21 + подпись центр +11 и полвысоты 22 = +64,
        // низ скобки = Radius+10. У верхнего края кадра скобка резалась, а
        // стрелка-компас пряталась (диск тела касался кадра) — маркер просто
        // исчезал. Теперь маркер гасится, когда не влезает целиком, и
        // направление показывает честная стрелка (ComputeEdge с той же
        // надбавкой) — «отключение за границей»
        private const float MarkerTopPad = 64f;
        private const float MarkerBottomPad = 10f;

        // своя метка (waypoint с карты) — зелёная (G-15): единый цвет с ромбом на
        // радаре; на бумажной карте тот же смысл — тёмно-карандашный GreenPencil
        public static readonly Color WaypointGreen = new Color(0.235f, 1f, 0.557f);
        private static readonly Color WaypointGreenText = new Color(0.62f, 1f, 0.78f);
        private static readonly Color AmberText = new Color(1f, 0.86f, 0.55f);
        // цель заказа — жёлтый проектного такси (Н4.2.3), жёлтым и остаётся (G-15)
        private static readonly Color TargetYellow = OnboardComputerFactory.TaxiColor;

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

        // v5.2 (владелец: «ореол не линией, а кругом, сзади планеты чуть больше
        // неё; и не только у заказов — у целей, выбранных на карте, там он
        // зелёный»): мировой пульсирующий ореол — МЯГКИЙ ДИСК (SoftDot, а не
        // тонкое кольцо) ПОД спрайтом планеты, чуть больше планеты; два
        // инстанса — жёлтый у цели заказа, зелёный у тела, выбранного на карте
        // (MapScreenBinder.Waypoint). Видны, когда планета в кадре (стрелка у
        // края гаснет — они дополняют друг друга)
        private GameObject targetHalo;
        private SpriteRenderer targetHaloRenderer;
        private GameObject waypointHalo;
        private SpriteRenderer waypointHaloRenderer;
        private bool targetInFrame;
        private bool waypointInFrame;
        private GravityField haloGravity; // ленивый бинд для ореола цели (см. UpdateTargetHalo)

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
            UpdateTargetHalo();
            UpdateWaypointHalo();
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

            if (markerRoot != null)
            {
                // M8.9: маркер в мире показываем только если он влезает в кадр
                // ЦЕЛИКОМ (скобка+подпись над телом), иначе — гасим, направление
                // держит стрелка-компас (см. ComputeEdge)
                bool fits = MarkerFitsFrame();
                if (markerRoot.gameObject.activeSelf != fits)
                    markerRoot.gameObject.SetActive(fits);
            }
        }

        // влезает ли скобка метки в кадр камеры целиком (см. MarkerTopPad)
        private bool MarkerFitsFrame()
        {
            if (markerBody == null)
                return true;
            Camera cam = Camera.main;
            if (cam == null)
                return true;
            Vector2 delta = markerBody.transform.position - cam.transform.position;
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            float r = markerBody.Radius;
            return Mathf.Abs(delta.x) <= halfW
                && delta.y + r + MarkerBottomPad >= -halfH
                && delta.y + r + MarkerTopPad <= halfH;
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
            markerLabel.fontSize = 84f; // 3D TMP: em ≈ fontSize/10 мировых юнитов; компактнее (G-15)
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

        // скобка над верхней кромкой тела + подпись; размеры в юнитах мира.
        // G-15: компактнее прежнего — зелёная метка не спорит с жёлтой целью
        private void PlaceMarker()
        {
            float gap = markerBody.Radius + 10f;
            const float len = 21f, width = 1.6f, tick = 7f;
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
            // зелёная метка (G-15) с лёгким пульсом: альфа гуляет слабее прежнего
            Color beamColor = new Color(WaypointGreen.r, WaypointGreen.g, WaypointGreen.b, 0.72f + 0.14f * Mathf.Sin(t));
            beam.color = beamColor;
            tickTop.color = beamColor;
            tickBottom.color = beamColor;
            markerLabel.color = new Color(WaypointGreenText.r, WaypointGreenText.g, WaypointGreenText.b, 0.78f + 0.12f * Mathf.Sin(t));
        }

        // ————— краевые стрелки —————

        private void UpdateArrows()
        {
            Camera cam = Camera.main;
            bool canvasOn = cam != null && !IsAnyPanelOpen();
            if (!canvasOn)
            {
                targetInFrame = false;
                waypointInFrame = false;
                if (arrowCanvas != null && arrowCanvas.enabled)
                    arrowCanvas.enabled = false; // панель погасила — не считаем вхолостую
                return;
            }
            EnsureArrowUi();
            arrowCanvas.enabled = true;

            (Vector2? pos, float radius) wp = WaypointRef();
            (Vector2? pos, float radius) tp = TargetRef();
            bool showWaypoint = ComputeEdge(wp.pos, wp.radius, cam, true, out Vector2 posW, out float angleW);
            waypointInFrame = canvasOn && wp.pos.HasValue && !showWaypoint;
            string distW = showWaypoint
                ? CachedDistance(ref lastDistWValue, ref lastDistWText, Vector2.Distance(ShipPos(), wp.pos.Value))
                : null;
            bool showTarget = ComputeEdge(tp.pos, tp.radius, cam, false, out Vector2 posT, out float angleT);
            targetInFrame = canvasOn && tp.pos.HasValue && !showTarget;
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

        // кламп направления к прямоугольнику экрана с отступом; false — цель видна.
        // bracketAbove: у маркера есть скобка с подписью НАД телом (метка) —
        // стрелка появляется и когда тело видно, но скобка резанётся краем
        // (M8.9), цель стрелки — верхняя точка маркера
        private bool ComputeEdge(Vector2? worldPoint, float radius, Camera cam, bool bracketAbove,
            out Vector2 canvasPos, out float angle)
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
            // тело краем уже в кадре — стрелка не нужна (M8.9: кроме случая,
            // когда скобка с подписью резанётся краем кадра)
            float cx = Mathf.Clamp(delta.x, -halfW, halfW);
            float cy = Mathf.Clamp(delta.y, -halfH, halfH);
            float ex = delta.x - cx;
            float ey = delta.y - cy;
            bool bodyInFrame = ex * ex + ey * ey <= radius * radius;
            bool bracketFits = !bracketAbove
                || (Mathf.Abs(delta.x) <= halfW
                    && delta.y + radius + MarkerBottomPad >= -halfH
                    && delta.y + radius + MarkerTopPad <= halfH);
            if (bodyInFrame && bracketFits)
                return false;
            if (bodyInFrame) // видно тело, но скобка резанётся — стрелка на её верх
                delta.y += radius + MarkerTopPad;

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

            waypointArrow = MakeArrow("WaypointArrow", WaypointGreen, WaypointGreenText,
                out waypointArrowTip, out waypointArrowDist);
            targetArrow = MakeArrow("TargetArrow", TargetYellow, AmberText,
                out targetArrowTip, out targetArrowDist);
        }

        // Н6 U3: спрайт владельца svrn.png — чисто белый (PNG-анализ: все
        // непрозрачные пиксели 255,255,255) и остриём вверх, как прежний
        // процедурный шеврон, поэтому мультипликативная тонировка Image.color
        // работает напрямую — перекрас в белое не нужен. Текстура импортирована
        // Multiple (два одинаковых шеврона svrn_0/svrn_1) — берём верхний
        // суб-спрайт через LoadAll (паттерн for_mini у радара). Публично:
        // маркер игрока на карте (Н4.6.2) красит этот же спрайт.
        // Фолбэк — прежняя цепочка: чёрный «Шеврон» перекрашивается в белое,
        // при нечитаемой текстуре — StubSprites.
        private static Sprite chevronSprite;

        public static Sprite ChevronSprite()
        {
            if (chevronSprite != null)
                return chevronSprite;
            // v5.2 (владелец перевёл импорт svrn.png Multiple → Single — нарезка
            // кадров резала концы шеврона): первичный источник — обычный svrn
            // (единый спрайт целиком); фолбэки: LoadAll (старый Multiple),
            // Шеврон.png (полый контур), StubSprites
            var svrn = Resources.Load<Sprite>("Art/icons/svrn");
            if (svrn != null)
            {
                chevronSprite = svrn;
                return chevronSprite;
            }
            var frames = Resources.LoadAll<Sprite>("Art/icons/svrn");
            if (frames != null && frames.Length > 0)
            {
                foreach (var frame in frames)
                    if (frame != null && frame.name == "svrn_0")
                        return frame;
                return frames[0];
            }
            var texture = Resources.Load<Texture2D>("Art/icons/Шеврон");
            if (texture == null)
                return null;
            try
            {
                var pixels = texture.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(255, 255, 255, pixels[i].a);
                var white = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                white.SetPixels32(pixels);
                white.Apply(true, false);
                chevronSprite = Sprite.Create(white, new Rect(0f, 0f, white.width, white.height),
                    new Vector2(0.5f, 0.5f), 100f);
            }
            catch (UnityException)
            {
                // Шеврон нечитаем (импортер без isReadable) — процедурный фолбэк
                return null; // StubSprites.TargetChevron
            }
            return chevronSprite;
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
            Sprite chevron = ChevronSprite(); // иконка Шеврон.png из Art/icons (v5.1)
            tipImage.sprite = chevron != null ? chevron : StubSprites.TargetChevron();
            tipImage.preserveAspect = true; // спрайт не сплющивается в квадрат tip'а
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

        // пульсирующий ореол под планетой: сортировка -1 (за спрайтом планеты
        // 0/1, над звёздами -10), МЯГКИЙ ДИСК чуть больше планеты (диаметр
        // ×1.28, у SoftDot мягкая кромка — видимое свечение-обод вокруг диска
        // планеты), пульс размером и альфой
        private void UpdateHalo(ref GameObject haloGo, ref SpriteRenderer haloRenderer,
            CelestialBody body, Color color, bool visible, string name)
        {
            if (!visible || body == null)
            {
                if (haloGo != null && haloGo.activeSelf)
                    haloGo.SetActive(false);
                return;
            }

            if (haloGo == null)
            {
                haloGo = new GameObject(name, typeof(SpriteRenderer));
                haloRenderer = haloGo.GetComponent<SpriteRenderer>();
                haloRenderer.sprite = StubSprites.SoftDot(256f, 0.82f, 1024); // гладкий край: плотность текстуры 1024
                haloRenderer.sortingOrder = -1;
            }
            if (!haloGo.activeSelf)
                haloGo.SetActive(true);
            haloGo.transform.position = body.transform.position;
            // StubSprites.SoftDot запекает диаметр в PPU (256 = 256 юнитов при
            // SoftDot(256,...)) — итоговый размер = 256 × localScale
            float baseD = body.Radius * 2.56f;
            float t = Time.unscaledTime;
            haloGo.transform.localScale = Vector3.one * (baseD / 256f * (1f + 0.08f * Mathf.Sin(t * 4.2f)));
            haloRenderer.color = new Color(color.r, color.g, color.b, 0.55f + 0.3f * Mathf.Sin(t * 3.4f));
        }

        private void UpdateTargetHalo()
        {
            CelestialBody body = null;
            var lvl = session != null ? session.Level : null;
            // ResolveBody зависит от gravity (инициализируется только меткой) —
            // ореолу цели нужен СВОЙ ленивый поиск по GravityField
            if (targetInFrame && lvl != null && targetIndex >= 0 && targetIndex < lvl.Bodies.Count)
            {
                if (haloGravity == null)
                    haloGravity = FindAnyObjectByType<GravityField>();
                if (haloGravity != null && haloGravity.Bodies != null)
                    foreach (CelestialBody b in haloGravity.Bodies)
                        if (b.DisplayName == lvl.Bodies[targetIndex].DisplayName)
                        {
                            body = b;
                            break;
                        }
            }
            UpdateHalo(ref targetHalo, ref targetHaloRenderer, body, TargetYellow, targetInFrame, "TargetHalo");
        }

        // зелёный ореол — у тела, ВЫБРАННОГО на карте (своя метка, G-15)
        private void UpdateWaypointHalo()
        {
            UpdateHalo(ref waypointHalo, ref waypointHaloRenderer, markerBody,
                WaypointGreen, waypointInFrame, "WaypointHalo");
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
