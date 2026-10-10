using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceTaxi.UI
{
    // Бортовой компьютер: TAB — выезд панели слева
    public class OnboardComputerPanel : MonoBehaviour
    {
        [SerializeField] private RectTransform panel;   // сама панель (480x1080)
        [SerializeField] private float hiddenX = -510f; // полностью за левым краем
        [SerializeField] private float shownX = 0f;
        [SerializeField] private float duration = 0.25f;

        private bool shown;
        private Coroutine anim;

        private void Awake()
        {
            SetX(hiddenX); // старт скрыт
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
                Toggle();
        }

        public void Toggle()
        {
            shown = !shown;
            if (anim != null) StopCoroutine(anim);
            anim = StartCoroutine(Slide(shown ? shownX : hiddenX));
        }

        public bool IsShown => shown;

        // программное открытие/закрытие (например, из карты или паузы)
        public void Show(bool value)
        {
            if (value != shown) Toggle();
        }

        // ease-out cubic: быстро выезжает, плавно докатывается
        private IEnumerator Slide(float targetX)
        {
            float fromX = panel.anchoredPosition.x;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / duration);
                float eased = 1f - Mathf.Pow(1f - k, 3f);
                panel.anchoredPosition = new Vector2(Mathf.LerpUnclamped(fromX, targetX, eased), panel.anchoredPosition.y);
                yield return null;
            }
            panel.anchoredPosition = new Vector2(targetX, panel.anchoredPosition.y);
            anim = null;
        }

        private void SetX(float x)
        {
            if (panel != null)
                panel.anchoredPosition = new Vector2(x, panel.anchoredPosition.y);
        }
    }
}
