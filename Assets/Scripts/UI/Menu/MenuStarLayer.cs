using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.UI
{
    // Один слой звёздного фона меню: рассыпает звёзды по своей площади и мерцает
    // ими (MenuTwinkle). Строится контроллером MenuBackdropController кодом.
    public sealed class MenuStarLayer : MonoBehaviour
    {
        public struct Config
        {
            public int Count;
            public float SizeMin, SizeMax;      // px на канвасе
            public float AlphaMin, AlphaMax;    // базовая альфа звезды
            public float TwinkleAmp;            // размах мерцания
            public float SpeedMin, SpeedMax;    // скорость мерцания
            public float SparkleChance;         // доля звёзд с лучами-бликом
            public float SoftCore;              // доля радиуса с полной альфой (0 — туманность, 0.45 — звезда)
            public float SwayAmplitude;         // px автономного дрейфа
            public float SwaySpeed;             // рад/с автономного дрейфа
            public float PhaseShift;            // сдвиг фазы мерцания слоя — слои сверкают в противофазе
            public float DriftAmplitude;        // px медленного «плавания» слоя
            public float DriftSpeed;            // рад/с медленного плавания (период ~минута)
            public float DriftAngleDeg;         // направление плавания, градусы (у слоёв встречные)
            public Color[] Palette;
        }

        private readonly List<MenuTwinkle.Star> stars = new();
        private Config config;
        private bool built;

        public float SwayAmplitude => config.SwayAmplitude;
        public float SwaySpeed => config.SwaySpeed;
        public float DriftAmplitude => config.DriftAmplitude;
        public float DriftSpeed => config.DriftSpeed;
        public float DriftAngleRad => config.DriftAngleDeg * Mathf.Deg2Rad;

        public void Configure(Config value) => config = value;

        // Рассыпать звёзды; вызывается контроллером, когда канвас уже имеет
        // размер (на первом кадре layout ещё нулевой — см. MenuBackdropController.Update)
        public void Build()
        {
            if (built)
                return;
            built = true;

            var rect = (RectTransform)transform;
            float halfWidth = rect.rect.width * 0.5f * 0.94f;
            float halfHeight = rect.rect.height * 0.5f * 0.94f;
            float pi2 = Mathf.PI * 2f;

            for (int i = 0; i < config.Count; i++)
            {
                Vector2 position = new(Random.Range(-halfWidth, halfWidth), Random.Range(-halfHeight, halfHeight));
                float size = Random.Range(config.SizeMin, config.SizeMax);
                Color color = config.Palette[Random.Range(0, config.Palette.Length)];
                color.a *= Random.Range(config.AlphaMin, config.AlphaMax);
                bool sparkle = Random.value < config.SparkleChance;
                MenuTwinkle.CreateStar(rect, position, size, color,
                    config.TwinkleAmp * Random.Range(0.6f, 1.4f),
                    Random.Range(config.SpeedMin, config.SpeedMax),
                    Random.Range(0f, pi2) + config.PhaseShift, sparkle, stars, config.SoftCore);
            }
        }

        private void Update()
        {
            if (built)
                MenuTwinkle.Step(stars, Time.unscaledTime);
        }

        // автономный сдвиг слоя (sway + drift); запас площади слоя (±400px)
        // гарантированно скрывает края при любом допустимом смещении
        public void ApplyOffset(Vector2 offset) => ((RectTransform)transform).anchoredPosition = offset;
    }
}
