using UnityEngine;
using SpaceTaxi.Audio;
using SpaceTaxi.Physics;

namespace SpaceTaxi.World
{
    // Чёрная дыра v5 (2026-11-15, переработка владельца): дыра В РАЗЫ БОЛЬШЕ
    // (радиус тела в ассете уровня ×3) И СИЛЬНЕЕ (воронка/закрутка — ручки
    // PhysicsConfig). Захват (кольцо) запускает окно поглощения
    // BlackHoleShrinkSeconds (~45 РЕАЛЬНЫХ секунд): корабль КРУТИТ и сжимает —
    // быстро в начале, потом всё слабее (ease-out по времени захвата), спираль
    // к центру тоже затухает по мере сжатия. В конце окна «ПРОПАДАЕТ СВЯЗЬ» —
    // корабль отключается сам, оператор спавнится на новом. Или оператор
    // отключает корабль сам в любой момент (X) — спавн на новом сразу. В ОБОИХ
    // случаях старый борт остаётся КРУЖИТЬ НА ОРБИТЕ дыры ДО КОНЦА ОКНА и его
    // видно с нового корабля (WreckSystem, орбитальный режим). Предупреждения —
    // в HUD-баннере (как жар), НЕ на весь экран; X ловит оверлей-реле.
    // Респавн — стандартный (на стартовой планете). Воронка v5.1: двигатели
    // в захвате РАБОТАЮТ (разворот/тормоз/тяга — без гасилки скорости) —
    // исход решает чистое сравнение силы тяги и воронки: слабые обречены
    // сразу, сильные (Барин) — пока глубина мала; у застрявших орбитальная
    // скорость вязко стягивается (потолок проседает со сжатием), предел
    // падения растёт — траектория угасает внутрь. Обломок отключённого
    // борта тоже спиралит внутрь (WreckSystem), а не висит клоном.
    public class BlackHole : CelestialBody
    {
        // дыра всегда ×8: если в данных множитель меньше — форсим (решение координатора)
        public const float HoleMassMultiplier = 8f;
        public const float EffectRadiusMultiplier = 6f;

        // вклад дыры в общее гравитационное поле (прогноз траектории, качели,
        // тормоз) — нарочно мал: настоящее притяжение даёт воронка ниже, а
        // полный множитель ×8 упирался бы в кламп 240 и не пускал никого
        public const float FieldGravityScale = 0.03f;

        // кривая воронки: притяжение растёт как depth^Power от края зоны к горизонту.
        // §1.6 (градиент риска): степень 2 — край воронки прощает всем,
        // середина оставляет шанс комфорту+, глубина — без шансов
        private const float PullDepthPower = 2f;
        // сжатие-спагетти: множитель по глубине почти равномерный (хотфикс
        // владельца: полное окно ≈ 3-4 РЕАЛЬНЫХ минуты стены — прежде у горизонта
        // сжатие шло в 4-5× быстрее стены и окно таяло за минуту)
        private const float ShrinkRateNearEdge = 0.9f;
        private const float ShrinkRateNearHorizon = 1.15f;

        // ease-out сжатия (2026-11-15, владелец: «в начале уменьшается заметно,
        // а потом всё слабее, слабее»): прогресс = 1-(1-x)^Power, x — доля окна.
        // Степень 2: к половине окна корабль сжат на 75%, дальше ползёт
        private const float ShrinkEasePower = 2f;

        private const float RestoreDuration = 1f; // выход из зоны — восстановление быстрее
        // гистерезис выхода из захвата: на границе кольца удержание и звук
        // «затянуло» не должны хлопать туда-обратно
        private const float CaptureExitFraction = 1.06f;

        // причины потери борта (тост «КОРАБЛЬ ПОТЕРЯН — …»): в конце окна
        // «пропадает связь», вручную — оператор отключил сам
        public const string SignalLostReason = "ПОТЕРЯНА СВЯЗЬ С БОРТОМ";
        public const string AbandonedReason = "БОРТ ОТКЛЮЧЁН ОПЕРАТОРОМ";

        public static readonly Color CoreColor = new Color(0.06f, 0.05f, 0.1f);
        public static readonly Color ZoneColor = new Color(0.45f, 0.25f, 0.7f, 0.1f);

        // горизонт = радиус тела, зона эффекта — вокруг него
        public float Horizon => Radius;
        public float EffectRadius => Radius * EffectRadiusMultiplier;

