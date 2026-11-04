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

        public void Init(Vector2 flightDirection, float flightSpeed, float worldRadius)
        {
            direction = flightDirection.normalized;
            speed = flightSpeed;
            despawnRadius = worldRadius + DespawnMargin;
        }

        private void Update()
        {
            transform.position += (Vector3)(direction * (speed * Time.deltaTime));

            // вылетела из системы — убираем, чтобы не копить мёртвые объекты
            if (((Vector2)transform.position).sqrMagnitude > despawnRadius * despawnRadius)
                Destroy(gameObject);
        }
    }
}
