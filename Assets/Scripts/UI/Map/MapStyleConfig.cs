using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SpaceTaxi.UI
{
    // Редактируемый стиль карты: визуальные ручки и свои надписи/контуры с окном
    // видимости по зуму. Правится окном Space Taxi → Map Style Editor, карта
    // перестраивается на лету; дефолт — Assets/Resources/MapStyle_Default.asset.
    [CreateAssetMenu(fileName = "MapStyle_Default", menuName = "Space Taxi/Map Style")]
    public class MapStyleConfig : ScriptableObject
    {
        [Header("Шрифт (null = шрифт сцены, Excalifont)")]
        public TMP_FontAsset fontOverride;

        [Header("Подписи тел (экранный кегль, константный при зуме)")]
        public float labelFontSize = 18f;
        public Color labelColor = new Color(0.243f, 0.184f, 0.137f); // Ink #3E2F23

        [Header("Районы — надписи областей")]
        public float districtFontSize = 38f;
        public Color districtColor = new Color(0.243f, 0.184f, 0.137f);
        [Range(0f, 1f)] public float districtFadeOutFraction = 0.60f; // гаснут при contentScale ≥

        [Header("Цель заказа (жёлтый = цель)")]
        public float targetRingPx = 64f;
        public float targetRingWidePx = 90f;
        public Color targetColor = new Color(1f, 0.94f, 0.3f);

        [Header("Своя метка (зелёный = метка игрока)")]
        public float waypointHaloPx = 64f;
        public float waypointWidePx = 90f;
        public float waypointPinPx = 20f;
        public Color waypointColor = new Color(0.235f, 1f, 0.557f);

        [Header("Легенда и заголовок")]
        public bool legendVisible = true;
        public Vector2 legendSize = new Vector2(330f, 170f);
        public bool titleVisible = true;
        public string titleText = "маршрутный лист";

        [Header("Край мира (туманная кромка)")]
        [Range(0.3f, 0.95f)] public float fogInnerFrac = 0.65f; // растворение начинается с доли радиуса

        [Header("Бумага")]
        public float paperWorldMargin = 1.6f;   // радиус бумаги = радиус мира ×
        public float worldUnitsPerTile = 512f;  // плотность зерна: юнитов мира на тайл

        [Header("Зум")]
        public float maxViewZoom = 14f;         // потолок приближения
        public float initialViewZoom = 9f;      // карта открывается приближенной

        [Header("Зависимости от масштаба")]
        [Range(0f, 1f)] public float rulerMinZoomFraction = 0.30f; // линейка видна при contentScale ≥
        [Range(0.5f, 2f)] public float zoomButtonScale = 1f;       // размер кнопок зума

        [Header("Свои надписи и контуры (ставятся кликом из Map Style Editor)")]
        public List<MapAnnotation> annotations = new List<MapAnnotation>();
    }

    // Аннотация карты: надпись или контур в мировых координатах (к планетам не
    // привязана — их расположение зафиксировано). Окно видимости min/maxZoom:
    // 0 = без границы с той стороны; контуры живут в масштабе мира (география),
    // надписи — по выбору: экранный кегль или вместе с миром.
    [System.Serializable]
    public class MapAnnotation
    {
        public string text = "метка";
        public AnnotationKind kind = AnnotationKind.Label;
        public Vector2 worldPosition;

        [Range(8f, 72f)] public float fontSize = 20f;  // Label: кегль; если screenFixedSize — экранные px
        public Color color = new Color(0.243f, 0.184f, 0.137f);
        public bool screenFixedSize = false;           // Label: true = не растёт с зумом (как подписи тел)

        public float radius = 300f;                    // Ring/Circle: юнитов мира; Rect: полширины
        public float height = 220f;                    // Rect: полвысоты
        [Range(1f, 12f)] public float thicknessPx = 2f; // Rect: толщина линий (экранные px)

        public float minZoom = 0f;                     // видима при зуме ≥ (0 = без границы)
        public float maxZoom = 0f;                     // видима при зуме ≤ (0 = без границы)
    }

    public enum AnnotationKind
    {
        Label,   // надпись
        Ring,    // кольцо-обводка (рисованный карандашный спрайт)
        Circle,  // залитый круг (мягкий, полупрозрачный)
        Rect     // прямоугольная рамка из чернильных линий
    }
}
