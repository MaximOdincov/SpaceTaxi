using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceTaxi.UI
{
    // Пауза (ESC): timeScale=0 + затемнение + борт.комп слева + меню справа (10_UI_DESIGN.md §7).
    // Вход в карту из паузы паузу НЕ снимает: экран паузы прячется, время стоит,
    // закрытие карты возвращает меню паузы (решение 2026-09-20).
    public class PauseScreenController : MonoBehaviour
    {
        [SerializeField] private GameObject pauseScreen;             // экран паузы
        [SerializeField] private OnboardComputerPanel onboard;      // панель борт.компа
        [SerializeField] private MapScreenController mapController; // чтобы не драться за ESC
        [SerializeField] private GameObject settingsUi;             // инстанс префаба настроек в сцене

        private bool paused;

        public bool IsPaused => paused;

        private void Start()
        {
            SetPaused(false, force: true);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (!kb.escapeKey.wasPressedThisFrame) return;
            // ESC занят: карта открыта или только что закрылась этим нажатием
            if (mapController != null && (mapController.IsOpened || mapController.EscJustClosed)) return;
            SetPaused(!paused);
        }

        // Кнопка «ПРОДОЛЖИТЬ».
        public void Resume()
        {
            SetPaused(false);
        }

        // Кнопка «КАРТА»: открыть карту, оставаясь на паузе.
        public void OpenMapFromPause()
        {
            if (mapController != null) mapController.Open();
        }

        // Кнопка «НАСТРОЙКИ» из паузы: спрятать меню паузы, показать настройки (пауза остаётся).
        public void OpenSettingsFromPause()
        {
            if (settingsUi == null) return;
            pauseScreen.SetActive(false);
            settingsUi.SetActive(true);
        }

        // Кнопка «НАЗАД» настроек, открытых из паузы: вернуть меню паузы.
        public void ReturnFromSettings()
        {
            if (settingsUi == null) return;
            settingsUi.SetActive(false);
            pauseScreen.SetActive(true);
        }

        // Карта открылась: если мы на паузе — спрятать меню паузы (пауза остаётся).
        public void NotifyMapOpened()
        {
            if (paused) pauseScreen.SetActive(false);
        }

        // Карта закрылась: если мы на паузе — вернуть меню паузы.
        public void NotifyMapClosed()
        {
            if (paused) pauseScreen.SetActive(true);
        }

        private void SetPaused(bool value, bool force = false)
        {
            if (!force && value == paused) return;
            paused = value;
            pauseScreen.SetActive(paused);
            onboard.Show(paused); // пауза с борт.компом; продолжить — панель уходит
            Time.timeScale = paused ? 0f : 1f;
        }
    }
}
