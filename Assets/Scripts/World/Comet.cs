using UnityEngine;

namespace SpaceTaxi.World
{
    // Маркер кометы: линейный полёт по хорде, урон считает принимающая сторона (LandingSystem);
    // не тело гравитации — в GravityField не регистрируется
    public class Comet : MonoBehaviour
    {
        public static readonly Color CometColor = new Color(0.78f, 0.93f, 1f); // яркий голубовато-белый

        private const float DespawnMargin = 200f; // умирает за границей мира + 200

        private Vector2 direction;
        private float speed;
        private float despawnRadius;
        private Rigidbody2D body;

        // кинематик с Continuous: на 400 ед/с статический коллайдер при просадках
        // FPS туннелирует сквозь корабль — «гибель при контакте» нарушается
        private void Awake()
        {
            body = gameObject.AddComponent<Rigidbody2D>();
            body.isKinematic = true;
            body.gravityScale = 0f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        public void Init(Vector2 flightDirection, float flightSpeed, float worldRadius)
        {
            direction = flightDirection.normalized;
            speed = flightSpeed;
            despawnRadius = worldRadius + DespawnMargin;
        }

        private void FixedUpdate()
        {
            body.MovePosition(body.position + direction * (speed * Time.fixedDeltaTime));

            // вылетела из системы — убираем, чтобы не копить мёртвые объекты
            if (body.position.sqrMagnitude > despawnRadius * despawnRadius)
                Destroy(gameObject);
        }
    }
}
