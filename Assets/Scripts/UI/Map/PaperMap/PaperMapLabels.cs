using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.World;

namespace SpaceTaxi.UI.PaperMap
{
    // — подсистема «подписи» бумажной карты (Н11): раскладка подписей «иконка+имя»
    // по 8 близким слотам, чернильные выноски, decluttering по тирaм зума,
    // альфа-фейды и призраки, реестр честных размеров иконок тел —
    // Выделена из MapScreenBinder (Н11): тела перенесены дословно. Читает зум
    // у PaperMapProjection (константный экранный кегль = LabelScreenFontSize /
    // zoom), легенду/заголовок/лист — у PaperMapChrome, край мира — через
    // BindEdgeRoot; цель активного заказа — через хост.
    internal sealed class PaperMapLabels
    {
        private readonly MapScreenBinder host;
        private readonly PaperMapProjection view;
        private readonly PaperMapChrome chrome;

        internal PaperMapLabels(MapScreenBinder host, PaperMapProjection view, PaperMapChrome chrome)
        {
            this.host = host;
            this.view = view;
            this.chrome = chrome;
        }

        // — шимы к хосту/виду/хрому (Н11): тела методов остаются дословными —
        private MapStyleConfig Style => host.Style;
        private LevelAsset level => host.Level;
        private float zoom => view.Zoom;
        private float scale => view.Scale;
        private float MaxViewZoom => Style.maxViewZoom; // Н11: та же ручка стиля, что у PaperMapProjection
        private int ActiveOrderTargetIndex() => host.ActiveOrderTargetIndex();
        private RectTransform sheetRect => view.SheetRect;
        private Vector2 WorldToMap(Vector2 world) => view.WorldToMap(world);
        private RectTransform legendRoot => chrome.LegendRoot;
        private RectTransform titleRoot => chrome.TitleRoot;

