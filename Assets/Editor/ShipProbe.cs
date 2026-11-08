using UnityEditor;
using UnityEngine;
using SpaceTaxi.Economy;
using SpaceTaxi.Physics;

namespace SpaceTaxi.EditorTools
{
    // Диагностика посадки корпуса: edit-mode обход всех моделей — инстанс
    // корабельного префаба, Apply каждой модели, замер мировых габаритов
    // визуала: смещение центра от корня и зазор кормы относительно HalfExtent.
    // Меню «Space Taxi/Ship Probe», CLI: -executeMethod ...ShipProbe.Run
    public static class ShipProbe
    {
        [MenuItem("Space Taxi/Ship Probe")]
        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Ship.prefab");
            if (prefab == null)
            {
                Debug.LogError("[ShipProbe] нет Assets/Prefabs/Ship.prefab");
                return;
            }
            var go = Object.Instantiate(prefab);
            go.name = "ShipProbe";
            var visual = go.GetComponent<ShipModelVisual>();
            if (visual == null)
                visual = go.AddComponent<ShipModelVisual>();

            var configs = Resources.LoadAll<ShipConfig>("Ships");
            System.Array.Sort(configs, (a, b) => a.Id.CompareTo(b.Id));
            foreach (ShipConfig config in configs)
            {
                if (config == null || string.IsNullOrEmpty(config.Id))
                    continue;
                visual.Apply(config, 0);
                var sr = visual.VisualRoot != null
                    ? visual.VisualRoot.GetComponentInChildren<SpriteRenderer>()
                    : null;
                if (sr == null || sr.sprite == null)
                {
                    Debug.LogWarning($"[ShipProbe] {config.Id}: визуала/спрайта нет");
                    continue;
                }
                Bounds b = sr.bounds;
                Vector2 centerOffset = (Vector2)(b.center - go.transform.position);
                Rect cr = SpaceTaxi.Core.SpriteArt.ContentRect(sr.sprite);
                Debug.Log($"[ShipProbe]   sprite={sr.sprite.name} tex={sr.sprite.texture.width}x{sr.sprite.texture.height} " +
                          $"ppu={sr.sprite.pixelsPerUnit:0.0} texRect={sr.sprite.textureRect} contentRect={cr} " +
                          $"visualLocalPos={visual.VisualRoot.localPosition} visualScale={visual.VisualRoot.localScale}");
                //Extent вниз от центра: фактический (по bounds) и расчётный (HalfExtent)
                float actualExtent = b.center.y - b.min.y;
                Debug.Log($"[ShipProbe] {config.Id}: len={config.VisualLength:0.#} " +
                          $"centerOffset=({centerOffset.x:F2},{centerOffset.y:F2}) " +
                          $"size=({b.size.x:F1}x{b.size.y:F1}) " +
                          $"downExtent actual={actualExtent:F2} HalfExtent={visual.HalfExtent:F2}");
            }
            Object.DestroyImmediate(go);
            EditorApplication.Exit(0);
        }
    }
}
