using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;
using SpaceTaxi.Orders;

namespace SpaceTaxi.UI
{
    // Экран завершения заказа (П4.3): модальная разбивка последней выплаты.
    // Данные — PayoutService.LastResult (null — экран не открывать) и дельта
    // рейтинга RatingService.LastDelta. Мир ставится на паузу (timeScale = 0)
    // на время экрана; закрытие ЛЮБЫМ путём (кнопка/ESC/Enter) возвращает 1.
    // ПРОБЕЛ закрывать НЕ МОЖЕТ: в мапе Ship это Thrust — закрытие по Space
    // взлетало тем же кадром, и камера «улетала» с места доставки (Н-9).
    // ESC уступается паузой по договорённости карты/станции
    // (NotifyOrderCompleteOpened/Closed + EscJustClosed): при открытом экране
    // завершения ESC закрывает его, пауза не открывается. Контроллера в сценах
    // нет — появляется сам (стиль DismissalScreenController), панель строится
    // кодом при первом показе (паттерн StationScreen П3.1).
    // Н-9: строки разбивки появляются по одной (выезд слева + фейд 0.2 с,
    // интервал 0.15 с). Вся анимация — по unscaledTime: модал живёт при
    // timeScale = 0. Н4.4: суммы в валюте борткомпа (Đ + компактный формат
    // OnboardBinder.FormatCredits) и каждая денежная строка прокатывается
    // счётчиком — паттерн AnimateMoney: значение догоняет целевое по
    // unscaledDeltaTime, старт в момент появления строки. Цвета по знаку:
    // приход (+) — яркий борткомпа, убытки (−) — красный, долг — «спокойный»
    // DebtCalmColor, дельта рейтинга — зелень/красный по знаку.
    public class OrderCompleteScreenController : MonoBehaviour
    {
        private const int SortingOrder = 110; // поверх станции (100), ниже увольнения (200)

        private const float RowSlideSeconds = 0.2f;    // выезд/фейд одной строки
        private const float RowInterval = 0.15f;       // между стартами строк
        private const float RowSlideFrom = -32f;       // стартовый сдвиг текста влево, px
        private const float CloseGuardSeconds = 0.25f; // кадр открытия не закрывает экран

        // валюта борткомпа (как OnboardBinder); минус в суммах — U+2212,
        // как в подписях кнопок панели («Отменить · −Đ N»)
        private const string Currency = "Đ";

        // строка разбивки: группа (альфа) + внутренний текст (слайд). Текст —
        // ребёнок строки, а не прямой элемент layout-группы: та двигает только
        // прямых детей, слайд внуков не ломает
        private sealed class PayoutRow
        {
            public TMP_Text Text;
            public CanvasGroup Group;
            public RectTransform Slide;
            public float StartAt; // unscaledTime начала появления

            // прокатка числа (Н4.4, паттерн AnimateMoney OnboardBinder):
            // Shown догоняет Target по unscaledDeltaTime — экран живёт при
            // timeScale = 0; старт — в момент появления строки
            public bool Rolling;
            public int Target;    // итоговая сумма со знаком (+приход / −убыток)
            public float Shown;   // текущее прокатанное значение
            public string Prefix; // «Перевозка: » и т.п.
            public string Suffix; // хвост вне прокатки (« (2 столкн.)»)
        }

        private bool bound;
        private bool opened;
        private GameObject screen;
        private GameSession session;
        private PauseScreenController pauseController;

        private PayoutRow subtitle; // шапка — без анимации
        private readonly List<PayoutRow> rows = new List<PayoutRow>(); // порядок показа
        private PayoutRow priceRow;
        private PayoutRow penaltyRow;
        private PayoutRow commissionRow;
        private PayoutRow tipsRow;
        private PayoutRow debtRow;
        private PayoutRow totalRow;
        private PayoutRow ratingRow;
        private PayoutRow taskRow;

        private float openedAtRealtime;

        public bool IsOpened => opened;

        public bool EscJustClosed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // домен редактора может не перезагружаться между запусками — снимаем старую подписку
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Spawn();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Spawn();
        }

        private static void Spawn()
        {
            if (FindAnyObjectByType<OrderCompleteScreenController>() != null)
                return;
            new GameObject("OrderCompleteScreenController").AddComponent<OrderCompleteScreenController>();
        }

