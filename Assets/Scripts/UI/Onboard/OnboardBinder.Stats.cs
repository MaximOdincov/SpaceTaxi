using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Audio;
using SpaceTaxi.Orders;
using SpaceTaxi.Core;

namespace SpaceTaxi.UI
{
    // Деньги, рейтинг, долг + чип статуса задания и строка выполнения заказа
    public partial class OnboardBinder
    {
        private TMP_Text moneyValue, ratingValue;
        // «★ 3.0»: префикс пуст, если атлас не дал глиф звезды (сброс dynamic-атласа)
        private string ratingPrefix = "";
        // анимации докатки (RollingNumber): показанное значение догоняет
        // целевое по unscaled-времени; деньги/долг — целые, рейтинг — десятые
        private RollingNumber moneyRoll = new RollingNumber { Shown = -1f };
        // строка долга: катится значением, цвет «дышит» — рост горячее погашения
        private GameObject debtRow;
        private TMP_Text debtValue;
        // «ПОГАСИТЬ»: списывает min(долг, кошелёк)
        private Button debtRepayButton;
        private RollingNumber debtRoll = new RollingNumber { Shown = -1f };
        private bool debtRising;
        // анимация рейтинга: докатка + цвет-динамика (падение краснит)
        private RollingNumber ratingRoll = new RollingNumber { Shown = -1f };
        private bool ratingDropping;
        // строка этапов ЗАБРАТЬ/ВЕЗИ под карточкой актива; текст ведёт SlowUpdate
        private TMP_Text activeProgress;
        // чип статуса задания (узел Expanded/Summary/Status); ведёт SlowUpdate
        private TMP_Text activeStatus;
        // запасные глифы, если «✓»/«✗»/«…» нет в атласе шрифта
        private string doneGlyph = "+";
        private string failGlyph = "×";
        private string ellipsis = "...";
        // остаток, с которого цвет таймера плывёт к красному (используется и HUD)
        public const float HurryWarnSeconds = 60f;
        private const float ProgressRowHeight = 26f; // высота строк сводки (17pt + запас)

        // статус задания: «выполняется…» → «✓ выполнено» / «✗ провалено»;
        // текст ведёт SlowUpdate
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

        // --- Строка выполнения активного заказа ---

        // строка выполнения — элемент activeArea, гибнет вместе с карточкой
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

        // галочка — готово, стрелка — текущий этап, точки — ожидает;
        // текст ведёт SlowUpdate, карточка не перестраивается
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

        // таймер торопящегося пассажира; null — не торопится. Цвет плывёт
        // к красному по мере истечения
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
        // события Shown == -1 (сентинел) — текст не пишем, не мигнув «Đ -1»
        private void ShowCredits(int credits)
        {
            moneyRoll.Target = credits;
            if (moneyRoll.Shown < 0f)
                moneyRoll.Shown = credits;
            UpdateMoneyText();
            // сумма на кнопке гашения = min(долг, кошелёк) — кошелёк изменился
            RefreshDebtRepay();
        }

        private void AnimateMoney()
        {
            if (moneyValue != null && moneyRoll.Step(80f, 4f))
                UpdateMoneyText();
        }

        private void UpdateMoneyText()
        {
            if (moneyValue != null && moneyRoll.Shown >= 0f)
                moneyValue.text = UiFormat.Currency + " " + UiFormat.FormatCredits(Mathf.RoundToInt(moneyRoll.Shown));
        }

        // рейтинг: показывается сразу, дальше докатывается с цветом динамики
        private void ShowRating(float value)
        {
            if (ratingValue == null)
                return;
            if (ratingRoll.Shown < 0f)
                ratingRoll.Shown = value;
            ratingDropping = value < ratingRoll.Shown - 0.001f;
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
            ratingRoll.Target = rating2.Value;
            if (ratingRoll.Shown < 0f)
                ratingRoll.Shown = ratingRoll.Target;
            if (Mathf.Approximately(ratingRoll.Shown, ratingRoll.Target))
                return;
            ratingDropping = ratingRoll.Target < ratingRoll.Shown;
            // плавный glide: скорость растёт с дельтой, минимум не даёт хвосту
            // залипать — виден перелист 3.0 → 4.5
            if (ratingRoll.Step(0.4f, 4f, 0.005f))
                WriteRatingText();
        }

        private void WriteRatingText()
        {
            if (ratingValue == null)
                return;
            ratingValue.text = ratingPrefix + ratingRoll.Shown.ToString("0.0", CultureInfo.InvariantCulture);
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
            if (debtRoll.Shown < 0f)
                debtRoll.Shown = value;
            debtRising = value > debtRoll.Shown + 0.5f;
            debtRoll.Target = value;
            if (debtRow != null)
                debtRow.SetActive(value > 0 || debtRoll.Shown > 0.5f);
            UpdateDebtText();
            RefreshDebtRepay(); // сумма/доступность кнопки гашения по новому долгу
        }

        private void AnimateDebt()
        {
            if (debtValue == null || !debtRoll.Step(40f, 3f))
                return;
            if (debtRow != null && debtRoll.Target <= 0 && debtRoll.Shown <= 0.5f)
                debtRow.SetActive(false); // докатка до нуля закончилась — строка уходит
            UpdateDebtText();
        }

        private void UpdateDebtText()
        {
            if (debtValue == null || debtRoll.Shown < 0f)
                return;
            debtValue.text = "Текущий долг: " + UiFormat.Currency + " " + UiFormat.FormatCredits(Mathf.RoundToInt(debtRoll.Shown));
            debtValue.color = debtRising && debtRoll.Target > debtRoll.Shown + 0.5f
                ? OnboardComputerFactory.DangerColor
                : OnboardComputerFactory.DebtCalmColor;
        }

        // N = min(долг, кошелёк) — сколько реально спишется сейчас;
        // без денег кнопка гаснет и подпись тускнеет
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

        // досрочное гашение: списывает сколько есть, остаток остаётся долгом;
        // полный расчёт — тост Success, частичное — Danger (деньги ушли)
        private void RepayDebtNow()
        {
            if (debt == null || wallet == null || debt.Balance <= 0)
                return;
            int amount = Mathf.Min(debt.Balance, wallet.Credits);
            if (amount <= 0 || !wallet.TrySpend(amount))
                return;
            debt.Repay(amount);
            bool full = debt.Balance <= 0;
            Sound.Play(SoundId.DebtTone); // тон расчёта по долгу
            FindAnyObjectByType<ToastNotifier>()?.Show(
                full
                    ? "Долг погашен"
                    : $"Долг уменьшен: −{UiFormat.Currency} {UiFormat.FormatCredits(amount)} · осталось {UiFormat.Currency} {UiFormat.FormatCredits(debt.Balance)}",
                full, full ? ToastTone.Success : ToastTone.Danger);
        }
    }
}
