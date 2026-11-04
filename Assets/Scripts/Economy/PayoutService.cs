using System;

namespace SpaceTaxi.Economy
{
    // Итог выплаты за доставку (16_BETA_PLAN §3.5). Tips всегда 0 —
    // П8.2 подставит чаевые отдельным параметром/методом по факту
    public sealed class PayoutResult
    {
        public int Price;      // цена заказа с карточки
        public int Penalty;    // штраф за столкновения
        public int Commission; // комиссия аренды
        public int Tips;       // чаевые (П8.2)
        public int ToDebt;     // автоматически ушло в счёт долга
        public int Net;        // на руки
    }

    // Выплата за доставку: штрафы/комиссия/долг (§3.5). Владелец — GameSession
    // (создаётся в Awake); Wallet и DebtService приходят Bind-ом, без поиска по сцене
    public sealed class PayoutService
    {
        private Wallet wallet;
        private DebtService debt;

        // выплата состоялась — для UI (тосты суммы и ухода в долг)
        public event Action<PayoutResult> Paid;

        public void Bind(Wallet wallet, DebtService debt)
        {
            this.wallet = wallet;
            this.debt = debt;
        }

        // Порядок: штраф (кап −45%) → комиссия аренды → доля в долг → на руки.
        // Wallet.Add(Net) и debt.Repay(ToDebt) — порядок не важен: Repay кошелёк не трогает,
        // доля долга уже вычтена из Net
        public PayoutResult Pay(PricingConfig pricing, int price, bool rentalShip, int collisions)
        {
            if (pricing == null)
                pricing = PricingConfig.Default;
            if (price < 0)
                price = 0;

            int counted = Math.Min(Math.Max(collisions, 0), 3);
            double penaltyRaw = (double)price * counted * pricing.PenaltyPerCollisionPct / 100.0;
            penaltyRaw = Math.Min(penaltyRaw, (double)price * pricing.PenaltyCapPct / 100.0);
            int penalty = (int)Math.Round(penaltyRaw, MidpointRounding.AwayFromZero);

            double commissionRaw = rentalShip
                ? (double)(price - penalty) * pricing.RentalCommissionPct / 100.0
                : 0.0;
            int commission = (int)Math.Round(commissionRaw, MidpointRounding.AwayFromZero);

            int gross = price - penalty - commission;

            int toDebt = 0;
            if (debt != null && debt.Balance > 0 && gross > 0)
            {
                double share = Math.Round((double)gross * pricing.DebtRepaySharePct / 100.0,
                    MidpointRounding.AwayFromZero);
                toDebt = (int)Math.Min(share, debt.Balance);
            }

            var result = new PayoutResult
            {
                Price = price,
                Penalty = penalty,
                Commission = commission,
                Tips = 0,
                ToDebt = toDebt,
                Net = gross - toDebt,
            };

            wallet?.Add(result.Net);
            debt?.Repay(result.ToDebt);
            Paid?.Invoke(result);
            return result;
        }
    }
}
