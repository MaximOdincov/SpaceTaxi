using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;

namespace SpaceTaxi.Physics
{
    // Визуал модели корабля: префаб из Resources/Ships/<Id> — SpriteRenderer со
    // спрайтом покраски + пустые child-слоты ExhaustMain/ExhaustRetro, позиции
    // которых правит владелец в префабе модели (EngineEffects подхватывает).
    // Префаба нет — спрайт рисует корневой SpriteRenderer (старое поведение).
    public class ShipModelVisual : MonoBehaviour
    {
        // длина корабля в мировых юнитах (как заглушка ShipTriangle 192px = 14)
        private const float WorldLength = 14f;

        private static readonly Dictionary<string, GameObject> prefabCache =
            new Dictionary<string, GameObject>();

        private Transform visual;

        public bool HasVisual => visual != null;
        public Transform VisualRoot => visual;

        // пересоздаёт визуал под модель и покраску; валидный спрайт решает
        // ShipConfig.GetPaintSprite, размер нормализуется до WorldLength
        public void Apply(ShipConfig config, int paintIndex)
        {
            if (visual != null)
                DestroyImmediate(visual.gameObject); // смена модели стоит в паузе экрана станции
            visual = null;

            GameObject prefab = LoadPrefab(config != null ? config.Id : null);
            var rootRenderer = GetComponent<SpriteRenderer>();
            Sprite sprite = config != null ? config.GetPaintSprite(paintIndex) : null;

            if (prefab == null)
            {
                if (rootRenderer != null)
                {
                    rootRenderer.enabled = true;
                    if (sprite != null)
                        rootRenderer.sprite = sprite;
                    else if (rootRenderer.sprite == null)
                        rootRenderer.sprite = StubSprites.ShipTriangle();
                }
                GetComponent<EngineEffects>()?.Rebind();
                return;
            }

            if (rootRenderer != null)
                rootRenderer.enabled = false; // спрайт рисует визуал модели

            visual = Instantiate(prefab, transform, false).transform;
            visual.name = "ModelVisual";
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;

            var renderer = visual.GetComponentInChildren<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null)
            {
                if (sprite != null)
                    renderer.sprite = sprite;
                float dim = Mathf.Max(renderer.sprite.bounds.size.x, renderer.sprite.bounds.size.y);
                float parentScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), 0.0001f);
                if (dim > 0.0001f)
                {
                    // любая картинка → WorldLength в мире (корень масштабирует x3)
                    float s = WorldLength / (dim * parentScale);
                    visual.localScale = new Vector3(s, s, 1f);
                }
            }

            GetComponent<EngineEffects>()?.Rebind(); // слоты переехали в новый визуал
        }

        private static GameObject LoadPrefab(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (prefabCache.TryGetValue(id, out GameObject cached))
                return cached; // null тоже кэшим: префаба нет и не появится
            cached = Resources.Load<GameObject>("Ships/" + id);
            prefabCache[id] = cached;
            return cached;
        }
    }
}
