using System.Collections.Generic; // список кораблей для переключения
using System.Linq; // сортировка ассетов по тиру
using UnityEngine; // GUI/OnGUI и типы Unity
using UnityEngine.InputSystem; // клавиатура для хоткея F9
using SpaceTaxi.Economy; // кошелёк/парк
using SpaceTaxi.Physics; // корабль и корпус

namespace SpaceTaxi.Core
{
    // Чит-панель для тестов: F9 (или `) — показать/скрыть. Самопоявляется в
    // любой сцене, если её не добавили на сцену руками (добавишь вручную —
    // правь поля в инспекторе, бустрап пропустит). Выдача кораблей работает
    // без правок FleetService: деньги выдаются и тут же списываются ценой.
    public class DebugCheats : MonoBehaviour // чит-слой отделён от ядра игры
    {
        private bool shown; // видна ли панель сейчас
        private string creditsField = "1000"; // текст поля выдачи денег
        private string ratingField = "5.0"; // текст поля установки рейтинга
        private int ownedCursor; // индекс своего корабля для переключения по кругу
        private const int WindowId = 4711; // панель одна — константный id окна OnGUI
        private Rect windowRect = new Rect(12f, 120f, 250f, 10f); // позиция окна (растягивается по контенту)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] // автоспавн после загрузки сцены
        private static void Bootstrap() // панель сама появляется в любой сцене
        {
            if (FindAnyObjectByType<DebugCheats>() == null) // нет на сцене — создаём
                new GameObject("DebugCheats").AddComponent<DebugCheats>(); // новый объект с компонентом
        }

        private void Update() // ловим хоткей панели
        {
            var kb = Keyboard.current; // главная клавиатура
            if (kb != null && (kb.f9Key.wasPressedThisFrame || kb.backquoteKey.wasPressedThisFrame)) // F9 или `
                shown = !shown; // показать/скрыть
        }

        private void OnGUI() // отрисовка окна читов
        {
            if (!shown) // панель скрыта — не рисуем
                return;
            windowRect = GUILayout.Window(WindowId, windowRect, Draw, "ЧИТЫ · F9 — скрыть"); // окно с контентом
        }

        private void Draw(int id) // содержимое окна
        {
            var session = FindAnyObjectByType<GameSession>(); // сессия ищется каждый кадр (допустимо для читов)
            if (session == null || session.Fleet == null || session.Wallet == null) // не в игровой сцене
            {
                GUILayout.Label("Сессии нет (нужна GameScene)"); // пояснение вместо кнопок
                GUI.DragWindow(); // окно всё равно можно таскать
                return;
            }

            FleetService fleet = session.Fleet; // парк кораблей
            GUILayout.Label($"Корабль: {fleet.Current?.DisplayName} · {UI.StationUI.Currency} {UI.OnboardBinder.FormatCredits(session.Wallet.Credits)}"); // статус

            GUILayout.BeginHorizontal(); // строка выдачи денег
            creditsField = GUILayout.TextField(creditsField); // поле суммы
            if (GUILayout.Button("ВЫДАТЬ Đ", GUILayout.Width(90)) // кнопка
                && int.TryParse(creditsField, out int amount) && amount > 0) // сумма корректна
                session.Wallet.Add(amount); // начислить кредиты
            GUILayout.EndHorizontal(); // конец строки

            // D2: гейты районов и шанс «жирных» заказов читают рейтинг — ручка
            // для приёмки прогрессии без прокачки вручную
            GUILayout.BeginHorizontal(); // строка рейтинга
            GUILayout.Label($"★ {session.Rating?.Value.ToString("0.0") ?? "—"}", GUILayout.Width(40)); // текущее значение
            ratingField = GUILayout.TextField(ratingField); // поле значения
            if (GUILayout.Button("РЕЙТИНГ", GUILayout.Width(90)) // кнопка установки
                && float.TryParse(ratingField, System.Globalization.NumberStyles.Float, // парсинг с точкой
                    System.Globalization.CultureInfo.InvariantCulture, out float ratingValue)) // инвариантная культура
                session.Rating?.Restore(Mathf.Clamp(ratingValue, 0f, 5f)); // установить с клампом 0..5
            GUILayout.EndHorizontal(); // конец строки

            if (GUILayout.Button("ВЫДАТЬ ВСЕ КОРАБЛИ (бесплатно)")) // массовая выдача парка
                GrantAllShips(session); // покупка каждого через обычный TryBuy
            if (GUILayout.Button("СЕСТЬ В СЛЕДУЮЩИЙ СВОЙ")) // пересадка по кругу
                CycleOwnedShip(fleet); // следующий свой корабль

            Ship ship = session.Ship; // текущий борт
            if (ship != null) // корабль есть в сцене
            {
                if (GUILayout.Button("ПОЛНЫЙ БАК + HP")) // ремонт и заправка
                {
                    ship.Fuel = ship.Eff.FuelMax; // бак на максимум
                    ship.GetComponent<HullSystem>()?.RepairAll(); // корпус на максимум
                }
                if (GUILayout.Button("УБИТЬ КОРАБЛЬ (тест гибели)")) // проверка гибели
                    ship.GetComponent<HullSystem>()?.ApplyDamage(ship.Hp); // урон = весь HP
            }

            GUI.DragWindow(); // перетаскивание окна
        }

        private static void GrantAllShips(GameSession session) // купить все модели парка
        {
            var fleet = session.Fleet; // парк
            foreach (ShipConfig config in Resources.LoadAll<ShipConfig>("Ships").OrderBy(c => c.Tier)) // все ассеты по тиру
            {
                if (config == null || config.IsRental || fleet.IsOwned(config) || config.Price <= 0) // пропустить аренду/уже своё/бесплатное
                    continue;
                session.Wallet.Add(config.Price); // компенсация цены — покупка бесплатная
                fleet.TryBuy(config, session.Wallet, Random.Range(0, config.PaintVariantCount + 1)); // покупка со случайной покраской
            }
        }

        private void CycleOwnedShip(FleetService fleet) // пересадка на следующий свой
        {
            var pool = new List<ShipConfig> { ShipConfig.EggRentalFallback }; // аренда тоже в круге
            pool.AddRange(fleet.OwnedShips.Where(c => c != null && !c.IsRental)); // плюс все свои
            if (pool.Count == 0) // пересаживаться не на что
                return;
            ownedCursor = (ownedCursor + 1) % pool.Count; // следующий по кругу
            fleet.SwitchTo(pool[ownedCursor]); // пересесть
        }
    }
}
