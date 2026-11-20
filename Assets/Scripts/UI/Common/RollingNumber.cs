using UnityEngine;

namespace SpaceTaxi.UI
{
    // Плавная докатка числа к цели (анимации денег/долга/рейтинга борткомпа).
    // unscaled-время — работает и на паузе; скорость растёт с дельтой,
    // snap — порог прилипания к цели
    public struct RollingNumber
    {
        public float Shown;  // текущее показанное; −1 — сентинел «не стартовало»
        public float Target; // цель докатки

        // первичный показ без прокатки
        public void Start(float value)
        {
            Shown = value;
            Target = value;
        }

        // один кадр докатки; true — Shown изменилось, вызывающий перечитает текст
        public bool Step(float minSpeed, float deltaK, float snap = 0.5f)
        {
            if (Shown < 0f || Mathf.Approximately(Shown, Target))
                return false;
            float speed = Mathf.Max(minSpeed, Mathf.Abs(Target - Shown) * deltaK);
            Shown = Mathf.MoveTowards(Shown, Target, speed * Time.unscaledDeltaTime);
            if (Mathf.Abs(Target - Shown) < snap)
                Shown = Target;
            return true;
        }
    }
}
