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

        // восстановление из сейва (П10.1): прямой присёт баланса — Add только
        // плюсует, TrySpend не умеет ставить произвольное значение
        public void Restore(int credits)
        {
            credits = Math.Max(0, credits);
            if (credits == Credits)
                return;
            Credits = credits;
            CreditsChanged?.Invoke(Credits);
        }
    }
}
