using System;
using UnityEngine;

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
        public Rigidbody2D Body { get; private set; }
        public World.Planet LandedOn { get; set; }

        private float fuel = 100f;

        /// <summary>Топливо 0..FuelMax. Событие — для HUD.</summary>
        public float Fuel
        {
            get => fuel;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, Config != null ? Config.FuelMax : 100f);
                if (Mathf.Approximately(clamped, fuel))
                    return;
                fuel = clamped;
                FuelChanged?.Invoke();
            }
        }

        public event Action FuelChanged;

        public void Init(PhysicsConfig config)
        {
            Config = config;
            Body = GetComponent<Rigidbody2D>();
            Body.gravityScale = 0f; // притяжение считает GravityField

            Hp = config.HpMax;
            Fuel = config.FuelMax;
        }

        // Для HUD.
        public float HpFraction => Config != null ? Hp / Config.HpMax : 0f;
        public float FuelFraction => Config != null ? Fuel / Config.FuelMax : 0f;
    }
}
