using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceTaxi.Economy;

namespace SpaceTaxi.EditorTools
{
    // Перелинковывает пользовательский арт кораблей: ShipConfig (ShipSprite +
    // PaintVariants) и префабы моделей (Assets/Prefabs/Resources/Ships) — на
    // спрайты из Assets/Resources/Art/ships/<модель>. Нужен после замены арта
    // или ручного YAML: fileID спрайт-подобъекта Unity генерирует сам, руками
    // его не угадать (fileID 2800000 — это Texture2D, не Sprite).
    // Меню «Space Taxi/Fix Ship Model Prefab Sprites».
    public static class ShipPrefabSpriteFix
    {
        // папки моделей → Id ShipConfig (как в tools/build_ship_prefabs.py)
        private static readonly Dictionary<string, string> FolderToId = new Dictionary<string, string>
        {
            { "барон", "barin" },
            { "дельта", "delta" },
            { "игла", "igla" },
            { "кабан", "kaban" },
            { "меридиан", "meridian" },
            { "скрепка", "skrepka" },
            { "стрела", "strela" },
            { "яйцо", "egg_rental" }, // egg_own получает тот же набор ниже
        };

        [MenuItem("Space Taxi/Fix Ship Model Prefab Sprites")]
        public static void Fix()
        {
            Dictionary<string, List<Sprite>> folders = LoadFolders();
            int configsFixed = 0, prefabsFixed = 0;

            foreach (ShipConfig config in Resources.LoadAll<ShipConfig>("Ships"))
            {
                if (config == null || string.IsNullOrEmpty(config.Id))
                    continue;
                string folder = FolderToId.FirstOrDefault(p => p.Value == config.Id).Key;
                if (config.Id == "egg_own")
                    folder = "яйцо";
                if (folder == null || !folders.TryGetValue(folder, out List<Sprite> sprites))
                {
                    Debug.LogWarning("[ShipPrefabFix] нет папки спрайтов для " + config.Id);
                    continue;
                }

                string configPath = AssetDatabase.GetAssetPath(config);
                var configObject = new SerializedObject(AssetDatabase.LoadAssetAtPath<ShipConfig>(configPath));
                configObject.FindProperty("ShipSprite").objectReferenceValue = sprites[0];
                SerializedProperty variants = configObject.FindProperty("PaintVariants");
                variants.arraySize = sprites.Count - 1;
                for (int i = 1; i < sprites.Count; i++)
                    variants.GetArrayElementAtIndex(i - 1).objectReferenceValue = sprites[i];
                configObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(config);
                configsFixed++;

                string prefabPath = "Assets/Prefabs/Resources/Ships/" + config.Id + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning("[ShipPrefabFix] нет префаба: " + prefabPath);
                    continue;
                }
                var renderer = prefab.GetComponentInChildren<SpriteRenderer>();
                if (renderer == null)
                {
                    Debug.LogWarning("[ShipPrefabFix] нет SpriteRenderer: " + prefabPath);
                    continue;
                }
                var serialized = new SerializedObject(renderer);
                serialized.FindProperty("m_Sprite").objectReferenceValue = sprites[0];
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(prefab);
                prefabsFixed++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[ShipPrefabFix] конфигов: {configsFixed}, префабов: {prefabsFixed}");
        }

        // спрайты по папкам моделей, порядок по номеру в имени файла
        private static Dictionary<string, List<Sprite>> LoadFolders()
        {
            var folders = new Dictionary<string, List<Sprite>>();
            string root = "Assets/Resources/Art/ships";
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
                folder = folder.Substring(folder.LastIndexOf('/') + 1).Normalize(); // macOS: NFD (й = и+бревис)
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                    continue;
                if (!folders.TryGetValue(folder, out List<Sprite> list))
                    folders[folder] = list = new List<Sprite>();
                list.Add(sprite);
            }
            foreach (List<Sprite> list in folders.Values)
                list.Sort((a, b) => NumberIn(a.name).CompareTo(NumberIn(b.name)));
            return folders;
        }

        private static int NumberIn(string name)
        {
            var digits = new string(name.Where(char.IsDigit).ToArray());
            return digits.Length > 0 ? int.Parse(digits) : 0;
        }
    }
}
