using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.Core;

namespace SpaceTaxi.Physics
{
    // Выхлоп двигателя: child-слоты ExhaustMain (за кормой) и ExhaustRetro (спереди) —
    // точки подключения частиц. Слоты берутся из визуала модели (ShipModelVisual,
    // префаб в Resources/Ships/<Id>) или с корня корабля, если визуала нет. PS на
    // слоте (в т.ч. вложенная глубже) используется как есть, пустой слот — дефолтное
    // пламя ребёнком слота, слота нет вовсе — строим слот и пламя кодом. Управление:
    // play/stop по тяге/тормозу, эмиссия и размер — от EnginePower.
    public class EngineEffects : MonoBehaviour
    {
        public const string MainSlotName = "ExhaustMain";
        public const string RetroSlotName = "ExhaustRetro";

        private Ship ship;
        private ShipController controller;
        private ShipModelVisual model;

        private ParticleSystem mainJet;
        private ParticleSystem retroJet;

        // авторский PS — трогаем только play/stop и множитель эмиссии
        private bool mainAuthored;
        private bool retroAuthored;

        // слоты/пламя, созданные кодом: при Rebind (смена модели) сносятся
        private readonly List<Transform> createdSlots = new List<Transform>();

        private void Awake()
        {
            ship = GetComponent<Ship>();
            controller = GetComponent<ShipController>();
            Rebind();
        }

        // перечитать слоты: визуал модели пересоздаётся при смене корпуса/покраски
        public void Rebind()
        {
            foreach (Transform slot in createdSlots)
                if (slot != null)
                    Destroy(slot.gameObject);
            createdSlots.Clear();
            mainJet = null;
            retroJet = null;
            mainAuthored = false;
            retroAuthored = false;

            model = GetComponent<ShipModelVisual>();
            Transform root = model != null && model.HasVisual ? model.VisualRoot : transform;

            Vector2 nose = NoseDir();
            mainJet = ResolveSlot(root, MainSlotName, -nose * 6f, -nose, isMain: true);
            retroJet = ResolveSlot(root, RetroSlotName, nose * 7f, nose, isMain: false);
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

        private ParticleSystem ResolveSlot(Transform root, string slotName, Vector3 localPos, Vector2 exhaustDir, bool isMain)
        {
            Transform slot = root.Find(slotName);
            if (slot != null)
            {
                // авторский PS на слоте или вложенный глубже (GetComponentInChildren
                // проверяет и сам слот) — дефолт не создаём
                ParticleSystem authored = slot.GetComponentInChildren<ParticleSystem>();
                if (authored != null)
                {
                    if (isMain)
                        mainAuthored = true;
                    else
                        retroAuthored = true;
                    return authored;
                }

                // пустой слот префаба — дефолтное пламя ребёнком, позицию держит слот
                var flameGo = new GameObject("DefaultFlame");
                flameGo.transform.SetParent(slot, false);
                flameGo.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, exhaustDir);
                CreateDefaultFlame(flameGo, isMain);
                createdSlots.Add(flameGo.transform);
                return flameGo.GetComponent<ParticleSystem>();
            }

            // слота нет вовсе — строим слот и дефолтное пламя (корневой фолбэк)
            var go = new GameObject(slotName);
            go.transform.SetParent(root, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, exhaustDir);
            CreateDefaultFlame(go, isMain);
            createdSlots.Add(go.transform);
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
