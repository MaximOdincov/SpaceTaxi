using UnityEngine;
using SpaceTaxi.Core; // StubSprites

namespace SpaceTaxi.Physics
{
    // Арт-данные дефолтного выхлопа: палитра, градиенты пламени/искр/следа
    // и фабрика материала частиц. Логика сборки и управления ParticleSystem —
    // в EngineEffects
    internal static class EngineFlameArt
    {
        // палитра: главный — белое-в-желту ядро → оранж → красный → дымный хвост
        private static readonly Color MainCore = new Color(1f, 0.96f, 0.7f);
        private static readonly Color MainFire = new Color(1f, 0.62f, 0.2f);
        private static readonly Color MainEmber = new Color(0.95f, 0.32f, 0.1f);
        private static readonly Color MainSmoke = new Color(0.5f, 0.38f, 0.34f);
        // ретро — мятно-голубой «пых», без дымного хвоста
        private static readonly Color RetroCore = new Color(0.85f, 1f, 0.92f);
        private static readonly Color RetroGlow = new Color(0.5f, 0.9f, 1f);
        private static readonly Color RetroFade = new Color(0.3f, 0.62f, 0.85f);
        // искры: главные — белые-в-оранж, ретро — белые-в-мяту
        private static readonly Color SparkHot = new Color(1f, 1f, 0.88f);
        private static readonly Color SparkFade = new Color(1f, 0.45f, 0.12f);
        private static readonly Color SparkRetroHot = new Color(0.92f, 1f, 0.95f);
        private static readonly Color SparkRetroFade = new Color(0.45f, 0.9f, 1f);

        // кэш материалов частиц (Н10 Слой 1, §3.6): прежде — new Material на
        // КАЖДЫЙ Rebind, а слоты при Rebind сносятся и материал оставался
        // в памяти без владельца. Один материал на шейдер корабля — слоты
        // делят его (свойств никто не мутирует индивидуально); если Unity
        // выгрузил материал (UnloadUnusedAssets без ссылок) — пересоздаём
        private static readonly System.Collections.Generic.Dictionary<Shader, Material> particleMaterials =
            new System.Collections.Generic.Dictionary<Shader, Material>();

        // материал частиц: шейдер SpriteRenderer корабля (или Sprites/Default) +
        // кружок-заглушка; Repeat нужен для тайлинга вдоль шлейфа (как в SpeedTrail)
        internal static Material MakeParticleMaterial(Transform spriteHost)
        {
            var src = spriteHost.GetComponent<SpriteRenderer>();
            Shader shader = src != null && src.sharedMaterial != null
                ? src.sharedMaterial.shader
                : Shader.Find("Sprites/Default");
            if (shader != null && particleMaterials.TryGetValue(shader, out Material cached) && cached != null)
                return cached;
            Texture2D tex = StubSprites.Circle(8).texture;
            tex.wrapMode = TextureWrapMode.Repeat;
            Material mat = new Material(shader) { mainTexture = tex };
            if (shader != null)
                particleMaterials[shader] = mat;
            return mat;
        }

        internal static Gradient MainFlameGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(MainCore, 0f),
                    new GradientColorKey(MainFire, 0.18f),
                    new GradientColorKey(MainEmber, 0.45f),
                    new GradientColorKey(MainSmoke, 0.75f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.95f, 0.18f),
                    new GradientAlphaKey(0.6f, 0.5f),
                    new GradientAlphaKey(0.25f, 0.78f),
                    new GradientAlphaKey(0f, 1f),
                });
            return g;
        }

        internal static Gradient RetroFlameGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(RetroCore, 0f),
                    new GradientColorKey(RetroGlow, 0.3f),
                    new GradientColorKey(RetroFade, 0.75f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.7f, 0.4f),
                    new GradientAlphaKey(0f, 1f),
                });
            return g;
        }

        internal static Gradient SparkGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(SparkHot, 0f),
                    new GradientColorKey(SparkFade, 0.6f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.9f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            return g;
        }

        internal static Gradient RetroSparkGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(SparkRetroHot, 0f),
                    new GradientColorKey(SparkRetroFade, 0.6f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.9f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            return g;
        }

        // след наследует цвет частицы, сам гасит только альфу вдоль длины
        internal static Gradient TrailFadeGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.4f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            return g;
        }
    }
}
