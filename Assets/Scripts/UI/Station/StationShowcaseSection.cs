using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Audio;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;
using UiKit;

namespace SpaceTaxi.UI
{
    // Витрина магазина: одна модель на экране, листание стрелками, выбор
    // покраски свотчами; Refresh дергает контроллер, ротацию слота отслеживает
    // UpdateRotation из его Update
    public sealed class StationShowcaseSection
    {
        // слева статы с риской текущего значения, справа большая модель
        private const float ShowcaseSpriteW = 310f;
        private const float ShowcaseSpriteH = 260f;
        private const int StatCount = 6; // Thrust, MaxV, FuelMax, HpMax, Retro, StrafeAcc
        // ширины колонок строки стата: подпись — под «Мощность торможения»
        // (самая длинная строка, 17pt), справа округлённое значение модели
        private const float StatLabelWidth = 200f;
        private const float StatValueWidth = 48f;
        // зазор между подписью/значением и полоской стата (воздух 8-10)
        private const float StatBarGap = 8f;
        private const float StatRowHeight = 24f; // строка 17pt ≈ 22.4 — правило 1.32
        // свотчи: 38px, при числе больше 9 — 32px
        private const float SwatchSize = 38f;
        private const float SwatchSizeSmall = 32f;

        // все статы «больше = лучше»; индексы совпадают со StatValue/CurrentStatValue
        private static readonly string[] StatLabels =
        {
            "Тяга", "Макс. скорость", "Бак", "Прочность", "Мощность торможения", "Манёвренность",
        };

        private readonly IStationSectionHost host;

        private GameObject shopWindow;    // витрина магазина — только вкладка МАГАЗИН

        // каталог из Resources сортируется по цене один раз; показанный вариант
        // покраски ротирует ShopStockService раз в 2 игровых часа
        private ShipConfig[] shipCatalog;
        private int showcaseIndex;
        private int showcasePaintIndex; // покраска на витрине сейчас: 0 = заводская, 1..N — PaintVariants
        private int stockSlot;           // слот ротации на последней отрисовке
        private ShipConfig showcaseShip; // модель на витрине сейчас
        private Image showcaseSprite;
        private TMP_Text showcaseName;
        private TMP_Text showcasePaint;
        // таблица 6 статов: полоска модели + риска текущего значения + отрезок разницы
        private ShowStatRowRefs[] showcaseStatRows;
        private TMP_Text showcaseState;    // «ВАШ · СЕЙЧАС АКТИВЕН» / «В АРЕНДЕ …»
        private GameObject paintSwatchRow; // свотчи расцветок: выбор меняет большой спрайт
        private Image[] paintSwatchIcons;  // иконки свотчей по индексу покраски
        private ShipConfig swatchConfig;   // модель, под которую собраны свотчи
        private Button shopBuy;
        private TMP_Text shopBuyLabel;
        private TMP_Text shopStatus; // «Не хватает Đ N» при недостаче, иначе пусто

        public StationShowcaseSection(IStationSectionHost host)
        {
            this.host = host;
        }

        // строит витрину в переданном родителе
        public void Build(Transform parent)
        {
            BuildShowcaseBlock(parent);
        }

        // перечитывание витрины по RefreshShop контроллера (деньги/парк)
        public void Refresh()
        {
            RefreshShowcase();
        }

        // видимость окна витрины — вкладкой МАГАЗИН (SelectTab контроллера)
        public void SetWindowVisible(bool visible)
        {
            if (shopWindow != null)
                shopWindow.SetActive(visible);
        }

        // слот ротации сменился — витрина перерисовывается сама
        public void UpdateRotation()
        {
            if (shopWindow == null || !shopWindow.activeSelf)
                return;
            int slot = ShopStockService.CurrentSlot(Time.time);
            if (slot != stockSlot)
                RefreshShowcase(); // внутри обновит stockSlot
        }

