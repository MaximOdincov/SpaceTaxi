using UnityEngine;

namespace SpaceTaxi.UI
{
    // Спидометр: полукруглая шкала speed.png поверх стрелки arrow.png; стрелка
    // вращается вокруг центра шкалы, цифры — TMP внутри шкалы. Каркас и углы
    // задаёт HudBinder при Setup.
    public class SpeedometerGauge : MonoBehaviour
    {
        private RectTransform needle;
        private float angleAtZero;
        private float sweepDegrees;

        // angleAtZero — локальный поворот стрелки при скорости 0 (остриё на
        // отметке «0» шкалы), sweepDegrees — полный ход стрелки до MaxV.
        // Ход по часовой (в Unity +z — против часовой, поэтому вычитаем).
        public void Setup(RectTransform needleRect, float angleAtZero, float sweepDegrees)
        {
            needle = needleRect;
            this.angleAtZero = angleAtZero;
            this.sweepDegrees = sweepDegrees;
        }

        // f: 0..1 доля от максимальной скорости модели
        public void SetFraction(float fraction)
        {
            if (needle != null)
                needle.localRotation = Quaternion.Euler(0f, 0f,
                    angleAtZero - sweepDegrees * Mathf.Clamp01(fraction));
        }
    }
}
