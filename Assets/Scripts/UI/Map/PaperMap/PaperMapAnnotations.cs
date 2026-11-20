using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.UI.PaperMap
{
    // подсистема «аннотации и смерть» бумажной карты: свои надписи/контуры
    // (Map Style Editor), эффект гибели корабля (кольца с искрами) и кляксы-кресты
    // «здесь я погиб» до закрытия карты. Подписку на смерть держит хост.
    internal sealed class PaperMapAnnotations
    {
        private readonly MapScreenBinder host;
        private readonly PaperMapProjection view;
        private readonly PaperMapRings rings;

        internal PaperMapAnnotations(MapScreenBinder host, PaperMapProjection view, PaperMapRings rings)
        {
            this.host = host;
            this.view = view;
            this.rings = rings;
        }

        // шимы к хосту/виду/кольцам
        private MapStyleConfig Style => host.Style;
        private float zoom => view.Zoom;
        private float scale => view.Scale;
        private Vector2 zoomOffset => view.ZoomOffset;
        private Vector2 worldCenter => view.WorldCenter;
        private RectTransform content => view.Content;
        private RectTransform sheetRect => view.SheetRect;
        private Vector2 WorldToMap(Vector2 world) => view.WorldToMap(world);
        private Vector2 SheetClampView(Vector2 sheetPos) => view.SheetClampView(sheetPos);
        private bool built => host.IsBuilt;
        private GameObject gameObject => host.gameObject;
        private HullSystem hull => host.Hull;
        private Ship ship => host.Ship;
        private RectTransform playerMarker => rings.PlayerMarker;
        private void PersistStyle() => host.PersistStyle();
        private static int StableHash(string text) => PaperMapUi.StableHash(text);

        // хелперы PaperMapUi
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

        private RectTransform annotationsRoot;
        private readonly List<AnnotationNode> annotationNodes = new List<AnnotationNode>();

        // аннотация на карте: рект для окна видимости + режим кегля
        private struct AnnotationNode
        {
            public RectTransform rect;
            public bool screenFixed;
        }

        private sealed class MapBurst
        {
            public RectTransform Root;
            public Image Ring;
            public Image RingLate;              // второе кольцо, отстаёт на задержку
            public readonly List<Image> Sparks = new List<Image>();
            public readonly List<Vector2> SparkVel = new List<Vector2>();
            public float Age;
            public Vector2 World;               // мировая точка гибели — для кляксы после кольца
        }
        private readonly List<MapBurst> bursts = new List<MapBurst>();

        // смерть при закрытой карте — burst доигрывает при открытии
        private const int BurstSparks = 18;     // искр на взрыв
        private Vector2? pendingDeathWorld;
        private const int MaxDeathMarks = 4;    // память листа: последние гибели
        private readonly List<Vector2> deathMarkWorlds = new List<Vector2>();
        private readonly List<RectTransform> deathMarkRects = new List<RectTransform>();
        // слой аннотаций поверх подписей: клики не ест (raycastTarget=false),
        // пан/зум работают сквозь него как по листу
        internal void BuildAnnotations()
        {
            if (annotationsRoot != null)
                Object.Destroy(annotationsRoot.gameObject);
            annotationNodes.Clear();
            annotationsRoot = MakeRect("Annotations", content, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            annotationsRoot.SetAsLastSibling();
            var list = Style.annotations;
            for (int i = 0; i < list.Count; i++)
                annotationNodes.Add(BuildAnnotation(list[i], i));
            UpdateAnnotations();
        }

        private AnnotationNode BuildAnnotation(MapAnnotation ann, int index)
        {
            string name = "Ann_" + index;
            Vector2 pos = WorldToMap(ann.worldPosition);
            var node = new AnnotationNode();
            switch (ann.kind)
            {
                case AnnotationKind.Label:
                {
                    TMP_Text label = MakeLabel(name, annotationsRoot, ann.text, ann.fontSize, ann.color);
                    if (ann.screenFixedSize) // экранный кегль: контентный = кегль/zoom
                        label.fontSize = ann.fontSize / Mathf.Max(zoom, 0.001f);
                    RectTransform rect = (RectTransform)label.transform;
                    rect.anchoredPosition = pos;
                    node.rect = rect;
                    node.screenFixed = ann.screenFixedSize;
                    break;
                }
                case AnnotationKind.Ring:
                {
                    // рисованный карандашный круг — тот же спрайт, что у колец меток;
                    // живёт в масштабе мира (географический контур района)
                    RectTransform ring = MakeImage(name, annotationsRoot, PaperMapArt.TargetRing(), ann.color,
                        Vector2.one * (Mathf.Max(1f, ann.radius) * 2f * scale), false);
                    ring.anchoredPosition = pos;
                    node.rect = ring;
                    break;
                }
                case AnnotationKind.Circle:
                {
                    RectTransform fill = MakeImage(name, annotationsRoot, StubSprites.SoftDot(64f, 0f), ann.color,
                        Vector2.one * (Mathf.Max(1f, ann.radius) * 2f * scale), false);
                    fill.anchoredPosition = pos;
                    node.rect = fill;
                    break;
                }
                case AnnotationKind.Rect:
                {
                    // рамка из чернильных линий (толщина в экранных px); группа —
                    // точка мира, линии в её локальных координатах
                    RectTransform group = MakeRect(name, annotationsRoot, new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                    group.anchoredPosition = pos;
                    float halfW = Mathf.Max(1f, ann.radius) * scale;
                    float halfH = Mathf.Max(1f, ann.height) * scale;
                    Color c = ann.color;
                    MakeSegment(group, new Vector2(-halfW, halfH), new Vector2(halfW, halfH), c, ann.thicknessPx);
                    MakeSegment(group, new Vector2(-halfW, -halfH), new Vector2(halfW, -halfH), c, ann.thicknessPx);
                    MakeSegment(group, new Vector2(-halfW, -halfH), new Vector2(-halfW, halfH), c, ann.thicknessPx);
                    MakeSegment(group, new Vector2(halfW, -halfH), new Vector2(halfW, halfH), c, ann.thicknessPx);
                    node.rect = group;
                    break;
                }
            }
            return node;
        }

        // окно видимости по зуму + экранный кегль fixed-надписей — каждый кадр
        internal void UpdateAnnotations()
        {
            if (annotationsRoot == null)
                return;
            var list = Style.annotations;
            if (list.Count != annotationNodes.Count)
            {
                BuildAnnotations(); // правка из окна (добавили/удалили) — слой рассинхронился
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                MapAnnotation ann = list[i];
                RectTransform rect = annotationNodes[i].rect;
                if (rect == null)
                    continue;
                bool visible = (ann.minZoom <= 0f || zoom >= ann.minZoom - 0.001f)
                    && (ann.maxZoom <= 0f || zoom <= ann.maxZoom + 0.001f);
                if (rect.gameObject.activeSelf != visible)
                    rect.gameObject.SetActive(visible);
                if (annotationNodes[i].screenFixed && visible)
                {
                    TMP_Text label = rect.GetComponent<TMP_Text>();
                    if (label != null)
                    {
                        float target = ann.fontSize / Mathf.Max(zoom, 0.001f);
                        if (!Mathf.Approximately(label.fontSize, target))
                            label.fontSize = target;
                    }
                }
            }
        }

        // точка гибели: корпус ещё на месте (телепорт респавна — через 0.6с).
        // Карта открыта — burst сразу; закрыта/не построена — burst доиграет при
        // открытии, клякса-крест «здесь я погиб» появляется в любом случае
        internal void OnShipDied()
        {
            Vector2 world = hull != null && hull.transform != null
                ? (Vector2)hull.transform.position
                : ship != null ? (Vector2)ship.transform.position : worldCenter;
            AddDeathMark(world);
            if (!built || sheetRect == null || !gameObject.activeInHierarchy)
            {
                pendingDeathWorld = world;
                return;
            }
            SpawnDeathBurst(world);
        }

        // два расходящихся кольца + искры в точке гибели; позиция клампится как у
        // ракеты — за ViewInset точка рисуется у края вида, а не прячется
        private void SpawnDeathBurst(Vector2 world)
        {
            if (playerMarker == null || sheetRect == null)
                return;
            Vector2 center = SheetClampView(zoomOffset + zoom * WorldToMap(world));
            var burst = new MapBurst
            {
                Root = MakeRect("MapBurst", sheetRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero),
                World = world
            };
            burst.Root.anchoredPosition = center;
            burst.Root.SetAsLastSibling();

            burst.Ring = MakeImage("Ring", burst.Root, PaperMapArt.TargetRing(),
                WithAlpha(PaperMapArt.Alarm, 0.9f), Vector2.one * 14f, false).GetComponent<Image>();
            burst.RingLate = MakeImage("RingLate", burst.Root, PaperMapArt.TargetRing(),
                WithAlpha(PaperMapArt.Alarm, 0.75f), Vector2.one * 10f, false).GetComponent<Image>();
            burst.RingLate.gameObject.SetActive(false); // родится с задержкой RingLateDelay
            var rng = new System.Random(StableHash("burst") + (int)(Time.unscaledTime * 1000f));
            for (int i = 0; i < BurstSparks; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float speed = 70f + (float)rng.NextDouble() * 150f;
                Image spark = MakeImage("Spark_" + i, burst.Root,
                    i % 3 == 0 ? PaperMapArt.InkDot() : PaperMapArt.MapSprite("map_asteroid"),
                    WithAlpha(PaperMapArt.Alarm, 0.9f), Vector2.one * (5f + (float)rng.NextDouble() * 6f), false)
                    .GetComponent<Image>();
                burst.Sparks.Add(spark);
                burst.SparkVel.Add(new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * speed);
            }
            bursts.Add(burst);
        }

        // кадр жизни эффектов; в конце — клякса-крест до закрытия карты
        internal void UpdateMapEffects()
        {
            const float Duration = 1.2f;
            const float RingMaxPx = 180f;
            const float LateDelay = 0.2f;
            if (pendingDeathWorld.HasValue) // смерть при закрытой карте — burst при открытии
            {
                SpawnDeathBurst(pendingDeathWorld.Value);
                pendingDeathWorld = null;
            }
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                MapBurst burst = bursts[i];
                if (burst.Root == null) // пережил пересборку карты — просто забываем
                {
                    bursts.RemoveAt(i);
                    continue;
                }
                burst.Age += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(burst.Age / Duration);
                if (burst.Ring != null)
                {
                    burst.Ring.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(14f, RingMaxPx, Mathf.SmoothStep(0f, 1f, k));
                    var c = burst.Ring.color;
                    c.a = 0.9f * (1f - k);
                    burst.Ring.color = c;
                }
                if (burst.RingLate != null)
                {
                    bool lateOn = burst.Age >= LateDelay;
                    if (burst.RingLate.gameObject.activeSelf != lateOn)
                        burst.RingLate.gameObject.SetActive(lateOn);
                    if (lateOn)
                    {
                        float k2 = Mathf.Clamp01((burst.Age - LateDelay) / Duration);
                        burst.RingLate.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(10f, RingMaxPx * 0.7f, Mathf.SmoothStep(0f, 1f, k2));
                        var c = burst.RingLate.color;
                        c.a = 0.75f * (1f - k2);
                        burst.RingLate.color = c;
                    }
                }
                for (int s = 0; s < burst.Sparks.Count; s++)
                {
                    Image spark = burst.Sparks[s];
                    if (spark == null)
                        continue;
                    spark.rectTransform.anchoredPosition += burst.SparkVel[s] * Time.unscaledDeltaTime;
                    var c = spark.color;
                    c.a = 0.9f * (1f - k);
                    spark.color = c;
                }
                if (k >= 1f)
                {
                    Object.Destroy(burst.Root.gameObject); // static-класс: не MonoBehaviour
                    bursts.RemoveAt(i);
                }
            }
            SyncDeathMarks(); // кляксы живы до закрытия карты, переживают пересборку
        }
        // ————— кляксы «здесь я погиб» —————

        private void AddDeathMark(Vector2 world)
        {
            deathMarkWorlds.Add(world);
            deathMarkRects.Add(null);
            while (deathMarkWorlds.Count > MaxDeathMarks)
            {
                deathMarkWorlds.RemoveAt(0);
                if (deathMarkRects[0] != null)
                    Object.Destroy(deathMarkRects[0].gameObject);
                deathMarkRects.RemoveAt(0);
            }
        }

        // чернильная клякса + крест в точке гибели (Alarm, до закрытия карты);
        // позиция клампится как у ракеты и пересчитывается при пан/зуме
        private void SyncDeathMarks()
        {
            if (sheetRect == null)
                return;
            for (int i = 0; i < deathMarkWorlds.Count; i++)
            {
                if (i >= deathMarkRects.Count)
                    deathMarkRects.Add(null);
                RectTransform rect = deathMarkRects[i];
                if (rect == null) // пересборка карты стёрла — перерисовать
                    deathMarkRects[i] = rect = MakeDeathMark();
                rect.anchoredPosition = SheetClampView(zoomOffset + zoom * WorldToMap(deathMarkWorlds[i]));
            }
        }

        private RectTransform MakeDeathMark()
        {
            RectTransform root = MakeRect("DeathMark", sheetRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            root.SetAsLastSibling();
            var ink = WithAlpha(PaperMapArt.Alarm, 0.9f);
            MakeImage("Blot", root, PaperMapArt.InkDot(), WithAlpha(PaperMapArt.Alarm, 0.8f), Vector2.one * 26f, false);
            MakeSegment(root, new Vector2(-8f, -8f), new Vector2(8f, 8f), ink, 2f);
            MakeSegment(root, new Vector2(-8f, 8f), new Vector2(8f, -8f), ink, 2f);
            return root;
        }

        // пересборка карты: слой аннотаций перестроится при следующем
        // BuildAnnotations; мировые точки гибелей перерисуются на новом листе
        internal void Reset()
        {
            bursts.Clear();
            annotationsRoot = null;
            annotationNodes.Clear();
            deathMarkRects.Clear(); // мировые точки гибелей живут — перерисуются на новом листе
        }

        // карта закрылась — кляксы гибелей и отложенный burst уходят
        internal void OnMapDisabled()
        {
            foreach (RectTransform mark in deathMarkRects)
                if (mark != null)
                    Object.Destroy(mark.gameObject);
            deathMarkRects.Clear();
            deathMarkWorlds.Clear();
            pendingDeathWorld = null;
        }
    }
}