        // зона захвата: кольцо из данных уровня (или r×3.3+30), но не дальше
        // зоны эффекта — воронка действует только в ней
        public float CaptureRadius => Mathf.Min(CaptureRing, EffectRadius);

        // оверлей Н-10 → v5: корабль в захвате дыры (окно поглощения идёт);
        // предупреждение — в HUD-баннере (HudBinder), реле X — оверлей-контроллер
        public bool SwallowActive => Captured;
        public bool Captured => wasCaptured && ship != null && ship.State == ShipState.Flying;
        // корабль в зоне влияния (до захвата) — для мягкого предупреждения в баннере
        public bool InEffectZone => ship != null && ship.State == ShipState.Flying
            && Vector2.Distance(ship.transform.position, transform.position) <= EffectRadius;
        // борт заглушен оператором (X) — баннер без подсказки X, реле X молчит
        public bool PoweredDown => ship != null && ship.PoweredDown;
        // сколько РЕАЛЬНЫХ секунд осталось до потери связи (для баннера и обломка):
        // время захвата копится с множителем глубины, остаток — от окна
        public float SwallowLeftSeconds => Mathf.Max(0f, config.BlackHoleShrinkSeconds - captureElapsed);

        private Ship ship;
        private HullSystem hull;
        private Vector3 shipScale = Vector3.one;
        private float shrink;          // 0 — нормальный корабль, 1 — поглощён полностью
        private float captureElapsed;  // время в захвате (с множителем глубины), с
        private bool swallowStarted;   // полное поглощение — один раз
        private bool wasCaptured;      // одноразовые эффекты на входе в кольцо

        public override void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig, bool fuelOutage = false)
        {
            base.Init(data, physicsConfig, fuelOutage);
            Mass = Mathf.Max(data.MassMultiplier, HoleMassMultiplier);
            // горизонт должен пропускать корабль внутрь: иначе физический
            // коллайдер держит его на R+4 и воронка не дотянет до центра
            GetComponent<CircleCollider2D>().isTrigger = true;
        }

        private void Update()
        {
            if (!LazyBindShip())
                return;

            // недостижимый запасной режим — корабль гибнет по порогам раньше
            if (ship.State == ShipState.Swallowed)
                return;

            float d = Vector2.Distance(ship.transform.position, transform.position);

            // v5.1: заглушенный борт — дыра держит при ЛЮБОЙ дистанции: окно
            // идёт, сжатие продолжается, гул не смолкает, restore не применяется
            if (ship.PoweredDown && ship.State == ShipState.Flying)
            {
                Sound.StartLoop(SoundId.BlackHoleHum);
                float span = Mathf.Max(EffectRadius - Horizon, 0.001f);
                float dClamped = Mathf.Clamp(d, Horizon + 0.01f, EffectRadius - 0.01f);
                float depth = Mathf.Clamp01((EffectRadius - dClamped) / span);
                Sound.SetLoopIntensity(SoundId.BlackHoleHum, Mathf.Lerp(0.3f, 1f, depth));

                float rate = Mathf.Lerp(ShrinkRateNearEdge, ShrinkRateNearHorizon, depth);
                captureElapsed += Mathf.Min(Time.deltaTime, 0.1f) * rate;
                float seconds = Mathf.Max(config.BlackHoleShrinkSeconds, 0.1f);
                float x = Mathf.Clamp01(captureElapsed / seconds);
                shrink = 1f - Mathf.Pow(1f - x, ShrinkEasePower);
                ship.transform.localScale = ShipScale();

                if (x >= 1f && !swallowStarted)
                    FullSwallow(); // «пропадает связь» — спавн на стартовой планете
                return;
            }

            if (d <= EffectRadius && ship.State == ShipState.Flying)
            {
                // гул притяжения: 0.3 у края зоны эффекта → 1.0 у горизонта;
                // StartLoop/StopLoop идемпотентны — вызов каждый кадр дешевле флагов
                Sound.StartLoop(SoundId.BlackHoleHum);
                float span = Mathf.Max(EffectRadius - Horizon, 0.001f);
                float depth = Mathf.Clamp01((EffectRadius - d) / span);
                Sound.SetLoopIntensity(SoundId.BlackHoleHum, Mathf.Lerp(0.3f, 1f, depth));

                // сжатие-спагетти: прогрессирует только в захвате (внутри
                // кольца), у горизонта — быстрее; между кольцом и краем держится.
                // v5: ход сжатия — ease-out (владелец: «в начале уменьшается
                // заметно, а потом всё слабее, слабее»): время захвата копится
                // с множителем глубины, сжатие = 1-(1-x)^Power — в конце окна
                // корабль всё же исчезает (ПОТЕРЯНА СВЯЗЬ)
                if (d <= CaptureRadius)
                {
                    float rate = Mathf.Lerp(ShrinkRateNearEdge, ShrinkRateNearHorizon, depth);
                    // кламп кадра: спайки dt (загрузка сцены, лаги) не съедают окно
                    // поглощения за миг — окно честно отмеряется игровым временем
                    captureElapsed += Mathf.Min(Time.deltaTime, 0.1f) * rate;
                    float seconds = Mathf.Max(config.BlackHoleShrinkSeconds, 0.1f);
                    float x = Mathf.Clamp01(captureElapsed / seconds);
                    shrink = 1f - Mathf.Pow(1f - x, ShrinkEasePower);
                    ship.transform.localScale = ShipScale();

                    if (x >= 1f && !swallowStarted)
                        FullSwallow(); // конец окна — потеря связи, борт списан
                }
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
                if (d > EffectRadius)
                    captureElapsed = 0f; // вышел из зоны — окно захвата заново
            }
        }

