using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Audio;
using SpaceTaxi.Orders;
using SpaceTaxi.Core;

namespace SpaceTaxi.UI
{
    // --- СОСТОЯНИЕ: деньги, рейтинг, долг ---
    // partial-секция OnboardBinder; сюда же — чип статуса задания (Н10.4)
    // и строка выполнения активного заказа (Н7 Q7.2).
    public partial class OnboardBinder
    {
        private TMP_Text moneyValue, ratingValue;
        // «★ 3.0»: префикс пуст, если атлас не дал глиф звезды (сброс dynamic-атласа)
        private string ratingPrefix = "";
        // анимация денег: показанное значение догоняет целевое (как рейтинг)
        private float moneyShown = -1f;
        private int moneyTarget;
        // строка долга: катится значением, цвет «дышит» — рост горячее погашения
        private GameObject debtRow;
        private TMP_Text debtValue;
        // «ПОГАСИТЬ · Đ N» рядом с долгом (Н7 Q7.6): списывает min(долг, кошелёк)
        private Button debtRepayButton;
        private float debtShown = -1f;
        private int debtTarget;
        private bool debtRising;
        // анимация рейтинга: показанное значение догоняет целевое, цвет — динамика
        private float ratingShown = -1f;
        private bool ratingDropping;
        // мини-строка выполнения под карточкой актива (Н7 Q7.2): этапы ЗАБРАТЬ/ВЕЗИ
        // и таймер «паникёра» (Н7 Q7.1); текст ведёт SlowUpdate
        private TMP_Text activeProgress;
        // Н10.4: статус-чип задания активного заказа — «выполняется…» →
        // «✓ выполнено» → «✗ провалено»; узел Expanded/Summary/Status,
        // текст ведёт SlowUpdate (источник: task.Failed/Succeeded)
        private TMP_Text activeStatus;
        // галочка этапа: «✓» есть не в каждом атласе (паттерн clockGlyph) — иначе «+»
        private string doneGlyph = "+";
        // Н10.4: «✗» есть не в каждом атласе (тот же паттерн) — иначе «×»;
        // «…» при отсутствии глифа — три точки
        private string failGlyph = "×";
        private string ellipsis = "...";
        // таймер «паникёра» (Н7 Q7.1): с этого остатка цвет плывёт к Danger;
        // публичная — HudBinder красит той же логикой подсказку цели
        public const float HurryWarnSeconds = 60f;
        private const float ProgressRowHeight = 26f; // высота строк сводки (17pt + запас)

        // Н10.4: статус-чип задания в строке Status активной карточки.
        // У точек интереса «✓» появляется в момент свершения (TrackInterestPoint
        // ставит Succeeded в тот же кадр, что и тост-момент); у избегающих
        // живой статус — только «выполняется…»/«✗ провалено»: успех у них
        // ставит SettleTask при доставке, когда карточка уже гибнет.
        // Тосты избегающим НЕ добавлять — владелец выбрал чип вместо тоста.
        // Сейв восстанавливает Succeeded/Failed — чип верен сразу после лоада
        private void UpdateActiveStatusChip()
        {
            if (activeCardGo == null || orders.Active == null)
                return;
            if (activeStatus == null)
                activeStatus = FindText(activeCardGo.transform, "Expanded/Summary/Status");
            if (activeStatus == null)
                return;
            PassengerTask task = orders.Active.Task;
            if (task == null || task.Kind == PassengerTaskKind.None)
            {
                activeStatus.gameObject.SetActive(false); // задания нет — строка не шумит
                return;
            }
            activeStatus.gameObject.SetActive(true);
            string label = $"<color={ColorHex(OnboardComputerFactory.LabelColor)}>Задание:</color>";
            if (task.Failed)
                activeStatus.text = label + $"<color={ColorHex(OnboardComputerFactory.DangerColor)}> {failGlyph} провалено</color>";
            else if (task.Succeeded)
                activeStatus.text = label + $"<color={ColorHex(OnboardComputerFactory.RatingGreen)}> {doneGlyph} выполнено</color>";
            else
                activeStatus.text = label + $"<color={ColorHex(OnboardComputerFactory.TaxiColor)}> выполняется{ellipsis}</color>";
        }

        // --- Строка выполнения активного заказа (Н7 Q7.2) ---

