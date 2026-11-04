using UnityEngine;

namespace SpaceTaxi.Economy
{
    // Параметры модели корабля: тяга, манёвр, бак, HP, цена.
    // Общие константы полёта остаются в PhysicsConfig
    [CreateAssetMenu(fileName = "ShipConfig", menuName = "Space Taxi/Ship Config")]
    public sealed class ShipConfig : ScriptableObject
    {
        [Header("Модель")]
        public string Id;
        public string DisplayName;

        [Tooltip("Тир корабля: 1–5")]
        [Range(1, 5)]
        public int Tier = 2;

        [Header("Полёт")]
        public float Thrust;    // тяга W — разгон вдоль вектора скорости
        public float StrafeAcc; // боковая тяга A/D
        public float MaxV;      // потолок скорости от собственной тяги

        [Header("Бак и корпус")]
        public float FuelMax;
        public float HpMax;

        [Tooltip("Спрайт модели (арт А1/А2, 512×512 PPU 32, нос вправо); пусто — заглушка")]
        public Sprite ShipSprite;

        [Header("Магазин")]
        public int Price;     // 0 = не продаётся
        public bool IsRental; // арендный — всегда сток
        [Tooltip("Одна строка для карточки магазина")]
        public string Description;

        private static ShipConfig eggRentalFallback;

        // Фолбэк на арендное «Яйцо», если ShipConfig в GameSession отключён
        public static ShipConfig EggRentalFallback
        {
            get
            {
                if (eggRentalFallback != null)
                    return eggRentalFallback;
                Debug.Log("[Ships] ShipConfig не подключён — фолбэк на арендное «Яйцо»");
                eggRentalFallback = CreateInstance<ShipConfig>();
                eggRentalFallback.Id = "egg_rental";
                eggRentalFallback.DisplayName = "Яйцо";
                eggRentalFallback.Tier = 2;
                eggRentalFallback.Thrust = 95f;
                eggRentalFallback.StrafeAcc = 85f;
                eggRentalFallback.MaxV = 330f;
                eggRentalFallback.FuelMax = 100f;
                eggRentalFallback.HpMax = 100f;
                eggRentalFallback.Price = 0;
                eggRentalFallback.IsRental = true;
                eggRentalFallback.Description = "Арендный корабль компании, всегда сток";
                return eggRentalFallback;
            }
        }
    }
}