        // строка стата: подпись | полоска с риской | значение.
        // Риска — твоё текущее значение, отрезок — разница (зелёный/красный)
        private sealed class ShowStatRowRefs
        {
            public TMP_Text Label;
            public TMP_Text Value;
            public RectTransform ValueFill; // доля рассматриваемого корабля (0..1)
            public RectTransform DeltaFill; // отрезок между твоим и рассматриваемым
            public Image DeltaImage;
            public RectTransform Tick;      // риска текущего значения
        }

        // каталог из Resources один раз; порядок — по цене (при равенстве
        // по тиру и имени — чтобы список не плясал)
        private ShipConfig[] Catalog()
        {
            if (shipCatalog != null)
                return shipCatalog;
            var configs = Resources.LoadAll<ShipConfig>("Ships");
            if (configs == null || configs.Length == 0)
                Debug.LogWarning("[Station] Каталог кораблей пуст — Assets/ScriptableObjects/Resources/Ships");
            if (configs == null)
                return Array.Empty<ShipConfig>(); // guard: Sort ниже разыменовал бы null
            Array.Sort(configs, (a, b) =>
            {
                int byPrice = a.Price.CompareTo(b.Price);
                if (byPrice != 0)
                    return byPrice;
                int byTier = a.Tier.CompareTo(b.Tier);
                return byTier != 0 ? byTier : string.CompareOrdinal(a.DisplayName, b.DisplayName);
            });
            shipCatalog = configs;
            return shipCatalog;
        }

