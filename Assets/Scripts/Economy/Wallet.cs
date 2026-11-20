using System;
using SpaceTaxi.Audio;

namespace SpaceTaxi.Economy
{
    // Баланс кредитов смены: старт 0, уйти в минус невозможно
    public sealed class Wallet
    {
        public event Action<int> CreditsChanged;

        public int Credits { get; private set; }

        // заработок за доставку/штрафы извне; отрицательные суммы не проходят (через Spend)
        public void Add(int amount)
        {
            if (amount <= 0)
                return; // пополнение только в плюс: списания — через TrySpend
            Credits += amount;
            CreditsChanged?.Invoke(Credits);
        }

        // покупка/штраф: атомарно — либо списано, либо false; кредиты не уходят в минус
        public bool TrySpend(int amount)
        {
            if (amount <= 0 || amount > Credits)
                return false;
            Credits -= amount;
            CreditsChanged?.Invoke(Credits);
            // звон монет на любое успешное списание: заправка, ремонт,
            // покупка, улучшение, штраф за отмену — точка одна, здесь
            Sound.Play(SoundId.MoneySpend);
            return true;
        }

        // восстановление из сейва: прямой присёт баланса — Add только
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
