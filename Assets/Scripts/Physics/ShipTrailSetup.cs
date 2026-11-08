using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.Physics
{
    // Настройка шлейфов конкретной модели: живёт на корне префаба модели
    // (Assets/Prefabs/Resources/Ships/<id>). Каждая запись — отдельный шлейф:
    // точка крепления (в локальных осях визуала), ширина, цвет, длина, скорость
    // появления. Точек может быть несколько (двигатели, крылья). Превью —
    // выдели префаб: точки рисуются гизмо прямо в редакторе.
    public class ShipTrailSetup : MonoBehaviour
    {
        [System.Serializable]
        public class TrailPoint
        {
            [Tooltip("Точка крепления в локальных осях визуала модели")]
            public Vector2 offset = new Vector2(0f, -2.2f);

            [Tooltip("Ширина шлейфа в мировых юнитах")]
            [Min(0.05f)]
            public float width = 1.2f;

            [Tooltip("Цвет шлейфа (к концу гаснет сам)")]
            public Color color = new Color(0.7f, 0.88f, 1f, 0.45f);

            [Tooltip("Длина шлейфа, с (при скорости 400 — сотни юнитов)")]
            [Min(0.05f)]
            public float time = 0.38f;

            [Tooltip("Минимальная скорость, с которой шлейф появляется")]
            [Min(0f)]
            public float minSpeed = 65f;

            [Tooltip("Яркость шлейфа (множитель альфы)")]
            [Range(0f, 2f)]
            public float strength = 1f;
        }

        [Tooltip("Шлейфы модели")]
        public List<TrailPoint> trails = new List<TrailPoint> { new TrailPoint() };

        // точка по умолчанию: старый одиночный шлейф за кормой (setup нет на префабе)
        public static TrailPoint DefaultPoint()
        {
            return new TrailPoint { width = 1.2f, time = 0.38f, minSpeed = 65f };
        }

        private void OnDrawGizmosSelected()
        {
            if (trails == null)
                return;
            foreach (TrailPoint point in trails)
            {
                Vector3 world = transform.TransformPoint(new Vector3(point.offset.x, point.offset.y, 0f));
                Gizmos.color = point.color;
                Gizmos.DrawWireSphere(world, 0.35f);
                Gizmos.DrawLine(world, world + transform.up * 1.5f);
            }
        }
    }
}
