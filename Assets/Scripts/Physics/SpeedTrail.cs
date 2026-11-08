using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.Core;

namespace SpaceTaxi.Physics
{
    // Шлейфы корабля: конфигурация из ShipTrailSetup на префабе модели
    // (несколько шлейфов, своя ширина/цвет/длина/порог скорости у каждого);
    // setup нет — одиночный шлейф за кормой, как раньше. Пересобирается при
    // смене модели (ShipModelVisual.Apply → Rebuild).
    public class SpeedTrail : MonoBehaviour
    {
        private Ship ship;
        private ShipState lastState;
        private ShipModelVisual model;
        private readonly List<TrailRenderer> trails = new List<TrailRenderer>();
        private readonly List<float> minSpeeds = new List<float>();

        private void Awake()
        {
            ship = GetComponent<Ship>();
            lastState = ship != null ? ship.State : ShipState.Flying;
            model = GetComponent<ShipModelVisual>();
            Rebuild();
        }

        // перечитать шлейфы: визуал модели пересоздаётся при смене корпуса
        public void Rebuild()
        {
            foreach (TrailRenderer trail in trails)
                if (trail != null)
                    Destroy(trail.gameObject);
            trails.Clear();
            minSpeeds.Clear();

            Transform host = transform;
            ShipTrailSetup setup = null;
            if (model != null && model.HasVisual)
            {
                host = model.VisualRoot;
                setup = host.GetComponentInChildren<ShipTrailSetup>();
            }
            if (setup == null)
                setup = GetComponent<ShipTrailSetup>();

            if (setup != null && setup.trails != null && setup.trails.Count > 0)
            {
                int index = 0;
                foreach (ShipTrailSetup.TrailPoint point in setup.trails)
                {
                    trails.Add(MakeTrail(host, "Trail" + index, point));
                    minSpeeds.Add(point.minSpeed);
                    index++;
                }
            }
            else
            {
                trails.Add(MakeTrail(host, "Trail", ShipTrailSetup.DefaultPoint()));
                minSpeeds.Add(65f);
            }
        }

        private TrailRenderer MakeTrail(Transform host, string name, ShipTrailSetup.TrailPoint point)
        {
            var go = new GameObject(name);
            go.transform.SetParent(host, false);
            go.transform.localPosition = new Vector3(point.offset.x, point.offset.y, 0f);

            var trail = go.AddComponent<TrailRenderer>();
            var src = GetComponent<SpriteRenderer>();
            Shader shader = src != null && src.sharedMaterial != null
                ? src.sharedMaterial.shader
                : Shader.Find("Sprites/Default");
            Texture2D tex = StubSprites.Circle(8).texture;
            tex.wrapMode = TextureWrapMode.Repeat; // тайлится вдоль шлейфа
            trail.material = new Material(shader) { mainTexture = tex };

            trail.time = point.time;
            trail.minVertexDistance = 1.5f;
            trail.numCapVertices = 2;
            trail.textureMode = LineTextureMode.Tile;
            trail.sortingOrder = 1;
            trail.startColor = point.color;
            Color end = point.color;
            end.a = 0f;
            trail.endColor = end;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0f));
            trail.widthMultiplier = point.width;
            trail.emitting = false;
            return trail;
        }

        private void Update()
        {
            if (ship == null || ship.ShipConfig == null || ship.Body == null)
                return;

            // телепорт (посадка/респавн) не тянет шлейф через полкарты
            if (ship.State != lastState && ship.State != ShipState.Flying)
                foreach (TrailRenderer trail in trails)
                    if (trail != null)
                        trail.Clear();
            lastState = ship.State;

            float v = ship.Body.linearVelocity.magnitude;
            bool flying = ship.State == ShipState.Flying;
            for (int i = 0; i < trails.Count; i++)
            {
                if (trails[i] == null)
                    continue;
                float min = i < minSpeeds.Count ? minSpeeds[i] : 65f;
                trails[i].emitting = flying && v > min;
            }
        }
    }
}
