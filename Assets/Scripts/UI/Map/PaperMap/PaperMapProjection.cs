using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SpaceTaxi.World;

namespace SpaceTaxi.UI.PaperMap
{
    // подсистема «вид» бумажной карты: проекция мира на лист, зум (колесо/
    // кнопки/анимация), пан ЛКМ-драгом, следование за ракетой. Мировые метрики
    // (масштаб вписывания, граница мира) и записи состояния вида — только здесь;
    // Content/SheetRect строит PaperMapChrome.
    internal sealed class PaperMapProjection
    {
        private readonly MapScreenBinder host;
        private Func<Vector2, bool> pointerOverChrome; // хром-тест колеса (хост/хром, SetPointerOverChrome)

        internal PaperMapProjection(MapScreenBinder host)
        {
            this.host = host;
        }

        internal void SetPointerOverChrome(Func<Vector2, bool> handler) // тест «курсор над хромом»
        {
            pointerOverChrome = handler;
        }

        // инъекция: лист/зум-контейнер строит PaperMapChrome, камеру — хост
        internal RectTransform Content { get => content; set => content = value; }
        internal RectTransform SheetRect { get => sheetRect; set => sheetRect = value; }
        internal Camera CanvasCam { get => canvasCam; set => canvasCam = value; }
        internal Button FollowButton { get => followButton; set => followButton = value; }
        internal TMP_Text FollowLabel { get => followLabel; set => followLabel = value; }

        // вид: базовый масштаб = весь мир вписан в лист, дальше зум к курсору.
        // Минимальный зум считается в BuildMap: полный отъезд показывает весь мир
        private const float MinZoomWorldFraction = 1.0f; // доля диаметра мира в окне при минимальном зуме
        // визуальные ручки — в MapStyleConfig (Map Style Editor)
        private float MaxViewZoom => Style.maxViewZoom;     // от максимального отъезда до одной планеты крупно
        private float InitialViewZoom => Style.initialViewZoom; // карта открывается приближенной к игроку
        private const float ZoomDuration = 0.22f;
        private const float WheelStep = 1.2f;

        private const float FitMargin = 1.10f;     // bbox тел занимает 90% листа
        private const float ViewInset = 76f;       // отступ клампа ракеты от края листа, px (ракета 64 + подпись «ВЫ» внизу)
        private const float RubberMargin = 1.15f;  // мягкий вынос за край при перетаскивании

        // режим «следить за ракетой»: пан и выбор тела сбрасывают фиксацию до
        // повторного клика кнопки — предсказуемо, без рывка возврата
        private const float FollowLerpSpeed = 5f;  // скорость догоняющего лерпа, 1/с (экспоненциальное сглаживание)

        private const float ClickDragGuard = 6f;   // сдвиг больше этого — пан, а не клик

        // инъекция: ссылки строятся хромом/хостом
        private Camera canvasCam; // null для Screen Space Overlay — так и нужно утилитам
        private RectTransform sheetRect;   // лист бумаги: ровно окно карты минус борткомп
        private RectTransform content;     // зум-контейнер: бумага + весь нарисованный мир

        private Vector2 worldCenter; // центр карты = origin мира (граница HullSystem — от него)
        private float mapRadius;
        private float bodyEdgeRadius; // фактический край тел/кометной зоны — от него туман
        private float scale = 1f;

        private float zoom = 1f;
        private float minViewZoom = 1f; // нижний порог зума отдаления; считается в BuildMap
        private float zoomFrom = 1f;
        private float zoomTo = 1f;
        private float zoomT = 1f;
        private Vector2 zoomOffset;
        private Vector2 offFrom;
        private Vector2 offTo;
        private bool zoomAnim;

        // пан: ЛКМ-драг по листу и столу; клик (сдвиг ≤ ClickDragGuard) — клик-выбор метки
        private bool dragging;
        private bool suppressClick; // нажатие переросло в пан — кликом не считать
        private float dragMoved;
        private Vector2 dragSheetPos;

        // режим «следить за ракетой»
        private bool followMode;
        private Button followButton;
        private TMP_Text followLabel;

