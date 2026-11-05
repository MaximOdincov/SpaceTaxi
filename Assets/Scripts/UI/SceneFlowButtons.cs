using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceTaxi.Core;

namespace SpaceTaxi.UI
{
    public class SceneFlowButtons : MonoBehaviour
    {
        // затемнение неактивного «Продолжить» (§3.9, П10.2): фон кнопки гасит
        // DisabledColor-переход, CanvasGroup добавно гасит и подпись
        private const float DisabledAlpha = 0.4f;

        public void StartNewGame() => Load("GameScene");

        // продолжение (П10.1): флаг ставится ДО загрузки сцены — GameSession.Start
        // применит сейв после сборки мира; новая игра флаг не ставит и сейв
        // игнорирует (но не удаляет). Сейва нет — no-op: страховка, кнопка
        // и так погашена
        public void ContinueGame()
        {
            if (!SaveSystem.HasSave())
                return;
            SaveSystem.MarkContinue();
            Load("GameScene");
        }

        public void LoadMainMenu()
        {
            SaveIfAlive(); // автосейв §3.9: выход в меню
            Load("MainMenu");
        }

        public void QuitGame()
        {
            SaveIfAlive(); // автосейв §3.9: выход из игры
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // «Продолжить» активно только при наличии сейва (§3.9). Меню входит
        // полной загрузкой сцены, так что OnEnable срабатывает на каждый показ;
        // состояние после увольнения (DeleteSave на экране увольнения)
        // подхватывается само
        private void OnEnable()
        {
            var go = GameObject.Find("Button_Continue"); // бинд по имени; в GameScene её нет
            if (go == null || !go.TryGetComponent<Button>(out var button))
                return;

            bool hasSave = SaveSystem.HasSave();
            button.interactable = hasSave;

            // у сценических кнопок CanvasGroup нет — добавляем свой, чужой не трогаем
            var dim = go.GetComponent<CanvasGroup>();
            if (dim == null)
                dim = go.AddComponent<CanvasGroup>();
            dim.alpha = hasSave ? 1f : DisabledAlpha;
        }

        // сейвим только живую сессию (в меню её нет); увольнённый забег Save
        // не перезапишет — терминальное состояние (3 аварии) он пропускает
        private static void SaveIfAlive()
        {
            var session = FindAnyObjectByType<GameSession>();
            if (session != null)
                SaveSystem.Save(session);
        }

        private static void Load(string scene)
        {
            Time.timeScale = 1f; // из паузы — с чистым временем
            SceneManager.LoadScene(scene);
        }
    }
}