        private void Update()
        {
            BindSession();
            if (!opened)
                return;

            Animate();

            var kb = Keyboard.current;
            if (kb == null)
                return;
            // нажатие кадра посадки (Space=Thrust, ESC) не должно закрыть экран
            // в тот же кадр — он не успел отрисоваться, владелец видит «только тост»
            if (Time.unscaledTime - openedAtRealtime < CloseGuardSeconds)
                return;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                Close();
                EscJustClosed = true;
            }
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                Close();
        }

        private void LateUpdate()
        {
            EscJustClosed = false;
        }

        private void OnDisable()
        {
            // страховка: экран сорвало со сцены — мир не должен остаться на паузе
            if (opened)
                Time.timeScale = 1f;
        }

        // лениво, пока сессия не соберётся (стиль биндеров)
        private void BindSession()
        {
            if (bound)
                return;
            session = FindAnyObjectByType<GameSession>();
            if (session == null || session.Payout == null)
                return;
            bound = true;
        }

        // открытие: GameSession зовёт после выплаты и тостов; offer даёт имя
        // пассажира для подзаголовка (может быть null)
        public void Show(OrderOffer offer = null)
        {
            if (opened)
                return;
            if (!bound)
                BindSession();
            if (session == null || session.Payout == null || session.Payout.LastResult == null)
                return; // выплаты не было — показывать нечего

            if (screen == null)
                screen = BuildScreen();
            Refresh(offer);
            screen.SetActive(true);
            opened = true;
            openedAtRealtime = Time.unscaledTime;
            Time.timeScale = 0f; // мир ждёт «ПРОДОЛЖИТЬ»

            if (pauseController == null) pauseController = FindAnyObjectByType<PauseScreenController>();
            if (pauseController != null)
                pauseController.NotifyOrderCompleteOpened();
        }

        private void Close()
        {
            if (!opened)
                return;
            opened = false;
            if (screen != null)
                screen.SetActive(false);
            Time.timeScale = 1f; // гарантия: любой путь закрытия возвращает мир

            if (pauseController == null) pauseController = FindAnyObjectByType<PauseScreenController>();
            if (pauseController != null)
                pauseController.NotifyOrderCompleteClosed();
        }

        private void Refresh(OrderOffer offer)
        {
            PayoutResult result = session.Payout.LastResult;

            bool hasPassenger = offer != null && offer.Passenger != null;
            SetRow(subtitle, hasPassenger, hasPassenger
                ? offer.Passenger.DisplayName + " доставлен"
                : "");

            // разбивка Н4.4: приход со «+», убытки с «−», суммы в формате
            // борткомпа (Đ + компактные числа); каждая строка прокатывается.
            // Порядок: Перевозка → Чаевые → Штраф → Комиссия аренды → Долг → ИТОГО
            // «Перевозка» — цена карточки ЦЕЛИКОМ: наценка рейтинга ×1.1
            // добавляется при спавне карточки (OrderService.PriceWithRatingBonus)
            // и запекается в offer.Price — базовая цена нигде не сохраняется.
            // Разделить на «база + бонус» в UI честно нельзя: текущий рейтинг не
            // равен рейтингу на момент спавна (доставка сама двигает его на
            // ±0.1–0.15, порог 4.0 в середине шкалы), а заказы из сейва вообще
            // не имеют контекста спавна — строка бонуса могла бы показывать
            // несуществующую надбавку. Чистое разделение требует одного поля
            // в OrderOffer (OrderService — вне скоупа правок этого экрана)
            SetMoneyRow(priceRow, true, "Перевозка: ", result.Price);
            SetMoneyRow(tipsRow, result.Tips > 0, "Чаевые: ", result.Tips);
            SetMoneyRow(penaltyRow, result.Penalty > 0, "Штраф: ", -result.Penalty,
                result.Collisions > 0 ? $" ({result.Collisions} столкн.)" : "");
            SetMoneyRow(commissionRow, result.Commission > 0, "Комиссия аренды: ", -result.Commission);
            SetMoneyRow(debtRow, result.ToDebt > 0, "Долг: ", -result.ToDebt);
            SetMoneyRow(totalRow, true, "ИТОГО: ", result.Net);

            float delta = session.Rating != null ? session.Rating.LastDelta : 0f;
            SetRow(ratingRow, true, "Рейтинг " + delta.ToString("+0.00;-0.00", CultureInfo.InvariantCulture));
            // тон по знаку дельты: плюс — зелень рейтинга шапки, минус — тревожный
            ratingRow.Text.color = delta >= 0f
                ? OnboardComputerFactory.RatingGreen
                : OnboardComputerFactory.DangerColor;

            // статус задания (П8.2) читается напрямую из task (UI-слой), в LastResult не пишется
            bool taskDone = hasPassenger && offer.Task != null && offer.Task.Succeeded;
            SetRow(taskRow, taskDone, "Задание пассажира: выполнено");

            ScheduleRows();
        }

        // строка разбивки: скрыта при нуле (чтобы экран был короче без инцидентов)
        private static void SetRow(PayoutRow row, bool visible, string text)
        {
            row.Group.gameObject.SetActive(visible);
            if (visible)
                row.Text.text = text;
        }

        // денежная строка: финальный текст пишется сразу (страховка на случай
        // сбоя анимации), ScheduleRows обнуляет показ — с момента появления
        // строки число прокатывается до Target
        private static void SetMoneyRow(PayoutRow row, bool visible, string prefix, int amount,
            string suffix = "")
        {
            row.Group.gameObject.SetActive(visible);
            row.Prefix = prefix;
            row.Suffix = suffix;
            row.Target = amount;
            row.Rolling = visible;
            if (visible)
                row.Text.text = prefix + FormatSigned(amount) + suffix;
        }

        // сумма со знаком и валютой борткомпа: «+Đ 850» / «−Đ 1.2k» — формат
        // тот же, что у денег в шапке (предсказуемая ширина строки под «…»)
        private static string FormatSigned(int value)
        {
            return (value < 0 ? "−" : "+") + Currency + " " + OnboardBinder.FormatCredits(Mathf.Abs(value));
        }

        // последовательность показа: видимые строки по одной, старт следующей
        // через RowInterval после предыдущей; скрытые слоты не занимают
        private void ScheduleRows()
        {
            float start = Time.unscaledTime + 0.1f;
            int slot = 0;
            foreach (PayoutRow row in rows)
            {
                if (!row.Group.gameObject.activeSelf)
                    continue;
                row.StartAt = start + RowInterval * slot++;
                row.Group.alpha = 0f;
                Vector2 pos = row.Slide.anchoredPosition;
                pos.x = RowSlideFrom;
                row.Slide.anchoredPosition = pos;
                // прокатка всегда стартует с нуля в момент появления строки —
                // повторное открытие экрана не наследует прошлое состояние
                row.Shown = 0f;
                if (row.Rolling)
                    row.Text.text = row.Prefix + FormatSigned(0) + row.Suffix;
            }
        }

        // покадровая анимация показа — только по unscaledTime (модал при timeScale = 0)
        private void Animate()
        {
            float now = Time.unscaledTime;
            foreach (PayoutRow row in rows)
            {
                if (!row.Group.gameObject.activeSelf)
                    continue;
                float t = Mathf.Clamp01((now - row.StartAt) / RowSlideSeconds);
                float eased = 1f - (1f - t) * (1f - t); // easeOutQuad
                row.Group.alpha = eased;
                Vector2 pos = row.Slide.anchoredPosition;
                pos.x = RowSlideFrom * (1f - eased);
                row.Slide.anchoredPosition = pos;

                AnimateRowValue(row, now);
            }
        }

        // прокатка числа строки (паттерн AnimateMoney OnboardBinder): скорость
        // пропорциональна остатку с нижним порогом, дойдя до цели — стоп и
        // финальный текст. Стартует вместе с появлением строки, не заранее
        private static void AnimateRowValue(PayoutRow row, float now)
        {
            if (!row.Rolling || now < row.StartAt || Mathf.Approximately(row.Shown, row.Target))
                return;
            float speed = Mathf.Max(80f, Mathf.Abs(row.Target - row.Shown) * 4f);
            row.Shown = Mathf.MoveTowards(row.Shown, row.Target, speed * Time.unscaledDeltaTime);
            if (Mathf.Abs(row.Target - row.Shown) < 0.5f)
                row.Shown = row.Target;
            row.Text.text = row.Prefix + FormatSigned(Mathf.RoundToInt(row.Shown)) + row.Suffix;
        }

        // каркас экрана строится кодом при первом показе (стиль OnboardComputerFactory)
        private GameObject BuildScreen()
        {
            var root = new GameObject("OrderCompleteScreen", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            // Масштаб как у сценического канваса (1920x1080, match 0.5) —
            // иначе на не-1080p разрешениях экран расходится с HUD
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(root.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)background.transform);
            background.GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.015f, 0.85f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(520f, 0f); // высоту подберёт fitter
            panel.GetComponent<Image>().color = OnboardComputerFactory.PanelColor;
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 18);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // --- Шапка: точка + заголовок + пассажир ---
            var titleRow = OnboardComputerFactory.H("TitleRow", panel.transform);
            // фикс-высота: 26pt*1.32 ≈ 34.3 (урок ночи про вертикальный Ellipsis)
            OnboardComputerFactory.FixedHeight(titleRow, 36f);
            var dot = OnboardComputerFactory.Text(titleRow, "<voffset=0.35em>●</voffset>", 18f, OnboardComputerFactory.TaxiColor, "LinkDot");
            OnboardComputerFactory.FixedWidth(dot.gameObject, 24f);
            var title = OnboardComputerFactory.Text(titleRow, "ЗАКАЗ ВЫПОЛНЕН", 26f, OnboardComputerFactory.BrightColor, "Title");
            OnboardComputerFactory.Flexible(title.gameObject);

            subtitle = BuildRow(panel.transform, "Subtitle", OnboardComputerFactory.LabelColor);

            var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(panel.transform, false);
            line.GetComponent<Image>().color = new Color(
                OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g, OnboardComputerFactory.LabelColor.b, 0.4f);
            OnboardComputerFactory.FixedHeight(line, 2f);

            // --- Разбивка выплаты: приход (+) — яркий борткомпа, убытки (−) — красные.
            // Порядок: Перевозка → Чаевые → Штраф → Комиссия аренды → Долг → ИТОГО ---
            priceRow = BuildRow(panel.transform, "PriceLine", OnboardComputerFactory.BrightColor);
            tipsRow = BuildRow(panel.transform, "TipsLine", OnboardComputerFactory.BrightColor);
            penaltyRow = BuildRow(panel.transform, "PenaltyLine", OnboardComputerFactory.DangerColor);
            commissionRow = BuildRow(panel.transform, "CommissionLine", OnboardComputerFactory.DangerColor);
            // «спокойный» красный долга — тот же тон, что у строки долга в шапке борткомпа
            debtRow = BuildRow(panel.transform, "DebtLine", OnboardComputerFactory.DebtCalmColor);

            totalRow = BuildRow(panel.transform, "TotalLine", OnboardComputerFactory.BrightColor, 28f, 40f);
            ratingRow = BuildRow(panel.transform, "RatingLine", OnboardComputerFactory.RatingGreen);
            taskRow = BuildRow(panel.transform, "TaskLine", OnboardComputerFactory.LabelColor, 16f, 22f);

            rows.Add(priceRow);
            rows.Add(tipsRow);
            rows.Add(penaltyRow);
            rows.Add(commissionRow);
            rows.Add(debtRow);
            rows.Add(totalRow);
            rows.Add(ratingRow);
            rows.Add(taskRow);

            // --- Кнопка продолжения ---
            var button = OnboardComputerFactory.Button(panel.transform, "ПРОДОЛЖИТЬ",
                new Color(OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g,
                    OnboardComputerFactory.LabelColor.b, 0.22f),
                OnboardComputerFactory.BrightColor, 20f, "ContinueButton");
            var buttonLe = button.AddComponent<LayoutElement>();
            buttonLe.minHeight = 46f;
            buttonLe.preferredHeight = 46f;
            button.GetComponent<Button>().onClick.AddListener(Close);

            var footer = OnboardComputerFactory.Text(panel, "ESC / ENTER — продолжить", 16f,
                OnboardComputerFactory.LabelColor, "Footer");
            OnboardComputerFactory.FixedHeight(footer.gameObject, 22f);

            return root;
        }

        // строка разбивки: строка-контейнер под layout-группой (высота, маска,
        // альфа) + внутренний текст на растяжке — его двигает анимация слайда
        private static PayoutRow BuildRow(Transform parent, string name, Color color, float size = 19f, float height = 26f)
        {
            var rowGo = new GameObject(name, typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            // урок ночи: Ellipsis режет текст и ПО ВЕРТИКАЛИ — узел с текстом
            // обязан быть ≥ fontSize*1.32 (JetBrainsMono), иначе 0 видимых символов
            height = Mathf.Max(height, Mathf.Ceil(size * 1.32f));
            var element = rowGo.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
            var group = rowGo.AddComponent<CanvasGroup>();
            rowGo.AddComponent<RectMask2D>(); // слайд «из-под края» строки, без налезания на соседей

            TMP_Text text = OnboardComputerFactory.Text(rowGo, "", size, color, name + "Text");
            var textRect = (RectTransform)text.transform;
            OnboardComputerFactory.Stretch(textRect);

            return new PayoutRow { Text = text, Group = group, Slide = textRect };
        }
    }
}
