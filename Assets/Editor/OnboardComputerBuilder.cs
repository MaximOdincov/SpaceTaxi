using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceTaxi.Tools
{
    // Перестройка бортового компьютера в редакторе: каркас сериализуется в сцену
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

            // снос старых детей с Undo
            var panel = binder.transform;
            for (int i = panel.childCount - 1; i >= 0; i--)
            {
                string n = panel.GetChild(i).name;
                if (n != "Frame" && n != "Background")
                    Undo.DestroyObjectImmediate(panel.GetChild(i).gameObject);
            }

            var refs = SpaceTaxi.UI.OnboardComputerFactory.Build(panel, destroyImmediate: true);
            // зарегистрировать созданное для Undo
            foreach (Transform child in panel)
                Undo.RegisterCreatedObjectUndo(child.gameObject, "Rebuild Onboard Computer");

            EditorUtility.SetDirty(binder.gameObject);
            EditorSceneManager.MarkSceneDirty(binder.gameObject.scene);
            Debug.Log("[OnboardComputerBuilder] каркас построен: " + refs.Clock.text);
        }

        // без Canvas layout-группы не применяются
        private static void RebuildLayout(RectTransform root)
        {
            var canvasGo = new GameObject("TempCanvas", typeof(Canvas));
            root.SetParent(canvasGo.transform, false);
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            root.SetParent(null, false);
            Object.DestroyImmediate(canvasGo);
        }

        // бейк текущих карточек фабрики — превью в префаб-режиме; в рантайме
        // биндер строит карточки кодом и префабы не читает
        [MenuItem("Space Taxi/Create Order UI Prefabs")]
        public static void CreateOrderPrefabs()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI"))
                AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

            var card = SpaceTaxi.UI.OnboardComputerFactory.BuildOfferCard();
            card.transform.SetParent(null, false);
            RebuildLayout(card.transform as RectTransform);
            PrefabUtility.SaveAsPrefabAsset(card, "Assets/Prefabs/UI/OfferCard.prefab");
            Object.DestroyImmediate(card);

            var block = SpaceTaxi.UI.OnboardComputerFactory.BuildActiveOrderBlock();
            block.transform.SetParent(null, false);
            RebuildLayout(block.transform as RectTransform);
            PrefabUtility.SaveAsPrefabAsset(block, "Assets/Prefabs/UI/ActiveOrder.prefab");
            Object.DestroyImmediate(block);

            AssetDatabase.SaveAssets();
            Debug.Log("[OnboardComputerBuilder] префабы карточек созданы");
        }
    }
}
