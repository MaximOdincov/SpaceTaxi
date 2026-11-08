using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.Core;

namespace SpaceTaxi.Physics
{
    // Выхлоп двигателя: child-слоты ExhaustMain (за кормой) и ExhaustRetro (спереди) —
    // точки подключения частиц. Слоты берутся из визуала модели (ShipModelVisual,
    // префаб в Resources/Ships/<Id>) или с корня корабля, если визуала нет. PS на
    // слоте (в т.ч. вложенная глубже) — «авторская», трогаем только play/stop и
    // множитель эмиссии. Пустой слот или его отсутствие — дефолтный выхлоп кодом:
    // пламя с Trail-шлейфом (след-«линия тяги» за каждой частицей) и искры разгона
    // вторым PS. Управление: play/stop по тяге/тормозу, эмиссия и размер — от
    // EnginePower; у дефолта мощность растит ещё скорость, длину и яркость пламени.
    public class EngineEffects : MonoBehaviour
    {
        public const string MainSlotName = "ExhaustMain";
        public const string RetroSlotName = "ExhaustRetro";

        // ---- дефолтный выхлоп: числа на глаз, владелец подвинет в Play Mode ----

        // главный луч: скорость × жизнь = длина пламени, размер = толщина
        private const float MainSpeedMin = 55f;
        private const float MainSpeedMax = 80f;
        private const float MainLifeMin = 0.25f;
        private const float MainLifeMax = 0.4f;
        private const float MainSizeMin = 1.7f;
        private const float MainSizeMax = 2.7f;
        private const float MainConeAngle = 13f;      // конус сопла (было 10)
        private const float MainConeRadius = 0.45f;
        private const float MainRate = 110f;          // эмиссия, частиц/с
        private const int MainMaxParticles = 300;

        // ретро: холодный пых — короче и шире главного
        private const float RetroSpeedMin = 38f;
        private const float RetroSpeedMax = 55f;
        private const float RetroLifeMin = 0.18f;
        private const float RetroLifeMax = 0.3f;
        private const float RetroSizeMin = 1.5f;
        private const float RetroSizeMax = 2.3f;
        private const float RetroConeAngle = 22f;
        private const float RetroConeRadius = 0.55f;
        private const float RetroRate = 85f;
        private const int RetroMaxParticles = 220;

        // реакция на EnginePower (1..3.2 в посадочном режиме): множитель = 1+(power-1)*gain
        private const float SizePowerGain = 0.25f;    // толщина (старая семантика Drive)
        private const float SpeedPowerGain = 0.3f;    // скорость выхлопа
        private const float LifePowerGain = 0.45f;    // длина пламени
        private const float BrightPowerGain = 0.45f;  // яркость ядра (RGB>1 в startColor)

        // шлейф (Trail-модуль): след длиной ~скорость×жизнь, ширина от размера частицы;
        // ratio меньше — «линии тяги» реже, без каши
        private const float TrailRatioMain = 0.8f;    // доля частиц со следом
        private const float TrailRatioRetro = 0.7f;
        private const float TrailRatioSpark = 1f;
        private const float TrailLifeMain = 0.16f;
        private const float TrailLifeRetro = 0.12f;
        private const float TrailLifeSpark = 0.09f;
        private const float TrailWidthMain = 0.35f;   // × размер частицы
        private const float TrailWidthRetro = 0.3f;
        private const float TrailWidthSpark = 0.22f;
        private const float TrailMinVertexDistance = 0.5f;

        // искры разгона: редкие быстрые горошки, белые-в-оранж
        private const float SparkSpeedMin = 90f;
        private const float SparkSpeedMax = 130f;
        private const float SparkLifeMin = 0.15f;
        private const float SparkLifeMax = 0.3f;
        private const float SparkSizeMin = 0.7f;
        private const float SparkSizeMax = 1.3f;
        private const float SparkConeAngle = 12f;
        private const float SparkRateMain = 14f;
        private const float SparkRateRetro = 10f;
        private const int SparkMaxParticles = 80;

        private const int SortOrderFlame = 3;
        private const int SortOrderSpark = 4;

