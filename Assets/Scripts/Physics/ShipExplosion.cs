using UnityEngine;
using SpaceTaxi.Core;

namespace SpaceTaxi.Physics
{
    // Одноразовый взрыв корабля при гибели: ParticleSystem строится кодом без
    // префаба — вспышка + осколки. Спавн в точке корабля в момент смерти, до
    // телепорта респавна; объект уничтожает себя через CleanupSeconds (пул не
    // нужен — один выстрел). Симуляция в мировых осях: частицы не уезжают
    // вместе с респавном; useUnscaledTime — горит и при timeScale=0 (модалы).
    // Звук не здесь: смерть уже озвучена в HullSystem.Die (Sound.Play(Explosion)),
    // точка подключения AudioManager остаётся там же.
    public static class ShipExplosion
    {
        private const float CleanupSeconds = 2.5f;
        private const int FlashOrder = 6; // поверх корабля и его эффектов (огонь 3, искры 4)
        private const int DebrisOrder = 5;
        private const int DebrisCountMin = 10;
        private const int DebrisCountMax = 16; // включительно
        // фолбэк габарита: заглушка корпуса ShipTriangle 35 юнитов (ShipModelVisual)
        private const float DefaultHalfExtent = 17.5f;

        // shipHalfExtent — половина длины корпуса в мировых юнитах
        // (ShipModelVisual.HalfExtent), от него масштабируются вспышка и осколки;
        // tint — красно-оранжевая палитра взрыва (передаёт HullSystem)
        public static void Spawn(Vector2 position, float shipHalfExtent, Color tint)
        {
            float half = shipHalfExtent > 0f ? shipHalfExtent : DefaultHalfExtent;
            var root = new GameObject("ShipExplosion");
            root.transform.position = position;
            BuildFlash(root.transform, half, tint);
            BuildDebris(root.transform, half, tint);
            Object.Destroy(root, CleanupSeconds);
        }

        // вспышка: один крупный мягкий спрайт — быстрое распухание и затухание
        private static void BuildFlash(Transform parent, float halfExtent, Color tint)
        {
            ParticleSystem ps = NewSystem(parent, "Flash", FlashOrder, StubSprites.SoftDot(64f, 0.7f).texture);

            var main = ps.main;
            main.startLifetime = 0.45f;
            main.startSpeed = 0f;
            main.startSize = halfExtent * 1.2f;
            main.maxParticles = 2;

            // распухание: за первые 0.2 c до полного размера, дальше медленный рост
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1.6f, new AnimationCurve(
                new Keyframe(0f, 0.35f),
                new Keyframe(0.2f, 1f),
                new Keyframe(1f, 1f)));

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.Lerp(tint, Color.white, 0.55f), 0f),
                    new GradientColorKey(tint, 0.45f),
                    new GradientColorKey(tint * 0.55f, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.85f, 0.55f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            ps.Play(true);
            ps.Emit(1);
        }

        // осколки: куски арта астероидов, разлёт из объёма корпуса, лёгкая
        // гравитация, торможение лимитером скорости
        private static void BuildDebris(Transform parent, float halfExtent, Color tint)
        {
            Sprite piece = WorldSprites.RandomAsteroid();
            Texture2D texture = piece != null ? piece.texture : StubSprites.Circle(8f).texture;
            ParticleSystem ps = NewSystem(parent, "Debris", DebrisOrder, texture);
            var shape = ps.shape;
            shape.radius = halfExtent * 0.5f; // осколки из всего корпуса

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(halfExtent * 0.8f, halfExtent * 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(halfExtent * 0.14f, halfExtent * 0.28f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.gravityModifier = 0.12f;
            main.maxParticles = DebrisCountMax;
            // разогретый камень: от светлого у вспышки до тёмно-красного
            main.startColor = new ParticleSystem.MinMaxGradient(
                Color.Lerp(tint, Color.white, 0.25f),
                new Color(0.45f, 0.16f, 0.08f));

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = halfExtent * 0.5f;
            limit.dampen = 0.25f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.7f, 0.85f),
                new Keyframe(1f, 0.55f)));

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-2.2f, 2.2f);

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
