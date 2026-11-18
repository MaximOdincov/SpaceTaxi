using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.UI.PaperMap
{
    // — подсистема «кольца и маркеры» бумажной карты (Н11): жёлтая цель заказа,
    // зелёная метка игрока (waypoint), маркер ракеты и кольцо стоянки, край мира
    // (туманная кромка, буи, «за кругом — потеря связи»), живые кометы —
    // Выделена из MapScreenBinder (Н11): тела перенесены дословно. Кольца
    // обнимают планету (IconSizeOf — PaperMapLabels), counter-scale 1/zoom —
    // PaperMapProjection; состояние метки (Waypoint) живёт в хосте.
    internal sealed class PaperMapRings
    {
        private readonly MapScreenBinder host;
        private readonly PaperMapProjection view;
        private readonly PaperMapLabels labels;

        internal PaperMapRings(MapScreenBinder host, PaperMapProjection view, PaperMapLabels labels)
        {
            this.host = host;
            this.view = view;
            this.labels = labels;
        }

        // — шимы к хосту/виду/подписям (Н11): тела методов остаются дословными —
        private MapStyleConfig Style => host.Style;
        private LevelAsset level => host.Level;
        private float zoom => view.Zoom;
        private float scale => view.Scale;
        private Vector2 zoomOffset => view.ZoomOffset;
        private RectTransform content => view.Content;
        private RectTransform sheetRect => view.SheetRect;
        private float mapRadius => view.MapRadius;
        private float bodyEdgeRadius => view.BodyEdgeRadius;
        private Vector2 WorldToMap(Vector2 world) => view.WorldToMap(world);
        private Vector2 SheetClampView(Vector2 sheetPos) => view.SheetClampView(sheetPos);
        private CelestialBodyData Waypoint => host.Waypoint;
        private int ActiveOrderTargetIndex() => host.ActiveOrderTargetIndex();
        private Transform ShipPosition() => host.ShipPosition();
        private float HullHeadingDeg() => host.HullHeadingDeg();
        private Ship ship => host.Ship;
        private float IconSizeOf(CelestialBodyData body) => labels.IconSizeOf(body);
        private int targetIndex
        {
            get => labels.TargetIndex;
            set => labels.TargetIndex = value;
        }

        // наружу подсистемам (Н11): кляксы смерти клампятся как маркер ракеты
        internal RectTransform PlayerMarker => playerMarker;
        internal RectTransform EdgeRoot => edgeRoot; // Н11: слоты подписей читают буи/пометки
        internal float PlayerHeading
        {
            get => playerHeading;
            set => playerHeading = value;
        }

        // хелперы PaperMapUi (Н11)
        private static RectTransform MakeRect(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
            => PaperMapUi.MakeRect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
        private static RectTransform MakeImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size, bool raycast)
            => PaperMapUi.MakeImage(name, parent, sprite, color, size, raycast);
        private static RectTransform MakeSegment(RectTransform parent, Vector2 a, Vector2 b, Color color, float thickness)
            => PaperMapUi.MakeSegment(parent, a, b, color, thickness);
        private static Color WithAlpha(Color color, float alpha)
            => PaperMapUi.WithAlpha(color, alpha);
        private TMP_Text MakeLabel(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
            => PaperMapUi.MakeLabel(host.Font, name, parent, text, size, color, align);

        // M7.1: маркеры цели/метки — константный ЭКРАННЫЙ размер (скайрим-урок):
        // рут в зум-контенте несёт counter-scale 1/zoom (как кометы), дети
        // заданы в экранных px и не растут при приближении
        private float TargetRingPx => Style.targetRingPx;
        private float TargetRingWidePx => Style.targetRingWidePx;
        // M8.8: своя метка — зелёный аналог жёлтой цели заказа (кольца того же
        // калибра) + булавка
        private float WaypointHaloPx => Style.waypointHaloPx;
        private float WaypointWidePx => Style.waypointWidePx;
        private float WaypointPinPx => Style.waypointPinPx;

        // M8.7: жёлтый полупрозрачный ореол/кольцо цели (жёлтый = цель заказа,
        // Н5.28; янтарный Accent отменён владельцем). Тот же тон, что в игре
        // (OrderService.UpdateTargetGlow)
        private Color TargetRingColor => Style.targetColor;

        // M8.8: своя метка — зелёный (зелёный = метка игрока), единый цвет с
        // червоном радара (MinimapBinder.WaypointColor) и скобкой в мире
        private Color WaypointMarkColor => Style.waypointColor;

        // «нос» арт-спрайтов при localRotation = 0, градусов от +X (замер по пикселям):
        // ракета летит остриём в правый-верхний угол, комета — головой в левый-низ
        private const float RocketNoseDeg = 55f;
        private const float CometHeadDeg = 226f;

        private const float RocketMarkerPx = 64f;  // маркер ракеты (Н6.K6: 48 → +33%)

        private RectTransform edgeFogRoot; // M7.5: туманная кромка — вне блокирующего хрома (гигантский rect)
        private RectTransform edgeRoot;    // буи границы мира, тревожные пометки

        private RectTransform cometsRoot;  // живые кометы

        private RectTransform playerMarker;      // на sheetRect (вне зум-контента): RectMask2D не режет, размер постоянен
        private RectTransform playerArrowRoot; // вращается по курсу; подпись «ВЫ» не крутится
        private RectTransform targetRing;
        private RectTransform targetRingPulse;   // жёлтый пульс: сплошное кольцо
        private RectTransform targetRingWide;    // и второе — видно издалека
        private Image targetRingPulseImage;      // кэш: пульс обновляется каждый кадр
        private Image targetRingWideImage;
        private RectTransform waypointRoot;      // зелёный ореол метки + булавка
        private RectTransform waypointHalo;
        private RectTransform waypointWide;      // второе, широкое кольцо (как у цели)
        private RectTransform waypointPin;
        private Image waypointHaloImage;         // кэш: пульс обновляется каждый кадр
        private Image waypointWideImage;
        private Image waypointPinImage;

        private float playerHeading;

        // живые кометы (по ActiveComets CometSystem — как точки миникарты)
        private CometSystem cometSystem;
        private readonly Dictionary<Comet, RectTransform> cometIcons = new Dictionary<Comet, RectTransform>();
        private readonly Dictionary<Comet, Vector2> cometLastPos = new Dictionary<Comet, Vector2>();

        private RectTransform landedPad;
        private Image landedPadImage;
        private RectTransform landedRing;       // M7.3: чернильное кольцо «припаркованы здесь»
        private RectTransform landedRingNote;   // подпись под кольцом — константного экранного размера
        private CelestialBody landedRingBody;   // вокруг какого тела построено
        // (из BuildMap, Н11): слои края мира — туманная кромка (вне блокирующего
        // хрома) + буи/пометки
        internal void BuildEdgeLayers(RectTransform content)
        {
            edgeFogRoot = MakeRect("EdgeFogRoot", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            edgeRoot = MakeRect("WorldEdge", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        // кометы под ореолами цели/метки: ореол не должен прятаться под пролетевшей кометой
        internal void CreateCometsLayer(RectTransform content)
        {
            cometsRoot = MakeRect("Comets", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        internal void BuildCometZone()
        {
            if (level.CometAreaRadius <= 0f) // хорды по всему миру — зону не подписываем
                return;
            Vector2 pos = WorldToMap(level.CometAreaCenter);
            TMP_Text label = MakeLabel("CometZoneLabel", edgeRoot, "кометная зона", 16f,
                WithAlpha(PaperMapArt.InkSoft, 0.8f));
            ((RectTransform)label.transform).sizeDelta = new Vector2(240f, 22f);
            ((RectTransform)label.transform).anchoredPosition = pos + Vector2.up * (level.CometAreaRadius * scale + 16f);
        }

        // граница мира (M7.5): туманная кромка — чернильный крап, нарастающий за
        // границей («дальше бумаги нет»), буи-звёздочки и пометки «за кругом —
        // потеря связи» (красный больше не здесь — только смерть).
        // M8.2: рамка-обод по краю удалена (вкус владельца), туман — мягкий:
        // растворение начинается глубже (0.65×R) и тянется шире — без резкой
        // внутренней границы
        internal void BuildWorldEdge()
        {
            float borderPx = mapRadius * scale;

            // крап-туман: растворение начинается от ФАКТИЧЕСКОГО края тел
            // (bodyEdgeRadius, M8.3 — прежде 0.65×mapRadius, что при плотной
            // компоновке с WorldRadius 4600 накрыло бы Дальний район) и густеет
            // за кольцом буёв; живёт в edgeFogRoot — не блокирует слоты
            // подписей (см. BuildMap). Максимум с прежним поведением: у старого
            // мира 0.65×6000 ≈ 3936 — край тел, вид не меняется
            float fogInnerPx = Mathf.Max(bodyEdgeRadius, mapRadius * Style.fogInnerFrac) * scale;
            RectTransform fog = MakeImage("EdgeFog", edgeFogRoot, PaperMapArt.EdgeFog(),
                WithAlpha(PaperMapArt.Ink, 0.75f),
                Vector2.one * (fogInnerPx * 2f / PaperMapArt.EdgeFogBorderFrac), false);
            fog.anchoredPosition = Vector2.zero;
            fog.SetAsFirstSibling();

            Sprite star = PaperMapArt.BuoyStar();
            for (int i = 0; i < 12; i++)
            {
                float ang = i * 30f * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                RectTransform buoy = MakeImage("Buoy_" + i, edgeRoot, star,
                    WithAlpha(PaperMapArt.Ink, 0.7f), Vector2.one * 11f, false);
                buoy.anchoredPosition = dir * borderPx;
                buoy.localRotation = Quaternion.Euler(0f, 0f, i * 17f);
            }

            // Граница связи — окружность из дуговых сегментов (владелец Н9.2:
            // «полноценный круг без углов» — TargetRing 256px, растянутый до
            // тысяч px, читался многоугольником) с тем же трёхлепестковым воблом
            const int boundarySegments = 180;
            Vector2 prev = new Vector2(borderPx, 0f);
            for (int i = 1; i <= boundarySegments; i++)
            {
                float ang = i * Mathf.PI * 2f / boundarySegments;
                float wob = 1f + 0.012f * Mathf.Sin(ang * 3f + 1.3f);
                Vector2 p = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (borderPx * wob);
                MakeSegment(edgeRoot, prev, p, WithAlpha(PaperMapArt.Ink, 0.55f), 2f);
                prev = p;
            }
            var hatch = new System.Random(20261114);
            int strokes = 64;
            for (int i = 0; i < strokes; i++)
            {
                float ang = i * Mathf.PI * 2f / strokes + (float)hatch.NextDouble() * 0.05f;
                float r1 = borderPx + 6f + (float)hatch.NextDouble() * 6f;
                float len = 10f + (float)hatch.NextDouble() * 12f;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 a = dir * r1;
                Vector2 b = dir * (r1 + len) + new Vector2(-dir.y, dir.x) * ((float)hatch.NextDouble() * 6f - 3f);
                MakeSegment(edgeRoot, a, b, WithAlpha(PaperMapArt.Ink, 0.42f), 1.5f);
            }

            var warnColor = WithAlpha(PaperMapArt.InkSoft, 0.75f);
            for (int i = 0; i < 2; i++)
            {
                float ang = (45f + 180f * i) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                TMP_Text warn = MakeLabel("NoSignal_" + i, edgeRoot, "за кругом — потеря связи", 15f, warnColor);
                ((RectTransform)warn.transform).sizeDelta = new Vector2(280f, 22f);
                ((RectTransform)warn.transform).anchoredPosition = dir * (mapRadius * scale + 26f);
                ((RectTransform)warn.transform).localRotation = Quaternion.Euler(0f, 0f, i == 0 ? -2.5f : 2.5f);
            }
        }

        internal void BuildWaypoint()
        {
            // M7.1: константный экранный размер — рут несёт counter-scale 1/zoom,
            // дети заданы в экранных px и больше не растут от IconSizeOf×zoom.
            // M8.8: своя метка ЗЕЛЁНАЯ (зелёный = метка игрока) — ореол-кольца
            // того же калибра, что у жёлтой цели заказа, булавка над кольцом
            waypointRoot = MakeRect("WaypointRoot", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            waypointWide = MakeImage("Wide", waypointRoot, PaperMapArt.TargetRing(),
                WithAlpha(WaypointMarkColor, 0.5f), Vector2.one * WaypointWidePx, false);
            waypointHalo = MakeImage("Halo", waypointRoot, PaperMapArt.TargetRing(),
                WaypointMarkColor, Vector2.one * WaypointHaloPx, false);
            waypointPin = MakeImage("Pin", waypointRoot, PaperMapArt.Pin(),
                WaypointMarkColor, Vector2.one * WaypointPinPx, false);
            waypointPin.anchoredPosition = Vector2.up * (WaypointHaloPx * 0.5f + 16f);
            waypointHaloImage = waypointHalo.GetComponent<Image>(); // кэш для кадра
            waypointWideImage = waypointWide.GetComponent<Image>();
            waypointPinImage = waypointPin.GetComponent<Image>();
            waypointRoot.gameObject.SetActive(false);
        }

        // зелёный пульс на теле метки + булавка над ним
        internal void UpdateWaypointVisual()
        {
            if (waypointRoot == null)
                return;
            if (Waypoint == null)
            {
                if (waypointRoot.gameObject.activeSelf)
                    waypointRoot.gameObject.SetActive(false);
                return;
            }
            if (!waypointRoot.gameObject.activeSelf)
                waypointRoot.gameObject.SetActive(true);

            waypointRoot.anchoredPosition = WorldToMap(Waypoint.Position);
            waypointRoot.localScale = Vector3.one / Mathf.Max(zoom, 0.001f); // экранный px постоянны

            // ХОТФИКС владельца v2: ореол метки ОБНИМАЕТ планету на любом зуме —
            // кольца = экранный диаметр иконки ×1.25/×1.75 (как у жёлтой цели;
            // прежде кольца были постоянного экранного размера и «менялись»
            // относительно планеты: на отъезде больше неё, вблизи терялись внутри).
            // Пульс остался, в фазе (Н8.23); булавка — над кольцом
            float planetPx = IconSizeOf(Waypoint) * zoom;
            waypointHalo.sizeDelta = Vector2.one * (planetPx * 1.25f);
            waypointWide.sizeDelta = Vector2.one * (planetPx * 1.75f);
            waypointPin.anchoredPosition = Vector2.up * (planetPx * 1.25f * 0.5f + 16f);

            float t = Time.unscaledTime;
            float pulse = 0.72f + 0.22f * Mathf.Sin(t * 3.4f);
            if (waypointHaloImage != null)
                waypointHaloImage.color = WithAlpha(WaypointMarkColor, pulse);
            if (waypointWideImage != null)
                waypointWideImage.color = WithAlpha(WaypointMarkColor, pulse * 0.55f);
            if (waypointPinImage != null)
                waypointPinImage.color = WithAlpha(WaypointMarkColor, 0.75f + 0.25f * Mathf.Sin(t * 2.8f + 1f));
        }

        internal void BuildTargetRing()
        {
            // M7.1: константный экранный размер (руту ставит counter-scale в Update);
            // M8.7: единственный насыщенный цвет карты — жёлтый ореол цели
            targetRing = MakeRect("TargetRing", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            targetRingPulse = MakeImage("Ring", targetRing, PaperMapArt.TargetRing(),
                TargetRingColor, Vector2.one * TargetRingPx, false);
            targetRingWide = MakeImage("RingWide", targetRing, PaperMapArt.TargetRing(),
                WithAlpha(TargetRingColor, 0.7f), Vector2.one * TargetRingWidePx, false);
            targetRingPulseImage = targetRingPulse.GetComponent<Image>(); // кэш для кадра
            targetRingWideImage = targetRingWide.GetComponent<Image>();
            // Н10.5 (владелец: «куда-то пропадает имя планеты, когда взят заказ»):
            // подпись цели больше не прячется — кольцо-ореол маркирует цель,
            // имя планеты видно всегда (PlaceAllLabels отодвигает подпись
            // цели за внешний радиус кольца зазором слота)
            targetRing.gameObject.SetActive(false);
            targetIndex = -1;
        }

        // цель активного заказа — жёлтый пульс (заметный издалека); имя планеты
        // цели видно всегда (Н10.5 — подавление подписи снято)
        internal void UpdateTargetRing()
        {
            int index = ActiveOrderTargetIndex();
            if (index < 0 || index >= level.Bodies.Count)
            {
                targetRing.gameObject.SetActive(false);
                targetIndex = -1;
                return;
            }

            CelestialBodyData body = level.Bodies[index];
            targetRing.gameObject.SetActive(true);
            targetIndex = index; // Н10.5: подпись цели не прячется — кольцо и имя живут вместе
            targetRing.anchoredPosition = WorldToMap(body.Position);
            targetRing.localScale = Vector3.one / Mathf.Max(zoom, 0.001f); // M7.1: экранные px постоянны
            // ХОТФИКС владельца: ореол цели ОБНИМАЕТ планету на любом зуме (прежде
            // кольцо было постоянным экранного размера — на отъезде «больше» планеты,
            // при приближении терялось внутри диска). Диаметры = экранный диаметр
            // иконки цели × 1.25 / ×1.75 — тот же принцип, что у жёлтого ореола в игре
            float planetPx = IconSizeOf(body) * zoom;
            // Н10.5 вкусовое (владелец: «подсветку сильнее на жёлтых планетах»):
            // было 1.25/1.75×, альфы 0.62/0.35, пульс 4.2/3.4
            targetRingPulse.sizeDelta = Vector2.one * (planetPx * 1.35f);
            targetRingWide.sizeDelta = Vector2.one * (planetPx * 1.95f);
            // пульс: кольца дышат размером и альфой (чуть быстрее прежнего)
            float t = Time.unscaledTime;
            targetRingPulse.localScale = Vector3.one * (1f + 0.09f * Mathf.Sin(t * 5.5f));
            if (targetRingPulseImage != null)
                targetRingPulseImage.color = WithAlpha(TargetRingColor, 0.85f + 0.15f * Mathf.Sin(t * 4.5f));
            if (targetRingWideImage != null)
                targetRingWideImage.color = WithAlpha(TargetRingColor, 0.55f + 0.2f * Mathf.Sin(t * 4.5f + Mathf.PI));
        }

        internal void BuildPlayerMarker()
        {
            // маркер живёт на ЛИСТЕ, не в зум-контенте: RectMask2D контента (инсет 26 px
            // от края листа) резал ракету и подпись «ВЫ» у краёв; позицию считаем сами
            playerMarker = MakeRect("PlayerMarker", sheetRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            playerMarker.SetAsLastSibling(); // поверх рамки листа, под экранным (легенда/записка)
            playerMarker.sizeDelta = Vector2.one * RocketMarkerPx;

            // тень-подставка на стоянке (Н6.K7): мягкое чернильное пятно под ракетой,
            // когда корабль Landed — скромный индикатор «припаркован»
            landedPad = MakeImage("LandedPad", playerMarker, PaperMapArt.InkDot(),
                WithAlpha(PaperMapArt.Ink, 0f), new Vector2(RocketMarkerPx * 1.7f, RocketMarkerPx * 0.55f), false);
            landedPad.SetAsFirstSibling();

            playerArrowRoot = MakeRect("ArrowRoot", playerMarker, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            Sprite rocket = PaperMapArt.MapSprite("map_rocket");
            Vector2 size;
            if (rocket != null)
            {
                // Н6.K6: ракета крупнее (+33% к прежним 36×48)
                size = rocket.rect.height > rocket.rect.width
                    ? new Vector2(48f, RocketMarkerPx)
                    : new Vector2(RocketMarkerPx, 48f);
                // M8.1: родной цвет арта ракеты — «вы здесь» держат размер,
                // подпись и движение маркера
                MakeImage("Rocket", playerArrowRoot, rocket, Color.white, size, false);
            }
            else
            {
                // арт не найден — шеврон в чернилах
                Sprite chev = WaypointIndicator.ChevronSprite() != null
                    ? WaypointIndicator.ChevronSprite()
                    : StubSprites.TargetChevron();
                MakeImage("Rocket", playerArrowRoot, chev, PaperMapArt.Ink, Vector2.one * 40f, false);
            }

        }

        // ракета видна всегда: размер не зависит от зума (маркер вне зум-контента),
        // за краем вида маркер клампится к листу и смотрит в сторону реальной позиции
        internal void UpdatePlayerMarker()
        {
            Transform shipT = ShipPosition();
            if (shipT == null || playerMarker == null || sheetRect == null)
                return;

            if (ship != null && ship.State == ShipState.Landed)
            {
                // M8.5: поза посадки — нос маркера по ориентации КОРПУСА
                // (SpriteNoseAngle, как на радаре Н7 G7.2), а не по последнему
                // вектору полёта: прежде ракета на карте «падала» носом вниз
                playerHeading = HullHeadingDeg();
            }
            else if (ship != null && ship.State == ShipState.Flying && ship.Body != null
                && ship.Body.linearVelocity.sqrMagnitude > 25f)
            {
                Vector2 v = ship.Body.linearVelocity;
                playerHeading = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            }

            // тень-подставка: корабль стоит на планете (Н6.K7) — мягкий пульс альфы
            if (landedPadImage == null && landedPad != null)
                landedPadImage = landedPad.GetComponent<Image>();
            if (landedPadImage != null)
            {
                bool parked = ship != null && ship.State == ShipState.Landed;
                float alpha = parked ? 0.20f + 0.08f * Mathf.Sin(Time.unscaledTime * 2.2f) : 0f;
                Color c = landedPadImage.color;
                c.a = alpha;
                landedPadImage.color = c;
                landedPad.gameObject.SetActive(parked);
            }

            // содержимое листа рисуется в sheet-координатах как zoomOffset + zoom * p,
            // маркер — напрямую в sheet-координатах: контр-масштаб не нужен
            Vector2 sheetPos = zoomOffset + zoom * WorldToMap(shipT.position);
            Vector2 clamped = SheetClampView(sheetPos); // кламп внутрь окна вида
            playerMarker.anchoredPosition = clamped;

            bool offView = (sheetPos - clamped).sqrMagnitude > 1f;
            float angle;
            if (offView) // за краем вида — ракета указывает на реальную позицию
            {
                Vector2 d = sheetPos - clamped;
                angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - RocketNoseDeg;
            }
            else
            {
                angle = playerHeading - RocketNoseDeg; // нос арт-ракеты смотрит в 55°
            }
            if (playerArrowRoot != null)
                playerArrowRoot.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        // M7.3: фиксация при посадке — чернильное кольцо «вы припаркованы здесь»
        // вокруг планеты стоянки (в content-координатах: «территория», скейлится
        // зумом честно, как иконка). Пересоздаётся на смену LandedOn; стоялка
        // читается поллом — событие взлёта не существует.
        // v5.2 (владелец: «убери обводку на карте у центральной планеты МТО»):
        // на хабе кольцо не рисуется — ракета-маркер и так стоит на планете
        internal void UpdateLandedRing()
        {
            if (content == null)
                return;
            CelestialBody parkedOn = ship != null && ship.State == ShipState.Landed
                && ship.LandedOn != null && !ship.LandedOn.IsHub
                    ? ship.LandedOn
                    : null;
            if (parkedOn == null)
            {
                if (landedRing != null && landedRing.gameObject.activeSelf)
                    landedRing.gameObject.SetActive(false);
                landedRingBody = null;
                return;
            }

            if (landedRing == null)
            {
                landedRing = MakeRect("LandedRing", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                Image ring = landedRing.gameObject.AddComponent<Image>();
                ring.sprite = PaperMapArt.TargetRing(); // кольцо «от руки»
                ring.color = WithAlpha(PaperMapArt.Ink, 0.85f); // M7.5: чернила, без второго цвета «ВЫ»
                ring.raycastTarget = false;
                TMP_Text note = MakeLabel("ParkNote", landedRing, "припаркованы", 14f,
                    WithAlpha(PaperMapArt.Ink, 0.9f));
                landedRingNote = (RectTransform)note.transform;
                landedRingNote.sizeDelta = new Vector2(150f, 20f);
            }

            // кольцо — «территория» (честно скейлится зумом, как иконка планеты),
            // подпись — константный экранный размер (counter-scale, как маркеры M7.1)
            if (landedRingNote != null)
                landedRingNote.localScale = Vector3.one / Mathf.Max(zoom, 0.001f);

            if (landedRingBody != parkedOn) // новая стоянка — перерисовать кольцо по её радиусу
            {
                landedRingBody = parkedOn;
                landedRing.gameObject.SetActive(true);
                float d = parkedOn.Radius * 2f * scale + 9f; // честный диаметр + 8-10px зазор
                Image ring = landedRing.GetComponent<Image>();
                if (ring != null)
                    ring.rectTransform.sizeDelta = Vector2.one * d;
                if (landedRingNote != null)
                    landedRingNote.anchoredPosition = Vector2.down * (d * 0.5f + 12f);
            }
            landedRing.anchoredPosition = WorldToMap(parkedOn.transform.position);
        }

        internal void UpdateComets()
        {
            if (cometsRoot == null)
                return;
            if (cometSystem == null)
                cometSystem = UnityEngine.Object.FindAnyObjectByType<CometSystem>();
            if (cometSystem == null)
                return;

            float inv = 1f / Mathf.Max(zoom, 0.001f);
            List<Comet> dead = null;
            foreach (KeyValuePair<Comet, RectTransform> kv in cometIcons)
            {
                if (kv.Key == null)
                {
                    (dead ??= new List<Comet>()).Add(kv.Key);
                    if (kv.Value != null)
                        UnityEngine.Object.Destroy(kv.Value.gameObject);
                    continue;
                }
                Vector2 world = kv.Key.transform.position;
                kv.Value.anchoredPosition = WorldToMap(world);
                kv.Value.localScale = Vector3.one * inv; // размер на экране постоянен
                Vector2 delta = world - cometLastPos[kv.Key];
                if (delta.sqrMagnitude > 0.01f)
                    kv.Value.localRotation = Quaternion.Euler(0f, 0f,
                        Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - CometHeadDeg);
                cometLastPos[kv.Key] = world;
            }
            if (dead != null)
                foreach (Comet gone in dead)
                {
                    cometIcons.Remove(gone);
                    cometLastPos.Remove(gone);
                }

            foreach (Comet comet in cometSystem.ActiveComets)
            {
                if (comet == null || cometIcons.ContainsKey(comet))
                    continue;
                Sprite art = PaperMapArt.MapSprite("map_cometa");
                // M8.1: арту кометы — родной цвет; процедурный фолбэк (белая
                // маска) тонируется чернилами, иначе не виден на бумаге
                bool hasArt = art != null;
                if (!hasArt)
                    art = PaperMapArt.CometIcon();
                Color cometColor = hasArt ? Color.white : WithAlpha(PaperMapArt.Ink, 0.62f);
                RectTransform icon = MakeImage("Comet_" + cometIcons.Count, cometsRoot, art,
                    cometColor, Vector2.one * 30f, false);
                Vector2 world = comet.transform.position;
                icon.anchoredPosition = WorldToMap(world);
                icon.localScale = Vector3.one * inv;
                cometIcons[comet] = icon;
                cometLastPos[comet] = world;
            }
        }

        // пересборка карты: кольца/маркеры/край мира гаснут, кометы перерисуются
        // (часть RebuildAll хоста, Н11); тир цели сбрасывает PaperMapLabels.Reset
        internal void Reset()
        {
            edgeFogRoot = null;
            edgeRoot = null;
            cometsRoot = null;
            cometIcons.Clear();
            cometLastPos.Clear();
            playerMarker = null;
            playerArrowRoot = null;
            landedPad = null;
            landedPadImage = null;
            landedRing = null;
            landedRingNote = null;
            landedRingBody = null;
            targetRing = null;
            targetRingPulse = null;
            targetRingWide = null;
            targetRingPulseImage = null;
            targetRingWideImage = null;
            waypointRoot = null;
            waypointHalo = null;
            waypointWide = null;
            waypointPin = null;
            waypointHaloImage = null;
            waypointWideImage = null;
            waypointPinImage = null;
        }

        // (Н11-step5-rings-applied)
    }
}
