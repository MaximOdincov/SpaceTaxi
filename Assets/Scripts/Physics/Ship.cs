using System;
using UnityEngine;
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

        private float fuel = 100f;

        public float Fuel
        {
            get => fuel;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, ShipConfig != null ? ShipConfig.FuelMax : 100f);
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

            Hp = shipConfig.HpMax;
            Fuel = shipConfig.FuelMax;
        }

        // Новый корпус = полные HP и полный бак; остальное (тяга, MaxV) читается
        // из ShipConfig динамически контроллерами, респавн тоже берёт новый конфиг
        public void Reconfigure(ShipConfig shipConfig)
        {
            if (shipConfig == null)
                return;
            ShipConfig = shipConfig;
            Hp = shipConfig.HpMax;
            Fuel = shipConfig.FuelMax; // сеттер сам шлёт FuelChanged
            Reconfigured?.Invoke();
        }

        // Для HUD.
        public float HpFraction => ShipConfig != null ? Hp / ShipConfig.HpMax : 0f;
        public float FuelFraction => ShipConfig != null ? Fuel / ShipConfig.FuelMax : 0f;
    }
}
