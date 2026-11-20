using System;

namespace SpaceTaxi.Economy
{
    // Долги за разбитые арендные корабли: баланс и увольнение при долге
    // свыше порога. Живёт в GameSession (создаётся в Awake рядом с
    // Wallet/Fleet). Кошельком не управляет: платит вызывающий через
    // Wallet.TrySpend.
    public sealed class DebtService
    {
        // разбил арендный корабль — долг +400 Đ
        private const int AccidentDebt = 400;

        // долг свыше порога — увольнение. Число живёт в PricingConfig
        // (DebtDismissalThreshold, GameSession конфигурирует при старте) —
        // здесь только дефолт
        private const int DefaultDismissalDebtThreshold = 2000;

        private int balance;

        private int dismissalDebtThreshold = DefaultDismissalDebtThreshold;

        public int Balance => balance;


        // порог долга, после которого забег терминален (конфигурируется из PricingConfig)
        public int DismissalDebtThreshold => dismissalDebtThreshold;

        // долг превысил порог увольнения (проверка после каждого роста баланса)
        public bool IsOverDismissalThreshold => balance > dismissalDebtThreshold;

        public event Action<int> DebtChanged;
        public event Action Dismissal;

        // авария аренды отдельно от DebtChanged: тост «за арендный
        // корабль» вешается только на это событие; рост через AddCharge
        // (недостача штрафа) тоста не даёт — штраф уже тостуется вызывающим
        public event Action<int> AccidentRegistered;

        // порог из прайс-конфига: числа баланса — в одном месте;
        // кривое значение (0/отрицательное) порог не меняет
        public void ConfigureDismissalDebtThreshold(int threshold)
        {
            if (threshold <= 0)
                return;
            dismissalDebtThreshold = threshold;
        }

        // долг за потерю арендного корабля; «арендный ли» решает вызывающий.
        // Проигрыш — только по порогу долга, авария просто +400 Đ долга
        public void RegisterAccident()
        {
            balance += AccidentDebt;
            NotifyBalanceChanged();
            AccidentRegistered?.Invoke(AccidentDebt); // после DebtChanged: UI обновится раньше тоста
        }

        // разовый штраф (отмена заказа оператором и др.):
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
        }

        // единая точка после роста баланса: событие + проверка увольнения
        // (после КАЖДОГО изменения долга; Repay только снижает
        // и проверки не требует)
        private void NotifyBalanceChanged()
        {
            DebtChanged?.Invoke(balance);
            if (balance > dismissalDebtThreshold) // единственное условие увольнения
                Dismissal?.Invoke();
        }

        // восстановление из сейва: только баланс. Dismissal на загрузке не
        // файрим: терминальное состояние сейвом не сохраняется — Save его
        // пропускает
        public void Restore(int restoredBalance)
        {
            int clamped = Math.Max(0, restoredBalance);
            if (clamped == balance)
                return; // как Wallet.Restore: без события — иначе ложный тон долга при загрузке
            balance = clamped;
            DebtChanged?.Invoke(balance);
        }
    }
}
