using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    [CreateAssetMenu(fileName = "PhysicsConfig", menuName = "Space Taxi/Physics Config")]
    public sealed class PhysicsConfig : ScriptableObject
    {
        [Header("Скорость")]
        public float Retro = 62f;             // торможение: мягче, не гасит скорость в ноль
        [Tooltip("Доля локальной гравитации, которую тормоз не может погасить: у падающего корабля есть мин. скорость падения — зависнуть в воздухе нельзя. В пустоте тормоз честный (можно остановиться). 0 — старое поведение без пола")]
        public float BrakeHoverFraction = 0.2f;
        [Tooltip("Не используется тормозом с Н4.11 (см. BrakeHoverFraction); оставлен как вход баланса (tools/balance_n4)")]
        public float MinSpeed = 10f;
        [Tooltip("Предел угловой скорости разворота")]
        public float StrafeMaxTurnRate = 60f; // град/с

        [Header("Посадка")]
        public float LandSpeed = 75f;         // безопасная скорость касания (посадка прощает больше)
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

        [Header("Чёрная дыра (Н7 G7.1)")]
        [Tooltip("Воронка: притяжение на краю зоны эффекта, ед/с²")]
        public float BlackHolePullEdge = 60f;
        [Tooltip("Воронка: притяжение у горизонта, ед/с² — глубже кольца вырваться не может никто")]
        public float BlackHolePullHorizon = 600f;
        [Tooltip("Порог вырывания: корабль не считается застрявшим, если его тяга выше притяжения × это значение. Стоковый Барин (140) > притяжение на кольце (~119) × 1.15 — вырывается; Игла (135) — уже нет")]
        public float BlackHoleEscapeThrustFraction = 1.15f;
        [Tooltip("Удержание «застрял»: скорость гашения радиального ВЫЛЕТА на кольце захвата, ед/с² (должно быть больше тяги корабля)")]
        public float BlackHoleRadialHoldEdge = 260f;
        [Tooltip("Удержание у горизонта, ед/с²")]
        public float BlackHoleRadialHoldHorizon = 620f;
        [Tooltip("Закрутка орбиты на кольце захвата, ед/с² — скорость вращения по орбите растёт")]
        public float BlackHoleSwirlEdge = 45f;
        [Tooltip("Закрутка у горизонта, ед/с²")]
        public float BlackHoleSwirlHorizon = 130f;
        [Tooltip("Потолок орбитальной скорости застрявшего корабля = фактор × √(притяжение × дистанция). Ниже центробежного баланса — корабль вязко спирали внутрь; вырывание через разгон орбиты остаётся у кораблей с достаточной тягой")]
        public float BlackHoleOrbitSpeedFactor = 0.95f;
        [Tooltip("Скорость стягивания орбитальной скорости к потолку, ед/с")]
        public float BlackHoleOrbitClampRate = 240f;
        [Tooltip("Предел скорости падения в захвате, ед/с — спираль тянется, а не обрывается")]
        public float BlackHoleSinkSpeedMax = 12f;
        [Tooltip("Как быстро скорость падения приводится к пределу, ед/с² — должно успевать гасить таран на максимальной скорости до порога приближения")]
        public float BlackHoleSinkClampRate = 900f;
        [Tooltip("Базовое время сжатия корабля-спагетти от 1 до порога, с; у горизонта сжатие ускоряется")]
        public float BlackHoleShrinkSeconds = 16f;
        [Tooltip("Порог гибели по размеру: масштаб корабля, при котором корпус гибнет")]
        public float BlackHoleMinScale = 0.25f;
        [Tooltip("Порог гибели по приближению: расстояние НАД горизонтом, ближе которого корпус гибнет")]
        public float BlackHoleDeathProximity = 45f;

        public float GravParamOf(CelestialBody body)
        {
            // дыра — исключение: её настоящее притяжение — воронка в BlackHole.
            // В общем поле оставляем слабый след (прогноз траектории, качели,
            // тормоз), иначе кламп 240 делал бы вырывание невозможным для любого корпуса
            float fieldScale = body is BlackHole ? BlackHole.FieldGravityScale : 1f;
            return GravityParamK * body.Radius * body.Mass * fieldScale;
        }

        public float OrbitRingOf(CelestialBody body)
        {
            return body.Radius * CaptureRingK + CaptureRingBase;
        }

        public static float CircularVelocity(float g, float d) => Mathf.Sqrt(g / d);

        public static float EscapeVelocity(float g, float d) => Mathf.Sqrt(2f * g / d);
    }
}
