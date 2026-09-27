using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceTaxi.UI
{
    public class SceneFlowButtons : MonoBehaviour
    {
        public void StartNewGame() => Load("GameScene");

        public void ContinueGame() => Load("GameScene");

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
