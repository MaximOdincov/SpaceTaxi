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

        public bool IsOpened => opened;

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
