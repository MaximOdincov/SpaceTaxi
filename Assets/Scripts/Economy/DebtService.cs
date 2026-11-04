using System;

namespace SpaceTaxi.Economy
{
    // Долги за разбитые арендные корабли: баланс, счётчик аварий, увольнение.
    // Владелец — GameSession (создаётся в Awake рядом с Wallet/Fleet).
    // Кошельком не управляет: платит вызывающий через Wallet.TrySpend.
    // Авто-гашение 50% с выплат (П4.2) и кнопка досрочного гашения (П3.3)
    // подключатся поверх этого API позже.
    public sealed class DebtService
    {
        // 16_BETA_PLAN §3.1: разбил арендный корабль — долг +400 кр
        private const int AccidentDebt = 400;
        // §3.2: три аварии с непогашенными долгами — увольнение
        private const int DismissalAfterAccidents = 3;

        private int balance;
        private int unpaidAccidents;

        public int Balance => balance;

        public int UnpaidAccidents => unpaidAccidents;

        public event Action<int> DebtChanged;
        public event Action Dismissal;

        // долг за потерю арендного корабля; «арендный ли» решает вызывающий
        public void RegisterAccident()
        {
            balance += AccidentDebt;
            unpaidAccidents++;
            DebtChanged?.Invoke(balance);
            if (unpaidAccidents >= DismissalAfterAccidents)
                Dismissal?.Invoke();
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
    }
}
