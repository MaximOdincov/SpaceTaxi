using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceTaxi.UI
{
    // Карта (M): открыть/закрыть. Сама по себе НЕ ставит паузу (решение 2026-09-20).
    // Открытие/закрытие — вместе с борт.компом. Если карта открыта из паузы —
    // пауза сохраняется, закрытие карты возвращает меню паузы.
    // ESC в карте закрывает её (не в паузу), если карта была открыта не из паузы.
    public class MapScreenController : MonoBehaviour
    {
        [SerializeField] private GameObject mapScreen;            // сам экран карты
        [SerializeField] private OnboardComputerPanel onboard;   // панель борт.компа
        [SerializeField] private PauseScreenController pause;    // пауза (карта из паузы её не снимает)

        private bool opened;

        public bool IsOpened => opened;

        // ESC только что закрыл карту (кадр-guard, чтобы пауза не открылась тем же нажатием).
        public bool EscJustClosed { get; private set; }

        private void Start()
        {
            SetOpened(false, force: true);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.mKey.wasPressedThisFrame)
                SetOpened(!opened);
            else if (opened && kb.escapeKey.wasPressedThisFrame)
            {
                SetOpened(false);
                EscJustClosed = true;
            }
        }

        private void LateUpdate()
        {
            EscJustClosed = false;
        }

        // Для кнопки «КАРТА» в меню паузы: открыть карту, паузу не трогая.
        public void Open()
        {
            SetOpened(true);
        }

        private void SetOpened(bool value, bool force = false)
        {
            if (!force && value == opened) return;
            opened = value;
            mapScreen.SetActive(opened);
            onboard.Show(opened); // карта и борт.комп живут вместе

            if (pause != null)
            {
                if (opened) pause.NotifyMapOpened();
                else pause.NotifyMapClosed();
            }
        }
    }
}
