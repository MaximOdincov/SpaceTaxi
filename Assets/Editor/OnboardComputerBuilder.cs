using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceTaxi.Tools
{
    // Перестройка бортового компьютера В РЕДАКТОРЕ: каркас сериализуется
    // в сцену и дальше правится руками — Play его не трогает, только данные.
    public static class OnboardComputerBuilder
    {
        [MenuItem("Space Taxi/Rebuild Onboard Computer")]
        public static void Rebuild()
        {
            var binder = Object.FindAnyObjectByType<SpaceTaxi.UI.OnboardBinder>();
            if (binder == null)
            {
                Debug.LogWarning("[OnboardComputerBuilder] В открытой сцене нет OnboardComputer.");
                return;
            }

            // Снос старых детей с Undo, построение нового каркаса.
            var panel = binder.transform;
            for (int i = panel.childCount - 1; i >= 0; i--)
            {
                string n = panel.GetChild(i).name;
                if (n != "Frame" && n != "Background")
                    Undo.DestroyObjectImmediate(panel.GetChild(i).gameObject);
            }

            var refs = SpaceTaxi.UI.OnboardComputerFactory.Build(panel, destroyImmediate: true);
            // Зарегистрировать созданные объекты для Undo.
            foreach (Transform child in panel)
                Undo.RegisterCreatedObjectUndo(child.gameObject, "Rebuild Onboard Computer");

            EditorUtility.SetDirty(binder.gameObject);
            EditorSceneManager.MarkSceneDirty(binder.gameObject.scene);
            Debug.Log("[OnboardComputerBuilder] каркас построен: " + refs.Clock.text);
        }
    }
}
