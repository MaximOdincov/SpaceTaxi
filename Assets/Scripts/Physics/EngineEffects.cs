using UnityEngine;
using SpaceTaxi.Core;

namespace SpaceTaxi.Physics
{
    // Выхлоп двигателя: child-слоты ExhaustMain (за кормой) и ExhaustRetro (спереди) —
    // точки подключения частиц. Слот с PS из префаба используется как есть (дефолт
    // не создаётся), пустой слот — резерв владельца, дефолт тоже не мешаем; слота
    // нет вовсе — строим слот и дефолтное пламя кодом. Управление: play/stop по
    // тяге/тормозу, эмиссия и размер — от EnginePower (у поверхности пламя гуще).
    public class EngineEffects : MonoBehaviour
    {
        public const string MainSlotName = "ExhaustMain";
        public const string RetroSlotName = "ExhaustRetro";

        private Ship ship;
        private ShipController controller;

        private ParticleSystem mainJet;
        private ParticleSystem retroJet;

        // авторский PS — трогаем только play/stop и множитель эмиссии
        private bool mainAuthored;
        private bool retroAuthored;

        private void Awake()
        {
            ship = GetComponent<Ship>();
            controller = GetComponent<ShipController>();

            Vector2 nose = NoseDir();
            mainJet = ResolveSlot(MainSlotName, -nose * 6f, -nose, isMain: true);
            retroJet = ResolveSlot(RetroSlotName, nose * 7f, nose, isMain: false);
        }

        private void Update()
        {
            if (ship == null || controller == null)
                return;

            bool flying = ship.State == ShipState.Flying;
            float power = Mathf.Max(1f, controller.EnginePower);

            Drive(mainJet, flying && controller.ThrustOn, power, scaleFlame: !mainAuthored);
            Drive(retroJet, flying && controller.BrakeOn, power, scaleFlame: !retroAuthored);

            // в посадочном развороте струя ретро к планете; авторский слот не трогаем
            if (retroJet != null && !retroAuthored)
            {
                Vector2 dir = controller.LandingRetro ? -NoseDir() : NoseDir();
                retroJet.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, dir);
            }
        }

        // нос спрайта в локальных осях: SpriteNoseAngle 0 = вправо, 90 = вверх
        private Vector2 NoseDir()
        {
            float a = (controller != null ? controller.SpriteNoseAngle : 0f) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        private ParticleSystem ResolveSlot(string slotName, Vector3 localPos, Vector2 exhaustDir, bool isMain)
        {
            Transform slot = transform.Find(slotName);
            if (slot != null)
            {
                ParticleSystem authored = slot.GetComponent<ParticleSystem>();
                if (authored == null)
                    return null; // слот зарезервирован в префабе без PS — ждём авторские частицы
                if (isMain)
                    mainAuthored = true;
                else
                    retroAuthored = true;
                return authored;
            }

            var go = new GameObject(slotName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, exhaustDir);
            CreateDefaultFlame(go, isMain);
            return go.GetComponent<ParticleSystem>();
        }

        private static void Drive(ParticleSystem ps, bool on, float power, bool scaleFlame)
        {
            if (ps == null)
                return;
            var em = ps.emission;
            em.rateOverTimeMultiplier = power;
            if (scaleFlame)
            {
                var main = ps.main;
                main.startSizeMultiplier = 1f + (power - 1f) * 0.25f;
            }

            if (on)
            {
                if (!ps.isEmitting)
                    ps.Play(true);
            }
            else if (ps.isEmitting)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); // хвост догорает сам
            }
        }

        // мягкое пламя: ColorOverLifetime оранж→прозрачный, конус от сопла
        private static void CreateDefaultFlame(GameObject go, bool isMain)
        {
            var src = go.transform.parent.GetComponent<SpriteRenderer>();
            Shader shader = src != null && src.sharedMaterial != null
                ? src.sharedMaterial.shader
                : Shader.Find("Sprites/Default");
            Texture2D tex = StubSprites.Circle(8).texture;
            tex.wrapMode = TextureWrapMode.Repeat;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(45f, 65f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 200;

            var emission = ps.emission;
            emission.rateOverTime = isMain ? 90f : 70f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.4f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0f),
                    new GradientColorKey(new Color(1f, 0.35f, 0.15f), 0.7f),
                },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0.5f, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.15f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(shader) { mainTexture = tex };
            renderer.sortingOrder = 3;
        }
    }
}
