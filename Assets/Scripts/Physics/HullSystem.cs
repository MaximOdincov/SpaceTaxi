using System;
using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    // Прочность: урон от столкновений и жара, гибель и респавн.
    // События HpChanged/Died/Respawned — задел под HUD.
    public class HullSystem : MonoBehaviour
    {
        private Ship ship;
        private GravityField gravity;
        private LevelAsset level;

        /// <summary>Дробное HP 0..1 — для HUD.</summary>
        public event Action HpChanged;

        /// <summary>Корабль погиб (взрыв).</summary>
        public event Action Died;

        /// <summary>Респавн завершён.</summary>
        public event Action Respawned;

        public void Init(LevelAsset levelAsset)
        {
            level = levelAsset;
        }

        private void Awake()
        {
            ship = GetComponent<Ship>();
            gravity = FindAnyObjectByType<GravityField>();
            if (level == null)
            {
                var session = FindAnyObjectByType<SpaceTaxi.Core.GameSession>();
                if (session != null && session.Level != null)
                    level = session.Level;
            }
        }

        private void FixedUpdate()
        {
            if (ship == null || ship.Config == null || ship.State != ShipState.Flying)
                return;
            ApplyHeat(Time.fixedDeltaTime);
        }

        public void ApplyDamage(float amount)
        {
            if (amount <= 0f || ship == null || ship.Hp <= 0f)
                return;
            ship.Hp = Mathf.Clamp(ship.Hp - amount, 0f, ship.Config.HpMax);
            HpChanged?.Invoke();
            if (ship.Hp <= 0f)
                Die();
        }

        // Жар звёзд: линейно от 0 на краю зоны до максимума у поверхности.
        private void ApplyHeat(float dt)
        {
            if (gravity == null)
                return;
            Vector2 pos = ship.transform.position;
            foreach (CelestialBody body in gravity.Bodies)
            {
                if (!(body is Star star) || star.HeatRadius <= 0f)
                    continue;
                float d = Vector2.Distance(star.transform.position, pos);
                if (d >= star.HeatRadius)
                    continue;
                float k = Mathf.Clamp01(1f - (d - star.Radius) / (star.HeatRadius - star.Radius));
                ApplyDamage(k * star.HeatDamagePerSecond * dt);
            }
        }

        private void Die()
        {
            if (ship.State == ShipState.Respawning)
                return;
            Died?.Invoke();
            ship.State = ShipState.Respawning;
            ship.Body.linearVelocity = Vector2.zero;
            StartCoroutine(RespawnAfter(0.6f)); // короткая пауза на взрыв
        }

        private System.Collections.IEnumerator RespawnAfter(float delay)
        {
            yield return new WaitForSeconds(delay);

            if (level != null && level.StartPlanetIndex >= 0 && level.StartPlanetIndex < level.Bodies.Count)
            {
                CelestialBodyData start = level.Bodies[level.StartPlanetIndex];
                float rad = level.SpawnAngleDeg * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                transform.position = start.Position + dir * (start.Radius + 7f);
                float nose = GetComponent<ShipController>() != null ? GetComponent<ShipController>().SpriteNoseAngle : 0f;
                transform.rotation = Quaternion.Euler(0f, 0f, level.SpawnAngleDeg - nose);
            }

            ship.Body.linearVelocity = Vector2.zero;
            ship.Hp = ship.Config.HpMax;
            ship.LandedOn = FindAnyObjectByType<GravityField>() != null ? LandedPlanet() : null;
            ship.State = ShipState.Landed;
            HpChanged?.Invoke();
            Respawned?.Invoke();
        }

        private Planet LandedPlanet()
        {
            if (level == null || level.StartPlanetIndex >= level.Bodies.Count)
                return null;
            foreach (Planet planet in FindObjectsByType<Planet>(FindObjectsSortMode.None))
                if (planet.name == level.Bodies[level.StartPlanetIndex].DisplayName)
                    return planet;
            return null;
        }
    }
}
