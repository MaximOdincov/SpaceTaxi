using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.Core;
using SpaceTaxi.Physics;

namespace SpaceTaxi.World
{
    // Процедурный параллакс-фон из звёзд: три слоя глубины. Корень слоя
    // держится на камере (без тряски — см. CameraFollow.AnchorPosition),
    // звёзды внутри тайла дрейфуют с долей скорости камеры (дальние
    // медленнее) и заворачиваются по краям — бесконечное поле без швов.
    // Глазу наконец есть опора: корабль «летит», а не висит на месте.
    [DefaultExecutionOrder(50)] // LateUpdate после CameraFollow — камера уже встала
    public class StarfieldParallax : MonoBehaviour
    {
        private class Layer
        {
            public Transform Root;
            public Vector2[] Seeds;
            public Transform[] Stars;
            public float Factor;
        }

        private readonly List<Layer> layers = new List<Layer>();
        private float tileX = 1400f;
        private float tileY = 950f;

        private static readonly Color[] Tints =
        {
            new Color(1f, 1f, 1f),
            new Color(0.78f, 0.86f, 1f),
            new Color(1f, 0.93f, 0.82f),
        };

        private void Awake()
        {
            var cam = GetComponent<Camera>();
            float aspect = cam != null ? cam.aspect : 1.78f;

            // Тайл с запасом под самый широкий зум и монитор: звезда,
            // завернувшаяся за краем, не должна вспыхивать на экране.
            tileX = Mathf.Max(1300f, 2f * 250f * aspect + 400f);
            tileY = Mathf.Max(950f, 2f * 250f + 450f);

            Build(0.12f, 90, 0.28f, 0.38f); // дальняя пыль
            Build(0.35f, 60, 0.48f, 0.52f); // средний слой
            Build(0.65f, 36, 0.78f, 0.68f); // ближние звёзды
        }

        private void Build(float factor, int count, float baseScale, float alpha)
        {
            var rootGo = new GameObject($"Stars_{factor:0.00}");
            var layer = new Layer
            {
                Root = rootGo.transform,
                Factor = factor,
                Seeds = new Vector2[count],
                Stars = new Transform[count],
            };

            Sprite sprite = StubSprites.Circle(6);
            for (int i = 0; i < count; i++)
            {
                var starGo = new GameObject("Star");
                starGo.transform.SetParent(layer.Root, false);
                var r = starGo.AddComponent<SpriteRenderer>();
                r.sprite = sprite;
                r.sortingOrder = -10;
                Color c = Tints[Random.Range(0, Tints.Length)];
                c.a = alpha * Random.Range(0.6f, 1f);
                r.color = c;
                float s = baseScale * Random.Range(0.7f, 1.5f);
                starGo.transform.localScale = new Vector3(s, s, 1f);
                layer.Seeds[i] = new Vector2(
                    Random.Range(-0.5f, 0.5f) * tileX,
                    Random.Range(-0.5f, 0.5f) * tileY);
                layer.Stars[i] = starGo.transform;
            }
            layers.Add(layer);
        }

        private void LateUpdate()
        {
            // Якорь — камера без тряски: дрожь ударов должна сдвигать мир
            // относительно фона, иначе тряску за звёздами не видно.
            var follow = CameraFollow.Instance;
            Vector3 anchor = follow != null
                ? follow.AnchorPosition
                : transform.position;

            foreach (Layer layer in layers)
            {
                // z = 0 обязательно: у якоря z камеры (−10) — слои попали бы
                // в плоскость самой камеры и отсекались near-плоскостью.
                layer.Root.position = new Vector3(anchor.x, anchor.y, 0f);
                Vector2 drift = (Vector2)anchor * layer.Factor;
                for (int i = 0; i < layer.Stars.Length; i++)
                    layer.Stars[i].localPosition = Wrap(layer.Seeds[i] - drift);
            }
        }

        // Заворачивание в тайльный прямоугольник [-tile/2, tile/2].
        private Vector2 Wrap(Vector2 v)
        {
            return new Vector2(WrapAxis(v.x, tileX), WrapAxis(v.y, tileY));
        }

        private static float WrapAxis(float value, float tile)
        {
            return (value % tile + tile) % tile - tile * 0.5f;
        }
    }
}
