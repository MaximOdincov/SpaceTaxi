using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpaceTaxi.Core;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.EditorTools
{
    // Одноразовая диагностика смещения корпуса: мировые габариты визуала vs
    // корень корабля, затем тест посадки (зазор до поверхности). Меню
    // «Space Taxi/Ship Probe», CLI: -executeMethod SpaceTaxi.EditorTools.ShipProbe.Run
    public static class ShipProbe
    {
        private static int step;
        private static double waitUntil;
        private static Ship ship;
        private static Planet target;
        private static Vector2 approachDir;

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
            EditorApplication.isPlaying = true;
            EditorApplication.update += Tick;
            step = 0;
        }

        private static void Tick()
        {
            if (step == 0)
            {
                var session = Object.FindAnyObjectByType<GameSession>();
                if (session == null || session.Ship == null || session.Level == null)
                    return;
                ship = session.Ship;
                waitUntil = EditorApplication.timeSinceStartup + 0.5;
                step = 1;
            }
            else if (step == 1)
            {
                if (EditorApplication.timeSinceStartup < waitUntil)
                    return;
                Sample("СТАРТ");
                // ближайшая планета — тест посадки над ней
                float best = float.MaxValue;
                foreach (CelestialBody body in Object.FindAnyObjectByType<GravityField>().Bodies)
                {
                    if (!(body is Planet p))
                        continue;
                    float d = Vector2.Distance(ship.transform.position, p.transform.position);
                    if (d < best) { best = d; target = p; }
                }
                approachDir = ((Vector2)ship.transform.position - (Vector2)target.transform.position).normalized;
                ship.transform.position = (Vector2)target.transform.position
                    + approachDir * (target.Radius + 40f);
                ship.Body.linearVelocity = -approachDir * 20f; // медленный заход
                waitUntil = EditorApplication.timeSinceStartup + 1.0;
                step = 2;
            }
            else if (step == 2)
            {
                if (EditorApplication.timeSinceStartup < waitUntil)
                    return;
                float gap = Vector2.Distance(ship.transform.position, target.transform.position) - target.Radius;
                Debug.Log($"[ShipProbe] ПОСАДКА: планета «{target.DisplayName}» R={target.Radius:F1} " +
                          $"зазор центр-поверхность={gap:F2} (норма ~HalfExtent)");
                Sample("ПОСАДКА");
                EditorApplication.isPlaying = false;
                EditorApplication.update -= Tick;
                EditorApplication.Exit(0);
            }
        }

        private static void Sample(string tag)
        {
            Debug.Log($"[ShipProbe] {tag}: root pos={ship.transform.position} scale={ship.transform.localScale} " +
                      $"state={ship.State}");
            foreach (var sr in ship.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.sprite == null)
                    continue;
                Bounds b = sr.bounds;
                Debug.Log($"[ShipProbe]   SR '{GetPath(sr.transform)}' sprite={sr.sprite.name} " +
                          $"enabled={sr.enabled} localPos={sr.transform.localPosition} " +
                          $"localScale={sr.transform.localScale} boundsCenter={b.center} " +
                          $"size={b.size} centerOffsetFromRoot={(Vector2)(b.center - ship.transform.position)}");
            }
        }

        private static string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null && t.parent != ship.transform)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
