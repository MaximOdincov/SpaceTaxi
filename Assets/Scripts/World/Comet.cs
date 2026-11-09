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
        private Vector2 despawnCenter; // центр отсчёта деспавна: мир — ноль, кометная зона — её центр
        private float despawnRadius;
        private Rigidbody2D body;

        // направление полёта (единичное) — радару для хвоста маркера по вектору скорости
        public Vector2 FlightDirection => direction;

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
            Init(flightDirection, flightSpeed, Vector2.zero, worldRadius);
        }

        // вариант для кометной зоны (П7.2): маршрут не покидает окружность зоны,
        // деспавн — при выходе из неё
        public void Init(Vector2 flightDirection, float flightSpeed, Vector2 despawnCenter, float despawnRadius)
        {
            direction = flightDirection.normalized;
            speed = flightSpeed;
            this.despawnCenter = despawnCenter;
            this.despawnRadius = despawnRadius + DespawnMargin;
        }

        private void FixedUpdate()
        {
            body.MovePosition(body.position + direction * (speed * Time.fixedDeltaTime));

            // вылетела из зоны/системы — убираем, чтобы не копить мёртвые объекты
            if ((body.position - despawnCenter).sqrMagnitude > despawnRadius * despawnRadius)
                Destroy(gameObject);
        }
    }
}
