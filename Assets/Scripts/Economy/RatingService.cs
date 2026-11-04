using System;

namespace SpaceTaxi.Economy
{
    // Рейтинг таксиста (16_BETA_PLAN §3.7): шкала 0.0–5.0, старт новой игры 3.0.
    // Дельты копятся точно (+0.15 за доставку не ложится на сетку 0.1 — шаг 0.1
    // это гранулярность отображения), кламп [0; 5]. Владелец — GameSession;
    // UI подписывается на RatingChanged, борткомп выводит одну десятую.
    public sealed class RatingService
    {
        private const float StartValue = 3.0f;

        // дельты §3.7 — в одном месте, чтобы не расходиться с таблицей
        private const float DeliveryBonus = 0.15f;
        private const float CollisionPenalty = 0.10f;
        private const float TaskBonus = 0.10f;
        private const float TaskFailPenalty = 0.10f; // жалоба пассажира на задание (§3.7, решение координатора)
        private const float FailNoDeathPenalty = 0.30f;
        private const float FailWithLossPenalty = 0.50f;

        public float Value { get; private set; } = StartValue;

        // дельта последнего изменения (после клампа) — строка «Рейтинг +0.15»
        // на экране завершения заказа
        public float LastDelta { get; private set; }

        public event Action<float> RatingChanged;

        public void AddDelivery() => Move(DeliveryBonus);

        public void AddCollisionPenalty() => Move(-CollisionPenalty);

        // задание пассажира (П5.3): успех при доставке / жалоба при провале задания
        public void TaskCompleted() => Move(TaskBonus);

        public void TaskFailed() => Move(-TaskFailPenalty);

        public void FailNoDeath() => Move(-FailNoDeathPenalty);

        public void FailWithLoss() => Move(-FailWithLossPenalty);

        private void Move(float delta)
        {
            float before = Value;
            Value = Math.Clamp(before + delta, 0f, 5f);
            LastDelta = Value - before;
            RatingChanged?.Invoke(Value);
        }
    }
}
