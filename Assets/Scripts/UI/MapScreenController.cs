using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceTaxi.UI
{
    public class MapScreenController : MonoBehaviour
    {
        [SerializeField] private GameObject mapScreen;
        [SerializeField] private OnboardComputerPanel onboard;
        [SerializeField] private PauseScreenController pause;

        private bool opened;
        private StationScreenController stationController; // станция поверх карты — её клавиши
        private OrderCompleteScreenController orderComplete; // модал завершения заказа
        private MapScreenBinder binder; // зум/записка карты едят Esc раньше закрытия карты

        public bool IsOpened => opened;

        public bool EscJustClosed { get; private set; }

        private void Start()
        {
            SetOpened(false, force: true);
        }

        private void Update()
        {
            if (stationController == null) stationController = FindAnyObjectByType<StationScreenController>();
            if (stationController != null && stationController.IsOpened)
                return; // станция открыта — M/Esc принадлежат ей
            if (orderComplete == null) orderComplete = FindAnyObjectByType<OrderCompleteScreenController>();
            if (orderComplete != null && orderComplete.IsOpened)
                return; // экран завершения модален — M/Esc ему не подчиняются
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.mKey.wasPressedThisFrame)
                SetOpened(!opened);
            else if (opened && kb.escapeKey.wasPressedThisFrame)
            {
                if (binder == null && mapScreen != null)
                    binder = mapScreen.GetComponentInChildren<MapScreenBinder>(true);
                if (binder != null && binder.TryHandleEscape())
                    return; // Esc вернул зум/закрыл записку — карта остаётся открытой
                SetOpened(false);
                EscJustClosed = true;
            }
        }

        private void LateUpdate()
        {
            EscJustClosed = false;
        }

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
