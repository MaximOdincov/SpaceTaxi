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

        // дожим в посадочную позу корутиной: контактная точка не совпадает с позой
        // покоя, мгновенный телепорт давал подпрыгивание — тело замораживается,
        // поза доводится плавно (A5)
        private Coroutine landingPinch;
        private RigidbodyConstraints2D savedConstraints;

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
            CancelPinch(); // снять дожим и разморозить тело, иначе импульс не сдвинет
            if (ship == null || ship.State != ShipState.Landed || ship.LandedOn == null)
                return;
            Vector2 dir = ((Vector2)ship.transform.position - (Vector2)ship.LandedOn.transform.position).normalized;
            ship.Body.linearVelocity = dir * ship.Config.TakeoffV;
            ship.LandedOn = null;
            ship.State = ShipState.Flying;
            Sound.Play(SoundId.Takeoff);
            CameraFollow.AddShake(0.25f); // толчок старта
            GetComponent<ShipController>()?.HoldHeading(1.5f); // подъём низом вперёд
            Debug.Log($"[Landing] взлёт: v={ship.Body.linearVelocity.magnitude}");
        }

        // выжившему на посадке станция чинит и заправляет под ноль
        private void Land(Planet planet, Collision2D collision)
        {
            touchHandled = true; // касание обработано — параллельные пары в этом шаге молчат

            // целевая поза по радиали от центра планеты, не по нормали контакта:
            // нормаль у касания капсулы уходит вбок — мгновенный снап давал боковой
            // рывок; радиаль совпадает с Takeoff и Ship.ApplyModelSprite
            Vector2 dir = ((Vector2)transform.position - (Vector2)planet.transform.position).normalized;
            if (dir.sqrMagnitude < 1e-6f)
                dir = collision.contactCount > 0 ? (Vector2)collision.contacts[0].normal : Vector2.up;
            float extent = ShipPlacement.LandedExtent(Visual);
            Vector2 targetPos = (Vector2)planet.transform.position + dir * (planet.Radius + extent);
            ShipController controller = GetComponent<ShipController>();
            float nose = controller != null ? controller.SpriteNoseAngle : 0f;
            float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - nose;

            // стартовая поза — до заморозки, из тела (при интерполяции transform отстаёт)
            Vector2 fromPos = ship.Body.position;
            float fromAngle = ship.Body.rotation;

            // заморозка на время дожима: солвер не выталкивает из позы «чуть ниже»
            // и не подпрыгивает; скорость и вращение гасим сразу
            ship.Body.linearVelocity = Vector2.zero;
            ship.Body.angularVelocity = 0f; // касание капсулой не должно оставлять вращение
            savedConstraints = ship.Body.constraints;
            ship.Body.constraints = RigidbodyConstraints2D.FreezeAll;

            // состояние и события — до корутины: UI и заказы видят Landed в кадр контакта
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

            if (landingPinch != null)
                StopCoroutine(landingPinch);
            landingPinch = StartCoroutine(PinchToPose(planet, fromPos, fromAngle, targetPos, targetAngle, 0.12f));
        }

        // плавный довод посадочной позы (unscaled — дожим доигрывается и на паузе
        // после модала доставки); взлёт/гибель посреди дожима отпускают тело
        private System.Collections.IEnumerator PinchToPose(Planet planet, Vector2 fromPos, float fromAngle,
            Vector2 toPos, float toAngle, float duration)
        {
            float t = 0f;
            while (t < 1f)
            {
                yield return null;
                if (ship == null || ship.State != ShipState.Landed || ship.LandedOn != planet)
                    break; // взлёт/гибель — отпустить
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / duration);
                float k = t * t * (3f - 2f * t);
                ShipPlacement.TeleportLanded(ship, Vector2.Lerp(fromPos, toPos, k), Mathf.LerpAngle(fromAngle, toAngle, k));
            }
            if (ship != null && ship.Body != null)
                ship.Body.constraints = savedConstraints;
            landingPinch = null;
        }

        // снять дожим, если идёт: корутина стоп, constraints восстановлены
        private void CancelPinch()
        {
            if (landingPinch == null)
                return;
            StopCoroutine(landingPinch);
            landingPinch = null;
            if (ship != null && ship.Body != null)
                ship.Body.constraints = savedConstraints;
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


        // урон контактного удара: 0 при v ≤ LandSpeed, плавный вход до полной
        // линейности к v = LandSpeed+30 (без ступени min(5,…)), кап 30 —
        // планеты и астероиды считают одинаково
        // квадратичный рост без капа: мягкое касание почти бесплатно,
        // средняя скорость — ощутимо, полная — смерть любому корпусу
        // (старый кап 30 делал 100 и 330 км/ч одинаково больно);
        // 0.0032: удар на MaxV снимает ≥ HP любого корпуса, вкл. Кабан 150×1.6 = 240 (Н3.1)
        private const float CrashDamagePerSpeedSq = 0.0032f;

        private float CrashDamage(float v)
        {
            float delta = v - ship.Config.LandSpeed;
            return delta <= 0f ? 0f : CrashDamagePerSpeedSq * delta * delta;
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
            Hull.ApplyDamage(damage);
            Sound.Play(SoundId.Collision, Mathf.Clamp01(damage / 30f));
            CameraFollow.AddShake(Mathf.Clamp01(damage / 30f) * 0.7f);
            Debug.Log($"[Landing] удар об астероид: v={v:0}, урон={damage:0.0}");
        }
    }
}
