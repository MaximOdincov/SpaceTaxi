using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.World
{
    // Обломки-призраки (Н7 G7.1 + v4 Н8.27): спрайт брошенного/погибшего корабля
    // остаётся кружить вокруг дыры — прилетев новым кораблём, игрок видит останки.
    // Два режима: СПИРАЛЬ (гибель в захвате — обломок стягивается к горизонту,
    // Н7) и ОРБИТА (отключение корабля v4 — брошенный борт держит свою орбиту
    // до конца окна поглощения, медленно сжимаясь, потом гаснет; решение
    // владельца «корабль остаётся на орбите, мы видим его с другого корабля»).
    // Кинематика без физики и коллайдеров (призрак). Живут только в текущей
    // сессии (сейв-персист — отдельное решение владельца), максимум три:
    // старейший гаснет при спавне нового. Спавнит BlackHole.
    public class WreckSystem : MonoBehaviour
    {
        private const int MaxWrecks = 3;
        // доля стартового масштаба, на которой обломок исчезает
        private const float MinScaleFraction = 0.12f;
        private const float ShrinkSeconds = 34f;          // медленнее сжатия живого корабля
        private const float RadiusDecayPerSecond = 4.5f;  // спираль к горизонту, юнитов/с
        private const float SpinAccelRadPerSec2 = 0.8f;   // рост угловой скорости, рад/с²
        private const float SpinMaxRadPerSec = 8f;        // потолок угловой скорости
        private const float FadeSeconds = 0.9f;           // затухание перед удалением

        private class Wreck
        {
            internal Transform transform;
            internal SpriteRenderer renderer;
            internal BlackHole hole;
            internal Vector2 center;
            internal float radius;
            internal float angle;        // радианы
            internal float angularSpeed; // рад/с, знак — направление орбиты
            internal float scale;
            internal float shrinkPerSecond;
            internal float minScale;
            internal float spinDegPerSec; // качание вокруг своей оси
            internal float fadeLeft;
            internal bool fading;
            internal bool orbital;        // v4: орбита дыры (отключение); v5.1 — со спиралью внутрь
            internal float lifetimeLeft;  // orbital: остаток окна поглощения
            internal float radiusDecayPerSecond; // v5.1: скорость угасания орбиты, юнитов/с
        }

        private readonly List<Wreck> wrecks = new List<Wreck>();

        // ленивое создание: спавнит BlackHole при первой гибели в захвате
        public static WreckSystem Ensure()
        {
            WreckSystem system = FindAnyObjectByType<WreckSystem>();
            if (system != null)
                return system;
            GameObject go = new GameObject("WreckSystem");
            return go.AddComponent<WreckSystem>();
        }

        public void Spawn(BlackHole hole, Vector2 position, Sprite sprite, float scale,
            float angularSpeedRadPerSec, float spinDegPerSec)
        {
            SpawnInternal(hole, position, sprite, scale, angularSpeedRadPerSec, spinDegPerSec,
                orbital: false, lifetimeSeconds: 0f);
        }

        // v5.1 (2026-11-15, владелец: «после отключения появляется его КЛОН»):
        // брошенный корабль — НЕ статичная копия, а погибающий борт: орбита
        // УГАСАЕТ к центру дыры за время окна (спираль, как у живого корабля
        // в воронке), сжатие продолжается, в конце — поглощение (гаснет)
        public void SpawnOrbital(BlackHole hole, Vector2 position, Sprite sprite, float scale,
            float angularSpeedRadPerSec, float spinDegPerSec, float lifetimeSeconds)
        {
            SpawnInternal(hole, position, sprite, scale, angularSpeedRadPerSec, spinDegPerSec,
                orbital: true, lifetimeSeconds: Mathf.Max(4f, lifetimeSeconds));
        }

        private void SpawnInternal(BlackHole hole, Vector2 position, Sprite sprite, float scale,
            float angularSpeedRadPerSec, float spinDegPerSec, bool orbital, float lifetimeSeconds)
        {
            if (hole == null || sprite == null || scale <= 0f)
                return;

            // лимит: старейший живой обломок гаснет при спавне нового
            int alive = 0;
            foreach (Wreck existing in wrecks)
                if (!existing.fading)
                    alive++;
            if (alive >= MaxWrecks)
                foreach (Wreck oldest in wrecks)
                {
                    if (!oldest.fading)
                    {
                        StartFade(oldest);
                        break;
                    }
                }

            GameObject go = new GameObject("ShipWreck");
            go.transform.SetParent(transform, false);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 3; // над ядром дыры и линией прогноза

            Vector2 center = hole.transform.position;
            Vector2 offset = position - center;
            var wreck = new Wreck
            {
                transform = go.transform,
                renderer = renderer,
                hole = hole,
                center = center,
                radius = Mathf.Max(offset.magnitude, hole.Horizon + 1f),
                angle = Mathf.Atan2(offset.y, offset.x),
                angularSpeed = Mathf.Clamp(angularSpeedRadPerSec, -SpinMaxRadPerSec, SpinMaxRadPerSec),
                scale = scale,
                radiusDecayPerSecond = orbital
                    ? Mathf.Max(offset.magnitude, hole.Horizon) / Mathf.Max(lifetimeSeconds, 1f)
                    : RadiusDecayPerSecond,
                minScale = scale * MinScaleFraction,
                shrinkPerSecond = orbital
                    ? scale * (1f - MinScaleFraction) / Mathf.Max(lifetimeSeconds, 1f)
                    : scale * (1f - MinScaleFraction) / ShrinkSeconds,
                spinDegPerSec = spinDegPerSec,
                orbital = orbital,
                lifetimeLeft = lifetimeSeconds,
            };
            go.transform.position = position;
            go.transform.localScale = Vector3.one * scale;
            wrecks.Add(wreck);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = wrecks.Count - 1; i >= 0; i--)
            {
                Wreck wreck = wrecks[i];
                if (wreck.transform == null)
                {
                    wrecks.RemoveAt(i); // сцена выгрузилась
                    continue;
                }

                if (!wreck.fading)
                {
                    if (wreck.hole == null)
                    {
                        StartFade(wreck); // дыра исчезла — обломок дотухает
                    }
                    else
                    {
                        wreck.center = wreck.hole.transform.position;
                        if (wreck.orbital)
                        {
                            // v5.1: орбита угасает — спираль внутрь дыры за время
                            // окна (радиус = начальный × остаток жизни), не статичный
                            // «клон» на месте; в конце — поглощение (гаснет)
                            wreck.angle += wreck.angularSpeed * dt;
                            wreck.lifetimeLeft -= dt;
                            wreck.radius = Mathf.MoveTowards(wreck.radius, 0f,
                                wreck.radiusDecayPerSecond * dt);
                            wreck.scale = Mathf.MoveTowards(
                                wreck.scale, wreck.minScale, wreck.shrinkPerSecond * dt);
                            wreck.transform.position = wreck.center
                                + new Vector2(Mathf.Cos(wreck.angle), Mathf.Sin(wreck.angle)) * wreck.radius;
                            wreck.transform.localScale = Vector3.one * wreck.scale;
                            wreck.transform.Rotate(0f, 0f, wreck.spinDegPerSec * dt);
                            if (wreck.lifetimeLeft <= 0f)
                                StartFade(wreck);
                        }
                        else
                        {
                            float direction = wreck.angularSpeed >= 0f ? 1f : -1f;
                            wreck.angularSpeed = direction * Mathf.MoveTowards(
                                Mathf.Abs(wreck.angularSpeed), SpinMaxRadPerSec, SpinAccelRadPerSec2 * dt);
                            wreck.angle += wreck.angularSpeed * dt;
                            wreck.radius = Mathf.MoveTowards(wreck.radius, 0f, RadiusDecayPerSecond * dt);
                            wreck.scale = Mathf.MoveTowards(wreck.scale, wreck.minScale, wreck.shrinkPerSecond * dt);
                            wreck.transform.position = wreck.center
                                + new Vector2(Mathf.Cos(wreck.angle), Mathf.Sin(wreck.angle)) * wreck.radius;
                            wreck.transform.localScale = Vector3.one * wreck.scale;
                            wreck.transform.Rotate(0f, 0f, wreck.spinDegPerSec * dt);

                            // полный уход под горизонт или минимальный размер — гаснем
                            if (wreck.radius <= wreck.hole.Horizon || wreck.scale <= wreck.minScale + 0.001f)
                                StartFade(wreck);
                        }
                    }
                }
                else
                {
                    wreck.fadeLeft -= dt;
                    Color color = wreck.renderer.color;
                    color.a = Mathf.Clamp01(wreck.fadeLeft / FadeSeconds);
                    wreck.renderer.color = color;
                    if (wreck.fadeLeft <= 0f)
                    {
                        Destroy(wreck.transform.gameObject);
                        wrecks.RemoveAt(i);
                    }
                }
            }
        }

        private static void StartFade(Wreck wreck)
        {
            if (wreck.fading)
                return;
            wreck.fading = true;
            wreck.fadeLeft = FadeSeconds;
        }
    }
}
