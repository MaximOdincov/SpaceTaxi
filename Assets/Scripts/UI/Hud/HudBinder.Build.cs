using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceTaxi.UI
{
    // Построение каркаса HUD: выравнивание топливного блока, спидометр,
    // иконки статов, сирены. Живые данные — в HudBinder.cs
    public partial class HudBinder
    {
        // поднимает блок топлива до верхней кромки миникарты (если он ниже)
        // и центрирует ячейки по иконке. Вызывать до BuildSpeedometer —
        // центр шкалы считается от уже выровненной иконки
        private void AlignFuelBlock()
        {
            var fuelIcon = transform.Find("FuelIcon") as RectTransform;
            var fuelTrack = transform.Find("FuelTrack") as RectTransform;
            if (fuelIcon == null || fuelTrack == null)
                return;

            // верхняя кромка миникарты в координатах родителя (якорь верх-право,
            // пивот центр: верх = anchoredPosition.y + (1 - pivot.y) * высота)
            var minimap = transform.Find("Minimap") as RectTransform;
            if (minimap != null)
            {
                float minimapTop = minimap.anchoredPosition.y + (1f - minimap.pivot.y) * minimap.sizeDelta.y;
                float iconTop = fuelIcon.anchoredPosition.y + (1f - fuelIcon.pivot.y) * fuelIcon.sizeDelta.y;
                if (minimapTop > iconTop)
                {
                    float shift = minimapTop - iconTop; // только вверх, вниз не двигаем
                    fuelIcon.anchoredPosition += Vector2.up * shift;
                    fuelTrack.anchoredPosition += Vector2.up * shift;
                }
            }

            // ячейки: высота = высоте трека, вертикальный центр = центру иконки
            // (у сегментов якорь и пивот (0, 0.5) — отсчёт от центра FuelTrack)
            float iconCenterY = fuelIcon.anchoredPosition.y + (0.5f - fuelIcon.pivot.y) * fuelIcon.sizeDelta.y;
            float trackCenterY = fuelTrack.anchoredPosition.y + (0.5f - fuelTrack.pivot.y) * fuelTrack.sizeDelta.y;
            float cellHeight = fuelTrack.sizeDelta.y;
            foreach (Image segment in fuelSegments)
            {
                if (segment == null)
                    continue;
                RectTransform rect = segment.rectTransform;
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, cellHeight);
                Vector2 pos = rect.anchoredPosition;
                pos.y = iconCenterY - trackCenterY;
                rect.anchoredPosition = pos;
            }
        }

        // ————— геометрия спидометра —————

        private const float GaugeSize = 55f;      // слот как у иконок статов (55×55)
        private const float NeedleScale = 0.85f;  // остриё не вылезает за шкалу
        private const float GaugeTopGap = 12f;    // зазор между шкалой и строкой топлива
        private const float GaugeHullGap = 6f;    // зазор между стрелкой и строкой корпуса
        // ход стрелки замерен по делениям спрайта шкалы: покой — остриё на «0»
        // (183.5°), размах до максимума — 154°
        private const float NeedleAngleAtZero = 183.5f;
        private const float NeedleSweep = 154f;
        // хаб стрелки (центр белого шара) в пикселях arrow.png — вокруг него
        // пивот, шар сидит на оси шкалы
        private static readonly Vector2 NeedleHubPx = new Vector2(13.5f, 13.5f);
        private static readonly Vector2 NeedleSpritePx = new Vector2(41f, 28f);

        // шкала (полукруг) + стрелка под ней, вращается по скорости; цифры справа
        private void BuildSpeedometer()
        {
            var gaugeGo = new GameObject("Speedometer", typeof(RectTransform), typeof(SpeedometerGauge));
            gaugeGo.transform.SetParent(speedValue.transform.parent, false);
            var gaugeRect = (RectTransform)gaugeGo.transform;
            gaugeRect.localScale = Vector3.one;
            // якоря как у соседей левой колонки (top-left): GaugeCenter()
            // возвращает координаты в этой конвенции
            gaugeRect.anchorMin = gaugeRect.anchorMax = new Vector2(0f, 1f);
            gaugeRect.pivot = new Vector2(0.5f, 0.5f);
            gaugeRect.sizeDelta = new Vector2(GaugeSize, GaugeSize);
            // центр считается один раз ДО перепарентчивания SpeedValue внутрь
            // шкалы — после SetParent координаты были бы уже локальными
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
            needleImage.color = IconGreen; // зелёный HUD
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
            dialImage.color = IconGreen;
            dialImage.raycastTarget = false;
            var dialRect = (RectTransform)dialGo.transform;
            dialRect.anchorMin = dialRect.anchorMax = new Vector2(0.5f, 0.5f);
            float dialHeight = GaugeSize * 0.5f; // арт speed.png 184×92
            dialRect.sizeDelta = new Vector2(GaugeSize, dialHeight);
            dialRect.anchoredPosition = new Vector2(0f, dialHeight * 0.5f); // плоская сторона — к центру

            // цифры справа от шкалы; у узла сцены Overflow — правило 1.32 не обязательно
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

        // центр спидометра: левый край — по колонке иконок статов, y — под
        // строкой топлива. Считать до перепарентчивания SpeedValue внутрь шкалы
        private Vector2 GaugeCenter()
        {
            var track = transform.Find("FuelTrack");
            var fuelIcon = transform.Find("FuelIcon");
            // выравнивание по FuelIcon, не по скрытому SpeedIcon
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

        // низ узла через его пивот
        private static float RowBottom(RectTransform rect)
        {
            return rect.anchoredPosition.y - rect.pivot.y * rect.sizeDelta.y;
        }

        // иконки статов: подменяем спрайты болванок сцены на арт из icons
        private void BuildStatIcons()
        {
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

        // иконка-сирена (комета/пояс) в HUD-колонке
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
    }
}