        // воронка: прямое ускорение к центру, растёт к горизонту как depth^3.
        // Внутри кольца захвата — удержание радиального вылета, закрутка орбиты
        // и вязкое ограничение скорости падения; корабль с достаточной тягой
        // от удержаний освобождён и может вырваться
        private void FixedUpdate()
        {
            if (ship == null || ship.Body == null || ship.State != ShipState.Flying)
                return;

            Vector2 center = transform.position;
            Vector2 offset = ship.Body.position - center;
            float d = offset.magnitude;
            bool powered = ship.PoweredDown;
            // заглушенный борт дыра держит при ЛЮБОЙ дистанции — дрейфом наружу
            // не уйти, окно дотикает где бы корабль ни был
            if (!powered && (d >= EffectRadius || d < 0.01f))
                return;
            if (d < 0.01f)
                return;

            float span = Mathf.Max(EffectRadius - Horizon, 0.001f);
            float dForDepth = powered ? Mathf.Clamp(d, Horizon + 0.01f, EffectRadius - 0.01f) : d;
            float depth = Mathf.Clamp01((EffectRadius - dForDepth) / span);
            Vector2 inward = -(Vector2)(offset / d);
            Vector2 velocity = ship.Body.linearVelocity + inward * (PullAt(depth) * Time.fixedDeltaTime);

            bool captured = powered || d <= (wasCaptured ? CaptureRadius * CaptureExitFraction : CaptureRadius);
            if (captured && !wasCaptured)
            {
                wasCaptured = true;
                Sound.Play(SoundId.BlackHoleSwallow); // слот «провала»: момент захвата
                CameraFollow.AddShake(0.45f);
                Debug.Log("[BlackHole] корабль захвачен — воронка сильнее тяги");
            }
            else if (!captured && wasCaptured)
            {
                wasCaptured = false;
                Debug.Log("[BlackHole] корабль вырвался из захвата");
            }

            if (captured)
            {
                Vector2 u = offset / d; // наружу от центра
                float radial = Vector2.Dot(velocity, u);
                Vector2 tangential = velocity - u * radial;

                // закрутка — вдоль текущего направления орбиты (или против часовой)
                Vector2 orbitDir = tangential.sqrMagnitude > 0.25f
                    ? tangential.normalized
                    : new Vector2(-u.y, u.x);
                float swirl = Mathf.Lerp(config.BlackHoleSwirlEdge, config.BlackHoleSwirlHorizon, depth);
                tangential += orbitDir * (swirl * Time.fixedDeltaTime);

                // вязкость падения (v5.1, владелец: «не только размер — должна
                // уменьшаться траектория и затягивать больше внутрь, а то
                // супер маленький корабль волочится по огромной орбите»):
                // предел падения РАСТЁТ по мере сжатия — крошечный корабль
                // уже не волочится по большому кругу, а падает внутрь всё
                // быстрее; быстрый таран горизонта по-прежнему превращается
                // в медленную спираль
                float sinkCap = config.BlackHoleSinkSpeedMax * (0.4f + 1.6f * shrink);
                if (radial < -sinkCap)
                    radial = Mathf.MoveTowards(radial, -sinkCap,
                        config.BlackHoleSinkClampRate * Time.fixedDeltaTime);

                // «застрял» (v5.1, владелец: «двигатели должны работать даже в
                // зоне влияния... у слабых шансов нет на разных этапах, у сильных
                // и Барина сначала есть, потом пропадает; разворот, тормоз и тд
                // работают — просто дыру уже не остановить»): тяга против
                // воронки — ЧИСТОЕ силовое сравнение, никакой гасилки скорости:
                // корабль жмёт наружу — воронка пересиливает. Застрявшему вязко
                // стягивается орбитальная скорость (потолок ниже центробежного
                // баланса и проседает со сжатием) — спираль внутрь неизбежна
                float thrust = powered ? 0f : ship.Eff.Thrust;
                if (thrust <= TotalPull(d, depth) * config.BlackHoleEscapeThrustFraction)
                {
                    float orbitCap = config.BlackHoleOrbitSpeedFactor * (1f - 0.4f * shrink)
                        * Mathf.Sqrt(TotalPull(d, depth) * d);
                    float tangentMag = tangential.magnitude;
                    if (tangentMag > 0.001f)
                        tangential = tangential.normalized * Mathf.MoveTowards(
                            tangentMag, orbitCap, config.BlackHoleOrbitClampRate * Time.fixedDeltaTime);
                }

                velocity = u * radial + tangential;
            }

            ship.Body.linearVelocity = velocity;

            // v4: порог приближения убран — дыра не убивает, корабль дрейфует
            // над горизонтом и сжимается, пока не поглощён полностью
        }