        // палитра: главный — белое-в-желту ядро → оранж → красный → дымный хвост
        private static readonly Color MainCore = new Color(1f, 0.96f, 0.7f);
        private static readonly Color MainFire = new Color(1f, 0.62f, 0.2f);
        private static readonly Color MainEmber = new Color(0.95f, 0.32f, 0.1f);
        private static readonly Color MainSmoke = new Color(0.5f, 0.38f, 0.34f);
        // ретро — мятно-голубой «пых»
        private static readonly Color RetroCore = new Color(0.85f, 1f, 0.92f);
        private static readonly Color RetroGlow = new Color(0.5f, 0.9f, 1f);
        private static readonly Color RetroFade = new Color(0.3f, 0.62f, 0.85f);
        // искры: главные — белые-в-оранж, ретро — белые-в-мяту
        private static readonly Color SparkHot = new Color(1f, 1f, 0.88f);
        private static readonly Color SparkFade = new Color(1f, 0.45f, 0.12f);
        private static readonly Color SparkRetroHot = new Color(0.92f, 1f, 0.95f);
        private static readonly Color SparkRetroFade = new Color(0.45f, 0.9f, 1f);

        private Ship ship;
        private ShipController controller;
        private ShipModelVisual model;

        private ParticleSystem mainJet;
        private ParticleSystem retroJet;

        // искры — дети дефолтного пламени; у авторского слота их нет
        private ParticleSystem mainSparks;
        private ParticleSystem retroSparks;

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
            // Destroy отложен до конца кадра — умирающий слот ещё виден Find ниже,
            // пропускаем его, чтобы не принять свой же пламя за авторский PS
            var dying = new List<Transform>(createdSlots);
            foreach (Transform slot in createdSlots)
                if (slot != null)
                    Destroy(slot.gameObject);
            createdSlots.Clear();
            mainJet = null;
            retroJet = null;
            mainSparks = null;
            retroSparks = null;
            mainAuthored = false;
            retroAuthored = false;

            model = GetComponent<ShipModelVisual>();
            Transform root = model != null && model.HasVisual ? model.VisualRoot : transform;

            Vector2 nose = NoseDir();
            mainJet = ResolveSlot(root, MainSlotName, -nose * 6f, -nose, isMain: true, dying);
            retroJet = ResolveSlot(root, RetroSlotName, nose * 7f, nose, isMain: false, dying);
        }

        private void Update()
        {
            if (ship == null || controller == null)
                return;

            bool flying = ship.State == ShipState.Flying;
            float power = Mathf.Max(1f, controller.EnginePower);

            bool mainOn = flying && controller.ThrustOn;
            bool retroOn = flying && controller.BrakeOn;
            Drive(mainJet, mainOn, power, scaleFlame: !mainAuthored);
            Drive(mainSparks, mainOn, power, scaleFlame: false); // у искр только эмиссия ×power
            Drive(retroJet, retroOn, power, scaleFlame: !retroAuthored);
            Drive(retroSparks, retroOn, power, scaleFlame: false);

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

        private ParticleSystem ResolveSlot(Transform root, string slotName, Vector3 localPos,
            Vector2 exhaustDir, bool isMain, List<Transform> dying)
        {
            Transform slot = root.Find(slotName);
            if (slot != null && !dying.Contains(slot))
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
                Material mat = MakeParticleMaterial(slot);
                var flameGo = new GameObject("DefaultFlame");
                flameGo.transform.SetParent(slot, false);
                flameGo.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, exhaustDir);
                CreateDefaultFlame(flameGo, isMain, mat);
                CreateSparks(flameGo, isMain, mat);
                createdSlots.Add(flameGo.transform);
                return flameGo.GetComponent<ParticleSystem>();
            }

