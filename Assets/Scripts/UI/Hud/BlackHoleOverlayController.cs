using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Отключение борта клавишей X в захвате чёрной дыры (RequestAbandon);
    // предупреждение о захвате живёт в HUD-баннере HudBinder. Контроллера
    // в сценах нет — самоспавн, переживает смену сцены без событий.
    public class BlackHoleOverlayController : MonoBehaviour
    {
        private BlackHole hole;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // домен редактора может не перезагружаться между запусками — снимаем старую подписку
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Spawn();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Spawn();
        }

        private static void Spawn()
        {
            if (FindAnyObjectByType<BlackHoleOverlayController>() != null)
                return;
            new GameObject("BlackHoleOverlayController").AddComponent<BlackHoleOverlayController>();
        }

        private void Update()
        {
            if (hole == null)
                hole = FindAnyObjectByType<BlackHole>();
            // панели клавишу X не едят — работает и поверх открытой карты;
            // уже заглушенный борт повторно не глушим
            if (hole == null || !hole.SwallowActive || hole.PoweredDown)
                return;

            var kb = Keyboard.current;
            if (kb != null && kb.xKey.wasPressedThisFrame)
                hole.RequestAbandon();
        }
    }
}
