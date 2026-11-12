using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.World
{
    // Обломки-призраки (Н7 G7.1): после гибели корабля в захвате чёрной дыры
    // его спрайт остаётся кружить вокруг дыры и медленно сжиматься — прилетев
    // новым кораблём, игрок видит останки прошлой гибели. Кинематика без
    // физики и коллайдеров (призрак): орбита с растущей угловой скоростью,
    // спираль к горизонту, исчезновение на минимальном размере или при уходе
    // под горизонт. Живут только в текущей сессии (сейв-персист — отдельное
    // решение владельца), максимум два: старейший гаснет при спавне нового.
    // Спавнит BlackHole в момент гибели (HullSystem.Died).
    public class WreckSystem : MonoBehaviour
    {
        private const int MaxWrecks = 2;
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
                minScale = scale * MinScaleFraction,
                shrinkPerSecond = scale * (1f - MinScaleFraction) / ShrinkSeconds,
                spinDegPerSec = spinDegPerSec,
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
