using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.TextCore.LowLevel;
using SpaceTaxi.UI;

namespace SpaceTaxi.Core.Tools
{
    // TMP-ассет кодом создать нельзя — одно нажатие владельцем:
    // «Space Taxi/Fonts/Create TMP Asset» печёт Excalifont SDF в Assets/Fonts/TMP/
    // (перезапуск не плодит новый GUID) и сразу назначает его MapScreenBinder открытых сцен.
    public static class FontAssetTool
    {
        private const string Folder = "Assets/Fonts/TMP";
        private const string AssetPath = Folder + "/Excalifont SDF.asset";

        [MenuItem("Space Taxi/Fonts/Create TMP Asset")]
        public static void CreateExcalifontAsset()
        {
            string[] found = AssetDatabase.FindAssets("Excalifont t:Font");
            if (found == null || found.Length == 0)
            {
                EditorUtility.DisplayDialog("Excalifont не найден",
                    "Положи Excalifont.ttf в Assets/Fonts/ (импорт по умолчанию) и запусти меню ещё раз.",
                    "Понятно");
                Debug.LogError("[FontAssetTool] Excalifont.ttf не найден в Assets — см. диалог");
                return;
            }
            string fontPath = AssetDatabase.GUIDToAssetPath(found[0]);
            Font font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null)
            {
                Debug.LogError("[FontAssetTool] не загрузился Font из " + fontPath);
                return;
            }

            TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
            if (asset == null)
            {
                if (!AssetDatabase.IsValidFolder(Folder))
                    AssetDatabase.CreateFolder("Assets/Fonts", "TMP");

                // динамический атлас: сразу после меню шрифт печатает кириллицу без запекания
                asset = TMP_FontAsset.CreateFontAsset(font, 72, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                    AtlasPopulationMode.Dynamic);
                asset.name = "Excalifont SDF";
                if (asset.atlasTexture != null)
                    asset.atlasTexture.name = asset.name + " Atlas";
                if (asset.material != null)
                    asset.material.name = asset.name + " Material";

                AssetDatabase.CreateAsset(asset, AssetPath);
                if (asset.atlasTexture != null)
                    AssetDatabase.AddObjectToAsset(asset.atlasTexture, AssetPath);
                if (asset.material != null)
                    AssetDatabase.AddObjectToAsset(asset.material, AssetPath);
            }

            AddFallback(asset, "JetBrainsMono-Regular SDF"); // кириллица точно есть

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool assigned = AssignToMapBinders(asset);
            Debug.Log("[FontAssetTool] " + AssetPath + (assigned
                ? " назначен MapScreenBinder открытых сцен"
                : " готов; MapScreenBinder в открытых сценах не найден — открой GameScene и повтори"));
            EditorGUIUtility.PingObject(asset);
        }

        // у динамического ассета список фолбэков может быть null
        private static void AddFallback(TMP_FontAsset asset, string fallbackName)
        {
            TMP_FontAsset fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Folder + "/" + fallbackName + ".asset");
            if (fallback == null || fallback == asset)
                return;
            if (asset.fallbackFontAssetTable == null)
                asset.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (!asset.fallbackFontAssetTable.Contains(fallback))
                asset.fallbackFontAssetTable.Add(fallback);
        }

        private static bool AssignToMapBinders(TMP_FontAsset asset)
        {
            bool assigned = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (MapScreenBinder binder in Object.FindObjectsByType<MapScreenBinder>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var so = new SerializedObject(binder);
                    SerializedProperty prop = so.FindProperty("mapFont");
                    if (prop == null)
                        continue;
                    prop.objectReferenceValue = asset;
                    so.ApplyModifiedProperties();
                    EditorSceneManager.MarkSceneDirty(scene);
                    assigned = true;
                }
            }
            if (assigned)
                EditorSceneManager.SaveOpenScenes();
            return assigned;
        }
    }
}
