using UnityEngine;
using SpaceTaxi.Core;

namespace SpaceTaxi.Physics
{
    // Скоростной шлейф за кораблём: включается только на скорости, толщина
    // растёт с ней — дешёвый источник ощущения «насколько быстро летим»
    // даже в чистом поле между планетами.
    public class SpeedTrail : MonoBehaviour
    {
        private const float MinSpeed = 65f;   // ниже — шлейф неуместен
        private const float TrailTime = 0.38f;

        private Ship ship;
        private TrailRenderer trail;
        private ShipState lastState;

        private void Awake()
        {
            ship = GetComponent<Ship>();
            lastState = ship != null ? ship.State : ShipState.Flying;

            trail = gameObject.AddComponent<TrailRenderer>();

            // Мягкая полоса из подручного круглого спрайта.
            var src = GetComponent<SpriteRenderer>();
            Shader shader = src != null && src.sharedMaterial != null
                ? src.sharedMaterial.shader
                : Shader.Find("Sprites/Default");
            Texture2D tex = StubSprites.Circle(8).texture;
            tex.wrapMode = TextureWrapMode.Repeat; // тайлится вдоль шлейфа
            trail.material = new Material(shader) { mainTexture = tex };

            trail.time = TrailTime;
            trail.minVertexDistance = 1.5f;
            trail.numCapVertices = 2;
            trail.textureMode = LineTextureMode.Tile;
            trail.sortingOrder = 1;
            trail.startColor = new Color(0.7f, 0.88f, 1f, 0.45f);
            trail.endColor = new Color(0.55f, 0.8f, 1f, 0f);
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0f));
            trail.widthMultiplier = 1.2f;
            trail.emitting = false;
        }

        private void Update()
        {
            if (ship == null || ship.Config == null || ship.Body == null)
                return;

            // Телепорты (посадка/респавн) не должны тянуть шлейф через полкарты.
            if (ship.State != lastState && ship.State != ShipState.Flying)
                trail.Clear();
            lastState = ship.State;

            float v = ship.Body.linearVelocity.magnitude;
            bool fast = ship.State == ShipState.Flying && v > MinSpeed;
            trail.emitting = fast;
            if (fast)
                trail.widthMultiplier = Mathf.Lerp(0.5f, 2.6f, Mathf.Clamp01(v / ship.Config.MaxV));
        }
    }
}
