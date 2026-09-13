using UnityEngine;

namespace SpaceTaxi.Physics
{
    // Следование за кораблём: лерп позиции 3.5/с, зум 3/с (как в прототипе).
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Ship ship;

        private Camera cam;

        public void SetTarget(Ship target)
        {
            ship = target;
        }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (ship == null)
                ship = FindAnyObjectByType<Ship>();
        }

        private void LateUpdate()
        {
            if (ship == null)
                return;
            float dt = Time.deltaTime;

            Vector3 pos = transform.position;
            Vector2 target = ship.transform.position;
            pos.x += (target.x - pos.x) * Mathf.Min(1f, 3.5f * dt);
            pos.y += (target.y - pos.y) * Mathf.Min(1f, 3.5f * dt);
            transform.position = pos;

            // По короткой стороне экрана видно 760/zoom юнитов.
            float zoom = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 760f, 0.65f, 1.15f);
            float targetSize = Screen.height / zoom * 0.5f;
            cam.orthographicSize += (targetSize - cam.orthographicSize) * Mathf.Min(1f, 3f * dt);
        }
    }
}
