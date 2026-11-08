using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.Core;

namespace SpaceTaxi.Physics
{
    // Мелкие эффекты повреждений: периодические искры у корпуса при HP < 40%.
    // Вспышку удара снесли (CrashImpactAt): бежевая точка в месте касания
    // читалась как мусор, полезной нагрузки в ней не было.
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
            effectMaterial = CreateEffectMaterial();
            sparks = CreateSparks();
        }

        private void OnDestroy()
        {
            if (hull != null)
                hull.HpChanged -= OnHpChanged;
        }

        private void Update()
        {
            UpdateSparks();
        }

        private void OnHpChanged()
        {
            lowHp = ship != null && ship.HpFraction < SparksHpFraction;
        }

        private void UpdateSparks()
        {
            if (sparks == null || ship == null)
                return;

            // искры только в полёте: у стоящего на планете пачки искр выглядят
            // как вечный «шарик» под корпусом (жалоба плейтеста)
            bool want = lowHp && ship.State == ShipState.Flying;
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