        // притяжение воронки на текущей глубине (без вклада общего поля)
        private float PullAt(float depth)
        {
            return Mathf.Lerp(config.BlackHolePullEdge, config.BlackHolePullHorizon,
                Mathf.Pow(depth, PullDepthPower));
        }

        // полное притяжение: воронка + след дыры в общем поле — с ним корабль
        // реально борется тягой, на него замкнуты порог вырывания и потолок орбиты
        private float TotalPull(float d, float depth)
        {
            float field = Mathf.Min(config.GravParamOf(this) / Mathf.Max(d * d, 1f), config.GravityAccelClamp);
            return PullAt(depth) + field;
        }

        // конец окна поглощения (владелец: «пропадает связь и корабль
        // отключается, и ты спавнишься на новом»): борт списан без взрыва —
        // стандартная потеря (HullSystem.Abandon: списание, пересадка на
        // аренду; RespawnAfter возвращает оператора на стартовую планету).
        // Обломок-клон у дыры НЕ спавнится — гибель корабля оператор видел
        // вживую (сжатие и спираль шли при нём)
        private void FullSwallow()
        {
            if (swallowStarted || hull == null || ship == null || ship.State != ShipState.Flying)
                return;
            swallowStarted = true;
            Sound.StopLoop(SoundId.BlackHoleHum);
            // Н9.6 (владелец: «быстро вернулся — старый борт должен быть там,
            // она как-бы медленно затягивает; в конце окна он полностью
            // пропадает в дыре»): обломок-призрак продолжает спираль от точки
            // потери — радиус и масштаб угасают за lifetime (SpawnOrbital),
            // lifetime = то же окно (45с). v5.2 снимал обломок совсем
            if (CaptureWreckPose(out Sprite sprite, out float scale, out float angular, out float spin))
                WreckSystem.Ensure().SpawnOrbital(this, ship.transform.position, sprite, scale,
                    angular, spin, config.BlackHoleShrinkSeconds);
            hull.Abandon(SignalLostReason);
        }

        // оператор отключил корабль сам (X в захвате): борт ГЛОХНЕТ — тяги
        // больше нет, воронка дотягивает его до конца окна. СПАВНА НОВОГО
        // КОРАБЛЯ ЗДЕСЬ НЕТ И КЛОНА У ДЫРЫ ТОЖЕ (владелец: «он должен
        // появиться на стартовой планете и ждать меня там, а не появляться
        // тут после отключения») — новый борт оператор получает в конце окна
        // (FullSwallow: «пропадает связь» → стандартный респавн)
        public void RequestAbandon()
        {
            if (ship == null || ship.State != ShipState.Flying)
                return;
            if (!wasCaptured)
                return; // вне захвата дыры отключать нечего
            ship.PoweredDown = true;
            Debug.Log("[BlackHole] борт заглушен оператором — воронка дотягивает до потери связи");
        }

