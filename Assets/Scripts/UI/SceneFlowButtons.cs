using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceTaxi.UI
{
    // Переходы между сценами и выход — мишени для onClick кнопок меню/паузы.
    // В редакторе «выход» останавливает Play Mode, в билде закрывает приложение.
    public class SceneFlowButtons : MonoBehaviour
    {
        public void StartNewGame() => Load("GameScene");

        public void ContinueGame() => Load("GameScene"); // v0.1: сейва ещё нет, тот же вход

        public void LoadMainMenu() => Load("MainMenu");

        public void QuitGame()
        {
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void Load(string scene)
        {
            Time.timeScale = 1f; // из паузы — с чистым временем
            SceneManager.LoadScene(scene);
        }
    }
}
