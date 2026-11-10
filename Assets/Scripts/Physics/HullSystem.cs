using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.Audio;
using SpaceTaxi.Economy;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    // Прочность: урон от столкновений и жара, гибель и респавн
    public class HullSystem : MonoBehaviour
    {
        private Ship ship;
        private GravityField gravity;
        private LevelAsset level;
        private Vector3 initialScale = Vector3.one; // респавн откатывает сжатие дырой
        private ShipModelVisual modelVisual;
        // рендереры, спрятанные на «мёртвую» паузу до респавна: возвращаем ровно их,
        // чтобы не воскресить корневой спрайт под визуалом модели (двойной корпус)
        private readonly List<SpriteRenderer> hiddenVisuals = new List<SpriteRenderer>();
        // палитра взрыва: копия OnboardComputerFactory.DangerColor — UI-неймспейс
        // в физику не тащим, цвет уходит параметром в ShipExplosion.Spawn
        private static readonly Color DangerTint = new Color(1f, 0.32f, 0.24f);

        // лениво с кэшем: ShipModelVisual добавляется позже Awake (WorldBuilder)
        private ShipModelVisual Visual => modelVisual != null
            ? modelVisual
            : (modelVisual = GetComponent<ShipModelVisual>());

        public event Action HpChanged;
        public event Action Died;
        public event Action<string> ShipLost;
        public event Action Respawned;

        public void Init(LevelAsset levelAsset)
        {
            level = levelAsset;
        }

        private void OnShipReconfigured()
        {
            HpChanged?.Invoke();
        }

        private void Awake()
        {
            ship = GetComponent<Ship>();
            modelVisual = GetComponent<ShipModelVisual>();
            initialScale = transform.localScale;
            // пересадка на другой корпус — HP стал полным, HUD должен увидеть
            ship.Reconfigured += OnShipReconfigured;
            gravity = FindAnyObjectByType<GravityField>();
            if (level == null)
            {
                var session = FindAnyObjectByType<SpaceTaxi.Core.GameSession>();
                if (session != null && session.Level != null)
                    level = session.Level;
            }
        }

        private void Update()
        {
            if (ship == null || ship.State != ShipState.Flying)
                return;
            float worldRadius = level != null ? level.WorldRadius : 2500f;
            if (ship.transform.position.magnitude > worldRadius)
                Abandon("ПОТЕРЯНА СВЯЗЬ");
        }

        // потеря корабля без взрыва
        public void Abandon(string reason)
        {
            if (ship == null || ship.State == ShipState.Respawning)
                return;
            ShipLost?.Invoke(reason);
            ship.State = ShipState.Respawning;
            ship.Body.linearVelocity = Vector2.zero;
            StartCoroutine(RespawnAfter(0.6f));
        }

        private void FixedUpdate()
        {
            if (ship == null || ship.ShipConfig == null || ship.State != ShipState.Flying)
                return;
            ApplyHeat(Time.fixedDeltaTime);
        }

        public void ApplyDamage(float amount)
        {
            if (amount <= 0f || ship == null || ship.Hp <= 0f)
                return;
            ship.Hp = Mathf.Clamp(ship.Hp - amount, 0f, ship.Eff.HpMax);
            HpChanged?.Invoke();
            if (ship.Hp <= 0f)
                Die();
        }

        // через событие HpChanged, иначе HUD не обновится
        public void RepairAll()
        {
            if (ship == null || ship.ShipConfig == null)
                return;
            Repair(ship.Eff.HpMax);
        }

        // частичный ремонт (экран станции) — тоже через HpChanged
        public void Repair(float amount)
        {
            if (amount <= 0f || ship == null || ship.ShipConfig == null)
                return;
            ship.Hp = Mathf.Clamp(ship.Hp + amount, 0f, ship.Eff.HpMax);
            HpChanged?.Invoke();
        }

        // жар: линейно от 0 на краю зоны до максимума у поверхности
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
                ApplyDamage(k * star.HeatDamagePerSecond * dt * ship.Eff.HeatMul); // жар-защита корпуса
            }
        }

        private void Die()
        {
            if (ship.State == ShipState.Respawning)
                return;
            Died?.Invoke();
            Sound.Play(SoundId.Explosion); // дакнет остальные группы в AudioManager
            CameraFollow.AddShake(0.85f); // гибель — самый сильный толчок
            // взрыв в точке корабля до телепорта респавна: вспышка закрывает корпус,
            // сам корпус на «мёртвую» паузу прячем — видны только частицы (0.6 c)
            ShipExplosion.Spawn(transform.position, Visual != null ? Visual.HalfExtent : 0f, DangerTint);
            HideVisuals();
            ship.State = ShipState.Respawning;
            ship.Body.linearVelocity = Vector2.zero;
            StartCoroutine(RespawnAfter(0.6f)); // короткая пауза на взрыв
        }

        // спрятать корпус на время между смертью и респавном (взрыв вместо него);
        // кэшируем только включённые рендереры — выключенные сами вернутся как были
        private void HideVisuals()
        {
            hiddenVisuals.Clear();
            foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (!renderer.enabled)
                    continue;
                renderer.enabled = false;
                hiddenVisuals.Add(renderer);
            }
        }

        private void RestoreVisuals()
        {
            foreach (SpriteRenderer renderer in hiddenVisuals)
                if (renderer != null)
                    renderer.enabled = true;
            hiddenVisuals.Clear();
        }

        private System.Collections.IEnumerator RespawnAfter(float delay)
        {
            yield return new WaitForSeconds(delay);

            // планету ищем до телепорта: она же пойдёт в LandedOn
            Planet respawnPlanet = FindAnyObjectByType<GravityField>() != null ? LandedPlanet() : null;

            if (level != null && level.StartPlanetIndex >= 0 && level.StartPlanetIndex < level.Bodies.Count)
            {
                CelestialBodyData start = level.Bodies[level.StartPlanetIndex];
                float rad = level.SpawnAngleDeg * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                // радиус живой планеты, а не сырых данных: облик планеты умножен
                // на PlanetSizeMul (CelestialBody.Init), респавн от start.Radius
                // оставлял корпус хвостом в планете; отступ — как у посадки (ShipPlacement)
                Vector2 center = respawnPlanet != null ? (Vector2)respawnPlanet.transform.position : start.Position;
                float radius = respawnPlanet != null ? respawnPlanet.Radius : start.Radius;
                Vector2 pos = center + dir * (radius + ShipPlacement.LandedExtent(Visual));
                float nose = GetComponent<ShipController>() != null ? GetComponent<ShipController>().SpriteNoseAngle : 0f;
                ShipPlacement.TeleportLanded(ship, pos, level.SpawnAngleDeg - nose);
            }

            ship.Body.linearVelocity = Vector2.zero;
            ship.Hp = ship.Eff.HpMax;
            ship.Fuel = ship.Eff.FuelMax;
            transform.localScale = initialScale; // сжатие дырой не переносится на новый корпус
            RestoreVisuals(); // корпус возвращается при респавне (после гибели прятался)
            ship.LandedOn = respawnPlanet;
            ship.State = ShipState.Landed;
            HpChanged?.Invoke();
            Respawned?.Invoke();
            Sound.Play(SoundId.Respawn);
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
