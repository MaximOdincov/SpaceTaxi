using UnityEngine;

namespace SpaceTaxi.Physics
{
    // Тюнинг выхлопа конкретной модели: живёт на корне префаба модели
    // (Assets/Prefabs/Resources/Ships/<id>). Множители применяются к дефолтному
    // пламени EngineEffects; авторские PS на слотах не трогаются — их крути
    // прямо в инспекторе ParticleSystem.
    public class ExhaustTune : MonoBehaviour
    {
        [Tooltip("Размер частиц пламени")]
        [Range(0.2f, 4f)]
        public float FlameSize = 1f;

        [Tooltip("Плотность эмиссии пламени")]
        [Range(0.2f, 4f)]
        public float FlamePower = 1f;

        [Tooltip("Ширина Trail-шлейфа")]
        [Range(0f, 3f)]
        public float TrailWidth = 1f;

        [Tooltip("Плотность искр разгона")]
        [Range(0f, 3f)]
        public float SparkPower = 1f;
    }
}