        // наружу подсистемам/хосту: только чтение; записи — через методы выше
        internal float Zoom => zoom;
        internal Vector2 ZoomOffset => zoomOffset;
        internal float Scale => scale;
        internal Vector2 WorldCenter => worldCenter;
        internal float MinViewZoom => minViewZoom;
        internal float MapRadius => mapRadius;
        internal float BodyEdgeRadius => bodyEdgeRadius;
        internal bool SuppressClick => suppressClick;

        // уровень нужен расчёту метрик (bbox тел) — читаем у хоста
        private LevelAsset level => host.Level;
        private MapStyleConfig Style => host.Style;
        private bool built => host.IsBuilt;
        private static Color WithAlpha(Color color, float alpha) => PaperMapUi.WithAlpha(color, alpha);
        private const float ContentMargin = PaperMapUi.ContentMargin; // окно вида внутри листа (SheetClampView)

        // мировые метрики: граница мира, масштаб «весь мир вписан в лист»,
        // порог минимального отдаления; радиусы тел — хост (GameRadiusOf)
        internal void ComputeWorldMetrics()
        {
            // граница мира = кольцо буёв (HullSystem бросает «ПОТЕРЯНА СВЯЗЬ» от origin)
            mapRadius = level.WorldRadius;
            float extX = 0f, extY = 0f; // bbox всех тел уровня — то, что обязано влезть
            bodyEdgeRadius = 0f;        // фактический край тел — от него туман
            foreach (CelestialBodyData body in level.Bodies)
            {
                float gameRadius = host.GameRadiusOf(body);
                float edge = body.Position.magnitude + gameRadius;
                if (edge > mapRadius)
                    mapRadius = edge + 100f;
                bodyEdgeRadius = Mathf.Max(bodyEdgeRadius, edge);
                extX = Mathf.Max(extX, Mathf.Abs(body.Position.x) + gameRadius);
                extY = Mathf.Max(extY, Mathf.Abs(body.Position.y) + gameRadius);
            }
            if (level.CometAreaRadius > 0f) // зона комет — тоже часть содержимого
            {
                extX = Mathf.Max(extX, Mathf.Abs(level.CometAreaCenter.x) + level.CometAreaRadius);
                extY = Mathf.Max(extY, Mathf.Abs(level.CometAreaCenter.y) + level.CometAreaRadius);
                bodyEdgeRadius = Mathf.Max(bodyEdgeRadius, level.CometAreaCenter.magnitude + level.CometAreaRadius);
            }
            if (extX <= 1f)
                extX = mapRadius;
            if (extY <= 1f)
                extY = mapRadius;
            worldCenter = Vector2.zero;

            // «весь мир на листе»: bbox тел уровня + поля 10% вписывается целиком
            float w = content.rect.width, h = content.rect.height;
            scale = Mathf.Min(w / (2f * extX * FitMargin), h / (2f * extY * FitMargin));
            scale = Mathf.Max(scale, 0.012f);

            // порог отдаления: при минимальном зуме по каждой оси видно
            // ≤ MinZoomWorldFraction диаметра мира — полный отъезд показывает весь мир
            float minByWidth = w / (scale * 2f * extX * MinZoomWorldFraction);
            float minByHeight = h / (scale * 2f * extY * MinZoomWorldFraction);
            minViewZoom = Mathf.Min(MaxViewZoom, Mathf.Max(minByWidth, minByHeight));
        }

        internal Vector2 WorldToMap(Vector2 world)
        {
            return (world - worldCenter) * scale;
        }

        // ————— стартовый вид: приближено к игроку —————

        // карта открывается сфокусированной на позиции игрока (Ship); корабль
        // недоступен — планета старта; зум InitialViewZoom: окрестности игрока,
        // ближайшие планеты и подписи читаются
        internal void FocusOnPlayer()
        {
            if (!built || content == null || level == null)
                return;
            Vector2 mapPos = WorldToMap(host.PlayerWorldPosition());
            ApplyZoom(InitialViewZoom, ClampZoomOffset(-mapPos * InitialViewZoom, InitialViewZoom));
        }

        // ————— вид: зум колесом/кнопками + пан драгом —————

