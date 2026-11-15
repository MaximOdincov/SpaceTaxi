using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Реле отключения борта в захвате чёрной дыры (v5, 2026-11-15): фуллскрин-
    // оверлей «ЧЁРНАЯ ДЫРА ЗАТЯГИВАЕТ» убран — владелец попросил предупреждения
    // «как с жаром, но не на весь экран»: они теперь в HUD-баннере (HudBinder.
    // UpdateWarning: текст захвата с отсчётом и подсказкой X). Здесь осталась
    // только клавиша X — RequestAbandon в захвате дыры. Пока открыты карта/
    // борткомп/станция/пауза — X не срабатывает (как раньше: панели клавишу
    // не едят, но отключать борт из-под панели нечестно). Контроллера в сценах
    // нет — самоспавн (стиль OrderCompleteScreenController), переживает смену
    // сцены без событий.
    public class BlackHoleOverlayController : MonoBehaviour
    {
        private BlackHole hole;
        private MapScreenController map;
        private OnboardComputerPanel onboard;
        private StationScreenController station;
        private PauseScreenController pause;

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
            if (hole == null || !hole.SwallowActive || AnyPanelOpen())
                return;

            var kb = Keyboard.current;
            if (kb != null && kb.xKey.wasPressedThisFrame)
                hole.RequestAbandon();
        }

        // при открытых панелях X не ловим — сами они клавишу X не едят
        private bool AnyPanelOpen()
        {
            if (map == null) map = FindAnyObjectByType<MapScreenController>();
            if (onboard == null) onboard = FindAnyObjectByType<OnboardComputerPanel>();
            if (station == null) station = FindAnyObjectByType<StationScreenController>();
            if (pause == null) pause = FindAnyObjectByType<PauseScreenController>();
            return (map != null && map.IsOpened)
                || (onboard != null && onboard.IsShown)
                || (station != null && station.IsOpened)
                || (pause != null && pause.IsPaused);
        }
    }
}
