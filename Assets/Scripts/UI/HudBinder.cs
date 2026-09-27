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

        private Ship ship;
        private GravityField gravity;
        private bool bound;

        // Цвета сегментов топлива из макета (10_UI_DESIGN).
        private static readonly Color SegmentOn = new Color(0.235f, 1f, 0.557f, 1f);
        private static readonly Color SegmentOff = new Color(0.059f, 0.2f, 0.125f, 0.824f);

        private void Awake()
        {
            speedValue = transform.Find("SpeedValue").GetComponent<TMP_Text>();
            hullValue = transform.Find("HullValue").GetComponent<TMP_Text>();
            for (int i = 0; i < fuelSegments.Length; i++)
                fuelSegments[i] = transform.Find($"FuelTrack/FuelSeg_{i + 1:00}").GetComponent<Image>();
            warningBanner = transform.Find("WarningBanner").gameObject;
            warningText = warningBanner.transform.Find("Text").GetComponent<TMP_Text>();
        }

        private void Update()
        {
            if (!bound)
            {
                ship = FindAnyObjectByType<Ship>();
                gravity = FindAnyObjectByType<GravityField>();
                if (ship != null && ship.Config != null)
                {
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
                speedValue.text = ((int)ship.Body.linearVelocity.magnitude).ToString();
                UpdateWarning();
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
