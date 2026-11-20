using UnityEngine;
using SpaceTaxi.Core;

namespace SpaceTaxi.Physics
{
    // Одноразовый взрыв корабля при гибели: вспышка + огонь + крупные
    // осколки + дымный хвост. ParticleSystem строится кодом без префаба; спавн
    // в точке корабля в момент смерти, до телепорта респавна; объект уничтожает
    // себя через CleanupSeconds (пул не нужен — один выстрел). Симуляция в
    // мировых осях: частицы не уезжают вместе с респавном; useUnscaledTime —
    // горит и при timeScale=0 (модалы).
    // Звук не здесь: смерть уже озвучена в HullSystem.Die (Sound.Play(Explosion)).
    public static class ShipExplosion
    {
        // --- время жизни ---
        private const float CleanupSeconds = 2.5f;
        private const float FlashLifetime = 0.18f;      // вспышка короткая: «щелчок», не «шар»
        private const float FireLifetimeMin = 0.35f;
        private const float FireLifetimeMax = 0.80f;
        private const float DebrisLifetimeMin = 1.40f;  // осколки дрейфуют дольше огня
        private const float DebrisLifetimeMax = 2.20f;
        private const float SmokeLifetimeMin = 1.40f;
        private const float SmokeLifetimeMax = 2.20f;

        // --- количества частиц (Debris 12-18, Fire 20-40) ---
        private const int DebrisCountMin = 14;
        private const int DebrisCountMax = 18;
        private const int FireCountMin = 24;
        private const int FireCountMax = 40;
        private const int SmokeCountMin = 10;
        private const int SmokeCountMax = 14;

        // --- размеры в долях полу-габарита корпуса ---
        private const float DebrisSizeMinFrac = 0.30f;  // осколки 0.3-0.5 корпуса
        private const float DebrisSizeMaxFrac = 0.50f;
        private const float DebrisSpeedMinFrac = 1.00f; // заметный дрейф от центра взрыва
        private const float DebrisSpeedMaxFrac = 2.00f;
        private const float DebrisSpinRadPerSec = 3.0f; // вращение обломков
        private const float DebrisDriftLimitFrac = 0.80f;
        private const float FireSizeMinFrac = 0.25f;
        private const float FireSizeMaxFrac = 0.55f;
        private const float FireSpeedMinFrac = 0.40f;
        private const float FireSpeedMaxFrac = 1.20f;
        private const float SmokeSizeMinFrac = 0.35f;
        private const float SmokeSizeMaxFrac = 0.60f;
        private const float SmokeSpeedMinFrac = 0.12f;
        private const float SmokeSpeedMaxFrac = 0.40f;
        private const float FlashSizeFrac = 1.15f;      // вспышка накрывает корпус
        // фолбэк габарита: заглушка корпуса ShipTriangle 35 юнитов (ShipModelVisual)
        private const float DefaultHalfExtent = 17.5f;

        // --- сортировка: поверх корабля и его эффектов (огонь 3, искры 4) ---
        private const int SmokeOrder = 4;   // дым под огнём и осколками
        private const int DebrisOrder = 5;
        private const int FireOrder = 6;
        private const int FlashOrder = 7;   // вспышка поверх всего

        // --- палитра огня: три оттенка оранжево-красного ---
        private static readonly Color FireBright = new Color(1.00f, 0.62f, 0.16f); // оранжевый
        private static readonly Color FireMid = new Color(1.00f, 0.32f, 0.10f);    // красно-оранжевый
        private static readonly Color FireDeep = new Color(0.85f, 0.12f, 0.05f);   // тёмно-красный
        private static readonly Color SmokeDark = new Color(0.16f, 0.14f, 0.13f);
        private static readonly Color SmokeLight = new Color(0.30f, 0.27f, 0.25f);

        // shipHalfExtent — половина длины корпуса в мировых юнитах
        // (ShipModelVisual.HalfExtent), от него масштабируются все слои;
        // tint — красно-оранжевая палитра взрыва (передаёт HullSystem)
        public static void Spawn(Vector2 position, float shipHalfExtent, Color tint)
        {
            float half = shipHalfExtent > 0f ? shipHalfExtent : DefaultHalfExtent;
            var root = new GameObject("ShipExplosion");
            root.transform.position = position;
            BuildSmoke(root.transform, half);
            BuildDebris(root.transform, half, tint);
            BuildFire(root.transform, half);
            BuildFlash(root.transform, half, tint);
            Object.Destroy(root, CleanupSeconds);
        }

