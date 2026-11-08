using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpaceTaxi.Core;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.EditorTools
{
    // Диагностика смещения корпуса: мировые габариты визуала vs корень корабля,
    // затем тест посадки (зазор до поверхности). Переживает domain reload при
    // входе в Play Mode: подписка в [InitializeOnLoad]-статик-конструкторе,
    // состояние в SessionState. Меню «Space Taxi/Ship Probe», CLI:
    // -executeMethod SpaceTaxi.EditorTools.ShipProbe.Run
    [InitializeOnLoad]
    public static class ShipProbe
    {
        private const string Flag = "shipprobe.on";
        private const string Deadline = "shipprobe.deadline";
        private const string StepKey = "shipprobe.step";

        static ShipProbe()
        {
            EditorApplication.update += Tick;
        }

        [MenuItem("Space Taxi/Ship Probe")]
        public static void Run()
        {
            SessionState.SetInt(Flag, 1);
            SessionState.SetInt(StepKey, 0);
            SessionState.SetFloat(Deadline, (float)EditorApplication.timeSinceStartup + 30f);
            EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
            EditorApplication.isPlaying = true;
        }

        private static void Tick()
        {
            if (SessionState.GetInt(Flag, 0) == 0)
                return;
            int step = SessionState.GetInt(StepKey, 0);

            // таймаут только на шаге посадки: у старта дедлайн перетирается
            // шагом 0 (он же выставляет окно на подлёт)
            if (step == 1 && EditorApplication.timeSinceStartup > SessionState.GetFloat(Deadline, 0f))
            {
                Debug.LogError("[ShipProbe] таймаут посадки");
                Finish();
                return;
            }
            var session = Object.FindAnyObjectByType<GameSession>();
            if (session == null || session.Ship == null || session.Level == null)
                return;

            if (step == 0)
            {
                SessionState.SetInt(StepKey, 1);
                SessionState.SetFloat(Deadline, (float)EditorApplication.timeSinceStartup + 3f);
                Sample(session.Ship, "СТАРТ");

                // ближайшая планета — тест посадки над ней
                Planet target = null;
                float best = float.MaxValue;
                foreach (CelestialBody body in Object.FindAnyObjectByType<GravityField>().Bodies)
                {
                    if (!(body is Planet p))
                        continue;
                    float d = Vector2.Distance(session.Ship.transform.position, p.transform.position);
                    if (d < best) { best = d; target = p; }
                }
                Vector2 dir = ((Vector2)session.Ship.transform.position - (Vector2)target.transform.position).normalized;
                ShipProbeState.Target = target;
                session.Ship.State = ShipState.Flying; // севший корабль телепортом не сядет
                session.Ship.LandedOn = null;
                session.Ship.transform.position = (Vector2)target.transform.position + dir * (target.Radius + 40f);
                session.Ship.Body.linearVelocity = -dir * 20f; // медленный заход на посадку
            }
            else if (step == 1)
            {
                Planet target = ShipProbeState.Target;
                float gap = Vector2.Distance(session.Ship.transform.position, target.transform.position) - target.Radius;
                Debug.Log($"[ShipProbe] ПОСАДКА: планета «{target.DisplayName}» R={target.Radius:F1} " +
                          $"зазор центр-поверхность={gap:F2}");
                Sample(session.Ship, "ПОСАДКА");
                Finish();
            }
        }

        private static void Finish()
        {
            SessionState.SetInt(Flag, 0);
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(0);
        }

        private static void Sample(Ship ship, string tag)
        {
            Debug.Log($"[ShipProbe] {tag}: root pos={ship.transform.position} rootScale={ship.transform.localScale} state={ship.State}");
            foreach (var sr in ship.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.sprite == null)
                    continue;
                Bounds b = sr.bounds;
                Debug.Log($"[ShipProbe]   SR '{sr.transform.name}' sprite={sr.sprite.name} enabled={sr.enabled} " +
                          $"localPos={sr.transform.localPosition} localScale={sr.transform.localScale} " +
                          $"boundsCenter={b.center} size={b.size} " +
                          $"centerOffsetFromRoot={(Vector2)(b.center - ship.transform.position)}");
            }
        }

        // ссылки между тиками (оба шага уже после reload — статика живёт)
        private static class ShipProbeState
        {
            public static Planet Target;
            public static Vector2 Dir;
        }
    }
}
