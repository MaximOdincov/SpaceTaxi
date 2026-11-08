using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.Economy;
using SpaceTaxi.Physics;

namespace SpaceTaxi.Core
{
    // Чит-панель для тестов: F9 (или `) — показать/скрыть. Самопоявляется в
    // любой сцене, если её не добавили на сцену руками (добавишь вручную —
    // правь поля в инспекторе, бустрап пропустит). Выдача кораблей работает
    // без правок FleetService: деньги выдаются и тут же списываются ценой.
    public class DebugCheats : MonoBehaviour
    {
        private bool shown;
        private string creditsField = "1000";
        private int ownedCursor;
        private const int WindowId = 4711; // панель одна — константный id окна OnGUI
        private Rect windowRect = new Rect(12f, 120f, 250f, 10f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<DebugCheats>() == null)
                new GameObject("DebugCheats").AddComponent<DebugCheats>();
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.f9Key.wasPressedThisFrame || kb.backquoteKey.wasPressedThisFrame))
                shown = !shown;
        }

        private void OnGUI()
        {
            if (!shown)
                return;
            windowRect = GUILayout.Window(WindowId, windowRect, Draw, "ЧИТЫ · F9 — скрыть");
        }

        private void Draw(int id)
        {
            var session = FindAnyObjectByType<GameSession>();
            if (session == null || session.Fleet == null || session.Wallet == null)
            {
                GUILayout.Label("Сессии нет (нужна GameScene)");
                GUI.DragWindow();
                return;
            }

            FleetService fleet = session.Fleet;
            GUILayout.Label($"Корабль: {fleet.Current?.DisplayName} · {session.Wallet.Credits} кр");

            GUILayout.BeginHorizontal();
            creditsField = GUILayout.TextField(creditsField);
            if (GUILayout.Button("ВЫДАТЬ КР", GUILayout.Width(90))
                && int.TryParse(creditsField, out int amount) && amount > 0)
                session.Wallet.Add(amount);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("ВЫДАТЬ ВСЕ КОРАБЛИ (бесплатно)"))
                GrantAllShips(session);
            if (GUILayout.Button("СЕСТЬ В СЛЕДУЮЩИЙ СВОЙ"))
                CycleOwnedShip(fleet);

            Ship ship = session.Ship;
            if (ship != null)
            {
                if (GUILayout.Button("ПОЛНЫЙ БАК + HP"))
                {
                    ship.Fuel = ship.Eff.FuelMax;
                    ship.GetComponent<HullSystem>()?.RepairAll();
                }
                if (GUILayout.Button("УБИТЬ КОРАБЛЬ (тест гибели)"))
                    ship.GetComponent<HullSystem>()?.ApplyDamage(ship.Hp);
            }

            GUI.DragWindow();
        }

        private static void GrantAllShips(GameSession session)
        {
            var fleet = session.Fleet;
            foreach (ShipConfig config in Resources.LoadAll<ShipConfig>("Ships").OrderBy(c => c.Tier))
            {
                if (config == null || config.IsRental || fleet.IsOwned(config) || config.Price <= 0)
                    continue;
                session.Wallet.Add(config.Price); // компенсация цены — покупка бесплатная
                fleet.TryBuy(config, session.Wallet, Random.Range(0, config.PaintVariantCount + 1));
            }
        }

        private void CycleOwnedShip(FleetService fleet)
        {
            var pool = new List<ShipConfig> { ShipConfig.EggRentalFallback };
            pool.AddRange(fleet.OwnedShips.Where(c => c != null && !c.IsRental));
            if (pool.Count == 0)
                return;
            ownedCursor = (ownedCursor + 1) % pool.Count;
            fleet.SwitchTo(pool[ownedCursor]);
        }
    }
}
