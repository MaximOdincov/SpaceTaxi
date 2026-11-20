using System;

namespace SpaceTaxi.Economy
{
    // Рейтинг таксиста: шкала 0.0–5.0, старт новой игры 3.0.
    // Дельты копятся точно (+0.15 за доставку не ложится на сетку 0.1 — шаг 0.1
    // это гранулярность отображения), кламп [0; 5]. Живёт в GameSession;
    // UI подписывается на RatingChanged, борткомп выводит одну десятую.
    public sealed class RatingService
    {
        private const float StartValue = 3.0f;

        // дельты — в одном месте, чтобы не расходиться с таблицей
        private const float DeliveryBonus = 0.15f;
        private const float CollisionPenalty = 0.10f;
        private const float TaskBonus = 0.10f;
        private const float TaskFailPenalty = 0.10f; // жалоба пассажира на задание
        private const float FailNoDeathPenalty = 0.30f;
        private const float FailWithLossPenalty = 0.50f;

        public float Value { get; private set; } = StartValue;

        // дельта последнего изменения (после клампа) — строка «Рейтинг +0.15»
        // на экране завершения заказа
        public float LastDelta { get; private set; }

        public event Action<float> RatingChanged;

        // довёз — фикс бонус доставки
        public void AddDelivery() => Move(DeliveryBonus);

        // столкновение с повреждением — штраф рейтинга
        public void AddCollisionPenalty() => Move(-CollisionPenalty);

        // задание пассажира: успех при доставке / жалоба при провале задания
        public void TaskCompleted() => Move(TaskBonus);

        // жалоба пассажира — провал задания
        public void TaskFailed() => Move(-TaskFailPenalty);

        // заказ сорван без гибели (отказ/высадка не туда)
        public void FailNoDeath() => Move(-FailNoDeathPenalty);

        // заказ сорван гибелью корабля
        public void FailWithLoss() => Move(-FailWithLossPenalty);

        // восстановление из сейва: прямое присвоение — дельты не суммируются,
        // LastDelta сбрасывается (на экране завершения показывать нечего)
        public void Restore(float restored)
        {
            Value = Math.Clamp(restored, 0f, 5f);
            LastDelta = 0f;
            RatingChanged?.Invoke(Value);
        }

        // единственная точка изменения рейтинга: кламп 0..5 + уведомление подписчиков
        private void Move(float delta)
        {
            float before = Value;
            Value = Math.Clamp(before + delta, 0f, 5f);
            LastDelta = Value - before;
            RatingChanged?.Invoke(Value);
        }
    }
}
