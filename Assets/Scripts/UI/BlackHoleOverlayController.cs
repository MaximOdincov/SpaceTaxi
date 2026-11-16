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
            // Н9.11 (владелец: «нажимаю X — ничего не происходит; надо отключать»):
            // гейт открытых панелей снят — панели клавишу X не едят, а борт под
            // открытой картой глушить честно. Заглушенный борт X не глушит
            if (hole == null || !hole.SwallowActive || hole.PoweredDown)
                return;

            var kb = Keyboard.current;
            if (kb != null && kb.xKey.wasPressedThisFrame)
                hole.RequestAbandon();
        }
    }
}
