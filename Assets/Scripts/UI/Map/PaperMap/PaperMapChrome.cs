using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SpaceTaxi.Audio;
using SpaceTaxi.Core;
using SpaceTaxi.World;

namespace SpaceTaxi.UI.PaperMap
{
    // подсистема «хром» бумажной карты: тёмный стол, лист бумаги, окно вида
    // с зум-контейнером, заголовок, легенда, линейка, кнопки зума/следования,
    // районы. Строит лист и зум-контейнер, инъекцирует их в PaperMapProjection.
    internal sealed class PaperMapChrome
    {
        private readonly MapScreenBinder host;
        private readonly PaperMapProjection view;

        internal PaperMapChrome(MapScreenBinder host, PaperMapProjection view)
        {
            this.host = host;
            this.view = view;
        }

        // шимы к хосту/виду
        private MapStyleConfig Style => host.Style;
        private LevelAsset level => host.Level;
        private RectTransform mapRect => host.MapRect;
        private TMP_FontAsset font => host.Font;
        private bool built => host.IsBuilt;
        private float zoom => view.Zoom;
        private float scale => view.Scale;
        private float mapRadius => view.MapRadius; // бумага — от радиуса мира
        private float minViewZoom => view.MinViewZoom;
        private Vector2 WorldToMap(Vector2 world) => view.WorldToMap(world);
        private Camera canvasCam => view.CanvasCam;
        private Button followButton { get => view.FollowButton; set => view.FollowButton = value; }
        private TMP_Text followLabel { get => view.FollowLabel; set => view.FollowLabel = value; }
        private void RefreshFollowButton() => view.RefreshFollowButton();
        private void ToggleFollow() => view.ToggleFollow();
        private void ZoomStep(float factor) => view.ZoomStep(factor);
        private void AnimateZoom(float targetScale, Vector2 targetOffset) => view.AnimateZoom(targetScale, targetOffset);
        private Color TargetRingColor => Style.targetColor; // тот же тон, что у колец цели (PaperMapRings)
        private float MaxViewZoom => Style.maxViewZoom; // та же ручка стиля, что у PaperMapProjection
        private const float LabelFadeSeconds = PaperMapUi.LabelFadeSeconds; // общий темп фейдов

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
            => PaperMapUi.MakeLabel(font, name, parent, text, size, color, align);

        // лист и бортовой компьютер: бумага идёт от края до края, без полей

        private const float SheetMargin = PaperMapUi.SheetMargin;   // константы листа — в PaperMapUi
        private const float ContentMargin = PaperMapUi.ContentMargin;

        private const float OnboardGap = 8f;           // зазор листа к краю борткомпа
        private const float OnboardFallbackWidth = 480f; // ширина панели борткомпа (OnboardComputer.prefab)
        // бумага — тайл от мира (не от окна): зерно привязано к мировым юнитам
        private float PaperWorldMargin => Style.paperWorldMargin;  // радиус бумаги = радиус мира × (поля за туманом)
        private float WorldUnitsPerTile => Style.worldUnitsPerTile; // мировых юнитов на один тайл бумаги
        private const int PaperTilePx = 512;            // размер кропа-тайла в пикселях текстуры
        private const float ButtonZoomStep = 1.35f;

        // линейка — тонкая, только на крупных зумах
        private float RulerMinZoomFraction => Style.rulerMinZoomFraction; // видна при contentScale ≥

        // районы — «география» листа: видны издалека, на приближении гаснут
        private float SectorFadeOutFraction => Style.districtFadeOutFraction; // исчезли при contentScale ≥

        private RectTransform sheetRect;   // лист бумаги: ровно окно карты минус борткомп
        private RectTransform viewport;    // фиксированное окно вида (маска): клип не движется с камерой
        private RectTransform paperRect;   // фон-бумага — ребёнок зум-контента: «один слой» с миром
        private RectTransform content;     // зум-контейнер: бумага + весь нарисованный мир
        private RectTransform sectorRoot;  // подписи районов
        private CanvasGroup sectorFade;    // районы гаснут на приближении

