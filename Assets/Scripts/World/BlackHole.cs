using UnityEngine;
using SpaceTaxi.Audio;
using SpaceTaxi.Physics;

namespace SpaceTaxi.World
{
    // Чёрная дыра v2 (Н-10). Зона эффекта — воронка: прямое ускорение к центру
    // (не через GravityField), растущее к горизонту и всегда выше максимальной
    // тяги корабля — из зоны вырваться нельзя. Горизонт НЕ убивает: корабль
    // переводится в ShipState.Swallowed — хаотично болтается внутри без
    // управления, пока оператор не отключит корабль (оверлей/клавиша X) или
    // не выйдет лимит болтания; дальше — стандартный флоу потери
    // через HullSystem.Abandon
    public class BlackHole : CelestialBody
    {
        // дыра всегда ×8: если в данных множитель меньше — форсим (решение координатора)
        public const float HoleMassMultiplier = 8f;
        public const float EffectRadiusMultiplier = 6f;

        // --- числа игры, владелец подкрутит ---
        public const float SwallowTimeoutSeconds = 20f; // лимит болтания до автоотключения
        // тяга воронки в долях максимальной тяги корабля (Eff.Thrust × EngineMultiplier):
        // у края запас 25% поверх полного диапазона тяги, к горизонту — вдвое
        private const float FunnelEdgeRatio = 1.25f;
        private const float FunnelHorizonRatio = 2.5f;
        private const float ShrinkDuration = 4f;  // 1→ShrinkMinScale внутри зоны эффекта
        private const float RestoreDuration = 1f; // выход из зоны — восстановление быстрее
        private const float ShrinkMinScale = 0.25f;      // swallowed не сжимается в ноль
        private const float SwallowHoldAccel = 460f;     // воронка внутри горизонта — держит
        private const float SwallowSwirlAccel = 240f;    // закрутка по касательной
        private const float SwallowDragPerSecond = 1.8f; // вязкое гашение болтания
        private const float SwallowJoltSpeed = 110f;     // случайный толчок
        private const float SwallowJoltInterval = 0.35f; // …примерно раз в
        private const float SwallowSpinDegPerSec = 220f; // хаотичное вращение
        private const float SwallowKeepFraction = 0.92f; // жёсткий кламп глубины болтания

        public const string AbandonReason = "ПОГЛОЩЁН ЧЁРНОЙ ДЫРОЙ";

        public static readonly Color CoreColor = new Color(0.06f, 0.05f, 0.1f);
        public static readonly Color ZoneColor = new Color(0.45f, 0.25f, 0.7f, 0.1f);

        // горизонт = радиус тела, зона эффекта — вокруг него
        public float Horizon => Radius;
        public float EffectRadius => Radius * EffectRadiusMultiplier;

        // оверлей Н-10: сейчас ли корабль болтается за горизонтом
        public bool SwallowActive => ship != null && ship.State == ShipState.Swallowed;
        public float SwallowElapsed => swallowTimer;

        private Ship ship;
        private HullSystem hull;
        private Vector3 shipScale = Vector3.one;
        private float shrink; // 0 — нормальный корабль, 1 — сжат до ShrinkMinScale
        private float swallowTimer;
        private float joltTimer;

        public override void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig)
        {
            base.Init(data, physicsConfig);
            Mass = Mathf.Max(data.MassMultiplier, HoleMassMultiplier);
            // горизонт должен пропускать корабль внутрь: иначе физический
            // коллайдер держит его на R+4 и воронка не дотянет до центра
            GetComponent<CircleCollider2D>().isTrigger = true;
        }

        private void Update()
        {
            if (!LazyBindShip())
                return;

            // болтание за горизонтом: игровое время (пауза честно стоит), по лимиту — потеря
            if (ship.State == ShipState.Swallowed)
            {
                swallowTimer += Time.deltaTime;
                if (swallowTimer >= SwallowTimeoutSeconds)
                    RequestAbandon();
                return;
            }

            float d = Vector2.Distance(ship.transform.position, transform.position);

            // за горизонтом — не гибель, а проглатывание (Н-10)
            if (d <= Horizon)
            {
                if (ship.State == ShipState.Flying)
                    EnterSwallow();
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
                ship.transform.localScale = ShipScale();
            }
            else
            {
                Sound.StopLoop(SoundId.BlackHoleHum);
                if (shrink > 0f)
                {
                    // мёртвый/приземлённый корабль тоже восстанавливается — без деформаций
                    shrink = Mathf.MoveTowards(shrink, 0f, Time.deltaTime / RestoreDuration);
                    ship.transform.localScale = ShipScale();
                }
            }
        }

