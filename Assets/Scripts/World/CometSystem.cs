using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.Core; // StubSprites
using Random = UnityEngine.Random; // System.Random конфликтует с using System

namespace SpaceTaxi.World
{
    // Кометы: до MaxRoutes маршрутов одновременно, цикл по таймеру — предупреждение
    // за WarningSeconds до входа (CometIncoming), спавн на случайной хорде через мир.
    // Столкновение = гибель, считает LandingSystem; система про UI и урон не знает.
    public class CometSystem : MonoBehaviour
    {
        // числа §3.8 — не менять
        public int MaxRoutes = 3;
        public float CometSpeed = 400f;
        public float SpawnIntervalMin = 90f;
        public float SpawnIntervalMax = 150f;
        public float WarningSeconds = 20f;
        public float CometRadius = 6f;

        private const float EntryMarginFromSpawn = 500f; // вход не рядом со стартом — новичка не убивает
        private const float ShipOffset = 7f;             // как в WorldBuilder (точка спавна над планетой)
        private const float MaxChordDeviationDeg = 65f;  // хорда не обязательно через центр мира

        // позиция входа на окружности мира + направление хорды — за WarningSeconds до появления
        public event Action<Vector2, Vector2> CometIncoming;
        public event Action CometSpawned;

        private LevelAsset level;
        private float nextSpawnAt;
        private bool warned;
        private Vector2 entryPoint;
        private Vector2 entryDirection;
        private Vector2 areaCenter;   // зона комет (П7.2): радиус 0 — хорды по всему миру
        private float areaRadius;
        private readonly List<Comet> active = new List<Comet>();

        public void Init(LevelAsset levelAsset)
        {
            level = levelAsset;
            if (levelAsset != null && levelAsset.CometAreaRadius > 0f)
            {
                areaCenter = levelAsset.CometAreaCenter;
                areaRadius = levelAsset.CometAreaRadius;
            }
            nextSpawnAt = Time.time + Random.Range(SpawnIntervalMin, SpawnIntervalMax);
        }

        private void Update()
        {
            if (level == null)
                return;
            PruneDead();

            // предупреждение — за WarningSeconds до спавна; хорда фиксируется заранее,
            // чтобы тост и маркер показывали реальную траекторию
            if (!warned && Time.time >= nextSpawnAt - WarningSeconds && active.Count < MaxRoutes)
            {
                warned = true;
                entryPoint = PickEntry();
                entryDirection = PickDirection(entryPoint);
                CometIncoming?.Invoke(entryPoint, entryDirection);
            }

            // мировое время (Time.time): пауза (timeScale = 0) таймер не двигает — как в OrderService;
            // без предупреждения (маршруты были заняты) окно пропускается, а не спавнит молча
            if (Time.time >= nextSpawnAt)
            {
                if (warned && active.Count < MaxRoutes)
                {
                    SpawnComet();
                    CometSpawned?.Invoke();
                }
                warned = false;
                nextSpawnAt = Time.time + Random.Range(SpawnIntervalMin, SpawnIntervalMax);
            }
        }

        private void SpawnComet()
        {
            var go = new GameObject("Comet");
            go.transform.SetParent(transform, false);
            go.transform.position = entryPoint;

            var collider = go.AddComponent<CircleCollider2D>();
            collider.radius = CometRadius; // физический, не триггер

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = StubSprites.Circle(CometRadius * 2f);
            renderer.color = Comet.CometColor;
            renderer.sortingOrder = 1;

            AddTrail(go, renderer);

            var comet = go.AddComponent<Comet>();
            if (areaRadius > 0f)
                comet.Init(entryDirection, CometSpeed, areaCenter, areaRadius);
            else
                comet.Init(entryDirection, CometSpeed, level.WorldRadius);
            active.Add(comet);
        }

        // след по образцу SpeedTrail: тот же материал-тайлинг, длиннее и ярче — виден издалека
        private static Material trailMaterial; // общий след всех комет (не на каждый спавн)

        private void AddTrail(GameObject go, SpriteRenderer renderer)
        {
            var trail = go.AddComponent<TrailRenderer>();
            if (trailMaterial == null)
            {
                Shader shader = renderer.sharedMaterial != null
                    ? renderer.sharedMaterial.shader
                    : Shader.Find("Sprites/Default");
                Texture2D tex = StubSprites.Circle(8).texture;
                tex.wrapMode = TextureWrapMode.Repeat; // тайлится вдоль шлейфа
                trailMaterial = new Material(shader) { mainTexture = tex };
            }
            trail.material = trailMaterial;

            trail.time = 1.1f; // при скорости 400 — хвост в несколько сотен юнитов
            trail.minVertexDistance = 2f;
            trail.numCapVertices = 2;
            trail.textureMode = LineTextureMode.Tile;
            trail.sortingOrder = 1;
            trail.startColor = Comet.CometColor;
            trail.endColor = new Color(Comet.CometColor.r, Comet.CometColor.g, Comet.CometColor.b, 0f);
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0f));
            trail.widthMultiplier = 2.5f;
        }

        private void PruneDead()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i] == null) // Destroy у кометы обнуляет ссылку
                    active.RemoveAt(i);
            }
        }

        // точка входа: в зоне — на окружности зоны, иначе — на окружности мира;
        // не ближе EntryMarginFromSpawn к точке спавна корабля
        private Vector2 PickEntry()
        {
            Vector2 guard = SpawnGuardPoint();
            float radius = areaRadius > 0f ? areaRadius : level.WorldRadius;
            Vector2 center = areaRadius > 0f ? areaCenter : Vector2.zero;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (Vector2.Distance(point, guard) >= EntryMarginFromSpawn)
                    return point;
            }
            // запасной ход: самая далёкая от старта точка окружности
            return center + (center - guard).normalized * radius;
        }

        // точка респавна корабля — как считает WorldBuilder (Landed) или явный SpawnPoint (Free)
        private Vector2 SpawnGuardPoint()
        {
            if (level.SpawnMode == SpawnMode.Free)
                return level.SpawnPoint;
            CelestialBodyData start = level.Bodies[level.StartPlanetIndex];
            float rad = level.SpawnAngleDeg * Mathf.Deg2Rad;
            return start.Position + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * (start.Radius + ShipOffset);
        }

        // в зоне — случайная хорда окружности зоны (маршрут не покидает её),
        // иначе — внутрь мира под случайным углом к нормали входа
        private Vector2 PickDirection(Vector2 entry)
        {
            if (areaRadius > 0f)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                Vector2 target = areaCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * areaRadius;
                Vector2 chord = target - entry;
                if (chord.sqrMagnitude > 0.0001f)
                    return chord.normalized;
                return (areaCenter - entry).normalized; // вырожденная хорда — через центр зоны
            }

            Vector2 inward = -entry.normalized;
            float deviation = Random.Range(-MaxChordDeviationDeg, MaxChordDeviationDeg) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(deviation);
            float sin = Mathf.Sin(deviation);
            return new Vector2(inward.x * cos - inward.y * sin, inward.x * sin + inward.y * cos);
        }
    }
}
