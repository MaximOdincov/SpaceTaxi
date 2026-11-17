using UnityEngine; // векторы

namespace SpaceTaxi.World
{
    public class Star : CelestialBody // звезда: жар вокруг ядра
    {
        [Tooltip("Радиус тепловой зоны вокруг звезды (область повреждения).")]
        public float HeatRadius = 180f; // радиус зоны жара

        [Tooltip("Урон корпуса в секунду у поверхности (на краю зоны — 0).")]
        public float HeatDamagePerSecond = 30f; // урон у поверхности

        public override void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig, bool fuelOutage = false) // инициализация
        {
            base.Init(data, physicsConfig, fuelOutage); // базовое тело
            HeatRadius = data.HeatRadius; // радиус жара из данных
        }

        public bool IsInHeatZone(Vector2 point) // точка в зоне жара
        {
            return Vector2.Distance(point, transform.position) <= HeatRadius; // дистанция до звезды
        }

        // Н10.11: единая формула глубины жара (0 на краю зоны → 1 у поверхности).
        // Прежде дублировалась в HullSystem (без пола) и HeatAudio (спан с
        // полом 1) — спан с полом 1 защищает от деления на ноль при
        // HeatRadius == Radius; 0 вне зоны — вызывающие не проверяют радиус
        public float HeatDepth(Vector2 point)
        {
            float d = Vector2.Distance(transform.position, point);
            if (d >= HeatRadius)
                return 0f;
            float span = Mathf.Max(HeatRadius - Radius, 1f);
            return Mathf.Clamp01(1f - (d - Radius) / span);
        }
    }
}
