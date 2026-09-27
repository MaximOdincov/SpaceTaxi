using System;
using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    // Мягкое касание (v < LandSpeed) — посадка, быстрее — урон (v-LandSpeed)*0.9
    // и отскок. Касание звезды — мгновенная гибель.
    public class LandingSystem : MonoBehaviour
    {
        private Ship ship;
        private HullSystem hull;

        // Лениво: HullSystem может быть добавлен позже Awake (WorldBuilder).
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
            float v = collision.relativeVelocity.magnitude;

            if (star != null)
            {
                // Касание звезды смертельно (как в прототипе).
                Hull.ApplyDamage(ship.Hp);
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

        // Взлёт со стоянки: импульс TakeoffV от центра планеты (W или ПРОБЕЛ).
        public void Takeoff()
        {
            if (ship == null || ship.State != ShipState.Landed || ship.LandedOn == null)
                return;
            Vector2 dir = ((Vector2)ship.transform.position - (Vector2)ship.LandedOn.transform.position).normalized;
            ship.Body.linearVelocity = dir * ship.Config.TakeoffV;
            ship.LandedOn = null;
            ship.State = ShipState.Flying;
            Debug.Log($"[Landing] взлёт: v={ship.Body.linearVelocity.magnitude}");
        }

        private void Land(Planet planet, Collision2D collision)
        {
            Vector2 normal = collision.contactCount > 0
                ? collision.contacts[0].normal
                : (Vector2)(transform.position - planet.transform.position).normalized;

            // Снап на поверхность, скорость в ноль, нос от центра.
            Vector2 pos = (Vector2)planet.transform.position + normal * (planet.Radius + 7f);
            transform.position = pos;
            ship.Body.linearVelocity = Vector2.zero;
            float nose = GetComponent<ShipController>() != null ? GetComponent<ShipController>().SpriteNoseAngle : 0f;
            float angle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle - nose);

            ship.LandedOn = planet;
            ship.State = ShipState.Landed;

            // Станция на планете: мгновенная заправка и ремонт (12 par.3.2).
            if (planet.HasStation)
            {
                bool healed = ship.Hp < ship.Config.HpMax;
                bool refueled = ship.Fuel < ship.Config.FuelMax;
                ship.Hp = ship.Config.HpMax;
                ship.Fuel = ship.Config.FuelMax;
                if (healed || refueled)
                    StationServiced?.Invoke(planet.DisplayName);
            }

            Debug.Log($"[Landing] посадка на «{planet.DisplayName}»" + (planet.HasStation ? " (станция: бак/корпус полные)" : ""));
        }

        /// <summary>Посадка на планету со станцией восполнила бак/корпус. Для тоста.</summary>
        public event Action<string> StationServiced;

        private void Crash(Planet planet, Collision2D collision, float v)
        {
            Vector2 normal = collision.contactCount > 0
                ? collision.contacts[0].normal
                : (Vector2)(transform.position - planet.transform.position).normalized;

            // Урон: max(5, (v - LandSpeed) * 0.9).
            float damage = Mathf.Max(5f, (v - ship.Config.LandSpeed) * 0.9f);
            Hull.ApplyDamage(damage);

            // Отскок: отражение нормальной составляющей (коэф. 1.7) и гашение до 45%.
            Vector2 velocity = ship.Body.linearVelocity;
            float vd = Vector2.Dot(velocity, normal);
            if (vd < 0f)
                velocity -= 1.7f * vd * normal;
            velocity *= 0.45f;
            ship.Body.linearVelocity = velocity;

            // Вытолкнуть над поверхностью.
            Vector2 pos = (Vector2)planet.transform.position + normal * (planet.Radius + 8f);
            transform.position = pos;

            Debug.Log($"[Landing] удар о «{planet.DisplayName}»: v={v:0}, урон={damage:0}");
        }
    }
}
