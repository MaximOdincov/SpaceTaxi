using System;
using UnityEngine;
using SpaceTaxi.Economy;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    // Мягкое касание — посадка, жёсткое — урон; звезда — гибель
    public class LandingSystem : MonoBehaviour
    {
        private Ship ship;
        private HullSystem hull;

        // лениво: HullSystem может быть добавлен позже Awake (WorldBuilder)
        private HullSystem Hull => hull != null ? hull : (hull = GetComponent<HullSystem>());

        private void Awake()
        {
            ship = GetComponent<Ship>();
            hull = GetComponent<HullSystem>();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (ship == null || ship.Config == null || ship.State != ShipState.Flying)
                return;

            Planet planet = collision.collider.GetComponentInParent<Planet>();
            Star star = collision.collider.GetComponentInParent<Star>();
            Asteroid asteroid = collision.collider.GetComponentInParent<Asteroid>();
            Comet comet = collision.collider.GetComponentInParent<Comet>();
            float v = collision.relativeVelocity.magnitude;

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

            if (asteroid != null)
            {
                HitAsteroid(v);
                return;
            }

            if (planet == null)
                return;

            if (v < ship.Config.LandSpeed)
            {
                Land(planet, collision);
            }
            else
            {
                Crash(planet, collision, v);
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
            CameraFollow.AddShake(0.25f); // толчок старта
            Debug.Log($"[Landing] взлёт: v={ship.Body.linearVelocity.magnitude}");
        }

        // выжившему на посадке станция чинит и заправляет под ноль
        private void Land(Planet planet, Collision2D collision)
        {
            Vector2 normal = collision.contactCount > 0
                ? collision.contacts[0].normal
                : (Vector2)(transform.position - planet.transform.position).normalized;

            // снап на поверхность, скорость в ноль, нос от центра
            Vector2 pos = (Vector2)planet.transform.position + normal * (planet.Radius + 7f);
            transform.position = pos;
            ship.Body.linearVelocity = Vector2.zero;
            float nose = GetComponent<ShipController>() != null ? GetComponent<ShipController>().SpriteNoseAngle : 0f;
            float angle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle - nose);

            ship.LandedOn = planet;
            ship.State = ShipState.Landed;
            CameraFollow.AddShake(0.08f); // лёгкий «шлёп» касания
            ShipLanded?.Invoke(planet);

            // станция на планете: сервис платный (экран [B]) — здесь только подсказка
            if (planet.HasStation && (ship.Fuel < ship.Eff.FuelMax || ship.Hp < ship.Eff.HpMax))
                StationHint?.Invoke();

            Debug.Log($"[Landing] посадка на «{planet.DisplayName}»" + (planet.HasStation ? " (станция: сервис за кредиты, [B])" : ""));
        }

        public event Action<string> StationServiced;

        // посадка на станцию с неполным баком/корпусом — подсказка про [B] (ToastNotifier)
        public event Action StationHint;

        // платный сервис выполнен на экране станции — тот же тост-поток
        public void ReportServiced(string stationName)
        {
            StationServiced?.Invoke(stationName);
        }

        public event Action<World.Planet> ShipLanded;

        // контактный удар с уроном — для штрафа выплаты (жар звезды НЕ считается)
        public event Action CrashImpact;

        private void Crash(Planet planet, Collision2D collision, float v)
        {
            CrashImpact?.Invoke();

            // урон сразу; выжил — считается посадкой
            float damage = Mathf.Max(5f, (v - ship.Config.LandSpeed) * 0.9f);
            Hull.ApplyDamage(damage);
            CameraFollow.AddShake(Mathf.Clamp01(damage / 45f) * 0.7f); // от «бумкнул» до «въехал»
            Debug.Log($"[Landing] удар о «{planet.DisplayName}»: v={v:0}, урон={damage:0}");

            if (ship.Hp > 0f)
                Land(planet, collision);
        }

        // астероид пояса: посадки нет, State не меняется — корабль отскакивает физикой;
        // медленное касание бесплатно, удар сжимает корпус и считается столкновением заказа
        private void HitAsteroid(float v)
        {
            if (v < ship.Config.LandSpeed)
                return;

            float damage = Mathf.Min(30f, (v - ship.Config.LandSpeed) * 0.9f);
            CrashImpact?.Invoke();
            Hull.ApplyDamage(damage);
            CameraFollow.AddShake(Mathf.Clamp01(damage / 30f) * 0.7f);
            Debug.Log($"[Landing] удар об астероид: v={v:0}, урон={damage:0}");
        }
    }
}
