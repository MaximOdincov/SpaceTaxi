using UnityEngine; // ScriptableObject
using SpaceTaxi.World; // тела и дыра

namespace SpaceTaxi.Physics
{
    [CreateAssetMenu(fileName = "PhysicsConfig", menuName = "Space Taxi/Physics Config")] // ассет-меню
    public sealed class PhysicsConfig : ScriptableObject // весь физический баланс в одном ассете
    {
        [Header("Скорость")]
        public float Retro = 62f;             // торможение: мягче, не гасит скорость в ноль
        [Tooltip("Доля локальной гравитации, которую тормоз не может погасить: у падающего корабля есть мин. скорость падения — зависнуть в воздухе нельзя. В пустоте тормоз честный (можно остановиться). 0 — старое поведение без пола")]
        public float BrakeHoverFraction = 0.2f; // «пол» тормоза у планеты
        [Tooltip("Не используется тормозом с Н4.11 (см. BrakeHoverFraction); оставлен как вход баланса (tools/balance_n4)")]
        public float MinSpeed = 10f; // устаревший вход баланса
        [Tooltip("Предел угловой скорости разворота")]
        public float StrafeMaxTurnRate = 60f; // град/с

        [Header("Посадка")]
        public float LandSpeed = 75f;         // безопасная скорость касания (посадка прощает больше)
        public float TakeoffV = 85f;          // импульс взлёта от центра планеты
        public float EngineMultiplier = 3.2f; // множитель тяги в посадочном кольце
        public float LandingRingOffset = 90f; // кольцо: поверхность + это расстояние

        [Header("Бак")]
        public float FuelDrainPerSecond = 5.5f; // расход топлива в секунду

        [Header("Гравитация")]
        public float GravityParamK = 52000f;  // гравитационный параметр: g = K * r * масса
        public float GravityAccelClamp = 240f;// предел ускорения от одного тела

        [Header("Кольцо захвата")]
        public float CaptureRingK = 3.3f;     // ring = r * K + Base
        public float CaptureRingBase = 30f;   // база кольца

        [Header("Нос")]
        public float HeadingTurnRate = 4f;    // доворот носа к скорости, 1/с
        public float HeadingMaxTurnSpeed = 260f; // предел угловой скорости доворота, град/с
        public float MinControlSpeed = 1f;    // ниже — управление не работает

        [Header("Качель")]
        public float GravityBoost = 1.15f;    // буст на входе в кольцо при v > vEsc

        [Header("Чёрная дыра (Н7 G7.1; §1.6 — градиент риска)")]
        [Tooltip("Воронка: притяжение на краю зоны эффекта, ед/с²")]
        public float BlackHolePullEdge = 70f; // притяжение на краю воронки
        [Tooltip("Воронка: притяжение у горизонта, ед/с² — глубже кольца вырваться не может никто")]
        public float BlackHolePullHorizon = 420f; // притяжение у горизонта
        [Tooltip("Порог «застрял» (v5.1 — без гасилки скорости, чистое силовое сравнение): корабль застрял, если его тяга ≤ притяжения × это значение; тогда орбитальная скорость вязко стягивается ниже центробежного баланса. Стоковый Барин (140): обречён глубже depth≈0.45, на малой глубине ещё вырывается; слабые — сразу")]
        public float BlackHoleEscapeThrustFraction = 1.15f; // порог силового сравнения
        [Tooltip("Закрутка орбиты на кольце захвата, ед/с² — скорость вращения по орбите растёт")]
        public float BlackHoleSwirlEdge = 80f; // закрутка на краю
        [Tooltip("Закрутка у горизонта, ед/с²")]
        public float BlackHoleSwirlHorizon = 220f; // закрутка у горизонта
        [Tooltip("Потолок орбитальной скорости застрявшего корабля = фактор × √(притяжение × дистанция). Ниже центробежного баланса — корабль вязко спирали внутрь; вырывание через разгон орбиты остаётся у кораблей с достаточной тягой")]
        public float BlackHoleOrbitSpeedFactor = 0.95f; // фактор орбитального потолка
        [Tooltip("Скорость стягивания орбитальной скорости к потолку, ед/с")]
        public float BlackHoleOrbitClampRate = 240f; // скорость стягивания орбиты
        [Tooltip("Предел скорости падения в захвате, ед/с — спираль тянется, а не обрывается")]
        public float BlackHoleSinkSpeedMax = 16f; // потолок скорости падения
        [Tooltip("Как быстро скорость падения приводится к пределу, ед/с² — должно успевать гасить таран на максимальной скорости до порога приближения")]
        public float BlackHoleSinkClampRate = 900f; // скорость приведения падения
        [Tooltip("Окно поглощения (v5, владелец: ~45 реальных секунд): корабль крутит и сжимает — быстро в начале, потом всё слабее (ease-out); в конце окна «пропадает связь», борт отключается сам, оператор спавнится на новом")]
        public float BlackHoleShrinkSeconds = 45f; // длина окна поглощения
        [Tooltip("Конечный масштаб сжатия: корабль исчезает, когда сжатие доходит до этой доли (полное поглощение)")]
        public float BlackHoleMinScale = 0.25f; // минимальный масштаб корабля
        [Tooltip("Сколько секунд обломок потерянного борта остаётся у дыры (Н9.11, владелец: «корабль должен оставаться в дыре — в этом прикол»): медленно спиралит внутрь и исчезает; раньше = окно 45с, возврат новым кораблём не успевал")]
        public float BlackHoleWreckSeconds = 120f; // сколько секунд обломок тонет у дыры
        [Tooltip("НЕ ИСПОЛЬЗУЕТСЯ с v4 (дыра не убивает по приближению) — оставлено для схемы ассета")]
        public float BlackHoleDeathProximity = 45f; // мёртвое поле для совместимости ассета

        public float GravParamOf(CelestialBody body) // гравитационный параметр тела
        {
            // дыра — исключение: её настоящее притяжение — воронка в BlackHole.
            // В общем поле оставляем слабый след (прогноз траектории, качели,
            // тормоз), иначе кламп 240 делал бы вырывание невозможным для любого корпуса
            float fieldScale = body is BlackHole ? BlackHole.FieldGravityScale : 1f; // дыра ослаблена в общем поле
            return GravityParamK * body.Radius * body.Mass * fieldScale; // K × радиус × масса
        }

        public float OrbitRingOf(CelestialBody body) // кольцо захвата тела
        {
            return body.Radius * CaptureRingK + CaptureRingBase; // формула кольца
        }

        public static float CircularVelocity(float g, float d) => Mathf.Sqrt(g / d); // круговая скорость

        public static float EscapeVelocity(float g, float d) => Mathf.Sqrt(2f * g / d); // скорость убегания
    }
}
