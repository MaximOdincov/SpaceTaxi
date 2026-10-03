using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Живой HUD: скорость, прочность, топливо сегментами, баннер тревоги.
    // Вешается на объект HUD, элементы находит по именам из сцены.
    public class HudBinder : MonoBehaviour
    {
        private TMP_Text speedValue;
        private TMP_Text hullValue;
        private Image[] fuelSegments = new Image[10];
        private GameObject warningBanner;
        private TMP_Text warningText;
        private TMP_Text goalHint;

        [SerializeField] private TMP_FontAsset font; // JetBrains Mono из сцены

        private Ship ship;
        private GravityField gravity;
        private ShipController controller;
        private bool bound;
        private Color speedBaseColor;

        // Цвета сегментов топлива из макета (10_UI_DESIGN).
        private static readonly Color SegmentOn = new Color(0.235f, 1f, 0.557f, 1f);
        private static readonly Color SegmentOff = new Color(0.059f, 0.2f, 0.125f, 0.824f);
        private static readonly Color SpeedSafe = new Color(0.235f, 1f, 0.557f, 1f);
        private static readonly Color SpeedDanger = new Color(1f, 0.35f, 0.3f, 1f);

        private void Awake()
        {
            speedValue = transform.Find("SpeedValue").GetComponent<TMP_Text>();
            hullValue = transform.Find("HullValue").GetComponent<TMP_Text>();
            for (int i = 0; i < fuelSegments.Length; i++)
                fuelSegments[i] = transform.Find($"FuelTrack/FuelSeg_{i + 1:00}").GetComponent<Image>();
            warningBanner = transform.Find("WarningBanner").gameObject;
            warningText = warningBanner.transform.Find("Text").GetComponent<TMP_Text>();
            speedBaseColor = speedValue.color;

            // Подсказка-цель: что делать прямо сейчас (под баннером тревоги).
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
            hintRect.anchoredPosition = new Vector2(0f, -140f);
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
                UpdateSpeedColor(speed);
                UpdateWarning();
                UpdateGoalHint();
            }
        }

        // Посадочная индикация: в посадочном кольце скорость зелёная —
        // касание будет мягким, красная — разобьёшься. Решение «тормозить
        // или садиться» считывается без арифметики в голове.
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

        // Подсказка-цель: постоянно показываем следующий шаг игрока.
        private void UpdateGoalHint()
        {
            var session = FindAnyObjectByType<SpaceTaxi.Core.GameSession>();
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

        // Один баннер, приоритет: пустой бак > жар > мало топлива.
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
                return;
            }
            warningBanner.SetActive(true);
            if (warningText.text != message)
                warningText.text = message;
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
