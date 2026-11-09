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
        // длина модели без ShipConfig (как заглушка ShipTriangle 192px = 14)
        private const float FallbackLength = 35f;
        // капсула по габаритам корпуса (высота ~0.96 длины, ширина по контенту):
        // круг-коллайдер давал контакт «в воздухе» у носа и утопание хвоста в планете;
        // публично — посадочный отступ ShipPlacement считается от неё же
        public const float CapsuleHeightFraction = 0.96f;
        private const float CapsuleWidthFraction = 0.9f;
        // половина высоты контента без визуала — прежний снап посадки/спавна
        private const float FallbackHalfExtent = 17.5f;

        private static readonly Dictionary<string, GameObject> prefabCache =
            new Dictionary<string, GameObject>();

        private Transform visual;
        private float contentHeight; // высота контента в юнитах корня (после масштаба визуала)

        public bool HasVisual => visual != null;
        public Transform VisualRoot => visual;

        // половина высоты контента в мире: корабли нарисованы носом вверх, при
        // посадке нос от планеты и корма касается поверхности; визуала нет —
        // прежний снап (фолбэк на корневой SpriteRenderer)
        public float HalfExtent => visual != null
            ? contentHeight * 0.5f * Mathf.Abs(transform.lossyScale.y)
            : FallbackHalfExtent;

        // пересоздаёт визуал под модель и покраску; валидный спрайт решает
        // ShipConfig.GetPaintSprite, контент арта (не канва) растягивается
        // до ShipConfig.VisualLength
        public void Apply(ShipConfig config, int paintIndex)
        {
            // сносим ВСЕ ModelVisual, а не только tracked: после смерти/респавна
            // двойной визуал = наложенные корпуса (баг «корабли накладываются»)
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child != null && child.name == "ModelVisual")
                    Destroy(child.gameObject); // DestroyImmediate запрещён в физика-коллбеках смерти
            }
            visual = null;
            contentHeight = 0f;

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
                // масштаб по контенту арта (альфа-границы): прозрачные поля канвы
                // раздували бы корабль, а смещённый контент уводил корму от посадки
                Rect content = SpriteArt.ContentRect(renderer.sprite);
                float dim = Mathf.Max(content.width, content.height);
                float parentScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), 0.0001f);
                if (dim > 0.0001f)
                {
                    float length = config != null ? config.VisualLength : FallbackLength;
                    float s = length / (dim * parentScale);
                    visual.localScale = new Vector3(s, s, 1f);
                    // центр контента на оси корабля (localPosition в осях корня,
                    // родительский масштаб применится сам)
                    visual.localPosition = -(content.center * s);
                    contentHeight = content.height * s;

                    var circle = GetComponent<CircleCollider2D>();
                    if (circle != null)
                        circle.enabled = false; // корпус длинный — круг бьёт мимо силуэта
                    var capsule = GetComponent<CapsuleCollider2D>();
                    if (capsule == null)
                        capsule = gameObject.AddComponent<CapsuleCollider2D>();
                    capsule.direction = CapsuleDirection2D.Vertical;
                    capsule.offset = Vector2.zero;
                    // габариты в локальных осях корня (родительский масштаб применится сам)
                    capsule.size = new Vector2(content.width * s * CapsuleWidthFraction / parentScale,
                                               content.height * s * CapsuleHeightFraction / parentScale);
                }
            }

            GetComponent<EngineEffects>()?.Rebind(); // слоты переехали в новый визуал
            GetComponent<SpeedTrail>()?.Rebuild(); // шлейфы — по setup новой модели
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