        private RectTransform backdropRect; // тёмный стол — только правее борткомпа
        private RectTransform legendRoot;
        private RectTransform rulerRoot;
        private CanvasGroup rulerFade;     // плавный показ линейки на крупных зумах
        private RectTransform zoomRoot;
        private RectTransform titleRoot;

        private OnboardComputerPanel onboardPanel; // его зона вычитается из окна карты
        private bool onboardShown;                 // последнее известное состояние панели

        // наружу подсистемам: слоты подписей читают легенду и заголовок
        internal RectTransform LegendRoot => legendRoot;
        internal RectTransform TitleRoot => titleRoot;

        // пересборка карты: лист строится заново
        internal void Reset()
        {
            sheetRect = null;
            viewport = null;
            paperRect = null;
            content = null;
            sectorRoot = null;
            sectorFade = null;
            sectorTexts.Clear();
            legendRoot = null;
            rulerRoot = null;
            zoomRoot = null;
            titleRoot = null;
            rulerFade = null;
            view.Content = null;
            view.SheetRect = null;
        }

        internal void BuildChrome()
        {
            // тёмный стол вокруг листа: ловит рейкаст, чтобы ЛКМ-драг панировал
            // карту и за пределами листа; начинается правее борткомпа — панель
            // под картой остаётся яркой
            backdropRect = MakeRect("Backdrop", mapRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image backImage = backdropRect.gameObject.AddComponent<Image>();
            backImage.color = PaperMapArt.Desk;
            backImage.raycastTarget = true;

            // лист жёлтой бумаги: ровно окно карты минус зона борткомпа
            sheetRect = MakeRect("Sheet", mapRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image sheetImage = sheetRect.gameObject.AddComponent<Image>();
            sheetImage.color = PaperMapArt.Paper; // подложка; текстура бумаги панится в PaperClip
            sheetImage.raycastTarget = false; // клики сквозь лист уходят в Backdrop
            LayoutSheet();

            // фиксированное окно вида с маской, внутри — единственный
            // зум-контейнер; бумага и весь мир — его дети, клип стоит на месте
            viewport = MakeRect("Viewport", sheetRect, Vector2.zero, Vector2.one,
                new Vector2(ContentMargin, ContentMargin), new Vector2(-ContentMargin, -ContentMargin));
            viewport.gameObject.AddComponent<RectMask2D>(); // окно вида не движется с камерой

            content = MakeRect("MapRoot", viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            paperRect = MakeRect("Paper", content, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
            Image paperImage = paperRect.gameObject.AddComponent<Image>();
            // бесшовный тайл бумаги (кроп background.png) — Image.Tiled,
            // тайл = 512 мировых юнитов (пересчёт в BuildMap);
            // тайла нет — ровный цвет Paper
            Sprite paperTile = PaperMapArt.PaperTile();
            paperImage.type = paperTile != null ? Image.Type.Tiled : Image.Type.Simple;
            paperImage.sprite = paperTile != null ? paperTile : PaperMapArt.SheetArt();
            paperImage.color = paperImage.sprite != null ? Color.white : PaperMapArt.Paper;
            paperImage.raycastTarget = false;
            paperRect.sizeDelta = Vector2.one * 1000f; // временно; BuildMap ставит размер от мира

            BuildTitle();
            BuildZoomButtons();

            // инъекция в подсистему вида: зум-контейнер и лист (пан/зум двигают content)
            view.Content = content;
            view.SheetRect = sheetRect;
        }

        // лист = доступная область окна карты минус зона борткомпа;
        // пересчитывается при смене экрана и TAB-переключении панели
        internal void LayoutSheet()
        {
            if (sheetRect == null || mapRect == null)
                return;
            sheetRect.offsetMin = new Vector2(ReservedLeftInset() + OnboardGap, SheetMargin);
            sheetRect.offsetMax = new Vector2(-SheetMargin, -SheetMargin);
            // бумага не зависит от окна: размер считает BuildMap от мира
            // стол — от края панели и правее; полоса борткомпа вне затемнения
            if (backdropRect != null)
            {
                backdropRect.offsetMin = new Vector2(ReservedLeftInset() - SheetMargin, 0f);
                backdropRect.offsetMax = Vector2.zero;
            }
        }

        // ширина зоны борткомпа слева (панель — корень префаба OnboardComputer, 480 px)
        private float ReservedLeftInset()
        {
            if (onboardPanel == null)
                onboardPanel = UnityEngine.Object.FindAnyObjectByType<OnboardComputerPanel>();
            if (onboardPanel == null || !onboardPanel.IsShown)
                return 0f;
            RectTransform panelRect = onboardPanel.transform as RectTransform;
            float width = panelRect != null ? panelRect.rect.width : OnboardFallbackWidth;
            return width > 1f ? width : OnboardFallbackWidth;
        }

        // борткомп выезжает/убирается по TAB — окно карты меняется, лист перекладываем
        internal bool SyncOnboardPanel()
        {
            if (onboardPanel == null)
                onboardPanel = UnityEngine.Object.FindAnyObjectByType<OnboardComputerPanel>();
            if (onboardPanel == null || onboardPanel.IsShown == onboardShown)
                return false;
            onboardShown = onboardPanel.IsShown;
            LayoutSheet();
            return built;
        }

        // размер бумаги — диаметр мира × поля, тайл в мировых юнитах
        internal void ApplyPaperSize()
        {
            // бумага — от мира: диаметр мира × поля; тайл = WorldUnitsPerTile
            // мировых юнитов → tilePx = PaperTilePx / (PPU/refPPU × multiplier),
            // отсюда multiplier = PaperTilePx×refPPU / (PPU-спрайта×юнитов×scale)
            if (paperRect != null)
            {
                paperRect.sizeDelta = Vector2.one * (2f * mapRadius * scale * PaperWorldMargin);
                Image paperImage = paperRect.GetComponent<Image>();
                if (paperImage != null && paperImage.type == Image.Type.Tiled)
                {
                    Canvas canvas = paperImage.canvas;
                    float refPpu = canvas != null ? canvas.referencePixelsPerUnit : 100f;
                    paperImage.pixelsPerUnitMultiplier =
                        PaperTilePx * refPpu / (100f * WorldUnitsPerTile * scale);
                }
            }
        }

        // слой районов + CanvasGroup фейда
        internal void BuildSectorLayer(RectTransform content)
        {
            sectorRoot = MakeRect("Sectors", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            sectorFade = sectorRoot.gameObject.AddComponent<CanvasGroup>(); // фейд районов по зуму
            sectorFade.blocksRaycasts = false;
            sectorFade.interactable = false;
        }

        // ————— районы: крупные рукописные подписи —————

        internal void BuildSectorLabels()
        {
            var groups = new Dictionary<Sector, List<CelestialBodyData>>();
            foreach (CelestialBodyData body in level.Bodies)
            {
                if (body.Type == BodyType.Comet)
                    continue; // живые строки данных; пояса участвуют центром
                if (!groups.TryGetValue(body.Sector, out List<CelestialBodyData> list))
                    groups[body.Sector] = list = new List<CelestialBodyData>();
                list.Add(body);
            }

            foreach (var pair in groups)
            {
                if (pair.Key == Sector.Hub)
                    continue; // у хаба есть собственная подпись тела

                // кольцевой тест — только по планетам сектора: звёзды периферии
                // с большим радиусом раздули бы разброс
                float rMin = float.MaxValue, rMax = 0f;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                var planetsOnly = new List<CelestialBodyData>();
                foreach (CelestialBodyData body in pair.Value)
                {
                    Vector2 p = body.Position;
                    minX = Mathf.Min(minX, p.x);
                    maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y);
                    maxY = Mathf.Max(maxY, p.y);
                    if (body.Type != BodyType.Planet)
                        continue;
                    float r = p.magnitude;
                    rMin = Mathf.Min(rMin, r);
                    rMax = Mathf.Max(rMax, r);
                    planetsOnly.Add(body);
                }

                // надпись района: разрядка + низкая альфа — «география», не UI-элемент.
                // Кольцевой сектор — подпись на кольце под своим углом,
                // географический (разброс радиусов велик) — по bbox
                TMP_Text label = MakeLabel("SectorLabel_" + pair.Key, sectorRoot, SectorName(pair.Key), Style.districtFontSize,
                    WithAlpha(Style.districtColor, 0.35f));
                RectTransform labelRect = (RectTransform)label.transform;
                labelRect.sizeDelta = new Vector2(560f, 56f);

                bool isRing = planetsOnly.Count > 0 && rMax - rMin < 260f;
                if (isRing)
                {
                    // подпись НА кольце — контент-пространство (пан/зум везут её сами)
                    Vector2 world = new Vector2(Mathf.Cos(SectorLabelAngle((int)pair.Key) * Mathf.Deg2Rad),
                        Mathf.Sin(SectorLabelAngle((int)pair.Key) * Mathf.Deg2Rad)) * rMean(planetsOnly);
                    labelRect.anchoredPosition = WorldToMap(world);
                }
                else
                {
                    float xShift = ((int)pair.Key % 2 == 0 ? -1f : 1f) * (maxX - minX) * 0.20f;
                    labelRect.anchoredPosition = WorldToMap(new Vector2((minX + maxX) * 0.5f + xShift, maxY)) + Vector2.up * 34f;
                }
                labelRect.localRotation = Quaternion.Euler(0f, 0f, (Hash01((int)pair.Key * 7 + 3) - 0.5f) * 3f);
                label.characterSpacing = 16f;
                sectorTexts.Add(label); // кегль — константный экранный (контр-скейл в SyncSectorFade)
            }
        }

        private readonly List<TMP_Text> sectorTexts = new List<TMP_Text>();

        // углы районных подписей на кольцах — вразлёт по компасу (лево/право-верх/низ),
        // каждый угол — середина дуги между планетами своего кольца
        private static float SectorLabelAngle(int sector)
        {
            switch ((Sector)sector)
            {
                case Sector.Near: return 165f;  // лево, чуть выше горизонта
                case Sector.Mid1: return 75f;   // верх-право
                case Sector.Mid2: return 255f;  // низ-лево
                case Sector.Far: return 345f;   // право, чуть ниже горизонта
                default: return 75f + 90f * (sector % 4); // неизвестный сектор — по кругу
            }
        }

        private static float rMean(List<CelestialBodyData> bodies)
        {
            float sum = 0f;
            foreach (CelestialBodyData body in bodies)
                sum += body.Position.magnitude;
            return sum / Mathf.Max(1, bodies.Count);
        }

        // заголовок листа «система … · …» — видимость из стиля
        internal void SyncTitle()
        {
            if (titleRoot != null)
            {
                bool titleOn = Style.titleVisible && !string.IsNullOrEmpty(Style.titleText);
                if (titleRoot.gameObject.activeSelf != titleOn)
                    titleRoot.gameObject.SetActive(titleOn);
                TMP_Text title = titleRoot.GetComponentInChildren<TMP_Text>();
                if (title != null)
                    title.text = "система " + level.LevelName + " · " + Style.titleText;
            }
        }

        private void BuildTitle()
        {
            titleRoot = MakeRect("Title", sheetRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            titleRoot.anchoredPosition = new Vector2(0f, -18f);
            titleRoot.sizeDelta = new Vector2(880f, 40f);
            titleRoot.localRotation = Quaternion.Euler(0f, 0f, 0.4f);
            TMP_Text title = MakeLabel("T", titleRoot, "маршрутный лист", 26f, WithAlpha(PaperMapArt.Ink, 0.7f));
            title.characterSpacing = 3f;
        }

        // легенда: компактный ключ в одном углу — нацарапан прямо на бумаге,
        // без подложки и рамки; значки — те же источники, что рисуют карту

        // арт map_rocket нарисован с наклоном носа +55° (0=вправо) — в легенде
        // выпрямляем до вертикали
        private const float LegendRocketDeg = 35f;

        internal void BuildLegend()
        {
            legendRoot = MakeRect("Legend", sheetRect, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            legendRoot.pivot = new Vector2(0f, 0f);
            legendRoot.anchoredPosition = new Vector2(18f, 18f);
            legendRoot.sizeDelta = Style.legendSize;
            legendRoot.localScale = Vector3.one * 1.4f;
            legendRoot.gameObject.SetActive(Style.legendVisible); // выключена — слоты подписей освобождаются

            int y = 0;
            // один столбец; значки — те же картографические арты, что рисуют тела
            LegendRow(ref y, PaperMapArt.MapSprite("map_planet"), Color.white, "планета");
            LegendRow(ref y, PaperMapArt.MapSprite("map_taxi_planet"), Color.white, "хаб МТО");
            LegendRow(ref y, PaperMapArt.MapSprite("map_sun"), Color.white, "звезда");
            LegendRow(ref y, PaperMapArt.MapSprite("map_black_hole"), Color.white, "чёрная дыра");
            LegendRow(ref y, PaperMapArt.MapSprite("map_asteroid"), Color.white, "астероид");
            LegendRow(ref y, PaperMapArt.MapSprite("map_cometa"), Color.white, "комета");
            LegendRow(ref y, PaperMapArt.TargetRing(), TargetRingColor, "цель заказа", 22f);
            LegendRow(ref y, PaperMapArt.MapSprite("map_rocket"), Color.white, "корабль", 20f, LegendRocketDeg);
        }

        private void LegendRow(ref int row, Sprite icon, Color color, string text,
            float iconSize = 18f, float iconRotationDeg = 0f)
        {
            float y = 252f - row * 30f;
            RectTransform iconRect = MakeRect("I", legendRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            iconRect.sizeDelta = Vector2.one * 24f;
            iconRect.anchoredPosition = new Vector2(12f, y);
            iconRect.localRotation = Quaternion.Euler(0f, 0f, iconRotationDeg);
            MakeImage("G", iconRect, icon, color, Vector2.one * iconSize, false);
            if (icon == null)
                MakeImage("F", iconRect, StubSprites.Circle(64f), WithAlpha(PaperMapArt.Ink, 0.7f), Vector2.one * 14f, false);

            TMP_Text rowLabel = MakeLabel("R", legendRoot, text, 16f, WithAlpha(PaperMapArt.Ink, 0.9f),
                TextAlignmentOptions.Left);
            ((RectTransform)rowLabel.transform).sizeDelta = new Vector2(130f, 22f);
            ((RectTransform)rowLabel.transform).anchorMin = new Vector2(0f, 0f);
            ((RectTransform)rowLabel.transform).anchorMax = new Vector2(0f, 0f);
            ((RectTransform)rowLabel.transform).pivot = new Vector2(0f, 0.5f);
            ((RectTransform)rowLabel.transform).anchoredPosition = new Vector2(30f, y);
            row++;
        }

        internal void BuildRuler()
        {
            // масштабная линейка: круглый шаг мира, влезающий в 150 px
            int[] steps = { 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000 };
            float step = steps[0];
            foreach (int candidate in steps)
            {
                if (candidate * scale <= 150f)
                    step = candidate;
            }
            float px = step * scale;

            rulerRoot = MakeRect("Ruler", sheetRect, new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            rulerRoot.pivot = new Vector2(0.5f, 0f);
            rulerRoot.anchoredPosition = new Vector2(-28f - px * 0.5f, 30f);
            rulerRoot.sizeDelta = new Vector2(px + 8f, 44f);
            rulerFade = rulerRoot.gameObject.AddComponent<CanvasGroup>();
            rulerFade.blocksRaycasts = false;
            rulerFade.interactable = false;
            var ink = WithAlpha(PaperMapArt.Ink, 0.62f);

            RectTransform line = MakeRect("Line", rulerRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
            line.sizeDelta = new Vector2(px, 2f);
            Image lineImage = line.gameObject.AddComponent<Image>();
            lineImage.color = ink;
            lineImage.raycastTarget = false;

            foreach (float x in new[] { -px * 0.5f, px * 0.5f })
            {
                RectTransform tick = MakeRect("Tick", rulerRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
                tick.anchoredPosition = new Vector2(x, 4f);
                tick.sizeDelta = new Vector2(2f, 8f);
                Image tickImage = tick.gameObject.AddComponent<Image>();
                tickImage.color = ink;
                tickImage.raycastTarget = false;
            }

            TMP_Text label = MakeLabel("T", rulerRoot, step + " юн", 14f, WithAlpha(PaperMapArt.Ink, 0.62f));
            ((RectTransform)label.transform).sizeDelta = new Vector2(px + 60f, 20f);
            ((RectTransform)label.transform).anchoredPosition = new Vector2(0f, 14f);
        }

        // линейка привязана к зуму — плавный фейд, на отъезде выключена
        internal void SyncRuler()
        {
            if (rulerRoot == null || rulerFade == null)
                return;
            float target = zoom / MaxViewZoom >= RulerMinZoomFraction ? 1f : 0f;
            float next = Mathf.MoveTowards(rulerFade.alpha, target, Time.unscaledDeltaTime / LabelFadeSeconds);
            if (next != rulerFade.alpha)
            {
                rulerFade.alpha = next;
                bool active = next > 0.001f;
                if (rulerRoot.gameObject.activeSelf != active)
                    rulerRoot.gameObject.SetActive(active);
            }
        }

        // районы читаются на обзоре, на приближении гаснут
        internal void SyncSectorFade()
        {
            if (sectorRoot == null || sectorFade == null)
                return;
            float contentScale = zoom / MaxViewZoom;
            float target = Mathf.Clamp01((SectorFadeOutFraction - contentScale) / 0.20f);
            float next = Mathf.MoveTowards(sectorFade.alpha, target, Time.unscaledDeltaTime / LabelFadeSeconds);
            if (next != sectorFade.alpha)
            {
                sectorFade.alpha = next;
                bool active = next > 0.001f;
                if (sectorRoot.gameObject.activeSelf != active)
                    sectorRoot.gameObject.SetActive(active);
            }

            // районы в контент-пространстве: кегль — константный экранный
            // (контентный = Style.districtFontSize / zoom), как у подписей тел
            float districtSize = Style.districtFontSize / Mathf.Max(zoom, 0.001f);
            foreach (TMP_Text text in sectorTexts)
                if (text != null && !Mathf.Approximately(text.fontSize, districtSize))
                    text.fontSize = districtSize;
        }

        // кнопки зума: +/− к центру листа, «сброс» — максимальный отъезд,
        // «следить» — фиксация камеры на ракете
        private void BuildZoomButtons()
        {
            zoomRoot = MakeRect("ZoomButtons", sheetRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
            zoomRoot.pivot = new Vector2(0.5f, 0f);
            zoomRoot.anchoredPosition = new Vector2(0f, 16f);
            zoomRoot.sizeDelta = new Vector2(480f, 64f);
            zoomRoot.localScale = Vector3.one * Style.zoomButtonScale; // ручка размера кнопок зума

            MakePaperButton("BtnZoomIn", zoomRoot, new Vector2(52f, 48f), new Vector2(-168f, 8f), "+", 26f,
                () => ZoomStep(ButtonZoomStep), 701);
            MakePaperButton("BtnZoomOut", zoomRoot, new Vector2(52f, 48f), new Vector2(-104f, 8f), "-", 26f,
                () => ZoomStep(1f / ButtonZoomStep), 702);
            MakePaperButton("BtnZoomReset", zoomRoot, new Vector2(124f, 48f), new Vector2(-10f, 8f), "сброс", 20f,
                () => AnimateZoom(minViewZoom, Vector2.zero), 703); // сброс = максимальный отъезд
            followButton = MakePaperButton("BtnFollow", zoomRoot, new Vector2(150f, 48f), new Vector2(130f, 8f),
                "следить: выкл", 15f, ToggleFollow, 704);
            followLabel = followButton.GetComponentInChildren<TMP_Text>();
            RefreshFollowButton();
        }

        // кнопка-клочок: бумага + рамка «от руки» + чернильный текст
        private Button MakePaperButton(string name, RectTransform parent, Vector2 size, Vector2 pos,
            string label, float fontSize, Action onClick, int seed)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;

            Image bg = go.GetComponent<Image>();
            bg.color = PaperMapArt.PaperDark;
            bg.raycastTarget = true;
            MakeSketchFrame(rect, WithAlpha(PaperMapArt.Ink, 0.9f), 2f, seed);

            TMP_Text text = MakeLabel("T", rect, label, fontSize, PaperMapArt.Ink);
            RectTransform textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = bg; // tint-подсветка на hover/нажатие
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.fadeDuration = 0.1f;
            colors.highlightedColor = new Color(0.93f, 0.84f, 0.60f);
            colors.pressedColor = new Color(0.72f, 0.62f, 0.42f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());
            UiSfx.Hook(button);
            return button;
        }

        internal void RestackChrome()
        {
            // экранное поверх листа: заголовок, легенда, линейка, кнопки
            if (legendRoot != null) legendRoot.SetAsLastSibling();
            if (titleRoot != null) titleRoot.SetAsLastSibling();
            if (rulerRoot != null) rulerRoot.SetAsLastSibling();
            if (zoomRoot != null) zoomRoot.SetAsLastSibling();
        }

        internal bool PointerOverChrome(Vector2 screenPos)
        {
            return ContainsScreen(legendRoot, screenPos)
                || ContainsScreen(zoomRoot, screenPos);
        }

        private bool ContainsScreen(RectTransform rect, Vector2 screenPos)
        {
            return rect != null && rect.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, canvasCam);
        }

        internal bool PressedOnChrome(PointerEventData eventData)
        {
            Transform pressed = eventData.pointerPress != null ? eventData.pointerPress.transform : null;
            return pressed != null
                && ((legendRoot != null && pressed.IsChildOf(legendRoot.transform))
                    || (zoomRoot != null && pressed.IsChildOf(zoomRoot.transform)));
        }

        // рамка «от руки»: отрезки с дрожью руки по периметру + кляксы-углы
        private static void MakeSketchFrame(RectTransform parent, Color color, float thickness, int seed)
        {
            Rect r = parent.rect;
            var rng = new System.Random(seed);
            float cx = r.center.x, cy = r.center.y;
            Edge(new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax)); // верх
            Edge(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin)); // низ
            Edge(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax)); // лево
            Edge(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax)); // право

            void Edge(Vector2 a, Vector2 b)
            {
                Vector2 dir = (b - a).normalized;
                Vector2 perp = new Vector2(-dir.y, dir.x);
                int count = Mathf.Max(3, Mathf.RoundToInt((b - a).magnitude / 110f));
                Vector2 prev = a;
                for (int i = 1; i <= count; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, i / (float)count);
                    if (i < count)
                        p += perp * (Wob(rng) * 3.5f); // дрожь стыка
                    MakeSegment(parent, prev, p, color, thickness);
                    prev = p;
                }
            }

            // кляксы-уголки: линия «выведена» за угол
            foreach (Vector2 c in new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin),
                     new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax) })
            {
                var go = new GameObject("Corner", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                RectTransform rect = (RectTransform)go.transform;
                rect.sizeDelta = Vector2.one * 10f;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(c.x - cx, c.y - cy);
                Image img = go.AddComponent<Image>();
                img.sprite = PaperMapArt.InkDot();
                img.color = color;
                img.raycastTarget = false;
            }
        }

        private static float Wob(System.Random rng)
        {
            return (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        // детерминированный [0..1] по целому — наклоны подписей держатся между открытиями
        private static float Hash01(int seed)
        {
            uint h = (uint)seed ^ 0x9E3779B9u;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }

        private static string SectorName(Sector sector)
        {
            switch (sector)
            {
                case Sector.Hub: return "ХАБ";
                case Sector.Near: return "БЛИЖНИЙ";
                case Sector.Mid1: return "СРЕДНИЙ I";
                case Sector.Mid2: return "СРЕДНИЙ II";
                case Sector.Far: return "ДАЛЬНИЙ";
                default: return "";
            }
        }
    }
}
