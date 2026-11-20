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
        private MapScreenController mapController; // карта открыта — Tab закрывает её
        // wasPressedThisFrame у Tab видят несколько компонентов в одном кадре —
        // статический флаг гарантирует, что нажатие обработано ровно один раз
        private static int tabConsumedFrame = -1;

        private void Awake()
        {
            // слайд и анимации добегают и без фокуса окна
            Application.runInBackground = true;
            SetX(hiddenX);
        }

        private void Update()
        {
            // единый потребитель Tab: при открытой карте Tab закрывает её
            // (панель прячется вместе с картой), иначе тогглит панель
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame
                && tabConsumedFrame != Time.frameCount)
            {
                tabConsumedFrame = Time.frameCount;
                if (mapController == null)
                    mapController = FindAnyObjectByType<MapScreenController>();
                if (mapController != null && mapController.IsOpened)
                    mapController.Close(); // панель прячется вместе с картой
                else
                    Toggle();
            }

            // страховка: корутина слайда могла умереть на полпути — тогда флаг
            // «открыто» и позиция разошлись, панель «пропадает». Выравниваем
            if (anim == null && panel != null)
            {
                float targetX = shown ? shownX : hiddenX;
                if (Mathf.Abs(panel.anchoredPosition.x - targetX) > 1f)
                    SetX(targetX);
            }
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
