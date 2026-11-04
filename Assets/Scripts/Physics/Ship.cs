using System;
using UnityEngine;
using SpaceTaxi.Core; // StubSprites
using SpaceTaxi.Economy;

namespace SpaceTaxi.Physics
{
    public enum ShipState
    {
        Flying = 0,
        Landed = 1,
        Respawning = 2,
    }

    [RequireComponent(typeof(Rigidbody2D))]
    public class Ship : MonoBehaviour
    {
        public ShipState State = ShipState.Flying;
        public float Hp = 100f;

        public PhysicsConfig Config { get; private set; }
        public ShipConfig ShipConfig { get; private set; }
        public Rigidbody2D Body { get; private set; }
        public World.Planet LandedOn { get; set; }

        // уровни улучшений [Engine, Tank, Hull, Maneuver], 0..3; 0 = сток
        public int[] UpgradeLevels { get; private set; }

        // эффективные статы (база + улучшения) — единственный источник чисел
        // для полёта и корпуса; пересчитывается в Init/Reconfigure/BindUpgradeConfig
        public EffectiveStats Eff { get; private set; }

        private UpgradeConfig upgradeConfig;

        private float fuel = 100f;

        public float Fuel
        {
            get => fuel;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, ShipConfig != null ? Eff.FuelMax : 100f);
                if (Mathf.Approximately(clamped, fuel))
                    return;
                fuel = clamped;
                FuelChanged?.Invoke();
            }
        }

        public event Action FuelChanged;

        // смена корпуса на живом корабле (FleetService) — HullSystem разошлёт HpChanged
        public event Action Reconfigured;

        public void Init(PhysicsConfig config, ShipConfig shipConfig)
        {
            Config = config;
            ShipConfig = shipConfig;
            Body = GetComponent<Rigidbody2D>();
            Body.gravityScale = 0f; // притяжение считает GravityField

            UpgradeLevels = ShipStats.NormalizeLevels(null);
            RecomputeEff();
            Hp = Eff.HpMax;
            Fuel = Eff.FuelMax;
        }

        // конфиг улучшений привязывается после постройки корабля (FleetService.BindShip)
        public void BindUpgradeConfig(UpgradeConfig config)
        {
            if (config == null || config == upgradeConfig)
                return;
            upgradeConfig = config;
            RecomputeEff();
        }

        // стоковый корпус (старый вызов): уровни обнуляются
        public void Reconfigure(ShipConfig shipConfig)
        {
            Reconfigure(shipConfig, null);
        }

        // Новый корпус = полные HP и полный бак (refill); остальное (тяга, MaxV)
        // читается из Eff динамически контроллерами, респавн тоже берёт новые статы.
        // refill = false — пересчёт статов без лечения/заправки: покупка улучшения
        // на стоящий корабль не должна быть халявным сервисом станции
        public void Reconfigure(ShipConfig shipConfig, int[] upgradeLevels, bool refill = true)
        {
            if (shipConfig == null)
                return;
            ShipConfig = shipConfig;
            UpgradeLevels = ShipStats.NormalizeLevels(upgradeLevels);
            RecomputeEff();
            ApplyModelSprite(shipConfig);
            // Полные HP/бак только живому корпусу: при гибели Reconfigure
            // вызывается до респавна, и мёртвый корабль не должен «ожить»
            // на месте гибели (иначе Crash усыплёт его посадкой)
            if (refill && Hp > 0f)
            {
                Hp = Eff.HpMax;
                Fuel = Eff.FuelMax; // сеттер сам шлёт FuelChanged
            }
            Reconfigured?.Invoke();
        }

        // спрайт модели (арт А1/А2): своя — ставим, нет — заглушка, чужой
        // корпус оставлять нельзя (пересадка должна быть видна)
        private void ApplyModelSprite(ShipConfig shipConfig)
        {
            var spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
                return;
            spriteRenderer.sprite = shipConfig.ShipSprite != null
                ? shipConfig.ShipSprite
                : StubSprites.ShipTriangle();
        }

        private void RecomputeEff()
        {
            Eff = ShipStats.Compute(Config, ShipConfig, UpgradeLevels, upgradeConfig);
        }

        // Для HUD.
        public float HpFraction => Eff.HpMax > 0f ? Hp / Eff.HpMax : 0f;
        public float FuelFraction => Eff.FuelMax > 0f ? Fuel / Eff.FuelMax : 0f;
    }
}
