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
    public partial class HudBinder : MonoBehaviour
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
        private HullSystem hullSystem; // для симметричной отписки в OnDestroy
        private BlackHole hole;      // предупреждения дыры — ленивый бинд, как в самом BlackHole
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

        // горящий/погашенный сегмент бака — палитра в HudPalette (Н12.4)
        private static readonly Color SegmentOn = HudPalette.SegmentOn;
        private static readonly Color SegmentOff = HudPalette.SegmentOff;
        // безопасная/опасная скорость (жар звёзд красит стрелку)
        private static readonly Color SpeedSafe = new Color(0.235f, 1f, 0.557f, 1f);
        private static readonly Color SpeedDanger = new Color(1f, 0.35f, 0.3f, 1f);
        // зелёный HUD-иконок — BrightColor каркаса борткомпа (принятый зелёный)
        private static readonly Color IconGreen = OnboardComputerFactory.BrightColor;

        // каркас HUD собирается кодом + подписки на корпус/бак/рейтинг
        private void Awake()
        {
            dimGroup = GetComponent<CanvasGroup>();
            if (dimGroup == null)
                dimGroup = gameObject.AddComponent<CanvasGroup>();

            speedValue = transform.Find("SpeedValue")?.GetComponent<TMP_Text>();
            hullValue = transform.Find("HullValue")?.GetComponent<TMP_Text>();
            warningBanner = transform.Find("WarningBanner")?.gameObject;
            warningText = warningBanner != null ? warningBanner.transform.Find("Text")?.GetComponent<TMP_Text>() : null;
            // переименование узла сцены — явная ошибка вместо NRE в кадре
            if (speedValue == null || hullValue == null || warningText == null)
            {
                Debug.LogError("[HudBinder] в HUD нет SpeedValue/HullValue/WarningBanner/Text — биндер отключён");
                enabled = false;
                return;
            }
            for (int i = 0; i < fuelSegments.Length; i++)
            {
                var seg = transform.Find($"FuelTrack/FuelSeg_{i + 1:00}");
                fuelSegments[i] = seg != null ? seg.GetComponent<Image>() : null;
                if (fuelSegments[i] == null)
                {
                    Debug.LogError($"[HudBinder] нет FuelSeg_{i + 1:00} — биндер отключён");
                    enabled = false;
                    return;
                }
            }
            warningText.fontSize = 36f; // тревога должна пугать издалека
            warningText.enableAutoSizing = true; // текст не вылезает из подложки
            warningText.fontSizeMin = 20f;
            warningText.fontSizeMax = 36f;
            var bannerRect = warningBanner.transform as RectTransform;
            if (bannerRect != null)
            {
                bannerRect.sizeDelta = new Vector2(bannerRect.sizeDelta.x * 1.3f, bannerRect.sizeDelta.y * 1.25f);
                // Н9.5: владелец вернул тревогу наверх («уведомления снизу —
                // наверх, например о чёрной дыре»); №19 гнал её вниз из-за
                // стопки тостов — теперь тост на экране один, баннер снова
                // над ним. Текст, пульс, сирены и звук остаются как были
                bannerRect.anchorMin = new Vector2(0.5f, 1f);
                bannerRect.anchorMax = new Vector2(0.5f, 1f);
                bannerRect.pivot = new Vector2(0.5f, 1f);
                bannerRect.anchoredPosition = new Vector2(0f, -16f);
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

            AlignFuelBlock();
            BuildSpeedometer();
            BuildStatIcons();
            BuildSirens();
        }

        // Н9.7 (конвенция OnboardBinder (Н12: partial-файлы)): издатели переживают подписчика
        // при смене сцены — отписка симметрична подписке в Update
        private void OnDestroy()
        {
            if (ship != null)
                ship.FuelChanged -= UpdateFuel;
            if (hullSystem != null)
                hullSystem.HpChanged -= UpdateHull;
        }

        // тик HUD: подсказка цели, спидометр, корпуса/бак/сирены
        private void Update()
        {
            if (!bound)
            {
                ship = FindAnyObjectByType<Ship>();
                gravity = FindAnyObjectByType<GravityField>();
                if (ship != null && ship.Config != null)
                {
                    controller = ship.GetComponent<ShipController>();
                    hullSystem = ship.GetComponent<HullSystem>();
                    if (hullSystem != null)
                    {
                        hullSystem.HpChanged += UpdateHull;
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

        // строка «ЗАКАЗ · забрать/вези …» над миром (кэш строк — без аллокаций)
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

            // таймер «паникёра» (Н7 Q7.1): остаток mm:ss рядом с маршрутом,
            // цвет плывёт к Danger по мере истечения; на нуле — срыв заказа
            if (active.HurrySeconds > 0f && active.HurryStartedAt >= 0f)
            {
                float remaining = active.HurryRemaining(Time.time);
                Color timerColor = Color.Lerp(OnboardComputerFactory.TaxiColor, OnboardComputerFactory.DangerColor,
                    1f - Mathf.Clamp01(remaining / OnboardBinder.HurryWarnSeconds));
                goalHint.text += $"<color=#{ColorUtility.ToHtmlStringRGB(timerColor)}> · ОСТАЛОСЬ "
                    + UiFormat.FormatClock(remaining) + " · срыв заказа</color>";
            }
        }

        // процент корпуса в HUD (из HpFraction)
        private void UpdateHull()
        {
            hullValue.text = Mathf.RoundToInt(ship.HpFraction * 100f) + "%";
        }

        // сегменты бака: горит = доля топлива
        private void UpdateFuel()
        {
            int lit = Mathf.CeilToInt(ship.FuelFraction * fuelSegments.Length);
            for (int i = 0; i < fuelSegments.Length; i++)
                fuelSegments[i].color = i < lit ? SegmentOn : SegmentOff;
        }

        // один баннер, приоритет: пустой бак > захват дыры > жар > зона дыры > мало топлива.
        // Тревога: крупный пульсирующий текст + сирены по бокам + звук.
        // v5 (2026-11-15, владелец: «предупреждения как с жаром, но не на весь
        // экран как сейчас»): предупреждения чёрной дыры — здесь, фуллскрин-
        // оверлей убран; в тексте захвата — отсчёт до потери связи и подсказка X
        private void UpdateWarning()
        {
            if (hole == null)
                hole = FindAnyObjectByType<BlackHole>();

            string message = null;
            if (ship.Fuel <= 0f)
                message = "⚠ ТОПЛИВО КОНЧИЛОСЬ — ДРЕЙФ";
            else if (hole != null && hole.SwallowActive)
                message = hole.PoweredDown
                    ? $"⚠ ЧЁРНАЯ ДЫРА — ЗАТЯГИВАЕТ · СВЯЗЬ ЧЕРЕЗ {Mathf.CeilToInt(hole.SwallowLeftSeconds)} с"
                    : $"⚠ ЧЁРНАЯ ДЫРА — ЗАТЯГИВАЕТ · СВЯЗЬ ЧЕРЕЗ {Mathf.CeilToInt(hole.SwallowLeftSeconds)} с · X — ОТКЛЮЧИТЬ БОРТ";
            else if (IsInHeat())
                message = "⚠ ЖАРОВАЯ ЗОНА — КОРПУС ГОРИТ";
            else if (hole != null && hole.InEffectZone)
                message = "⚠ ЧЁРНАЯ ДЫРА — ЗОНА ВЛИЯНИЯ";
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

        // луп сирены при тревоге (жар/сирены): старт/стоп один раз
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

        // корабль в зоне жара любой звезды — стрелка скорости краснеет
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