        // мини-строка под карточкой актива: «ВЫПОЛНЕНИЕ: ЗАБРАТЬ: А → ВЕЗИ: Б»
        // со состоянием этапов и таймером «паникёра». Рядовой элемент activeArea —
        // гибнет вместе с карточкой при смене заказа (activeAreaChildren)
        private void BuildActiveProgressRow()
        {
            var row = new GameObject("ActiveProgress", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(activeArea, false);
            activeAreaChildren.Add(row);
            LayoutElement le = row.GetComponent<LayoutElement>();
            le.preferredHeight = ProgressRowHeight;
            le.flexibleWidth = 1f;
            activeProgress = OnboardComputerFactory.Text(row, "", 17f, OnboardComputerFactory.BrightColor);
            activeProgress.alignment = TextAlignmentOptions.Left;
            UpdateActiveProgressRow();
        }

        // этапы: галочка — готово (Bright), стрелка — текущий (Taxi), точки —
        // ожидает (приглушённый); текст ведёт SlowUpdate (смена состояния заказа
        // видна в четверть секунды, карточка при этом не перестраивается)
        private void UpdateActiveProgressRow()
        {
            if (activeProgress == null || orders.Active == null)
                return;
            OrderOffer offer = orders.Active;
            bool aboard = offer.State == OrderState.Aboard;
            string pickupLabel = "ЗАБРАТЬ: " + orders.PlanetName(offer.PickupIndex).ToUpper();
            string dropLabel = "ВЕЗИ: " + orders.PlanetName(offer.DropoffIndex).ToUpper();
            string pickup = aboard ? StageDone(pickupLabel) : StageCurrent(pickupLabel);
            string drop = aboard ? StageCurrent(dropLabel) : StagePending(dropLabel);
            string text = $"<color={ColorHex(OnboardComputerFactory.LabelColor)}>ВЫПОЛНЕНИЕ:</color> {pickup} → {drop}";
            string hurry = HurryText(offer);
            if (hurry != null)
                text += " · " + hurry;
            activeProgress.text = text;
        }

        private string StageDone(string label)
        {
            return $"<color={ColorHex(OnboardComputerFactory.BrightColor)}>{doneGlyph} {label}</color>";
        }

        private string StageCurrent(string label)
        {
            return $"<color={ColorHex(OnboardComputerFactory.TaxiColor)}>→ {label}</color>";
        }

        private string StagePending(string label)
        {
            return $"<color={ColorHex(OnboardComputerFactory.LabelColor)}>·· {label}</color>";
        }

        // таймер «паникёра» для строки выполнения (Н7 Q7.1); null — пассажир
        // не торопится. Цвет плывёт к Danger по мере истечения (HurryWarnSeconds),
        // приписка «срыв заказа» объясняет, что будет на нуле
        private string HurryText(OrderOffer offer)
        {
            if (offer.HurrySeconds <= 0f || offer.HurryStartedAt < 0f)
                return null;
            float remaining = offer.HurryRemaining(Time.time);
            Color color = Color.Lerp(OnboardComputerFactory.TaxiColor, OnboardComputerFactory.DangerColor,
                1f - Mathf.Clamp01(remaining / HurryWarnSeconds));
            return $"<color={ColorHex(color)}>ТОРОПИТСЯ: {UiFormat.FormatClock(remaining)} · срыв заказа</color>";
        }

        // --- СОСТОЯНИЕ: деньги, рейтинг, долг ---

        // кредит обновляется событием, значение плавно докатывается; до первого
        // события moneyShown == -1 (сентинел) — текст не пишем, не мигнув «Đ -1»
        private void ShowCredits(int credits)
        {
            moneyTarget = credits;
            if (moneyShown < 0f)
                moneyShown = credits;
            UpdateMoneyText();
            // сумма на кнопке гашения = min(долг, кошелёк) — кошелёк изменился
            RefreshDebtRepay();
        }

        private void AnimateMoney()
        {
            if (moneyValue == null || moneyShown < 0f || Mathf.Approximately(moneyShown, moneyTarget))
                return;
            float speed = Mathf.Max(80f, Mathf.Abs(moneyTarget - moneyShown) * 4f);
            moneyShown = Mathf.MoveTowards(moneyShown, moneyTarget, speed * Time.unscaledDeltaTime);
            if (Mathf.Abs(moneyTarget - moneyShown) < 0.5f)
                moneyShown = moneyTarget;
            UpdateMoneyText();
        }

        private void UpdateMoneyText()
        {
            if (moneyValue != null && moneyShown >= 0f)
                moneyValue.text = UiFormat.Currency + " " + UiFormat.FormatCredits(Mathf.RoundToInt(moneyShown));
        }

        // рейтинг догоняет целевое значение, цвет — динамика (красный/зелёный)
        private void ShowRating(float value)
        {
            if (ratingValue == null)
                return;
            if (ratingShown < 0f)
                ratingShown = value;
            ratingDropping = value < ratingShown - 0.001f;
            WriteRatingText();
        }

        private void AnimateRating()
        {
            // сессия кэшируется при бинде: тяжёлый поиск в каждом кадре не нужен
            var s2 = cachedSession != null ? cachedSession : FindAnyObjectByType<GameSession>();
            cachedSession = s2;
            var rating2 = s2 != null ? s2.Rating : null;
            if (rating2 == null || ratingValue == null)
                return;
            float target = rating2.Value;
            if (ratingShown < 0f)
                ratingShown = target;
            if (Mathf.Approximately(ratingShown, target))
                return;
            ratingDropping = target < ratingShown;
            // плавный glide: скорость пропорциональна дельте (лёгкий разгон и мягкий
            // докат), минимум не даёт хвосту «залипать» — виден перелист 3.0 → 4.5
            float delta = Mathf.Abs(target - ratingShown);
            float speed = Mathf.Max(delta * 4f, 0.4f);
            ratingShown = Mathf.MoveTowards(ratingShown, target, speed * Time.unscaledDeltaTime);
            if (Mathf.Abs(target - ratingShown) < 0.005f)
                ratingShown = target;
            WriteRatingText();
        }

        private void WriteRatingText()
        {
            if (ratingValue == null)
                return;
            ratingValue.text = ratingPrefix + ratingShown.ToString("0.0", CultureInfo.InvariantCulture);
            // в покое — зелёный (цвет рейтинга в шапке), в динамике — красный/зелёный
            ratingValue.color = ratingDropping
                ? OnboardComputerFactory.DangerColor
                : OnboardComputerFactory.RatingGreen;
        }

        // долг: строка скрыта при нуле (layout сжимается), значение катится,
        // цвет «дышит» — рост горячее, погашение спокойнее
        private void RefreshDebt(int value)
        {
            if (debtValue == null)
                return;
            if (debtShown < 0f)
                debtShown = value;
            debtRising = value > debtShown + 0.5f;
            debtTarget = value;
            if (debtRow != null)
                debtRow.SetActive(value > 0 || debtShown > 0.5f);
            UpdateDebtText();
            RefreshDebtRepay(); // сумма/доступность кнопки гашения по новому долгу
        }

        private void AnimateDebt()
        {
            if (debtValue == null || debtShown < 0f || Mathf.Approximately(debtShown, debtTarget))
                return;
            float speed = Mathf.Max(40f, Mathf.Abs(debtTarget - debtShown) * 3f);
            debtShown = Mathf.MoveTowards(debtShown, debtTarget, speed * Time.unscaledDeltaTime);
            if (Mathf.Abs(debtTarget - debtShown) < 0.5f)
                debtShown = debtTarget;
            if (debtRow != null && debtTarget <= 0 && debtShown <= 0.5f)
                debtRow.SetActive(false); // докатка до нуля закончилась — строка уходит
            UpdateDebtText();
        }

        private void UpdateDebtText()
        {
            if (debtValue == null || debtShown < 0f)
                return;
            debtValue.text = "Текущий долг: " + UiFormat.Currency + " " + UiFormat.FormatCredits(Mathf.RoundToInt(debtShown));
            debtValue.color = debtRising && debtTarget > debtShown + 0.5f
                ? OnboardComputerFactory.DangerColor
                : OnboardComputerFactory.DebtCalmColor;
        }

        // кнопка «ПОГАСИТЬ · Đ N» (Н7 Q7.6): N = min(долг, кошелёк) — сколько
        // реально спишется сейчас; без денег гаснет (лейбл в цвет лейблов,
        // паттерн «КУПИТЬ» станции, Н6 S4). Показанные числа — фактические,
        // без анимации докатки (как строка долга на станции)
        private void RefreshDebtRepay()
        {
            if (debtRepayButton == null || debt == null || wallet == null || debt.Balance <= 0)
                return;
            int amount = Mathf.Min(debt.Balance, wallet.Credits);
            TMP_Text label = debtRepayButton.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = "ПОГАСИТЬ · " + UiFormat.Currency + " " + UiFormat.FormatCredits(amount);
                label.color = amount > 0
                    ? OnboardComputerFactory.BrightColor
                    : OnboardComputerFactory.LabelColor;
            }
            debtRepayButton.interactable = amount > 0;
        }

        // досрочное гашение из борткомпа: списывает сколько есть — min(долг,
        // кошелёк), остаток остаётся долгом. Полный расчёт — тост Success (Н7 Q7.6),
        // частичное — Danger (деньги ушли, U4); тон расчёта — как на станции
        private void RepayDebtNow()
        {
            if (debt == null || wallet == null || debt.Balance <= 0)
                return;
            int amount = Mathf.Min(debt.Balance, wallet.Credits);
            if (amount <= 0 || !wallet.TrySpend(amount))
                return;
            debt.Repay(amount);
            bool full = debt.Balance <= 0;
            Sound.Play(SoundId.DebtTone); // П9.3: тон расчёта по долгу
            FindAnyObjectByType<ToastNotifier>()?.Show(
                full
                    ? "Долг погашен"
                    : $"Долг уменьшен: −{UiFormat.Currency} {UiFormat.FormatCredits(amount)} · осталось {UiFormat.Currency} {UiFormat.FormatCredits(debt.Balance)}",
                full, full ? ToastTone.Success : ToastTone.Danger);
        }
    }
}
