using UnityEngine;
using SpaceTaxi.Physics;

namespace SpaceTaxi.Economy
{
    // Эффективные параметры корабля с учётом улучшений: единственный источник
    // чисел для полёта (ShipController), корпуса (HullSystem) и максимумов
    public struct EffectiveStats
    {
        public float Thrust;       // тяга двигателя
        public float Strafe;       // боковая тяга
        public float FuelMax;      // ёмкость бака
        public float HpMax;        // прочность корпуса
        public float FuelDrain;    // расход топлива в секунду
        public float GravityBoost; // буст качели на входе в кольцо захвата
        public float HeatMul;      // множитель урона от жара (1 = без защиты)
        public float Retro;        // торможение (модельный, иначе глобальный)
    }

    // Уровни улучшений -> статы. Чистая функция без состояния: ур.1 = 1/3,
    // ур.2 = 2/3, ур.3 = максимум; нулевые уровни = базовые статы.
    // MaxV улучшениями не меняется (16_BETA_PLAN §3.4)
    public static class ShipStats
    {
        public const int LineCount = 4; // Engine, Tank, Hull, Maneuver
        public const int MaxLevel = 3;

        public static EffectiveStats Compute(PhysicsConfig physics, ShipConfig ship, int[] levels, UpgradeConfig upgrades)
        {
            var stats = new EffectiveStats
            {
                Thrust = ship != null ? ship.Thrust : 0f,
                Strafe = ship != null ? ship.StrafeAcc : 0f,
                FuelMax = ship != null ? ship.FuelMax : 0f,
                HpMax = ship != null ? ship.HpMax : 0f,
                FuelDrain = physics != null ? physics.FuelDrainPerSecond : 0f,
                GravityBoost = physics != null ? physics.GravityBoost : 1f,
                HeatMul = 1f,
                Retro = ship != null && ship.Retro > 0f
                    ? ship.Retro
                    : physics != null ? physics.Retro : 80f,
            };
            if (ship == null || physics == null || upgrades == null)
                return stats; // без конфига улучшений — сток

            int[] norm = NormalizeLevels(levels);
            float engine = LevelFraction(norm[(int)UpgradeLine.Engine]);
            float tank = LevelFraction(norm[(int)UpgradeLine.Tank]);
            float hull = LevelFraction(norm[(int)UpgradeLine.Hull]);
            float maneuver = LevelFraction(norm[(int)UpgradeLine.Maneuver]);

            stats.Thrust *= 1f + upgrades.EngineThrustPct / 100f * engine;
            stats.GravityBoost = Mathf.Lerp(stats.GravityBoost, upgrades.EngineBoostMax, engine);
            stats.FuelMax *= 1f + upgrades.TankCapacityPct / 100f * tank;
            stats.FuelDrain *= 1f - upgrades.TankDrainRedPct / 100f * tank;
            stats.HpMax *= 1f + upgrades.HullHpPct / 100f * hull;
            stats.HeatMul = 1f - upgrades.HullHeatRedPct / 100f * hull;
            stats.Strafe *= 1f + upgrades.ManeuverStrafePct / 100f * maneuver;
            return stats;
        }

        // Массив всегда длиной 4, значения 0..3; null/короткий/лишний — обрезка и нули
        public static int[] NormalizeLevels(int[] levels)
        {
            var norm = new int[LineCount];
            if (levels == null)
                return norm;
            for (int i = 0; i < levels.Length && i < LineCount; i++)
                norm[i] = Mathf.Clamp(levels[i], 0, MaxLevel);
            return norm;
        }

        private static float LevelFraction(int level)
        {
            return Mathf.Clamp01(level / (float)MaxLevel);
        }
    }
}
