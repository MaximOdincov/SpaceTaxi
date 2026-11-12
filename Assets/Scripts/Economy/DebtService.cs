using System;

namespace SpaceTaxi.Economy
{
    // Долги за разбитые арендные корабли: баланс, счётчик аварий, увольнение
    // (три аварии или долг свыше порога — Н7 Q7.8). Владелец — GameSession
    // (создаётся в Awake рядом с Wallet/Fleet).
    // Кошельком не управляет: платит вызывающий через Wallet.TrySpend.
    // Авто-гашение 50% с выплат (П4.2) и кнопка досрочного гашения (П3.3)
    // подключатся поверх этого API позже.
    public sealed class DebtService
    {
        // 16_BETA_PLAN §3.1: разбил арендный корабль — долг +400 Đ
        private const int AccidentDebt = 400;
        // §3.2: три аварии с непогашенными долгами — увольнение
        private const int DismissalAfterAccidents = 3;
        // Н7 Q7.8: долг свыше порога — тоже увольнение. Число живёт в
        // PricingConfig (DebtDismissalThreshold, GameSession конфигурирует
        // при старте) — здесь только дефолт плана, пока конфигурации не было
        private const int DefaultDismissalDebtThreshold = 2000;

        private int balance;
        private int unpaidAccidents;
        private int dismissalDebtThreshold = DefaultDismissalDebtThreshold;

        public int Balance => balance;

        public int UnpaidAccidents => unpaidAccidents;

        // порог долга, после которого забег терминален (конфигурируется из PricingConfig)
        public int DismissalDebtThreshold => dismissalDebtThreshold;

        // долг превысил порог увольнения (проверка после каждого роста баланса)
        public bool IsOverDismissalThreshold => balance > dismissalDebtThreshold;

        public event Action<int> DebtChanged;
        public event Action Dismissal;

        // авария аренды отдельно от DebtChanged (Н7 A-FIX): тост «за арендный
        // корабль» вешается только на это событие; рост через AddCharge
        // (недостача штрафа) тоста не даёт — штраф уже тостуется вызывающим
        public event Action<int> AccidentRegistered;

        // порог из прайс-конфига (Н7 Q7.8): числа баланса — в одном месте;
        // кривое значение (0/отрицательное) порог не меняет
        public void ConfigureDismissalDebtThreshold(int threshold)
        {
            if (threshold <= 0)
                return;
            dismissalDebtThreshold = threshold;
        }

        // долг за потерю арендного корабля; «арендный ли» решает вызывающий
        public void RegisterAccident()
        {
            balance += AccidentDebt;
            unpaidAccidents++;
            NotifyBalanceChanged();
            AccidentRegistered?.Invoke(AccidentDebt); // после DebtChanged: UI обновится раньше тоста
        }

        // разовый штраф (отмена заказа оператором, Н7 Q7.5 — пассажир и др.):
        // баланс растёт, счётчик аварий не трогаем — авария тут ни при чём;
        // автогашение с выплат подхватит долг как обычный
        public void AddCharge(int amount)
        {
            if (amount <= 0)
                return;
            balance += amount;
            NotifyBalanceChanged();
        }

        // гашение: кламп до нуля (переплата кредитов не создаёт — платить
        // должен вызывающий), полный расчёт обнуляет счётчик аварий
        public void Repay(int amount)
        {
            if (amount <= 0 || balance <= 0)
                return;
            balance -= Math.Min(amount, balance);
            DebtChanged?.Invoke(balance);
            if (balance == 0)
                unpaidAccidents = 0;
        }

        // единая точка после роста баланса: событие + проверка увольнения
        // (Н7 Q7.8 — после КАЖДОГО изменения долга; Repay только снижает
        // и проверки не требует)
        private void NotifyBalanceChanged()
        {
            DebtChanged?.Invoke(balance);
            if (balance > dismissalDebtThreshold || unpaidAccidents >= DismissalAfterAccidents)
                Dismissal?.Invoke();
        }

        // восстановление из сейва (П10.1): баланс и счётчик аварий целиком.
        // Dismissal на загрузке не файрим: терминальное состояние (3 аварии)
        // сейвом не сохраняется — Save его пропускает
        public void Restore(int restoredBalance, int restoredAccidents)
        {
            int clamped = Math.Max(0, restoredBalance);
            if (clamped == balance && Math.Max(0, restoredAccidents) == unpaidAccidents)
                return; // как Wallet.Restore: без события — иначе ложный тон долга при загрузке
            balance = clamped;
            unpaidAccidents = Math.Max(0, restoredAccidents);
            DebtChanged?.Invoke(balance);
        }
    }
}
