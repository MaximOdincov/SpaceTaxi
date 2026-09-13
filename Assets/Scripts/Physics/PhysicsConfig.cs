using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    // Значения по умолчанию — из HTML-прототипа v3 (01_MECHANICS.md §1).
    [CreateAssetMenu(fileName = "PhysicsConfig", menuName = "Space Taxi/Physics Config")]
    public sealed class PhysicsConfig : ScriptableObject
    {
        [Header("Тяга")]
        public float Thrust = 95f;            // W — разгон вдоль вектора скорости
        public float Retro = 115f;            // S — торможение
        public float StrafeAcc = 85f;         // A/D — боковая тяга (перпендикуляр скорости)
        public float MaxV = 330f;             // выше — тяга не работает (не жёсткий кламп)

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
        public float GravityParamK = 26000f;  // гравитационный параметр: g = K * r * масса
        public float GravityAccelClamp = 240f;// предел ускорения от одного тела

        [Header("Кольцо захвата")]
        public float CaptureRingK = 3.3f;     // ring = r * K + Base
        public float CaptureRingBase = 30f;

        [Header("Автопилот и нос")]
        public float AutopilotRate = 8f;      // лерп скорости к круговой, 1/с
        public float HeadingTurnRate = 8f;    // доворот носа к скорости, 1/с
        public float MinControlSpeed = 1f;    // ниже — управление и автопилот не работают

        [Header("Качель")]
        public float GravityBoost = 1.15f;    // буст на входе в кольцо при v > vEsc

        // Гравитационный параметр тела (масса уже внутри).
        public float GravParamOf(CelestialBody body)
        {
            return GravityParamK * body.Radius * body.Mass;
        }

        // Радиус кольца захвата тела.
        public float OrbitRingOf(CelestialBody body)
        {
            return body.Radius * CaptureRingK + CaptureRingBase;
        }

        // Круговая скорость на дистанции d.
        public static float CircularVelocity(float g, float d) => Mathf.Sqrt(g / d);

        // Убегающая скорость на дистанции d.
        public static float EscapeVelocity(float g, float d) => Mathf.Sqrt(2f * g / d);
    }
}
