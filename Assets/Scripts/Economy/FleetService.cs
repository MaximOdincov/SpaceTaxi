using System;
using System.Collections.Generic;
using SpaceTaxi.Physics;

namespace SpaceTaxi.Economy
{
    // Парк кораблей: владение купленными моделями, аренда «Яйца» как фолбэк.
    // Владелец — GameSession (создаётся в Awake); UI магазина подключается в П3.2,
    // долг за потерю аренды — в П2.2
    public sealed class FleetService
    {
        private readonly List<ShipConfig> owned = new List<ShipConfig>();
        private Ship ship; // живой корабль для мгновенной пересадки

        // всегда валиден: старт новой игры — арендное «Яйцо»
        public ShipConfig Current { get; private set; }

        public IReadOnlyList<ShipConfig> OwnedShips => owned;

        // смена текущего корабля — для UI (П3+)
        public event Action ShipChanged;

        public FleetService()
        {
            // поле currentShip сцены не читаем: новая игра всегда с аренды
            Current = ShipConfig.EggRentalFallback;
        }

        // без подписок здесь — на гибель/потерю подписывается GameSession
        public void BindShip(Ship ship)
        {
            this.ship = ship;
        }

        public bool IsOwned(ShipConfig config)
        {
            return config != null && owned.Contains(config);
        }

        // Покупка: только не-арендные (Price > 0) и ещё не купленные.
        // После оплаты — сразу пересадка на купленный: новый корпус = полные
        // HP и бак, применяется мгновенно (корабль стоит на станции)
        public bool TryBuy(ShipConfig config, Wallet wallet)
        {
            if (config == null || config.IsRental || config.Price <= 0 || IsOwned(config))
                return false;
            if (wallet == null || !wallet.TrySpend(config.Price))
                return false;

            owned.Add(config);
            SetCurrent(config);
            return true;
        }

        // Потеря корабля: свой — списывается вместе с улучшениями (учёт
        // улучшений добавит П2.3), арендный — просто остаёмся на аренде
        public void NotifyShipLost()
        {
            owned.Remove(Current);
            SetCurrent(ShipConfig.EggRentalFallback);
        }

        private void SetCurrent(ShipConfig config)
        {
            Current = config;
            if (ship != null)
                ship.Reconfigure(config);
            ShipChanged?.Invoke();
        }
    }
}