        // хелперы PaperMapUi (Н11)
        private static RectTransform MakeRect(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
            => PaperMapUi.MakeRect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
        private static RectTransform MakeSegment(RectTransform parent, Vector2 a, Vector2 b, Color color, float thickness)
            => PaperMapUi.MakeSegment(parent, a, b, color, thickness);
        private static Color WithAlpha(Color color, float alpha)
            => PaperMapUi.WithAlpha(color, alpha);
        private static Rect CenteredRect(Vector2 center, Vector2 size)
            => PaperMapUi.CenteredRect(center, size);
        private static void ClearChildren(Transform t)
            => PaperMapUi.ClearChildren(t);
        private TMP_Text MakeLabel(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
            => PaperMapUi.MakeLabel(host.Font, name, parent, text, size, color, align);

        private RectTransform labelsRoot;  // подписи тел (в плоскости бумаги)
        private RectTransform edgeRoot; // край мира (буи/«потеря связи») — слоты его читают (BindEdgeRoot)

        private readonly Dictionary<CelestialBodyData, float> bodySizes = new Dictionary<CelestialBodyData, float>();

        private int targetIndex = -1;

        // подписи тел (заполняются в PlaceAllLabels; читаются ореолом цели)
        private readonly Dictionary<CelestialBodyData, TMP_Text> bodyLabels = new Dictionary<CelestialBodyData, TMP_Text>();
        // выноски подписей — toggl вместе со своей подписью (decluttering, ореол цели)
        private readonly Dictionary<CelestialBodyData, RectTransform> labelLeaders = new Dictionary<CelestialBodyData, RectTransform>();
        private int labelTierState = -1; // максимальный видимый тир подписей при текущем зуме
        private float labelPlacedZoom = 1f; // зум, под который разложены подписи (M7.5)

        // M7.5: альфа-фейд подписей на смене тира — новые подписи проявляются,
        // исчезающие доигрывают призраками (0.12с), вместо мгновенного popping.
        // TMP_Text.alpha — это альфа цвета шрифта (множителя нет): фейд пишет
        // в цвет BaseColor.a * K
        private sealed class LabelFade
        {
            public TMP_Text Label;    // живая подпись или призрак
            public Color BaseColor;   // цвет при K=1 (у призрака — захваченный текущий)
            public Image Leader;      // выноска (может быть null)
            public Color LeaderColor; // цвет выноски при K=1
            public bool Ghost;        // призрак — уничтожить по завершении
            public float K;           // прогресс 0..1
            public float TargetK;
        }
        private readonly List<LabelFade> labelFades = new List<LabelFade>();

        // снимок подписи, исчезающей на новом тире, — после сноса рисуется призраком
        private sealed class GhostLabel
        {
            public string Text;
            public float FontSize;
            public Color Color; // захваченный цвет (альфа уже частично погашена фейдом)
            public TextAlignmentOptions Align;
            public Vector2 Position;
            public Vector2 Size;
            public bool HasLeader;
            public Vector2 LeaderPosition;
            public Vector2 LeaderSize;
            public Quaternion LeaderRotation;
            public Color LeaderColor;
        }

        // ————— подписи «блок иконка+имя» (Н6.HF4): 8 близких слотов вокруг иконки —————
        // Подпись живёт ПРИ иконке на фиксированном близком расстоянии — «далеко от
        // своей планеты» невозможно по построению: иконка всегда на своём мировом
        // месте, двигается только подпись. Порядок размещения: хаб, затем тела по
        // убыванию радиуса, пояса последними; уже размещённые подписи занимают
        // слоты и блокируют их для следующих. Кандидат свободен, если его rect
        // (preferred-размер текста) не задевает чужие подписи, чужие иконки и
        // статичный хром (районы, край мира, легенда, заголовок). Если заняты все
        // 8 близких слотов — один шаг дальше в направлении с минимальной коллизией
        // и тонкая чернильная выноска от края иконки до подписи (не более одной на
        // тело); если и там коллизия — наименее коллизирующий слот (best effort).

        private const float LabelFadeSeconds = PaperMapUi.LabelFadeSeconds; // альфа-фейд подписей на смене тира (M7.5) — общий темп в PaperMapUi (Н11)

        private const float LabelSlotGap = 3f;      // зазор края подписи от края иконки, px контента
        private const float LeaderThickness = 1.6f; // толщина чернильной выноски
        // — decluttering по зуму (Н6.HF4.1) — с ХОТФИКСА владельца («не у всех планет
        // есть надписи») тиры видимости для тел ВЫКЛЮЧЕНЫ (LabelTierOf всегда 0):
        // подпись у каждого тела на любом зуме. Пороги contentScale остались
        // триггерами ПЕРЕРАЗМЕЩЕНИЯ подписей при пересечении (дрейф разложенных) —
        // contentScale = zoom / MaxViewZoom (1.0 — максимальное приближение,
        // полный отъезд ≈ 0.16); скрытые подписи не блокируют слоты видимым
        private const float LabelTier1ZoomFraction = 0.20f; // порог переразмещения
        private const float LabelTier2ZoomFraction = 0.32f; // порог переразмещения
        // M7.5: подписи тел — константного ЭКРАННОГО размера (скайрим-урок, как
        // маркеры M7.1): контентный кегль = LabelScreenFontSize / zoom, иначе
        // на приближении имена разрастаются (16px × zoom9 = 144px — «куча мала»).
        // Солвер не тронут: работает с тем же кеглем через GetPreferredValues;
        // при дрейфе зума подписи переразмещаются (SyncLabelVisibility)
        // ХОТФИКС владельца (день после Н8): 15px мелковато — искать планету по
        // названию тяжело; 18px на любом зуме (константный экранный кегль M7.5)
        private float LabelScreenFontSize => Style.labelFontSize;
        private const float LabelRezoomGrow = 1.20f;  // зум вырос на 20% — переразместить (кегль вырос)
        private const float LabelRezoomShrink = 0.55f; // зум упал сильнее — переразместить (кегль отстал)
        private static void CollectRects(RectTransform root, List<Rect> into)
        {
            if (root == null)
                return;
            foreach (Transform child in root)
                if (child is RectTransform rect)
                    into.Add(CenteredRect(rect.anchoredPosition, rect.sizeDelta));
        }

        // размещает подписи всех тел и поясов — вызывается один раз в BuildMap
        // ПОСЛЕ построения всех иконок: их rects известны сразу
        internal void PlaceAllLabels()
        {
            // переразмещение при смене тира зума: старые подписи и выноски сносятся;
            // исчезающие на новом тире доигрывают призраками (M7.5)
            List<GhostLabel> ghosts = CaptureGhostLabels();
            ClearChildren(labelsRoot);
            bodyLabels.Clear();
            labelLeaders.Clear();

            var chrome = CollectStaticChrome();
            var iconRects = new Dictionary<CelestialBodyData, Rect>(level.Bodies.Count);
            foreach (CelestialBodyData body in level.Bodies)
            {
                float half = PlacementIconHalf(body);
                iconRects[body] = CenteredRect(WorldToMap(body.Position), Vector2.one * (half * 2f));
            }

            var placed = new List<Rect>(); // блокеры — только подписи, видимые на текущем тире
            // Н10.5: подпись планеты-цели отодвигается ЗА внешний радиус жёлтого
            // кольца (диаметр 1.95× иконки) — имя не тонет под ореолом
            int ringTargetIdx = ActiveOrderTargetIndex();
            CelestialBodyData ringTarget = ringTargetIdx >= 0 && ringTargetIdx < level.Bodies.Count
                ? level.Bodies[ringTargetIdx]
                : null;
            foreach (CelestialBodyData body in LabelPlacementOrder())
            {
                string text = LabelText(body);
                if (string.IsNullOrEmpty(text))
                    continue; // безымянное тело не подписываем
                bool visible = LabelVisible(body);
                TMP_Text label = MakeLabel("Label_" + body.DisplayName, labelsRoot, text,
                    LabelScreenFontSize / Mathf.Max(zoom, 0.001f), // M7.5: константный экранный размер
                    body.Type == BodyType.AsteroidBelt ? WithAlpha(PaperMapArt.Ink, 0.85f) : WithAlpha(PaperMapArt.Ink, 0.95f));
                label.raycastTarget = false; // подпись не ловит клики — чистая графика
                Vector2 labelSize = label.GetPreferredValues();
                RectTransform labelRect = (RectTransform)label.transform;
                labelRect.sizeDelta = labelSize;
                bodyLabels[body] = label;

                Vector2 center = WorldToMap(body.Position);
                float iconHalf = PlacementIconHalf(body);
                Vector2 labelHalf = labelSize * 0.5f;

                int slot = -1;
                // Н10.5: у планеты-цели зазор слота отодвигает подпись за кольцо
                float slotGap = body == ringTarget ? RingClearGap(body, iconHalf) : LabelSlotGap;
                // ХОТФИКС владельца («чупа-чупсы — палочки странные»): близкий слот
                // разрешает перекрыть ЧУЖУЮ иконку (мелькающая под чернилами мелочь) —
                // жёсткие блокеры только чужие подписи и хром; ярлык сидит при своей
                // планете, палочка-выноска не нужна. Иконки учитывает лишь дальний
                // проход (все близкие заняты подписями/хромом)
                for (int i = 0; i < 8 && slot < 0; i++) // низ → верх → право → лево → диагонали
                {
                    Rect candidate = CenteredRect(center + SlotOffset(i, iconHalf, slotGap, labelHalf), labelSize);
                    if (PlacementCollisions(candidate, body, null, placed, chrome) == 0)
                        slot = i;
                }

                RectTransform leader = null;
                if (slot >= 0)
                {
                    labelRect.anchoredPosition = center + SlotOffset(slot, iconHalf, slotGap, labelHalf);
                }
                else // все близкие слоты заняты — шаг дальше + линия-выноска
                {
                    float farGap = body == ringTarget ? slotGap + labelHalf.y + 6f : labelHalf.y + 6f;
                    int best = 0, bestCollisions = int.MaxValue;
                    Rect bestRect = Rect.zero;
                    for (int i = 0; i < 8; i++)
                    {
                        Rect candidate = CenteredRect(center + SlotOffset(i, iconHalf, farGap, labelHalf), labelSize);
                        int collisions = PlacementCollisions(candidate, body, iconRects, placed, chrome);
                        if (collisions < bestCollisions)
                        {
                            best = i;
                            bestCollisions = collisions;
                            bestRect = candidate;
                        }
                    }
                    labelRect.anchoredPosition = bestRect.center;
                    leader = MakeLeaderLine(best, center, iconHalf, bestRect); // не более одной на тело
                    labelLeaders[body] = leader;
                }

                // decluttering (M7.5): вместо мгновенного enabled — альфа-фейд;
                // скрытая подпись позицию держит (при появлении не «переезжает»
                // с нуля), но слоты видимым не занимает.
                // ВАЖНО: TMP_Text.alpha пишет прямо в color.a — полный цвет
                // захватываем ДО гашения, иначе фейд гонит ноль к нулю
                Color fullColor = label.color;
                label.alpha = 0f; // старт фейда: видимая проявится к 1, скрытая остаётся прозрачной
                if (leader != null)
                    leader.gameObject.SetActive(visible);
                if (visible)
                {
                    placed.Add(CenteredRect(labelRect.anchoredPosition, labelSize));
                    labelFades.Add(new LabelFade
                    {
                        Label = label,
                        BaseColor = fullColor,
                        Leader = leader != null ? leader.GetComponent<Image>() : null,
                        LeaderColor = WithAlpha(PaperMapArt.Ink, 0.5f),
                        K = 0f,
                        TargetK = 1f
                    });
                }
            }

            SpawnGhostLabels(ghosts);
            labelTierState = LabelTierState(); // состояние синхронно с выполненным размещением
            labelPlacedZoom = zoom;            // кегль разложен под этот зум (M7.5)
        }

        // снимок подписей, исчезающих на новом тире (старое состояние живо в
        // bodyLabels), — после сноса листа они доигрывают призраками
        private List<GhostLabel> CaptureGhostLabels()
        {
            List<GhostLabel> ghosts = null;
            if (bodyLabels.Count == 0)
                return ghosts;
            foreach (var pair in bodyLabels)
            {
                TMP_Text label = pair.Value;
                if (label == null || LabelVisible(pair.Key) || label.alpha <= 0.02f)
                    continue;
                RectTransform rect = (RectTransform)label.transform;
                var ghost = new GhostLabel
                {
                    Text = label.text,
                    FontSize = label.fontSize,
                    Color = label.color,
                    Align = label.alignment,
                    Position = rect.anchoredPosition,
                    Size = rect.sizeDelta
                };
                if (labelLeaders.TryGetValue(pair.Key, out RectTransform leader) && leader != null
                    && leader.gameObject.activeSelf)
                {
                    Image image = leader.GetComponent<Image>();
                    ghost.HasLeader = true;
                    ghost.LeaderPosition = leader.anchoredPosition;
                    ghost.LeaderSize = leader.sizeDelta;
                    ghost.LeaderRotation = leader.localRotation;
                    ghost.LeaderColor = image != null ? image.color : WithAlpha(PaperMapArt.Ink, 0.5f);
                }
                (ghosts ??= new List<GhostLabel>()).Add(ghost);
            }
            return ghosts;
        }

        // призраки: копии исчезнувших подписей (и выносок) — гаснут за 0.12с и умирают
        private void SpawnGhostLabels(List<GhostLabel> ghosts)
        {
            if (ghosts == null)
                return;
            foreach (GhostLabel ghost in ghosts)
            {
                TMP_Text label = MakeLabel("LabelGhost", labelsRoot, ghost.Text, ghost.FontSize, ghost.Color, ghost.Align);
                RectTransform rect = (RectTransform)label.transform;
                rect.sizeDelta = ghost.Size;
                rect.anchoredPosition = ghost.Position;

                Image leaderImage = null;
                if (ghost.HasLeader)
                {
                    var go = new GameObject("LeaderGhost", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(labelsRoot, false);
                    RectTransform leaderRect = (RectTransform)go.transform;
                    leaderRect.anchorMin = leaderRect.anchorMax = new Vector2(0.5f, 0.5f);
                    leaderRect.anchoredPosition = ghost.LeaderPosition;
                    leaderRect.sizeDelta = ghost.LeaderSize;
                    leaderRect.localRotation = ghost.LeaderRotation;
                    leaderImage = go.GetComponent<Image>();
                    leaderImage.sprite = StubSprites.Line();
                    leaderImage.color = ghost.LeaderColor;
                    leaderImage.raycastTarget = false;
                }

                labelFades.Add(new LabelFade
                {
                    Label = label,
                    BaseColor = ghost.Color,
                    Leader = leaderImage,
                    LeaderColor = ghost.LeaderColor,
                    K = 1f,
                    TargetK = 0f,
                    Ghost = true
                });
            }
        }

        // шаг фейда: живые подписи проявляются к 1, призраки гаснут и умирают
        internal void StepLabelFades()
        {
            if (labelFades.Count == 0)
                return;
            float step = Time.unscaledDeltaTime / LabelFadeSeconds;
            for (int i = labelFades.Count - 1; i >= 0; i--)
            {
                LabelFade fade = labelFades[i];
                if (fade.Label == null) // пережил пересборку карты — просто забыть
                {
                    labelFades.RemoveAt(i);
                    continue;
                }
                fade.K = Mathf.MoveTowards(fade.K, fade.TargetK, step);
                var color = fade.BaseColor;
                color.a *= fade.K;
                fade.Label.color = color;
                if (fade.Leader != null)
                {
                    var leaderColor = fade.LeaderColor;
                    leaderColor.a *= fade.K;
                    fade.Leader.color = leaderColor;
                }
                if (fade.K == fade.TargetK)
                {
                    if (fade.Ghost)
                        Object.Destroy(fade.Label.gameObject); // static-класс: не MonoBehaviour (Н11)
                    labelFades.RemoveAt(i);
                }
            }
        }

        // статичный хром блок-листа: край мира (буи, «потеря связи», кометная
        // зона) — в координатах контента; легенда и заголовок лежат на листе —
        // их rect переводится в координаты контента.
        // M7.5: районы в хром НЕ собираются — их 560×56-rects в контент-px
        // накрывали почти весь лист (вид на zoom9 ≈ 100×61 px) и гнали все
        // подписи в дальние слоты с выносками-«палками». Район — фоновый
        // водяной знак (alpha ~0.35): мелкая подпись поверх него читается
        private List<Rect> CollectStaticChrome()
        {
            var chrome = new List<Rect>();
            CollectRects(edgeRoot, chrome);
            AddSheetChrome(legendRoot, chrome);
            AddSheetChrome(titleRoot, chrome);
            return chrome;
        }

        // rect экранных детей листа в координатах контента: при сборке зум=1 и
        // смещение=0, а центры листа и контента совпадают — локальные координаты
        // листа и контента тождественны
        private void AddSheetChrome(RectTransform child, List<Rect> into)
        {
            if (child == null || sheetRect == null)
                return;
            Rect parentRect = sheetRect.rect;
            Vector2 pivot = child.pivot;
            Vector2 anchorRef = new Vector2(
                parentRect.xMin + Mathf.Lerp(child.anchorMin.x, child.anchorMax.x, pivot.x) * parentRect.width,
                parentRect.yMin + Mathf.Lerp(child.anchorMin.y, child.anchorMax.y, pivot.y) * parentRect.height);
            Vector2 center = anchorRef + child.anchoredPosition
                + Vector2.Scale(Vector2.one * 0.5f - pivot, child.rect.size);
            into.Add(CenteredRect(center, child.rect.size));
        }

        // половина «иконки» для слотов и коллизий: у пояса — кольцо камней
        // (честная ширина, как в игре ±BeltWidth/2), у остальных — иконка
        private float PlacementIconHalf(CelestialBodyData body)
        {
            return body.Type == BodyType.AsteroidBelt
                ? (body.Radius + body.BeltWidth * 0.5f) * scale
                : IconSizeOf(body) * 0.5f;
        }

        // ХОТФИКС владельца («не у всех планет есть надписи»): decluttering тиров
        // для тел выключен — подпись теперь у КАЖДОГО тела на любом зуме (кегль
        // константный экранный, солвер разводит с выносками; на полном отъезде
        // прежде планеты молчали до приближения). Механика переразмещения при
        // зуме (дрейф разложенных подписей) осталась
        private static int LabelTierOf(CelestialBodyData body)
        {
            return 0;
        }

        // нормированный масштаб контента на экране → максимальный видимый тир:
        // полный отъезд показывает только тир 0, приближение открывает 1-й и 2-й
        private int LabelTierState()
        {
            float contentScale = zoom / MaxViewZoom;
            if (contentScale >= LabelTier2ZoomFraction)
                return 2;
            return contentScale >= LabelTier1ZoomFraction ? 1 : 0;
        }

        // видима ли подпись при текущем тире зума
        private bool LabelVisible(CelestialBodyData body)
        {
            return LabelTierOf(body) <= labelTierState;
        }

        // decluttering: при пересечении порога тира подписи переразмещаются —
        // дешёвый полл ловит любую смену зума (колесо, кнопки, анимация,
        // следование); M7.5: переразмещение и при дрейфе зума от разложенного —
        // кегль подписей константного экранного размера (LabelScreenFontSize/zoom)
        internal void SyncLabelVisibility()
        {
            if (labelsRoot == null)
                return;
            int state = LabelTierState();
            bool rezoom = zoom > labelPlacedZoom * LabelRezoomGrow || zoom < labelPlacedZoom * LabelRezoomShrink;
            if (state == labelTierState && !rezoom)
                return;
            labelTierState = state;
            PlaceAllLabels();
            // ореол цели перечитает свежие подписи в своём тике (targetIndex сброшен)
            targetIndex = -1;
        }
        // порядок размещения: хаб первым, затем тела по убыванию радиуса,
        // пояса последними (внутри группы — порядок уровня)
        private IEnumerable<CelestialBodyData> LabelPlacementOrder()
        {
            CelestialBodyData hub = null;
            var planets = new List<CelestialBodyData>();
            var belts = new List<CelestialBodyData>();
            foreach (CelestialBodyData body in level.Bodies)
            {
                if (body.Type == BodyType.AsteroidBelt)
                    belts.Add(body);
                else if (body.IsHub && hub == null)
                    hub = body;
                else
                    planets.Add(body);
            }
            planets.Sort((a, b) => b.Radius.CompareTo(a.Radius));
            if (hub != null)
                yield return hub;
            foreach (CelestialBodyData body in planets)
                yield return body;
            foreach (CelestialBodyData body in belts)
                yield return body;
        }

        // текст подписи: у пояса — «пояс Имя»; безымянные не подписываются
        private static string LabelText(CelestialBodyData body)
        {
            if (string.IsNullOrEmpty(body.DisplayName))
                return "";
            if (body.Type != BodyType.AsteroidBelt)
                return body.DisplayName;
            string name = body.DisplayName;
            return name.ToLower().Contains("пояс") ? name : "пояс " + name;
        }

        // центр подписи относительно центра иконки: слоты 0 низ (дефолт), 1 верх,
        // 2 право, 3 лево (по центру иконки), 4..7 — диагонали низ-право,
        // низ-лево, верх-право, верх-лево
        private static Vector2 SlotOffset(int slot, float iconHalf, float gap, Vector2 labelHalf)
        {
            float sx = 0f, sy = -1f;
            switch (slot)
            {
                case 1: sy = 1f; break;
                case 2: sx = 1f; sy = 0f; break;
                case 3: sx = -1f; sy = 0f; break;
                case 4: sx = 1f; break;
                case 5: sx = -1f; break;
                case 6: sx = 1f; sy = 1f; break;
                case 7: sx = -1f; sy = 1f; break;
            }
            return new Vector2(sx * (iconHalf + gap + labelHalf.x), sy * (iconHalf + gap + labelHalf.y));
        }

        // Н10.5: зазор слота, отодвигающий подпись планеты-цели за внешний
        // радиус широкого кольца (диаметр 1.95× иконки) — имя планеты не
        // прячется под ореолом (подавление подписи снято). Контент-единицы:
        // и кольцо (localScale 1/zoom), и подписи (шрифт /zoom) в контенте
        // имеют радиус IconSizeOf×0.975 независимо от зума
        private float RingClearGap(CelestialBodyData body, float iconHalf)
        {
            return Mathf.Max(LabelSlotGap, IconSizeOf(body) * 0.975f + 2f - iconHalf);
        }

        // число препятствий под кандидатом: чужие подписи, чужие иконки, хром.
        // Свою иконку кандидат не накрывает по построению слота — не считаем
        private static int PlacementCollisions(Rect candidate, CelestialBodyData self,
            Dictionary<CelestialBodyData, Rect> iconRects, List<Rect> placedLabels, List<Rect> chrome)
        {
            int count = 0;
            foreach (Rect rect in placedLabels)
                if (candidate.Overlaps(rect))
                    count++;
            // iconRects == null — близкий проход: чужие иконки не блокируют
            // (ХОТФИКС «чупа-чупсы»); дальний проход считает их как прежде
            if (iconRects != null)
                foreach (var pair in iconRects)
                    if (pair.Key != self && candidate.Overlaps(pair.Value))
                        count++;
            foreach (Rect rect in chrome)
                if (candidate.Overlaps(rect))
                    count++;
            return count;
        }

        // тонкая чернильная линия-выноска от края иконки до края подписи —
        // функциональный элемент: связывает отошедшую подпись с телом.
        // Rect возвращается вызывающему: toggl вместе со своей подписью
        private RectTransform MakeLeaderLine(int slot, Vector2 center, float iconHalf, Rect labelRect)
        {
            const float diag = 0.70710678f;
            Vector2 a, b;
            switch (slot)
            {
                case 1: a = center + Vector2.up * iconHalf; b = new Vector2(center.x, labelRect.yMin); break;
                case 2: a = center + Vector2.right * iconHalf; b = new Vector2(labelRect.xMin, center.y); break;
                case 3: a = center + Vector2.left * iconHalf; b = new Vector2(labelRect.xMax, center.y); break;
                case 4: a = center + new Vector2(diag, -diag) * iconHalf; b = new Vector2(labelRect.xMin, labelRect.yMax); break;
                case 5: a = center + new Vector2(-diag, -diag) * iconHalf; b = new Vector2(labelRect.xMax, labelRect.yMax); break;
                case 6: a = center + new Vector2(diag, diag) * iconHalf; b = new Vector2(labelRect.xMin, labelRect.yMin); break;
                case 7: a = center + new Vector2(-diag, diag) * iconHalf; b = new Vector2(labelRect.xMax, labelRect.yMin); break;
                default: a = center + Vector2.down * iconHalf; b = new Vector2(center.x, labelRect.yMax); break;
            }
            return MakeSegment(labelsRoot, a, b, WithAlpha(PaperMapArt.Ink, 0.5f), LeaderThickness);
        }

        // (из BuildMap, Н11): слой подписей — ниже иконок тел, выше поясов
        internal void CreateLabelsRoot(RectTransform content)
        {
            labelsRoot = MakeRect("Labels", content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        // (Н11): слой края мира (буи/«потеря связи») — блокирует слоты подписей;
        // ссылку отдаёт хост после BuildWorldEdge
        internal void BindEdgeRoot(RectTransform edgeRoot)
        {
            this.edgeRoot = edgeRoot;
        }

        // (Н11): хост-строитель иконок пишет честный диаметр по иконке/поясу
        internal void SetIconSize(CelestialBodyData body, float diameter)
        {
            bodySizes[body] = diameter;
        }

        // пересборка карты: подписи/размеры иконок гаснут (часть RebuildAll хоста, Н11)
        internal void Reset()
        {
            bodySizes.Clear();
            bodyLabels.Clear();
            labelLeaders.Clear();
            labelTierState = -1;
            labelFades.Clear(); // M7.5: фейд-состояния пересборки не переживают
            labelsRoot = null;
            targetIndex = -1;
        }

        // наружу подсистемам/хосту (Н11): кольца цели/метки читают честный диаметр,
        // тир цели сбрасывается при переразмещении
        internal int TargetIndex
        {
            get => targetIndex;
            set => targetIndex = value;
        }

        // честный диаметр иконки на карте (px контента) — читают слоты подписей
        internal float IconSizeOf(CelestialBodyData body)
        {
            return bodySizes.TryGetValue(body, out float size) ? size : 22f;
        }

        // (Н11-step4-labels-applied)
    }
}
