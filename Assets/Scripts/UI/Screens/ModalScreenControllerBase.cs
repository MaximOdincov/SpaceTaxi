using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Events;

namespace SpaceTaxi.UI
{
    // Помощник Bootstrap-каркаса модалок (Н10.13, Слой 2 — CODE_REVIEW_N9 §4
    // «дубли каркаса: Bootstrap/OnSceneLoaded/Spawn в 4 модалках»): дженерик
    // держит СВОЙ статический обработчик sceneLoaded на каждый T — старая
    // подписка снимается так же, как прежде (домен редактора может не
    // перезагружаться между запусками); контроллер появляется сам, если его
    // ещё нет
    public static class ModalBootstrap<T> where T : MonoBehaviour
    {
        private static UnityAction<Scene, LoadSceneMode> handler;

        public static void Ensure()
        {
            if (handler == null)
            {
                handler = (_, __) => Spawn();
                SceneManager.sceneLoaded -= handler; // снять подписку прошлой загрузки домена
                SceneManager.sceneLoaded += handler;
            }
            Spawn();
        }

        private static void Spawn()
        {
            if (Object.FindAnyObjectByType<T>() != null)
                return;
            new GameObject(typeof(T).Name).AddComponent<T>();
        }
    }

    // Общий каркас модального экрана (Н10.13): открытие ставит мир на паузу
    // (timeScale = 0), закрытие ЛЮБЫМ путём возвращает (OnDisable-страховка);
    // ESC/Enter закрывают с гвардом кадра открытия (нажатие кадра посадки не
    // должно закрыть экран в тот же кадр — Н-9) и флагом EscJustClosed —
    // договорённость карты/станции: пауза под модалкой не открывается.
    // Анимации внутри — по unscaledTime: модал живёт при timeScale = 0
    public abstract class ModalScreenControllerBase : MonoBehaviour
    {
        private const float CloseGuardSeconds = 0.25f; // кадр открытия не закрывает экран

        protected bool opened;
        protected GameObject screen;
        protected float openedAtRealtime;

        public bool IsOpened => opened;

        public bool EscJustClosed { get; private set; }

        // ESC закрывает и ставит флаг (пауза его читает); Enter/нумпад — просто закрывают
        protected bool HandleModalKeys()
        {
            var kb = Keyboard.current;
            if (kb == null)
                return false;
            // нажатие кадра открытия не закрывает экран — он не успел
            // отрисоваться, владелец видит «только тост»
            if (Time.unscaledTime - openedAtRealtime < CloseGuardSeconds)
                return false;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                Close();
                EscJustClosed = true;
                return true;
            }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                Close();
                return true;
            }
            return false;
        }

        // каркас строится при первом показе (панель — кодом, паттерн StationScreen П3.1)
        protected void EnsureScreen()
        {
            if (screen == null)
                screen = BuildScreen();
        }

        protected void ActivateModal()
        {
            screen.SetActive(true);
            opened = true;
            openedAtRealtime = Time.unscaledTime;
            Time.timeScale = 0f; // мир ждёт закрытия
        }

        // закрытие ЛЮБЫМ путём возвращает мир; OnClosed — свои хвосты (уведомить паузу и т.п.)
        protected void Close()
        {
            if (!opened)
                return;
            opened = false;
            if (screen != null)
                screen.SetActive(false);
            Time.timeScale = 1f; // гарантия: любой путь закрытия возвращает мир
            OnClosed();
        }

        // страховка: экран сорвало со сцены — мир не должен остаться на паузе
        protected virtual void OnDisable()
        {
            if (opened)
                Time.timeScale = 1f;
        }

        private void LateUpdate()
        {
            EscJustClosed = false;
        }

        protected abstract GameObject BuildScreen();

        protected virtual void OnClosed() { }
    }
}
