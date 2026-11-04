using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    [CreateAssetMenu(fileName = "PhysicsConfig", menuName = "Space Taxi/Physics Config")]
    public sealed class PhysicsConfig : ScriptableObject
    {
        [Header("Скорость")]
        public float Retro = 80f;             // торможение (было 115 — слишком резко)
        public float MaxV = 330f;             // максимальная скорость
        [Tooltip("Предел угловой скорости разворота")]
        public float StrafeMaxTurnRate = 60f; // град/с

        [Header("Посадка")]
        public float LandSpeed = 55f;         // безопасная скорость касания (было 38)
        public float TakeoffV = 85f;          // импульс взлёта от центра планеты
        public float EngineMultiplier = 3.2f; // множитель тяги в посадочном кольце
        public float LandingRingOffset = 90f; // кольцо: поверхность + это расстояние

        [Header("Бак")]
        public float FuelDrainPerSecond = 5.5f;

        [Header("Гравитация")]
        public float GravityParamK = 52000f;  // гравитационный параметр: g = K * r * масса
        public float GravityAccelClamp = 240f;// предел ускорения от одного тела

        [Header("Кольцо захвата")]
        public float CaptureRingK = 3.3f;     // ring = r * K + Base
        public float CaptureRingBase = 30f;

        [Header("Нос")]
        public float HeadingTurnRate = 4f;    // доворот носа к скорости, 1/с
        public float HeadingMaxTurnSpeed = 260f; // предел угловой скорости доворота, град/с
        public float MinControlSpeed = 1f;    // ниже — управление не работает

        [Header("Качель")]
        public float GravityBoost = 1.15f;    // буст на входе в кольцо при v > vEsc

        public float GravParamOf(CelestialBody body)
        {
            return GravityParamK * body.Radius * body.Mass;
        }

        public float OrbitRingOf(CelestialBody body)
        {
            return body.Radius * CaptureRingK + CaptureRingBase;
        }

        public static float CircularVelocity(float g, float d) => Mathf.Sqrt(g / d);

        public static float EscapeVelocity(float g, float d) => Mathf.Sqrt(2f * g / d);
    }
}
