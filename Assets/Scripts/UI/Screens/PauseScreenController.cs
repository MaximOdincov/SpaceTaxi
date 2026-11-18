using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceTaxi.UI
{
    // Пауза (ESC): timeScale = 0 + меню
    public class PauseScreenController : MonoBehaviour
    {
        [SerializeField] private GameObject pauseScreen;         // экран паузы
        [SerializeField] private OnboardComputerPanel onboard;  // панель борт.компа
        [SerializeField] private MapScreenController mapController; // чтобы не драться за ESC
        [SerializeField] private GameObject settingsUi;         // настройки в сцене

        private bool paused;
        private StationScreenController stationController; // станция тоже занимает ESC
        private OrderCompleteScreenController orderCompleteController; // экран завершения тоже

        public bool IsPaused => paused;

        private void Start()
        {
            SetPaused(false, force: true);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (stationController == null) stationController = FindAnyObjectByType<StationScreenController>();
            if (orderCompleteController == null) orderCompleteController = FindAnyObjectByType<OrderCompleteScreenController>();
            if (!kb.escapeKey.wasPressedThisFrame) return;
            // ESC занят картой
            if (mapController != null && (mapController.IsOpened || mapController.EscJustClosed)) return;
            // ESC занят станцией
            if (stationController != null && (stationController.IsOpened || stationController.EscJustClosed)) return;
            // ESC занят экраном завершения заказа: тот же кадр закрывает ЕГО,
            // пауза не открывается
            if (orderCompleteController != null && (orderCompleteController.IsOpened || orderCompleteController.EscJustClosed)) return;
            SetPaused(!paused);
        }

        public void Resume()
        {
            SetPaused(false);
        }

        public void OpenMapFromPause()
        {
            if (mapController != null) mapController.Open();
        }

        // настройки поверх, пауза остаётся
        public void OpenSettingsFromPause()
        {
            if (settingsUi == null) return;
            pauseScreen.SetActive(false);
            settingsUi.SetActive(true);
        }

        public void ReturnFromSettings()
        {
            if (settingsUi == null) return;
            settingsUi.SetActive(false);
            pauseScreen.SetActive(true);
        }

        public void NotifyMapOpened()
        {
            if (paused) pauseScreen.SetActive(false);
        }

        public void NotifyMapClosed()
        {
            if (paused) pauseScreen.SetActive(true);
        }

        // экран станции (B) прячет меню паузы, пока открыт; закрытие возвращает
        public void NotifyStationOpened()
        {
            if (paused) pauseScreen.SetActive(false);
        }

        public void NotifyStationClosed()
        {
            if (paused) pauseScreen.SetActive(true);
        }

        // экран завершения заказа (П4.3) — та же договорённость, что у станции
        public void NotifyOrderCompleteOpened()
        {
            if (paused) pauseScreen.SetActive(false);
        }

        public void NotifyOrderCompleteClosed()
        {
            if (paused) pauseScreen.SetActive(true);
        }

        private void SetPaused(bool value, bool force = false)
        {
            if (!force && value == paused) return;
            paused = value;
            pauseScreen.SetActive(paused);
            onboard.Show(paused); // пауза с борт.компом
            Time.timeScale = paused ? 0f : 1f;
        }
    }
}
