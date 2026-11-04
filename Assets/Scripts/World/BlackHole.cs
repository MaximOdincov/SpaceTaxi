using UnityEngine;
using SpaceTaxi.Physics;

namespace SpaceTaxi.World
{
    // Чёрная дыра: гравитация ×8, горизонт = мгновенная гибель,
    // в зоне эффекта корабль медленно сжимается (1→0) и погибает
    public class BlackHole : CelestialBody
    {
        // дыра всегда ×8: если в данных множитель меньше — форсим (решение координатора)
        public const float HoleMassMultiplier = 8f;
        public const float EffectRadiusMultiplier = 6f;

        private const float ShrinkDuration = 4f;  // 1→0 внутри зоны эффекта
        private const float RestoreDuration = 1f; // выход из зоны — восстановление быстрее

        public static readonly Color CoreColor = new Color(0.06f, 0.05f, 0.1f);
        public static readonly Color ZoneColor = new Color(0.45f, 0.25f, 0.7f, 0.1f);

        // горизонт = радиус тела, зона эффекта — вокруг него
        public float Horizon => Radius;
        public float EffectRadius => Radius * EffectRadiusMultiplier;

        private Ship ship;
        private HullSystem hull;
        private Vector3 shipScale = Vector3.one;
        private float shrink; // 0 — нормальный корабль, 1 — достигнут горизонт по сжатию

        public override void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig)
        {
            base.Init(data, physicsConfig);
            Mass = Mathf.Max(data.MassMultiplier, HoleMassMultiplier);
        }

        private void Update()
        {
            if (!LazyBindShip())
                return;

            float d = Vector2.Distance(ship.transform.position, transform.position);

            // горизонт событий — гибель независимо от состояния
            if (d <= Horizon)
            {
                Kill();
                return;
            }

            if (d <= EffectRadius && ship.State == ShipState.Flying)
            {
                shrink = Mathf.MoveTowards(shrink, 1f, Time.deltaTime / ShrinkDuration);
                if (shrink >= 1f)
                {
                    Kill();
                    return;
                }
            }
            else
            {
                // мёртвый/приземлённый корабль тоже восстанавливается — без деформаций
                shrink = Mathf.MoveTowards(shrink, 0f, Time.deltaTime / RestoreDuration);
            }

            // масштаб меняем, коллайдер не трогаем; RespawnAfter вернёт исходный
            ship.transform.localScale = shrink > 0f ? shipScale * (1f - shrink) : shipScale;
        }

        private void Kill()
        {
            if (hull == null)
                return;
            hull.ApplyDamage(ship.Hp); // весь HP разом, как при касании звезды
        }

        private bool LazyBindShip()
        {
            if (ship != null)
                return true;
            ship = FindAnyObjectByType<Ship>();
            if (ship == null)
                return false;
            shipScale = ship.transform.localScale;
            hull = ship.GetComponent<HullSystem>();
            return hull != null;
        }
    }
}