        // масштаб сжатия: 1 → BlackHoleMinScale (порог гибели по размеру)
        private Vector3 ShipScale()
        {
            return shipScale * Mathf.Lerp(1f, config.BlackHoleMinScale, shrink);
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
                hull.Died += OnShipDied;
                hull.ShipLost += _ => OnShipLostInCapture();
            }
            return hull != null;
        }

        // потеря борта в захвате (конец окна): сброс сжатия. Обломок-клон у дыры
        // НЕ спавнится (владелец: «после отключения появляется его клон» —
        // гибель корабля оператор видел вживую, новый борт ждёт на стартовой)
        private void OnShipLostInCapture()
        {
            shrink = 0f;
            wasCaptured = false;
            swallowStarted = false;
            captureElapsed = 0f;
        }

        // гибель корпуса: сброс сжатия и — если это гибель в захвате дыры —
        // обломок-призрак, который остаётся кружить до конца сессии
        private void OnShipDied()
        {
            shrink = 0f;
            wasCaptured = false;
            if (ship == null || ship.State == ShipState.Respawning)
                return;
            if (Vector2.Distance(ship.transform.position, transform.position) > CaptureRadius)
                return; // гибель вне захвата (звезда/столкновение) — без обломка у дыры

            if (CaptureWreckPose(out Sprite sprite, out float scale, out float angular, out float spin))
                WreckSystem.Ensure().Spawn(this, ship.transform.position, sprite, scale, angular, spin);
        }

        // поза и спрайт корабля для обломка: масштаб визуала модели (с учётом
        // сжатия дырой — обломок рождается ровно в размере гибели), закрутка
        // из орбитальной скорости. Вызывать ДО Abandon/HideVisuals — спрайт
        // ещё жив (позже использующий код не обязан знать про порядок методов)
        private bool CaptureWreckPose(out Sprite sprite, out float scale, out float angular, out float spin)
        {
            sprite = CaptureShipSprite();
            scale = 0f;
            angular = 0.8f; // рад/с, против часовой по умолчанию
            spin = 25f;     // качание вокруг своей оси, град/с
            if (sprite == null)
                return false;

            ShipModelVisual visual = ship.GetComponent<ShipModelVisual>();
            if (visual != null && visual.VisualRoot != null)
                scale = Mathf.Max(Mathf.Abs(visual.VisualRoot.lossyScale.x), 0.05f);
            else
                scale = Mathf.Max(Mathf.Abs(ship.transform.localScale.x), 0.05f);

            if (ship.Body != null)
            {
                Vector2 offset = ship.transform.position - transform.position;
                float dist = Mathf.Max(offset.magnitude, 0.01f);
                Vector2 u = offset / dist;
                float tangent = Vector2.Dot(ship.Body.linearVelocity, new Vector2(-u.y, u.x));
                float fromVelocity = Mathf.Clamp(tangent / dist, -1.5f, 1.5f);
                if (Mathf.Abs(fromVelocity) > 0.05f)
                    angular = fromVelocity;
                float bodySpin = Mathf.Clamp(ship.Body.angularVelocity, -90f, 90f);
                if (Mathf.Abs(bodySpin) > 15f)
                    spin = bodySpin;
            }
            return true;
        }

        // спрайт корабля: визуал модели (арт А1/А2) или корневой рендерер.
        // Вызывается внутри HullSystem.Died — до HideVisuals, спрайт ещё жив
        private Sprite CaptureShipSprite()
        {
            ShipModelVisual visual = ship.GetComponent<ShipModelVisual>();
            if (visual != null && visual.VisualRoot != null)
            {
                SpriteRenderer renderer = visual.VisualRoot.GetComponentInChildren<SpriteRenderer>();
                if (renderer != null && renderer.sprite != null)
                    return renderer.sprite;
            }
            SpriteRenderer root = ship.GetComponent<SpriteRenderer>();
            return root != null ? root.sprite : null;
        }
    }
}
