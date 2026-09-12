using UnityEngine;

namespace SpaceTaxi.Physics
{
    // Значения по умолчанию — из HTML-прототипа v3 (01_MECHANICS.md §1).
    [CreateAssetMenu(fileName = "PhysicsConfig", menuName = "Space Taxi/Physics Config")]
    public sealed class PhysicsConfig : ScriptableObject
    {
        [Header("Тяга")]
        public float Thrust = 95f;            // W — разгон по курсу
        public float Retro = 115f;            // S — тормоз
        public float StrafeAcc = 85f;         // A/D — боковая тяга
        public float MaxV = 330f;             // предел скорости

        [Header("Посадка")]
        public float LandSpeed = 38f;         // безопасная скорость касания
        public float TakeoffV = 85f;          // импульс взлёта от центра планеты
        public float EngineMultiplier = 3.2f; // множитель тяги в посадочном кольце
        public float LandingRingOffset = 90f; // кольцо: поверхность + это расстояние

        [Header("Бак и корпус")]
        public float FuelMax = 100f;
        public float FuelDrainPerSecond = 5.5f;
        public float HpMax = 100f;

        [Header("Гравитация")]
        public float GravityConstant = 1f; // калибруется вместе с массами тел
        public float GravityBoost = 1.15f; // буст тяги рядом с массивным телом
    }
}