        // вспышка: один крупный мягкий спрайт — короткое распухание и мгновенный спад
        private static void BuildFlash(Transform parent, float halfExtent, Color tint)
        {
            ParticleSystem ps = NewSystem(parent, "Flash", FlashOrder, StubSprites.SoftDot(64f, 0.7f).texture);

            var main = ps.main;
            main.startLifetime = FlashLifetime;
            main.startSpeed = 0f;
            main.startSize = halfExtent * FlashSizeFrac;
            main.maxParticles = 2;

            // распухание за первую половину жизни вспышки
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.30f),
                new Keyframe(0.5f, 1f),
                new Keyframe(1f, 1.1f)));

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.Lerp(tint, Color.white, 0.6f), 0f),
                    new GradientColorKey(tint, 0.55f),
                    new GradientColorKey(tint * 0.6f, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.7f, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            ps.Play(true);
            ps.Emit(1);
        }

        // огонь: рыхлое пламя из мягких точек, 3 оттенка оранжево-красного,
        // вырывается из корпуса и быстро съедается
        private static void BuildFire(Transform parent, float halfExtent)
        {
            ParticleSystem ps = NewSystem(parent, "Fire", FireOrder, StubSprites.SoftDot(64f, 0.5f).texture);
            var shape = ps.shape;
            shape.radius = halfExtent * 0.4f; // пламя из всего корпуса

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(FireLifetimeMin, FireLifetimeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(halfExtent * FireSpeedMinFrac, halfExtent * FireSpeedMaxFrac);
            main.startSize = new ParticleSystem.MinMaxCurve(halfExtent * FireSizeMinFrac, halfExtent * FireSizeMaxFrac);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.gravityModifier = 0f;
            main.maxParticles = FireCountMax;
            // случайный оттенок на частицу: от яркого оранжевого к красно-оранжевому
            main.startColor = new ParticleSystem.MinMaxGradient(FireMid, FireBright);

            // жёлто-белое ядро выгорает в тёмно-красный
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.Lerp(FireBright, Color.white, 0.55f), 0f),
                    new GradientColorKey(FireBright, 0.3f),
                    new GradientColorKey(FireMid, 0.6f),
                    new GradientColorKey(FireDeep, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.85f, 0.45f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f),
                new Keyframe(0.25f, 1f),
                new Keyframe(1f, 0.35f)));

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);

            ps.Play(true);
            ps.Emit(Random.Range(FireCountMin, FireCountMax + 1));
        }

        // осколки: крупные куски арта астероидов (0.3-0.5 корпуса), разлёт из
        // объёма корпуса, заметный дрейф, вращение, мягкое торможение лимитером
        private static void BuildDebris(Transform parent, float halfExtent, Color tint)
        {
            Sprite piece = WorldSprites.RandomAsteroid();
            Texture2D texture = piece != null ? piece.texture : StubSprites.Circle(8f).texture;
            ParticleSystem ps = NewSystem(parent, "Debris", DebrisOrder, texture);
            var shape = ps.shape;
            shape.radius = halfExtent * 0.5f; // осколки из всего корпуса

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(DebrisLifetimeMin, DebrisLifetimeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(halfExtent * DebrisSpeedMinFrac, halfExtent * DebrisSpeedMaxFrac);
            main.startSize = new ParticleSystem.MinMaxCurve(halfExtent * DebrisSizeMinFrac, halfExtent * DebrisSizeMaxFrac);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.gravityModifier = 0.12f;
            main.maxParticles = DebrisCountMax;
            // разогретый камень: от светлого у вспышки до тёмно-красного
            main.startColor = new ParticleSystem.MinMaxGradient(
                Color.Lerp(tint, Color.white, 0.25f),
                new Color(0.45f, 0.16f, 0.08f));

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = halfExtent * DebrisDriftLimitFrac;
            limit.dampen = 0.15f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.7f, 0.85f),
                new Keyframe(1f, 0.55f)));

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-DebrisSpinRadPerSec, DebrisSpinRadPerSec);

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.9f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            ps.Play(true);
            ps.Emit(Random.Range(DebrisCountMin, DebrisCountMax + 1));
        }

        // дым: тёмные мягкие клубы — медленный «хвост» после вспышки, растёт и тает
        private static void BuildSmoke(Transform parent, float halfExtent)
        {
            ParticleSystem ps = NewSystem(parent, "Smoke", SmokeOrder, StubSprites.SoftDot(64f, 0.35f).texture);
            var shape = ps.shape;
            shape.radius = halfExtent * 0.35f;

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(SmokeLifetimeMin, SmokeLifetimeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(halfExtent * SmokeSpeedMinFrac, halfExtent * SmokeSpeedMaxFrac);
            main.startSize = new ParticleSystem.MinMaxCurve(halfExtent * SmokeSizeMinFrac, halfExtent * SmokeSizeMaxFrac);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.gravityModifier = 0f;
            main.maxParticles = SmokeCountMax;
            main.startColor = new ParticleSystem.MinMaxGradient(SmokeDark, SmokeLight);

            // клуб растёт по мере дрейфа
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f),
                new Keyframe(1f, 1.4f)));

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(SmokeLight, 0f),
                    new GradientColorKey(SmokeDark, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.55f, 0f),
                    new GradientAlphaKey(0.35f, 0.4f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            ps.Play(true);
            ps.Emit(Random.Range(SmokeCountMin, SmokeCountMax + 1));
        }

        // общий каркас: мировые оси, realtime-симуляция, разовый ручной выброс
        private static ParticleSystem NewSystem(Transform parent, string name, int order, Texture2D texture)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;

            var emission = ps.emission;
            emission.rateOverTime = 0f; // только ручной Emit

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            Shader shader = Shader.Find("Sprites/Default"); // как в DamageEffects
            if (shader != null)
                renderer.material = new Material(shader) { mainTexture = texture };
            renderer.sortingOrder = order;
            return ps;
        }
    }
}