        // каркас: слева статы + свотчи, справа модель, внизу навигация и кнопка
        private void BuildShowcaseBlock(Transform parent)
        {
            shopWindow = StationUI.Section(parent, "// МАГАЗИН", "ShopWindow");

            var showcaseRow = OnboardComputerFactory.H("ShowcaseRow", shopWindow.transform);

            // --- левая колонка: 6 статов + выбор расцветки ---
            var statsColumn = OnboardComputerFactory.V("StatsColumn", showcaseRow.transform, 6f);
            OnboardComputerFactory.Flexible(statsColumn);

            // легенда: янтарная риска — твой текущий стат
            var tickCaption = OnboardComputerFactory.Text(statsColumn, "ЯНТАРНАЯ РИСКА — ТВОЙ ТЕКУЩИЙ СТАТ",
                14f, OnboardComputerFactory.TaxiColor, "TickCaption");
            OnboardComputerFactory.FixedHeight(tickCaption.gameObject, 18f);

            showcaseStatRows = new ShowStatRowRefs[StatCount];
            for (int i = 0; i < StatCount; i++)
                showcaseStatRows[i] = BuildStatRow(statsColumn.transform, i, StatLabels[i]);

            // выбор расцветки: клик по свотчу меняет большой спрайт,
            // покупка берёт выбранный вариант
            var paintCaption = OnboardComputerFactory.Text(statsColumn, "РАСКРАВКА", 15f,
                OnboardComputerFactory.LabelColor, "PaintCaption");
            OnboardComputerFactory.FixedHeight(paintCaption.gameObject, 20f);
            paintSwatchRow = OnboardComputerFactory.H("Swatches", statsColumn.transform);
            paintSwatchRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            OnboardComputerFactory.FixedHeight(paintSwatchRow.gameObject, SwatchSize);

            showcasePaint = OnboardComputerFactory.Text(statsColumn, "", 15f,
                OnboardComputerFactory.LabelColor, "PaintLine");
            OnboardComputerFactory.FixedHeight(showcasePaint.gameObject, 20f);

            // --- правая колонка: большая модель ---
            var spriteColumn = OnboardComputerFactory.V("SpriteColumn", showcaseRow.transform, 6f);
            OnboardComputerFactory.FixedWidth(spriteColumn, ShowcaseSpriteW);

            var box = new GameObject("SpriteBox", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(spriteColumn.transform, false);
            showcaseSprite = box.GetComponent<Image>();
            showcaseSprite.color = Ui.WithAlpha(OnboardComputerFactory.LabelColor, 0.08f);
            showcaseSprite.preserveAspect = true;
            // рамка как у кнопок фабрики — карточка читается как один блок
            var frame = box.AddComponent<Outline>();
            frame.effectColor = new Color(0.55f, 0.85f, 0.7f, 0.35f);
            frame.effectDistance = new Vector2(2f, -2f);
            OnboardComputerFactory.FixedSize(box, ShowcaseSpriteW, ShowcaseSpriteH);

            // навигация: стрелки 40×40 и название с тиром; статус владения — подписью ниже
            var navRow = OnboardComputerFactory.H("NavBar", shopWindow.transform);
            OnboardComputerFactory.FixedHeight(navRow.gameObject, 44f);
            navRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            BuildNavArrow(navRow.transform, "←", () => FlipShowcase(-1));
            showcaseName = OnboardComputerFactory.Text(navRow, "", 21f,
                OnboardComputerFactory.BrightColor, "ModelName");
            OnboardComputerFactory.Flexible(showcaseName.gameObject);
            // 36: 21pt ≈ 27.7 — запас против вертикального Ellipsis
            OnboardComputerFactory.FixedHeight(showcaseName.gameObject, 36f);
            showcaseName.alignment = TextAlignmentOptions.Center;
            BuildNavArrow(navRow.transform, "→", () => FlipShowcase(1));

            showcaseState = OnboardComputerFactory.Text(shopWindow, "", 15f,
                OnboardComputerFactory.TaxiColor, "StateLine");
            OnboardComputerFactory.FixedHeight(showcaseState.gameObject, 20f);
            showcaseState.alignment = TextAlignmentOptions.Center;

            shopBuy = StationUI.ServiceButton(shopWindow.transform, "ShopBuyButton", ConfirmShopAction);
            shopBuyLabel = shopBuy.GetComponentInChildren<TMP_Text>();
            shopStatus = OnboardComputerFactory.Text(shopWindow, "", 17f, OnboardComputerFactory.TaxiColor, "Status");
            OnboardComputerFactory.FixedHeight(shopStatus.gameObject, 23f);

            Catalog(); // сортировка по цене — до первой отрисовки
            RefreshShowcase();
        }

        // строка стата: подпись | полоска | значение. Дети посажены явными
        // якорями, НЕ лейаутом: HorizontalLayoutGroup с childForceExpandWidth
        // сжимал полоску между текстами до ~60px
        private ShowStatRowRefs BuildStatRow(Transform parent, int index, string label)
        {
            var rowGo = new GameObject("Stat_" + index, typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            OnboardComputerFactory.FixedHeight(rowGo.gameObject, StatRowHeight);

            var refs = new ShowStatRowRefs();

            // подпись: слева, фиксированная ширина, высота строки
            refs.Label = OnboardComputerFactory.Text(rowGo, label, 17f, OnboardComputerFactory.LabelColor, "Label");
            var labelRect = (RectTransform)refs.Label.transform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = new Vector2(StatLabelWidth, 0f);
            refs.Label.alignment = TextAlignmentOptions.Left;

            // полоска: всё место между подписью и значением
            var bar = new GameObject("Bar", typeof(RectTransform));
            bar.transform.SetParent(rowGo.transform, false);
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = Vector2.zero;
            barRect.anchorMax = Vector2.one;
            barRect.offsetMin = new Vector2(StatLabelWidth + StatBarGap, 0f);
            barRect.offsetMax = new Vector2(-(StatValueWidth + StatBarGap), 0f);
            var dim = OnboardComputerFactory.LabelColor;
            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(bar.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)track.transform);
            var trackImage = track.GetComponent<Image>();
            trackImage.color = Ui.WithAlpha(dim, 0.12f);
            trackImage.raycastTarget = false;

            refs.ValueFill = StationUI.FillChild(track.transform, Ui.WithAlpha(dim, 0.45f), "ValueFill");
            refs.DeltaFill = StationUI.FillChild(track.transform, dim, "DeltaFill");
            refs.DeltaImage = refs.DeltaFill.GetComponent<Image>();
            refs.Tick = BuildTick(track.transform);

            // значение модели: справа, у правого края колонки
            refs.Value = OnboardComputerFactory.Text(rowGo, "", 16f, OnboardComputerFactory.LabelColor, "Value");
            var valueRect = (RectTransform)refs.Value.transform;
            valueRect.anchorMin = new Vector2(1f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.offsetMin = new Vector2(-StatValueWidth, 0f);
            valueRect.offsetMax = Vector2.zero;
            refs.Value.alignment = TextAlignmentOptions.Right;
            return refs;
        }

        // отрезок полоски между двумя долями 0..1 (подкраска разницы)
        private static void SetSegment(RectTransform fill, float from, float to)
        {
            float a = Mathf.Clamp01(Mathf.Min(from, to));
            float b = Mathf.Clamp01(Mathf.Max(from, to));
            fill.anchorMin = new Vector2(a, 0f);
            fill.anchorMax = new Vector2(b, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }

        // риска текущего значения: вертикальная полоска 6px с выносом за
        // дорожку — чтобы читалась как маркер
        private static RectTransform BuildTick(Transform track)
        {
            var tick = new GameObject("Tick", typeof(RectTransform), typeof(Image));
            tick.transform.SetParent(track, false);
            var image = tick.GetComponent<Image>();
            image.color = OnboardComputerFactory.TaxiColor;
            image.raycastTarget = false;
            var rect = (RectTransform)tick.transform;
            rect.offsetMin = new Vector2(0f, -2f);  // вынос вниз за дорожку
            rect.offsetMax = new Vector2(0f, 2f);   // вынос вверх
            return rect;
        }

        private static void SetTick(RectTransform tick, float fraction)
        {
            float x = Mathf.Clamp01(fraction);
            tick.anchorMin = new Vector2(x, 0f);
            tick.anchorMax = new Vector2(x, 1f);
            tick.sizeDelta = new Vector2(6f, 0f); // ширина 6px, высоту дают якоря
        }

        // компактная стрелка 40×40; звук уже хукнут каркасом
        private static void BuildNavArrow(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = StationUI.Button(parent, label, 18f, "NavArrow");
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 40f;
            le.preferredWidth = 40f;
            le.minHeight = 40f;
            le.preferredHeight = 40f;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            go.GetComponent<Button>().onClick.AddListener(onClick);
        }

        private void FlipShowcase(int delta)
        {
            int count = Catalog().Length;
            if (count == 0)
                return;
            showcaseIndex = (showcaseIndex + delta + count) % count;
            RefreshShowcase();
        }

        // свотчи пересобираются под модель (число вариантов разное);
        // у аренды нестандартные — серые и некликабельные
        private void RebuildPaintSwatches(ShipConfig config)
        {
            if (paintSwatchRow == null || config == null)
                return;
            for (int i = paintSwatchRow.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(paintSwatchRow.transform.GetChild(i).gameObject);
            int count = config.PaintVariantCount + 1;
            paintSwatchIcons = new Image[count];
            bool locked = config.IsRental;
            // крупный свотч 38px; при числе больше 9 — мелкий
            float size = count > 9 ? SwatchSizeSmall : SwatchSize;
            for (int i = 0; i < count; i++)
                paintSwatchIcons[i] = BuildPaintSwatch(config, i, size, locked);
            UpdatePaintSwatches();
        }

        // свотч: рамка только у выбранного; звук хукнут каркасом первым
        private Image BuildPaintSwatch(ShipConfig config, int paintIndex, float size, bool locked)
        {
            var go = new GameObject("Swatch_" + paintIndex, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(paintSwatchRow.transform, false);
            var icon = go.GetComponent<Image>();
            Sprite sprite = config.GetPaintSprite(paintIndex);
            if (sprite != null)
            {
                icon.sprite = sprite;
                icon.preserveAspect = true;
            }
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.85f, 0.7f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = size;
            le.preferredWidth = size;
            le.minHeight = size;
            le.preferredHeight = size;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.fadeDuration = 0.1f;
            colors.highlightedColor = new Color(0.82f, 1f, 0.9f);
            colors.pressedColor = new Color(0.6f, 1f, 0.78f);
            button.colors = colors;
            if (locked && paintIndex > 0)
                button.interactable = false; // аренда: только заводская
            UiSfx.Hook(button); // хук первым, слушатели вызывающего — после
            int captured = paintIndex;
            button.onClick.AddListener(() => SelectShowcasePaint(captured));
            return icon;
        }

        // выбор расцветки: большой спрайт перерисовывается, покупка возьмёт
        // выбранный индекс
        private void SelectShowcasePaint(int paintIndex)
        {
            ShipConfig config = showcaseShip;
            if (config == null || shopWindow == null || host.Session == null || host.Session.Fleet == null)
                return;
            if (paintIndex < 0 || paintIndex > config.PaintVariantCount)
                return;
            if (config.IsRental && paintIndex > 0)
                return; // аренда = только заводская покраска
            showcasePaintIndex = paintIndex;
            ApplyShowcaseSprite(host.Session.Fleet, config, paintIndex);
            UpdatePaintSwatches();
            showcasePaint.text = PaintLineText(config, paintIndex, config.IsRental);
        }

        // подсветка выбранного свотча: рамка + полный цвет; закрытые — глуше
        private void UpdatePaintSwatches()
        {
            ShipConfig config = swatchConfig;
            if (paintSwatchIcons == null || config == null)
                return;
            for (int i = 0; i < paintSwatchIcons.Length; i++)
            {
                bool selected = i == showcasePaintIndex;
                var image = paintSwatchIcons[i];
                Color baseColor = image.sprite != null ? Color.white : TierShade(config.Tier);
                float dim = config.IsRental && i > 0 ? 0.38f : 0.62f; // закрытый — глуше
                image.color = selected
                    ? baseColor
                    : new Color(baseColor.r * dim, baseColor.g * dim, baseColor.b * dim, baseColor.a);
                var outline = image.GetComponent<Outline>();
                if (outline != null)
                    outline.enabled = selected;
            }
        }

        // перечитывается на каждый RefreshShop (деньги/парк) и при смене слота
        // ротации; ручной выбор покраски сбрасывается только сменой модели/слота
        private void RefreshShowcase()
        {
            ShipConfig[] catalog = Catalog();
            if (shopWindow == null || catalog.Length == 0
                || host.Session == null || host.Session.Fleet == null)
                return;
            var fleet = host.Session.Fleet;
            ShipConfig config = catalog[showcaseIndex];
            bool modelChanged = showcaseShip != config;
            showcaseShip = config;
            int slot = ShopStockService.CurrentSlot(Time.time);
            bool slotChanged = slot != stockSlot;
            stockSlot = slot;
            if (modelChanged || slotChanged)
                showcasePaintIndex = ShopStockService.DisplayPaintIndex(config, slot);
            if (config.IsRental)
                showcasePaintIndex = 0; // аренда всегда заводская
            if (swatchConfig != config)
            {
                swatchConfig = config;
                RebuildPaintSwatches(config);
            }
            ApplyShowcaseSprite(fleet, config, showcasePaintIndex);

            // «ЯЙЦО · ТИР 2» + статус владения
            showcaseName.text = config.DisplayName.ToUpper() + " · ТИР " + config.Tier;
            showcaseState.text = OwnershipLine(fleet, config);
            UpdateShowcaseStats(config);
            UpdatePaintSwatches();
            showcasePaint.text = PaintLineText(config, showcasePaintIndex, config.IsRental);

            bool rental = config.IsRental;
            bool current = IsCurrent(fleet, config);
            bool owned = fleet.IsOwned(config);
            bool canBuy = !owned && !rental && config.Price > 0;
            bool canSit = !current && (owned || rental);
            bool affordable = host.Session.Wallet.Credits >= config.Price;
            shopBuy.interactable = canBuy ? affordable : canSit;
            shopBuyLabel.text = canBuy
                ? $"КУПИТЬ · {UiFormat.Currency} {UiFormat.FormatCredits(config.Price)}"
                : canSit ? "ПЕРЕСЕСТЬ"
                : current ? "СЕЙЧАС АКТИВЕН"
                : "НЕТ В ПРОДАЖЕ";
            // недостача кредитов: кнопка гаснет + строка с нехваткой
            shopStatus.text = canBuy && !affordable
                ? $"Не хватает {UiFormat.Currency} {UiFormat.FormatCredits(config.Price - host.Session.Wallet.Credits)}"
                : "";
        }

        // статус владения под названием
        private static string OwnershipLine(FleetService fleet, ShipConfig config)
        {
            bool current = IsCurrent(fleet, config);
            if (current)
                return fleet.IsOwned(config)
                    ? "ВАШ · СЕЙЧАС АКТИВЕН"
                    : "В АРЕНДЕ · СЕЙЧАС АКТИВЕН";
            if (fleet.IsOwned(config))
                return "ВАШ · В АНГАРЕ";
            return config.IsRental ? "АРЕНДА" : "";
        }

        // спрайт показанного варианта; нет арта — заглушка по тиру;
        // своя модель «в ангаре» — серым
        private void ApplyShowcaseSprite(FleetService fleet, ShipConfig config, int paintIndex)
        {
            Sprite sprite = config.GetPaintSprite(paintIndex);
            bool current = IsCurrent(fleet, config);
            bool owned = fleet.IsOwned(config);
            showcaseSprite.sprite = sprite != null ? sprite : StubSprites.ShipTriangle();
            showcaseSprite.color = sprite == null ? TierShade(config.Tier)
                : owned && !current ? new Color(0.62f, 0.62f, 0.62f, 0.9f) // «в ангаре» — серым
                : Color.white;
        }

        // строка «Расцветка: …» под названием модели
        private static string PaintLineText(ShipConfig config, int paintIndex, bool rental = false)
        {
            if (rental)
                return "Расцветка: заводская · аренда без покраски";
            int variants = config.PaintVariantCount;
            return variants == 0 ? "Расцветка: заводская"
                : paintIndex == 0
                    ? $"Расцветка: заводская · ещё {variants} в ротации"
                    : $"Расцветка: вариант {paintIndex} из {variants} (ротация 2 игрочаса)";
        }

        // значение стата модели по индексам StatLabels (все «больше = лучше»)
        private float StatValue(ShipConfig config, int index)
        {
            switch (index)
            {
                case 0: return config.Thrust;
                case 1: return config.MaxV;
                case 2: return config.FuelMax;
                case 3: return config.HpMax;
                case 4: return RetroOf(config);
                default: return config.StrafeAcc;
            }
        }

        // модельное Retro или глобальный фолбэк (80); 0 не отдаём
        private float RetroOf(ShipConfig config)
        {
            if (config != null && config.Retro > 0f)
                return config.Retro;
            float global = host.Session != null && host.Session.Config != null ? host.Session.Config.Retro : 80f;
            return global > 0f ? global : 80f;
        }

        // твоё текущее значение: эффективные статы с учётом улучшений
        // (MaxV улучшениями не меняется — из конфига модели)
        private float CurrentStatValue(int index)
        {
            ShipConfig baseline = host.Session != null && host.Session.Fleet != null ? host.Session.Fleet.Current : null;
            if (host.Ship == null)
                return baseline != null ? StatValue(baseline, index) : 0f;
            switch (index)
            {
                case 0: return host.Ship.Eff.Thrust;
                case 1: return baseline != null ? baseline.MaxV : 0f;
                case 2: return host.Ship.Eff.FuelMax;
                case 3: return host.Ship.Eff.HpMax;
                case 4: return host.Ship.Eff.Retro;
                default: return host.Ship.Eff.Strafe;
            }
        }

        // масштаб — максимум по каталогу и твоим статам: полоски всех моделей
        // сопоставимы, риска не выходит за дорожку
        private float StatScale(int index, float value, float current)
        {
            float max = Mathf.Max(value, current);
            ShipConfig[] catalog = Catalog();
            for (int c = 0; c < catalog.Length; c++)
                if (catalog[c] != null)
                    max = Mathf.Max(max, StatValue(catalog[c], index));
            return max > 0f ? max : 1f;
        }

        // полоска — модель, риска — твоё значение; отрезок между ними
        // зелёный/красный по сравнению
        private void UpdateShowcaseStats(ShipConfig config)
        {
            if (showcaseStatRows == null || config == null || host.Session == null || host.Session.Fleet == null)
                return;
            for (int i = 0; i < showcaseStatRows.Length; i++)
            {
                float value = StatValue(config, i);
                float current = CurrentStatValue(i);
                float max = StatScale(i, value, current);
                float valueFraction = Mathf.Clamp01(value / max);
                float currentFraction = Mathf.Clamp01(current / max);

                var row = showcaseStatRows[i];
                StationUI.SetFill(row.ValueFill, valueFraction);
                SetSegment(row.DeltaFill, currentFraction, valueFraction);
                SetTick(row.Tick, currentFraction);

                float diff = value - current;
                bool better = diff > 0.001f;
                bool worse = diff < -0.001f;
                row.DeltaImage.color = better ? OnboardComputerFactory.BrightColor
                    : worse ? OnboardComputerFactory.DangerColor
                    : OnboardComputerFactory.LabelColor;
                row.Value.text = Mathf.RoundToInt(value).ToString();
            }
        }

        // «Яйцо» из фолбэка и ассет аренды — одна модель: до первой пересадки
        // Current не совпадает с витриной по ссылке
        private static bool IsCurrent(FleetService fleet, ShipConfig config)
        {
            if (fleet.Current == config)
                return true;
            return config != null && config.IsRental
                && fleet.Current != null && fleet.Current.IsRental;
        }

        // затенение заглушки по тиру: Т1 — приглушённая, Т5 — яркая
        private static Color TierShade(int tier)

        {
            float t = Mathf.Clamp01((tier - 1) / 4f);
            var dim = OnboardComputerFactory.LabelColor;
            dim.a = 0.4f;
            return Color.Lerp(dim, OnboardComputerFactory.BrightColor, t);
        }

        // единственная кнопка: покупка показанного варианта или пересадка.
        // Правила — в FleetService.TryBuy (после оплаты сразу пересаживает,
        // ShipChanged перерисует витрину сам)
        private void ConfirmShopAction()
        {
            if (host.Session == null || host.Session.Fleet == null || showcaseShip == null || !host.CanOpen())
                return;
            var config = showcaseShip;
            if (host.Session.Fleet.IsOwned(config) || config.IsRental)
            {
                SitIn(config);
                return;
            }
            int price = config.Price;
            if (price <= 0)
                return; // «НЕТ В ПРОДАЖЕ» — кнопка серая, но поток проверяет сам
            // покупка берёт индекс покраски с витрины — «показано X, куплено X»
            int paintIndex = showcasePaintIndex;
            if (!host.Session.Fleet.TryBuy(config, host.Session.Wallet, paintIndex))
                return;
            Sound.Play(SoundId.PurchaseCash); // касса на покупку корабля
            host.ShowToast($"Куплен «{config.DisplayName}» (−{UiFormat.Currency} {UiFormat.FormatCredits(price)})", emphasized: true, tone: ToastTone.Danger); // списание — красный
            // купленная модель остаётся в списке серой («ВАШ · В АНГАРЕ»),
            // витрина листается дальше
            FlipShowcase(1);
        }

        private void SitIn(ShipConfig config)
        {
            if (host.Session == null || host.Session.Fleet == null || config == null || !host.CanOpen())
                return;
            if (IsCurrent(host.Session.Fleet, config))
                return; // уже за штурвалом
            host.Session.Fleet.SwitchTo(config);
            host.ShowToast($"Пересел в «{config.DisplayName}»");
        }
    }
}
