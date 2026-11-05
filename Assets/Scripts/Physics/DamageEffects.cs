using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.Core;

namespace SpaceTaxi.Physics
{
    // Мелкие эффекты повреждений: периодические искры у корпуса при HP < 40%
    // и короткая вспышка в точке контактного удара (LandingSystem.CrashImpactAt).
    public class DamageEffects : MonoBehaviour
    {
        private const float SparksHpFraction = 0.4f;

        private Ship ship;
        private HullSystem hull;
        private LandingSystem landing;
        private ParticleSystem sparks;
        private Material effectMaterial; // общий для искр и вспышки, шейдер корпуса

        private bool lowHp;
        private float sparkTimer;

        private readonly List<Flash> flashes = new List<Flash>();

        private struct Flash
        {
            public SpriteRenderer Renderer;
            public float Time;
            public float Duration;
            public float FromScale;
            public float ToScale;
        }

        private void Awake()
        {
            ship = GetComponent<Ship>();
            hull = GetComponent<HullSystem>();
            landing = GetComponent<LandingSystem>();
            if (hull != null)
            {
                hull.HpChanged += OnHpChanged;
                lowHp = ship != null && ship.HpFraction < SparksHpFraction;
            }
            if (landing != null)
                landing.CrashImpactAt += OnImpact;

            effectMaterial = CreateEffectMaterial();
            sparks = CreateSparks();
        }

        private void OnDestroy()
        {
            if (hull != null)
                hull.HpChanged -= OnHpChanged;
            if (landing != null)
                landing.CrashImpactAt -= OnImpact;
        }

        private void Update()
        {
            UpdateSparks();
            UpdateFlashes();
        }

        private void OnHpChanged()
        {
            lowHp = ship != null && ship.HpFraction < SparksHpFraction;
        }

        private void OnImpact(Vector2 point)
        {
            var go = new GameObject("CrashFlash");
            go.transform.position = point;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = StubSprites.Circle(6f);
            renderer.sharedMaterial = effectMaterial;
            renderer.sortingOrder = 5;
            renderer.color = new Color(1f, 0.9f, 0.6f, 0.95f);

            // разлёт: растёт и гаснет за 0.22 с
            flashes.Add(new Flash
            {
                Renderer = renderer,
                Time = 0f,
                Duration = 0.22f,
                FromScale = 0.6f,
                ToScale = 3.4f,
            });
        }

        private void UpdateSparks()
        {
            if (sparks == null || ship == null)
                return;

            // искры на живом корабле; в респавне/пасти дыры — тишина
            bool want = lowHp && (ship.State == ShipState.Flying || ship.State == ShipState.Landed);
            if (want)
            {
                if (!sparks.isEmitting)
                    sparks.Play(true);
                sparkTimer -= Time.deltaTime;
                if (sparkTimer <= 0f)
                {
                    sparks.Emit(Random.Range(3, 7)); // пачка из случайной точки корпуса
                    sparkTimer = Random.Range(0.35f, 1.1f);
                }
            }
            else if (sparks.isEmitting)
            {
                sparkTimer = 0f;
                sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void UpdateFlashes()
        {
            float dt = Time.deltaTime;
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                Flash f = flashes[i];
                if (f.Renderer == null)
                {
                    flashes.RemoveAt(i);
                    continue;
                }
                f.Time += dt;
                float t = Mathf.Clamp01(f.Time / f.Duration);
                f.Renderer.transform.localScale = Vector3.one * Mathf.Lerp(f.FromScale, f.ToScale, t);
                Color c = f.Renderer.color;
                c.a = 0.95f * (1f - t) * (1f - t);
                f.Renderer.color = c;
                if (t >= 1f)
                {
                    Destroy(f.Renderer.gameObject);
                    flashes.RemoveAt(i);
                }
            }
        }

        private Material CreateEffectMaterial()
        {
            var src = GetComponent<SpriteRenderer>();
            Shader shader = src != null && src.sharedMaterial != null
                ? src.sharedMaterial.shader
                : Shader.Find("Sprites/Default");
            Texture2D tex = StubSprites.Circle(8).texture;
            tex.wrapMode = TextureWrapMode.Repeat;
            return new Material(shader) { mainTexture = tex };
        }

        // искры не «шлейфят» за кораблём — мировое пространство, пачки вручную через Emit
        private ParticleSystem CreateSparks()
        {
            var go = new GameObject("DamageSparks");
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 3.5f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f),
                    new GradientColorKey(new Color(1f, 0.4f, 0.1f), 0.6f),
                },
                new[]
                {
                    new GradientAlphaKey(0.95f, 0f),
                    new GradientAlphaKey(0.7f, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = effectMaterial;
            renderer.sortingOrder = 3;
            return ps;
        }
    }
}
