using UnityEngine;
using SpaceTaxi.Audio;
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
            // горизонт должен пропускать корабль внутрь: иначе физический
            // коллайдер держит его на R+4 и «гибель на горизонте» не срабатывает
            GetComponent<CircleCollider2D>().isTrigger = true;
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

            // гул притяжения: 0.3 у края зоны эффекта → 1.0 у горизонта;
            // StartLoop/StopLoop идемпотентны — вызов каждый кадр дешевле флагов
            if (d <= EffectRadius && ship.State == ShipState.Flying)
            {
                Sound.StartLoop(SoundId.BlackHoleHum);
                float span = Mathf.Max(EffectRadius - Horizon, 0.001f);
                float depth = Mathf.Clamp01((EffectRadius - d) / span);
                Sound.SetLoopIntensity(SoundId.BlackHoleHum, Mathf.Lerp(0.3f, 1f, depth));

                shrink = Mathf.MoveTowards(shrink, 1f, Time.deltaTime / ShrinkDuration);
                if (shrink >= 1f)
                {
                    Kill();
                    return;
                }
                ship.transform.localScale = shipScale * (1f - shrink);
            }
            else
            {
                Sound.StopLoop(SoundId.BlackHoleHum);
                if (shrink > 0f)
                {
                    // мёртвый/приземлённый корабль тоже восстанавливается — без деформаций
                    shrink = Mathf.MoveTowards(shrink, 0f, Time.deltaTime / RestoreDuration);
                    // последняя итерация вернёт исходный масштаб и больше не пишет
                    ship.transform.localScale = shrink > 0f ? shipScale * (1f - shrink) : shipScale;
                }
            }
        }

        private void Kill()
        {
            if (hull == null)
                return;
            // одноразово: Update зовёт Kill каждый кадр, пока труп в горизонте,
            // повторный проход молчит по guard-у ApplyDamage (Hp <= 0)
            if (ship.Hp > 0f)
            {
                Sound.StopLoop(SoundId.BlackHoleHum);
                Sound.Play(SoundId.BlackHoleSwallow);
            }
            hull.ApplyDamage(ship.Hp); // весь HP разом, как при касании звезды
        }

        // сцена может выгрузиться прямо в гуле — луп живёт в AudioManager, глушим
        protected override void OnDisable()
        {
            base.OnDisable(); // разрегистрация в GravityField
            Sound.StopLoop(SoundId.BlackHoleHum);
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
            if (hull != null)
            {
                // смерть от другого источника — перестаём трогать масштаб,
                // иначе перетираем восстановленный RespawnAfter
                hull.Died += () => shrink = 0f;
                hull.ShipLost += _ => shrink = 0f;
            }
            return hull != null;
        }
    }
}
