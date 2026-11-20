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
    // Экран завершения заказа: модальная разбивка выплаты (PayoutService.
    // LastResult). Мир на паузе; строки появляются по одной (выезд + фейд),
    // суммы прокатываются счётчиками. ПРОБЕЛ не закрывает: в мапе это тяга —
    // закрытие взлетало бы тем же кадром. ESC — по общей договорённости
    // (EscJustClosed). Панель строится кодом при первом показе; анимации —
    // unscaledTime
    public class OrderCompleteScreenController : ModalScreenControllerBase
    {
        private const int SortingOrder = 110; // поверх станции (100), ниже увольнения (200)

        private const float RowSlideSeconds = 0.2f;    // выезд/фейд одной строки
        private const float RowInterval = 0.15f;       // между стартами строк
        private const float RowSlideFrom = -32f;       // стартовый сдвиг текста влево, px
        private const float CloseGuardSeconds = 0.25f; // экран не закрывается в кадр открытия

        // строка разбивки: группа (альфа) + внутренний текст (слайд). Текст —
        // ребёнок строки, а не прямой элемент layout-группы: та двигает только
        // прямых детей, слайд внуков не ломает
        private sealed class PayoutRow
        {
            public TMP_Text Text;
            public CanvasGroup Group;
            public RectTransform Slide;
            public float StartAt; // unscaledTime начала появления

            // прокатка числа: Shown догоняет Target по unscaledDeltaTime
            // (экран живёт при timeScale = 0), старт — при появлении строки
            public bool Rolling;
            public int Target;    // итоговая сумма со знаком (+приход / −убыток)
            public float Shown;   // текущее прокатанное значение
            public string Prefix; // «Перевозка: » и т.п.
            public string Suffix; // хвост вне прокатки (« (2 столкн.)»)
        }

        private bool bound;
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




        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            ModalBootstrap<OrderCompleteScreenController>.Ensure();
        }

        private void Update()
        {
            BindSession();
            if (!opened)
                return;

            Animate();
            HandleModalKeys(); // ESC/Enter + гвард кадра открытия — в базе
        }


        // лениво, пока сессия не соберётся
        private void BindSession()
        {
            if (bound)
                return;
            session = FindAnyObjectByType<GameSession>();
            if (session == null || session.Payout == null)
                return;
            bound = true;
        }

        // открытие: GameSession зовёт после выплаты; offer даёт имя пассажира
        public void Show(OrderOffer offer = null)
        {
            if (opened)
                return;
            if (!bound)
                BindSession();
            if (session == null || session.Payout == null || session.Payout.LastResult == null)
                return; // выплаты не было — показывать нечего

            EnsureScreen();
            Refresh(offer);
            ActivateModal();

            if (pauseController == null) pauseController = FindAnyObjectByType<PauseScreenController>();
            if (pauseController != null)
                pauseController.NotifyOrderCompleteOpened();
        }

        // закрытие (timeScale/скрытие) — в базе; хвост — уведомить паузу
        protected override void OnClosed()
        {
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

            // приход со «+», убытки с «−». «Перевозка» — цена карточки целиком:
            // бонус рейтинга запекается при спавне (PriceWithRatingBonus),
            // базовой цены в offer нет — разделить честно нельзя
            SetMoneyRow(priceRow, true, "Перевозка: ", result.Price);
            SetMoneyRow(tipsRow, result.Tips > 0, "Чаевые: ", result.Tips);
            SetMoneyRow(penaltyRow, result.Penalty > 0, "Штраф: ", -result.Penalty,
                result.Collisions > 0 ? $" ({result.Collisions} столкн.)" : "");
            SetMoneyRow(commissionRow, result.Commission > 0, "Комиссия аренды: ", -result.Commission);
            SetMoneyRow(debtRow, result.ToDebt > 0, "Долг: ", -result.ToDebt);
            SetMoneyRow(totalRow, true, "ИТОГО: ", result.Net);

            float delta = session.Rating != null ? session.Rating.LastDelta : 0f;
            SetRow(ratingRow, true, "Рейтинг " + delta.ToString("+0.00;-0.00", CultureInfo.InvariantCulture));
            // тон по знаку дельты
            ratingRow.Text.color = delta >= 0f
                ? OnboardComputerFactory.RatingGreen
                : OnboardComputerFactory.DangerColor;

            // статус задания читается напрямую из task (в LastResult не пишется);
            // у точек интереса незавершённое к доставке = провал
            bool taskDone = hasPassenger && offer.Task != null && offer.Task.Succeeded;
            bool taskFailed = hasPassenger && offer.Task != null
                && (offer.Task.Failed
                    || (PassengerTasks.IsInterestPoint(offer.Task.Kind) && !offer.Task.Succeeded));
            SetRow(taskRow, taskDone || taskFailed,
                taskFailed ? "Задание пассажира: провалено — жалоба" : "Задание пассажира: выполнено");

            ScheduleRows();
        }

        // строка разбивки: скрыта при нуле (чтобы экран был короче без инцидентов)
        private static void SetRow(PayoutRow row, bool visible, string text)
        {
            row.Group.gameObject.SetActive(visible);
            if (visible)
                row.Text.text = text;
        }

        // денежная строка: финальный текст пишется сразу (страховка от сбоя
        // анимации), прокатку стартует ScheduleRows
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

        // «+Đ 850» / «−Đ 1.2k» — как деньги в шапке борткомпа
        private static string FormatSigned(int value)
        {
            return (value < 0 ? "−" : "+") + UiFormat.Currency + " " + UiFormat.FormatCredits(Mathf.Abs(value));
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
                // прокатка стартует с нуля — повторное открытие не наследует прошлое
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

        // скорость пропорциональна остатку; дойдя до цели — финальный текст
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

        // каркас строится кодом при первом показе
        protected override GameObject BuildScreen()
        {
            var root = new GameObject("OrderCompleteScreen", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            // масштаб как у канваса сцены — иначе экран расходится с HUD
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
