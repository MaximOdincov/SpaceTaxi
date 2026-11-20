using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceTaxi.Core;

namespace SpaceTaxi.UI
{
    public class SceneFlowButtons : MonoBehaviour
    {
        // альфа неактивной кнопки «Продолжить»: гасим и фон, и подпись
        private const float DisabledAlpha = 0.4f;

        public void StartNewGame() => Load("GameScene");

        // флаг ставится ДО загрузки сцены: GameSession применит сейв после
        // сборки мира; новой игрой сейв игнорируется, но не удаляется
        public void ContinueGame()
        {
            if (!SaveSystem.HasSave())
                return;
            SaveSystem.MarkContinue();
            Load("GameScene");
        }

        public void LoadMainMenu()
        {
            SaveIfAlive(); // автосейв при выходе в меню
            Load("MainMenu");
        }

        public void QuitGame()
        {
            SaveIfAlive(); // автосейв при выходе из игры
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // «Продолжить» активно только при наличии сейва; меню каждый раз
        // грузится целиком, так что OnEnable хватает каждый показ
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

        // сейвим только живую сессию; терминальное состояние (уволен) Save пропускает
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
