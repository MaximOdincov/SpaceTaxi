using UnityEngine;

namespace SpaceTaxi.Physics
{
    // Следование за кораблём: лерп позиции 3.5/с (как в прототипе).
    // Посадочный зум: с начала ретро-разворота и до взлёта камера ближе —
    // планета «растёт»; после взлёта плавно отдаляется.
    public class CameraFollow : MonoBehaviour
    {
        private const float FlightViewHeight = 320f;
        private const float LandingViewHeight = 190f;

        [SerializeField] private Ship ship;

        private Camera cam;
        private ShipController controller;

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

            if (controller == null && ship != null)
                controller = ship.GetComponent<ShipController>();
            bool landing = ship.State == ShipState.Landed
                || (controller != null && controller.LandingRetro);
            float viewHeight = landing ? LandingViewHeight : FlightViewHeight;

            float targetSize = viewHeight * 0.5f;
            cam.orthographicSize += (targetSize - cam.orthographicSize) * Mathf.Min(1f, 3f * dt);
        }
    }
}