        // воронка: прямое ускорение к центру, глуб² — растёт к горизонту; у края
        // уже выше максимальной тяги корабля (даже с EngineMultiplier) — вырваться
        // нельзя при любом корпусе и улучшениях
        private void FixedUpdate()
        {
            if (ship == null || ship.Body == null)
                return;

            if (ship.State == ShipState.Swallowed)
            {
                SwallowedTumble(Time.fixedDeltaTime);
                return;
            }
            if (ship.State != ShipState.Flying)
                return;

            Vector2 center = transform.position;
            Vector2 offset = ship.Body.position - center;
            float d = offset.magnitude;
            if (d >= EffectRadius || d < 0.01f)
                return;

            float span = Mathf.Max(EffectRadius - Horizon, 0.001f);
            float depth = Mathf.Clamp01((EffectRadius - d) / span);
            float maxThrust = ship.Eff.Thrust * ship.Config.EngineMultiplier;
            float pull = Mathf.Lerp(maxThrust * FunnelEdgeRatio, maxThrust * FunnelHorizonRatio, depth * depth);
            ship.Body.linearVelocity += (Vector2)(-offset / d) * (pull * Time.fixedDeltaTime);
        }

        // болтание в режиме Swallowed: ShipController при State != Flying не
        // трогает скорость — здесь только наша физика: удержание воронкой,
        // закрутка, вязкость, случайные толчки и вращение (декорация)
        private void SwallowedTumble(float dt)
        {
            Vector2 center = transform.position;
            Vector2 offset = ship.Body.position - center;
            float d = Mathf.Max(offset.magnitude, 0.01f);
            Vector2 inward = (Vector2)(-offset / d);
            Vector2 tangent = new Vector2(-inward.y, inward.x);

            Vector2 velocity = ship.Body.linearVelocity;
            velocity += inward * (SwallowHoldAccel * dt);
            velocity += tangent * (SwallowSwirlAccel * dt);
            velocity *= Mathf.Clamp01(1f - SwallowDragPerSecond * dt);

            joltTimer -= dt;
            if (joltTimer <= 0f)
            {
                joltTimer = SwallowJoltInterval * Random.Range(0.7f, 1.3f);
                velocity += Random.insideUnitCircle * SwallowJoltSpeed;
                ship.Body.angularVelocity = Random.Range(-SwallowSpinDegPerSec, SwallowSpinDegPerSec);
            }
            ship.Body.linearVelocity = velocity;

            // страховка: за один шаг с толчком не вылететь за горизонт
            if (offset.magnitude > Horizon * SwallowKeepFraction)
                ship.Body.position = center + (Vector2)(offset / offset.magnitude) * (Horizon * SwallowKeepFraction);
        }

        private void EnterSwallow()
        {
            ship.State = ShipState.Swallowed; // гасит все входы в ShipController, шлейф и траекторию
            swallowTimer = 0f;
            joltTimer = 0f;
            shrink = 1f; // затянутый внутрь корабль — минимальный визуальный размер
            if (ship.Body != null)
            {
                ship.Body.linearVelocity *= 0.4f; // гасим вход, чтобы не проскочил насквозь
                ship.Body.angularVelocity = Random.Range(-SwallowSpinDegPerSec, SwallowSpinDegPerSec);
            }
            ship.transform.localScale = ShipScale();

            Sound.StopLoop(SoundId.BlackHoleHum);
            Sound.Play(SoundId.BlackHoleSwallow); // слот «провала» переехал сюда: гибели на горизонте больше нет
            CameraFollow.AddShake(0.5f);
            Debug.Log("[BlackHole] корабль затянуло за горизонт — управление отключено");
        }

        // оператор отключил корабль (оверлей/X) или вышел лимит болтания —
        // стандартный флоу потери: респавн, провал заказа, долг за аренду
        public void RequestAbandon()
        {
            if (ship == null || ship.State != ShipState.Swallowed || hull == null)
                return;
            hull.Abandon(AbandonReason);
        }

        // масштаб сжатия: 1 → ShrinkMinScale (внутри горизонта корабль остаётся виден)
        private Vector3 ShipScale()
        {
            return shipScale * Mathf.Lerp(1f, ShrinkMinScale, shrink);
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
                // смерть/потеря от другого источника — перестаём трогать масштаб,
                // иначе перетираем восстановленный RespawnAfter
                hull.Died += () => shrink = 0f;
                hull.ShipLost += _ => shrink = 0f;
            }
            return hull != null;
        }
    }
}
