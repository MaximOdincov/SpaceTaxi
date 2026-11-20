using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Events;

namespace SpaceTaxi.UI
{
    // Самоспавн контроллера-модалки: своя подписка sceneLoaded на каждый T
    // (снимается при перезагрузке домена), объект создаётся, если его ещё нет
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

    // База модальных экранов: открытие ставит timeScale = 0, закрытие любым
    // путём возвращает время (страховка в OnDisable). ESC/Enter закрывают
    // с гвардом кадра открытия (нажатие кадра посадки не закрывает экран),
    // EscJustClosed — флаг для паузы: «ESC только что закрыл этот экран»,
    // пауза в том же кадре не открывается. Анимации — по unscaledTime
    public abstract class ModalScreenControllerBase : MonoBehaviour
    {
        private const float CloseGuardSeconds = 0.25f; // экран не закрывается в кадр открытия

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
            // гвард: экран ещё не успел отрисоваться
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

        // каркас строится при первом показе
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