        // колесо: зум к курсору, точка под мышью остаётся на месте
        internal void HandleWheel()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || content == null)
                return;
            Vector2 scroll = mouse.scroll.ReadValue();
            if (scroll.y == 0f)
                return;
            Vector2 pos = mouse.position.ReadValue();
            if (PointerOverChrome(pos))
                return;
            ZoomAt(pos, scroll.y > 0f ? WheelStep : 1f / WheelStep);
        }

        // пан — только мышью (ЛКМ-драг): игра под картой ловит клавиши без конфликта

        private void ZoomAt(Vector2 screenPos, float factor)
        {
            if (content == null || sheetRect == null || !TryScreenToSheet(screenPos, out Vector2 m))
                return;
            zoomAnim = false;
            float s = Mathf.Clamp(zoom * factor, minViewZoom, MaxViewZoom);
            if (Mathf.Abs(s - zoom) < 0.0001f)
                return;
            Vector2 c = sheetRect.rect.center;
            Vector2 offset = (m - c) * (1f - s / zoom) + zoomOffset * (s / zoom);
            ApplyZoom(s, ClampZoomOffset(offset, s));
        }

        // кнопка +/−: зум к центру экрана (точка центра остаётся на месте)
        internal void ZoomStep(float factor)
        {
            if (content == null)
                return;
            float s = Mathf.Clamp(zoom * factor, minViewZoom, MaxViewZoom);
            if (Mathf.Abs(s - zoom) < 0.0001f)
                return;
            AnimateZoom(s, zoomOffset * (s / zoom));
        }

        // программный вид для тестов/оркестратора: зум и смещение контента
        internal void SetView(float viewZoom, Vector2 sheetOffset)
        {
            if (!built || content == null)
                return;
            float s = Mathf.Clamp(viewZoom, minViewZoom, MaxViewZoom);
            zoomAnim = false;
            ApplyZoom(s, ClampZoomOffset(sheetOffset, s));
        }

        // ————— режим «следить за ракетой» —————

        internal void ToggleFollow()
        {
            followMode = !followMode;
            RefreshFollowButton();
        }

        internal void RefreshFollowButton()
        {
            if (followLabel != null)
                followLabel.text = followMode ? "следить: вкл" : "следить: выкл";
            if (followButton != null)
            {
                var image = followButton.GetComponent<Image>();
                if (image != null) // включённый режим — кнопка затемнена чернилами, текст бумагой
                    image.color = followMode ? WithAlpha(PaperMapArt.Ink, 0.42f) : PaperMapArt.PaperDark;
            }
            if (followLabel != null)
                followLabel.color = followMode ? PaperMapArt.Paper : PaperMapArt.Ink;
        }

        // камера плавно (экспоненциальное сглаживание, без телепорта) держит
        // ракету в центре; колесо/кнопки зума живут вместе с фиксацией
        internal void UpdateFollow()
        {
            if (!followMode || content == null)
                return;
            Vector2 target = ClampZoomOffset(-WorldToMap(host.PlayerWorldPosition()) * zoom, zoom);
            float k = 1f - Mathf.Exp(-FollowLerpSpeed * Time.unscaledDeltaTime);
            zoomAnim = false; // фиксация главнее остаточной анимации зума
            ApplyZoom(zoom, Vector2.Lerp(zoomOffset, target, k));
        }

        internal bool TryScreenToSheet(Vector2 screenPos, out Vector2 sheetLocal)
        {
            sheetLocal = Vector2.zero;
            return sheetRect != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(sheetRect, screenPos, canvasCam, out sheetLocal);
        }

        // пан: обработчики интерфейса хоста
        internal void BeginDrag(PointerEventData eventData, Func<PointerEventData, bool> pressOnChrome)
        {
            if (eventData.button != PointerEventData.InputButton.Left || pressOnChrome(eventData))
                return;
            dragging = true;
            zoomAnim = false; // рука перебивает анимацию зума
            // пан — ручное управление видом; фиксация на ракете выключается
            // до повторного клика кнопки
            followMode = false;
            RefreshFollowButton();
            TryScreenToSheet(eventData.position, out dragSheetPos);
        }

        internal void Drag(PointerEventData eventData)
        {
            if (!dragging || !TryScreenToSheet(eventData.position, out Vector2 pos))
                return;
            Vector2 delta = pos - dragSheetPos;
            dragMoved += delta.magnitude;
            if (dragMoved > ClickDragGuard)
                suppressClick = true; // перетащили — это пан, не клик
            dragSheetPos = pos;
            // резиновый вынос за край; на отпускании лист плавно вернётся в границы мира
            ApplyZoom(zoom, ClampZoomOffset(zoomOffset + delta, zoom, RubberMargin));
        }

        // тело OnPointerDown хоста
        internal void PressReset()
        {
            suppressClick = false;
            dragMoved = 0f;
        }

        internal void EndDrag(PointerEventData eventData)
        {
            if (!dragging)
                return;
            dragging = false;
            AnimateZoom(zoom, zoomOffset); // плавное клампирование в границы мира
        }

        internal void AnimateZoom(float targetScale, Vector2 targetOffset)
        {
            Vector2 clamped = ClampZoomOffset(targetOffset, targetScale);
            if (Mathf.Abs(targetScale - zoom) < 0.001f && (clamped - zoomOffset).sqrMagnitude < 1f)
            {
                zoomAnim = false;
                ApplyZoom(targetScale, clamped);
                return;
            }
            zoomFrom = zoom;
            offFrom = zoomOffset;
            zoomTo = targetScale;
            offTo = clamped;
            zoomT = 0f;
            zoomAnim = true;
        }

        internal void UpdateZoomAnim()
        {
            if (!zoomAnim)
                return;
            zoomT = Mathf.Clamp01(zoomT + Time.unscaledDeltaTime / ZoomDuration);
            float k = Mathf.SmoothStep(0f, 1f, zoomT);
            ApplyZoom(Mathf.Lerp(zoomFrom, zoomTo, k), Vector2.Lerp(offFrom, offTo, k));
            if (zoomT >= 1f)
                zoomAnim = false;
        }

        private void ApplyZoom(float s, Vector2 offset)
        {
            zoom = s;
            zoomOffset = offset;
            if (content == null)
                return;
            content.localScale = Vector3.one * s;
            content.anchoredPosition = offset; // бумага — ребёнок контента: двигается одним слоем
        }

        // зум/пан не выносят содержимое за экран; margin > 1 — резиновый вынос при перетаскивании
        private Vector2 ClampZoomOffset(Vector2 offset, float s, float margin = 1f)
        {
            if (content == null)
                return offset;
            if (s <= 1f)
                return Vector2.zero; // контент меньше экрана — всегда по центру
            Vector2 max = content.rect.size * (s - 1f) * 0.5f * margin;
            return Vector2.Max(-max, Vector2.Min(max, offset));
        }

        // кламп точки вида внутрь окна листа — как у маркера ракеты: точка за
        // ViewInset рисуется у края, «кламп не прячет событие»
        internal Vector2 SheetClampView(Vector2 sheetPos)
        {
            Vector2 half = sheetRect.rect.size * 0.5f - new Vector2(ContentMargin, ContentMargin);
            return new Vector2(
                Mathf.Clamp(sheetPos.x, -half.x + ViewInset, half.x - ViewInset),
                Mathf.Clamp(sheetPos.y, -half.y + ViewInset, half.y - ViewInset));
        }

        // хром-тест для колеса: делегат хоста/хрома (PointerOverChrome)
        private bool PointerOverChrome(Vector2 pos)
            => pointerOverChrome != null && pointerOverChrome(pos);

        // карта закрылась — вид сбрасываем, следующий вход с чистого экрана
        internal void OnMapDisabled()
        {
            dragging = false;
            suppressClick = false;
            dragMoved = 0f;
            if (content != null)
                ApplyZoom(1f, Vector2.zero);
            zoomAnim = false;
        }

        // пересборка карты: вид сбрасывается целиком
        internal void Reset()
        {
            dragging = false;
            suppressClick = false;
            dragMoved = 0f;
            zoomAnim = false;
            zoom = 1f;
            zoomOffset = Vector2.zero;
            followButton = null;
            followLabel = null;
            followMode = false;
        }
    }
}
