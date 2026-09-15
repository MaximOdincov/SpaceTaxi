using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Tools
{
    // Гизмо уровня в Scene View (ТЗ: 09_LEVEL_DESIGN.md §1.3, §1.4): кольца,
    // жар, спавн, дистанции, рамка камеры, сетка; drag тел со snap и Undo.
    [InitializeOnLoad]
    public static class LevelEditorSceneView
    {
        // Вид игрока: высота из CameraFollow, 16:9.
        private const float ViewHeight = 320f;
        private const float ViewWidth = ViewHeight * 16f / 9f;

        private static readonly Color PlanetFill = new Color(0.6f, 0.8f, 1f, 0.08f);
        private static readonly Color StarFill = new Color(1f, 0.8f, 0.3f, 0.1f);
        private static readonly Color LandingRing = new Color(0.3f, 1f, 0.4f, 0.8f);
        private static readonly Color CaptureRing = new Color(0.4f, 0.8f, 1f, 0.7f);
        private static readonly Color HeatColor = new Color(1f, 0.5f, 0.2f, 0.8f);
        private static readonly Color SpawnColor = new Color(1f, 0.9f, 0.3f, 0.9f);
        private static readonly Color CameraFrame = new Color(0.6f, 0.9f, 1f, 0.5f);
        private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color DangerColor = new Color(1f, 0.4f, 0.3f, 0.9f);
        private static readonly Color DistanceColor = new Color(1f, 1f, 1f, 0.35f);

        private static bool dragging;
        private static Vector2 dragOffset;

        static LevelEditorSceneView()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private static void OnSceneGui(SceneView view)
        {
            LevelEditorWindow window = LevelEditorWindow.OpenWindow;
            if (window == null || window.Level == null)
                return;
            LevelAsset level = window.Level;

            if (window.ShowGrid)
                DrawGrid(level);

            for (int i = 0; i < level.Bodies.Count; i++)
                DrawBody(level.Bodies[i], i == window.SelectedIndex);

            DrawSelectionDistances(level, window.SelectedIndex);
            DrawSpawn(level);
            DrawCameraFrame(level);

            HandleDrag(window, level);
        }

        private static void DrawBody(CelestialBodyData body, bool selected)
        {
            Vector3 center = body.Position;
            bool star = body.Type == BodyType.Star;

            // Заливка + контур + имя.
            Handles.color = star ? StarFill : PlanetFill;
            Handles.DrawSolidDisc(center, Vector3.forward, body.Radius);
            Handles.color = selected ? Color.white : (star ? new Color(1f, 0.8f, 0.3f) : new Color(0.6f, 0.8f, 1f));
            Handles.DrawWireDisc(center, Vector3.forward, body.Radius);
            Handles.Label(center + Vector3.up * (body.Radius + 12f), body.DisplayName);

            // Посадочное кольцо r+90 — зелёный пунктир (только планеты).
            if (!star)
                DottedCircle(center, body.Radius + 90f, LandingRing, 4f);

            // Кольцо качели r*3.3+30 — голубой точечный, мельче.
            DottedCircle(center, body.Radius * 3.3f + 30f, CaptureRing, 2f);

            // Жаровая зона — оранжевый пунктир + слабая заливка.
            if (star && body.HeatRadius > 0f)
            {
                Handles.color = new Color(HeatColor.r, HeatColor.g, HeatColor.b, 0.05f);
                Handles.DrawSolidDisc(center, Vector3.forward, body.HeatRadius);
                DottedCircle(center, body.HeatRadius, HeatColor, 4f);
            }
        }

        // Линии от выбранного тела до остальных с дистанциями; < 300 — тревожно.
        private static void DrawSelectionDistances(LevelAsset level, int selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex >= level.Bodies.Count)
                return;
            var from = level.Bodies[selectedIndex];

            foreach (var other in level.Bodies)
            {
                if (other == from)
                    continue;
                float d = Vector2.Distance(from.Position, other.Position);
                bool close = d < 300f;
                Handles.color = close ? DangerColor : DistanceColor;
                Handles.DrawLine(from.Position, other.Position);
                Handles.Label(
                    Vector3.Lerp(from.Position, other.Position, 0.5f),
                    d.ToString("0"), new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = close ? DangerColor : Color.gray } });
            }
        }

        // Спавн: маркер-треугольник + стрелка курса от поверхности.
        private static void DrawSpawn(LevelAsset level)
        {
            if (level.StartPlanetIndex < 0 || level.StartPlanetIndex >= level.Bodies.Count)
                return;
            var start = level.Bodies[level.StartPlanetIndex];
            float rad = level.SpawnAngleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Vector2 pos = start.Position + dir * (start.Radius + 7f);

            Handles.color = SpawnColor;
            Vector3 p = pos;
            Vector3 side = new Vector3(-dir.y, dir.x) * 8f;
            Handles.DrawPolyLine(p + (Vector3)dir * 14f, p - side, p + side, p + (Vector3)dir * 14f);

            Vector3 a = p + (Vector3)dir * 60f;
            Handles.DrawLine(p, a);
            Handles.DrawLine(a, a - (Vector3)dir * 12f - side * 0.7f);
            Handles.DrawLine(a, a - (Vector3)dir * 12f + side * 0.7f);
            Handles.Label(a + (Vector3)dir * 10f, "спавн " + level.SpawnAngleDeg.ToString("0") + "°");
        }

        // Рамка «что видит игрок» вокруг спавна.
        private static void DrawCameraFrame(LevelAsset level)
        {
            Vector3 c = LevelEditorWindow.SpawnPointOf(level);
            float hw = ViewWidth * 0.5f;
            float hh = ViewHeight * 0.5f;
            Handles.color = CameraFrame;
            Handles.DrawPolyLine(
                new Vector3(c.x - hw, c.y - hh), new Vector3(c.x + hw, c.y - hh),
                new Vector3(c.x + hw, c.y + hh), new Vector3(c.x - hw, c.y + hh),
                new Vector3(c.x - hw, c.y - hh));
        }

        private static void DrawGrid(LevelAsset level)
        {
            if (level.Bodies.Count == 0)
                return;
            float minX = level.Bodies.Min(b => b.Position.x) - 400f;
            float maxX = level.Bodies.Max(b => b.Position.x) + 400f;
            float minY = level.Bodies.Min(b => b.Position.y) - 400f;
            float maxY = level.Bodies.Max(b => b.Position.y) + 400f;

            Handles.color = GridColor;
            for (float x = Mathf.Floor(minX / 100f) * 100f; x <= maxX; x += 100f)
                Handles.DrawLine(new Vector3(x, minY), new Vector3(x, maxY));
            for (float y = Mathf.Floor(minY / 100f) * 100f; y <= maxY; y += 100f)
                Handles.DrawLine(new Vector3(minX, y), new Vector3(maxX, y));
        }

        // Клик — выбрать тело, drag — переместить (snap 10, Shift — свободно).
        private static void HandleDrag(LevelEditorWindow window, LevelAsset level)
        {
            Event ev = Event.current;
            if (ev == null)
                return;

            if (ev.type == EventType.MouseDown && ev.button == 0 && !ev.alt)
            {
                Vector2 world = MouseWorld(ev.mousePosition);
                int hit = FindBodyAt(level, world);
                if (hit >= 0)
                {
                    window.SetSelected(hit);
                    dragging = true;
                    dragOffset = level.Bodies[hit].Position - world;
                    Undo.RecordObject(level, "Move Body");
                    ev.Use();
                }
            }
            else if (ev.type == EventType.MouseDrag && dragging)
            {
                int index = window.SelectedIndex;
                if (index >= 0 && index < level.Bodies.Count)
                {
                    Vector2 pos = MouseWorld(ev.mousePosition) + dragOffset;
                    if (window.SnapEnabled && !ev.shift)
                        pos = new Vector2(Mathf.Round(pos.x / 10f) * 10f, Mathf.Round(pos.y / 10f) * 10f);
                    level.Bodies[index].Position = pos;
                    EditorUtility.SetDirty(level);
                    ev.Use();
                }
            }
            else if (ev.type == EventType.MouseUp && dragging)
            {
                dragging = false;
                ev.Use();
            }
        }

        private static int FindBodyAt(LevelAsset level, Vector2 world)
        {
            for (int i = 0; i < level.Bodies.Count; i++)
                if (Vector2.Distance(level.Bodies[i].Position, world) < level.Bodies[i].Radius)
                    return i;
            return -1;
        }

        private static Vector2 MouseWorld(Vector2 mouse)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
            return new Vector2(ray.origin.x, ray.origin.y);
        }

        private static void DottedCircle(Vector3 center, float radius, Color color, float dotSize)
        {
            Handles.color = color;
            const int segments = 64;
            Vector3 prev = center + new Vector3(radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
                Handles.DrawDottedLine(prev, next, dotSize);
                prev = next;
            }
        }
    }
}
