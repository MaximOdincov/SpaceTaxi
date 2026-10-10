using UnityEngine;
using SpaceTaxi.Core;

namespace SpaceTaxi.Physics
{
    // Выхлоп тяги, ретро-сопла и микросопла стрейфа
    public class EngineEffects : MonoBehaviour
    {
        private Ship ship;
        private ShipController controller;
        private ParticleSystem mainJet;
        private ParticleSystem retroJet;
        private ParticleSystem strafeTop;
        private ParticleSystem strafeBottom;

        private static readonly Quaternion RetroForward = Quaternion.FromToRotation(Vector3.forward, Vector3.right);
        private static readonly Quaternion RetroFlipped = Quaternion.FromToRotation(Vector3.forward, Vector3.left);

        private void Awake()
        {
            ship = GetComponent<Ship>();
            controller = GetComponent<ShipController>();

            // мягкие круглые частицы из подручных спрайтов, один материал на все сопла
            var src = GetComponent<SpriteRenderer>();
            Shader shader = src != null && src.sharedMaterial != null
                ? src.sharedMaterial.shader
                : Shader.Find("Sprites/Default");
            Texture2D tex = StubSprites.Circle(8).texture;
            tex.wrapMode = TextureWrapMode.Repeat;
            Material mat = new Material(shader) { mainTexture = tex };

            // тёплый выхлоп — тяга и стрейф, холодный — ретро
            mainJet = CreateJet("MainJet", new Vector3(-6f, 0f, 0f), Vector3.left,
                new Color(1f, 0.85f, 0.45f), new Color(1f, 0.35f, 0.15f), mat, 90f);
            retroJet = CreateJet("RetroJet", new Vector3(7f, 0f, 0f), Vector3.right,
                new Color(0.65f, 0.9f, 1f), new Color(0.25f, 0.5f, 1f), mat, 70f);
            strafeTop = CreateJet("StrafeTop", new Vector3(0f, 3f, 0f), Vector3.up,
                new Color(1f, 0.85f, 0.45f), new Color(1f, 0.35f, 0.15f), mat, 40f);
            strafeBottom = CreateJet("StrafeBottom", new Vector3(0f, -3f, 0f), Vector3.down,
                new Color(1f, 0.85f, 0.45f), new Color(1f, 0.35f, 0.15f), mat, 40f);
        }

        private void Update()
        {
            if (ship == null || controller == null)
                return;

            bool flying = ship.State == ShipState.Flying;
            SetEmission(mainJet, flying && controller.ThrustOn);
            //SetEmission(retroJet, flying && controller.BrakeOn);
            //SetEmission(strafeTop, flying && controller.StrafeLevel > 0f);
            //SetEmission(strafeBottom, flying && controller.StrafeLevel < 0f);

            // в посадочном развороте нос против скорости — струя к планете
            retroJet.transform.localRotation = controller.LandingRetro ? RetroFlipped : RetroForward;

            // у поверхности тяга выше — пламя гуще
            float power = Mathf.Max(1f, controller.EnginePower);
            ApplyPower(mainJet, power);
            ApplyPower(retroJet, power);
        }

        private static void ApplyPower(ParticleSystem ps, float power)
        {
            var em = ps.emission;
            em.rateOverTimeMultiplier = power;
            var main = ps.main;
            main.startSizeMultiplier = 1f + (power - 1f) * 0.25f;
        }

        private static void SetEmission(ParticleSystem ps, bool on)
        {
            var em = ps.emission;
            if (em.enabled != on)
                em.enabled = on;
        }

        private ParticleSystem CreateJet(string name, Vector3 localPos, Vector3 localDir,
            Color startColor, Color endColor, Material mat, float rate)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, localDir);

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
            emission.rateOverTime = rate;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.4f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(startColor, 0f), new GradientColorKey(endColor, 0.7f) },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0.5f, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(1f, 0.15f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = mat;
            renderer.sortingOrder = 3;

            ps.Play();
            return ps;
        }
    }
}
