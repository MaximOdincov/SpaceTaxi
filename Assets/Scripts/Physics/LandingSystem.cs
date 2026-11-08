using System;
using UnityEngine;
using SpaceTaxi.Audio;
using SpaceTaxi.Economy;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    // Мягкое касание — посадка, жёсткое — урон; звезда — гибель.
    // После ShipController: кэш скорости до шага физики должен включать его тягу/гравитацию
    [DefaultExecutionOrder(200)]
    public class LandingSystem : MonoBehaviour
    {
        private Ship ship;
        private HullSystem hull;
        private ShipModelVisual modelVisual;

        // снап без визуала модели (HalfExtent ShipModelVisual возвращает то же)
        private const float DefaultHalfExtent = 7f;

        // лениво: HullSystem и ShipModelVisual добавляются позже Awake (WorldBuilder)
        private HullSystem Hull => hull != null ? hull : (hull = GetComponent<HullSystem>());
        private ShipModelVisual Visual => modelVisual != null
            ? modelVisual
            : (modelVisual = GetComponent<ShipModelVisual>());

        // скорость корабля до текущего шага физики. collision.relativeVelocity в
        // OnCollisionEnter2D ненадёжен: солвер к моменту коллбэка уже погасил
        // нормальную составляющую удара — лобовой удар читался как ~0 и «без урона»
        private Vector2 preSolveVelocity;

        // один контакт за шаг физики: несколько пар в один шаг (планета+астероид,
        // две планеты) не должны бить и сажать дважды
        private bool touchHandled;

        private void FixedUpdate()
        {
            touchHandled = false; // каждый шаг физики предшествует своей пачке коллбэков
            preSolveVelocity = ship != null && ship.Body != null ? ship.Body.linearVelocity : Vector2.zero;
        }

        private void Awake()
        {
            ship = GetComponent<Ship>();
            hull = GetComponent<HullSystem>();
            modelVisual = GetComponent<ShipModelVisual>();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (ship == null || ship.Config == null || ship.State != ShipState.Flying)
                return;

            Planet planet = collision.collider.GetComponentInParent<Planet>();
            Star star = collision.collider.GetComponentInParent<Star>();
            Asteroid asteroid = collision.collider.GetComponentInParent<Asteroid>();
            Comet comet = collision.collider.GetComponentInParent<Comet>();

            // скорость удара по нормали + тангенсу: relativeVelocity добиваем
            // кэшем до шага физики (планеты/астероиды статичны — это полная скорость)
            float v = Mathf.Max(collision.relativeVelocity.magnitude, preSolveVelocity.magnitude);

            if (star != null)
            {
                Hull.ApplyDamage(ship.Hp);
                return;
            }

            if (comet != null)
            {
                // комета — гибель, как у звезды; CrashImpact не файрим: провал
                // заказа уже ловит Died-путь, штраф столкновения тут не при чём
                Hull.ApplyDamage(ship.Hp);
                return;
            }

            // планета — до астероида: Enter для пары не повторяется, и если
            // первым придёт астероид (ставит touchHandled), контакт планеты
            // проглотится навсегда — корабль «лежит» на планете без посадки
            if (planet != null && !touchHandled)
            {
                if (v < ship.Config.LandSpeed)
                    Land(planet, collision);
                else
                    Crash(planet, collision, v);
                return;
            }

            if (asteroid != null)
            {
                HitAsteroid(v, collision);
            }
        }

        // взлёт со стоянки — импульс от центра планеты
        public void Takeoff()
        {
            if (ship == null || ship.State != ShipState.Landed || ship.LandedOn == null)
                return;
            Vector2 dir = ((Vector2)ship.transform.position - (Vector2)ship.LandedOn.transform.position).normalized;
            ship.Body.linearVelocity = dir * ship.Config.TakeoffV;
            ship.LandedOn = null;
            ship.State = ShipState.Flying;
            Sound.Play(SoundId.Takeoff);
            CameraFollow.AddShake(0.25f); // толчок старта
            Debug.Log($"[Landing] взлёт: v={ship.Body.linearVelocity.magnitude}");
        }

        // выжившему на посадке станция чинит и заправляет под ноль
        private void Land(Planet planet, Collision2D collision)
        {
            touchHandled = true; // касание обработано — параллельные пары в этом шаге молчат
            Vector2 normal = collision.contactCount > 0
                ? collision.contacts[0].normal
                : (Vector2)(transform.position - planet.transform.position).normalized;

            // снап на поверхность, скорость в ноль, нос от центра; отступ —
            // половина высоты контента визуала: корма касается, а не канва спрайта
            float extent = Visual != null ? Visual.HalfExtent : DefaultHalfExtent;
            Vector2 pos = (Vector2)planet.transform.position + normal * (planet.Radius + extent);
            transform.position = pos;
            ship.Body.linearVelocity = Vector2.zero;
            float nose = GetComponent<ShipController>() != null ? GetComponent<ShipController>().SpriteNoseAngle : 0f;
            float angle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle - nose);

            ship.LandedOn = planet;
            ship.State = ShipState.Landed;
            CameraFollow.AddShake(0.08f); // лёгкий «шлёп» касания
            Sound.Play(SoundId.LandingTouch);
            ShipLanded?.Invoke(planet);

            // сервис платный (экран [B]) — подсказка, только если планета имеет нужное:
            // на чистой заправке не зовём [B] ради ремонта и наоборот
            bool needFuel = planet.HasFuel && ship.Fuel < ship.Eff.FuelMax;
            bool needRepair = planet.HasRepair && ship.Hp < ship.Eff.HpMax;
            if (needFuel || needRepair)
                StationHint?.Invoke(planet);

            Debug.Log($"[Landing] посадка на «{planet.DisplayName}»" + (planet.HasFuel || planet.HasRepair ? " (станция: сервис за кредиты, [B])" : ""));
        }

        public event Action<string> StationServiced;

        // посадка на станцию с неполным баком/корпусом — подсказка про [B] (ToastNotifier);
        // планета в аргументе — текст подсказки собирается по её флагам услуг
        public event Action<World.Planet> StationHint;

        // платный сервис выполнен на экране станции — тот же тост-поток
        public void ReportServiced(string stationName)
        {
            StationServiced?.Invoke(stationName);
        }

        public event Action<World.Planet> ShipLanded;

        // контактный удар с уроном — для штрафа выплаты (жар звезды НЕ считается)
        public event Action CrashImpact;

        // точка контактного удара в мире — для вспышки эффектов (Н-7)
        public event Action<Vector2> CrashImpactAt;

        private Vector2 ContactPoint(Collision2D collision)
        {
            return collision.contactCount > 0
                ? collision.contacts[0].point
                : (Vector2)transform.position;
        }

        // урон контактного удара: 0 при v ≤ LandSpeed, плавный вход до полной
        // линейности к v = LandSpeed+30 (без ступени min(5,…)), кап 30 —
        // планеты и астероиды считают одинаково
        private float CrashDamage(float v)
        {
            float delta = v - ship.Config.LandSpeed;
            if (delta <= 0f)
                return 0f;
            float ramp = Mathf.Clamp01(delta / 30f);
            return Mathf.Min(30f, delta * 0.9f * ramp);
        }

        private void Crash(Planet planet, Collision2D collision, float v)
        {
            float damage = CrashDamage(v);
            if (damage <= 0f)
            {
                // пороговая скорость — урона нет, это просто посадка
                Land(planet, collision);
                return;
            }

            touchHandled = true;
            CrashImpact?.Invoke();
            CrashImpactAt?.Invoke(ContactPoint(collision));

            // урон сразу; выжил — считается посадкой
            Hull.ApplyDamage(damage);
            Sound.Play(SoundId.Collision, Mathf.Clamp01(damage / 30f)); // вариация по силе удара
            CameraFollow.AddShake(Mathf.Clamp01(damage / 45f) * 0.7f); // от «бумкнул» до «въехал»
            Debug.Log($"[Landing] удар о «{planet.DisplayName}»: v={v:0}, урон={damage:0.0}");

            if (ship.Hp > 0f)
                Land(planet, collision);
        }

        // астероид пояса: посадки нет, State не меняется — корабль отскакивает физикой;
        // медленное касание бесплатно (и флаг не тратит), удар сжимает корпус
        // и считается столкновением заказа
        private void HitAsteroid(float v, Collision2D collision)
        {
            if (touchHandled)
                return;
            float damage = CrashDamage(v);
            if (damage <= 0f)
                return;

            touchHandled = true;
            CrashImpact?.Invoke();
            CrashImpactAt?.Invoke(ContactPoint(collision));
            Hull.ApplyDamage(damage);
            Sound.Play(SoundId.Collision, Mathf.Clamp01(damage / 30f));
            CameraFollow.AddShake(Mathf.Clamp01(damage / 30f) * 0.7f);
            Debug.Log($"[Landing] удар об астероид: v={v:0}, урон={damage:0.0}");
        }
    }
}
