using UnityEngine;

namespace SpaceTaxi.UI
{
    // Спидометр: диск + стрелка вокруг цифрового значения. Арт меняется в
    // префабе Assets/Prefabs/Resources/UI/Speedometer.prefab (Dial/Needle) —
    // при пустых спрайтах HUD подставляет процедурные заглушки.
    public class SpeedometerGauge : MonoBehaviour
    {
        [SerializeField] private RectTransform needle;

        public void Setup(RectTransform needleRect)
        {
            needle = needleRect;
        }

        // f: 0..1 доля от максимальной скорости модели
        public void SetFraction(float fraction)
        {
            if (needle != null)
                needle.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-120f, 120f, Mathf.Clamp01(fraction)));
        }
    }
}
