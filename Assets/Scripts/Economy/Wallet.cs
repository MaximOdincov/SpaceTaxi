using System;

namespace SpaceTaxi.Economy
{
    // Баланс кредитов смены: старт 0, уйти в минус невозможно
    public sealed class Wallet
    {
        public event Action<int> CreditsChanged;

        public int Credits { get; private set; }

        public void Add(int amount)
        {
            if (amount <= 0)
                return; // пополнение только в плюс: списания — через TrySpend
            Credits += amount;
            CreditsChanged?.Invoke(Credits);
        }

        public bool TrySpend(int amount)
        {
            if (amount <= 0 || amount > Credits)
                return false;
            Credits -= amount;
            CreditsChanged?.Invoke(Credits);
            return true;
        }
    }
}
