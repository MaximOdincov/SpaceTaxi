using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceTaxi.Core;

namespace SpaceTaxi.UI
{
    public class SceneFlowButtons : MonoBehaviour
    {
        public void StartNewGame() => Load("GameScene");

        // продолжение (П10.1): флаг ставится ДО загрузки сцены — GameSession.Start
        // применит сейв после сборки мира; новая игра флаг не ставит и сейв
        // игнорирует (но не удаляет)
        public void ContinueGame()
        {
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
