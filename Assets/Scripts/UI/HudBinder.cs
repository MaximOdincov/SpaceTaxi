using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Audio;
using SpaceTaxi.Core;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // HUD: скорость, прочность, топливо сегментами, баннер тревоги
    public class HudBinder : MonoBehaviour
    {
        private TMP_Text speedValue;
        private TMP_Text hullValue;
        private Image[] fuelSegments = new Image[10];
        private GameObject warningBanner;
        private TMP_Text warningText;
        private TMP_Text goalHint;
        private SpeedometerGauge gauge;
        private Image sirenLeft, sirenRight;
        private bool alarmOn;

        [SerializeField] private TMP_FontAsset font; // JetBrains Mono из сцены

        private Ship ship;
        private GravityField gravity;
        private ShipController controller;
        private bool bound;
        private Color speedBaseColor;

        // гашение HUD при открытых панелях (П11.1): тост внутри HUD не гаснет —
        // у него своя группа с ignoreParentGroups
        private const float DimmedAlpha = 0.35f;
        private const float DimFadeSeconds = 0.15f;
        private CanvasGroup dimGroup;
        private MapScreenController mapScreen;
        private OnboardComputerPanel onboard;
        private StationScreenController station;
        private float currentAlpha = 1f;

        private static readonly Color SegmentOn = new Color(0.235f, 1f, 0.557f, 1f);
        private static readonly Color SegmentOff = new Color(0.059f, 0.2f, 0.125f, 0.824f);
        private static readonly Color SpeedSafe = new Color(0.235f, 1f, 0.557f, 1f);
        private static readonly Color SpeedDanger = new Color(1f, 0.35f, 0.3f, 1f);
        // зелёный HUD-иконок — BrightColor каркаса борткомпа (принятый зелёный)
        private static readonly Color IconGreen = OnboardComputerFactory.BrightColor;

        private void Awake()
        {
            dimGroup = GetComponent<CanvasGroup>();
            if (dimGroup == null)
                dimGroup = gameObject.AddComponent<CanvasGroup>();

            speedValue = transform.Find("SpeedValue").GetComponent<TMP_Text>();
            hullValue = transform.Find("HullValue").GetComponent<TMP_Text>();
            for (int i = 0; i < fuelSegments.Length; i++)
                fuelSegments[i] = transform.Find($"FuelTrack/FuelSeg_{i + 1:00}").GetComponent<Image>();
            warningBanner = transform.Find("WarningBanner").gameObject;
            warningText = warningBanner.transform.Find("Text").GetComponent<TMP_Text>();
            warningText.fontSize = 36f; // тревога должна пугать издалека
            warningText.enableAutoSizing = true; // текст не вылезает из подложки
            warningText.fontSizeMin = 20f;
            warningText.fontSizeMax = 36f;
            var bannerRect = warningBanner.transform as RectTransform;
            if (bannerRect != null)
            {
                bannerRect.sizeDelta = new Vector2(bannerRect.sizeDelta.x * 1.3f, bannerRect.sizeDelta.y * 1.25f);
                // №19: тревога убрана из центра UI — баннер переезжает на низ экрана
                // (сверху стопка тостов и подсказка цели); текст, пульс, сирены
                // и звук сирены остаются как были
                bannerRect.anchorMin = new Vector2(0.5f, 0f);
                bannerRect.anchorMax = new Vector2(0.5f, 0f);
                bannerRect.pivot = new Vector2(0.5f, 0f);
                bannerRect.anchoredPosition = new Vector2(0f, 16f);
            }
            speedBaseColor = speedValue.color;

            // подсказка текущей цели
            var hintGo = new GameObject("GoalHint", typeof(RectTransform), typeof(TextMeshProUGUI));
            hintGo.transform.SetParent(transform, false);
            goalHint = hintGo.GetComponent<TextMeshProUGUI>();
            goalHint.font = font;
            goalHint.fontSize = 22f;
            goalHint.color = new Color(0.66f, 1f, 0.8f);
            goalHint.alignment = TextAlignmentOptions.Center;
            var hintRect = goalHint.rectTransform;
            hintRect.anchorMin = new Vector2(0.5f, 1f);
            hintRect.anchorMax = new Vector2(0.5f, 1f);
            hintRect.sizeDelta = new Vector2(900f, 30f);
            hintRect.anchoredPosition = new Vector2(0f, -156f); // ниже тоста

            BuildSpeedometer();
            BuildStatIcons();
            BuildSirens();
        }

        // ————— геометрия спидометра (каркас строится кодом, префаб удалён) —————

        private const float GaugeSize = 55f;      // слот как у соседних иконок статов (FuelIcon/HullIcon 55×55)
        private const float NeedleScale = 0.85f;  // остриё 23.4 < радиуса шкалы 27.5 — не вылезает
        private const float GaugeTopGap = 12f;    // зазор между шкалой и строкой топлива (опущена ниже по Н4.13)
        private const float GaugeHullGap = 6f;    // зазор между стрелкой и строкой корпуса
        // Ход стрелки: 240° из допустимых брифом 180..270. Хвост arrow.png в z=0
        // смотрит вправо-вниз (≈ −12°), −78° ставит его строго вниз; дальше — по
        // часовой: вниз → влево → вверх → до упора вправо-вверх у MaxV.
        private const float NeedleAngleAtZero = -78f;
        private const float NeedleSweep = 240f;
        // Хаб стрелки (центр белого шара) в пикселях arrow.png 41×28 — вокруг
        // него делаем пивот, шар сидит на оси шкалы
        private static readonly Vector2 NeedleHubPx = new Vector2(13.5f, 13.5f);
        private static readonly Vector2 NeedleSpritePx = new Vector2(41f, 28f);

        // спидометр: шкала speed.png (полукруг) + стрелка arrow.png ПОД ней,
        // вращается по скорости; цифры — внутри шкалы. Место: слева между строкой
        // топлива и строкой корпуса — корпус сдвигается вниз, старая иконка
        // скорости прячется (Н4.2.1: код вместо битого префаба UI/Speedometer)
        private void BuildSpeedometer()
        {
            var gaugeGo = new GameObject("Speedometer", typeof(RectTransform), typeof(SpeedometerGauge));
            gaugeGo.transform.SetParent(speedValue.transform.parent, false);
            var gaugeRect = (RectTransform)gaugeGo.transform;
            gaugeRect.localScale = Vector3.one;
            // якоря как у соседей левой колонки (FuelTrack/FuelIcon — top-left):
            // GaugeCenter() возвращает координаты именно в этой конвенции,
            // центр-якорь сдвигал бы спидометр на пол-канваса (к центру экрана)
            gaugeRect.anchorMin = gaugeRect.anchorMax = new Vector2(0f, 1f);
            gaugeRect.pivot = new Vector2(0.5f, 0.5f);
            gaugeRect.sizeDelta = new Vector2(GaugeSize, GaugeSize);
            // центр считается ОДИН раз до перепарентчивания SpeedValue внутрь
            // шкалы: GaugeCenter() читает узлы в координатах родителя HUD, а
            // повторный вызов после SetParent дал бы смешение пространств
            Vector2 gaugeCenter = GaugeCenter();
            gaugeRect.anchoredPosition = gaugeCenter;

            // стрелка — нижний слой: хаб в центре шкалы, вращается вокруг него
            var needleGo = new GameObject("Needle", typeof(RectTransform), typeof(Image));
            needleGo.transform.SetParent(gaugeRect, false);
            var needleImage = needleGo.GetComponent<Image>();
            Sprite arrow = Resources.Load<Sprite>("Art/icons/arrow");
            if (arrow == null)
                Debug.LogWarning("[Hud] Art/icons/arrow не найден — спидометр без стрелки");
            needleImage.sprite = arrow;
            needleImage.color = IconGreen; // стрелка в зелёном HUD, как шкала и иконки (Н4.13)
            needleImage.raycastTarget = false;
            var needleRect = (RectTransform)needleGo.transform;
            needleRect.anchorMin = needleRect.anchorMax = new Vector2(0.5f, 0.5f);
            needleRect.pivot = new Vector2(NeedleHubPx.x / NeedleSpritePx.x, 1f - NeedleHubPx.y / NeedleSpritePx.y);
            needleRect.sizeDelta = Vector2.Scale(NeedleSpritePx, Vector2.one * NeedleScale);
            needleRect.anchoredPosition = Vector2.zero;

            // шкала — поверх стрелки: полукруг 2:1, центр дуги — в центре спидометра
            var dialGo = new GameObject("Dial", typeof(RectTransform), typeof(Image));
            dialGo.transform.SetParent(gaugeRect, false);
            var dialImage = dialGo.GetComponent<Image>();
            dialImage.sprite = Resources.Load<Sprite>("Art/icons/speed");
            dialImage.color = IconGreen; // зелёный HUD, как иконки щита/топлива
            dialImage.raycastTarget = false;
            var dialRect = (RectTransform)dialGo.transform;
            dialRect.anchorMin = dialRect.anchorMax = new Vector2(0.5f, 0.5f);
            float dialHeight = GaugeSize * 0.5f; // арт speed.png 184×92
            dialRect.sizeDelta = new Vector2(GaugeSize, dialHeight);
            dialRect.anchoredPosition = new Vector2(0f, dialHeight * 0.5f); // плоская сторона — к центру

            // цифровое значение — СПРАВА от шкалы, размер как у HullValue (36pt,
            // высота 42, BrightColor — ровный ряд статов, Н4.13); высота 42 < 47.5
            // правила 1.32 допустима: у SpeedValue Overflow (узел сцены, не фабрика)
            speedValue.transform.SetParent(gaugeRect, false);
            var speedRect = speedValue.rectTransform;
            speedRect.anchorMin = new Vector2(0.5f, 0.5f);
            speedRect.anchorMax = new Vector2(0.5f, 0.5f);
            speedRect.pivot = new Vector2(0f, 0.5f); // левый край — от шкалы вправо
            speedRect.sizeDelta = new Vector2(130f, 42f);
            speedRect.anchoredPosition = new Vector2(GaugeSize * 0.5f + 8f, 0f);
            speedValue.fontSize = 36f;
            speedValue.color = OnboardComputerFactory.BrightColor;
            speedValue.alignment = TextAlignmentOptions.Left;

            gauge = gaugeGo.GetComponent<SpeedometerGauge>();
            gauge.Setup(needleRect, NeedleAngleAtZero, NeedleSweep);

            // старая иконка скорости больше не нужна — её место заняла шкала
            var speedIcon = transform.Find("SpeedIcon");
            if (speedIcon != null)
                speedIcon.gameObject.SetActive(false);

            ShiftHullRow(GaugeHullTop(gaugeCenter));
        }

        // центр спидометра: левый край шкалы выровнен по левому краю иконок
        // статов (FuelIcon/HullIcon x=14.2, пивоты x=0 — anchoredPosition.x и есть
        // левый край), у — под строкой топлива с зазором GaugeTopGap (опущена
        // ниже по Н4.13). Всё в координатах родителя (точечные якоря), углы —
        // от его верха-лева. Вызывать ДО перепарентчивания SpeedValue внутрь
        // шкалы (poseSource не использует SpeedValue — после SetParent его
        // координаты были бы локальными координатами шкалы)
        private Vector2 GaugeCenter()
        {
            var track = transform.Find("FuelTrack");
            var fuelIcon = transform.Find("FuelIcon");
            // колонка статов задана FuelIcon (x=14.2): шкала выравнивается по ней,
            // а не по скрытому SpeedIcon (x=18) — «выровняй с остальными» (Н4.13)
            Transform xSource = fuelIcon != null ? fuelIcon : track;
            Vector2 center = xSource != null
                ? new Vector2(((RectTransform)xSource).anchoredPosition.x + GaugeSize * 0.5f, 0f)
                : new Vector2(16f + GaugeSize * 0.5f, -32f - 55f); // сцена без строк — левый край под шапкой
            float dialHalf = GaugeSize * 0.5f;
            if (fuelIcon != null || track != null)
            {
                var rowRect = (RectTransform)(fuelIcon != null ? fuelIcon : track);
                center.y = RowBottom(rowRect) - GaugeTopGap - dialHalf;
            }
            return center;
        }

        // верх строки корпуса, чтобы не задевать висящую вниз стрелку покоя
        private float GaugeHullTop(Vector2 gaugeCenter)
        {
            var hullIcon = transform.Find("HullIcon");
            if (hullIcon == null)
                return float.MinValue; // двигать нечего
            var hullRect = (RectTransform)hullIcon;
            float needleLen = (NeedleSpritePx.x - NeedleHubPx.x) * NeedleScale;
            return gaugeCenter.y - needleLen - GaugeHullGap;
        }

        // сдвиг строки корпуса (иконка + значение) вниз под спидометр
        private void ShiftHullRow(float newTopY)
        {
            var hullIcon = transform.Find("HullIcon");
            var hullValue = transform.Find("HullValue");
            if (hullIcon == null || hullValue == null)
                return;
            var hullRect = (RectTransform)hullIcon;
            float delta = newTopY - hullRect.anchoredPosition.y;
            if (delta >= 0f)
                return; // место и так есть — сцену не трогаем
            hullRect.anchoredPosition += Vector2.up * delta;
            ((RectTransform)hullValue).anchoredPosition += Vector2.up * delta;
        }

        // низ узла по его пивоту: FuelIcon (пивот 0,1) → −87, FuelTrack (0, 0.5) → −69
        private static float RowBottom(RectTransform rect)
        {
            return rect.anchoredPosition.y - rect.pivot.y * rect.sizeDelta.y;
        }

        // иконки статов: в сцене болванки ui_icon_shield/ui_icon_fuel — меняем
        // спрайты на арт из icons и красим в зелёный HUD (BrightColor борткомпа)
        private void BuildStatIcons()
        {
            // владелец вернул shield_icon.png (Н5.A1) — shield.png слишком «залитый»
            SetIcon("HullIcon", "Art/icons/shield_icon");
            SetIcon("FuelIcon", "Art/icons/gas_icon");
        }

        // подмена спрайта существующему Image-узлу; при неудаче остаётся болванка
        private void SetIcon(string nodeName, string resourcePath)
        {
            Sprite sprite = Resources.Load<Sprite>(resourcePath);
            var node = transform.Find(nodeName);
            if (sprite == null || node == null)
                return;
            var img = node.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = sprite;
                img.color = IconGreen;
                img.preserveAspect = true; // пропорции арта не тянутся в квадратный слот
            }
        }

        // сирены по бокам экрана: мигают красным при тревоге
        private void BuildSirens()
        {
            Sprite danger = Resources.Load<Sprite>("Art/icons/danger");
            if (danger == null)
                return;
            sirenLeft = MakeSiren("SirenLeft", new Vector2(0.03f, 0.72f), danger);
            sirenRight = MakeSiren("SirenRight", new Vector2(0.97f, 0.72f), danger);
        }

        private Image MakeSiren(string name, Vector2 anchor, Sprite danger)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.sprite = danger;
            img.raycastTarget = false;
            img.color = new Color(1f, 0.2f, 0.15f, 0f);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(64f, 58f);
            return img;
        }

        private void Update()
        {
            if (!bound)
            {
                ship = FindAnyObjectByType<Ship>();
                gravity = FindAnyObjectByType<GravityField>();
                if (ship != null && ship.Config != null)
                {
                    controller = ship.GetComponent<ShipController>();
                    var hull = ship.GetComponent<HullSystem>();
                    if (hull != null)
                    {
                        hull.HpChanged += UpdateHull;
                        UpdateHull();
                    }
                    ship.FuelChanged += UpdateFuel;
                    UpdateFuel();
                    bound = true;
                }
            }

            if (ship != null && ship.Config != null)
            {
                float speed = ship.Body.linearVelocity.magnitude;
                speedValue.text = ((int)speed).ToString();
                if (gauge != null && ship.ShipConfig != null && ship.ShipConfig.MaxV > 0f)
                    gauge.SetFraction(speed / ship.ShipConfig.MaxV);
                UpdateSpeedColor(speed);
                UpdateWarning();
                UpdateGoalHint();
            }

            UpdateDim();
        }

        // опрос панелей каждый кадр, кэш контроллеров ленивый — стиль биндера;
        // unscaledDeltaTime: панели открываются и в паузе
        private void UpdateDim()
        {
            if (mapScreen == null) mapScreen = FindAnyObjectByType<MapScreenController>();
            if (onboard == null) onboard = FindAnyObjectByType<OnboardComputerPanel>();
            if (station == null) station = FindAnyObjectByType<StationScreenController>();

            bool panelOpen = (mapScreen != null && mapScreen.IsOpened)
                || (onboard != null && onboard.IsShown)
                || (station != null && station.IsOpened);
            float target = panelOpen ? DimmedAlpha : 1f;
            currentAlpha = Mathf.MoveTowards(currentAlpha, target, Time.unscaledDeltaTime / DimFadeSeconds);
            dimGroup.alpha = currentAlpha;
        }

        // в посадочном кольце: зелёная — сядем, красная — разобьёмся
        private void UpdateSpeedColor(float speed)
        {
            if (ship.State == ShipState.Flying
                && controller != null && controller.InLandingRing)
            {
                speedValue.color = speed < ship.Config.LandSpeed ? SpeedSafe : SpeedDanger;
            }
            else
            {
                speedValue.color = speedBaseColor;
            }
        }

        private SpaceTaxi.Core.GameSession cachedSession; // кэш — поиск каждый кадр не нужен

        private void UpdateGoalHint()
        {
            if (cachedSession == null)
                cachedSession = FindAnyObjectByType<SpaceTaxi.Core.GameSession>();
            var session = cachedSession;
            var orders = session != null ? session.Orders : null;
            var active = orders != null ? orders.Active : null;

            if (active == null)
            {
                goalHint.text = ""; // без заказа — не висит ничего
                return;
            }

            if (active.State == SpaceTaxi.Orders.OrderState.Aboard)
            {
                int index = active.DropoffIndex;
                goalHint.text = "ПАССАЖИР НА БОРТУ · доставить: " + orders.PlanetName(index).ToUpper()
                    + " (" + orders.DistanceToPlanet(index, ship.transform.position).ToString("0") + " юн)";
            }
            else
            {
                int index = active.PickupIndex;
                goalHint.text = "ЗАКАЗ · забрать: " + active.Passenger.DisplayName + " — "
                    + orders.PlanetName(index).ToUpper()
                    + " (" + orders.DistanceToPlanet(index, ship.transform.position).ToString("0") + " юн)";
            }
        }

        private void UpdateHull()
        {
            hullValue.text = Mathf.RoundToInt(ship.HpFraction * 100f) + "%";
        }

        private void UpdateFuel()
        {
            int lit = Mathf.CeilToInt(ship.FuelFraction * fuelSegments.Length);
            for (int i = 0; i < fuelSegments.Length; i++)
                fuelSegments[i].color = i < lit ? SegmentOn : SegmentOff;
        }

        // один баннер, приоритет: пустой бак > жар > мало топлива.
        // Тревога: крупный пульсирующий текст + сирены по бокам + звук
        private void UpdateWarning()
        {
            string message = null;
            if (ship.Fuel <= 0f)
                message = "⚠ ТОПЛИВО КОНЧИЛОСЬ — ДРЕЙФ";
            else if (IsInHeat())
                message = "⚠ ЖАРОВАЯ ЗОНА — КОРПУС ГОРИТ";
            else if (ship.FuelFraction < 0.1f)
                message = "⚠ ТОПЛИВО КОНЧАЕТСЯ";

            if (message == null)
            {
                warningBanner.SetActive(false);
                SetAlarm(false);
                if (sirenLeft != null)
                {
                    var c0 = sirenLeft.color;
                    c0.a = 0f;
                    sirenLeft.color = c0;
                    sirenRight.color = c0;
                }
                return;
            }
            warningBanner.SetActive(true);
            if (warningText.text != message)
                warningText.text = message;
            SetAlarm(true);

            // мигание: текст и сирены пульсируют в противофазе — заметно периферией
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
            warningText.color = Color.Lerp(SpeedDanger, Color.white, pulse * 0.6f);
            float sirenAlpha = 0.25f + 0.75f * (1f - pulse);
            if (sirenLeft != null)
            {
                var c = sirenLeft.color;
                c.a = sirenAlpha;
                sirenLeft.color = c;
                sirenRight.color = c;
            }
        }

        private void SetAlarm(bool on)
        {
            if (alarmOn == on)
                return;
            alarmOn = on;
            if (on)
                Sound.StartLoop(SoundId.Alarm);
            else
                Sound.StopLoop(SoundId.Alarm);
        }

        private bool IsInHeat()
        {
            if (gravity == null)
                return false;
            foreach (CelestialBody body in gravity.Bodies)
                if (body is Star star
                    && star.IsInHeatZone(ship.transform.position))
                    return true;
            return false;
        }
    }
}