            // слота нет вовсе — строим слот и дефолтное пламя (корневой фолбэк)
            Material rootMat = MakeParticleMaterial(root);
            var go = new GameObject(slotName);
            go.transform.SetParent(root, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, exhaustDir);
            CreateDefaultFlame(go, isMain, rootMat);
            CreateSparks(go, isMain, rootMat);
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
                // дефолт: мощность делает пламя толще, длиннее, быстрее и ярче
                float gain = power - 1f;
                var main = ps.main;
                main.startSizeMultiplier = 1f + gain * SizePowerGain;
                main.startSpeedMultiplier = 1f + gain * SpeedPowerGain;
                main.startLifetimeMultiplier = 1f + gain * LifePowerGain;
                // яркость: RGB>1 в startColor (перемножается с градиентом
                // ColorOverLifetime), шейдер насытит ядро в белый
                float bright = 1f + gain * BrightPowerGain;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(bright, bright, bright, 1f));
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

        // материал частиц: шейдер SpriteRenderer корабля (или Sprites/Default) +
        // кружок-заглушка; Repeat нужен для тайлинга вдоль шлейфа (как в SpeedTrail)
        private static Material MakeParticleMaterial(Transform spriteHost)
        {
            var src = spriteHost.GetComponent<SpriteRenderer>();
            Shader shader = src != null && src.sharedMaterial != null
                ? src.sharedMaterial.shader
                : Shader.Find("Sprites/Default");
            Texture2D tex = StubSprites.Circle(8).texture;
            tex.wrapMode = TextureWrapMode.Repeat;
            return new Material(shader) { mainTexture = tex };
        }

        // дефолтное пламя: белое-в-желту ядро → оранж/красный → дымный хвост;
        // конус от сопла, у части частиц тонкий Trail-след
        private static void CreateDefaultFlame(GameObject go, bool isMain, Material mat)
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                isMain ? MainLifeMin : RetroLifeMin, isMain ? MainLifeMax : RetroLifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                isMain ? MainSpeedMin : RetroSpeedMin, isMain ? MainSpeedMax : RetroSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(
                isMain ? MainSizeMin : RetroSizeMin, isMain ? MainSizeMax : RetroSizeMax);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = isMain ? MainMaxParticles : RetroMaxParticles;

            var emission = ps.emission;
            emission.rateOverTime = isMain ? MainRate : RetroRate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = isMain ? MainConeAngle : RetroConeAngle;
            shape.radius = isMain ? MainConeRadius : RetroConeRadius;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(
                isMain ? MainFlameGradient() : RetroFlameGradient());

            // пламя: рождается узким у сопла, раздувается, хвост схлопывается
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f),
                new Keyframe(0.3f, 1f),
                new Keyframe(1f, 0.18f)));

            ApplyTrails(ps,
                isMain ? TrailRatioMain : TrailRatioRetro,
                isMain ? TrailLifeMain : TrailLifeRetro,
                isMain ? TrailWidthMain : TrailWidthRetro);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = mat;
            renderer.trailMaterial = mat; // следы тем же материалом — тайлится вдоль следа
            renderer.sortingOrder = SortOrderFlame;
        }

        // искры разгона: редкие быстрые горошки со следом — ощущение мощности;
        // ребёнок пламени, умирает вместе с ним при Rebind
        private void CreateSparks(GameObject parent, bool isMain, Material mat)
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(parent.transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(SparkLifeMin, SparkLifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(SparkSpeedMin, SparkSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(SparkSizeMin, SparkSizeMax);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = SparkMaxParticles;

            var emission = ps.emission;
            emission.rateOverTime = isMain ? SparkRateMain : SparkRateRetro;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = SparkConeAngle;
            shape.radius = 0.25f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(
                isMain ? SparkGradient() : RetroSparkGradient());

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.35f)));

            ApplyTrails(ps, TrailRatioSpark, TrailLifeSpark, TrailWidthSpark);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = mat;
            renderer.trailMaterial = mat;
            renderer.sortingOrder = SortOrderSpark;

            if (isMain)
                mainSparks = ps;
            else
                retroSparks = ps;
        }

        // Trail-модуль: тонкий затухающий след за каждой частицей — «линии тяги»
        private static void ApplyTrails(ParticleSystem ps, float ratio, float life, float width)
        {
            var trails = ps.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = ratio;
            trails.lifetime = new ParticleSystem.MinMaxCurve(life * 0.75f, life * 1.25f);
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(width, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.15f)));
            trails.sizeAffectsWidth = true;     // ширина следа от размера частицы
            trails.sizeAffectsLifetime = false;
            trails.dieWithParticles = true;     // след умирает с частицей — без «каши»
            trails.inheritParticleColor = true; // цвет наследует пламя/искру
            trails.colorOverTrail = new ParticleSystem.MinMaxGradient(TrailFadeGradient());
            trails.minVertexDistance = TrailMinVertexDistance;
            trails.worldSpace = false;
            trails.textureMode = ParticleSystemTrailTextureMode.Tile; // тайлинг как в SpeedTrail
        }

        // главный: белое-в-желту ядро → оранж → красный → лёгкий дымный хвост
        private static Gradient MainFlameGradient()
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

        // ретро: холодный мятно-голубой, без дымного хвоста
        private static Gradient RetroFlameGradient()
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

        private static Gradient SparkGradient()
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

        private static Gradient RetroSparkGradient()
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
        private static Gradient TrailFadeGradient()
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
